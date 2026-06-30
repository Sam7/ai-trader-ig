using Trading.Abstractions;

namespace Trading.Automation.Execution;

internal static class IntradayChartAttachmentLabel
{
    public static string Format(
        string instrumentName,
        int chartLookbackHours,
        PriceResolution chartResolution)
        => $"{instrumentName} {FormatLookback(chartLookbackHours)} {FormatResolution(chartResolution)} chart";

    private static string FormatLookback(int chartLookbackHours)
    {
        if (chartLookbackHours % 24 == 0)
        {
            var days = chartLookbackHours / 24;
            return $"{days}-day";
        }

        return $"{chartLookbackHours}-hour";
    }

    private static string FormatResolution(PriceResolution resolution)
    {
        var interval = ToTimeSpan(resolution);
        if (interval.TotalDays >= 1 && interval.TotalDays % 1 == 0)
        {
            return $"{interval.TotalDays:0}-day";
        }

        if (interval.TotalHours >= 1 && interval.TotalHours % 1 == 0)
        {
            return $"{interval.TotalHours:0}-hour";
        }

        if (interval.TotalMinutes >= 1 && interval.TotalMinutes % 1 == 0)
        {
            return $"{interval.TotalMinutes:0}-minute";
        }

        return $"{interval.TotalSeconds:0}-second";
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
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported price resolution."),
        };
}
