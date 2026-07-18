using Trading.Abstractions;

namespace Trading.IG;

public static class IgStreamingConversions
{
    public static string ToIgChartScale(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.Minute => "1MINUTE",
            PriceResolution.FiveMinutes => "5MINUTE",
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported IG chart streaming resolution."),
        };
}
