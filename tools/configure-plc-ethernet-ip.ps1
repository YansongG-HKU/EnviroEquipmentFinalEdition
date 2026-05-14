param(
    [string]$InterfaceName = '以太网',
    [string]$Address = '192.168.2.10',
    [string]$Mask = '255.255.255.0'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must run as administrator because it changes an Ethernet IPv4 address.'
}

$existing = netsh interface ipv4 show addresses name="$InterfaceName"
if ($existing -match [regex]::Escape($Address)) {
    Write-Host "$InterfaceName already has $Address. Nothing to change."
    exit 0
}

Write-Host "Adding $Address/$Mask to $InterfaceName without a default gateway ..."
netsh interface ipv4 add address name="$InterfaceName" address=$Address mask=$Mask

Write-Host ''
netsh interface ipv4 show addresses name="$InterfaceName"
Write-Host ''
Write-Host 'Done. Now run tools\connect-plc.bat or tools\connect-plc.ps1.'
