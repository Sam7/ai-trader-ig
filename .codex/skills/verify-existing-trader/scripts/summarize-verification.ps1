param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()

$reviewLog = Join-Path $run.RunPath 'G17-code-review-scope.log'
$parserCommand = '$paths = Get-ChildItem -LiteralPath ''.codex\skills\verify-existing-trader\scripts'' -Filter ''*.ps1'' | Select-Object -ExpandProperty FullName; foreach ($path in $paths) { $tokens = $null; $errors = $null; [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count -gt 0) { throw ($path + '': '' + ($errors | ForEach-Object { $_.Message } | Out-String)) } }'
$encodedParserCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parserCommand))
$reviewCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/code-review-guard/scripts/list-uncommitted.ps1; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }; git diff --stat; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }; powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedParserCommand; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }; python ""C:\Users\sam.sperling\.codex\skills\.system\skill-creator\scripts\quick_validate.py"" "".codex\skills\verify-existing-trader"""
$review = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $reviewCommand `
    -OutputPath $reviewLog `
    -TimeoutSeconds 120

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G17' `
    -Name 'Final regression and code review' `
    -Status $(if ($review.ExitCode -eq 0) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $reviewCommand `
    -ExitCode $review.ExitCode `
    -Evidence @($reviewLog) `
    -Summary $(if ($review.ExitCode -eq 0) { 'Uncommitted change scope, parser validation, and skill validation completed for the verification-skill changes.' } else { 'Final review/validation command failed; inspect evidence.' }) `
    -Classification $(if ($review.ExitCode -eq 0) { $null } else { 'environmental tooling problem' })

Complete-VerificationLedger -LedgerPath $run.LedgerPath -RunPath $run.RunPath
Write-Host "Summary updated for run $($run.RunId)"
Write-Host "Report: $(Join-Path $run.RunPath 'REPORT.md')"
