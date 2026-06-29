using Trading.Abstractions;

namespace Trading.MarketData;

public static class PriceBarAggregator
{
    public static PriceSeries Aggregate(PriceSeries source, PriceResolution targetResolution)
    {
        if (source.Resolution is null)
        {
            throw new ArgumentException("Source price series must have a resolution.", nameof(source));
        }

        var sourceInterval = PriceResolutionIntervals.ToTimeSpan(source.Resolution.Value);
        var targetInterval = PriceResolutionIntervals.ToTimeSpan(targetResolution);
        if (targetInterval < sourceInterval || targetInterval.Ticks % sourceInterval.Ticks != 0)
        {
            throw new ArgumentException("Target resolution must be a whole multiple of the source resolution.", nameof(targetResolution));
        }

        var ordered = source.Bars
            .OrderBy(bar => bar.TimestampUtc)
            .ToArray();

        if (targetInterval == sourceInterval)
        {
            return new PriceSeries(source.Instrument, targetResolution, ordered);
        }

        var expectedSourceBars = checked((int)(targetInterval.Ticks / sourceInterval.Ticks));
        var aggregated = ordered
            .GroupBy(bar => PriceResolutionIntervals.AlignDown(bar.TimestampUtc, targetInterval))
            .OrderBy(group => group.Key)
            .Select(group => CreateAggregateBar(group.Key, group.OrderBy(bar => bar.TimestampUtc).ToArray(), sourceInterval, expectedSourceBars))
            .Where(bar => bar is not null)
            .Cast<PriceBar>()
            .ToArray();

        return new PriceSeries(source.Instrument, targetResolution, aggregated);
    }

    private static PriceBar? CreateAggregateBar(
        DateTimeOffset bucketStartUtc,
        IReadOnlyList<PriceBar> bars,
        TimeSpan sourceInterval,
        int expectedSourceBars)
    {
        if (bars.Count != expectedSourceBars)
        {
            return null;
        }

        for (var index = 0; index < bars.Count; index++)
        {
            if (bars[index].TimestampUtc != bucketStartUtc.AddTicks(sourceInterval.Ticks * index))
            {
                return null;
            }
        }

        var first = bars[0];
        var last = bars[^1];
        long? volume = bars.Any(bar => bar.Volume is not null)
            ? bars.Sum(bar => bar.Volume.GetValueOrDefault())
            : null;

        return new PriceBar(
            bucketStartUtc,
            first.BidOpen,
            bars.Max(bar => bar.BidHigh),
            bars.Min(bar => bar.BidLow),
            last.BidClose,
            first.AskOpen,
            bars.Max(bar => bar.AskHigh),
            bars.Min(bar => bar.AskLow),
            last.AskClose,
            volume);
    }
}
