using Trading.Abstractions;

namespace Trading.Automation.Execution;

public sealed class IntradayPriceSeriesCache
{
    private readonly ITradingGateway _tradingGateway;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string InstrumentId, PriceResolution Resolution), PriceSeries> _seriesByInstrument = [];

    public IntradayPriceSeriesCache(ITradingGateway tradingGateway)
    {
        _tradingGateway = tradingGateway;
    }

    public async Task<PriceSeries> GetSeriesAsync(
        InstrumentId instrument,
        DateTimeOffset requestedAtUtc,
        int chartLookbackHours,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var lookbackFromUtc = requestedAtUtc.AddHours(-chartLookbackHours);
        var key = (instrument.Value, resolution);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_seriesByInstrument.TryGetValue(key, out var existing)
                || existing.Bars.Count == 0
                || existing.Bars.Min(bar => bar.TimestampUtc) > lookbackFromUtc)
            {
                var bootstrap = await _tradingGateway.GetPricesAsync(
                    new GetPricesRequest(
                        instrument,
                        resolution,
                        FromUtc: lookbackFromUtc,
                        ToUtc: requestedAtUtc),
                    cancellationToken);

                _seriesByInstrument[key] = TrimSeries(bootstrap, lookbackFromUtc);
                return _seriesByInstrument[key];
            }

            var latestCachedBar = existing.Bars.MaxBy(bar => bar.TimestampUtc)!;
            var incrementalFromUtc = latestCachedBar.TimestampUtc.AddMinutes(-GetResolutionMinutes(resolution));
            if (incrementalFromUtc > requestedAtUtc)
            {
                incrementalFromUtc = requestedAtUtc;
            }

            var incremental = await _tradingGateway.GetPricesAsync(
                new GetPricesRequest(
                    instrument,
                    resolution,
                    FromUtc: incrementalFromUtc,
                    ToUtc: requestedAtUtc),
                cancellationToken);

            var mergedBars = existing.Bars
                .Concat(incremental.Bars)
                .GroupBy(bar => bar.TimestampUtc)
                .Select(group => group.OrderByDescending(bar => bar.TimestampUtc).First())
                .Where(bar => bar.TimestampUtc >= lookbackFromUtc)
                .OrderBy(bar => bar.TimestampUtc)
                .ToArray();

            var merged = new PriceSeries(instrument, resolution, mergedBars);
            _seriesByInstrument[key] = merged;
            return merged;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PriceSeries TrimSeries(PriceSeries series, DateTimeOffset lookbackFromUtc)
        => new(
            series.Instrument,
            series.Resolution,
            series.Bars
                .Where(bar => bar.TimestampUtc >= lookbackFromUtc)
                .OrderBy(bar => bar.TimestampUtc)
                .ToArray());

    private static int GetResolutionMinutes(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.TenMinutes => 10,
            PriceResolution.FifteenMinutes => 15,
            PriceResolution.ThirtyMinutes => 30,
            PriceResolution.Hour => 60,
            _ => throw new InvalidOperationException($"Intraday cache does not support resolution '{resolution}'."),
        };
}
