namespace Trading.MarketData;

public sealed class MarketDataCloudSnapshotOptions
{
    public string BucketName { get; init; } = string.Empty;

    public string ObjectName { get; init; } = "market-data/ig-market-data.sqlite";

    public MarketDataSnapshotPublisherOptions Publisher { get; init; } = new();

    public MarketDataSnapshotMirrorOptions Mirror { get; init; } = new();
}

public sealed class MarketDataSnapshotPublisherOptions
{
    public bool Enabled { get; init; }

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    public string StagingDirectory { get; init; } = Path.Combine("Logs", "MarketData", "snapshot-publisher");
}

public sealed class MarketDataSnapshotMirrorOptions
{
    public bool Enabled { get; init; }

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    public string SnapshotDirectory { get; init; } = Path.Combine("Logs", "MarketData", "cloud-mirror", "snapshots");

    public string StatePath { get; init; } = Path.Combine("Logs", "MarketData", "cloud-mirror", "state.json");

    public string LockPath { get; init; } = Path.Combine("Logs", "MarketData", "cloud-mirror", "sync.lock");

    public int RetainedSnapshotCount { get; init; } = 3;

    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);
}
