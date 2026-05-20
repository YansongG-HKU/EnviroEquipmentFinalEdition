[CmdletBinding()]
param(
    [string]$CssPath = "$PSScriptRoot/../温箱202605/styles.css",
    [string]$OutPath = "$PSScriptRoot/../src/SiemensS7Demo.Wpf/Themes/Tokens.xaml"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CssPath)) {
    throw "CSS not found at $CssPath"
}

$content = Get-Content -LiteralPath $CssPath -Raw

# Match the :root { ... } block only (skip :root.theme-night and tweak rules in pass 1)
$match = [regex]::Match($content, ":root\s*\{(?<body>[^}]*)\}", 'IgnoreCase')
if (-not $match.Success) {
    throw "No :root block found in $CssPath"
}

$body = $match.Groups['body'].Value
$varRe = '(?m)^\s*--(?<name>[a-z0-9\-]+)\s*:\s*(?<value>[^;]+);'
$matches = [regex]::Matches($body, $varRe)

# CSS custom property name -> XAML resource key
function ConvertTo-Key([string]$n) {
    $segments = $n -split '-' | ForEach-Object {
        if ($_.Length -gt 0) { $_.Substring(0,1).ToUpper() + $_.Substring(1) } else { '' }
    }
    return "Brush" + ($segments -join '')
}

function ConvertHexShortToFull([string]$hex) {
    if ($hex.Length -eq 4) {
        $r = $hex[1]; $g = $hex[2]; $b = $hex[3]
        return "#$r$r$g$g$b$b"
    }
    return $hex
}

function ConvertColor([string]$v) {
    $v = $v.Trim()
    if ($v.StartsWith('#')) {
        return (ConvertHexShortToFull $v).ToUpper()
    }
    $m = [regex]::Match($v, 'rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)(?:\s*,\s*(?<a>[\d.]+))?\s*\)')
    if ($m.Success) {
        $r = [int]$m.Groups['r'].Value
        $g = [int]$m.Groups['g'].Value
        $b = [int]$m.Groups['b'].Value
        if ($m.Groups['a'].Success) {
            $a = [int]([math]::Round([double]$m.Groups['a'].Value * 255))
            return "#{0:X2}{1:X2}{2:X2}{3:X2}" -f $a, $r, $g, $b
        }
        return "#{0:X2}{1:X2}{2:X2}" -f $r, $g, $b
    }
    return $null
}

$brushLines = New-Object System.Collections.Generic.List[string]
$thicknessLines = New-Object System.Collections.Generic.List[string]
$fontLines = New-Object System.Collections.Generic.List[string]

foreach ($m in $matches) {
    $name = $m.Groups['name'].Value
    $value = $m.Groups['value'].Value.Trim()
    $key = ConvertTo-Key $name

    if ($name -like 'font-*') {
        # Strip stack quotes -> first family
        $first = ($value -split ',')[0].Trim().Trim('"').Trim("'")
        $fontLines.Add("    <FontFamily x:Key=`"$key`">$first</FontFamily>")
        continue
    }
    if ($name -like 'r-*' -or $name -in @('row-h','tab-h','bar-h')) {
        $px = ($value -replace 'px','').Trim()
        $thicknessLines.Add("    <sys:Double x:Key=`"$key`">$px</sys:Double>")
        continue
    }
    $hex = ConvertColor $value
    if ($null -ne $hex) {
        $brushLines.Add("    <SolidColorBrush x:Key=`"$key`" Color=`"$hex`" />")
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
[void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
[void]$sb.AppendLine('                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">')
[void]$sb.AppendLine('    <!-- Generated from styles.css via tools/CssToXaml.ps1. Do not hand-edit. -->')
foreach ($l in $brushLines)    { [void]$sb.AppendLine($l) }
foreach ($l in $fontLines)     { [void]$sb.AppendLine($l) }
foreach ($l in $thicknessLines){ [void]$sb.AppendLine($l) }
[void]$sb.AppendLine('</ResourceDictionary>')

$dir = Split-Path -Parent $OutPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
Set-Content -LiteralPath $OutPath -Value $sb.ToString() -Encoding utf8 -NoNewline

Write-Output "Wrote $($brushLines.Count) brushes, $($fontLines.Count) fonts, $($thicknessLines.Count) doubles -> $OutPath"
