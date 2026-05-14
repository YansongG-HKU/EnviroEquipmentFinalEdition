param(
    [string]$Ip = '192.168.2.180',
    [string]$Cpu = 'S7-200 SMART',
    [int]$Rack = 0,
    [object[]]$Slots = @(0, 1, 2),
    [object[]]$ConnectionTypes = @('basic', 'op', 'pg'),
    [int]$Port = 102,
    [switch]$AllowVirtualRoute
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Convert-ToList([object[]]$Values) {
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($value in $Values) {
        foreach ($part in ([string]$value -split ',')) {
            $trimmed = $part.Trim()
            if ($trimmed.Length -gt 0) {
                $items.Add($trimmed)
            }
        }
    }

    return $items
}

$slotList = Convert-ToList $Slots | ForEach-Object { [int]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }
$connectionTypeList = Convert-ToList $ConnectionTypes | ForEach-Object { $_.ToLowerInvariant() }

Write-Host 'Step 1: network route and TCP check'
$networkArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-plc-network.ps1'), '-Ip', $Ip, '-Port', $Port)
if (-not $AllowVirtualRoute) {
    $networkArgs += '-RequireLocalSubnet'
}

& powershell.exe @networkArgs
$networkExitCode = $LASTEXITCODE
if ($networkExitCode -ne 0) {
    Write-Host ''
    Write-Host 'Network precheck failed. Snap7 handshake was not attempted.'
    exit $networkExitCode
}

Write-Host ''
Write-Host 'Step 2: Snap7 connection handshake'
foreach ($connectionType in $connectionTypeList) {
    foreach ($slot in $slotList) {
        Write-Host ''
        Write-Host "Trying connectionType=$connectionType rack=$Rack slot=$slot ..."
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $attemptOutput = & powershell.exe `
                -NoProfile `
                -ExecutionPolicy Bypass `
                -File (Join-Path $PSScriptRoot 'run-s7-demo.ps1') `
                --adapter snap7 `
                --ip $Ip `
                --cpu $Cpu `
                --port $Port `
                --rack $Rack `
                --slot $slot `
                --connection-type $connectionType `
                --connect-only 2>&1
            $attemptExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if ($attemptExitCode -eq 0) {
            $attemptOutput | ForEach-Object { Write-Host $_ }
            Write-Host ''
            Write-Host "SUCCESS: Snap7 connected with connectionType=$connectionType rack=$Rack slot=$slot."
            exit 0
        }

        $attemptOutput | ForEach-Object { Write-Host $_ }
        Write-Host "FAILED: connectionType=$connectionType rack=$Rack slot=$slot."
    }
}

Write-Host ''
Write-Host 'No tested Snap7 connection type / rack / slot combination completed the handshake.'
Write-Host 'Fix routing first if the network check shows a VPN/TAP source address.'
exit 1
