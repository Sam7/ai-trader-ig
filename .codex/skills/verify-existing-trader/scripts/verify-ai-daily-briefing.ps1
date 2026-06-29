param(
    [string]$RunId = '',
    [string]$Date = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function Resolve-TradingDate {
    param(
        [string]$RepositoryRoot,
        [string]$Date
    )

    if (-not [string]::IsNullOrWhiteSpace($Date)) {
        return $Date
    }

    $project = Join-Path $RepositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
    $summary = Get-EffectiveConfigurationSummary -RepositoryRoot $RepositoryRoot -ProjectPath $project
    $timezoneId = if ([string]::IsNullOrWhiteSpace($summary.automation.timezone)) { 'Australia/Melbourne' } else { $summary.automation.timezone }
    $timezoneResult = Test-TimeZoneConfigured -TimezoneId $timezoneId
    if (-not $timezoneResult.Resolved) {
        throw $timezoneResult.Error
    }

    $timezone = [TimeZoneInfo]::FindSystemTimeZoneById($timezoneResult.ResolvedId)
    return ([TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $timezone)).DateTime.ToString('yyyy-MM-dd')
}

function Find-ArtifactPath {
    param([string]$Output)

    if ($Output -match '(?im)Artifact\s+(.+)$') {
        $candidate = $Matches[1].Trim()
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Find-LatestPromptEnvelope {
    param(
        [string]$RepositoryRoot,
        [string]$PromptName,
        [string]$Date
    )

    $root = Join-Path $RepositoryRoot "Logs\Observability\$Date"
    if (-not (Test-Path -LiteralPath $root)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $root -Filter "*-$PromptName.json" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 |
        ForEach-Object { $_.FullName }
}

function Find-LatestPromptTextArtifact {
    param(
        [string]$RepositoryRoot,
        [string]$PromptName,
        [string]$Date,
        [string]$Extension
    )

    $root = Join-Path $RepositoryRoot "Logs\Observability\$Date"
    if (-not (Test-Path -LiteralPath $root)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $root -Filter "*-$PromptName.$Extension" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 |
        ForEach-Object { $_.FullName }
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$tradingDate = Resolve-TradingDate -RepositoryRoot $repositoryRoot -Date $Date
$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$config = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject

if (-not $config.openAi.apiKeyConfigured) {
    $started = (Get-Date).ToUniversalTime()
    $evidence = Join-Path $run.RunPath 'G09-G10-openai-blocked.txt'
    Set-Content -LiteralPath $evidence -Value 'OpenAI API key is not configured for the CLI effective configuration.' -Encoding UTF8
    foreach ($gate in @(
        @{ Id = 'G09'; Name = 'Daily AI research' },
        @{ Id = 'G10'; Name = 'Daily plan conversion' }
    )) {
        Update-VerificationGate `
            -LedgerPath $run.LedgerPath `
            -RunPath $run.RunPath `
            -Id $gate.Id `
            -Name $gate.Name `
            -Status 'Blocked' `
            -StartedAtUtc $started `
            -CompletedAtUtc (Get-Date).ToUniversalTime() `
            -Command 'verify-ai-daily-briefing.ps1' `
            -Evidence @($evidence) `
            -Summary 'Blocked because OpenAI API key is not configured.' `
            -Classification 'missing credentials' `
            -Blocker 'OpenAI API key missing'
    }
    Write-Host 'G09/G10 blocked: OpenAI key missing.'
    return
}

$g09Started = (Get-Date).ToUniversalTime()
$researchLog = Join-Path $run.RunPath 'G09-daily-research.log'
$researchCommand = "dotnet run --project src/Trading.Cli -- automation brief research --date $tradingDate"
$research = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $researchCommand `
    -OutputPath $researchLog `
    -TimeoutSeconds 1800

$researchArtifact = Find-ArtifactPath -Output $research.Output
if (-not $researchArtifact) {
    $researchArtifact = Find-LatestPromptTextArtifact -RepositoryRoot $repositoryRoot -PromptName 'daily-brief-research' -Date $tradingDate -Extension 'md'
}
$researchEnvelope = Find-LatestPromptEnvelope -RepositoryRoot $repositoryRoot -PromptName 'daily-brief-research' -Date $tradingDate
$researchEvidence = @($researchLog)
if ($researchArtifact) { $researchEvidence += $researchArtifact }
if ($researchEnvelope) { $researchEvidence += $researchEnvelope }

$researchStatus = $research.ExitCode -eq 0 -and $researchArtifact -and (Test-Path -LiteralPath $researchArtifact)
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G09' `
    -Name 'Daily AI research' `
    -Status $(if ($researchStatus) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g09Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $researchCommand `
    -ExitCode $research.ExitCode `
    -Evidence $researchEvidence `
    -Summary $(if ($researchStatus) { "Daily research completed for $tradingDate and produced an artifact." } else { "Daily research failed or did not produce an artifact for $tradingDate." }) `
    -Classification $(if ($researchStatus) { $null } else { 'OpenAI failure' })

$g10Started = (Get-Date).ToUniversalTime()
$planLog = Join-Path $run.RunPath 'G10-daily-plan.log'
$planCommand = "dotnet run --project src/Trading.Cli -- automation brief plan --date $tradingDate"
$plan = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $planCommand `
    -OutputPath $planLog `
    -TimeoutSeconds 2400

$convertLog = Join-Path $run.RunPath 'G10-daily-convert.log'
$convertExit = $null
$convertEvidence = @()
if ($researchArtifact) {
    $convertCommand = "dotnet run --project src/Trading.Cli -- automation brief convert --date $tradingDate --input `"$researchArtifact`""
    $convert = Invoke-VerificationCommand `
        -RepositoryRoot $repositoryRoot `
        -RunPath $run.RunPath `
        -Command $convertCommand `
        -OutputPath $convertLog `
        -TimeoutSeconds 1800
    $convertExit = $convert.ExitCode
    $convertEvidence += $convertLog
}
else {
    Set-Content -LiteralPath $convertLog -Value 'Skipped convert command because G09 did not produce a research markdown artifact.' -Encoding UTF8
    $convertExit = 1
    $convertEvidence += $convertLog
}

$planEnvelope = Find-LatestPromptEnvelope -RepositoryRoot $repositoryRoot -PromptName 'daily-plan-json' -Date $tradingDate
$planEvidence = @($planLog) + $convertEvidence
if ($planEnvelope) { $planEvidence += $planEnvelope }

$planOutputHasWatchlist = $plan.Output -match '(?im)Watch List\s+[1-9]\d*'
$convertOutputHasWatchlist = if ($researchArtifact) { (Get-Content -Raw -LiteralPath $convertLog) -match '(?im)Watch List\s+[1-9]\d*' } else { $false }
$g10Passed = $plan.ExitCode -eq 0 -and $convertExit -eq 0 -and $planOutputHasWatchlist -and $convertOutputHasWatchlist
$convertOutput = if (Test-Path -LiteralPath $convertLog) { Get-Content -Raw -LiteralPath $convertLog } else { '' }
$openAiServiceFailure = $plan.Output -match 'ClientResultException|Status:\s+5\d\d' -or $convertOutput -match 'ClientResultException|Status:\s+5\d\d'
$g10Status = if ($g10Passed) { 'Passed' } elseif ($openAiServiceFailure) { 'Blocked' } else { 'Failed' }
$g10Classification = if ($g10Passed) { $null } elseif ($openAiServiceFailure) { 'OpenAI failure' } else { 'malformed AI output' }
$g10Blocker = if ($openAiServiceFailure) { 'OpenAI service request failed during daily plan conversion.' } else { $null }

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G10' `
    -Name 'Daily plan conversion' `
    -Status $g10Status `
    -StartedAtUtc $g10Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command "$planCommand; convert from G09 artifact" `
    -ExitCode $(if ($g10Passed) { 0 } else { 1 }) `
    -Evidence $planEvidence `
    -Summary $(if ($g10Passed) { "Daily plan and separate research conversion completed for $tradingDate with non-empty watchlists." } elseif ($openAiServiceFailure) { "Daily plan conversion was blocked by OpenAI service failure for $tradingDate." } else { "Daily plan conversion failed or did not show a non-empty watchlist for $tradingDate." }) `
    -Classification $g10Classification `
    -Blocker $g10Blocker

Write-Host "G09/G10 complete for run $($run.RunId)"
