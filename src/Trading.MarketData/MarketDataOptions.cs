using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public string StorePath { get; init; } = Path.Combine("Logs", "MarketData", "ig-market-data.sqlite");

    public PriceResolution CanonicalResolution { get; init; } = PriceResolution.FiveMinutes;

    public bool BackfillEnabled { get; init; } = true;
}
