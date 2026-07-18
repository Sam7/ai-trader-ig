[CmdletBinding()]
param(
    [ValidateRange(60, 86400)]
    [int] $DurationSeconds = 1800,
    [string] $Instruments = "CS.D.CFAGOLD.CFA.IP,CC.D.CL.UMA.IP,CC.D.NG.UMA.IP,CS.D.EURUSD.CFD.IP",
    [ValidateRange(1, 60)]
    [int] $SampleSeconds = 5,
    [ValidateSet("Minute", "FiveMinutes")]
    [string] $Resolution = "Minute"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$settingsPath = Join-Path $repoRoot "appsettings.json"
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "The local IG configuration file is missing: $settingsPath"
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.Ig.BaseUrl -ne "https://demo-api.ig.com/gateway/deal") {
    throw "Refusing to run: configured IG endpoint is not the demo endpoint."
}

$runId = "live-ig-stream-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
$outputDirectory = Join-Path $repoRoot "artifacts\$runId"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$databasePath = Join-Path $outputDirectory "ig-market-data.sqlite"
$stdoutPath = Join-Path $outputDirectory "worker.stdout.log"
$stderrPath = Join-Path $outputDirectory "worker.stderr.log"
$samplesPath = Join-Path $outputDirectory "memory.csv"
$summaryPath = Join-Path $outputDirectory "summary.json"
$duration = [TimeSpan]::FromSeconds($DurationSeconds).ToString('c')

$env:MarketData__StorePath = $databasePath
$env:MarketData__Collector__Resolution = $Resolution
$arguments = @(
    "run",
    "--project", (Join-Path $repoRoot "src\Trading.Cli\Trading.Cli.csproj"),
    "--configuration", "Release",
    "--no-restore",
    "--",
    "marketdata", "collect",
    "--instruments", $Instruments,
    "--duration", $duration
)

$worker = Start-Process -FilePath "dotnet" -WorkingDirectory $repoRoot -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -ArgumentList $arguments
"timestampUtc,pid,process,workingSetBytes,privateMemoryBytes,cpuSeconds,databaseBytes,walBytes,shmBytes" | Set-Content -LiteralPath $samplesPath

function Get-ProcessTreeIds {
    param([int] $RootId)

    $allProcesses = @(Get-CimInstance Win32_Process)
    $childrenByParent = @{}
    foreach ($entry in $allProcesses) {
        $parentId = [int]$entry.ParentProcessId
        if (-not $childrenByParent.ContainsKey($parentId)) {
            $childrenByParent[$parentId] = [System.Collections.Generic.List[int]]::new()
        }
        $childrenByParent[$parentId].Add([int]$entry.ProcessId)
    }

    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $pending = [System.Collections.Generic.Queue[int]]::new()
    $seen.Add($RootId) | Out-Null
    $pending.Enqueue($RootId)
    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        if (-not $childrenByParent.ContainsKey($parentId)) {
            continue
        }
        foreach ($childId in $childrenByParent[$parentId]) {
            if ($seen.Add($childId)) {
                $pending.Enqueue($childId)
            }
        }
    }
    return @($seen)
}

function Get-FileLengthOrZero {
    param([string] $Path)

    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return [int64]0
    }
    return [int64]$item.Length
}

while ($true) {
    $processIds = Get-ProcessTreeIds -RootId $worker.Id
    $processes = @($processIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    $processes = $processes | Where-Object { $null -ne $_ } | Sort-Object Id -Unique
    foreach ($process in $processes) {
        $walPath = "$databasePath-wal"
        $shmPath = "$databasePath-shm"
        $databaseBytes = Get-FileLengthOrZero -Path $databasePath
        $walBytes = Get-FileLengthOrZero -Path $walPath
        $shmBytes = Get-FileLengthOrZero -Path $shmPath
        "{0:o},{1},{2},{3},{4},{5},{6},{7},{8}" -f [DateTimeOffset]::UtcNow, $process.Id, $process.ProcessName, $process.WorkingSet64, $process.PrivateMemorySize64, [math]::Round($process.TotalProcessorTime.TotalSeconds, 3), $databaseBytes, $walBytes, $shmBytes | Add-Content -LiteralPath $samplesPath
    }

    if ($worker.HasExited) {
        break
    }
    Start-Sleep -Seconds $SampleSeconds
}

$exitCode = $worker.ExitCode
[pscustomobject]@{
    RunId = $runId
    ExitCode = $exitCode
    StartedAtUtc = $worker.StartTime.ToUniversalTime()
    CompletedAtUtc = [DateTime]::UtcNow
    DurationSeconds = $DurationSeconds
    Instruments = $Instruments
    DatabasePath = $databasePath
    SamplesPath = $samplesPath
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
} | ConvertTo-Json | Set-Content -LiteralPath $summaryPath

[pscustomobject]@{
    RunId = $runId
    ExitCode = $exitCode
    OutputDirectory = $outputDirectory
    SamplesPath = $samplesPath
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
} | Format-List

if ($exitCode -ne 0) {
    throw "The live IG stream worker exited with code $exitCode. Inspect $stderrPath."
}
