$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$analyzer = Join-Path $repoRoot 'tools/analyze-worker-ai-memory-lab.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("ai-trader-ai-memory-analyzer-" + [guid]::NewGuid().ToString('N'))

try {
    $traceDirectory = Join-Path $root 'trace'
    $observabilityDirectory = Join-Path $root 'observability'
    New-Item -ItemType Directory -Force -Path $traceDirectory, $observabilityDirectory | Out-Null

    $rows = for ($index = 0; $index -lt 2; $index++) {
        $cgroupMiB = if ($index -eq 0) { 100 } else { 500 }
        $managedMiB = if ($index -eq 0) { 20 } else { 300 }
        [ordered]@{
            schemaVersion = 2
            observedAtUtc = "2026-07-18T00:0$index`:00Z"
            sequence = $index + 1
            process = [ordered]@{
                workingSetBytes = $cgroupMiB * 1MB
                privateMemoryBytes = $cgroupMiB * 1MB
                threadCount = 12
                linux = [ordered]@{
                    pssBytes = $cgroupMiB * 1MB
                    privateDirtyBytes = 20MB
                }
                managedRuntime = [ordered]@{
                    liveBytes = $managedMiB * 1MB
                    totalCommittedBytes = $managedMiB * 1MB
                    allocationRateBytesPerSecond = 1MB
                    largeObjectHeap = [ordered]@{ sizeAfterBytes = 50MB }
                    pinnedObjectHeap = [ordered]@{ sizeAfterBytes = 1MB }
                    threadPool = [ordered]@{ threadCount = 4 }
                }
            }
            cgroup = [ordered]@{
                currentBytes = $cgroupMiB * 1MB
                highEvents = if ($index -eq 0) { 0 } else { 1 }
                maxEvents = 0
                oomEvents = 0
                oomKillEvents = 0
                memoryStat = [ordered]@{ file = 5MB }
            }
            sqlite = [ordered]@{
                allocatorCurrentBytes = 2MB
                pagecacheCurrentBytes = 1MB
            }
            stream = [ordered]@{
                dispatcherDepth = 0
                ingestorDepth = 0
                droppedUpdates = 0
            }
            operations = [ordered]@{
                activeOperations = @()
                recentCheckpoints = @([ordered]@{
                    operation = 'intraday-ai-review'
                    correlationId = 'safe-correlation'
                    outcome = 'Completed'
                    itemCount = 1
                    payloadBytes = 0
                    startedAtUtc = '2026-07-18T00:00:10Z'
                    completedAtUtc = '2026-07-18T00:00:50Z'
                    duration = '00:00:40'
                    beforeMemory = [ordered]@{ cgroupCurrentBytes = 100MB; pssBytes = 100MB; workingSetBytes = 100MB; managedCommittedBytes = 20MB }
                    afterMemory = [ordered]@{ cgroupCurrentBytes = 500MB; pssBytes = 500MB; workingSetBytes = 500MB; managedCommittedBytes = 300MB }
                })
            }
        } | ConvertTo-Json -Depth 8 -Compress
    }
    Set-Content -LiteralPath (Join-Path $traceDirectory 'worker.jsonl') -Value $rows

    [ordered]@{
        promptId = 'intraday-opportunity-review'
        promptName = 'Intraday opportunity review'
        status = 'Completed'
        requestedAtUtc = '2026-07-18T00:00:10Z'
        completedAtUtc = '2026-07-18T00:00:50Z'
        modelId = 'test-model'
        processingMode = 'Synchronous'
        requestText = 'SECRET-PROMPT-CONTENT-MUST-NOT-APPEAR'
        responseText = 'SECRET-RESPONSE-CONTENT-MUST-NOT-APPEAR'
        durationMs = 40000
        cost = [ordered]@{
            inputTokens = 100
            outputTokens = 50
            cachedInputTokens = 0
            totalCostUsd = 0.01
        }
        attachmentArtifactPaths = @('chart.png')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $observabilityDirectory 'prompt.json')
    Set-Content -LiteralPath (Join-Path $observabilityDirectory 'prompt-extracted.json') -Value '{"not":"a prompt observation"}'

    try {
        & $analyzer -RunDirectory $root -ProductionLimitMiB 480 -HardLimitMiB 600
    }
    catch {
        throw "Analyzer failure: $($_.Exception.Message) $($_.ScriptStackTrace) $($_.InvocationInfo.PositionMessage)"
    }
    if (-not $?) { throw 'The combined worker memory analyzer failed.' }

    $analysis = Join-Path $root 'analysis'
    $summary = Get-Content -Raw (Join-Path $analysis 'summary.json') | ConvertFrom-Json
    if (-not $summary.outcome.productionLimitReached) {
        throw 'Expected the analyzer to record the 480 MiB production breach.'
    }
    if ($summary.attribution.classification -ne 'ManagedGrowthCandidate') {
        throw "Expected ManagedGrowthCandidate, got $($summary.attribution.classification)."
    }
    if ($summary.ai.promptCount -ne 1) {
        throw "Expected one prompt record, got $($summary.ai.promptCount)."
    }
    if ($summary.evidence.invalidPromptRecordCount -ne 0) {
        throw "Expected prompt sidecar files to be ignored, got $($summary.evidence.invalidPromptRecordCount) invalid prompt record(s)."
    }
    $report = Get-Content -Raw (Join-Path $analysis 'REPORT.md')
    if ($report -match 'SECRET-(PROMPT|RESPONSE)-CONTENT') {
        throw 'The report leaked raw prompt or response content.'
    }
    foreach ($artifact in 'timeline.csv', 'summary.json', 'operation-checkpoints.json', 'prompt-summary.json', 'sample-gaps.json', 'REPORT.md', 'sources.sha256') {
        if (-not (Test-Path (Join-Path $analysis $artifact))) {
            throw "Analyzer did not create $artifact."
        }
    }

    Write-Output 'analyze-worker-ai-memory-lab tests passed'
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
