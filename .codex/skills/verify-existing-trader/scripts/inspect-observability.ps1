param(
    [string]$RunId = '',
    [string]$Date = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()

$root = Join-Path $repositoryRoot 'Logs\Observability'
if (-not [string]::IsNullOrWhiteSpace($Date)) {
    $root = Join-Path $root $Date
}

$summaryPath = Join-Path $run.RunPath 'observability-summary.json'
$records = New-Object System.Collections.Generic.List[object]
if (Test-Path -LiteralPath $root) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.json' -File -Recurse) {
        try {
            $json = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
            if ($json.PSObject.Properties.Name -contains 'promptId') {
                $records.Add([ordered]@{
                    path = $file.FullName
                    promptName = $json.promptName
                    status = $json.status
                    modelId = $json.modelId
                    requestedAtUtc = $json.requestedAtUtc
                    completedAtUtc = $json.completedAtUtc
                    hasUsage = $null -ne $json.usage
                    hasCost = $null -ne $json.cost
                    hasError = -not [string]::IsNullOrWhiteSpace([string]$json.error)
                    textArtifactPath = $json.textArtifactPath
                    structuredArtifactPath = $json.structuredArtifactPath
                    attachmentCount = @($json.attachmentArtifactPaths).Count
                })
            }
        }
        catch {
            $records.Add([ordered]@{
                path = $file.FullName
                status = 'Unreadable'
                error = $_.Exception.Message
            })
        }
    }
}

$pending = @($records | Where-Object { $_.status -eq 'Pending' })
$failed = @($records | Where-Object { $_.status -eq 'Failed' })
$completed = @($records | Where-Object { $_.status -eq 'Completed' })

Write-JsonFile -Path $summaryPath -Value ([ordered]@{
    inspectedRoot = $root
    total = $records.Count
    completed = $completed.Count
    failed = $failed.Count
    pending = $pending.Count
    records = $records
})

Write-Host "Observability inspected: $($records.Count) records, $($pending.Count) pending."
