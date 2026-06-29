param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId -Create
$started = (Get-Date).ToUniversalTime()

$repoSummaryPath = Join-Path $run.RunPath 'repository-summary.txt'
$gitStatusPath = Join-Path $run.RunPath 'git-status.txt'
$dotnetInfoPath = Join-Path $run.RunPath 'dotnet-info.txt'
$configSummaryPath = Join-Path $run.RunPath 'redacted-configuration-summary.json'
$demoSafetyPath = Join-Path $run.RunPath 'demo-safety-check.txt'
$secretScanPath = Join-Path $run.RunPath 'tracked-secret-scan.txt'
$ignoredConfigPath = Join-Path $run.RunPath 'ignored-local-config.txt'

$agentsText = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'AGENTS.md')
$branch = Invoke-GitText -RepositoryRoot $repositoryRoot -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')
$commit = Invoke-GitText -RepositoryRoot $repositoryRoot -Arguments @('rev-parse', 'HEAD')
$projectList = (& dotnet sln (Join-Path $repositoryRoot 'Trading.slnx') list 2>&1) -join "`r`n"

$summary = @(
    "Repository root: $repositoryRoot",
    "Branch: $branch",
    "Commit: $commit",
    "AGENTS.md read: $([bool]$agentsText)",
    '',
    'Solution project inventory:',
    $projectList
) -join "`r`n"
Set-Content -LiteralPath $repoSummaryPath -Value (Redact-Text -Text $summary) -Encoding UTF8

$gitStatus = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'git status --short --branch' `
    -OutputPath $gitStatusPath

$dotnetInfo = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet --info' `
    -OutputPath $dotnetInfoPath

$trackedFiles = @((Invoke-GitText -RepositoryRoot $repositoryRoot -Arguments @('ls-files')) -split "`n" |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$credentialPatterns = '(?i)(sk-[A-Za-z0-9_\-]{12,}|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|api[_-]?key\s*[:=]\s*["''][^"'']{20,}|password\s*[:=]\s*["''][^"'']{20,})'
$matches = New-Object System.Collections.Generic.List[string]
foreach ($file in $trackedFiles) {
    $path = Join-Path $repositoryRoot $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    try {
        Select-String -LiteralPath $path -Pattern $credentialPatterns -AllMatches -ErrorAction Stop |
            ForEach-Object {
                $line = $_.Line.Trim()
                if ($line -match 'YOUR_|test-api-key|test key|ApiKey = "key"|Password = "password"') {
                    return
                }

                $matches.Add(("{0}:{1}:{2}" -f $file, $_.LineNumber, $line))
            }
    }
    catch {
        # Ignore binary or unreadable tracked files.
    }
}

if ($matches.Count -eq 0) {
    Set-Content -LiteralPath $secretScanPath -Value 'No obvious tracked credentials found by heuristic scan.' -Encoding UTF8
}
else {
    Set-Content -LiteralPath $secretScanPath -Value (Redact-Text -Text ($matches -join "`r`n")) -Encoding UTF8
}

$ignoredLines = New-Object System.Collections.Generic.List[string]
foreach ($path in @('appsettings.json', 'appsettings.local.json', 'appsettings.*.local.json', 'artifacts/verification/example', 'Logs/Verification/example')) {
    $command = "git check-ignore -v '$path'"
    $result = Invoke-VerificationCommand `
        -RepositoryRoot $repositoryRoot `
        -RunPath $run.RunPath `
        -Command $command `
        -OutputPath (Join-Path $run.RunPath ("check-ignore-" + ($path -replace '[^A-Za-z0-9]+', '-') + '.txt'))
    $ignoredLines.Add("Path: $path")
    $ignoredLines.Add("ExitCode: $($result.ExitCode)")
    $ignoredLines.Add(($result.Output -split "`r?`n" | Select-Object -Last 5) -join "`r`n")
    $ignoredLines.Add('')
}
Set-Content -LiteralPath $ignoredConfigPath -Value (Redact-Text -Text ($ignoredLines -join "`r`n")) -Encoding UTF8

$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$workerProject = Join-Path $repositoryRoot 'src\Trading.Automation\Trading.Automation.csproj'
$cliConfig = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$workerConfig = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $workerProject
$demoSafety = Assert-DemoConfiguration -ConfigurationSummary $workerConfig

$redactedSummary = [ordered]@{
    cli = $cliConfig
    automation = $workerConfig
    demoSafety = [ordered]@{
        isDemoSafe = $demoSafety.IsDemoSafe
        reason = $demoSafety.Reason
        mutatingBrokerGates = if ($demoSafety.IsDemoSafe) { 'Allowed after account proof' } else { 'Blocked' }
    }
}
Write-JsonFile -Path $configSummaryPath -Value $redactedSummary

$demoText = @(
    "Demo safe: $($demoSafety.IsDemoSafe)",
    "Reason: $($demoSafety.Reason)",
    "CLI BaseUrl: $($cliConfig.ig.baseUrl)",
    "Automation BaseUrl: $($workerConfig.ig.baseUrl)",
    "CLI Account ID: $($cliConfig.ig.accountIdRedacted)",
    "Automation Account ID: $($workerConfig.ig.accountIdRedacted)",
    "Broker-mutating gates: $(if ($demoSafety.IsDemoSafe) { 'not yet run; require live account proof before execution' } else { 'blocked' })"
) -join "`r`n"
Set-Content -LiteralPath $demoSafetyPath -Value (Redact-Text -Text $demoText) -Encoding UTF8

$ledger = Read-JsonFile -Path $run.LedgerPath
$ledger.environment = if ($workerConfig.ig.isDemoEndpoint) { 'IG Demo' } else { 'Unknown' }
Write-JsonFile -Path $run.LedgerPath -Value $ledger

$status = if ($matches.Count -eq 0) { 'Passed' } else { 'Failed' }
$classification = if ($matches.Count -eq 0) { $null } else { 'security' }
$summaryText = if ($matches.Count -eq 0) {
    "Repository baseline captured; tracked secret scan found no obvious credentials; demo safety is $($demoSafety.IsDemoSafe)."
}
else {
    "Repository baseline captured, but tracked secret scan found $($matches.Count) suspicious lines."
}

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G00' `
    -Name 'Repository and safety baseline' `
    -Status $status `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'inspect-repository.ps1' `
    -ExitCode 0 `
    -Evidence @($repoSummaryPath, $gitStatusPath, $dotnetInfoPath, $configSummaryPath, $demoSafetyPath, $secretScanPath, $ignoredConfigPath) `
    -Summary $summaryText `
    -Classification $classification

Write-Host "Verification run: $($run.RunId)"
Write-Host "Evidence directory: $($run.RunPath)"
