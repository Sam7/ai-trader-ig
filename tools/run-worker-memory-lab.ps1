[CmdletBinding()]
param(
    [ValidateSet("idle", "moderate", "pressure")]
    [string] $Profile = "moderate",
    [ValidateRange(1, 3600)]
    [int] $DurationSeconds = 60,
    [ValidateRange(128, 2048)]
    [int] $MemoryMaxMiB = 384,
    [string] $Distro = "Ubuntu",
    [switch] $UseWorkstationGarbageCollection,
    [switch] $DisableDiagnostics,
    [switch] $NormalTelemetryOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required to publish the local memory lab."
}

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    throw "WSL is required because the worker runs under Linux cgroup v2 in this lab."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$toolProject = Join-Path $repoRoot "tools\Trading.Worker.Diagnostics\Trading.Worker.Diagnostics.csproj"
$publishDirectory = Join-Path $repoRoot "artifacts\publish\worker-diagnostics-lab"
$runId = "memory-lab-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
$localRunDirectory = Join-Path $repoRoot "artifacts\diagnostics-lab\$runId"
New-Item -ItemType Directory -Path $localRunDirectory -Force | Out-Null
$unitName = "ai-trader-$runId"

$profileOptions = switch ($Profile) {
    "idle" {
        [pscustomobject]@{
            Enabled = $false
            RetainedMegabytes = 0
            ChurnMegabytesPerInterval = 0
            BurstMegabytes = 0
        }
    }
    "moderate" {
        [pscustomobject]@{
            Enabled = $true
            RetainedMegabytes = 96
            ChurnMegabytesPerInterval = 8
            BurstMegabytes = 64
        }
    }
    "pressure" {
        [pscustomobject]@{
            Enabled = $true
            RetainedMegabytes = 160
            ChurnMegabytesPerInterval = 8
            BurstMegabytes = 64
        }
    }
}

$publishArguments = @(
    "publish",
    $toolProject,
    "--configuration", "Release",
    "--runtime", "linux-x64",
    "--self-contained", "true",
    "--output", $publishDirectory,
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)
if ($UseWorkstationGarbageCollection) {
    $publishArguments += "/p:UseWorkstationGarbageCollection=true"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Publishing the local memory lab failed with exit code $LASTEXITCODE."
}

$wslPublishDirectory = (& wsl.exe -d $Distro -- wslpath -a -u ($publishDirectory -replace "\\", "/")).Trim()
$wslRunDirectory = (& wsl.exe -d $Distro -- wslpath -a -u ($localRunDirectory -replace "\\", "/")).Trim()
$wslExecutable = "$wslPublishDirectory/Trading.Worker.Diagnostics"
$duration = [TimeSpan]::FromSeconds($DurationSeconds).ToString('c')
$diagnosticsEnabled = (-not $DisableDiagnostics).ToString().ToLowerInvariant()
$syntheticEnabled = $profileOptions.Enabled.ToString().ToLowerInvariant()

$systemdArguments = @(
    "-d", $Distro, "--",
    "systemd-run", "--user", "--unit=$unitName", "--wait", "--pipe",
    "-p", "MemoryMax=$MemoryMaxMiB`M",
    "-p", "MemoryAccounting=yes",
    "--", $wslExecutable,
    "--WorkerDiagnostics:Enabled=$diagnosticsEnabled",
    "--WorkerDiagnostics:LocalDirectory=$wslRunDirectory/trace",
    "--SyntheticWorkerLoad:Enabled=$syntheticEnabled",
    "--SyntheticWorkerLoad:Duration=$duration",
    "--SyntheticWorkerLoad:RetainedMegabytes=$($profileOptions.RetainedMegabytes)",
    "--SyntheticWorkerLoad:ChurnMegabytesPerInterval=$($profileOptions.ChurnMegabytesPerInterval)",
    "--SyntheticWorkerLoad:BurstMegabytes=$($profileOptions.BurstMegabytes)",
    "--SyntheticWorkerLoad:BurstInterval=00:00:10",
    "--SyntheticWorkerLoad:BurstHold=00:00:01",
    "--SyntheticWorkerLoad:ResultPath=$wslRunDirectory/summary.json"
)
if ($NormalTelemetryOnly) {
    # WSL user scopes can include unrelated processes. Keep the overhead gate in normal five-second
    # sampling so a host-scope false positive does not measure one-shot forensic artifacts instead.
    $systemdArguments += @(
        "--WorkerDiagnostics:Pressure:WorkerCgroupWarningBytes=$($MemoryMaxMiB * 1MB)",
        "--WorkerDiagnostics:Pressure:HostAvailableWarningBytes=1",
        "--WorkerDiagnostics:Pressure:ExternalProcessCountGrowth=100000"
    )
}

& wsl.exe @systemdArguments
$runExitCode = $LASTEXITCODE

$summaryPath = Join-Path $localRunDirectory "summary.json"
$summary = if (Test-Path -LiteralPath $summaryPath) {
    Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
}

$traceRows = @()
$traceDirectory = Join-Path $localRunDirectory "trace"
if (Test-Path -LiteralPath $traceDirectory) {
    $traceRows = Get-ChildItem -LiteralPath $traceDirectory -Filter "*.jsonl" -File |
        Get-Content |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json }
}

$maxCgroupEvents = if ($traceRows.Count -gt 0) {
    ($traceRows | ForEach-Object { $_.cgroup.maxEvents } | Measure-Object -Maximum).Maximum
} else {
    $null
}
$oomEvents = if ($traceRows.Count -gt 0) {
    ($traceRows | ForEach-Object { $_.cgroup.oomEvents } | Measure-Object -Maximum).Maximum
} else {
    $null
}
$oomKills = if ($traceRows.Count -gt 0) {
    ($traceRows | ForEach-Object { $_.cgroup.oomKillEvents } | Measure-Object -Maximum).Maximum
} else {
    $null
}
$usesServerGarbageCollection = if ($null -ne $summary) {
    $summary.UsesServerGarbageCollection
} else {
    $null
}
$peakWorkingSetBytes = if ($null -ne $summary) { $summary.PeakWorkingSetBytes } else { $null }
$peakCgroupMemoryBytes = if ($null -ne $summary) { $summary.PeakCgroupMemoryBytes } else { $null }
$peakManagedMemoryBytes = if ($null -ne $summary) { $summary.PeakManagedMemoryBytes } else { $null }

[pscustomobject]@{
    Profile = $Profile
    DiagnosticsEnabled = -not $DisableDiagnostics
    NormalTelemetryOnly = $NormalTelemetryOnly.IsPresent
    RequestedWorkstationGc = $UseWorkstationGarbageCollection.IsPresent
    SystemdExitCode = $runExitCode
    ArtifactDirectory = $localRunDirectory
    UsesServerGarbageCollection = $usesServerGarbageCollection
    PeakWorkingSetBytes = $peakWorkingSetBytes
    PeakCgroupMemoryBytes = $peakCgroupMemoryBytes
    PeakManagedMemoryBytes = $peakManagedMemoryBytes
    MaxMemoryEvents = $maxCgroupEvents
    OomEvents = $oomEvents
    OomKills = $oomKills
} | Format-List

if ($runExitCode -ne 0) {
    throw "The memory lab process exited with code $runExitCode. Inspect $localRunDirectory before trying another profile."
}
