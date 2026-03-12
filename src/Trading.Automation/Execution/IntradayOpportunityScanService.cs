using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
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
    private readonly IntradayOpportunityPreparationWriter _preparationWriter;
    private readonly ITradingDayWorkflow _workflow;
    private readonly AutomationOptions _automationOptions;
    private readonly IReadOnlyDictionary<string, string> _instrumentNames;
    private readonly ILogger<IntradayOpportunityScanService> _logger;

    public IntradayOpportunityScanService(
        ITradingDayStore tradingDayStore,
        IntradayPriceSeriesCache priceSeriesCache,
        IPriceChartRenderer priceChartRenderer,
        IntradayOpportunityReviewer intradayOpportunityReviewer,
        IntradayOpportunityPreparationWriter preparationWriter,
        ITradingDayWorkflow workflow,
        IOptions<AutomationOptions> automationOptions,
        IOptions<DailyBriefingOptions> dailyBriefingOptions,
        ILogger<IntradayOpportunityScanService> logger)
    {
        _tradingDayStore = tradingDayStore;
        _priceSeriesCache = priceSeriesCache;
        _priceChartRenderer = priceChartRenderer;
        _intradayOpportunityReviewer = intradayOpportunityReviewer;
        _preparationWriter = preparationWriter;
        _workflow = workflow;
        _automationOptions = automationOptions.Value;
        _instrumentNames = dailyBriefingOptions.Value.TrackedMarkets.ToDictionary(
            market => market.InstrumentId,
            market => string.IsNullOrWhiteSpace(market.DisplayName) ? market.InstrumentId : market.DisplayName,
            StringComparer.Ordinal);
        _logger = logger;
    }

    public Task<IntradayOpportunitySubmitResult?> RunForTodayAsync(CancellationToken cancellationToken = default)
        => RunAsync(ResolveTradingDate(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, cancellationToken);

    public async Task<IntradayOpportunityPreparationDocument?> PrepareForTodayAsync(CancellationToken cancellationToken = default)
    {
        var requestedAtUtc = DateTimeOffset.UtcNow;
        return await PrepareAsync(ResolveTradingDate(requestedAtUtc), requestedAtUtc, cancellationToken);
    }

    public async Task<IntradayOpportunityPreparationDocument?> PrepareAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var options = _automationOptions.IntradayOpportunities;
        options.Validate();

        var preparedRun = await BuildPreparedRunAsync(tradingDate, requestedAtUtc, options, cancellationToken);
        if (preparedRun is null)
        {
            return null;
        }

        var document = await _preparationWriter.WriteAsync(tradingDate, requestedAtUtc, preparedRun, cancellationToken);
        _logger.LogInformation(
            "Prepared intraday opportunity review for {TradingDate}. Saved request artifact at {PreparedPath}.",
            tradingDate,
            document.PreparedArtifact.Path);
        return document;
    }

    public async Task<IntradayOpportunitySubmitResult> SubmitAsync(string preparedJsonPath, CancellationToken cancellationToken = default)
    {
        var prepared = await _preparationWriter.LoadAsync(preparedJsonPath, cancellationToken);
        return await SubmitAsync(prepared, cancellationToken);
    }

    public async Task<IntradayOpportunitySubmitResult?> RunAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(tradingDate, requestedAtUtc, cancellationToken);
        if (prepared is null)
        {
            return null;
        }

        return await SubmitAsync(prepared, cancellationToken);
    }

    private async Task<IntradayOpportunitySubmitResult> SubmitAsync(
        IntradayOpportunityPreparationDocument prepared,
        CancellationToken cancellationToken)
    {
        var renderedRequestText = _intradayOpportunityReviewer.RenderRequestText(prepared.Input);
        if (!string.Equals(renderedRequestText, prepared.RenderedRequestText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared request text for '{prepared.PreparedArtifact.Path}' no longer matches the current prompt template. Regenerate the prepared run before submitting.");
        }

        var attachments = prepared.Attachments
            .Select(attachment => new PromptAttachment(
                attachment.Label,
                attachment.MediaType,
                File.ReadAllBytes(attachment.Artifact.Path)))
            .ToArray();

        var execution = await _intradayOpportunityReviewer.ReviewAsync(prepared.Input, attachments, cancellationToken);
        var workflowResult = await _workflow.ReviewIntradayOpportunitiesAsync(execution.Batch, cancellationToken);

        var artifactReferences = execution.AttachmentArtifactPaths
            .Select(ToArtifactReference)
            .ToArray();
        var result = new IntradayOpportunitySubmitResult(
            prepared,
            new IntradayOpportunityExecutionArtifacts(
                ToArtifactReference(execution.EnvelopeArtifactPath),
                ToArtifactReference(execution.StructuredArtifactPath),
                artifactReferences),
            execution.Batch,
            workflowResult);

        _logger.LogInformation(
            "Submitted intraday opportunity review for {TradingDate}. Envelope: {EnvelopePath}. Extracted JSON: {StructuredPath}.",
            prepared.TradingDate,
            result.ExecutionArtifacts.PromptEnvelopeArtifact.Path,
            result.ExecutionArtifacts.ExtractedJsonArtifact.Path);

        return result;
    }

    private async Task<IntradayPreparedRun?> BuildPreparedRunAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        IntradayOpportunityScanOptions options,
        CancellationToken cancellationToken)
    {
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

        var preparedMarkets = new List<PreparedIntradayMarket>(record.Plan.WatchList.Count);
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

        return new IntradayPreparedRun(
            input,
            _intradayOpportunityReviewer.RenderRequestText(input),
            preparedMarkets);
    }

    private async Task<PreparedIntradayMarket?> TryPrepareMarketAsync(
        MarketWatch market,
        DateTimeOffset requestedAtUtc,
        IntradayOpportunityScanOptions options,
        CancellationToken cancellationToken)
    {
        var cachedSeries = await _priceSeriesCache.GetSeriesAsync(
            market.Instrument,
            requestedAtUtc,
            options.ChartLookbackHours,
            options.ChartResolution,
            cancellationToken);
        var series = cachedSeries.Series;

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
        var instrumentName = ResolveInstrumentName(market.Instrument);
        var chartBytes = _priceChartRenderer.RenderPng(series, PriceChartStyle.Ohlc, PriceGapMode.Compress);

        return new PreparedIntradayMarket(
            market.Instrument,
            instrumentName,
            market.Rank,
            market.Rationale,
            market.LongScenario,
            market.ShortScenario,
            currentBid,
            currentAsk,
            currentPrice,
            currentSpread,
            latestBar.TimestampUtc,
            cachedSeries.RefreshMode,
            cachedSeries.FetchedBarCount,
            $"{instrumentName} 4-day 10-minute chart",
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

    private static ArtifactReference ToArtifactReference(string path)
        => new(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private static string FormatDailyPlanSummary(TradingDayPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Market regime: {plan.MarketRegime}");
        builder.AppendLine($"Macro summary: {plan.MacroSummary}");
        builder.AppendLine($"Regime summary: {plan.MarketRegimeSummary}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatWatchedMarketsContext(IReadOnlyList<PreparedIntradayMarket> markets)
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

        return builder.ToString().TrimEnd();
    }
}
