param(
    [string]$Snap7Root
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$bundledRoot = Join-Path $repoRoot 'src\SiemensS7Demo\Native\Snap7'
$dllPath = Join-Path $bundledRoot 'win64\snap7.dll'
$wrapperPath = Join-Path $bundledRoot 'reference\dotnet\snap7.net.cs'
$licensePath = Join-Path $bundledRoot 'licenses\lgpl-3.0.txt'

function Copy-Snap7IntoProject {
    param([string]$SourceRoot)

    $sourceFull = [System.IO.Path]::GetFullPath($SourceRoot)
    $sourceDll = Join-Path $sourceFull 'build\bin\win64\snap7.dll'
    $sourceWrapper = Join-Path $sourceFull 'release\wrappers\dot.net\snap7.net.cs'
    $sourceLgpl = Join-Path $sourceFull 'lgpl-3.0.txt'
    $sourceGpl = Join-Path $sourceFull 'gpl.txt'
    $sourceReadme = Join-Path $sourceFull 'readme.md'
    $sourceHistory = Join-Path $sourceFull 'HISTORY.txt'

    if (-not (Test-Path $sourceDll)) {
        throw "snap7.dll not found in source root: $sourceDll"
    }
    if (-not (Test-Path $sourceWrapper)) {
        throw "official .NET wrapper not found in source root: $sourceWrapper"
    }
    if (-not (Test-Path $sourceLgpl)) {
        throw "LGPL license not found in source root: $sourceLgpl"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $bundledRoot 'win64') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundledRoot 'reference\dotnet') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundledRoot 'licenses') | Out-Null

    Copy-Item -LiteralPath $sourceDll -Destination $dllPath -Force
    Copy-Item -LiteralPath $sourceWrapper -Destination $wrapperPath -Force
    Copy-Item -LiteralPath $sourceLgpl -Destination $licensePath -Force

    if (Test-Path $sourceGpl) {
        Copy-Item -LiteralPath $sourceGpl -Destination (Join-Path $bundledRoot 'licenses\gpl.txt') -Force
    }
    if (Test-Path $sourceReadme) {
        Copy-Item -LiteralPath $sourceReadme -Destination (Join-Path $bundledRoot 'UPSTREAM_README.md') -Force
    }
    if (Test-Path $sourceHistory) {
        Copy-Item -LiteralPath $sourceHistory -Destination (Join-Path $bundledRoot 'HISTORY.txt') -Force
    }
}

if (-not [string]::IsNullOrWhiteSpace($Snap7Root)) {
    Copy-Snap7IntoProject -SourceRoot $Snap7Root
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($required in @($dllPath, $wrapperPath, $licensePath)) {
    if (-not (Test-Path $required)) {
        $failures.Add($required) | Out-Null
    }
}

Write-Host "Project Snap7 root: $bundledRoot"
Write-Host "Native DLL:         $dllPath"
Write-Host "C# API reference:   $wrapperPath"
Write-Host "License:            $licensePath"

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'Project-bundled Snap7 files are incomplete:'
    foreach ($failure in $failures) {
        Write-Host "  missing: $failure"
    }
    Write-Host ''
    Write-Host 'To refresh from an official Snap7 checkout, run:'
    Write-Host '  .\tools\ensure-snap7.ps1 -Snap7Root <official-snap7-checkout>'
    exit 1
}

Write-Host ''
Write-Host 'Project-bundled Snap7 dependency is ready.'
