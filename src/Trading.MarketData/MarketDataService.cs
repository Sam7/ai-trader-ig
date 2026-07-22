using Microsoft.Extensions.Options;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataService
{
    private readonly IMarketDataStore _store;
    private readonly MarketDataOptions _options;

    public MarketDataService(
        IMarketDataStore store,
        IOptions<MarketDataOptions> options)
    {
        _store = store;
        _options = options.Value;
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
        var finalCanonical = await GetFinalCanonicalBarsAsync(request.Instrument, canonicalResolution, canonicalFromUtc, canonicalToUtc, cancellationToken);
        var remainingGaps = FindGaps(finalCanonical, canonicalFromUtc, canonicalToUtc, canonicalInterval);
        var series = BuildSeries(request, canonicalResolution, finalCanonical);
        var status = remainingGaps.Count == 0 ? MarketDataStatus.Completed : MarketDataStatus.Partial;
        var source = initial.Count == 0 ? MarketDataResultSource.None : MarketDataResultSource.LocalCache;

        return new MarketDataResult(
            status,
            series,
            source,
            remainingGaps,
            0,
            0);
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

}
