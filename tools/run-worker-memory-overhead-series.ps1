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
$labScript = Join-Path $repoRoot "tools\run-worker-memory-lab.ps1"
$artifactRoot = Join-Path $repoRoot "artifacts\diagnostics-lab"
$seriesId = "overhead-series-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
$seriesDirectory = Join-Path $artifactRoot $seriesId
New-Item -ItemType Directory -Path $seriesDirectory -Force | Out-Null
$lockPath = Join-Path $artifactRoot ".overhead-series.lock"
try {
    New-Item -ItemType Directory -Path $lockPath -ErrorAction Stop | Out-Null
} catch {
    throw "Another overhead series is already running. Remove $lockPath only after verifying no coordinator or lab unit is active."
}

$records = [System.Collections.Generic.List[object]]::new()

try {
for ($run = 1; $run -le $Runs; $run++) {
    foreach ($mode in @("off", "on")) {
        $startedDirectories = @(
            Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter "memory-lab-*" |
                Select-Object -ExpandProperty FullName
        )
        $logPath = Join-Path $seriesDirectory ("run-{0:D2}-{1}.log" -f $run, $mode)
        $arguments = @(
            "-NoProfile",
            "-File", $labScript,
            "-Profile", $Profile,
            "-DurationSeconds", $DurationSeconds,
            "-MemoryMaxMiB", $MemoryMaxMiB,
            "-Distro", $Distro
        )
        if ($mode -eq "off") {
            $arguments += "-DisableDiagnostics"
        } else {
            $arguments += "-NormalTelemetryOnly"
        }

        & pwsh @arguments *>&1 | Tee-Object -LiteralPath $logPath
        if ($LASTEXITCODE -ne 0) {
            throw "Memory lab failed for run $run ($mode). Inspect $logPath."
        }

        $runDirectory = Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter "memory-lab-*" |
            Where-Object { $startedDirectories -notcontains $_.FullName } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -eq $runDirectory) {
            throw "Memory lab did not produce a new artifact directory for run $run ($mode)."
        }

        $summaryPath = Join-Path $runDirectory.FullName "summary.json"
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $oomEvents = if ($summary.PSObject.Properties.Name -contains "OomEvents") { $summary.OomEvents } else { $null }
        $oomKills = if ($summary.PSObject.Properties.Name -contains "OomKills") { $summary.OomKills } else { $null }
        $records.Add([pscustomobject]@{
            Run = $run
            Mode = $mode
            ArtifactDirectory = $runDirectory.FullName
            PeakWorkingSetBytes = $summary.PeakWorkingSetBytes
            BaselineWorkingSetBytes = $summary.BaselineWorkingSetBytes
            PeakCgroupMemoryBytes = $summary.PeakCgroupMemoryBytes
            PeakManagedMemoryBytes = $summary.PeakManagedMemoryBytes
            OomEvents = $oomEvents
            OomKills = $oomKills
        })
    }
}

$records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $seriesDirectory "summary.json")
$records | Format-Table Run, Mode, PeakWorkingSetBytes, PeakCgroupMemoryBytes, OomEvents, OomKills
Write-Output "SeriesDirectory=$seriesDirectory"
} finally {
    Remove-Item -LiteralPath $lockPath -Recurse -Force -ErrorAction SilentlyContinue
}
