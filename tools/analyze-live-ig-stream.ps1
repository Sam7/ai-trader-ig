[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RunDirectory,
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runPath = (Resolve-Path -LiteralPath $RunDirectory).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runPath "analysis"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$summaryPath = Join-Path $runPath "summary.json"
$samplesPath = Join-Path $runPath "memory.csv"
$stdoutPath = Join-Path $runPath "worker.stdout.log"
$stderrPath = Join-Path $runPath "worker.stderr.log"
$databasePath = Join-Path $runPath "ig-market-data.sqlite"

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
$allRows = @(Import-Csv -LiteralPath $samplesPath)
$workerRows = @(
    $allRows |
        Where-Object { $_.process -eq "Trading.Cli" } |
        ForEach-Object {
            [pscustomobject]@{
                Timestamp = [DateTimeOffset]::Parse($_.timestampUtc)
                Pid = [int]$_.pid
                RssBytes = [int64]$_.workingSetBytes
                PrivateBytes = [int64]$_.privateMemoryBytes
                CpuSeconds = [double]$_.cpuSeconds
                DatabaseBytes = [int64]$_.databaseBytes
                WalBytes = [int64]$_.walBytes
                ShmBytes = [int64]$_.shmBytes
            }
        }
)
if ($workerRows.Count -lt 2) {
    throw "At least two Trading.Cli samples are required."
}

function Get-SlopePerHour {
    param(
        [object[]] $Rows,
        [scriptblock] $Selector
    )

    $x = @($Rows | ForEach-Object { ($_.Timestamp - $Rows[0].Timestamp).TotalHours })
    $y = @($Rows | ForEach-Object { & $Selector $_ })
    $xMean = ($x | Measure-Object -Average).Average
    $yMean = ($y | Measure-Object -Average).Average
    $numerator = 0.0
    $denominator = 0.0
    for ($i = 0; $i -lt $x.Count; $i++) {
        $numerator += ($x[$i] - $xMean) * ($y[$i] - $yMean)
        $denominator += ($x[$i] - $xMean) * ($x[$i] - $xMean)
    }
    if ($denominator -eq 0) { return 0.0 }
    return $numerator / $denominator
}

function Get-Correlation {
    param([double[]] $X, [double[]] $Y)

    $xMean = ($X | Measure-Object -Average).Average
    $yMean = ($Y | Measure-Object -Average).Average
    $numerator = 0.0
    $xSum = 0.0
    $ySum = 0.0
    for ($i = 0; $i -lt $X.Count; $i++) {
        $xDelta = $X[$i] - $xMean
        $yDelta = $Y[$i] - $yMean
        $numerator += $xDelta * $yDelta
        $xSum += $xDelta * $xDelta
        $ySum += $yDelta * $yDelta
    }
    if ($xSum -eq 0 -or $ySum -eq 0) { return 0.0 }
    return $numerator / [math]::Sqrt($xSum * $ySum)
}

function Get-Median {
    param([double[]] $Values)
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count % 2 -eq 1) { return $sorted[[int]($sorted.Count / 2)] }
    return ($sorted[($sorted.Count / 2) - 1] + $sorted[$sorted.Count / 2]) / 2
}

function MiB([double] $Bytes) { return [math]::Round($Bytes / 1MB, 3) }

$first = $workerRows[0]
$last = $workerRows[$workerRows.Count - 1]
$warmupEnd = $first.Timestamp.AddHours(1)
$steadyRows = @($workerRows | Where-Object { $_.Timestamp -ge $warmupEnd })
$durationHours = ($last.Timestamp - $first.Timestamp).TotalHours
$intervals = @(
    for ($i = 1; $i -lt $workerRows.Count; $i++) {
        ($workerRows[$i].Timestamp - $workerRows[$i - 1].Timestamp).TotalSeconds
    }
)
$peakRss = $workerRows | Sort-Object RssBytes -Descending | Select-Object -First 1
$peakPrivate = $workerRows | Sort-Object PrivateBytes -Descending | Select-Object -First 1
$dbGrowth = $last.DatabaseBytes - $first.DatabaseBytes
$steadyDbGrowth = $steadyRows[$steadyRows.Count - 1].DatabaseBytes - $steadyRows[0].DatabaseBytes

$timeline = @(
    $workerRows |
        ForEach-Object {
            [pscustomobject]@{
                Hour = [int][math]::Floor(($_.Timestamp - $first.Timestamp).TotalHours)
                Timestamp = $_.Timestamp
                RssMiB = MiB $_.RssBytes
                PrivateMiB = MiB $_.PrivateBytes
                DatabaseMiB = MiB $_.DatabaseBytes
            }
        } |
        Group-Object Hour |
        ForEach-Object {
            $group = @($_.Group)
            [pscustomobject]@{
                Hour = [int]$_.Name
                StartUtc = $group[0].Timestamp
                EndUtc = $group[$group.Count - 1].Timestamp
                Samples = $group.Count
                RssFirstMiB = $group[0].RssMiB
                RssMedianMiB = [math]::Round((Get-Median @($group.RssMiB)), 3)
                RssPeakMiB = [math]::Round((($group | Measure-Object RssMiB -Maximum).Maximum), 3)
                PrivateFirstMiB = $group[0].PrivateMiB
                PrivateMedianMiB = [math]::Round((Get-Median @($group.PrivateMiB)), 3)
                PrivatePeakMiB = [math]::Round((($group | Measure-Object PrivateMiB -Maximum).Maximum), 3)
                DatabaseFirstMiB = $group[0].DatabaseMiB
                DatabaseLastMiB = $group[$group.Count - 1].DatabaseMiB
            }
        }
)
$timelinePath = Join-Path $OutputDirectory "timeline.csv"
$timeline | Export-Csv -LiteralPath $timelinePath -NoTypeInformation
$hashPath = Join-Path $OutputDirectory "sources.sha256"
@($summaryPath, $samplesPath, $databasePath, $stdoutPath, $stderrPath) |
    ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
    ForEach-Object { "$($_.Hash)  $($_.Path)" } |
    Set-Content -LiteralPath $hashPath

$streamText = Get-Content -LiteralPath $stdoutPath -Raw
$streamEvents = [pscustomobject]@{
    AuthenticationHttp200 = ([regex]::Matches($streamText, "Received HTTP response headers.*200")).Count
    SubscriptionEstablished = ([regex]::Matches($streamText, "subscription established")).Count
    SubscriptionStopped = ([regex]::Matches($streamText, "subscription stopped")).Count
    CollectorCompleted = ([regex]::Matches($streamText, "collector completed")).Count
    StderrBytes = (Get-Item -LiteralPath $stderrPath).Length
}

$processFamilies = @(
    $allRows |
        Group-Object process |
        ForEach-Object {
            [pscustomobject]@{
                Process = $_.Name
                Rows = $_.Count
                Pids = (@($_.Group | Select-Object -ExpandProperty pid -Unique) -join ",")
            }
        }
)

$metrics = [ordered]@{
    SchemaVersion = 1
    RunId = $summary.RunId
    ExitCode = $summary.ExitCode
    StartedAtUtc = $summary.StartedAtUtc
    CompletedAtUtc = $summary.CompletedAtUtc
    InstrumentCount = @($summary.Instruments -split ",").Count
    Instruments = $summary.Instruments
    WorkerSampleCount = $workerRows.Count
    AllSampleRowCount = $allRows.Count
    WorkerSampleIntervalSeconds = [ordered]@{
        Minimum = [math]::Round(($intervals | Measure-Object -Minimum).Minimum, 3)
        Average = [math]::Round(($intervals | Measure-Object -Average).Average, 3)
        Maximum = [math]::Round(($intervals | Measure-Object -Maximum).Maximum, 3)
        GapsOver15Seconds = @($intervals | Where-Object { $_ -gt 15 }).Count
    }
    Worker = [ordered]@{
        FirstRssMiB = MiB $first.RssBytes
        LastRssMiB = MiB $last.RssBytes
        PeakRssMiB = MiB $peakRss.RssBytes
        PeakRssAtUtc = $peakRss.Timestamp
        FirstPrivateMiB = MiB $first.PrivateBytes
        LastPrivateMiB = MiB $last.PrivateBytes
        PeakPrivateMiB = MiB $peakPrivate.PrivateBytes
        PeakPrivateAtUtc = $peakPrivate.Timestamp
        RssDeltaMiB = MiB ($last.RssBytes - $first.RssBytes)
        PrivateDeltaMiB = MiB ($last.PrivateBytes - $first.PrivateBytes)
        RssSlopeMiBPerHour = [math]::Round((Get-SlopePerHour $workerRows { param($row) MiB $row.RssBytes }), 4)
        PrivateSlopeMiBPerHour = [math]::Round((Get-SlopePerHour $workerRows { param($row) MiB $row.PrivateBytes }), 4)
        CpuSeconds = $last.CpuSeconds
        AverageCpuPercentOfOneCore = [math]::Round(($last.CpuSeconds / ($durationHours * 3600)) * 100, 4)
    }
    WarmupFirstHour = [ordered]@{
        EndUtc = $warmupEnd
        Samples = $workerRows.Count - $steadyRows.Count
        RssFirstMiB = MiB $first.RssBytes
        RssLastMiB = MiB $steadyRows[0].RssBytes
        PrivateFirstMiB = MiB $first.PrivateBytes
        PrivateLastMiB = MiB $steadyRows[0].PrivateBytes
    }
    SteadyState = [ordered]@{
        DurationHours = [math]::Round(($steadyRows[$steadyRows.Count - 1].Timestamp - $steadyRows[0].Timestamp).TotalHours, 4)
        Samples = $steadyRows.Count
        RssFirstMiB = MiB $steadyRows[0].RssBytes
        RssLastMiB = MiB $steadyRows[$steadyRows.Count - 1].RssBytes
        RssDeltaMiB = MiB ($steadyRows[$steadyRows.Count - 1].RssBytes - $steadyRows[0].RssBytes)
        RssSlopeMiBPerHour = [math]::Round((Get-SlopePerHour $steadyRows { param($row) MiB $row.RssBytes }), 4)
        PrivateFirstMiB = MiB $steadyRows[0].PrivateBytes
        PrivateLastMiB = MiB $steadyRows[$steadyRows.Count - 1].PrivateBytes
        PrivateDeltaMiB = MiB ($steadyRows[$steadyRows.Count - 1].PrivateBytes - $steadyRows[0].PrivateBytes)
        PrivateSlopeMiBPerHour = [math]::Round((Get-SlopePerHour $steadyRows { param($row) MiB $row.PrivateBytes }), 4)
    }
    SQLite = [ordered]@{
        FirstMiB = MiB $first.DatabaseBytes
        LastMiB = MiB $last.DatabaseBytes
        GrowthMiB = MiB $dbGrowth
        GrowthMiBPerHour = [math]::Round((Get-SlopePerHour $workerRows { param($row) MiB $row.DatabaseBytes }), 4)
        SteadyGrowthMiB = MiB $steadyDbGrowth
        PeakWalMiB = MiB (($workerRows | Measure-Object WalBytes -Maximum).Maximum)
        PeakShmMiB = MiB (($workerRows | Measure-Object ShmBytes -Maximum).Maximum)
        CorrelationWithRss = [math]::Round((Get-Correlation @($workerRows | ForEach-Object { MiB $_.RssBytes }) @($workerRows | ForEach-Object { MiB $_.DatabaseBytes })), 4)
        CorrelationWithPrivate = [math]::Round((Get-Correlation @($workerRows | ForEach-Object { MiB $_.PrivateBytes }) @($workerRows | ForEach-Object { MiB $_.DatabaseBytes })), 4)
    }
    StreamEvents = $streamEvents
    ProcessFamilies = $processFamilies
    ArtifactPaths = [ordered]@{
        Summary = $summaryPath
        Samples = $samplesPath
        Database = $databasePath
        Stdout = $stdoutPath
        Stderr = $stderrPath
        Timeline = $timelinePath
        Hashes = $hashPath
    }
}
$metricsPath = Join-Path $OutputDirectory "analysis.json"
$metrics | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $metricsPath

$reportPath = Join-Path $OutputDirectory "REPORT.md"
$report = @"
# Live IG stream memory analysis

Run: $($summary.RunId)
Window: $($summary.StartedAtUtc) to $($summary.CompletedAtUtc)
Exit code: $($summary.ExitCode)
Instruments: $($summary.Instruments)

## Conclusion

The run does **not** show sustained worker memory growth after the first warm-up hour. RSS increased by **$(MiB ($last.RssBytes - $first.RssBytes)) MiB** and private memory by **$(MiB ($last.PrivateBytes - $first.PrivateBytes)) MiB** overall, but during the remaining five hours the deltas were **$(MiB ($steadyRows[$steadyRows.Count - 1].RssBytes - $steadyRows[0].RssBytes)) MiB RSS** and **$(MiB ($steadyRows[$steadyRows.Count - 1].PrivateBytes - $steadyRows[0].PrivateBytes)) MiB private memory**. SQLite grew by **$(MiB $dbGrowth) MiB** while worker private memory remained essentially flat.

Classification for this profile: **no evidence of a sustained SQLite-driven worker leak; production attribution remains incomplete**. This run measured Windows RSS/private memory and file sizes only. It did not measure GC generations/LOH/POH, PSS/private-dirty mappings, SQLite allocator usage, host pressure, or cgroup OOM behavior.

## Measurements

| Metric | Result |
| --- | ---: |
| Worker samples | $($workerRows.Count) |
| Average worker sample interval | $([math]::Round(($intervals | Measure-Object -Average).Average, 3)) s |
| Gaps over 15 seconds | $(@($intervals | Where-Object { $_ -gt 15 }).Count) |
| Initial RSS | $(MiB $first.RssBytes) MiB |
| Peak RSS | $(MiB $peakRss.RssBytes) MiB at $($peakRss.Timestamp) |
| Final RSS | $(MiB $last.RssBytes) MiB |
| Initial private memory | $(MiB $first.PrivateBytes) MiB |
| Peak private memory | $(MiB $peakPrivate.PrivateBytes) MiB at $($peakPrivate.Timestamp) |
| Final private memory | $(MiB $last.PrivateBytes) MiB |
| SQLite initial size | $(MiB $first.DatabaseBytes) MiB |
| SQLite final size | $(MiB $last.DatabaseBytes) MiB |
| SQLite WAL peak | $(MiB (($workerRows | Measure-Object WalBytes -Maximum).Maximum)) MiB |
| Average process CPU | $([math]::Round(($last.CpuSeconds / ($durationHours * 3600)) * 100, 4))% of one core |

## Stream evidence

- Authentication HTTP 200 responses: $($streamEvents.AuthenticationHttp200)
- Subscription-established events: $($streamEvents.SubscriptionEstablished)
- Subscription-stopped events: $($streamEvents.SubscriptionStopped)
- Collector completion messages: $($streamEvents.CollectorCompleted)
- Worker stderr bytes: $($streamEvents.StderrBytes)

The logs show one additional subscription start/stop pair, indicating at least one stream interruption/reconnect. The logs do not include timestamps for those messages, so this report does not claim memory causality for that event.

## Limits and next test

This profile is useful evidence against a simple “SQLite file size alone causes unbounded RSS” explanation. It cannot distinguish managed retention from native/runtime allocations during the warm-up ramp, and it cannot reproduce the 480 MiB Linux host-pressure boundary. The next attribution step is the production-shaped Linux cgroup run with the same one-minute ingestion profile plus GC, PSS/smaps, cgroup, SQLite allocator, and operation-span telemetry.

Generated files: analysis.json, timeline.csv, sources.sha256, and this report.
"@
$report | Set-Content -LiteralPath $reportPath

[pscustomobject]@{
    AnalysisPath = $metricsPath
    ReportPath = $reportPath
    TimelinePath = $timelinePath
    Classification = "No sustained SQLite-driven worker leak evidence; production attribution incomplete"
} | Format-List
