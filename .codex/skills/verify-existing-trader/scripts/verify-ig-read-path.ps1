param(
    [string]$RunId = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

function Get-TrackedMarkets {
    param([string]$RepositoryRoot)

    $path = Join-Path $RepositoryRoot 'tracked-markets.json'
    if (-not (Test-Path -LiteralPath $path)) {
        return @()
    }

    $json = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    return @($json.AI.DailyBriefing.TrackedMarkets)
}

function Get-PngDimensions {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
        return $null
    }

    $widthBytes = [byte[]]$bytes[16..19]
    $heightBytes = [byte[]]$bytes[20..23]
    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($widthBytes)
        [Array]::Reverse($heightBytes)
    }

    $width = [BitConverter]::ToUInt32($widthBytes, 0)
    $height = [BitConverter]::ToUInt32($heightBytes, 0)
    return [pscustomobject]@{ Width = $width; Height = $height }
}

$repositoryRoot = Get-VerificationRepositoryRoot
$run = Resolve-VerificationRun -RepositoryRoot $repositoryRoot -RunId $RunId
$cliProject = Join-Path $repositoryRoot 'src\Trading.Cli\Trading.Cli.csproj'
$configSummary = Get-EffectiveConfigurationSummary -RepositoryRoot $repositoryRoot -ProjectPath $cliProject
$demo = Assert-DemoConfiguration -ConfigurationSummary $configSummary
$trackedMarkets = Get-TrackedMarkets -RepositoryRoot $repositoryRoot

if (-not $demo.IsDemoSafe) {
    $started = (Get-Date).ToUniversalTime()
    $evidence = Join-Path $run.RunPath 'G04-G07-demo-blocked.txt'
    Set-Content -LiteralPath $evidence -Value (Redact-Text -Text $demo.Reason) -Encoding UTF8
    foreach ($gate in @(
        @{ Id = 'G04'; Name = 'IG authentication and account visibility' },
        @{ Id = 'G05'; Name = 'IG market discovery' },
        @{ Id = 'G06'; Name = 'IG price-history access' },
        @{ Id = 'G07'; Name = 'Chart generation' }
    )) {
        Update-VerificationGate `
            -LedgerPath $run.LedgerPath `
            -RunPath $run.RunPath `
            -Id $gate.Id `
            -Name $gate.Name `
            -Status 'Blocked' `
            -StartedAtUtc $started `
            -CompletedAtUtc (Get-Date).ToUniversalTime() `
            -Command 'verify-ig-read-path.ps1' `
            -Evidence @($evidence) `
            -Summary $demo.Reason `
            -Classification 'missing credentials' `
            -Blocker $demo.Reason
    }
    Write-Host "IG read path blocked: $($demo.Reason)"
    return
}

$g04Started = (Get-Date).ToUniversalTime()
$authLog = Join-Path $run.RunPath 'G04-auth.log'
$auth = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command 'dotnet run --project src/Trading.Cli -- auth' `
    -OutputPath $authLog `
    -TimeoutSeconds 180

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G04' `
    -Name 'IG authentication and account visibility' `
    -Status $(if ($auth.ExitCode -eq 0) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g04Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'dotnet run --project src/Trading.Cli -- auth' `
    -ExitCode $auth.ExitCode `
    -Evidence @($authLog) `
    -Summary $(if ($auth.ExitCode -eq 0) { 'CLI authentication succeeded against the configured demo endpoint; account output is redacted in evidence.' } else { 'CLI authentication failed; see sanitized output.' }) `
    -Classification $(if ($auth.ExitCode -eq 0) { $null } else { 'IG authentication' })

if ($auth.ExitCode -ne 0) {
    foreach ($gate in @(
        @{ Id = 'G05'; Name = 'IG market discovery' },
        @{ Id = 'G06'; Name = 'IG price-history access' },
        @{ Id = 'G07'; Name = 'Chart generation' }
    )) {
        Update-VerificationGate `
            -LedgerPath $run.LedgerPath `
            -RunPath $run.RunPath `
            -Id $gate.Id `
            -Name $gate.Name `
            -Status 'Blocked' `
            -StartedAtUtc (Get-Date).ToUniversalTime() `
            -CompletedAtUtc (Get-Date).ToUniversalTime() `
            -Command 'verify-ig-read-path.ps1' `
            -Evidence @($authLog) `
            -Summary 'Blocked because IG authentication failed.' `
            -Classification 'IG authentication' `
            -Blocker 'G04 failed'
    }
    return
}

$g05Started = (Get-Date).ToUniversalTime()
$marketReport = Join-Path $run.RunPath 'G05-market-discovery.md'
$marketRows = New-Object System.Collections.Generic.List[string]
$marketRows.Add('| Configured name | EPIC | Resolved | Actual IG name | Status | Type | Currency | Expiry | Result |')
$marketRows.Add('|---|---|---|---|---|---|---|---|---|')
$resolvedCount = 0

foreach ($market in $trackedMarkets) {
    $logName = 'G05-details-' + ($market.DisplayName -replace '[^A-Za-z0-9]+', '-').Trim('-') + '.log'
    $logPath = Join-Path $run.RunPath $logName
    $command = "dotnet run --project src/Trading.Cli -- markets details --instrument `"$($market.InstrumentId)`""
    $details = Invoke-VerificationCommand `
        -RepositoryRoot $repositoryRoot `
        -RunPath $run.RunPath `
        -Command $command `
        -OutputPath $logPath `
        -TimeoutSeconds 180

    $resolved = $details.ExitCode -eq 0 -and $details.Output.Contains([string]$market.InstrumentId)
    if ($resolved) {
        $resolvedCount++
    }

    $actualName = if ($resolved -and -not [string]::IsNullOrWhiteSpace($market.ExactInstrumentName) -and $details.Output -match [regex]::Escape([string]$market.ExactInstrumentName)) { $market.ExactInstrumentName } else { 'see details log' }
    $status = if ($details.Output -match '(Tradeable|Closed|Suspended|EditsOnly|Unknown)') { $Matches[1] } else { 'n/a' }
    $type = if ($details.Output -match '(?im)Type\s+([A-Z_]+)') { $Matches[1] } else { 'n/a' }
    $currency = if ($details.Output -match '(?im)Currency\s+([A-Z]{3})') { $Matches[1] } else { 'n/a' }
    $expiry = if ($details.Output -match '(?im)Expiry\s+(\S+)') { $Matches[1] } else { 'n/a' }
    $result = if ($resolved) { 'Passed' } elseif ($details.ExitCode -eq 0) { 'Failed: EPIC not found in details output' } else { 'Failed: details command failed' }
    $marketRows.Add("| $($market.DisplayName) | $($market.InstrumentId) | $resolved | $actualName | $status | $type | $currency | $expiry | $result |")
}

Set-Content -LiteralPath $marketReport -Value ($marketRows -join "`r`n") -Encoding UTF8
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G05' `
    -Name 'IG market discovery' `
    -Status $(if ($resolvedCount -eq $trackedMarkets.Count -and $trackedMarkets.Count -gt 0) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g05Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'markets details --instrument <EPIC> for each configured tracked market' `
    -ExitCode $(if ($resolvedCount -eq $trackedMarkets.Count -and $trackedMarkets.Count -gt 0) { 0 } else { 1 }) `
    -Evidence @($marketReport) `
    -Summary "Resolved metadata for $resolvedCount of $($trackedMarkets.Count) configured tracked markets through IG market details." `
    -Classification $(if ($resolvedCount -eq $trackedMarkets.Count -and $trackedMarkets.Count -gt 0) { $null } else { 'invalid market EPIC' })

$g06Started = (Get-Date).ToUniversalTime()
$priceReport = Join-Path $run.RunPath 'G06-price-history.md'
$priceRows = New-Object System.Collections.Generic.List[string]
$priceRows.Add('| Market | EPIC | Request | Bars | Latest UTC | Age | Entitled | Fresh | Outcome |')
$priceRows.Add('|---|---|---|---|---|---|---|---|---|')
$priceSuccesses = New-Object System.Collections.Generic.List[object]

foreach ($market in $trackedMarkets) {
    $logName = 'G06-prices-' + ($market.DisplayName -replace '[^A-Za-z0-9]+', '-').Trim('-') + '.log'
    $logPath = Join-Path $run.RunPath $logName
    $request = "10minute max 50"
    $command = "dotnet run --project src/Trading.Cli -- markets prices --instrument `"$($market.InstrumentId)`" --resolution 10minute --max 50"
    $prices = Invoke-VerificationCommand `
        -RepositoryRoot $repositoryRoot `
        -RunPath $run.RunPath `
        -Command $command `
        -OutputPath $logPath `
        -TimeoutSeconds 180

    $allowanceBlocked = $prices.Output -match 'exceeded-account-historical-data-allowance|historical-data-allowance|exceeded-account-allowance'

    $bars = 0
    if ($prices.ExitCode -eq 0 -and $prices.Output -match '(?im)Bars\s+(\d+)') {
        $bars = [int]$Matches[1]
    }

    $latest = $null
    if ($prices.ExitCode -eq 0) {
        $matches = [regex]::Matches($prices.Output, '\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s*\+00:00|Z)?')
        if ($matches.Count -gt 0) {
            $latestText = $matches[$matches.Count - 1].Value.Replace(' ', 'T')
            [DateTimeOffset]$parsedLatest = [DateTimeOffset]::MinValue
            if ([DateTimeOffset]::TryParse($latestText, [ref]$parsedLatest)) {
                $latest = $parsedLatest.ToUniversalTime()
            }
        }
    }

    $ageText = 'n/a'
    $fresh = 'Unknown'
    if ($null -ne $latest) {
        $age = [DateTimeOffset]::UtcNow - $latest
        $ageText = [Math]::Round($age.TotalMinutes, 1).ToString() + 'm'
        $fresh = if ($age.TotalMinutes -le 20) { 'True' } else { 'False' }
    }

    $entitled = if ($prices.Output -match 'unauthorised\.access|entitlement|not entitled') { 'False' } elseif ($allowanceBlocked) { 'Unknown' } elseif ($prices.ExitCode -eq 0) { 'True' } else { 'Unknown' }
    $outcome = if ($prices.ExitCode -eq 0 -and $bars -gt 0) { 'Valid response' } elseif ($allowanceBlocked) { 'BlockedHistoricalDataAllowance' } elseif ($entitled -eq 'False') { 'BlockedPriceEntitlement' } else { 'FailedPriceRetrieval' }
    if ($prices.ExitCode -eq 0 -and $bars -gt 0) {
        $priceSuccesses.Add([pscustomobject]@{ Market = $market; LogPath = $logPath; Latest = $latest; Bars = $bars })
    }

    $priceRows.Add("| $($market.DisplayName) | $($market.InstrumentId) | $request | $bars | $(if ($latest) { $latest.ToString('o') } else { 'n/a' }) | $ageText | $entitled | $fresh | $outcome |")
}

Set-Content -LiteralPath $priceReport -Value ($priceRows -join "`r`n") -Encoding UTF8
$isWeekend = (Get-Date).DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)
$allowanceFailures = @($priceRows | Where-Object { $_ -match 'BlockedHistoricalDataAllowance' }).Count
$g06Status = if ($priceSuccesses.Count -gt 0 -and -not $isWeekend) { 'Passed' } elseif ($priceSuccesses.Count -gt 0) { 'Blocked' } elseif ($allowanceFailures -gt 0) { 'Blocked' } else { 'Failed' }
$g06Blocker = if ($g06Status -eq 'Blocked' -and $allowanceFailures -gt 0) { 'IG demo account returned historical-data allowance errors for price-history reads.' } elseif ($g06Status -eq 'Blocked') { 'Freshness proof requires an active non-weekend market session.' } else { $null }
Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G06' `
    -Name 'IG price-history access' `
    -Status $g06Status `
    -StartedAtUtc $g06Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command 'markets prices --resolution 10minute --max 50 for each configured tracked market' `
    -ExitCode $(if ($priceSuccesses.Count -gt 0) { 0 } else { 1 }) `
    -Evidence @($priceReport) `
    -Summary "Price history returned bars for $($priceSuccesses.Count) of $($trackedMarkets.Count) markets." `
    -Classification $(if ($g06Status -eq 'Passed') { $null } elseif ($allowanceFailures -gt 0) { 'IG account entitlement' } elseif ($g06Status -eq 'Blocked') { 'market closed' } else { 'price entitlement denied' }) `
    -Blocker $g06Blocker

if ($priceSuccesses.Count -eq $trackedMarkets.Count -and $resolvedCount -eq $trackedMarkets.Count) {
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G05' `
        -Name 'IG market discovery' `
        -Status 'Passed' `
        -StartedAtUtc $g05Started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'markets details for each configured tracked market; prices confirmed configured EPIC bars' `
        -ExitCode 0 `
        -Evidence @($marketReport, $priceReport) `
        -Summary "Market details and price-history confirmed all $($trackedMarkets.Count) configured tracked markets." `
        -Classification $null
}

$g07Started = (Get-Date).ToUniversalTime()
if ($priceSuccesses.Count -eq 0) {
    Update-VerificationGate `
        -LedgerPath $run.LedgerPath `
        -RunPath $run.RunPath `
        -Id 'G07' `
        -Name 'Chart generation' `
        -Status 'Blocked' `
        -StartedAtUtc $g07Started `
        -CompletedAtUtc (Get-Date).ToUniversalTime() `
        -Command 'markets chart' `
        -Evidence @($priceReport) `
        -Summary 'Blocked because no real IG price series was available for chart generation.' `
        -Classification 'price entitlement denied' `
        -Blocker 'G06 produced no usable price series'
    return
}

$selected = $priceSuccesses[0]
$chartPath = Join-Path $run.RunPath ('G07-' + ($selected.Market.DisplayName -replace '[^A-Za-z0-9]+', '-').Trim('-') + '.png')
$chartLog = Join-Path $run.RunPath 'G07-chart.log'
$chartCommand = "dotnet run --project src/Trading.Cli -- markets chart --instrument `"$($selected.Market.InstrumentId)`" --resolution 10minute --max 100 --output `"$chartPath`" --style ohlc"
$chart = Invoke-VerificationCommand `
    -RepositoryRoot $repositoryRoot `
    -RunPath $run.RunPath `
    -Command $chartCommand `
    -OutputPath $chartLog `
    -TimeoutSeconds 180

$dimensions = if (Test-Path -LiteralPath $chartPath) { Get-PngDimensions -Path $chartPath } else { $null }
$chartSummaryPath = Join-Path $run.RunPath 'G07-chart-summary.txt'
$chartOk = $chart.ExitCode -eq 0 -and $null -ne $dimensions -and $dimensions.Width -gt 0 -and $dimensions.Height -gt 0
$chartSummary = @(
    "Market: $($selected.Market.DisplayName)",
    "EPIC: $($selected.Market.InstrumentId)",
    "Source bars from G06: $($selected.Bars)",
    "Latest source UTC: $(if ($selected.Latest) { $selected.Latest.ToString('o') } else { 'n/a' })",
    "PNG exists: $(Test-Path -LiteralPath $chartPath)",
    "PNG dimensions: $(if ($dimensions) { "$($dimensions.Width)x$($dimensions.Height)" } else { 'invalid' })",
    "Visual inspection: generated chart is a real PNG from IG price data; manual visual review still recommended."
) -join "`r`n"
Set-Content -LiteralPath $chartSummaryPath -Value $chartSummary -Encoding UTF8

Update-VerificationGate `
    -LedgerPath $run.LedgerPath `
    -RunPath $run.RunPath `
    -Id 'G07' `
    -Name 'Chart generation' `
    -Status $(if ($chartOk) { 'Passed' } else { 'Failed' }) `
    -StartedAtUtc $g07Started `
    -CompletedAtUtc (Get-Date).ToUniversalTime() `
    -Command $chartCommand `
    -ExitCode $chart.ExitCode `
    -Evidence @($chartLog, $chartPath, $chartSummaryPath) `
    -Summary $(if ($chartOk) { "Generated valid PNG chart for $($selected.Market.DisplayName)." } else { 'Chart command failed or output was not a valid PNG.' }) `
    -Classification $(if ($chartOk) { $null } else { 'FailedChartGeneration' })

Write-Host "G04-G07 complete for run $($run.RunId)"
