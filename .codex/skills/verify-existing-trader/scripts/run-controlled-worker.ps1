param(
    [string]$RunId = '',
    [int]$DurationMinutes = 25
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function New-ControlledCron {
    param([datetimeoffset]$When)
    return "0 $($When.Minute) $($When.Hour) * * *"
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$started = (Get-Date).ToUniversalTime()
$nowLocal = [DateTimeOffset]::Now
$dailyAtRaw = $nowLocal.AddMinutes(3)
$dailyAt = [DateTimeOffset]::new(
    $dailyAtRaw.Year,
    $dailyAtRaw.Month,
    $dailyAtRaw.Day,
    $dailyAtRaw.Hour,
    $dailyAtRaw.Minute,
    0,
    $dailyAtRaw.Offset)
$intradayCron = '0 */5 * * * *'
$dailyCron = New-ControlledCron -When $dailyAt

$overrides = @{
    Automation__Enabled = 'true'
    Automation__Timezone = 'Australia/Melbourne'
    Automation__DailyBriefCron = $dailyCron
    Automation__IntradayOpportunities__Enabled = 'true'
    Automation__IntradayOpportunities__Cron = $intradayCron
    AI__DailyBriefing__TrackedMarketsConfigFile = 'tracked-markets.verification.json'
}

$overridePath = Join-Path $run.RunPath 'G14-worker-overrides.json'
Write-JsonFile -Path $overridePath -Value ([ordered]@{
    automationEnabled = $true
    timezone = 'Australia/Melbourne'
    dailyBriefCron = $dailyCron
    intradayCron = $intradayCron
    trackedMarketsConfigFile = 'tracked-markets.verification.json'
    durationMinutes = $DurationMinutes
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
})

$workerLog = Join-Path $run.RunPath 'G14-worker.log'
$command = 'dotnet run --project src/Trading.Cli -- automation run'
$worker = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $command `
    -OutputPath $workerLog `
    -Environment $overrides `
    -TimeoutSeconds ($DurationMinutes * 60)

$required = [ordered]@{
    registeredDaily = 'Registered daily briefing schedule'
    registeredIntraday = 'Registered intraday opportunity schedule'
    ranDaily = 'Running scheduled daily briefing job'
    plannedDay = 'Planning trading day'
    ranIntraday = 'Running scheduled intraday opportunity scan'
    preparedIntraday = 'Prepared intraday opportunity review'
    submittedIntraday = 'Submitted intraday opportunity review'
    terminalOutcome = 'Decision logic pending'
}

$observed = [ordered]@{}
foreach ($key in $required.Keys) {
    $observed[$key] = $worker.Output.Contains($required[$key])
}

$sequencePath = Join-Path $run.RunPath 'G14-worker-sequence.json'
Write-JsonFile -Path $sequencePath -Value ([ordered]@{
    commandExitCode = $worker.ExitCode
    timedOutByDesign = $worker.ExitCode -eq 124
    observed = $observed
    finalConfirmedStage = (@($observed.GetEnumerator() | Where-Object { $_.Value }) | Select-Object -Last 1).Name
})

$completeCycle = -not (@($observed.Values | Where-Object { -not $_ }).Count -gt 0)
$g14Status = if ($completeCycle) { 'Passed' } else { 'Blocked' }
$missing = @($observed.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object { $_.Key })

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G14' `
    -Name 'Controlled same-process scheduler run' `
    -Status $g14Status `
    -StartedAtUtc $started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $command `
    -ExitCode $worker.ExitCode `
    -Evidence @($overridePath, $workerLog, $sequencePath) `
    -Summary $(if ($completeCycle) { 'Worker completed one same-process daily-plan-to-intraday-review cycle.' } else { 'Worker did not demonstrate the full required sequence. Missing: ' + ($missing -join ', ') }) `
    -Classification $(if ($completeCycle) { $null } else { 'scheduler failure' }) `
    -Blocker $(if ($completeCycle) { $null } else { 'Full worker sequence not observed during controlled run window.' })

$g15Started = (Get-Date).ToUniversalTime()
$cycleMatches = [regex]::Matches($worker.Output, 'Running scheduled intraday opportunity scan job')
$terminalMatches = [regex]::Matches($worker.Output, 'Decision logic pending|Skipping intraday opportunity scan')
$g15Passed = $cycleMatches.Count -ge 3 -and $terminalMatches.Count -ge 3
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G15' `
    -Name 'Repeated active-session run' `
    -Status $(if ($g15Passed) { 'Passed' } else { 'Blocked' }) `
    -StartedAtUtc $g15Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $command `
    -ExitCode $worker.ExitCode `
    -Evidence @($workerLog, $sequencePath) `
    -Summary "Observed $($cycleMatches.Count) intraday job starts and $($terminalMatches.Count) explicit terminal outcomes in the controlled worker log." `
    -Classification $(if ($g15Passed) { $null } else { 'market closed' }) `
    -Blocker $(if ($g15Passed) { $null } else { 'At least three complete or explicitly skipped active-session cycles were not observed.' })

$g16Started = (Get-Date).ToUniversalTime()
$restartPath = Join-Path $run.RunPath 'G16-restart-behaviour.txt'
Set-Content -LiteralPath $restartPath -Value @(
    'G16 restart behaviour is intentionally not fully automated by this script because it requires stopping and restarting the worker around scheduler timing.',
    'Expected current behaviour: the in-memory trading-day plan is lost across restart, and intraday scanning skips until planning runs again.',
    'Use a controlled worker run, stop after plan plus one intraday cycle, restart before the next daily briefing, and capture the no-plan skip reason.'
) -Encoding UTF8
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G16' `
    -Name 'Restart behaviour' `
    -Status 'Blocked' `
    -StartedAtUtc $g16Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'manual restart procedure required' `
    -Evidence @($restartPath) `
    -Summary 'Restart limitation documented, but the stop/restart observation has not been captured by this script.' `
    -Classification 'in-memory state limitation' `
    -Blocker 'Manual controlled restart observation required.'

Write-Host "G14-G16 complete for run $($run.RunId)"
