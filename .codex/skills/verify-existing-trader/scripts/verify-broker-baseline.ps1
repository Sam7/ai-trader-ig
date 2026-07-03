param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function New-BrokerBaselineEnvironment {
    param(
        [string]$RepositoryRoot,
        [string]$ProjectPath,
        [string]$EvidenceRoot
    )

    $effective = Get-EffectiveConfiguration -RepositoryRoot $RepositoryRoot -ProjectPath $ProjectPath
    $config = $effective.Configuration
    $env = @{
        RUN_IG_INTEGRATION = 'true'
        RUN_IG_BROKER_BASELINE = 'true'
        BROKER_BASELINE_EVIDENCE_ROOT = $EvidenceRoot
        IG__BaseUrl = [string](Get-NestedValue -Source $config -Path 'IG:BaseUrl')
        IG__ApiKey = [string](Get-NestedValue -Source $config -Path 'IG:ApiKey')
        IG__Identifier = [string](Get-NestedValue -Source $config -Path 'IG:Identifier')
        IG__Password = [string](Get-NestedValue -Source $config -Path 'IG:Password')
    }

    foreach ($key in @(
        'IG:AccountId',
        'IG:UseEncryptedPassword',
        'IG:TestEpic',
        'IG:TestSize',
        'IG:WorkingOrderTestLevel',
        'IG:MarketSearchTerm'
    )) {
        $value = [string](Get-NestedValue -Source $config -Path $key)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $env[$key.Replace(':', '__')] = $value
        }
    }

    foreach ($legacy in @(
        'IG__TestEpic',
        'IG__TestSize',
        'IG__WorkingOrderTestLevel',
        'IG__MarketSearchTerm'
    )) {
        $value = [Environment]::GetEnvironmentVariable($legacy)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $env[$legacy] = $value
        }
    }

    return $env
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()
$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$configSummary = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$demo = Assert-DemoConfiguration -ConfigurationSummary $configSummary
$safetyPath = Join-Path $run.RunPath 'G08B-broker-baseline-safety.txt'
$evidenceRoot = Join-Path $run.RunPath 'broker-baseline'

if (-not $demo.IsDemoSafe) {
    Set-Content -LiteralPath $safetyPath -Value $demo.Reason -Encoding UTF8
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G08B' `
        -Name 'Phase 0 broker baseline' `
        -Status 'Blocked' `
        -StartedAtUtc $started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'verify-broker-baseline.ps1' `
        -Evidence @($safetyPath) `
        -Summary $demo.Reason `
        -Classification 'missing credentials' `
        -Blocker $demo.Reason
    Write-Host "G08B blocked: $($demo.Reason)"
    return
}

$envOverrides = New-BrokerBaselineEnvironment -RepositoryRoot $repositoryRoot -ProjectPath $cliProject -EvidenceRoot $evidenceRoot
if ($envOverrides['IG__BaseUrl'] -ne 'https://demo-api.ig.com/gateway/deal') {
    Set-Content -LiteralPath $safetyPath -Value 'Refused to run: effective IG__BaseUrl is not the demo endpoint.' -Encoding UTF8
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G08B' `
        -Name 'Phase 0 broker baseline' `
        -Status 'Blocked' `
        -StartedAtUtc $started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'verify-broker-baseline.ps1' `
        -Evidence @($safetyPath) `
        -Summary 'Refused to run because the effective IG endpoint is not demo.' `
        -Classification 'configuration defect' `
        -Blocker 'Non-demo IG endpoint'
    return
}

$safety = @(
    'Broker baseline safety preflight passed.',
    "Base URL: $($envOverrides['IG__BaseUrl'])",
    "Configured account: $(ConvertTo-RedactedAccountId -Value $envOverrides['IG__AccountId'])",
    'RUN_IG_BROKER_BASELINE=true is required for broker-mutating baseline tests.',
    'Secrets are passed to the child process environment and are not written to evidence.'
) -join "`r`n"
Set-Content -LiteralPath $safetyPath -Value $safety -Encoding UTF8

$beforePositions = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- positions list' `
    -OutputPath (Join-Path $run.RunPath 'G08B-before-positions.log') `
    -TimeoutSeconds 180

$beforeOrders = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- working list' `
    -OutputPath (Join-Path $run.RunPath 'G08B-before-working-orders.log') `
    -TimeoutSeconds 180

$testResults = Join-Path $run.RunPath 'test-results\broker-baseline'
New-Item -ItemType Directory -Force -Path $testResults | Out-Null
$testCommand = "dotnet test tests/Trading.IG.Tests/Trading.IG.Tests.csproj --filter `"Category=BrokerBaseline`" --logger `"trx;LogFileName=broker-baseline.trx`" --results-directory `"$testResults`""
$tests = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $testCommand `
    -OutputPath (Join-Path $run.RunPath 'G08B-broker-baseline-tests.log') `
    -Environment $envOverrides `
    -TimeoutSeconds 1800

$afterPositions = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- positions list' `
    -OutputPath (Join-Path $run.RunPath 'G08B-after-positions.log') `
    -TimeoutSeconds 180

$afterOrders = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- working list' `
    -OutputPath (Join-Path $run.RunPath 'G08B-after-working-orders.log') `
    -TimeoutSeconds 180

$trxFiles = @(Get-ChildItem -LiteralPath $testResults -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$baselineEvidence = @(Get-ChildItem -LiteralPath $evidenceRoot -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$blocked = $tests.Output -match 'exceeded-account-allowance|Market is not tradeable|No tradeable broker-baseline canary|pre-existing positions|pre-existing working orders'
$status = if ($tests.ExitCode -eq 0) { 'Passed' } elseif ($blocked) { 'Blocked' } else { 'Failed' }
$classification = if ($tests.ExitCode -eq 0) { $null } elseif ($blocked) { 'broker baseline precondition' } else { 'broker baseline failure' }
$blocker = if ($blocked) { 'Broker baseline could not complete because a demo account, allowance, market, or canary exposure precondition was not satisfied.' } else { $null }
$summary = if ($tests.ExitCode -eq 0) {
    'Broker baseline scenarios completed; before/after broker state and sanitized scenario evidence were captured.'
}
elseif ($blocked) {
    'Broker baseline was blocked by a demo broker precondition. Inspect evidence for the specific blocker.'
}
else {
    'Broker baseline failed. Before/after broker state was captured for cleanup diagnosis.'
}

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G08B' `
    -Name 'Phase 0 broker baseline' `
    -Status $status `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $testCommand `
    -ExitCode $tests.ExitCode `
    -Evidence (@($safetyPath, $beforePositions.OutputPath, $beforeOrders.OutputPath, $tests.OutputPath, $afterPositions.OutputPath, $afterOrders.OutputPath) + $trxFiles + $baselineEvidence) `
    -Summary $summary `
    -Classification $classification `
    -Blocker $blocker

Write-Host "G08B complete for run $($run.RunId)"
