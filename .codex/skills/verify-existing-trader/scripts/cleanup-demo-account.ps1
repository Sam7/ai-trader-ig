param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()
$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$config = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$demo = Assert-DemoConfiguration -ConfigurationSummary $config
$summaryPath = Join-Path $run.RunPath 'cleanup-demo-account-summary.txt'

if (-not $demo.IsDemoSafe) {
    Set-Content -LiteralPath $summaryPath -Value "Cleanup refused: $($demo.Reason)" -Encoding UTF8
    Write-Host "Cleanup refused: $($demo.Reason)"
    return
}

$positions = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- positions list' `
    -OutputPath (Join-Path $run.RunPath 'cleanup-positions.log') `
    -TimeoutSeconds 180

$orders = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- working list' `
    -OutputPath (Join-Path $run.RunPath 'cleanup-working-orders.log') `
    -TimeoutSeconds 180

Set-Content -LiteralPath $summaryPath -Value @(
    "Started UTC: $($started.ToString('o'))",
    'This cleanup script only lists current demo positions and working orders.',
    'It does not close/cancel anything automatically because unrelated user-created demo state cannot be identified with certainty from repository evidence alone.',
    "Positions exit code: $($positions.ExitCode)",
    "Working orders exit code: $($orders.ExitCode)"
) -Encoding UTF8

Write-Host "Demo account inspected. Evidence: $summaryPath"
