param(
    [string]$LegacyRoot = 'H:\qtFileForVscode\EnviroEquipmentFinalEdition_202604\Code\Bin\Debug',
    [string]$OutputPath = 'src\SiemensS7Demo\Config\legacy_imported.project.json',
    [string]$S7IpOverride = '',
    [int]$MaxTagsPerDevice = 0,
    [switch]$EnableModbus
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$equipmentConfig = Join-Path $LegacyRoot 'ProjectData\equipmentConfig.xml'
if (-not (Test-Path $equipmentConfig)) {
    throw "Legacy equipmentConfig.xml not found: $equipmentConfig"
}

function Convert-SafeId {
    param([string]$Text, [string]$Fallback)

    $clean = ($Text -replace '[^A-Za-z0-9_-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return $Fallback
    }
    return $clean.ToLowerInvariant()
}

function Get-ElementValue {
    param([string[]]$Lines, [string]$Name)

    $pattern = "<$Name>\s*(?<value>.*?)\s*</$Name>"
    foreach ($line in $Lines) {
        $match = [regex]::Match($line, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            return $match.Groups['value'].Value.Trim()
        }
    }

    return ''
}

function Get-AttributeValue {
    param([string]$Line, [string]$Name)

    $match = [regex]::Match($Line, "$Name\s*=\s*`"(?<value>[^`"]*)`"", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return $match.Groups['value'].Value.Trim()
    }

    return ''
}

function Convert-Scale {
    param([string]$ScaleText)

    $scale = 0.0
    if (-not [double]::TryParse($ScaleText, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$scale)) {
        return 1.0
    }

    if ([Math]::Abs($scale) -lt [double]::Epsilon) {
        return 1.0
    }

    return 1.0 / $scale
}

function Convert-TagType {
    param([string]$TypeText)

    if ($TypeText -match '^(V|Q)$') {
        return 'Bool'
    }
    if ($TypeText -match 'HRF' -and $TypeText -match 'float') {
        return 'Real'
    }
    if ($TypeText -match 'HRU') {
        return 'UInt16'
    }
    if ($TypeText -match 'HRS') {
        return 'Int16'
    }
    if ($TypeText -match 'int16') {
        return 'Int16'
    }

    return ''
}

function Convert-Address {
    param(
        [string]$Protocol,
        [string]$DataType,
        [string]$AddressText,
        [string]$DeviationText,
        [string]$AreaText,
        [string]$DbNumberText
    )

    $address = 0
    if (-not [int]::TryParse($AddressText, [ref]$address)) {
        return ''
    }

    $deviation = 0
    [void][int]::TryParse($DeviationText, [ref]$deviation)

    if ($Protocol -eq 's7') {
        $db = 1
        [void][int]::TryParse($DbNumberText, [ref]$db)
        $area = if ([string]::IsNullOrWhiteSpace($AreaText)) { 'db' } else { $AreaText.ToLowerInvariant() }

        if ($area -eq 'db') {
            switch ($DataType) {
                'Bool' { return "DB$([int]$db).DBX$address.$deviation" }
                'Real' { return "DB$([int]$db).DBD$address" }
                default { return "DB$([int]$db).DBW$address" }
            }
        }

        switch ($DataType) {
            'Bool' { return "M$address.$deviation" }
            'Real' { return "MD$address" }
            default { return "MW$address" }
        }
    }

    if ($Protocol -eq 'modbus') {
        switch ($DataType) {
            'Bool' { return "C$address" }
            'Real' { return "HRF$address" }
            default { return "HR$address" }
        }
    }

    return ''
}

function Read-LegacyTags {
    param([string]$Path, [string]$Protocol, [string]$DeviceId, [int]$MaxTags)

    if (-not (Test-Path $Path)) {
        Write-Warning "Address config missing: $Path"
        return @()
    }

    $lines = Get-Content -Path $Path -Encoding UTF8
    $group = ''
    $blocks = New-Object System.Collections.Generic.List[object]
    $current = New-Object System.Collections.Generic.List[string]
    $insideParam = $false
    $paramStart = ''

    foreach ($line in $lines) {
        if ($line -match '<ParamType') {
            $group = Get-AttributeValue -Line $line -Name 'GroupName'
        }

        if ($line -match '<Param\b' -and $line -notmatch '/>') {
            $insideParam = $true
            $paramStart = $line
            $current.Clear()
            $current.Add($line)
            continue
        }

        if ($insideParam) {
            $current.Add($line)
            if ($line -match '</Param>') {
                $blocks.Add([pscustomobject]@{
                    Group = $group
                    Start = $paramStart
                    Lines = @($current.ToArray())
                })
                $insideParam = $false
            }
        }
    }

    $tags = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($block in $blocks) {
        if ($MaxTags -gt 0 -and $tags.Count -ge $MaxTags) {
            break
        }

        $addressText = Get-ElementValue -Lines $block.Lines -Name 'address'
        if ([string]::IsNullOrWhiteSpace($addressText)) {
            continue
        }

        $typeText = Get-ElementValue -Lines $block.Lines -Name 'type'
        $dataType = Convert-TagType -TypeText $typeText
        if ([string]::IsNullOrWhiteSpace($dataType)) {
            continue
        }

        $index++
        $displayName = Get-AttributeValue -Line $block.Start -Name 'ParamName'
        if ([string]::IsNullOrWhiteSpace($displayName)) {
            $displayName = Get-ElementValue -Lines $block.Lines -Name 'describe'
        }
        if ([string]::IsNullOrWhiteSpace($displayName)) {
            $displayName = "Legacy tag $index"
        }

        $address = Convert-Address `
            -Protocol $Protocol `
            -DataType $dataType `
            -AddressText $addressText `
            -DeviationText (Get-ElementValue -Lines $block.Lines -Name 'deviation') `
            -AreaText (Get-ElementValue -Lines $block.Lines -Name 'area') `
            -DbNumberText (Get-ElementValue -Lines $block.Lines -Name 'dbnumber')

        if ([string]::IsNullOrWhiteSpace($address)) {
            continue
        }

        $scale = Convert-Scale -ScaleText (Get-ElementValue -Lines $block.Lines -Name 'scale')
        $tagName = '{0}_tag_{1:0000}' -f $DeviceId, $index

        $tags.Add([ordered]@{
            name = $tagName
            displayName = $displayName
            group = if ([string]::IsNullOrWhiteSpace($block.Group)) { 'Legacy' } else { $block.Group }
            address = $address
            dataType = $dataType
            unit = ''
            scale = $scale
            offset = 0
            access = 'Read'
            safeWrite = $false
        })
    }

    return $tags.ToArray()
}

[xml]$equipmentXml = Get-Content -Path $equipmentConfig -Raw -Encoding UTF8
$devices = New-Object System.Collections.Generic.List[object]
$enabledS7Assigned = $false
$deviceIndex = 0

foreach ($equipment in $equipmentXml.equipments.equipment) {
    $deviceIndex++
    $legacyProtocol = [string]$equipment.protocol
    $module = [string]$equipment.module
    $protocol = switch ($legacyProtocol) {
        'Siemens' { 's7' }
        'Schneider' { 'modbus' }
        default { 'mock' }
    }

    $deviceIdSeed = if (-not [string]::IsNullOrWhiteSpace([string]$equipment.id)) { [string]$equipment.id } else { "$module-$deviceIndex" }
    $deviceId = Convert-SafeId -Text $deviceIdSeed -Fallback "device-$deviceIndex"
    $deviceIp = [string]$equipment.ip
    if ($protocol -eq 's7' -and -not [string]::IsNullOrWhiteSpace($S7IpOverride)) {
        $deviceIp = $S7IpOverride
    }

    $addressConfigPath = Join-Path $LegacyRoot "addressProtocol\$legacyProtocol\$module\addressConfig.xml"
    $tags = Read-LegacyTags -Path $addressConfigPath -Protocol $protocol -DeviceId $deviceId -MaxTags $MaxTagsPerDevice

    $enabled = $false
    if ($protocol -eq 's7' -and -not $enabledS7Assigned) {
        $enabled = $true
        $enabledS7Assigned = $true
    }
    elseif ($protocol -eq 'modbus' -and $EnableModbus) {
        $enabled = $true
    }

    $port = 0
    [void][int]::TryParse([string]$equipment.port, [ref]$port)
    if ($port -le 0) {
        $port = if ($protocol -eq 'modbus') { 502 } else { 102 }
    }

    $devices.Add([ordered]@{
        id = $deviceId
        name = [string]$equipment.name
        enabled = $enabled
        protocol = $protocol
        ip = $deviceIp
        port = $port
        cpuType = if ($protocol -eq 's7') { 'S7-200 SMART' } else { 'Modbus TCP' }
        rack = 0
        slot = 0
        connectionType = 'basic'
        unitId = 1
        pollingIntervalMs = 1000
        tags = $tags
    })
}

$project = [ordered]@{
    projectId = 'legacy-imported'
    projectName = 'Legacy Imported Project'
    devices = $devices
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$json = $project | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))

$totalTags = ($devices | ForEach-Object { $_.tags.Count } | Measure-Object -Sum).Sum
Write-Output "Imported devices: $($devices.Count)"
Write-Output "Imported tags: $totalTags"
Write-Output "Output: $OutputPath"
