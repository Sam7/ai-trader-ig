[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $RunDirectory,

    [ValidateRange(128, 2048)]
    [int] $ProductionLimitMiB = 480,

    [ValidateRange(128, 2048)]
    [int] $HardLimitMiB = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PropertyValue {
    param(
        [AllowNull()] $InputObject,
        [Parameter(Mandatory)][string[]] $Path
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
    $result = 0.0
    if ([double]::TryParse([string]$Value, [ref]$result)) {
        return $result
    }

    return 0.0
}

function Get-NullableNumber {
    param([AllowNull()] $Value)

    if ($null -eq $Value) { return $null }
    $result = 0.0
    if ([double]::TryParse([string]$Value, [ref]$result)) {
        return $result
    }

    return $null
}

function ConvertTo-UtcTimestamp {
    param([AllowNull()] $Value)

    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) { return ([DateTimeOffset]$Value).ToUniversalTime() }
    try {
        return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
    }
    catch {
        return $null
    }
}

function Get-FirstThresholdSample {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Samples,
        [Parameter(Mandatory)][long] $ThresholdBytes
    )

    return $Samples | Where-Object {
        (Get-Number (Get-PropertyValue $_ @("cgroup", "currentBytes"))) -ge $ThresholdBytes
    } | Select-Object -First 1
}

function Get-MaximumPathValue {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Samples,
        [Parameter(Mandatory)][string[]] $Path
    )

    $maximum = $null
    foreach ($sample in $Samples) {
        $value = Get-NullableNumber (Get-PropertyValue $sample $Path)
        if ($null -ne $value -and ($null -eq $maximum -or $value -gt $maximum)) {
            $maximum = $value
        }
    }

    return $maximum
}

function Get-MiB {
    param([AllowNull()] $Bytes)

    if ($null -eq $Bytes) { return $null }
    return [Math]::Round(([double]$Bytes) / 1MB, 2)
}

function Get-SafePromptNumber {
    param(
        [AllowNull()] $Record,
        [string[]] $Paths
    )

    foreach ($path in $Paths) {
        $parts = $path -split "/"
        $value = Get-NullableNumber (Get-PropertyValue $Record $parts)
        if ($null -ne $value) { return $value }
    }

    return $null
}

function Get-DurationMilliseconds {
    param([AllowNull()] $Value)

    if ($null -eq $Value) { return 0.0 }
    if ($null -ne $Value.PSObject.Properties["TotalMilliseconds"]) {
        return Get-Number $Value.TotalMilliseconds
    }

    $duration = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse([string]$Value, [ref]$duration)) {
        return [Math]::Round($duration.TotalMilliseconds, 3)
    }

    return 0.0
}

function Get-NearestSampleMemory {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Samples,
        [AllowNull()][DateTimeOffset] $Timestamp
    )

    if ($null -eq $Timestamp -or $Samples.Count -eq 0) { return $null }

    $nearest = $null
    $nearestDistanceSeconds = [double]::PositiveInfinity
    foreach ($sample in $Samples) {
        $sampleTimestamp = ConvertTo-UtcTimestamp $sample.observedAtUtc
        if ($null -eq $sampleTimestamp) { continue }
        $distance = [Math]::Abs(($sampleTimestamp - $Timestamp).TotalSeconds)
        if ($distance -lt $nearestDistanceSeconds) {
            $nearest = $sample
            $nearestDistanceSeconds = $distance
        }
    }

    if ($null -eq $nearest) { return $null }
    return [ordered]@{
        observedAtUtc = (ConvertTo-UtcTimestamp $nearest.observedAtUtc).ToString("O")
        distanceSeconds = [Math]::Round($nearestDistanceSeconds, 3)
        cgroupMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $nearest @("cgroup", "currentBytes")))
        pssMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $nearest @("process", "linux", "pssBytes")))
        managedCommittedMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $nearest @("process", "managedRuntime", "totalCommittedBytes")))
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Value
    )

    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}

$resolvedRunDirectory = (Resolve-Path -LiteralPath $RunDirectory).Path
$traceDirectory = Join-Path $resolvedRunDirectory "trace"
$observabilityDirectory = Join-Path $resolvedRunDirectory "observability"
$analysisDirectory = Join-Path $resolvedRunDirectory "analysis"
New-Item -ItemType Directory -Force -Path $analysisDirectory | Out-Null

$traceFiles = if (Test-Path -LiteralPath $traceDirectory) {
    @(Get-ChildItem -LiteralPath $traceDirectory -File |
        Where-Object { $_.Name -like "*.jsonl*" } |
        Sort-Object FullName)
}
else {
    @()
}

$samples = [System.Collections.Generic.List[object]]::new()
$invalidTraceLines = 0
foreach ($traceFile in $traceFiles) {
    foreach ($line in Get-Content -LiteralPath $traceFile.FullName) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $sample = $line | ConvertFrom-Json
            if ($null -ne $sample.observedAtUtc -and $null -ne $sample.process) {
                [void]$samples.Add($sample)
            }
            else {
                $invalidTraceLines++
            }
        }
        catch {
            # A truncated final JSONL record is expected after a cgroup kill.
            $invalidTraceLines++
        }
    }
}

$orderedSamples = @($samples | Sort-Object { ConvertTo-UtcTimestamp $_.observedAtUtc })
$productionLimitBytes = $ProductionLimitMiB * 1MB
$hardLimitBytes = $HardLimitMiB * 1MB
$baseline = if ($orderedSamples.Count -gt 0) { $orderedSamples[0] } else { $null }
$final = if ($orderedSamples.Count -gt 0) { $orderedSamples[-1] } else { $null }
$peak = $null
$peakCgroupBytes = $null
foreach ($sample in $orderedSamples) {
    $current = Get-NullableNumber (Get-PropertyValue $sample @("cgroup", "currentBytes"))
    if ($null -ne $current -and ($null -eq $peakCgroupBytes -or $current -gt $peakCgroupBytes)) {
        $peak = $sample
        $peakCgroupBytes = $current
    }
}

$firstProductionLimit = Get-FirstThresholdSample -Samples $orderedSamples -ThresholdBytes $productionLimitBytes
$firstHardLimit = Get-FirstThresholdSample -Samples $orderedSamples -ThresholdBytes $hardLimitBytes
$firstAt = if ($null -ne $baseline) { ConvertTo-UtcTimestamp $baseline.observedAtUtc } else { $null }
$peakAt = if ($null -ne $peak) { ConvertTo-UtcTimestamp $peak.observedAtUtc } else { $null }
$finalAt = if ($null -ne $final) { ConvertTo-UtcTimestamp $final.observedAtUtc } else { $null }

$sampleGaps = @()
for ($index = 1; $index -lt $orderedSamples.Count; $index++) {
    $previous = ConvertTo-UtcTimestamp $orderedSamples[$index - 1].observedAtUtc
    $current = ConvertTo-UtcTimestamp $orderedSamples[$index].observedAtUtc
    if ($null -ne $previous -and $null -ne $current) {
        $gapSeconds = ($current - $previous).TotalSeconds
        if ($gapSeconds -gt 15) {
            $sampleGaps += [pscustomobject]@{
                fromUtc = $previous.ToString("O")
                toUtc = $current.ToString("O")
                seconds = [Math]::Round($gapSeconds, 3)
            }
        }
    }
}

$timeline = @(
foreach ($sample in $orderedSamples) {
    [pscustomobject]@{
        observedAtUtc = (ConvertTo-UtcTimestamp $sample.observedAtUtc).ToString("O")
        sequence = Get-Number $sample.sequence
        cgroupMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("cgroup", "currentBytes")))
        cgroupFileMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("cgroup", "memoryStat", "file")))
        rssMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "workingSetBytes")))
        privateMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "privateMemoryBytes")))
        pssMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "linux", "pssBytes")))
        privateDirtyMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "linux", "privateDirtyBytes")))
        managedLiveMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "managedRuntime", "liveBytes")))
        managedCommittedMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "managedRuntime", "totalCommittedBytes")))
        lohMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "managedRuntime", "largeObjectHeap", "sizeAfterBytes")))
        pohMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("process", "managedRuntime", "pinnedObjectHeap", "sizeAfterBytes")))
        allocationRateMiBPerSecond = [Math]::Round((Get-Number (Get-PropertyValue $sample @("process", "managedRuntime", "allocationRateBytesPerSecond"))) / 1MB, 3)
        sqliteAllocatorMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("sqlite", "allocatorCurrentBytes")))
        sqlitePagecacheMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $sample @("sqlite", "pagecacheCurrentBytes")))
        streamDispatcherDepth = Get-Number (Get-PropertyValue $sample @("stream", "dispatcherDepth"))
        streamIngestorDepth = Get-Number (Get-PropertyValue $sample @("stream", "ingestorDepth"))
        streamDropped = Get-Number (Get-PropertyValue $sample @("stream", "droppedUpdates"))
        threadCount = Get-Number (Get-PropertyValue $sample @("process", "threadCount"))
        threadPoolCount = Get-Number (Get-PropertyValue $sample @("process", "managedRuntime", "threadPool", "threadCount"))
        activeOperations = @((Get-PropertyValue $sample @("operations", "activeOperations")) | ForEach-Object { $_.operation } | Where-Object { $_ }) -join ";"
    }
}
)
if ($timeline.Count -gt 0) {
    $timeline | Export-Csv -LiteralPath (Join-Path $analysisDirectory "timeline.csv") -NoTypeInformation
}
else {
    Set-Content -LiteralPath (Join-Path $analysisDirectory "timeline.csv") -Value "observedAtUtc,cgroupMiB"
}

$checkpointSignatures = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$checkpoints = [System.Collections.Generic.List[object]]::new()
foreach ($sample in $orderedSamples) {
    foreach ($checkpoint in @((Get-PropertyValue $sample @("operations", "recentCheckpoints")))) {
        if ($null -eq $checkpoint) { continue }
        $signature = "$($checkpoint.correlationId)|$($checkpoint.outcome)|$($checkpoint.completedAtUtc)"
        if (-not $checkpointSignatures.Add($signature)) { continue }
        $checkpointStartedAt = ConvertTo-UtcTimestamp $checkpoint.startedAtUtc
        $checkpointCompletedAt = ConvertTo-UtcTimestamp $checkpoint.completedAtUtc
        $beforeMemory = Get-PropertyValue $checkpoint @("beforeMemory")
        $afterMemory = Get-PropertyValue $checkpoint @("afterMemory")
        [void]$checkpoints.Add([ordered]@{
            operation = [string]$checkpoint.operation
            outcome = [string]$checkpoint.outcome
            itemCount = Get-Number (Get-PropertyValue $checkpoint @("itemCount"))
            payloadBytes = Get-Number (Get-PropertyValue $checkpoint @("payloadBytes"))
            startedAtUtc = if ($null -ne $checkpointStartedAt) { $checkpointStartedAt.ToString("O") } else { $null }
            completedAtUtc = if ($null -ne $checkpointCompletedAt) { $checkpointCompletedAt.ToString("O") } else { $null }
            durationMilliseconds = Get-DurationMilliseconds (Get-PropertyValue $checkpoint @("duration"))
            before = [ordered]@{
                cgroupMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $beforeMemory @("cgroupCurrentBytes")))
                pssMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $beforeMemory @("pssBytes")))
                workingSetMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $beforeMemory @("workingSetBytes")))
                managedCommittedMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $beforeMemory @("managedCommittedBytes")))
            }
            after = [ordered]@{
                cgroupMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $afterMemory @("cgroupCurrentBytes")))
                pssMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $afterMemory @("pssBytes")))
                workingSetMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $afterMemory @("workingSetBytes")))
                managedCommittedMiB = Get-MiB (Get-NullableNumber (Get-PropertyValue $afterMemory @("managedCommittedBytes")))
            }
        })
    }
}
Write-JsonFile -Path (Join-Path $analysisDirectory "operation-checkpoints.json") -Value @($checkpoints)

$promptFiles = if (Test-Path -LiteralPath $observabilityDirectory) {
    @(Get-ChildItem -LiteralPath $observabilityDirectory -Recurse -Filter "*.json" -File | Sort-Object FullName)
}
else {
    @()
}
$promptSummaries = [System.Collections.Generic.List[object]]::new()
$invalidPromptRecordCount = 0
foreach ($promptFile in $promptFiles) {
    try {
        $record = Get-Content -Raw -LiteralPath $promptFile.FullName | ConvertFrom-Json
        $promptId = [string](Get-PropertyValue $record @("promptId"))
        $promptName = [string](Get-PropertyValue $record @("promptName"))
        if ([string]::IsNullOrWhiteSpace($promptId) -or [string]::IsNullOrWhiteSpace($promptName)) {
            continue
        }

        $requestedAt = ConvertTo-UtcTimestamp $record.requestedAtUtc
        $completedAt = ConvertTo-UtcTimestamp $record.completedAtUtc
        [void]$promptSummaries.Add([ordered]@{
            promptId = $promptId
            promptName = $promptName
            status = [string](Get-PropertyValue $record @("status"))
            requestedAtUtc = if ($null -ne $requestedAt) { $requestedAt.ToString("O") } else { $null }
            completedAtUtc = if ($null -ne $completedAt) { $completedAt.ToString("O") } else { $null }
            modelId = [string](Get-PropertyValue $record @("modelId"))
            processingMode = [string](Get-PropertyValue $record @("processingMode"))
            durationMilliseconds = Get-SafePromptNumber $record @("durationMs")
            inputTokens = Get-SafePromptNumber $record @("cost/inputTokens", "usage/inputTokenCount")
            outputTokens = Get-SafePromptNumber $record @("cost/outputTokens", "usage/outputTokenCount")
            cachedInputTokens = Get-SafePromptNumber $record @("cost/cachedInputTokens")
            totalCostUsd = Get-SafePromptNumber $record @("cost/totalCostUsd")
            attachmentCount = @((Get-PropertyValue $record @("attachmentArtifactPaths"))).Count
            attemptCount = @((Get-PropertyValue $record @("attempts"))).Count
            nearestRequestedMemory = Get-NearestSampleMemory -Samples $orderedSamples -Timestamp $requestedAt
            nearestCompletedMemory = Get-NearestSampleMemory -Samples $orderedSamples -Timestamp $completedAt
        })
    }
    catch {
        # Prompt payloads must not be emitted from analysis. Count malformed metadata as missing evidence.
        $invalidPromptRecordCount++
    }
}
Write-JsonFile -Path (Join-Path $analysisDirectory "prompt-summary.json") -Value @($promptSummaries)

$baselineCgroup = if ($null -ne $baseline) { Get-Number (Get-PropertyValue $baseline @("cgroup", "currentBytes")) } else { 0 }
$peakCgroup = if ($null -ne $peak) { Get-Number (Get-PropertyValue $peak @("cgroup", "currentBytes")) } else { 0 }
$finalCgroup = if ($null -ne $final) { Get-Number (Get-PropertyValue $final @("cgroup", "currentBytes")) } else { 0 }
$growthToPeak = [Math]::Max(0, $peakCgroup - $baselineCgroup)
$managedDelta = if ($null -ne $peak -and $null -ne $baseline) {
    [Math]::Max(0, (Get-Number (Get-PropertyValue $peak @("process", "managedRuntime", "totalCommittedBytes"))) - (Get-Number (Get-PropertyValue $baseline @("process", "managedRuntime", "totalCommittedBytes"))))
} else { 0 }
$nativeDelta = if ($null -ne $peak -and $null -ne $baseline) {
    [Math]::Max(0, (Get-Number (Get-PropertyValue $peak @("process", "linux", "privateDirtyBytes"))) - (Get-Number (Get-PropertyValue $baseline @("process", "linux", "privateDirtyBytes"))))
} else { 0 }
$fileDelta = if ($null -ne $peak -and $null -ne $baseline) {
    [Math]::Max(0, (Get-Number (Get-PropertyValue $peak @("cgroup", "memoryStat", "file"))) - (Get-Number (Get-PropertyValue $baseline @("cgroup", "memoryStat", "file"))))
} else { 0 }
$sqliteDelta = if ($null -ne $peak -and $null -ne $baseline) {
    [Math]::Max(0, (Get-Number (Get-PropertyValue $peak @("sqlite", "allocatorCurrentBytes"))) - (Get-Number (Get-PropertyValue $baseline @("sqlite", "allocatorCurrentBytes"))))
} else { 0 }

$classification = "InsufficientEvidence"
if ($orderedSamples.Count -ge 2) {
    if ($growthToPeak -le 8MB) { $classification = "StableOrBounded" }
    elseif ($managedDelta / $growthToPeak -ge 0.60) { $classification = "ManagedGrowthCandidate" }
    elseif ($nativeDelta / $growthToPeak -ge 0.60) { $classification = "NativePrivateGrowthCandidate" }
    elseif ($fileDelta / $growthToPeak -ge 0.60) { $classification = "FileCacheGrowthCandidate" }
    elseif ($sqliteDelta / $growthToPeak -ge 0.60) { $classification = "SqliteGrowthCandidate" }
    else { $classification = "MixedOrUnattributedGrowth" }
}

$peakHighEvents = Get-MaximumPathValue -Samples $orderedSamples -Path @("cgroup", "highEvents")
$peakMaxEvents = Get-MaximumPathValue -Samples $orderedSamples -Path @("cgroup", "maxEvents")
$peakOomEvents = Get-MaximumPathValue -Samples $orderedSamples -Path @("cgroup", "oomEvents")
$peakOomKillEvents = Get-MaximumPathValue -Samples $orderedSamples -Path @("cgroup", "oomKillEvents")
$forensicArtifacts = if (Test-Path -LiteralPath $traceDirectory) {
    @(Get-ChildItem -LiteralPath $traceDirectory -Filter "forensic-*.gz" -File)
}
else {
    @()
}
$activeAtProductionLimit = if ($null -ne $firstProductionLimit) {
    @((Get-PropertyValue $firstProductionLimit @("operations", "activeOperations")) |
        ForEach-Object { [string]$_.operation } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
}
else {
    @()
}

$completionPath = Join-Path $resolvedRunDirectory "run-completion.json"
$completion = if (Test-Path -LiteralPath $completionPath) {
    Get-Content -Raw -LiteralPath $completionPath | ConvertFrom-Json
}
else {
    $null
}

$sourceFiles = @(
    @(
        $traceFiles | ForEach-Object { $_.FullName }
        $promptFiles | ForEach-Object { $_.FullName }
    ) | Where-Object { $_ } | Sort-Object -Unique
)
$sourceHashes = foreach ($sourceFile in $sourceFiles) {
    $hash = Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $sourceFile
}
$sourceHashes | Set-Content -LiteralPath (Join-Path $analysisDirectory "sources.sha256")

$traceFileCount = @($traceFiles).Count
$validSampleCount = @($orderedSamples).Count
$sampleGapCount = @($sampleGaps).Count
$forensicArtifactCount = @($forensicArtifacts).Count
$promptRecordCount = @($promptSummaries).Count
$sourceHashCount = @($sourceFiles).Count

$summary = [ordered]@{
    schemaVersion = 1
    evidence = [ordered]@{
        traceFileCount = $traceFileCount
        validSampleCount = $validSampleCount
        invalidOrTruncatedTraceLineCount = $invalidTraceLines
        sampleGapCountOver15Seconds = $sampleGapCount
        largestSampleGapSeconds = if ($sampleGapCount) { ($sampleGaps | Measure-Object -Property seconds -Maximum).Maximum } else { 0 }
        forensicArtifactCount = $forensicArtifactCount
        promptRecordCount = $promptRecordCount
        invalidPromptRecordCount = $invalidPromptRecordCount
        sourceHashCount = $sourceHashCount
    }
    limits = [ordered]@{
        productionMemoryMaxMiB = $ProductionLimitMiB
        labMemoryMaxMiB = $HardLimitMiB
    }
    outcome = [ordered]@{
        productionLimitReached = ($null -ne $firstProductionLimit)
        productionLimitReachedAtUtc = if ($null -ne $firstProductionLimit) { (ConvertTo-UtcTimestamp $firstProductionLimit.observedAtUtc).ToString("O") } else { $null }
        activeOperationsAtProductionLimit = $activeAtProductionLimit
        hardLimitObserved = ($null -ne $firstHardLimit)
        cgroupHighEvents = $peakHighEvents
        cgroupMaxEvents = $peakMaxEvents
        cgroupOomEvents = $peakOomEvents
        cgroupOomKillEvents = $peakOomKillEvents
        workerExitCode = if ($null -ne $completion) { $completion.systemdExitCode } else { $null }
    }
    interval = [ordered]@{
        firstObservedAtUtc = if ($null -ne $firstAt) { $firstAt.ToString("O") } else { $null }
        peakObservedAtUtc = if ($null -ne $peakAt) { $peakAt.ToString("O") } else { $null }
        finalObservedAtUtc = if ($null -ne $finalAt) { $finalAt.ToString("O") } else { $null }
        minutesToPeak = if ($null -ne $firstAt -and $null -ne $peakAt) { [Math]::Round(($peakAt - $firstAt).TotalMinutes, 3) } else { $null }
    }
    memoryMiB = [ordered]@{
        baselineCgroup = Get-MiB $baselineCgroup
        peakCgroup = Get-MiB $peakCgroup
        finalCgroup = Get-MiB $finalCgroup
        peakPss = Get-MiB (Get-MaximumPathValue -Samples $orderedSamples -Path @("process", "linux", "pssBytes"))
        peakRss = Get-MiB (Get-MaximumPathValue -Samples $orderedSamples -Path @("process", "workingSetBytes"))
        peakManagedCommitted = Get-MiB (Get-MaximumPathValue -Samples $orderedSamples -Path @("process", "managedRuntime", "totalCommittedBytes"))
        peakLargeObjectHeap = Get-MiB (Get-MaximumPathValue -Samples $orderedSamples -Path @("process", "managedRuntime", "largeObjectHeap", "sizeAfterBytes"))
        peakPinnedObjectHeap = Get-MiB (Get-MaximumPathValue -Samples $orderedSamples -Path @("process", "managedRuntime", "pinnedObjectHeap", "sizeAfterBytes"))
        growthToPeak = Get-MiB $growthToPeak
        managedCommittedGrowthToPeak = Get-MiB $managedDelta
        nativePrivateDirtyGrowthToPeak = Get-MiB $nativeDelta
        cgroupFileGrowthToPeak = Get-MiB $fileDelta
        sqliteAllocatorGrowthToPeak = Get-MiB $sqliteDelta
        postPeakRelease = Get-MiB ([Math]::Max(0, $peakCgroup - $finalCgroup))
    }
    attribution = [ordered]@{
        classification = $classification
        interpretation = "Heuristic attribution compares baseline-to-peak deltas. It is a lead for inspection, not proof of ownership."
    }
    ai = [ordered]@{
        promptCount = $promptSummaries.Count
        completedPromptCount = @($promptSummaries | Where-Object { $_.status -eq "Completed" }).Count
        submittedPromptCount = @($promptSummaries | Where-Object { $_.status -eq "Submitted" }).Count
        totalInputTokens = [Math]::Round((@($promptSummaries | ForEach-Object { Get-Number $_.inputTokens } | Measure-Object -Sum).Sum), 0)
        totalOutputTokens = [Math]::Round((@($promptSummaries | ForEach-Object { Get-Number $_.outputTokens } | Measure-Object -Sum).Sum), 0)
        totalCostUsd = [Math]::Round((@($promptSummaries | ForEach-Object { Get-Number $_.totalCostUsd } | Measure-Object -Sum).Sum), 6)
    }
    operationNames = @($checkpoints | ForEach-Object { $_.operation } | Where-Object { $_ } | Sort-Object -Unique)
}
Write-JsonFile -Path (Join-Path $analysisDirectory "summary.json") -Value $summary
Write-JsonFile -Path (Join-Path $analysisDirectory "sample-gaps.json") -Value @($sampleGaps)

$productionResult = if ($summary.outcome.productionLimitReached) {
    "YES at $($summary.outcome.productionLimitReachedAtUtc)"
}
else {
    "NO"
}
$hardLimitResult = if ($summary.outcome.cgroupOomKillEvents -gt 0) {
    "OOM-killed"
}
elseif ($summary.outcome.hardLimitObserved -or $summary.outcome.cgroupMaxEvents -gt 0) {
    "hard limit contacted"
}
else {
    "not contacted"
}
$operationText = if ($summary.operationNames.Count) { $summary.operationNames -join ", " } else { "none captured" }
$report = @"
# Combined worker AI memory-lab report

## Outcome

- Production 480 MiB limit reached: **$productionResult**.
- Lab 600 MiB outcome: **$hardLimitResult**.
- Peak cgroup: **$($summary.memoryMiB.peakCgroup) MiB**; peak PSS: **$($summary.memoryMiB.peakPss) MiB**; peak RSS: **$($summary.memoryMiB.peakRss) MiB**.
- Baseline to peak: **$($summary.memoryMiB.growthToPeak) MiB**; memory released after peak: **$($summary.memoryMiB.postPeakRelease) MiB**.
- Attribution lead: **$classification**.

## Evidence quality

- $($summary.evidence.validSampleCount) valid samples from $($summary.evidence.traceFileCount) trace file(s); $($summary.evidence.sampleGapCountOver15Seconds) gap(s) longer than 15 seconds; $($summary.evidence.forensicArtifactCount) threshold forensic artifact(s).
- Cgroup events: high $($summary.outcome.cgroupHighEvents), max $($summary.outcome.cgroupMaxEvents), OOM $($summary.outcome.cgroupOomEvents), OOM-kill $($summary.outcome.cgroupOomKillEvents).
- AI observations: $($summary.ai.promptCount) prompt record(s), $($summary.ai.completedPromptCount) completed, $($summary.ai.totalInputTokens) input token(s), $($summary.ai.totalOutputTokens) output token(s), `$$($summary.ai.totalCostUsd) estimated cost.
- Operation markers: $operationText.

## How to read this result

- If 480 MiB was reached, production's current `MemoryMax=480M` would have terminated this process. Inspect the timestamp, `operation-checkpoints.json`, and `prompt-summary.json` around that moment.
- If peak memory falls materially before shutdown, the run is showing a transient peak or cache warm-up; if it keeps stepping upward after the same operation, treat it as a retention candidate.
- Managed committed/LOH growth that dominates the baseline-to-peak interval points to managed retention or allocation pressure. Private-dirty/PSS growth with flat managed memory points toward native runtime, charting, HTTP, or SQLite allocations. File growth points to page cache or mapped-file pressure.
- This lab intentionally permits stale/incomplete local bars, disables historical backfill/recovery, and disables execution. Its purpose is to exercise the same worker, stream, chart, and OpenAI paths without turning data gaps into a reason not to call the AI.

Raw prompt requests, responses, and provider error text are deliberately excluded from this analysis. The source prompt records remain only in the run-local observability directory.
"@
$report | Set-Content -LiteralPath (Join-Path $analysisDirectory "REPORT.md") -Encoding utf8

Write-Output "Combined worker AI memory analysis written to $analysisDirectory"
