using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataService
{
    private readonly IMarketDataStore _store;
    private readonly ITradingGateway _tradingGateway;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataService> _logger;
    private ITradingSession? _session;

    public MarketDataService(
        IMarketDataStore store,
        ITradingGateway tradingGateway,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataService> logger)
    {
        _store = store;
        _tradingGateway = tradingGateway;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MarketDataResult> GetBarsAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();

        var canonicalResolution = _options.CanonicalResolution;
        if (!CanServeResolution(canonicalResolution, request.Resolution))
        {
            return new MarketDataResult(
                MarketDataStatus.UnsupportedResolution,
                new PriceSeries(request.Instrument, request.Resolution, []),
                MarketDataResultSource.None,
                [],
                0,
                0,
                $"Resolution '{request.Resolution}' cannot be served from canonical resolution '{canonicalResolution}'.");
        }

        var canonicalInterval = PriceResolutionIntervals.ToTimeSpan(canonicalResolution);
        var canonicalFromUtc = PriceResolutionIntervals.AlignDown(request.FromUtc, canonicalInterval);
        var canonicalToUtc = PriceResolutionIntervals.AlignUp(request.ToUtc, canonicalInterval);
        var initial = await GetFinalCanonicalBarsAsync(request.Instrument, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
        var missing = FindGaps(initial, canonicalFromUtc, canonicalToUtc, canonicalInterval);
        var brokerRequestCount = 0;
        var backfilledBarCount = 0;
        var attemptedBackfill = false;

        if (missing.Count > 0 && request.AllowBackfill && _options.BackfillEnabled && !_options.CloudSnapshot.Mirror.Enabled)
        {
            foreach (var gap in missing)
            {
                attemptedBackfill = true;
                try
                {
                    var fetched = await GetPricesWithAuthenticatedSessionAsync(
                        new GetPricesRequest(
                            request.Instrument,
                            canonicalResolution,
                            FromUtc: gap.FromUtc,
                            ToUtc: gap.ToUtc),
                        cancellationToken);
                    brokerRequestCount++;
                    backfilledBarCount += fetched.Bars.Count;

                    var stored = fetched.Bars
                        .Where(bar => bar.TimestampUtc >= gap.FromUtc && bar.TimestampUtc < gap.ToUtc)
                        .Select(bar => StoredPriceBar.FromPriceBar(request.Instrument, canonicalResolution, bar, MarketDataSource.RestBackfill))
                        .ToArray();
                    await _store.UpsertAsync(stored, cancellationToken);
                }
                catch (TradingGatewayException exception) when (IsAllowanceFailure(exception))
                {
                    _logger.LogWarning(
                        exception,
                        "Blocked market-data backfill for {Instrument}: IG allowance was exceeded.",
                        request.Instrument);
                    var partial = await BuildSeriesAsync(request, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
                    return new MarketDataResult(
                        MarketDataStatus.BlockedBackfillAllowance,
                        partial,
                        partial.Bars.Count == 0 ? MarketDataResultSource.None : MarketDataResultSource.Mixed,
                        missing,
                        brokerRequestCount,
                        backfilledBarCount,
                        exception.Message);
                }
                catch (TradingGatewayException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed market-data backfill for {Instrument}.",
                        request.Instrument);
                    var partial = await BuildSeriesAsync(request, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
                    return new MarketDataResult(
                        MarketDataStatus.FailedBackfill,
                        partial,
                        partial.Bars.Count == 0 ? MarketDataResultSource.None : MarketDataResultSource.Mixed,
                        missing,
                        brokerRequestCount,
                        backfilledBarCount,
                        exception.Message);
                }
            }
        }

        var finalCanonical = await GetFinalCanonicalBarsAsync(request.Instrument, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
        var remainingGaps = FindGaps(finalCanonical, canonicalFromUtc, canonicalToUtc, canonicalInterval);
        var series = BuildSeries(request, canonicalResolution, finalCanonical);
        var status = remainingGaps.Count == 0 ? MarketDataStatus.Completed : MarketDataStatus.Partial;
        var source = ResolveSource(initial.Count, attemptedBackfill, brokerRequestCount);

        return new MarketDataResult(
            status,
            series,
            source,
            remainingGaps,
            brokerRequestCount,
            backfilledBarCount);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return;
        }

        _session = await _tradingGateway.AuthenticateAsync(cancellationToken);
    }

    private async Task<PriceSeries> GetPricesWithAuthenticatedSessionAsync(
        GetPricesRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        try
        {
            return await _tradingGateway.GetPricesAsync(request, cancellationToken);
        }
        catch (TradingGatewayException exception) when (IsSessionFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Trading session was rejected while backfilling market data for {Instrument}. Reauthenticating once.",
                request.Instrument);
            _session = null;
            await EnsureAuthenticatedAsync(cancellationToken);
            return await _tradingGateway.GetPricesAsync(request, cancellationToken);
        }
    }

    private async Task<PriceSeries> BuildSeriesAsync(
        MarketDataRequest request,
        PriceResolution canonicalResolution,
        DateTimeOffset canonicalFromUtc,
        DateTimeOffset canonicalToUtc,
        CancellationToken cancellationToken)
    {
        var canonical = await GetFinalCanonicalBarsAsync(request.Instrument, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
        return BuildSeries(request, canonicalResolution, canonical);
    }

    private async Task<IReadOnlyList<StoredPriceBar>> GetFinalCanonicalBarsAsync(
        InstrumentId instrument,
        PriceResolution canonicalResolution,
        DateTimeOffset canonicalFromUtc,
        DateTimeOffset canonicalToUtc,
        CancellationToken cancellationToken)
        => (await _store.GetRangeAsync(instrument, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken))
            .Where(bar => bar.IsFinal)
            .OrderBy(bar => bar.Bar.TimestampUtc)
            .ToArray();

    private static PriceSeries BuildSeries(
        MarketDataRequest request,
        PriceResolution canonicalResolution,
        IReadOnlyList<StoredPriceBar> canonicalBars)
    {
        var canonicalSeries = new PriceSeries(
            request.Instrument,
            canonicalResolution,
            canonicalBars.Select(bar => bar.Bar).OrderBy(bar => bar.TimestampUtc).ToArray());
        var series = PriceBarAggregator.Aggregate(canonicalSeries, request.Resolution);
        return series with
        {
            Bars = series.Bars
                .Where(bar => bar.TimestampUtc >= request.FromUtc && bar.TimestampUtc < request.ToUtc)
                .ToArray(),
        };
    }

    private static IReadOnlyList<MarketDataGap> FindGaps(
        IReadOnlyList<StoredPriceBar> bars,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan interval)
    {
        var present = bars
            .Select(bar => bar.Bar.TimestampUtc)
            .ToHashSet();
        var gaps = new List<MarketDataGap>();
        DateTimeOffset? gapStart = null;
        var cursor = fromUtc;

        while (cursor < toUtc)
        {
            if (!present.Contains(cursor))
            {
                gapStart ??= cursor;
            }
            else if (gapStart is not null)
            {
                gaps.Add(new MarketDataGap(gapStart.Value, cursor));
                gapStart = null;
            }

            cursor = cursor.Add(interval);
        }

        if (gapStart is not null)
        {
            gaps.Add(new MarketDataGap(gapStart.Value, toUtc));
        }

        return gaps;
    }

    private static bool CanServeResolution(PriceResolution canonicalResolution, PriceResolution requestedResolution)
    {
        var canonical = PriceResolutionIntervals.ToTimeSpan(canonicalResolution);
        var requested = PriceResolutionIntervals.ToTimeSpan(requestedResolution);
        return requested >= canonical && requested.Ticks % canonical.Ticks == 0;
    }

    private static MarketDataResultSource ResolveSource(
        int initialLocalBars,
        bool attemptedBackfill,
        int brokerRequestCount)
    {
        if (brokerRequestCount == 0)
        {
            return initialLocalBars == 0
                ? MarketDataResultSource.None
                : MarketDataResultSource.LocalCache;
        }

        return initialLocalBars == 0 && attemptedBackfill
            ? MarketDataResultSource.RestBackfill
            : MarketDataResultSource.Mixed;
    }

    private static bool IsAllowanceFailure(TradingGatewayException exception)
        => exception.Message.Contains("allowance", StringComparison.OrdinalIgnoreCase);

    private static bool IsSessionFailure(TradingGatewayException exception)
        => exception.ErrorCode is TradingErrorCode.AuthenticationFailed or TradingErrorCode.SessionExpired;
}
