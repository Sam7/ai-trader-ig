using System.Text;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Charting;
using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;
using Trading.Strategy.Workflow;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityScanService
{
    private readonly ITradingDayStore _tradingDayStore;
    private readonly IntradayPriceSeriesCache _priceSeriesCache;
    private readonly IPriceChartRenderer _priceChartRenderer;
    private readonly IntradayOpportunityReviewer _intradayOpportunityReviewer;
    private readonly ITradingDayWorkflow _workflow;
    private readonly AutomationOptions _automationOptions;
    private readonly IReadOnlyDictionary<string, string> _instrumentNames;
    private readonly ILogger<IntradayOpportunityScanService> _logger;

    public IntradayOpportunityScanService(
        ITradingDayStore tradingDayStore,
        IntradayPriceSeriesCache priceSeriesCache,
        IPriceChartRenderer priceChartRenderer,
        IntradayOpportunityReviewer intradayOpportunityReviewer,
        ITradingDayWorkflow workflow,
        IOptions<AutomationOptions> automationOptions,
        IOptions<DailyBriefingOptions> dailyBriefingOptions,
        ILogger<IntradayOpportunityScanService> logger)
    {
        _tradingDayStore = tradingDayStore;
        _priceSeriesCache = priceSeriesCache;
        _priceChartRenderer = priceChartRenderer;
        _intradayOpportunityReviewer = intradayOpportunityReviewer;
        _workflow = workflow;
        _automationOptions = automationOptions.Value;
        _instrumentNames = dailyBriefingOptions.Value.TrackedMarkets.ToDictionary(
            market => market.InstrumentId,
            market => string.IsNullOrWhiteSpace(market.DisplayName) ? market.InstrumentId : market.DisplayName,
            StringComparer.Ordinal);
        _logger = logger;
    }

    public Task<IntradayOpportunityReviewResult?> RunForTodayAsync(CancellationToken cancellationToken = default)
        => RunAsync(ResolveTradingDate(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, cancellationToken);

    public async Task<IntradayOpportunityReviewResult?> RunAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var options = _automationOptions.IntradayOpportunities;
        options.Validate();

        var record = await _tradingDayStore.GetAsync(tradingDate, cancellationToken);
        if (record?.Plan is null)
        {
            _logger.LogInformation("Skipping intraday opportunity scan for {TradingDate}: no trading day plan exists.", tradingDate);
            return null;
        }

        if (record.Plan.WatchList.Count == 0)
        {
            _logger.LogInformation("Skipping intraday opportunity scan for {TradingDate}: watch list is empty.", tradingDate);
            return null;
        }

        var preparedMarkets = new List<PreparedMarketContext>(record.Plan.WatchList.Count);
        foreach (var market in record.Plan.WatchList)
        {
            var prepared = await TryPrepareMarketAsync(market, requestedAtUtc, options, cancellationToken);
            if (prepared is null)
            {
                continue;
            }

            preparedMarkets.Add(prepared);
        }

        if (preparedMarkets.Count == 0)
        {
            _logger.LogInformation(
                "Skipping intraday opportunity scan for {TradingDate}: no watched markets had fresh price data.",
                tradingDate);
            return null;
        }

        var lookbackStartUtc = requestedAtUtc.AddMinutes(-options.LookbackMinutes);
        var input = new IntradayOpportunityReviewInput(
            tradingDate,
            lookbackStartUtc,
            requestedAtUtc,
            preparedMarkets.Count,
            options.MaxCandidatesPerRun,
            _automationOptions.Timezone,
            FormatDailyPlanSummary(record.Plan),
            FormatWatchedMarketsContext(preparedMarkets),
            FormatCalendarEventsContext(record.Plan.CalendarEvents),
            tradingDate,
            requestedAtUtc);

        var attachments = preparedMarkets
            .Select(market => new PromptAttachment($"{market.InstrumentName} 4-day 10-minute chart", "image/png", market.ChartBytes))
            .ToArray();

        var batch = await _intradayOpportunityReviewer.ReviewAsync(input, attachments, cancellationToken);
        var result = await _workflow.ReviewIntradayOpportunitiesAsync(batch, cancellationToken);

        _logger.LogInformation(
            "Completed intraday opportunity scan for {TradingDate}. Assessed {AssessmentCount} markets and returned {CandidateCount} actionable candidates.",
            tradingDate,
            result.MarketAssessments.Count,
            result.CandidateOpportunities.Count);

        return result;
    }

    private async Task<PreparedMarketContext?> TryPrepareMarketAsync(
        MarketWatch market,
        DateTimeOffset requestedAtUtc,
        IntradayOpportunityScanOptions options,
        CancellationToken cancellationToken)
    {
        var series = await _priceSeriesCache.GetSeriesAsync(
            market.Instrument,
            requestedAtUtc,
            options.ChartLookbackHours,
            options.ChartResolution,
            cancellationToken);

        if (series.Bars.Count == 0)
        {
            _logger.LogInformation("Skipping {Instrument}: no chart bars returned for intraday scan.", market.Instrument);
            return null;
        }

        var latestBar = series.Bars.OrderByDescending(bar => bar.TimestampUtc).First();
        var maxAge = TimeSpan.FromMinutes(options.FreshPriceMaxAgeMinutes);
        if (requestedAtUtc - latestBar.TimestampUtc > maxAge)
        {
            _logger.LogInformation(
                "Skipping {Instrument}: latest bar at {TimestampUtc} is older than {MaxAge}.",
                market.Instrument,
                latestBar.TimestampUtc,
                maxAge);
            return null;
        }

        var currentBid = latestBar.BidClose;
        var currentAsk = latestBar.AskClose;
        var currentPrice = (currentBid + currentAsk) / 2m;
        var currentSpread = Math.Max(0m, currentAsk - currentBid);
        var chartBytes = _priceChartRenderer.RenderPng(series, PriceChartStyle.Ohlc, PriceGapMode.Compress);

        return new PreparedMarketContext(
            market.Instrument,
            ResolveInstrumentName(market.Instrument),
            market.Rank,
            market.Rationale,
            market.LongScenario,
            market.ShortScenario,
            currentBid,
            currentAsk,
            currentPrice,
            currentSpread,
            latestBar.TimestampUtc,
            chartBytes);
    }

    private string ResolveInstrumentName(InstrumentId instrument)
        => _instrumentNames.TryGetValue(instrument.Value, out var instrumentName)
            ? instrumentName
            : instrument.Value;

    private DateOnly ResolveTradingDate(DateTimeOffset utcNow)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_automationOptions.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, timezone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static string FormatDailyPlanSummary(TradingDayPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Market regime: {plan.MarketRegime}");
        builder.AppendLine($"Macro summary: {plan.MacroSummary}");
        builder.AppendLine($"Regime summary: {plan.MarketRegimeSummary}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatWatchedMarketsContext(IReadOnlyList<PreparedMarketContext> markets)
    {
        var builder = new StringBuilder();
        foreach (var market in markets.OrderBy(market => market.Rank))
        {
            builder.AppendLine($"## Rank {market.Rank}: {market.InstrumentName}");
            builder.AppendLine($"Instrument ID: {market.Instrument.Value}");
            builder.AppendLine($"Current bid: {market.CurrentBid.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current ask: {market.CurrentAsk.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current mid price: {market.CurrentPrice.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current spread: {market.CurrentSpread.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Latest price timestamp UTC: {market.LatestBarAtUtc:O}");
            builder.AppendLine($"Daily rationale: {market.Rationale}");
            builder.AppendLine($"Long scenario thesis: {market.LongScenario.Thesis}");
            builder.AppendLine($"Long confirmation: {market.LongScenario.Confirmation}");
            builder.AppendLine($"Long invalidation: {market.LongScenario.Invalidation}");
            builder.AppendLine($"Short scenario thesis: {market.ShortScenario.Thesis}");
            builder.AppendLine($"Short confirmation: {market.ShortScenario.Confirmation}");
            builder.AppendLine($"Short invalidation: {market.ShortScenario.Invalidation}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCalendarEventsContext(IReadOnlyList<EconomicEvent> calendarEvents)
    {
        if (calendarEvents.Count == 0)
        {
            return "No scheduled calendar events were captured in the daily plan.";
        }

        var builder = new StringBuilder();
        foreach (var calendarEvent in calendarEvents.OrderBy(calendarEvent => calendarEvent.ScheduledAtUtc))
        {
            builder.AppendLine(
                $"- {calendarEvent.Id} | {calendarEvent.ScheduledAtUtc:O} | {calendarEvent.Impact} | {calendarEvent.Title} | affected instruments: {string.Join(", ", calendarEvent.AffectedInstruments.Select(instrument => instrument.Value))}");
        }

        return builder.ToString();
    }

    private sealed record PreparedMarketContext(
        InstrumentId Instrument,
        string InstrumentName,
        int Rank,
        string Rationale,
        TradeScenario LongScenario,
        TradeScenario ShortScenario,
        decimal CurrentBid,
        decimal CurrentAsk,
        decimal CurrentPrice,
        decimal CurrentSpread,
        DateTimeOffset LatestBarAtUtc,
        byte[] ChartBytes);
}
