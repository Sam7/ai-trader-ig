[CmdletBinding()]
param(
    [ValidateRange(1, 24)]
    [int] $DurationHours = 8,

    [ValidateRange(1, 30)]
    [int] $InitialDelayMinutes = 4,

    [ValidateSet(15, 30, 60)]
    [int] $IntradayIntervalMinutes = 15,

    [ValidateRange(1, 8760)]
    [int] $ChartLookbackHours = 720,

    [ValidateRange(128, 1024)]
    [int] $MemoryObservationMiB = 480,

    [ValidateRange(128, 2048)]
    [int] $MemoryHighMiB = 560,

    [ValidateRange(128, 2048)]
    [int] $MemoryMaxMiB = 600,

    [string] $Distro = "Ubuntu",

    [string] $SeedDatabasePath,

    [string] $TrackedMarketsPath,

    [switch] $ValidateOnly,

    [switch] $StartupCheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-WslPath {
    param([Parameter(Mandatory)][string] $Path)

    $converted = & wsl.exe -d $Distro -- wslpath -a -u ($Path -replace "\\", "/")
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($converted)) {
        throw "Unable to convert '$Path' to a WSL path for distro '$Distro'."
    }

    return $converted.Trim()
}

function Resolve-ConfiguredMarketDataPath {
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $localSettingsPath = Join-Path $RepositoryRoot "appsettings.local.json"
    if (-not (Test-Path -LiteralPath $localSettingsPath)) {
        throw "No seed database was supplied and $localSettingsPath does not exist. Supply -SeedDatabasePath."
    }

    $settings = Get-Content -Raw -LiteralPath $localSettingsPath | ConvertFrom-Json
    $configuredPath = [string]$settings.MarketData.StorePath
    if ([string]::IsNullOrWhiteSpace($configuredPath)) {
        throw "MarketData:StorePath is required in $localSettingsPath when -SeedDatabasePath is not supplied."
    }

    if ([IO.Path]::IsPathRooted($configuredPath)) {
        return $configuredPath
    }

    return Join-Path $RepositoryRoot $configuredPath
}

function Copy-SqliteSeed {
    param(
        [Parameter(Mandatory)][string] $SourcePath,
        [Parameter(Mandatory)][string] $DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "The market-data seed database was not found: $SourcePath"
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath
    foreach ($suffix in @("-wal", "-shm")) {
        $companion = "$SourcePath$suffix"
        if (Test-Path -LiteralPath $companion -PathType Leaf) {
            Copy-Item -LiteralPath $companion -Destination "$DestinationPath$suffix"
        }
    }
}

function Get-AustralianEasternTime {
    try {
        return [TimeZoneInfo]::FindSystemTimeZoneById("AUS Eastern Standard Time")
    }
    catch [TimeZoneNotFoundException] {
        return [TimeZoneInfo]::Local
    }
}

function Set-RunFile {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Value
    )

    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}

if ($MemoryObservationMiB -ge $MemoryHighMiB) {
    throw "MemoryObservationMiB must be lower than MemoryHighMiB so the lab can observe post-480 MiB behavior before cgroup pressure begins."
}

if ($MemoryHighMiB -ge $MemoryMaxMiB) {
    throw "MemoryHighMiB must be lower than MemoryMaxMiB."
}

foreach ($command in @("dotnet", "wsl.exe", "pwsh")) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command is required for the worker AI memory lab."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appSettingsPath = Join-Path $repoRoot "appsettings.json"
if (-not (Test-Path -LiteralPath $appSettingsPath)) {
    throw "The worker configuration $appSettingsPath was not found."
}

$appSettings = Get-Content -Raw -LiteralPath $appSettingsPath | ConvertFrom-Json
$igBaseUrl = [string]$appSettings.IG.BaseUrl
if ($igBaseUrl -notmatch "(?i)demo") {
    throw "The local lab only supports an IG demo endpoint. Refusing to start with the configured non-demo endpoint."
}

$seedPath = if ([string]::IsNullOrWhiteSpace($SeedDatabasePath)) {
    Resolve-ConfiguredMarketDataPath -RepositoryRoot $repoRoot
}
else {
    $SeedDatabasePath
}
$seedPath = (Resolve-Path -LiteralPath $seedPath).Path

$trackedPath = if ([string]::IsNullOrWhiteSpace($TrackedMarketsPath)) {
    Join-Path $repoRoot "tracked-markets.verification.json"
}
else {
    $TrackedMarketsPath
}
$trackedPath = (Resolve-Path -LiteralPath $trackedPath).Path
$trackedSettings = Get-Content -Raw -LiteralPath $trackedPath | ConvertFrom-Json
$trackedInstruments = @($trackedSettings.AI.DailyBriefing.TrackedMarkets |
    ForEach-Object { [string]$_.InstrumentId } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique)
if ($trackedInstruments.Count -lt 3) {
    throw "The lab needs at least three tracked markets so the daily planning policy can produce its normal shortlist."
}

$systemdState = (& wsl.exe -d $Distro -- systemctl --user is-system-running).Trim()
if ($LASTEXITCODE -ne 0 -or $systemdState -ne "running") {
    throw "WSL user systemd must be running for cgroup accounting. '$Distro' reported '$systemdState'."
}

$runId = "worker-ai-memory-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
$runDirectory = Join-Path $repoRoot "artifacts\worker-ai-memory-lab\$runId"
$traceDirectory = Join-Path $runDirectory "trace"
$observabilityDirectory = Join-Path $runDirectory "observability"
$seedDirectory = Join-Path $runDirectory "market-data"
New-Item -ItemType Directory -Force -Path $traceDirectory, $observabilityDirectory, $seedDirectory | Out-Null

$labDatabasePath = Join-Path $seedDirectory "ig-market-data.sqlite"
Copy-SqliteSeed -SourcePath $seedPath -DestinationPath $labDatabasePath

$publishDirectory = Join-Path $repoRoot "artifacts\publish\worker-ai-memory-lab"
$workerProject = Join-Path $repoRoot "src\Trading.Worker\Trading.Worker.csproj"
$publishArguments = @(
    "publish",
    $workerProject,
    "--configuration", "Release",
    "--runtime", "linux-x64",
    "--self-contained", "true",
    "--output", $publishDirectory,
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)

Write-Host "Publishing the production-shaped Linux worker..."
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Publishing the worker AI memory lab failed with exit code $LASTEXITCODE."
}

$wslRepository = ConvertTo-WslPath -Path $repoRoot
$wslTraceDirectory = ConvertTo-WslPath -Path $traceDirectory
$wslObservabilityDirectory = ConvertTo-WslPath -Path $observabilityDirectory
$wslLabDatabasePath = ConvertTo-WslPath -Path $labDatabasePath
$wslTrackedMarketsPath = ConvertTo-WslPath -Path $trackedPath
$wslPublishDirectory = ConvertTo-WslPath -Path $publishDirectory
$wslWorkerExecutable = "$wslPublishDirectory/Trading.Worker"

$timezone = Get-AustralianEasternTime
$nowInTimezone = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $timezone)
$dailyAt = $nowInTimezone.AddMinutes($InitialDelayMinutes)
$dailyAt = [DateTimeOffset]::new($dailyAt.Year, $dailyAt.Month, $dailyAt.Day, $dailyAt.Hour, $dailyAt.Minute, 0, $dailyAt.Offset)
$firstIntradayAt = $dailyAt.AddMinutes(4)
$intradayMinuteOffset = $firstIntradayAt.Minute % $IntradayIntervalMinutes
$dailyCron = "0 $($dailyAt.Minute) $($dailyAt.Hour) * * *"
$intradayCron = "0 $intradayMinuteOffset/$IntradayIntervalMinutes * * * *"

$environment = [ordered]@{
    "ASPNETCORE_ENVIRONMENT" = "Development"
    "Automation__Enabled" = "true"
    "Automation__Timezone" = "Australia/Melbourne"
    "Automation__DailyBriefCron" = $dailyCron
    "Automation__Execution__Mode" = "Disabled"
    "Automation__Execution__StorePath" = (ConvertTo-WslPath -Path (Join-Path $runDirectory "execution-boundary.sqlite"))
    "Automation__IntradayOpportunities__Enabled" = "true"
    "Automation__IntradayOpportunities__Cron" = $intradayCron
    "Automation__IntradayOpportunities__ChartResolution" = "FiveMinutes"
    "Automation__IntradayOpportunities__ChartLookbackHours" = "$ChartLookbackHours"
    "Automation__IntradayOpportunities__AllowStalePriceDataForDiagnostics" = "true"
    "MarketData__StorePath" = $wslLabDatabasePath
    "MarketData__CanonicalResolution" = "FiveMinutes"
    "MarketData__Recovery__Mode" = "Disabled"
    "MarketData__CloudSnapshot__Mirror__Enabled" = "false"
    "MarketData__CloudSnapshot__Publisher__Enabled" = "false"
    "AI__Prompts__ObservabilityRootPath" = $wslObservabilityDirectory
    "WorkerHealth__Enabled" = "false"
    "Alerting__Slack__Enabled" = "false"
    "WorkerDiagnostics__Enabled" = "true"
    "WorkerDiagnostics__LocalDirectory" = $wslTraceDirectory
    "WorkerDiagnostics__SentryInterval" = "00:00:01"
    "WorkerDiagnostics__SampleInterval" = "00:00:05"
    "WorkerDiagnostics__FlushInterval" = "00:00:05"
    "WorkerDiagnostics__SegmentMaximumBytes" = "16777216"
    "WorkerDiagnostics__RetentionMaximumBytes" = "134217728"
    "WorkerDiagnostics__UploadClosedSegments" = "false"
    "WorkerDiagnostics__Pressure__WorkerCgroupWarningBytes" = "$($MemoryObservationMiB * 1MB)"
    "WorkerDiagnostics__Pressure__HostAvailableWarningBytes" = "1"
    "WorkerDiagnostics__Pressure__ExternalProcessCountGrowth" = "100000"
    "WorkerDiagnostics__Containment__Enabled" = "false"
}
for ($index = 0; $index -lt $trackedInstruments.Count; $index++) {
    $environment["AI__DailyBriefing__TrackedMarketInstrumentFilter__$index"] = $trackedInstruments[$index]
}

$runManifest = [ordered]@{
    schemaVersion = 1
    runId = $runId
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    durationHours = $DurationHours
    startupCheckOnly = $StartupCheckOnly.IsPresent
    observationLimitMiB = $MemoryObservationMiB
    memoryHighMiB = $MemoryHighMiB
    memoryMaxMiB = $MemoryMaxMiB
    expectedDailyPlanAt = $dailyAt.ToString("O")
    expectedFirstIntradayAt = $firstIntradayAt.ToString("O")
    dailyCron = $dailyCron
    intradayCron = $intradayCron
    intradayIntervalMinutes = $IntradayIntervalMinutes
    chartLookbackHours = $ChartLookbackHours
    marketDataSeedPath = $seedPath
    marketDataCopyPath = $labDatabasePath
    trackedMarketCount = $trackedInstruments.Count
    trackedMarketConfigPath = $trackedPath
    validateOnly = $ValidateOnly.IsPresent
    notes = @(
        "Execution is disabled.",
        "Historical backfill and automatic recovery are disabled.",
        "The worker connects to the demo IG endpoint only.",
        "Stale local bars are allowed only to ensure chart and OpenAI paths are exercised."
    )
}
Set-RunFile -Path (Join-Path $runDirectory "run.json") -Value $runManifest

if ($ValidateOnly) {
    Write-Host "Combined worker AI memory-lab preflight completed. Artifacts: $runDirectory"
    return
}

$unitName = "ai-trader-$runId"
$durationSeconds = if ($StartupCheckOnly) { 300 } else { [Math]::Ceiling($DurationHours * 3600) }
$systemdArguments = @(
    "-d", $Distro, "--",
    "systemd-run", "--user", "--unit=$unitName", "--wait", "--pipe",
    "--property=MemoryAccounting=yes",
    "--property=MemoryHigh=$MemoryHighMiB`M",
    "--property=MemoryMax=$MemoryMaxMiB`M",
    "--property=MemorySwapMax=0",
    "--working-directory=$wslRepository"
)
foreach ($item in $environment.GetEnumerator()) {
    $systemdArguments += "--setenv=$($item.Key)=$($item.Value)"
}
$systemdArguments += @(
    "--",
    "/usr/bin/timeout", "--preserve-status", "--signal=INT", "--kill-after=30s", "$durationSeconds`s",
    $wslWorkerExecutable,
    "--AI:DailyBriefing:TrackedMarketsConfigFile=$wslTrackedMarketsPath"
)

$workerLogPath = Join-Path $runDirectory "worker.log"
Write-Host "Starting $unitName."
Write-Host "Artifacts: $runDirectory"
Write-Host "Daily plan is scheduled for $($dailyAt.ToString('HH:mm zzz')); the first intraday run is expected at $($firstIntradayAt.ToString('HH:mm zzz'))."
Write-Host "Optional live log: Get-Content -LiteralPath '$workerLogPath' -Wait"

& wsl.exe @systemdArguments *> $workerLogPath
$workerExitCode = $LASTEXITCODE

$completion = [ordered]@{
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    systemdExitCode = $workerExitCode
    unitName = $unitName
}
Set-RunFile -Path (Join-Path $runDirectory "run-completion.json") -Value $completion

$analyzer = Join-Path $repoRoot "tools\analyze-worker-ai-memory-lab.ps1"
& pwsh -NoProfile -File $analyzer -RunDirectory $runDirectory -ProductionLimitMiB $MemoryObservationMiB -HardLimitMiB $MemoryMaxMiB
if ($LASTEXITCODE -ne 0) {
    throw "The worker completed, but the analysis step failed. Inspect $runDirectory."
}

if ($workerExitCode -notin @(0, 124)) {
    throw "The worker exited with code $workerExitCode. The analysis is available in $runDirectory."
}

Write-Host "Worker AI memory lab completed. Read $(Join-Path $runDirectory 'analysis\REPORT.md')."
