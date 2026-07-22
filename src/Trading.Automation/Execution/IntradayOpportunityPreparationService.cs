using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.Automation.Configuration;
using Trading.Charting;
using Trading.Automation.Health;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityPreparationService : IIntradayOpportunityPreparationService
{
    private readonly ITradingDayStore _tradingDayStore;
    private readonly IIntradayPriceSeriesSource _priceSeriesSource;
    private readonly IPriceChartRenderer _priceChartRenderer;
    private readonly IIntradayOpportunityRequestRenderer _requestRenderer;
    private readonly IIntradayOpportunityPreparationStore _store;
    private readonly AutomationOptions _automationOptions;
    private readonly IReadOnlyDictionary<string, string> _instrumentNames;
    private readonly WorkerOperationMetrics _operationMetrics;
    private readonly ILogger<IntradayOpportunityPreparationService> _logger;

    public IntradayOpportunityPreparationService(
        ITradingDayStore tradingDayStore,
        IIntradayPriceSeriesSource priceSeriesSource,
        IPriceChartRenderer priceChartRenderer,
        IIntradayOpportunityRequestRenderer requestRenderer,
        IIntradayOpportunityPreparationStore store,
        IOptions<AutomationOptions> automationOptions,
        IOptions<DailyBriefingOptions> dailyBriefingOptions,
        WorkerOperationMetrics operationMetrics,
        ILogger<IntradayOpportunityPreparationService> logger)
    {
        _tradingDayStore = tradingDayStore;
        _priceSeriesSource = priceSeriesSource;
        _priceChartRenderer = priceChartRenderer;
        _requestRenderer = requestRenderer;
        _store = store;
        _automationOptions = automationOptions.Value;
        _instrumentNames = dailyBriefingOptions.Value.TrackedMarkets.ToDictionary(
            market => market.InstrumentId,
            market => string.IsNullOrWhiteSpace(market.DisplayName) ? market.InstrumentId : market.DisplayName,
            StringComparer.Ordinal);
        _operationMetrics = operationMetrics;
        _logger = logger;
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

        var document = await _store.WriteAsync(tradingDate, requestedAtUtc, preparedRun, cancellationToken);
        _logger.LogInformation(
            "Prepared intraday opportunity review for {TradingDate}. Saved request artifact at {PreparedPath}.",
            tradingDate,
            document.PreparedArtifact.Path);
        return document;
    }

    public Task<IntradayOpportunityPreparationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
        => _store.LoadAsync(path, cancellationToken);

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
            if (prepared is not null)
            {
                preparedMarkets.Add(prepared);
            }
        }

        if (preparedMarkets.Count == 0)
        {
            _logger.LogInformation(
                "Skipping intraday opportunity scan for {TradingDate}: no watched markets had fresh price data.",
                tradingDate);
            return null;
        }

        var request = new IntradayOpportunityReviewRequest(
            tradingDate,
            requestedAtUtc.AddMinutes(-options.LookbackMinutes),
            requestedAtUtc,
            options.MaxCandidatesPerRun,
            _automationOptions.Timezone,
            record.Plan,
            preparedMarkets.Select(ToReviewContext).ToArray(),
            requestedAtUtc);

        return new IntradayPreparedRun(
            request,
            _requestRenderer.RenderRequestText(request),
            preparedMarkets,
            _requestRenderer.Contract,
            IntradayPreparationProfileReference.Default);
    }

    private async Task<PreparedIntradayMarket?> TryPrepareMarketAsync(
        MarketWatch market,
        DateTimeOffset requestedAtUtc,
        IntradayOpportunityScanOptions options,
        CancellationToken cancellationToken)
    {
        var priceLoad = _operationMetrics.Begin("intraday-price-load", itemCount: 1);
        CachedPriceSeriesResult cachedSeries;
        try
        {
            cachedSeries = await _priceSeriesSource.GetSeriesAsync(
                market.Instrument,
                requestedAtUtc,
                options.ChartLookbackHours,
                options.ChartResolution,
                cancellationToken);
            priceLoad.Complete();
        }
        catch
        {
            priceLoad.Fail();
            throw;
        }
        var series = cachedSeries.Series;

        if (series.Bars.Count == 0)
        {
            _logger.LogInformation("Skipping {Instrument}: no chart bars returned for intraday scan.", market.Instrument);
            return null;
        }

        var latestBar = series.Bars.OrderByDescending(bar => bar.TimestampUtc).First();
        var maxAge = TimeSpan.FromMinutes(options.FreshPriceMaxAgeMinutes);
        if (requestedAtUtc - latestBar.TimestampUtc > maxAge && !options.AllowStalePriceDataForDiagnostics)
        {
            _logger.LogInformation(
                "Skipping {Instrument}: latest bar at {TimestampUtc} is older than {MaxAge}.",
                market.Instrument,
                latestBar.TimestampUtc,
                maxAge);
            return null;
        }

        if (requestedAtUtc - latestBar.TimestampUtc > maxAge)
        {
            _logger.LogWarning(
                "Using stale local price data for diagnostics for {Instrument}: latest bar at {TimestampUtc} is older than {MaxAge}.",
                market.Instrument,
                latestBar.TimestampUtc,
                maxAge);
        }

        var currentBid = latestBar.BidClose;
        var currentAsk = latestBar.AskClose;
        var instrumentName = ResolveInstrumentName(market.Instrument);
        var chartOperation = _operationMetrics.Begin("intraday-chart-render", series.Bars.Count);
        byte[] chart;
        try
        {
            chart = _priceChartRenderer.RenderPng(series, PriceChartStyle.Ohlc, PriceGapMode.Compress);
            chartOperation.Complete(chart.Length);
        }
        catch
        {
            chartOperation.Fail();
            throw;
        }
        return new PreparedIntradayMarket(
            market.Instrument,
            instrumentName,
            market.Rank,
            market.Rationale,
            market.LongScenario,
            market.ShortScenario,
            currentBid,
            currentAsk,
            (currentBid + currentAsk) / 2m,
            Math.Max(0m, currentAsk - currentBid),
            latestBar.TimestampUtc,
            cachedSeries.RefreshMode,
            cachedSeries.FetchedBarCount,
            [new PreparedDecisionEvidence(
                DecisionEvidenceKind.PriceChart,
                IntradayChartAttachmentLabel.Format(instrumentName, options.ChartLookbackHours, options.ChartResolution),
                "image/png",
                chart,
                series.Bars.Min(bar => bar.TimestampUtc),
                series.Bars.Max(bar => bar.TimestampUtc),
                latestBar.TimestampUtc,
                "price-chart-ohlc-compressed",
                "1")]);
    }

    private string ResolveInstrumentName(InstrumentId instrument)
        => _instrumentNames.TryGetValue(instrument.Value, out var name) ? name : instrument.Value;

    private static IntradayMarketReviewContext ToReviewContext(PreparedIntradayMarket market)
        => new(
            market.Instrument,
            market.InstrumentName,
            market.Rank,
            market.Rationale,
            market.LongScenario,
            market.ShortScenario,
            market.CurrentBid,
            market.CurrentAsk,
            market.CurrentPrice,
            market.CurrentSpread,
            market.LatestBarAtUtc);
}
