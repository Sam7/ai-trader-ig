[CmdletBinding()]
param(
    [ValidateSet("production", "resolution", "dimensions", "features", "retention", "all")]
    [string] $Profile = "production",
    [ValidateRange(1, 1000)]
    [int] $Iterations = 100,
    [ValidateRange(0, 100)]
    [int] $WarmupIterations = 10,
    [ValidateRange(128, 2048)]
    [int] $MemoryMaxMiB = 480,
    [ValidateRange(1, 10)]
    [int] $Runs = 1,
    [ValidateRange(1, 1000)]
    [int] $PeakSampleMilliseconds = 2,
    [string] $Distro = "Ubuntu",
    [switch] $UseWorkstationGarbageCollection,
    [switch] $WriteCharts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet is required." }
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { throw "WSL is required for the Linux cgroup lab." }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "tools\Trading.Charting.MemoryLab\Trading.Charting.MemoryLab.csproj"
$publishDirectory = Join-Path $repoRoot "artifacts\publish\chart-memory-lab"
$runRoot = Join-Path $repoRoot "artifacts\chart-memory-lab\chart-memory-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$publishArguments = @(
    "publish", $project, "--configuration", "Release", "--runtime", "linux-x64", "--self-contained", "true",
    "--output", $publishDirectory, "/p:DebugType=None", "/p:DebugSymbols=false"
)
if ($UseWorkstationGarbageCollection) { $publishArguments += "/p:UseWorkstationGarbageCollection=true" }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "Chart memory lab publish failed with exit code $LASTEXITCODE." }

$wslPublishDirectory = (& wsl.exe -d $Distro -- wslpath -a -u ($publishDirectory -replace '\\', "/")).Trim()
$wslRunRoot = (& wsl.exe -d $Distro -- wslpath -a -u ($runRoot -replace '\\', "/")).Trim()
$executable = "$wslPublishDirectory/Trading.Charting.MemoryLab"

for ($run = 1; $run -le $Runs; $run++) {
    $runDirectory = Join-Path $runRoot ("run-{0:D2}" -f $run)
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $wslRunDirectory = (& wsl.exe -d $Distro -- wslpath -a -u ($runDirectory -replace '\\', "/")).Trim()
    $unitName = "ai-trader-chart-memory-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$run"
    $systemdArguments = @(
        "-d", $Distro, "--", "systemd-run", "--user", "--unit=$unitName", "--wait", "--pipe",
        "-p", "MemoryMax=$MemoryMaxMiB`M", "-p", "MemoryAccounting=yes", "--", $executable,
        "--profile", $Profile, "--iterations", $Iterations, "--warmup", $WarmupIterations,
        "--sample-ms", $PeakSampleMilliseconds, "--output", $wslRunDirectory
    )
    if ($WriteCharts) { $systemdArguments += "--write-charts" }
    & wsl.exe @systemdArguments
    if ($LASTEXITCODE -ne 0) { throw "Chart memory lab run $run failed with exit code $LASTEXITCODE. Inspect $runDirectory." }
}

[pscustomobject]@{
    Profile = $Profile
    Runs = $Runs
    Iterations = $Iterations
    MemoryMaxMiB = $MemoryMaxMiB
    WorkstationGC = $UseWorkstationGarbageCollection.IsPresent
    OutputDirectory = $runRoot
} | Format-List
