using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public string StorePath { get; init; } = Path.Combine("Logs", "MarketData", "ig-market-data.sqlite");

    public PriceResolution CanonicalResolution { get; init; } = PriceResolution.FiveMinutes;

    public bool BackfillEnabled { get; init; } = true;

    public MarketDataStreamIngestionOptions StreamIngestion { get; init; } = new();

    public MarketDataCloudSnapshotOptions CloudSnapshot { get; init; } = new();
}

public sealed class MarketDataStreamIngestionOptions
{
    public int DispatcherCapacity { get; init; } = 4096;

    public int BatchSize { get; init; } = 250;

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan HealthUpdateThrottle { get; init; } = TimeSpan.FromSeconds(30);

    public double WarningQueueUtilization { get; init; } = 0.70;

    public double CriticalQueueUtilization { get; init; } = 0.90;

    public TimeSpan StreamSilenceThreshold { get; init; } = TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (DispatcherCapacity <= 0)
        {
            throw new InvalidOperationException("Market-data stream dispatcher capacity must be greater than zero.");
        }

        if (BatchSize <= 0)
        {
            throw new InvalidOperationException("Market-data stream batch size must be greater than zero.");
        }

        if (FlushInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Market-data stream flush interval must be greater than zero.");
        }

        if (DrainTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Market-data stream drain timeout must be greater than zero.");
        }

        if (HealthUpdateThrottle < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Market-data stream health update throttle cannot be negative.");
        }

        if (WarningQueueUtilization <= 0 || WarningQueueUtilization > 1)
        {
            throw new InvalidOperationException("Market-data stream warning queue utilization must be between 0 and 1.");
        }

        if (CriticalQueueUtilization <= 0 || CriticalQueueUtilization > 1)
        {
            throw new InvalidOperationException("Market-data stream critical queue utilization must be between 0 and 1.");
        }

        if (WarningQueueUtilization >= CriticalQueueUtilization)
        {
            throw new InvalidOperationException("Market-data stream warning queue utilization must be lower than critical utilization.");
        }
    }
}
