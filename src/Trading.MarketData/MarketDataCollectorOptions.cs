using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataCollectorOptions
{
    public PriceResolution Resolution { get; init; } = PriceResolution.FiveMinutes;

    public TimeSpan BootstrapLookback { get; init; } = TimeSpan.FromHours(6);
}
