namespace Trading.MarketData;

public sealed record MarketDataSnapshotObject(
    string BucketName,
    string ObjectName,
    string? Generation,
    string? ETag,
    string? Sha256,
    DateTimeOffset? UpdatedUtc,
    long? SizeBytes,
    DateTimeOffset? LatestBarUtc);

public sealed record MarketDataSnapshotValidationResult(
    string SnapshotPath,
    string Sha256,
    long SizeBytes,
    int FinalPriceBarCount,
    DateTimeOffset? LatestBarUtc);

public sealed record MarketDataSnapshotImportResult(
    int SnapshotFinalPriceBarCount,
    DateTimeOffset? LatestBarUtc);

public enum MarketDataSnapshotRefreshStatus
{
    Disabled = 0,
    Succeeded = 1,
    Unchanged = 2,
    AlreadyRunning = 3,
    OlderSnapshotRejected = 4,
    Failed = 5,
}

public sealed record MarketDataSnapshotRefreshResult(
    MarketDataSnapshotRefreshStatus Status,
    string Message,
    string? RemoteGeneration = null,
    string? RemoteSha256 = null,
    string? LocalSnapshotPath = null,
    int ImportedBarCount = 0,
    DateTimeOffset? LatestBarUtc = null);

public sealed record MarketDataMirrorState(
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulSyncUtc,
    string? RemoteGeneration,
    string? RemoteSha256,
    string? LocalSnapshotPath,
    DateTimeOffset? LatestBarUtc,
    MarketDataSnapshotRefreshStatus LastStatus,
    string? LastMessage);

public sealed record MarketDataMirrorStatus(
    bool Enabled,
    bool IsConfigured,
    bool IsStale,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulSyncUtc,
    DateTimeOffset? LatestBarUtc,
    string? RemoteGeneration,
    string? RemoteSha256,
    string? LocalSnapshotPath,
    MarketDataSnapshotRefreshStatus LastStatus,
    string? LastMessage);

public interface IMarketDataSnapshotObjectStore
{
    Task<MarketDataSnapshotObject?> GetAsync(
        string bucketName,
        string objectName,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        string bucketName,
        string objectName,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string bucketName,
        string objectName,
        string sourcePath,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}

public interface IMarketDataSnapshotImporter
{
    Task<MarketDataSnapshotImportResult> ImportSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default);
}
