param(
    [string]$RunId = '',
    [switch]$IncludeEncrypted,
    [switch]$IncludeNavigation,
    [switch]$IncludePrices
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function New-IgEnvironment {
    param(
        [string]$RepositoryRoot,
        [string]$ProjectPath
    )

    $effective = Get-EffectiveConfiguration -RepositoryRoot $RepositoryRoot -ProjectPath $ProjectPath
    $config = $effective.Configuration
    $env = @{
        RUN_IG_INTEGRATION = 'true'
        IG__BaseUrl = [string](Get-NestedValue -Source $config -Path 'IG:BaseUrl')
        IG__ApiKey = [string](Get-NestedValue -Source $config -Path 'IG:ApiKey')
        IG__Identifier = [string](Get-NestedValue -Source $config -Path 'IG:Identifier')
        IG__Password = [string](Get-NestedValue -Source $config -Path 'IG:Password')
    }

    $accountId = [string](Get-NestedValue -Source $config -Path 'IG:AccountId')
    if (-not [string]::IsNullOrWhiteSpace($accountId)) {
        $env['IG__AccountId'] = $accountId
    }

    $encrypted = [string](Get-NestedValue -Source $config -Path 'IG:UseEncryptedPassword')
    if (-not [string]::IsNullOrWhiteSpace($encrypted)) {
        $env['IG__UseEncryptedPassword'] = $encrypted
    }

    $testEpic = [Environment]::GetEnvironmentVariable('IG__TestEpic')
    if (-not [string]::IsNullOrWhiteSpace($testEpic)) {
        $env['IG__TestEpic'] = $testEpic
    }

    $testSize = [Environment]::GetEnvironmentVariable('IG__TestSize')
    if (-not [string]::IsNullOrWhiteSpace($testSize)) {
        $env['IG__TestSize'] = $testSize
    }

    $workingLevel = [Environment]::GetEnvironmentVariable('IG__WorkingOrderTestLevel')
    if (-not [string]::IsNullOrWhiteSpace($workingLevel)) {
        $env['IG__WorkingOrderTestLevel'] = $workingLevel
    }

    $searchTerm = [Environment]::GetEnvironmentVariable('IG__MarketSearchTerm')
    if (-not [string]::IsNullOrWhiteSpace($searchTerm)) {
        $env['IG__MarketSearchTerm'] = $searchTerm
    }

    if ($IncludeEncrypted) {
        $env['RUN_IG_ENCRYPTED_INTEGRATION'] = 'true'
    }
    if ($IncludeNavigation) {
        $env['RUN_IG_NAVIGATION_INTEGRATION'] = 'true'
    }
    if ($IncludePrices) {
        $env['RUN_IG_PRICES_INTEGRATION'] = 'true'
    }

    return $env
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$configSummary = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$demo = Assert-DemoConfiguration -ConfigurationSummary $configSummary
$started = (Get-Date).ToUniversalTime()
$safetyPath = Join-Path $run.RunPath 'G08-demo-lifecycle-safety.txt'

if (-not $demo.IsDemoSafe) {
    Set-Content -LiteralPath $safetyPath -Value $demo.Reason -Encoding UTF8
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G08' `
        -Name 'Live IG demo integration suite' `
        -Status 'Blocked' `
        -StartedAtUtc $started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'verify-ig-demo-lifecycle.ps1' `
        -Evidence @($safetyPath) `
        -Summary $demo.Reason `
        -Classification 'missing credentials' `
        -Blocker $demo.Reason
    Write-Host "G08 blocked: $($demo.Reason)"
    return
}

$envOverrides = New-IgEnvironment -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
if ($envOverrides['IG__BaseUrl'] -ne 'https://demo-api.ig.com/gateway/deal') {
    Set-Content -LiteralPath $safetyPath -Value 'Refused to run: effective IG__BaseUrl is not the demo endpoint.' -Encoding UTF8
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G08' `
        -Name 'Live IG demo integration suite' `
        -Status 'Blocked' `
        -StartedAtUtc $started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'verify-ig-demo-lifecycle.ps1' `
        -Evidence @($safetyPath) `
        -Summary 'Refused to run because the effective IG endpoint is not demo.' `
        -Classification 'configuration defect' `
        -Blocker 'Non-demo IG endpoint'
    return
}

$safety = @(
    'Demo lifecycle safety preflight passed.',
    "Base URL: $($envOverrides['IG__BaseUrl'])",
    "Configured account: $(ConvertTo-RedactedAccountId -Value $envOverrides['IG__AccountId'])",
    "Optional encrypted auth: $IncludeEncrypted",
    "Optional navigation: $IncludeNavigation",
    "Optional prices: $IncludePrices",
    'Secrets are passed to the child process environment and are not written to evidence.'
) -join "`r`n"
Set-Content -LiteralPath $safetyPath -Value $safety -Encoding UTF8

$beforePositions = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- positions list' `
    -OutputPath (Join-Path $run.RunPath 'G08-before-positions.log') `
    -TimeoutSeconds 180

$beforeOrders = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- working list' `
    -OutputPath (Join-Path $run.RunPath 'G08-before-working-orders.log') `
    -TimeoutSeconds 180

$testResults = Join-Path $run.RunPath 'test-results\ig-integration'
New-Item -ItemType Directory -Force -Path $testResults | Out-Null
$testCommand = "dotnet test tests/Trading.IG.Tests/Trading.IG.Tests.csproj --no-build --filter `"Category=Integration`" --logger `"trx;LogFileName=ig-integration.trx`" --results-directory `"$testResults`""
$tests = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $testCommand `
    -OutputPath (Join-Path $run.RunPath 'G08-ig-integration-tests.log') `
    -Environment $envOverrides `
    -TimeoutSeconds 1800

$afterPositions = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- positions list' `
    -OutputPath (Join-Path $run.RunPath 'G08-after-positions.log') `
    -TimeoutSeconds 180

$afterOrders = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- working list' `
    -OutputPath (Join-Path $run.RunPath 'G08-after-working-orders.log') `
    -TimeoutSeconds 180

$trxFiles = @(Get-ChildItem -LiteralPath $testResults -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$marketUnavailable = $tests.Output -match 'Market is not tradeable|Status:\s*(EDITS_ONLY|CLOSED|OFFLINE)'
$status = if ($tests.ExitCode -eq 0) { 'Passed' } elseif ($marketUnavailable) { 'Blocked' } else { 'Failed' }
$classification = if ($tests.ExitCode -eq 0) { $null } elseif ($marketUnavailable) { 'market closed' } else { 'IG account entitlement' }
$blocker = if ($marketUnavailable) { 'Configured demo test market was not tradeable during the run.' } else { $null }

$summary = if ($tests.ExitCode -eq 0) {
    'IG demo integration tests completed. Before/after broker state captured; inspect logs for unrelated pre-existing demo state.'
}
elseif ($marketUnavailable) {
    'IG demo integration lifecycle was blocked because the configured test market was not tradeable.'
}
else {
    'IG demo integration tests failed or timed out. Before/after broker state captured for cleanup diagnosis.'
}

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G08' `
    -Name 'Live IG demo integration suite' `
    -Status $status `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $testCommand `
    -ExitCode $tests.ExitCode `
    -Evidence (@($safetyPath, $beforePositions.OutputPath, $beforeOrders.OutputPath, $tests.OutputPath, $afterPositions.OutputPath, $afterOrders.OutputPath) + $trxFiles) `
    -Summary $summary `
    -Classification $classification `
    -Blocker $blocker

Write-Host "G08 complete for run $($run.RunId)"
