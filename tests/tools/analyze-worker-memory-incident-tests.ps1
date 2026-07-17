$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$analyzer = Join-Path $repoRoot 'tools/analyze-worker-memory-incident.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("ai-trader-analyzer-" + [guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Force -Path $root | Out-Null

    $scenarios = @{
        managed = @{ Committed = @(100, 220); PrivateDirty = @(10, 20); File = @(5, 5); Stack = @(1, 1); Worker = @(100, 240); HostAvailable = @(500, 450); ProcessCount = @(10, 10); Expected = 'ManagedRetention' }
        native = @{ Committed = @(100, 105); PrivateDirty = @(10, 150); File = @(5, 5); Stack = @(1, 1); Worker = @(100, 240); HostAvailable = @(500, 450); ProcessCount = @(10, 10); Expected = 'NativeRuntime' }
        file = @{ Committed = @(100, 105); PrivateDirty = @(10, 15); File = @(5, 150); Stack = @(1, 1); Worker = @(100, 240); HostAvailable = @(500, 450); ProcessCount = @(10, 10); Expected = 'FileCache' }
        external = @{ Committed = @(100, 100); PrivateDirty = @(10, 10); File = @(5, 5); Stack = @(1, 1); Worker = @(100, 102); HostAvailable = @(500, 100); ProcessCount = @(10, 25); Expected = 'ExternalHostPressure' }
        mixed = @{ Committed = @(100, 120); PrivateDirty = @(10, 30); File = @(5, 30); Stack = @(1, 1); Worker = @(100, 240); HostAvailable = @(500, 450); ProcessCount = @(10, 10); Expected = 'Inconclusive' }
    }

    foreach ($entry in $scenarios.GetEnumerator()) {
        $scenario = $entry.Value
        $trace = Join-Path $root "$($entry.Key).jsonl"
        $rows = for ($index = 0; $index -lt 2; $index++) {
            [ordered]@{
                schemaVersion = 2
                observedAtUtc = "2026-07-16T00:0$index`:00Z"
                sequence = $index + 1
                process = [ordered]@{
                    processId = 42
                    workingSetBytes = $scenario.Worker[$index] * 1MB
                    managedRuntime = [ordered]@{
                        totalCommittedBytes = $scenario.Committed[$index] * 1MB
                        totalAllocatedBytes = (100 + ($index * 200)) * 1MB
                        allocationRateBytesPerSecond = 10MB
                    }
                    linux = [ordered]@{
                        privateDirtyBytes = $scenario.PrivateDirty[$index] * 1MB
                        stackBytes = $scenario.Stack[$index] * 1MB
                        pssBytes = $scenario.Worker[$index] * 1MB
                    }
                }
                cgroup = [ordered]@{
                    currentBytes = $scenario.Worker[$index] * 1MB
                    memoryStat = [ordered]@{ file = $scenario.File[$index] * 1MB }
                }
                host = [ordered]@{
                    availableBytes = $scenario.HostAvailable[$index] * 1MB
                    processCount = $scenario.ProcessCount[$index]
                    topProcesses = @([ordered]@{ processId = 99; executableName = 'gcloud'; pssBytes = (50 + ($index * 100)) * 1MB })
                }
            } | ConvertTo-Json -Depth 8 -Compress
        }
        Set-Content -LiteralPath $trace -Value $rows
        $output = Join-Path $root "$($entry.Key)-output"
        & pwsh -NoProfile -File $analyzer -TracePaths $trace -OutputDirectory $output
        if ($LASTEXITCODE -ne 0) { throw "Analyzer failed for $($entry.Key)." }
        $summary = Get-Content (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
        if ($summary.classification -ne $scenario.Expected) {
            throw "Expected $($scenario.Expected) for $($entry.Key), got $($summary.classification)."
        }
        foreach ($artifact in 'timeline.csv', 'summary.json', 'REPORT.md', 'sources.sha256') {
            if (-not (Test-Path (Join-Path $output $artifact))) {
                throw "Analyzer did not create $artifact for $($entry.Key)."
            }
        }
    }

    Write-Output 'analyze-worker-memory-incident tests passed'
}
finally {
    if (Test-Path $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
