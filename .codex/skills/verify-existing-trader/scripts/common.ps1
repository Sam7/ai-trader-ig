Set-StrictMode -Version 2.0

$script:KnownSecretKeys = @(
    'IG__ApiKey',
    'IG__Identifier',
    'IG__Password',
    'IG__AccountId',
    'AI__OpenAI__ApiKey',
    'OpenAI__ApiKey',
    'OPENAI_API_KEY'
)

function Get-VerificationRepositoryRoot {
    param([string]$StartPath = (Get-Location).Path)

    $current = (Resolve-Path -LiteralPath $StartPath).Path
    while ($true) {
        if ((Test-Path -LiteralPath (Join-Path $current 'Trading.slnx')) -and
            (Test-Path -LiteralPath (Join-Path $current 'AGENTS.md'))) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not find repository root from '$StartPath'."
        }

        $current = $parent
    }
}

function New-VerificationRunId {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    return "$stamp-$suffix"
}

function Get-VerificationRootPath {
    param([string]$RepositoryRoot)
    return Join-Path $RepositoryRoot 'artifacts\verification'
}

function Get-VerificationRunPath {
    param(
        [string]$RepositoryRoot,
        [string]$RunId
    )

    return Join-Path (Get-VerificationRootPath -RepositoryRoot $RepositoryRoot) $RunId
}

function Get-LatestVerificationRunId {
    param([string]$RepositoryRoot)

    $root = Get-VerificationRootPath -RepositoryRoot $RepositoryRoot
    if (-not (Test-Path -LiteralPath $root)) {
        return $null
    }

    $latest = Get-ChildItem -LiteralPath $root -Directory |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return $null
    }

    return $latest.Name
}

function Invoke-GitText {
    param(
        [string]$RepositoryRoot,
        [string[]]$Arguments
    )

    $previous = Get-Location
    try {
        Set-Location -LiteralPath $RepositoryRoot
        $output = & git @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            return ''
        }

        return ($output -join "`n").Trim()
    }
    finally {
        Set-Location -LiteralPath $previous
    }
}

function Write-JsonFile {
    param(
        [string]$Path,
        [object]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $json = Get-Content -Raw -LiteralPath $Path
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    return $json | ConvertFrom-Json
}

function Initialize-VerificationRun {
    param(
        [string]$RepositoryRoot,
        [string]$RunId = ''
    )

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $RunId = New-VerificationRunId
    }

    $runPath = Get-VerificationRunPath -RepositoryRoot $RepositoryRoot -RunId $RunId
    New-Item -ItemType Directory -Force -Path $runPath | Out-Null

    $ledgerPath = Join-Path $runPath 'verification.json'
    if (-not (Test-Path -LiteralPath $ledgerPath)) {
        $branch = Invoke-GitText -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse', '--abbrev-ref', 'HEAD')
        $commit = Invoke-GitText -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse', 'HEAD')
        $sdk = (& dotnet --version 2>$null) -join ''
        $ledger = [ordered]@{
            runId = $RunId
            startedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            completedAtUtc = $null
            gitCommit = $commit
            branch = $branch
            dotnetSdk = $sdk
            environment = 'Unknown'
            overallStatus = 'InProgress'
            gates = @()
        }

        Write-JsonFile -Path $ledgerPath -Value $ledger
    }

    return [pscustomobject]@{
        RepositoryRoot = $RepositoryRoot
        RunId = $RunId
        RunPath = $runPath
        LedgerPath = $ledgerPath
    }
}

function Resolve-VerificationRun {
    param(
        [string]$RepositoryRoot,
        [string]$RunId = '',
        [switch]$Create
    )

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        if ($Create) {
            return Initialize-VerificationRun -RepositoryRoot $RepositoryRoot
        }

        $RunId = Get-LatestVerificationRunId -RepositoryRoot $RepositoryRoot
        if ([string]::IsNullOrWhiteSpace($RunId)) {
            return Initialize-VerificationRun -RepositoryRoot $RepositoryRoot
        }
    }

    return Initialize-VerificationRun -RepositoryRoot $RepositoryRoot -RunId $RunId
}

function Redact-Text {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return $Text
    }

    $redacted = $Text
    foreach ($key in $script:KnownSecretKeys) {
        $value = [Environment]::GetEnvironmentVariable($key)
        if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Length -gt 2) {
            $redacted = $redacted.Replace($value, "[REDACTED:$key]")
        }
    }

    $redacted = [regex]::Replace($redacted, '(?i)(ApiKey\s*["'':=]\s*["'']?)[^"'',\s\}]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(Password\s*["'':=]\s*["'']?)[^"'',\s\}]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(X-IG-API-KEY\s*[:=]\s*)[^\s,;]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(CST\s*[:=]\s*)[^\s,;]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(X-SECURITY-TOKEN\s*[:=]\s*)[^\s,;]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(OpenAI[^:=]*ApiKey\s*[:=]\s*)[^\s,;]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, 'sk-[A-Za-z0-9_\-]{12,}', '[REDACTED:OPENAI_KEY]')
    $redacted = [regex]::Replace($redacted, '(?im)(\bAccount\b\s+)[A-Za-z0-9_\-]{4,}', '$1[REDACTED]')
    return $redacted
}

function ConvertTo-RedactedAccountId {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    if ($Value.Length -le 4) {
        return '****'
    }

    return ('*' * [Math]::Max(0, $Value.Length - 4)) + $Value.Substring($Value.Length - 4)
}

function ConvertTo-RunRelativePath {
    param(
        [string]$RunPath,
        [string]$Path
    )

    $fullRun = [System.IO.Path]::GetFullPath($RunPath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRun, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRun.Length).Replace('\', '/')
    }

    return $fullPath
}

function Invoke-VerificationCommand {
    param(
        [string]$RepositoryRoot,
        [string]$RunPath,
        [string]$Command,
        [string]$OutputPath,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 0
    )

    $started = (Get-Date).ToUniversalTime()
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = 'powershell.exe'
    $process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $process.StartInfo.WorkingDirectory = $RepositoryRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true

    foreach ($key in $Environment.Keys) {
        $process.StartInfo.Environment[$key] = [string]$Environment[$key]
    }

    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $timedOut = $false
    if ($TimeoutSeconds -gt 0) {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $timedOut = $true
            try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
            $process.WaitForExit()
        }
    }
    else {
        $process.WaitForExit()
    }

    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
    $completed = (Get-Date).ToUniversalTime()
    $elapsed = [Math]::Round(($completed - $started).TotalSeconds, 3)

    $content = @(
        "Command: $Command",
        "StartedAtUtc: $($started.ToString('o'))",
        "CompletedAtUtc: $($completed.ToString('o'))",
        "ElapsedSeconds: $elapsed",
        "ExitCode: $exitCode",
        "TimedOut: $timedOut",
        '',
        '--- STDOUT ---',
        $stdout,
        '',
        '--- STDERR ---',
        $stderr
    ) -join "`r`n"

    $redacted = Redact-Text -Text $content
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    Set-Content -LiteralPath $OutputPath -Value $redacted -Encoding UTF8

    return [pscustomobject]@{
        Command = $Command
        StartedAtUtc = $started
        CompletedAtUtc = $completed
        ExitCode = $exitCode
        TimedOut = $timedOut
        OutputPath = $OutputPath
        Output = $redacted
    }
}

function Update-VerificationGate {
    param(
        [string]$LedgerPath,
        [string]$RunPath,
        [string]$Id,
        [string]$Name,
        [ValidateSet('Passed', 'Failed', 'Blocked', 'NotApplicable', 'IntentionallyUnimplemented')]
        [string]$Status,
        [datetime]$StartedAtUtc,
        [datetime]$CompletedAtUtc,
        [string]$Command = '',
        [Nullable[int]]$ExitCode = $null,
        [string[]]$Evidence = @(),
        [string]$Summary = '',
        [string]$Classification = $null,
        [string]$Blocker = $null
    )

    $ledger = Read-JsonFile -Path $LedgerPath
    if ($null -eq $ledger) {
        throw "Ledger not found at '$LedgerPath'."
    }

    $existing = @($ledger.gates | Where-Object { $_.id -ne $Id })
    $relativeEvidence = @($Evidence | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        ConvertTo-RunRelativePath -RunPath $RunPath -Path $_
    })

    $gate = [ordered]@{
        id = $Id
        name = $Name
        status = $Status
        startedAtUtc = $StartedAtUtc.ToUniversalTime().ToString('o')
        completedAtUtc = $CompletedAtUtc.ToUniversalTime().ToString('o')
        command = $Command
        exitCode = $ExitCode
        evidence = $relativeEvidence
        summary = $Summary
        classification = $Classification
        blocker = $Blocker
    }

    $ledger.gates = @($existing) + @($gate)
    $ledger.gates = @($ledger.gates | Sort-Object id)
    Write-JsonFile -Path $LedgerPath -Value $ledger
    Write-VerificationReport -LedgerPath $LedgerPath -RunPath $RunPath
}

function Complete-VerificationLedger {
    param(
        [string]$LedgerPath,
        [string]$RunPath
    )

    $ledger = Read-JsonFile -Path $LedgerPath
    if ($null -eq $ledger) {
        throw "Ledger not found at '$LedgerPath'."
    }

    $gates = @($ledger.gates)
    if ($gates.Count -eq 0) {
        $overall = 'Blocked'
    }
    elseif ($gates.status -contains 'Failed') {
        $overall = 'Failed'
    }
    elseif ($gates.status -contains 'Blocked') {
        $overall = 'Blocked'
    }
    elseif (($gates | Where-Object { $_.status -eq 'Passed' }).Count -gt 0) {
        $overall = 'Passed'
    }
    else {
        $overall = 'Blocked'
    }

    $ledger.overallStatus = $overall
    $ledger.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Write-JsonFile -Path $LedgerPath -Value $ledger
    Write-VerificationReport -LedgerPath $LedgerPath -RunPath $RunPath
}

function Write-VerificationReport {
    param(
        [string]$LedgerPath,
        [string]$RunPath
    )

    $ledger = Read-JsonFile -Path $LedgerPath
    if ($null -eq $ledger) {
        return
    }

    $reportPath = Join-Path $RunPath 'REPORT.md'
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# AI Trader IG Verification Report')
    $lines.Add('')
    $lines.Add('Run ID: `' + $ledger.runId + '`')
    $lines.Add("Overall status: **$($ledger.overallStatus)**")
    $lines.Add('Started UTC: `' + $ledger.startedAtUtc + '`')
    $lines.Add('Completed UTC: `' + $ledger.completedAtUtc + '`')
    $lines.Add('Branch: `' + $ledger.branch + '`')
    $lines.Add('Commit: `' + $ledger.gitCommit + '`')
    $lines.Add('Environment: `' + $ledger.environment + '`')
    $lines.Add('')
    $lines.Add('## Capability Matrix')
    $lines.Add('')
    $lines.Add('| Gate | Capability | Status | Evidence | Notes |')
    $lines.Add('|---|---|---|---|---|')

    foreach ($gate in @($ledger.gates | Sort-Object id)) {
        $evidence = if ($gate.evidence) {
            (@($gate.evidence) | ForEach-Object { "[$_]($_)" }) -join '<br>'
        }
        else {
            ''
        }

        $notes = ($gate.summary -replace '\|', '\|')
        if (-not [string]::IsNullOrWhiteSpace($gate.blocker)) {
            $notes = "$notes Blocker: $($gate.blocker)"
        }

        $lines.Add("| $($gate.id) | $($gate.name) | $($gate.status) | $evidence | $notes |")
    }

    $lines.Add('')
    $lines.Add('## Failed Or Blocked Capabilities')
    $lines.Add('')
    $problemGates = @($ledger.gates | Where-Object { $_.status -in @('Failed', 'Blocked') } | Sort-Object id)
    if ($problemGates.Count -eq 0) {
        $lines.Add('None recorded.')
    }
    else {
        foreach ($gate in $problemGates) {
            $classification = if ([string]::IsNullOrWhiteSpace($gate.classification)) { 'unclassified' } else { $gate.classification }
            $blocker = if ([string]::IsNullOrWhiteSpace($gate.blocker)) { '' } else { " Blocker: $($gate.blocker)" }
            $lines.Add('- `' + $gate.id + '` ' + $gate.name + ': ' + $gate.status + ', ' + $classification + '.' + $blocker)
        }
    }

    $lines.Add('')
    $lines.Add('## Intentional Limitations')
    $lines.Add('')
    $lines.Add('- No deterministic candidate decision is implemented.')
    $lines.Add('- No strategy-generated order execution is expected.')
    $lines.Add('- Trading-day state is in memory and is lost across worker restart.')
    $lines.Add('- Profitability, streaming data, backtesting, dashboards, and notifications are outside this verification.')
    $lines.Add('')
    $lines.Add('## Repeat Instructions')
    $lines.Add('')
    $lines.Add('```powershell')
    $lines.Add('powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/inspect-repository.ps1')
    $lines.Add("powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-build-and-tests.ps1 -RunId $($ledger.runId)")
    $lines.Add("powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-configuration.ps1 -RunId $($ledger.runId)")
    $lines.Add("powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/summarize-verification.ps1 -RunId $($ledger.runId)")
    $lines.Add('```')

    Set-Content -LiteralPath $reportPath -Value ($lines -join "`r`n") -Encoding UTF8
}

function Get-UserSecretsId {
    param([string]$ProjectPath)

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        return $null
    }

    [xml]$xml = Get-Content -Raw -LiteralPath $ProjectPath
    $node = $xml.Project.PropertyGroup.UserSecretsId | Select-Object -First 1
    if ($null -eq $node) {
        return $null
    }

    return [string]$node
}

function Get-UserSecretsPath {
    param([string]$UserSecretsId)

    if ([string]::IsNullOrWhiteSpace($UserSecretsId)) {
        return $null
    }

    $appData = [Environment]::GetFolderPath('ApplicationData')
    if ([string]::IsNullOrWhiteSpace($appData)) {
        return $null
    }

    return Join-Path $appData "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
}

function ConvertTo-Hashtable {
    param([object]$Value)

    if ($null -eq $Value) {
        return @{}
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $hash = @{}
        foreach ($key in $Value.Keys) {
            $hash[$key] = ConvertTo-Hashtable $Value[$key]
        }
        return $hash
    }

    if ($Value -is [pscustomobject]) {
        $hash = @{}
        foreach ($property in $Value.PSObject.Properties) {
            $hash[$property.Name] = ConvertTo-Hashtable $property.Value
        }
        return $hash
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return @($Value | ForEach-Object { ConvertTo-Hashtable $_ })
    }

    return $Value
}

function Read-JsonHashtable {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @{}
    }

    try {
        $parsed = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
        return ConvertTo-Hashtable $parsed
    }
    catch {
        return @{}
    }
}

function Merge-Hashtable {
    param(
        [hashtable]$Base,
        [hashtable]$Overlay
    )

    foreach ($key in $Overlay.Keys) {
        if ($Base.ContainsKey($key) -and $Base[$key] -is [hashtable] -and $Overlay[$key] -is [hashtable]) {
            Merge-Hashtable -Base $Base[$key] -Overlay $Overlay[$key]
        }
        else {
            $Base[$key] = $Overlay[$key]
        }
    }
}

function Set-NestedValue {
    param(
        [hashtable]$Target,
        [string]$Key,
        [object]$Value
    )

    $parts = $Key -split '__'
    if ($parts.Count -lt 2) {
        return
    }

    $cursor = $Target
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
        $part = $parts[$i]
        if (-not $cursor.ContainsKey($part) -or $cursor[$part] -isnot [hashtable]) {
            $cursor[$part] = @{}
        }

        $cursor = $cursor[$part]
    }

    $cursor[$parts[-1]] = $Value
}

function Get-NestedValue {
    param(
        [hashtable]$Source,
        [string]$Path
    )

    $cursor = $Source
    foreach ($part in ($Path -split ':')) {
        if ($cursor -isnot [hashtable] -or -not $cursor.ContainsKey($part)) {
            return $null
        }

        $cursor = $cursor[$part]
    }

    return $cursor
}

function Get-EffectiveConfigurationSummary {
    param(
        [string]$RepositoryRoot,
        [string]$ProjectPath
    )

    $effective = Get-EffectiveConfiguration -RepositoryRoot $RepositoryRoot -ProjectPath $ProjectPath
    $config = $effective.Configuration
    $userSecretsId = $effective.UserSecretsId
    $userSecretsPath = $effective.UserSecretsPath

    $trackedMarkets = Get-NestedValue -Source $config -Path 'AI:DailyBriefing:TrackedMarkets'
    $baseUrl = [string](Get-NestedValue -Source $config -Path 'IG:BaseUrl')
    $accountId = [string](Get-NestedValue -Source $config -Path 'IG:AccountId')
    $observabilityRootPath = [string](Get-NestedValue -Source $config -Path 'AI:Prompts:ObservabilityRootPath')
    if ([string]::IsNullOrWhiteSpace($observabilityRootPath)) {
        $observabilityRootPath = 'Logs/Observability'
    }

    $intradayEnabled = [string](Get-NestedValue -Source $config -Path 'Automation:IntradayOpportunities:Enabled')
    if ([string]::IsNullOrWhiteSpace($intradayEnabled)) { $intradayEnabled = 'True' }

    $intradayCron = [string](Get-NestedValue -Source $config -Path 'Automation:IntradayOpportunities:Cron')
    if ([string]::IsNullOrWhiteSpace($intradayCron)) { $intradayCron = '0 */15 * * * *' }

    $chartResolution = [string](Get-NestedValue -Source $config -Path 'Automation:IntradayOpportunities:ChartResolution')
    if ([string]::IsNullOrWhiteSpace($chartResolution)) { $chartResolution = 'TenMinutes' }

    $freshPriceMaxAgeMinutes = [string](Get-NestedValue -Source $config -Path 'Automation:IntradayOpportunities:FreshPriceMaxAgeMinutes')
    if ([string]::IsNullOrWhiteSpace($freshPriceMaxAgeMinutes)) { $freshPriceMaxAgeMinutes = '20' }

    return [ordered]@{
        projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
        userSecretsId = $userSecretsId
        userSecretsFilePresent = -not [string]::IsNullOrWhiteSpace($userSecretsPath) -and (Test-Path -LiteralPath $userSecretsPath)
        ig = [ordered]@{
            baseUrl = $baseUrl
            isDemoEndpoint = $baseUrl -eq 'https://demo-api.ig.com/gateway/deal'
            apiKeyConfigured = -not [string]::IsNullOrWhiteSpace([string](Get-NestedValue -Source $config -Path 'IG:ApiKey'))
            identifierConfigured = -not [string]::IsNullOrWhiteSpace([string](Get-NestedValue -Source $config -Path 'IG:Identifier'))
            passwordConfigured = -not [string]::IsNullOrWhiteSpace([string](Get-NestedValue -Source $config -Path 'IG:Password'))
            accountIdConfigured = -not [string]::IsNullOrWhiteSpace($accountId)
            accountIdRedacted = ConvertTo-RedactedAccountId -Value $accountId
            useEncryptedPassword = [string](Get-NestedValue -Source $config -Path 'IG:UseEncryptedPassword')
        }
        openAi = [ordered]@{
            apiKeyConfigured = -not [string]::IsNullOrWhiteSpace([string](Get-NestedValue -Source $config -Path 'AI:OpenAI:ApiKey'))
            requestTimeout = [string](Get-NestedValue -Source $config -Path 'AI:OpenAI:RequestTimeout')
            researchModel = [string](Get-NestedValue -Source $config -Path 'AI:DailyBriefing:Research:ModelId')
            planModel = [string](Get-NestedValue -Source $config -Path 'AI:DailyBriefing:PlanJson:ModelId')
            intradayModel = [string](Get-NestedValue -Source $config -Path 'AI:IntradayOpportunityReview:ModelId')
        }
        automation = [ordered]@{
            enabled = [string](Get-NestedValue -Source $config -Path 'Automation:Enabled')
            timezone = [string](Get-NestedValue -Source $config -Path 'Automation:Timezone')
            dailyBriefCron = [string](Get-NestedValue -Source $config -Path 'Automation:DailyBriefCron')
            intradayEnabled = $intradayEnabled
            intradayCron = $intradayCron
            chartResolution = $chartResolution
            freshPriceMaxAgeMinutes = $freshPriceMaxAgeMinutes
        }
        trackedMarkets = [ordered]@{
            configFilePresent = Test-Path -LiteralPath (Join-Path $RepositoryRoot 'tracked-markets.json')
            count = if ($trackedMarkets -is [System.Array]) { $trackedMarkets.Count } elseif ($null -eq $trackedMarkets) { 0 } else { 1 }
            instruments = if ($trackedMarkets -is [System.Array]) { @($trackedMarkets | ForEach-Object { $_['InstrumentId'] }) } else { @() }
        }
        observability = [ordered]@{
            rootPath = $observabilityRootPath
        }
    }
}

function Test-TimeZoneConfigured {
    param([string]$TimezoneId)

    if ([string]::IsNullOrWhiteSpace($TimezoneId)) {
        return [pscustomobject]@{ Resolved = $false; ResolvedId = $null; Error = 'Timezone is not configured.' }
    }

    foreach ($candidate in @($TimezoneId, (Convert-IanaTimeZoneToWindows -TimezoneId $TimezoneId))) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        try {
            $timezone = [TimeZoneInfo]::FindSystemTimeZoneById($candidate)
            return [pscustomobject]@{ Resolved = $true; ResolvedId = $timezone.Id; Error = $null }
        }
        catch {
        }
    }

    return [pscustomobject]@{ Resolved = $false; ResolvedId = $null; Error = "Timezone '$TimezoneId' did not resolve in this host." }
}

function Convert-IanaTimeZoneToWindows {
    param([string]$TimezoneId)

    switch ($TimezoneId) {
        'Australia/Melbourne' { return 'AUS Eastern Standard Time' }
        'Australia/Sydney' { return 'AUS Eastern Standard Time' }
        default { return $null }
    }
}

function Get-EffectiveConfiguration {
    param(
        [string]$RepositoryRoot,
        [string]$ProjectPath
    )

    $config = @{}
    Merge-Hashtable -Base $config -Overlay (Read-JsonHashtable -Path (Join-Path $RepositoryRoot 'appsettings.json'))

    foreach ($item in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ([string]$item.Key -like '*__*') {
            Set-NestedValue -Target $config -Key ([string]$item.Key) -Value ([string]$item.Value)
        }
    }

    foreach ($file in @('appsettings.local.json', 'tracked-markets.json')) {
        $path = Join-Path $RepositoryRoot $file
        Merge-Hashtable -Base $config -Overlay (Read-JsonHashtable -Path $path)
    }

    $userSecretsId = Get-UserSecretsId -ProjectPath $ProjectPath
    $userSecretsPath = Get-UserSecretsPath -UserSecretsId $userSecretsId
    $userSecrets = Read-JsonHashtable -Path $userSecretsPath
    Merge-Hashtable -Base $config -Overlay $userSecrets

    return [pscustomobject]@{
        Configuration = $config
        UserSecretsId = $userSecretsId
        UserSecretsPath = $userSecretsPath
    }
}

function Assert-DemoConfiguration {
    param([object]$ConfigurationSummary)

    $ig = $ConfigurationSummary.ig
    if (-not $ig.isDemoEndpoint) {
        return [pscustomobject]@{
            IsDemoSafe = $false
            Reason = "IG BaseUrl is not the demo endpoint."
        }
    }

    if (-not $ig.apiKeyConfigured -or -not $ig.identifierConfigured -or -not $ig.passwordConfigured) {
        return [pscustomobject]@{
            IsDemoSafe = $false
            Reason = "IG credentials are not fully configured."
        }
    }

    return [pscustomobject]@{
        IsDemoSafe = $true
        Reason = "IG BaseUrl is the demo endpoint and required IG credentials are configured."
    }
}
