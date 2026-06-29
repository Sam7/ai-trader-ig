using Trading.Abstractions;

namespace Trading.MarketData;

internal static class PriceResolutionIntervals
{
    public static TimeSpan ToTimeSpan(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.Second => TimeSpan.FromSeconds(1),
            PriceResolution.Minute => TimeSpan.FromMinutes(1),
            PriceResolution.TwoMinutes => TimeSpan.FromMinutes(2),
            PriceResolution.ThreeMinutes => TimeSpan.FromMinutes(3),
            PriceResolution.FiveMinutes => TimeSpan.FromMinutes(5),
            PriceResolution.TenMinutes => TimeSpan.FromMinutes(10),
            PriceResolution.FifteenMinutes => TimeSpan.FromMinutes(15),
            PriceResolution.ThirtyMinutes => TimeSpan.FromMinutes(30),
            PriceResolution.Hour => TimeSpan.FromHours(1),
            PriceResolution.TwoHours => TimeSpan.FromHours(2),
            PriceResolution.ThreeHours => TimeSpan.FromHours(3),
            PriceResolution.FourHours => TimeSpan.FromHours(4),
            PriceResolution.Day => TimeSpan.FromDays(1),
            PriceResolution.Week => TimeSpan.FromDays(7),
            PriceResolution.Month => TimeSpan.FromDays(30),
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported price resolution."),
        };

    public static DateTimeOffset AlignDown(DateTimeOffset timestampUtc, TimeSpan interval)
    {
        var utc = timestampUtc.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % interval.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static DateTimeOffset AlignUp(DateTimeOffset timestampUtc, TimeSpan interval)
    {
        var down = AlignDown(timestampUtc, interval);
        return down == timestampUtc.ToUniversalTime() ? down : down.Add(interval);
    }
}
