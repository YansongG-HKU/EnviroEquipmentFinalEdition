param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\SiemensS7Demo\SiemensS7Demo.csproj'
$configPath = Join-Path $repoRoot 'src\SiemensS7Demo\Config\siemens_s7_200_smart_sample.xml'
$snap7RuntimeRoot = Join-Path $repoRoot 'src\SiemensS7Demo\Native\Snap7'
$snap7DllPath = Join-Path $snap7RuntimeRoot 'win64\snap7.dll'
$snap7WrapperPath = Join-Path $snap7RuntimeRoot 'reference\dotnet\snap7.net.cs'
$snap7LicensePath = Join-Path $snap7RuntimeRoot 'licenses\lgpl-3.0.txt'
$runtimeRoot = 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App'
$cscCandidates = @(
    'H:\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
)

$failures = [System.Collections.Generic.List[string]]::new()

function Write-Check {
    param(
        [string]$Name,
        [bool]$Ok,
        [string]$Detail
    )

    $status = if ($Ok) { 'OK ' } else { 'FAIL' }
    Write-Host ("[{0}] {1} - {2}" -f $status, $Name, $Detail)
    if (-not $Ok) {
        $failures.Add($Name) | Out-Null
    }
}

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
Write-Check 'dotnet command' ([bool]$dotnet) ($(if ($dotnet) { $dotnet.Source } else { 'not found' }))

$sdkList = if ($dotnet) { & dotnet --list-sdks } else { @() }
$hasSdk = ($sdkList | Where-Object { $_ -match '^8\.' } | Select-Object -First 1) -ne $null
Write-Host ("[INFO] .NET 8 SDK - {0}" -f ($(if ($hasSdk) { 'installed; dotnet run is available' } else { 'not installed; tools\run-s7-demo.ps1 will use csc fallback' })))

$runtimeDir = $null
if (Test-Path $runtimeRoot) {
    $runtimeDir = Get-ChildItem $runtimeRoot -Directory |
        Where-Object { $_.Name -like '8.*' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
}
Write-Check '.NET 8 runtime' ([bool]$runtimeDir) ($(if ($runtimeDir) { $runtimeDir.FullName } else { 'not found' }))

$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    $cscCommand = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($cscCommand) {
        $csc = $cscCommand.Source
    }
}
Write-Check 'C# compiler fallback' ([bool]$csc) ($(if ($csc) { $csc } else { 'not found; install .NET SDK or Visual Studio Build Tools' }))

Write-Check 'project file' (Test-Path $projectPath) $projectPath
Write-Check 'tag XML' (Test-Path $configPath) $configPath

if (Test-Path $configPath) {
    try {
        [xml]$xml = Get-Content -Raw -Encoding UTF8 $configPath
        $firstTag = $xml.DeviceProtocol.Tags.Tag | Select-Object -First 1
        Write-Check 'tag XML UTF-8 parse' ($null -ne $firstTag) ("first tag: {0} / {1}" -f $firstTag.name, $firstTag.displayName)
    }
    catch {
        Write-Check 'tag XML UTF-8 parse' $false $_.Exception.Message
    }
}

Write-Check 'project-bundled Snap7 DLL' (Test-Path $snap7DllPath) $snap7DllPath
Write-Check 'project-bundled Snap7 .NET reference' (Test-Path $snap7WrapperPath) $snap7WrapperPath
Write-Check 'project-bundled Snap7 license' (Test-Path $snap7LicensePath) $snap7LicensePath

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host 'Prerequisite check failed.'
    exit 1
}

Write-Host 'Prerequisite check passed.'
