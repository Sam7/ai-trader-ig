using Trading.Abstractions;

namespace Trading.Charting.MemoryLab;

public static class PriceSeriesFixtureFactory
{
    public static PriceSeries Create(ChartMemoryScenario scenario, bool includeGaps = true)
    {
        scenario.Validate();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var spacing = ToTimeSpan(scenario.Resolution);
        var bars = new List<PriceBar>(scenario.BarCount);

        for (var index = 0; index < scenario.BarCount; index++)
        {
            var gap = includeGaps && index > 0 && index % 97 == 0 ? spacing * 3 : TimeSpan.Zero;
            var timestamp = start.AddTicks((spacing.Ticks * index) + gap.Ticks * (index / 97));
            var open = 100m + (index % 200) * 0.05m;
            bars.Add(new PriceBar(
                timestamp,
                open,
                open + 0.75m,
                open - 0.35m,
                open + 0.20m,
                open + 0.10m,
                open + 0.85m,
                open - 0.25m,
                open + 0.30m,
                1_000 + index));
        }

        return new PriceSeries(new InstrumentId("MEMORY.LAB"), scenario.Resolution, bars);
    }

    private static TimeSpan ToTimeSpan(PriceResolution resolution)
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
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null),
        };
}
