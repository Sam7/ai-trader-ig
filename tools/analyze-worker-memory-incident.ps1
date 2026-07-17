[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]] $TracePaths,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [string[]] $SerialPaths = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PropertyValue {
    param(
        [AllowNull()] $InputObject,
        [string[]] $Path
    )

    $current = $InputObject
    foreach ($name in $Path) {
        if ($null -eq $current) { return $null }
        $property = $current.PSObject.Properties[$name]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }

    return $current
}

function Get-Number {
    param([AllowNull()] $Value)

    if ($null -eq $Value) { return 0.0 }
    return [double]$Value
}

function Get-Delta {
    param([double] $First, [double] $Last)

    return [Math]::Max(0, $Last - $First)
}

function ConvertTo-UtcTimestamp {
    param([AllowNull()] $Value)

    if ($Value -is [DateTimeOffset]) {
        return $Value.ToUniversalTime()
    }

    if ($Value -is [DateTime]) {
        return [DateTimeOffset]$Value.ToUniversalTime()
    }

    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
}

function Get-FirstThresholdTime {
    param([object[]] $Samples, [long] $ThresholdBytes)

    $match = $Samples | Where-Object { (Get-Number (Get-PropertyValue $_ @('cgroup', 'currentBytes'))) -ge $ThresholdBytes } | Select-Object -First 1
    if ($null -eq $match) { return $null }
    return (ConvertTo-UtcTimestamp $match.observedAtUtc).ToString('O')
}

$resolvedTracePaths = @($TracePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$resolvedSerialPaths = @($SerialPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$samples = [System.Collections.Generic.List[object]]::new()
foreach ($tracePath in $resolvedTracePaths) {
    foreach ($line in Get-Content -LiteralPath $tracePath) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $sample = $line | ConvertFrom-Json
            if ($null -ne $sample.observedAtUtc -and $null -ne $sample.process) {
                [void]$samples.Add($sample)
            }
        }
        catch {
            # A truncated final JSONL line is evidence of a crash, not a reason to reject earlier rows.
        }
    }
}

if ($samples.Count -lt 2) {
    throw 'At least two valid diagnostic samples are required to attribute a memory-growth interval.'
}

$ordered = @($samples | Sort-Object { ConvertTo-UtcTimestamp $_.observedAtUtc })
$first = $ordered[0]
$last = $ordered[-1]
$firstAt = ConvertTo-UtcTimestamp $first.observedAtUtc
$lastAt = ConvertTo-UtcTimestamp $last.observedAtUtc
$durationMinutes = [Math]::Max(0.0001, ($lastAt - $firstAt).TotalMinutes)

$workingSetDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('process', 'workingSetBytes'))) (Get-Number (Get-PropertyValue $last @('process', 'workingSetBytes')))
$cgroupDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('cgroup', 'currentBytes'))) (Get-Number (Get-PropertyValue $last @('cgroup', 'currentBytes')))
$materialGrowth = [Math]::Max($workingSetDelta, $cgroupDelta)
$managedDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('process', 'managedRuntime', 'totalCommittedBytes'))) (Get-Number (Get-PropertyValue $last @('process', 'managedRuntime', 'totalCommittedBytes')))
$nativeDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('process', 'linux', 'privateDirtyBytes'))) (Get-Number (Get-PropertyValue $last @('process', 'linux', 'privateDirtyBytes')))
$fileDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('cgroup', 'memoryStat', 'file'))) (Get-Number (Get-PropertyValue $last @('cgroup', 'memoryStat', 'file')))
$threadDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('process', 'linux', 'stackBytes'))) (Get-Number (Get-PropertyValue $last @('process', 'linux', 'stackBytes')))
$sqliteDelta = Get-Delta (Get-Number (Get-PropertyValue $first @('sqlite', 'allocatorCurrentBytes'))) (Get-Number (Get-PropertyValue $last @('sqlite', 'allocatorCurrentBytes')))
$hostAvailableDrop = Get-Delta (Get-Number (Get-PropertyValue $last @('host', 'availableBytes'))) (Get-Number (Get-PropertyValue $first @('host', 'availableBytes')))
$processCountGrowth = Get-Delta (Get-Number (Get-PropertyValue $first @('host', 'processCount'))) (Get-Number (Get-PropertyValue $last @('host', 'processCount')))
$externalPssDelta = Get-Delta (
    (@(Get-PropertyValue $first @('host', 'topProcesses')) | ForEach-Object { Get-Number $_.pssBytes } | Measure-Object -Sum).Sum) (
    (@(Get-PropertyValue $last @('host', 'topProcesses')) | ForEach-Object { Get-Number $_.pssBytes } | Measure-Object -Sum).Sum)

$remaining = $materialGrowth
$assigned = [ordered]@{}
foreach ($category in @(
    @{ Name = 'managed'; Value = $managedDelta },
    @{ Name = 'native'; Value = $nativeDelta },
    @{ Name = 'fileCache'; Value = $fileDelta },
    @{ Name = 'threadStacks'; Value = $threadDelta },
    @{ Name = 'sqlite'; Value = $sqliteDelta })) {
    $allocation = [Math]::Min($remaining, [double]$category.Value)
    $assigned[$category.Name] = [Math]::Round($allocation, 0)
    $remaining -= $allocation
}
$explainedPercent = if ($materialGrowth -le 0) { 100.0 } else { [Math]::Round((($materialGrowth - $remaining) / $materialGrowth) * 100, 2) }

$classification = 'Inconclusive'
if ($workingSetDelta -lt (8MB) -and $hostAvailableDrop -ge (64MB) -and $processCountGrowth -ge 4) {
    $classification = 'ExternalHostPressure'
}
elseif ($materialGrowth -gt 0 -and $managedDelta / $materialGrowth -ge 0.60) {
    $classification = 'ManagedRetention'
}
elseif ($materialGrowth -gt 0 -and $nativeDelta / $materialGrowth -ge 0.60) {
    $classification = 'NativeRuntime'
}
elseif ($materialGrowth -gt 0 -and $fileDelta / $materialGrowth -ge 0.60) {
    $classification = 'FileCache'
}
elseif ($materialGrowth -gt 0 -and $threadDelta / $materialGrowth -ge 0.60) {
    $classification = 'Threading'
}
elseif ($materialGrowth -gt 0 -and $sqliteDelta / $materialGrowth -ge 0.60) {
    $classification = 'Sqlite'
}
elseif ($materialGrowth -le (8MB) -and (Get-Number (Get-PropertyValue $last @('process', 'managedRuntime', 'allocationRateBytesPerSecond'))) -gt 0) {
    $classification = 'ManagedChurn'
}

$operationNames = @(
    $ordered |
        ForEach-Object { Get-PropertyValue $_ @('operations', 'recentCheckpoints') } |
        Where-Object { $null -ne $_ } |
        ForEach-Object { $_ } |
        ForEach-Object { Get-PropertyValue $_ @('operation') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)

$serialIndicators = [ordered]@{ chartOutOfMemoryMentions = 0; kernelOomMentions = 0 }
foreach ($serialPath in $resolvedSerialPaths) {
    foreach ($line in Get-Content -LiteralPath $serialPath) {
        if ($line -match 'System\.OutOfMemoryException') { $serialIndicators.chartOutOfMemoryMentions++ }
        if ($line -match '(?i)Killed process|Out of memory') { $serialIndicators.kernelOomMentions++ }
    }
}

$timeline = foreach ($sample in $ordered) {
    [pscustomobject]@{
        observedAtUtc = (ConvertTo-UtcTimestamp $sample.observedAtUtc).ToString('O')
        sequence = Get-Number $sample.sequence
        cgroupCurrentBytes = Get-Number (Get-PropertyValue $sample @('cgroup', 'currentBytes'))
        workingSetBytes = Get-Number (Get-PropertyValue $sample @('process', 'workingSetBytes'))
        pssBytes = Get-Number (Get-PropertyValue $sample @('process', 'linux', 'pssBytes'))
        managedCommittedBytes = Get-Number (Get-PropertyValue $sample @('process', 'managedRuntime', 'totalCommittedBytes'))
        privateDirtyBytes = Get-Number (Get-PropertyValue $sample @('process', 'linux', 'privateDirtyBytes'))
        cgroupFileBytes = Get-Number (Get-PropertyValue $sample @('cgroup', 'memoryStat', 'file'))
        hostAvailableBytes = Get-Number (Get-PropertyValue $sample @('host', 'availableBytes'))
        hostProcessCount = Get-Number (Get-PropertyValue $sample @('host', 'processCount'))
        activeOperations = @(
            (Get-PropertyValue $sample @('operations', 'activeOperations')) |
                Where-Object { $null -ne $_ } |
                ForEach-Object { Get-PropertyValue $_ @('operation') } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        ) -join ';'
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$timeline | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'timeline.csv') -NoTypeInformation
$sources = @(@($resolvedTracePaths + $resolvedSerialPaths) | Sort-Object -Unique)
$sourceHashes = foreach ($source in $sources) {
    $hash = Get-FileHash -LiteralPath $source -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $source
}
$sourceHashes | Set-Content -LiteralPath (Join-Path $OutputDirectory 'sources.sha256')

$summary = [ordered]@{
    classification = $classification
    classificationIsConclusive = ($explainedPercent -ge 95 -and $classification -ne 'Inconclusive')
    sourceCount = $sources.Count
    firstObservedAtUtc = $firstAt.ToString('O')
    lastObservedAtUtc = $lastAt.ToString('O')
    durationMinutes = [Math]::Round($durationMinutes, 3)
    timeToThresholdUtc = [ordered]@{
        '256MiB' = Get-FirstThresholdTime $ordered (256MB)
        '320MiB' = Get-FirstThresholdTime $ordered (320MB)
        '384MiB' = Get-FirstThresholdTime $ordered (384MB)
    }
    growth = [ordered]@{
        materialBytes = [Math]::Round($materialGrowth, 0)
        workingSetBytes = [Math]::Round($workingSetDelta, 0)
        cgroupBytes = [Math]::Round($cgroupDelta, 0)
        cgroupSlopeBytesPerMinute = [Math]::Round($cgroupDelta / $durationMinutes, 2)
    }
    assignedGrowthBytes = $assigned
    unexplainedGrowthBytes = [Math]::Round($remaining, 0)
    explainedPercent = $explainedPercent
    host = [ordered]@{
        availableMemoryDropBytes = [Math]::Round($hostAvailableDrop, 0)
        processCountGrowth = $processCountGrowth
        topProcessPssGrowthBytes = [Math]::Round($externalPssDelta, 0)
    }
    operationNames = $operationNames
    serialIndicators = $serialIndicators
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')

$report = @"
# Worker memory incident report

Classification: **$classification**

- Evidence coverage: $explainedPercent% of material worker growth; $([Math]::Round($remaining / 1MB, 2)) MiB remains unexplained.
- Interval: $($firstAt.ToString('O')) to $($lastAt.ToString('O')) ($([Math]::Round($durationMinutes, 2)) minutes).
- Cgroup growth slope: $([Math]::Round(($cgroupDelta / 1MB) / $durationMinutes, 2)) MiB/minute.
- Worker growth: $([Math]::Round($materialGrowth / 1MB, 2)) MiB (cgroup $([Math]::Round($cgroupDelta / 1MB, 2)) MiB; RSS $([Math]::Round($workingSetDelta / 1MB, 2)) MiB).
- Assigned categories: managed $([Math]::Round($assigned.managed / 1MB, 2)) MiB, native $([Math]::Round($assigned.native / 1MB, 2)) MiB, file/cache $([Math]::Round($assigned.fileCache / 1MB, 2)) MiB, thread stacks $([Math]::Round($assigned.threadStacks / 1MB, 2)) MiB, SQLite $([Math]::Round($assigned.sqlite / 1MB, 2)) MiB.
- Host: available-memory drop $([Math]::Round($hostAvailableDrop / 1MB, 2)) MiB; process-count growth $processCountGrowth; top-process PSS growth $([Math]::Round($externalPssDelta / 1MB, 2)) MiB.
- Operation markers: $(if ($operationNames.Count) { $operationNames -join ', ' } else { 'none captured' }).
- Serial indicators: chart OOM $($serialIndicators.chartOutOfMemoryMentions); kernel OOM $($serialIndicators.kernelOomMentions).

The analyzer does not select a root cause when evidence coverage is below 95% or signals conflict. See `timeline.csv`, `summary.json`, and `sources.sha256` for the deterministic inputs and calculations.
"@
$report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'REPORT.md')

Write-Output "Worker memory incident analysis written to $(Resolve-Path -LiteralPath $OutputDirectory)"
