namespace Trading.Automation.Configuration;

public sealed class MarketDataCollectionOptions
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
}
