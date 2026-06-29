param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId

$restoreLog = Join-Path $run.RunPath 'G01-restore.log'
$buildLog = Join-Path $run.RunPath 'G01-build.log'
$testLog = Join-Path $run.RunPath 'G02-non-integration-tests.log'
$testResults = Join-Path $run.RunPath 'test-results\non-integration'
New-Item -ItemType Directory -Force -Path $testResults | Out-Null

$g01Started = (Get-Date).ToUniversalTime()
$restore = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet restore Trading.slnx' `
    -OutputPath $restoreLog `
    -TimeoutSeconds 900

$build = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet build Trading.slnx --no-restore' `
    -OutputPath $buildLog `
    -TimeoutSeconds 900

$g01Passed = $restore.ExitCode -eq 0 -and $build.ExitCode -eq 0
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G01' `
    -Name 'Restore and compile' `
    -Status $(if ($g01Passed) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g01Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'dotnet restore Trading.slnx; dotnet build Trading.slnx --no-restore' `
    -ExitCode $(if ($g01Passed) { 0 } else { 1 }) `
    -Evidence @($restoreLog, $buildLog) `
    -Summary $(if ($g01Passed) { 'Restore and build succeeded for Trading.slnx.' } else { "Restore exit $($restore.ExitCode), build exit $($build.ExitCode)." }) `
    -Classification $(if ($g01Passed) { $null } else { 'code defect' })

$g02Started = (Get-Date).ToUniversalTime()
if (-not $g01Passed) {
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G02' `
        -Name 'Existing automated tests' `
        -Status 'Blocked' `
        -StartedAtUtc $g02Started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'dotnet test Trading.slnx --no-build --filter "Category!=Integration"' `
        -Evidence @($restoreLog, $buildLog) `
        -Summary 'Non-integration tests blocked because G01 build did not pass.' `
        -Classification 'code defect' `
        -Blocker 'Build failed'
    return
}

$testCommand = "dotnet test Trading.slnx --no-build --filter `"Category!=Integration`" --logger `"trx;LogFileName=non-integration.trx`" --results-directory `"$testResults`""
$tests = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $testCommand `
    -OutputPath $testLog `
    -TimeoutSeconds 1200

$trxFiles = @(Get-ChildItem -LiteralPath $testResults -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$testMatches = [regex]::Matches($tests.Output, 'Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)')
$summary = if ($testMatches.Count -gt 0) {
    $failed = 0
    $passed = 0
    $skipped = 0
    $total = 0
    foreach ($match in $testMatches) {
        $failed += [int]$match.Groups[1].Value
        $passed += [int]$match.Groups[2].Value
        $skipped += [int]$match.Groups[3].Value
        $total += [int]$match.Groups[4].Value
    }

    "Total $total, passed $passed, failed $failed, skipped $skipped across $($testMatches.Count) test projects."
}
else {
    "dotnet test exit code $($tests.ExitCode). See sanitized log and TRX files."
}

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G02' `
    -Name 'Existing automated tests' `
    -Status $(if ($tests.ExitCode -eq 0) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g02Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $testCommand `
    -ExitCode $tests.ExitCode `
    -Evidence (@($testLog) + $trxFiles) `
    -Summary $summary `
    -Classification $(if ($tests.ExitCode -eq 0) { $null } else { 'code defect' })

Write-Host "G01/G02 complete for run $($run.RunId)"
