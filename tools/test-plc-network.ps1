param(
    [string]$Ip = '192.168.2.180',
    [int]$Port = 102,
    [switch]$RequireLocalSubnet
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$routeRisk = $false

Write-Host "Testing ICMP reachability for $Ip ..."
$pingOk = Test-Connection -ComputerName $Ip -Count 2 -Quiet -ErrorAction SilentlyContinue
Write-Host "Ping: $pingOk"

Write-Host "Testing TCP $Ip`:$Port ..."
$tcp = Test-NetConnection -ComputerName $Ip -Port $Port -WarningAction SilentlyContinue
$sourceAddress = if ($tcp.SourceAddress -and $tcp.SourceAddress.IPAddress) {
    $tcp.SourceAddress.IPAddress
}
else {
    $tcp.SourceAddress
}

Write-Host "TcpTestSucceeded: $($tcp.TcpTestSucceeded)"
Write-Host "RemoteAddress: $($tcp.RemoteAddress)"
Write-Host "InterfaceAlias: $($tcp.InterfaceAlias)"
Write-Host "SourceAddress: $sourceAddress"

if ($Ip -like '192.168.2.*' -and $sourceAddress -notlike '192.168.2.*') {
    $routeRisk = $true
    Write-Host ''
    Write-Host 'WARNING: target is in 192.168.2.x, but the selected source address is not.'
    Write-Host 'The connection is probably going through a VPN/TAP/default route instead of the PLC LAN.'
    Write-Host 'Snap7 may open TCP 102 but still time out during the ISO/S7 handshake in this state.'
}

if ($tcp.InterfaceAlias -match 'TAP|VPN|Nord|Lets') {
    $routeRisk = $true
    Write-Host ''
    Write-Host "WARNING: route uses virtual adapter '$($tcp.InterfaceAlias)'."
    Write-Host 'For a directly connected PLC, connect Ethernet and configure a local 192.168.2.x address.'
}

if (-not $tcp.TcpTestSucceeded) {
    Write-Host ''
    Write-Host "FAIL: TCP $Ip`:$Port is not reachable."
    exit 1
}

Write-Host ''
Write-Host 'Relevant ARP/neighbor entries:'
Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -eq $Ip } |
    Select-Object ifIndex, IPAddress, LinkLayerAddress, State |
    Format-Table -AutoSize

Write-Host ''
Write-Host 'Active IPv4 interfaces:'
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.AddressState -eq 'Preferred' } |
    Select-Object InterfaceAlias, IPAddress, PrefixLength |
    Format-Table -AutoSize

if ($routeRisk -and $RequireLocalSubnet) {
    Write-Host ''
    Write-Host 'FAIL: route is not suitable for the default direct-PLC startup path.'
    Write-Host 'Use the physical Ethernet PLC network, or rerun the caller with -AllowVirtualRoute if this route is intentional.'
    exit 2
}

Write-Host ''
Write-Host 'Network precheck passed.'
