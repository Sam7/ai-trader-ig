[CmdletBinding()]
param(
    [ValidateRange(1, 10)]
    [int] $Runs = 3,
    [ValidateSet("idle", "moderate", "pressure")]
    [string] $Profile = "moderate",
    [ValidateRange(60, 3600)]
    [int] $DurationSeconds = 600,
    [ValidateRange(128, 2048)]
    [int] $MemoryMaxMiB = 480,
    [string] $Distro = "Ubuntu"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runner = Join-Path $repoRoot "tools\run-worker-memory-overhead-series.ps1"
$artifactRoot = Join-Path $repoRoot "artifacts\diagnostics-lab"
$launchId = "overhead-detached-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
$stdoutPath = Join-Path $artifactRoot "$launchId.stdout.log"
$stderrPath = Join-Path $artifactRoot "$launchId.stderr.log"
$metadataPath = Join-Path $artifactRoot "$launchId.json"
$lockPath = Join-Path $artifactRoot ".overhead-series.lock"

$activeCoordinators = Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -match "run-worker-memory-overhead-series\.ps1" }
if ($null -ne $activeCoordinators) {
    throw "An overhead-series coordinator is already running: $($activeCoordinators.ProcessId -join ', ')."
}

$activeUnits = (& wsl.exe -d $Distro -- systemctl --user list-units --type=service --state=running 2>$null) |
    Where-Object { $_ -match "ai-trader-memory-lab-" }
if ($activeUnits) {
    throw "A memory-lab systemd unit is already running. Stop and verify it before starting another series."
}

if (Test-Path -LiteralPath $lockPath) {
    $lock = Get-Item -LiteralPath $lockPath
    if (([DateTime]::UtcNow - $lock.LastWriteTimeUtc) -gt [TimeSpan]::FromMinutes(5) -and -not $activeCoordinators -and -not $activeUnits) {
        Remove-Item -LiteralPath $lockPath -Recurse -Force -ErrorAction Stop
    } else {
        throw "The overhead-series lock exists. It is not safe to recover automatically: $lockPath"
    }
}

$arguments = @(
    "-NoProfile",
    "-File", $runner,
    "-Runs", $Runs,
    "-Profile", $Profile,
    "-DurationSeconds", $DurationSeconds,
    "-MemoryMaxMiB", $MemoryMaxMiB,
    "-Distro", $Distro
)

$process = Start-Process `
    -FilePath (Join-Path $PSHOME "pwsh.exe") `
    -WorkingDirectory $repoRoot `
    -WindowStyle Hidden `
    -PassThru `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -ArgumentList $arguments

Start-Sleep -Seconds 3
$liveProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
if ($null -eq $liveProcess) {
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
    throw "Detached coordinator exited during startup. stderr: $stderr"
}

[pscustomobject]@{
    ProcessId = $process.Id
    StartedAt = $process.StartTime
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
    MetadataPath = $metadataPath
    Runs = $Runs
    DurationSeconds = $DurationSeconds
    MemoryMaxMiB = $MemoryMaxMiB
    Distro = $Distro
} | ConvertTo-Json | Set-Content -LiteralPath $metadataPath

[pscustomobject]@{
    ProcessId = $process.Id
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
    MetadataPath = $metadataPath
}
