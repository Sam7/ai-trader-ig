param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()

$outputPath = Join-Path $run.RunPath 'G03-effective-configuration.json'
$summaryPath = Join-Path $run.RunPath 'G03-configuration-summary.txt'

$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$automationProject = Join-Path $repositoryRoot 'src\Trading.Automation\Trading.Automation.csproj'
$cli = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$automation = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $automationProject

$timezoneResult = Test-TimeZoneConfigured -TimezoneId $automation.automation.timezone
$timezoneOk = $timezoneResult.Resolved
$timezoneError = $timezoneResult.Error

$checks = [ordered]@{
    cliIgReady = $cli.ig.isDemoEndpoint -and $cli.ig.apiKeyConfigured -and $cli.ig.identifierConfigured -and $cli.ig.passwordConfigured
    automationIgReady = $automation.ig.isDemoEndpoint -and $automation.ig.apiKeyConfigured -and $automation.ig.identifierConfigured -and $automation.ig.passwordConfigured
    cliOpenAiReady = $cli.openAi.apiKeyConfigured
    automationOpenAiReady = $automation.openAi.apiKeyConfigured
    timezoneResolved = $timezoneOk
    trackedMarketsLoaded = $automation.trackedMarkets.count -gt 0
    promptObservabilityConfigured = -not [string]::IsNullOrWhiteSpace($automation.observability.rootPath)
}

$result = [ordered]@{
    cli = $cli
    automation = $automation
    checks = $checks
    resolvedTimezoneId = $timezoneResult.ResolvedId
    timezoneError = $timezoneError
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
Write-JsonFile -Path $outputPath -Value $result

$summaryLines = @(
    "CLI IG ready: $($checks.cliIgReady)",
    "Automation IG ready: $($checks.automationIgReady)",
    "CLI OpenAI ready: $($checks.cliOpenAiReady)",
    "Automation OpenAI ready: $($checks.automationOpenAiReady)",
    "Timezone resolved: $($checks.timezoneResolved) ($($automation.automation.timezone))",
    "Tracked markets loaded: $($checks.trackedMarketsLoaded) count=$($automation.trackedMarkets.count)",
    "Observability root: $($automation.observability.rootPath)",
    "CLI user secrets present: $($cli.userSecretsFilePresent)",
    "Automation user secrets present: $($automation.userSecretsFilePresent)"
)
Set-Content -LiteralPath $summaryPath -Value ($summaryLines -join "`r`n") -Encoding UTF8

$allRequired = $checks.cliIgReady -and $checks.automationIgReady -and $checks.cliOpenAiReady -and $checks.automationOpenAiReady -and $checks.timezoneResolved -and $checks.trackedMarketsLoaded -and $checks.promptObservabilityConfigured
$status = if ($allRequired) { 'Passed' } else { 'Blocked' }
$blockers = New-Object System.Collections.Generic.List[string]
foreach ($key in $checks.Keys) {
    if (-not $checks[$key]) {
        $blockers.Add($key)
    }
}

$ledger = Read-JsonFile -Path $run.LedgerPath
$ledger.environment = if ($automation.ig.isDemoEndpoint) { 'IG Demo' } else { 'Unknown' }
Write-JsonFile -Path $run.LedgerPath -Value $ledger

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G03' `
    -Name 'Runtime configuration validation' `
    -Status $status `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'verify-configuration.ps1' `
    -ExitCode 0 `
    -Evidence @($outputPath, $summaryPath) `
    -Summary $(if ($allRequired) { 'CLI and automation effective configuration can resolve required IG/OpenAI/scheduler/tracked-market settings.' } else { 'Configuration validation blocked: ' + ($blockers -join ', ') }) `
    -Classification $(if ($allRequired) { $null } else { 'configuration defect' }) `
    -Blocker $(if ($allRequired) { $null } else { $blockers -join ', ' })

Write-Host "G03 complete for run $($run.RunId)"
