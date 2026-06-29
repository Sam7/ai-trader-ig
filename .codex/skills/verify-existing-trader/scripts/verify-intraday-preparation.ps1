param(
    [string]$RunId = '',
    [string]$Date = '',
    [string]$At = '',
    [string]$PreparedJsonPath = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function Resolve-TradingDate {
    param([string]$Date)

    if (-not [string]::IsNullOrWhiteSpace($Date)) {
        return $Date
    }

    return (Get-Date).ToString('yyyy-MM-dd')
}

function Resolve-At {
    param([string]$At)

    if (-not [string]::IsNullOrWhiteSpace($At)) {
        return ([DateTimeOffset]::Parse($At).ToUniversalTime()).ToString('o')
    }

    return ([DateTimeOffset]::UtcNow).ToString('o')
}

function Find-PreparedJson {
    param(
        [string]$RepositoryRoot,
        [string]$Date
    )

    $root = Join-Path $RepositoryRoot "Logs\Observability\$Date"
    if (-not (Test-Path -LiteralPath $root)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $root -Filter '*-intraday-opportunity-prepare.json' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 |
        ForEach-Object { $_.FullName }
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$tradingDate = Resolve-TradingDate -Date $Date
$requestedAt = Resolve-At -At $At

$g11Started = (Get-Date).ToUniversalTime()
$prepareLog = Join-Path $run.RunPath 'G11-intraday-prepare.log'
$prepareCommand = "dotnet run --project src/Trading.Cli -- automation intraday prepare --date $tradingDate --at `"$requestedAt`""
$prepare = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $prepareCommand `
    -OutputPath $prepareLog `
    -TimeoutSeconds 900

$prepared = if (-not [string]::IsNullOrWhiteSpace($PreparedJsonPath) -and (Test-Path -LiteralPath $PreparedJsonPath)) {
    (Resolve-Path -LiteralPath $PreparedJsonPath).Path
}
else {
    Find-PreparedJson -RepositoryRoot $repositoryRoot -Date $tradingDate
}

$prepareProduced = $prepare.ExitCode -eq 0 -and $prepared -and (Test-Path -LiteralPath $prepared)
$noPlan = $prepare.Output -match 'No eligible intraday preparation result|no trading day plan|no trading day plan exists'
$g11Status = if ($prepareProduced) { 'Passed' } elseif ($noPlan) { 'Blocked' } else { 'Failed' }
$g11Blocker = if ($noPlan) { 'Standalone CLI process has no in-memory trading-day plan; verify same-process path in G14.' } else { $null }
$g11Evidence = @($prepareLog)
if ($prepared) { $g11Evidence += $prepared }

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G11' `
    -Name 'Intraday preparation without OpenAI' `
    -Status $g11Status `
    -StartedAtUtc $g11Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $prepareCommand `
    -ExitCode $prepare.ExitCode `
    -Evidence $g11Evidence `
    -Summary $(if ($prepareProduced) { 'Prepared intraday request artifact from current dependencies.' } elseif ($noPlan) { 'Standalone prepare invocation had no in-memory plan, which is an expected limitation.' } else { 'Intraday preparation failed for a reason other than missing in-memory plan.' }) `
    -Classification $(if ($prepareProduced) { $null } elseif ($noPlan) { 'in-memory state limitation' } else { 'unknown' }) `
    -Blocker $g11Blocker

$g12Started = (Get-Date).ToUniversalTime()
if (-not $prepared) {
    $blockPath = Join-Path $run.RunPath 'G12-submit-blocked.txt'
    Set-Content -LiteralPath $blockPath -Value 'No prepared intraday JSON artifact is available. Produce one in G11 or G14, then rerun with -PreparedJsonPath.' -Encoding UTF8
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G12' `
        -Name 'Intraday OpenAI submission' `
        -Status 'Blocked' `
        -StartedAtUtc $g12Started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'automation intraday submit' `
        -Evidence @($blockPath) `
        -Summary 'Blocked because no prepared intraday JSON artifact exists.' `
        -Classification 'in-memory state limitation' `
        -Blocker 'Prepared artifact missing'
}
else {
    $submitLog = Join-Path $run.RunPath 'G12-intraday-submit.log'
    $submitCommand = "dotnet run --project src/Trading.Cli -- automation intraday submit --input `"$prepared`""
    $submit = Invoke-VerificationCommand `
        -RepositoryRoot $repositoryRoot `
        -RunPath $run.RunPath `
        -Command $submitCommand `
        -OutputPath $submitLog `
        -TimeoutSeconds 1800

    $decisionPending = $submit.Output -match 'Decision logic pending'
    $assessments = $submit.Output -match '(?im)Assessments\s+[1-9]\d*'
    $g12Passed = $submit.ExitCode -eq 0 -and $decisionPending -and $assessments

    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G12' `
        -Name 'Intraday OpenAI submission' `
        -Status $(if ($g12Passed) { 'Passed' } else { 'Failed' }) `
        -StartedAtUtc $g12Started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command $submitCommand `
        -ExitCode $submit.ExitCode `
        -Evidence @($submitLog, $prepared) `
        -Summary $(if ($g12Passed) { 'Intraday OpenAI submission returned structured assessments and reached the decision-logic-pending boundary.' } else { 'Intraday OpenAI submission failed or did not reach the expected terminal outcome.' }) `
        -Classification $(if ($g12Passed) { $null } else { 'FailedOpenAI' })
}

$g13Started = (Get-Date).ToUniversalTime()
$g13Evidence = Join-Path $run.RunPath 'G13-skip-failure-paths.txt'
$g13Text = @(
    'G13 requires explicit exercise of skip and failure paths.',
    "Observed standalone no-plan path in G11: $noPlan",
    'Existing tests cover some prompt failure observability and strategy validation paths, but this script does not claim full G13 verification.',
    'Run targeted tests or add narrow tests for remaining branches before marking G13 passed.'
) -join "`r`n"
Set-Content -LiteralPath $g13Evidence -Value $g13Text -Encoding UTF8
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G13' `
    -Name 'Existing skip and failure paths' `
    -Status 'Blocked' `
    -StartedAtUtc $g13Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'verify-intraday-preparation.ps1' `
    -Evidence @($g13Evidence) `
    -Summary 'G13 is not fully verified by this wrapper; remaining skip/failure paths require targeted tests or controlled runs.' `
    -Classification 'test defect' `
    -Blocker 'Coverage for all required skip/failure paths is not yet demonstrated.'

Write-Host "G11-G13 complete for run $($run.RunId)"
