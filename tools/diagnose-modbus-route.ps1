param(
    [Parameter(Mandatory = $true)]
    [string]$Ip,
    [int]$Port = 502,
    [int]$TimeoutMs = 1500
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:Findings = New-Object System.Collections.Generic.List[string]
$script:Risk = $false
$script:Severity = 'INFO'

function Add-Finding {
    param([string]$Level, [string]$Message)

    $script:Findings.Add("[$Level] $Message") | Out-Null
    if ($Level -eq 'RISK') {
        $script:Risk = $true
        if ($script:Severity -ne 'BLOCK') { $script:Severity = 'RISK' }
    }
    elseif ($Level -eq 'BLOCK') {
        $script:Risk = $true
        $script:Severity = 'BLOCK'
    }
}

function Test-IsLoopbackAdapter {
    param([string]$Alias)

    if ([string]::IsNullOrWhiteSpace($Alias)) { return $false }
    return ($Alias -match 'Loopback')
}

function Test-IsVirtualAdapter {
    param([string]$Alias)

    if ([string]::IsNullOrWhiteSpace($Alias)) { return $false }
    if (Test-IsLoopbackAdapter -Alias $Alias) { return $false }
    return ($Alias -match 'TAP|VPN|Nord|Lets|Hyper-V|vEthernet|WireGuard|OpenVPN|Tailscale|ZeroTier')
}

function Get-AdapterClass {
    param([string]$Alias)

    if ([string]::IsNullOrWhiteSpace($Alias)) { return 'unknown' }
    if (Test-IsLoopbackAdapter -Alias $Alias) { return 'loopback' }
    if (Test-IsVirtualAdapter -Alias $Alias) { return 'virtual' }
    if ($Alias -match 'Ethernet|以太网|Eth') { return 'ethernet' }
    if ($Alias -match 'Wi-?Fi|WLAN|无线') { return 'wifi' }
    return 'other'
}

Write-Host '================================================================'
Write-Host " Modbus Route Diagnostic"
Write-Host "   Target : $Ip`:$Port"
Write-Host "   Time   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host '================================================================'
Write-Host ''

# ---------------------------------------------------------------- Step 1: target classification
Write-Host '[1/6] Target classification'
$parsedTarget = $null
if (-not [System.Net.IPAddress]::TryParse($Ip, [ref]$parsedTarget)) {
    Write-Host "   FAIL: '$Ip' is not a valid IPv4 / IPv6 address."
    exit 2
}

$isPrivate = $false
if ($parsedTarget.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
    $bytes = $parsedTarget.GetAddressBytes()
    if ($bytes[0] -eq 10) { $isPrivate = $true }
    elseif ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) { $isPrivate = $true }
    elseif ($bytes[0] -eq 192 -and $bytes[1] -eq 168) { $isPrivate = $true }
    elseif ($bytes[0] -eq 127) { Write-Host '   loopback target (127.0.0.0/8)' }
}

$classText = if ($parsedTarget.ToString().StartsWith('127.')) {
    'loopback'
} elseif ($isPrivate) {
    'private LAN (RFC1918)'
} else {
    'public / non-private'
}
Write-Host "   address class: $classText"
Write-Host ''

# ---------------------------------------------------------------- Step 2: physical adapters
Write-Host '[2/6] Active IPv4 adapters'
$activeAddresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.InterfaceAlias }

if (-not $activeAddresses) {
    Write-Host '   (none)'
    Add-Finding -Level 'BLOCK' -Message 'No active IPv4 adapters found. Plug in Ethernet or enable Wi-Fi before retrying.'
}
else {
    $adapterRows = foreach ($addr in $activeAddresses) {
        [pscustomobject]@{
            Alias        = $addr.InterfaceAlias
            Address      = $addr.IPAddress
            Prefix       = $addr.PrefixLength
            Class        = Get-AdapterClass -Alias $addr.InterfaceAlias
        }
    }
    $adapterRows | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ }
}

$physicalLanAdapters = @($activeAddresses | Where-Object { (Get-AdapterClass -Alias $_.InterfaceAlias) -in 'ethernet','wifi' })
$virtualAdapters = @($activeAddresses | Where-Object { Test-IsVirtualAdapter -Alias $_.InterfaceAlias })
$loopbackAdapters = @($activeAddresses | Where-Object { Test-IsLoopbackAdapter -Alias $_.InterfaceAlias })

# ---------------------------------------------------------------- Step 3: route resolution (Find-NetRoute)
Write-Host '[3/6] Route resolution for target'
$selectedSourceAddress = $null
$selectedInterfaceAlias = $null
$selectedInterfaceIndex = $null
try {
    $routeInfo = Find-NetRoute -RemoteIPAddress $Ip -ErrorAction Stop | Select-Object -First 1
    if ($routeInfo) {
        $selectedSourceAddress = $routeInfo.IPAddress
        $selectedInterfaceIndex = $routeInfo.InterfaceIndex
        $iface = Get-NetIPInterface -InterfaceIndex $selectedInterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($iface) { $selectedInterfaceAlias = $iface.InterfaceAlias }
    }
}
catch {
    Add-Finding -Level 'RISK' -Message "Find-NetRoute failed for $Ip ($($_.Exception.Message)). Try as Administrator if the route table is needed."
}

if ($selectedSourceAddress) {
    Write-Host "   selected source: $selectedSourceAddress"
    Write-Host "   selected adapter: $selectedInterfaceAlias (ifIndex=$selectedInterfaceIndex)"
    Write-Host "   adapter class:   $(Get-AdapterClass -Alias $selectedInterfaceAlias)"
}
else {
    Write-Host '   (no route resolved)'
}
Write-Host ''

# ---------------------------------------------------------------- Step 4: TCP probe
Write-Host '[4/6] TCP probe'
$tcp = Test-NetConnection -ComputerName $Ip -Port $Port -WarningAction SilentlyContinue
$tcpSourceAddress = if ($tcp.SourceAddress -and $tcp.SourceAddress.IPAddress) { $tcp.SourceAddress.IPAddress } else { $tcp.SourceAddress }
Write-Host "   TcpTestSucceeded : $($tcp.TcpTestSucceeded)"
Write-Host "   RemoteAddress    : $($tcp.RemoteAddress)"
Write-Host "   InterfaceAlias   : $($tcp.InterfaceAlias)"
Write-Host "   SourceAddress    : $tcpSourceAddress"

if (-not $tcp.TcpTestSucceeded) {
    Add-Finding -Level 'BLOCK' -Message "TCP $Ip`:$Port unreachable. Either the device is offline, a firewall is blocking, or no route exists."
}

if ($selectedSourceAddress -and $tcpSourceAddress -and ($selectedSourceAddress -ne $tcpSourceAddress)) {
    Write-Host ''
    Write-Host "   WARN: Find-NetRoute source ($selectedSourceAddress) differs from Test-NetConnection source ($tcpSourceAddress)."
}

# Final effective adapter — Test-NetConnection wins because it represents the real connect() path
$effectiveAlias = if (-not [string]::IsNullOrWhiteSpace($tcp.InterfaceAlias)) { $tcp.InterfaceAlias } else { $selectedInterfaceAlias }
$effectiveClass = Get-AdapterClass -Alias $effectiveAlias
Write-Host ''
Write-Host "   effective adapter: $effectiveAlias (class=$effectiveClass)"

# ---------------------------------------------------------------- Step 5: side-by-side comparison
Write-Host ''
Write-Host '[5/6] Physical vs virtual adapter comparison'
if ($physicalLanAdapters.Count -eq 0 -and $virtualAdapters.Count -eq 0) {
    Write-Host '   (no adapters in either bucket)'
}
else {
    Write-Host '   Physical LAN candidates (Ethernet / Wi-Fi):'
    if ($physicalLanAdapters.Count -eq 0) {
        Write-Host '     (none active)'
    }
    else {
        foreach ($phy in $physicalLanAdapters) {
            $marker = if ($phy.InterfaceAlias -eq $effectiveAlias) { ' <== effective' } else { '' }
            Write-Host ("     - {0,-30}  {1,-18}  /{2}{3}" -f $phy.InterfaceAlias, $phy.IPAddress, $phy.PrefixLength, $marker)
        }
    }

    Write-Host '   Virtual adapters (VPN / TAP / vEthernet):'
    if ($virtualAdapters.Count -eq 0) {
        Write-Host '     (none active)'
    }
    else {
        foreach ($virt in $virtualAdapters) {
            $marker = if ($virt.InterfaceAlias -eq $effectiveAlias) { ' <== effective (route hijack risk)' } else { '' }
            Write-Host ("     - {0,-30}  {1,-18}  /{2}{3}" -f $virt.InterfaceAlias, $virt.IPAddress, $virt.PrefixLength, $marker)
        }
    }
}

# ---------------------------------------------------------------- Step 6: risk evaluation
Write-Host ''
Write-Host '[6/6] Risk evaluation'

# Same-subnet quick sanity for 192.168.2.x style targets — many legacy PLCs sit on /24s
$hasSameSubnetPhysical = $false
if ($parsedTarget.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
    $targetBytes = $parsedTarget.GetAddressBytes()
    foreach ($phy in $physicalLanAdapters) {
        if ($phy.PrefixLength -ge 8 -and $phy.PrefixLength -le 32 -and $phy.IPAddress) {
            $phyParsed = $null
            if ([System.Net.IPAddress]::TryParse([string]$phy.IPAddress, [ref]$phyParsed)) {
                $phyBytes = $phyParsed.GetAddressBytes()
                $prefix = [int]$phy.PrefixLength
                $maskBytes = New-Object byte[] 4
                for ($i = 0; $i -lt 4; $i++) {
                    $bitsInThisByte = [Math]::Max(0, [Math]::Min(8, $prefix - $i * 8))
                    if ($bitsInThisByte -le 0) {
                        $maskBytes[$i] = 0
                    }
                    elseif ($bitsInThisByte -ge 8) {
                        $maskBytes[$i] = 0xFF
                    }
                    else {
                        $maskBytes[$i] = [byte](0xFF -shl (8 - $bitsInThisByte) -band 0xFF)
                    }
                }
                $same = $true
                for ($i = 0; $i -lt 4; $i++) {
                    if (($targetBytes[$i] -band $maskBytes[$i]) -ne ($phyBytes[$i] -band $maskBytes[$i])) {
                        $same = $false
                        break
                    }
                }
                if ($same) {
                    $hasSameSubnetPhysical = $true
                    break
                }
            }
        }
    }

    if (-not $hasSameSubnetPhysical -and $isPrivate -and -not $parsedTarget.ToString().StartsWith('127.')) {
        Add-Finding -Level 'RISK' -Message "No physical LAN adapter is on the same subnet as $Ip. Traffic will either fall back to the default route (VPN / virtual) or fail."
    }
}

if ($effectiveClass -eq 'virtual') {
    Add-Finding -Level 'RISK' -Message "Connection currently uses virtual adapter '$effectiveAlias'. Modbus TCP may open port $Port via the tunnel but fail at the protocol layer because the device is not on the other end."
}

if ($effectiveClass -eq 'loopback' -and -not $parsedTarget.ToString().StartsWith('127.')) {
    Add-Finding -Level 'RISK' -Message "Effective adapter is loopback but target is not in 127.0.0.0/8. This is unexpected."
}

if ($virtualAdapters.Count -gt 0 -and $physicalLanAdapters.Count -gt 0 -and $effectiveClass -eq 'virtual') {
    Add-Finding -Level 'RISK' -Message "Physical adapter(s) exist but the OS picked a virtual adapter. Likely cause: virtual adapter has a lower interface metric or a catch-all route."
}

if ($effectiveClass -eq 'wifi' -and $physicalLanAdapters | Where-Object { (Get-AdapterClass -Alias $_.InterfaceAlias) -eq 'ethernet' }) {
    Add-Finding -Level 'RISK' -Message "Wi-Fi is selected but Ethernet is available. For wired PLCs prefer Ethernet."
}

if ($Findings.Count -eq 0) {
    Add-Finding -Level 'OK' -Message "Route looks sane. Effective adapter '$effectiveAlias' (class=$effectiveClass)."
}

foreach ($f in $Findings) { Write-Host "   $f" }

# ---------------------------------------------------------------- Remediation
Write-Host ''
Write-Host '----------------------------- Remediation -----------------------------'

if ($effectiveClass -eq 'virtual') {
    Write-Host ''
    Write-Host 'Recommended remediation (try in this order):'
    Write-Host ''
    Write-Host '  1. Disable the virtual adapter so Windows falls back to physical LAN:'
    Write-Host "         Disable-NetAdapter -Name '$effectiveAlias' -Confirm:`$false"
    Write-Host '     Re-enable later with Enable-NetAdapter -Name <alias>.'
    Write-Host ''
    Write-Host '  2. Lower the metric on the physical PLC adapter (preferred when you must keep VPN on):'

    $physRecommend = $physicalLanAdapters | Select-Object -First 1
    if ($physRecommend) {
        Write-Host "         Set-NetIPInterface -InterfaceAlias '$($physRecommend.InterfaceAlias)' -InterfaceMetric 5"
        Write-Host "         Set-NetIPInterface -InterfaceAlias '$effectiveAlias' -InterfaceMetric 200"
    }
    else {
        Write-Host '         (no physical LAN adapter detected — plug in Ethernet first)'
    }
    Write-Host ''
    Write-Host '  3. Add a host-specific route that pins the target to the physical adapter:'
    if ($physRecommend) {
        $gw = '<plc-gateway-or-on-link>'
        Write-Host "         route ADD $Ip MASK 255.255.255.255 $gw IF $($physRecommend.InterfaceIndex)"
        Write-Host '     Replace <plc-gateway-or-on-link> with the LAN gateway (e.g. 192.168.2.1) or 0.0.0.0 for on-link.'
    }
    else {
        Write-Host '         route ADD <ip> MASK 255.255.255.255 <gateway> IF <ifIndex>'
    }
    Write-Host "     Verify with: Get-NetRoute -DestinationPrefix '$Ip/32'"
    Write-Host ''
    Write-Host '  After applying any of the above, re-run this script to confirm the effective adapter changed.'
}
elseif (-not $hasSameSubnetPhysical -and $isPrivate) {
    Write-Host ''
    Write-Host 'Recommended remediation:'
    Write-Host '  - Plug a wired LAN cable to the PLC and configure a static IP on the same subnet.'
    Write-Host '  - For 192.168.2.0/24 PLCs, set the NIC to 192.168.2.x (x != PLC IP) / 255.255.255.0.'
    Write-Host '  - See tools/configure-plc-ethernet-ip.ps1.'
}
elseif (-not $tcp.TcpTestSucceeded) {
    Write-Host ''
    Write-Host 'Recommended remediation:'
    Write-Host '  - Verify the PLC is powered, the cable is connected, and TCP 502 is enabled in firmware.'
    Write-Host "  - Run: Test-NetConnection -ComputerName $Ip -Port $Port -InformationLevel Detailed"
}
else {
    Write-Host ''
    Write-Host 'No remediation required. The effective route looks healthy.'
}

Write-Host ''
Write-Host '------------------------------- Summary -------------------------------'
Write-Host "  Target          : $Ip`:$Port"
Write-Host "  TCP open        : $($tcp.TcpTestSucceeded)"
Write-Host "  Effective NIC   : $effectiveAlias ($effectiveClass)"
Write-Host "  Severity        : $script:Severity"
Write-Host ''

# Exit code semantics:
#   0 = OK
#   1 = RISK (TCP probably succeeds but protocol layer may still fail)
#   2 = BLOCK (TCP unreachable / no adapter at all — operator must fix first)
switch ($script:Severity) {
    'OK'    { exit 0 }
    'INFO'  { exit 0 }
    'RISK'  { exit 1 }
    'BLOCK' { exit 2 }
    default { exit 1 }
}
