using Microsoft.Extensions.Logging;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed class IntradayPriceSeriesCache
{
    private readonly MarketDataService _marketDataService;
    private readonly ILogger<IntradayPriceSeriesCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IntradayPriceSeriesCache(
        MarketDataService marketDataService,
        ILogger<IntradayPriceSeriesCache> logger)
    {
        _marketDataService = marketDataService;
        _logger = logger;
    }

    public async Task<CachedPriceSeriesResult> GetSeriesAsync(
        InstrumentId instrument,
        DateTimeOffset requestedAtUtc,
        int chartLookbackHours,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var lookbackFromUtc = requestedAtUtc.AddHours(-chartLookbackHours);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation(
                "Resolving intraday prices for {Instrument}. Resolution: {Resolution}. From UTC: {FromUtc}. To UTC: {ToUtc}.",
                instrument,
                resolution,
                lookbackFromUtc,
                requestedAtUtc);

            var result = await _marketDataService.GetBarsAsync(
                new MarketDataRequest(
                    instrument,
                    resolution,
                    lookbackFromUtc,
                    requestedAtUtc),
                cancellationToken);

            if (result.Status == MarketDataStatus.BlockedBackfillAllowance)
            {
                _logger.LogWarning(
                    "Intraday price backfill was blocked by broker allowance for {Instrument}. Returning {BarCount} locally available bars.",
                    instrument,
                    result.Series.Bars.Count);
            }

            _logger.LogInformation(
                "Resolved intraday prices for {Instrument}. Status: {Status}. Source: {Source}. Bars: {BarCount}. Broker requests: {BrokerRequests}. Backfilled bars: {BackfilledBars}.",
                instrument,
                result.Status,
                result.Source,
                result.Series.Bars.Count,
                result.BrokerRequestCount,
                result.BackfilledBarCount);

            return new CachedPriceSeriesResult(
                result.Series,
                ToRefreshMode(result),
                result.BackfilledBarCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PriceSeriesRefreshMode ToRefreshMode(MarketDataResult result)
        => result.Source switch
        {
            MarketDataResultSource.RestBackfill => PriceSeriesRefreshMode.Bootstrap,
            MarketDataResultSource.Mixed => PriceSeriesRefreshMode.Incremental,
            _ => PriceSeriesRefreshMode.LocalCache,
        };
}
