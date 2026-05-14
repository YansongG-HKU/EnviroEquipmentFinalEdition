param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DemoArgs
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'src\SiemensS7Demo'
$outputDir = Join-Path $repoRoot "artifacts\s7-demo\run-$PID"
$outputExe = Join-Path $outputDir 'SiemensS7Demo.exe'
$runtimeConfig = Join-Path $outputDir 'SiemensS7Demo.runtimeconfig.json'

function Test-Snap7Requested {
    param([string[]]$Arguments)

    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        $arg = $Arguments[$i]
        if ($arg -eq '--real') {
            return $true
        }
        if ($arg -eq '--mock') {
            return $false
        }
        if ($arg -eq '--adapter' -and ($i + 1) -lt $Arguments.Count) {
            return ($Arguments[$i + 1] -eq 'snap7')
        }
        if ($arg -like '--adapter=*') {
            return (($arg.Substring('--adapter='.Length)) -eq 'snap7')
        }
    }

    return $false
}

function Resolve-Snap7Dll {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:SNAP7_DLL)) {
        $candidates += [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($env:SNAP7_DLL))
    }

    $candidates += [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'src\SiemensS7Demo\Native\Snap7\win64\snap7.dll'))

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

$cscCandidates = @(
    'H:\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
)

$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    $cscCommand = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($cscCommand) {
        $csc = $cscCommand.Source
    }
}

if (-not $csc) {
    throw 'Could not find csc.exe. Install Visual Studio Build Tools or the .NET SDK.'
}

$runtimeRoot = 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App'
if (-not (Test-Path $runtimeRoot)) {
    throw 'Could not find Microsoft.NETCore.App runtime. Install the .NET 8 runtime.'
}

$runtimeDir = Get-ChildItem $runtimeRoot -Directory |
    Where-Object { $_.Name -like '8.*' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if (-not $runtimeDir) {
    throw 'Could not find a .NET 8 runtime under C:\Program Files\dotnet\shared\Microsoft.NETCore.App.'
}

$snap7DllPath = Resolve-Snap7Dll
if ((Test-Snap7Requested -Arguments $DemoArgs) -and -not $snap7DllPath) {
    throw "Snap7 mode needs the project-bundled snap7.dll at src\SiemensS7Demo\Native\Snap7\win64\snap7.dll, or set SNAP7_DLL explicitly."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$references = @()
Get-ChildItem $runtimeDir.FullName -Filter *.dll | ForEach-Object {
    try {
        [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
        $references += '/reference:' + $_.FullName
    }
    catch {
        # Native runtime DLLs do not contain managed metadata.
    }
}

$snap7ReferenceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'Native\Snap7\reference'))
$sources = Get-ChildItem $projectRoot -Recurse -Filter *.cs |
    Where-Object {
        -not $_.FullName.StartsWith(
            $snap7ReferenceRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)
    } |
    ForEach-Object { $_.FullName }

& $csc `
    /nologo `
    /langversion:latest `
    /nullable:enable `
    /utf8output `
    /target:exe `
    "/out:$outputExe" `
    /nostdlib `
    @references `
    @sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$configSource = Join-Path $projectRoot 'Config'
$configTarget = Join-Path $outputDir 'Config'
Copy-Item -Path $configSource -Destination $configTarget -Recurse -Force

$snap7Dll = if ($snap7DllPath) { Resolve-Path $snap7DllPath -ErrorAction SilentlyContinue } else { $null }
if ($snap7Dll) {
    Copy-Item -Path $snap7Dll.Path -Destination (Join-Path $outputDir 'snap7.dll') -Force
}

$runtimeJson = @{
    runtimeOptions = @{
        tfm = 'net8.0'
        framework = @{
            name = 'Microsoft.NETCore.App'
            version = $runtimeDir.Name
        }
    }
} | ConvertTo-Json -Depth 5

[System.IO.File]::WriteAllText($runtimeConfig, $runtimeJson, [System.Text.UTF8Encoding]::new($false))

& dotnet $outputExe @DemoArgs
exit $LASTEXITCODE
