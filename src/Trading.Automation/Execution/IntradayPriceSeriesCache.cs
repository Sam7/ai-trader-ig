using Microsoft.Extensions.Logging;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed class IntradayPriceSeriesCache : IIntradayPriceSeriesSource
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
                PriceSeriesRefreshMode.LocalCache,
                result.BackfilledBarCount);
        }
        finally
        {
            _gate.Release();
        }
    }

}
