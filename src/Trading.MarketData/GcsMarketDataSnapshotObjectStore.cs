using Google;
using Google.Cloud.Storage.V1;
using System.Net;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace Trading.MarketData;

public sealed class GcsMarketDataSnapshotObjectStore : IMarketDataSnapshotObjectStore, IMarketDataObjectStore
{
    private const string MetadataSha256 = "sha256";
    private const string MetadataLatestBarTicks = "latest-bar-utc-ticks";

    private readonly StorageClient? _client;
    private readonly Lazy<StorageClient>? _lazyClient;

    public GcsMarketDataSnapshotObjectStore()
    {
        // Do not require local ADC during worker startup when cloud mirror/publishing is disabled.
        _lazyClient = new(StorageClient.Create, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GcsMarketDataSnapshotObjectStore(StorageClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    private StorageClient Client => _client ?? _lazyClient!.Value;

    public async Task<MarketDataSnapshotObject?> GetAsync(
        string bucketName,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var obj = await Client.GetObjectAsync(bucketName, objectName, cancellationToken: cancellationToken);
            return ToSnapshotObject(obj);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DownloadAsync(
        string bucketName,
        string objectName,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        await using var stream = File.Create(destinationPath);
        await Client.DownloadObjectAsync(bucketName, objectName, stream, cancellationToken: cancellationToken);
    }

    public async Task UploadAsync(
        string bucketName,
        string objectName,
        string sourcePath,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
        => await UploadAsync(bucketName, objectName, sourcePath, metadata, "application/vnd.sqlite3", cancellationToken);

    public async Task UploadAsync(
        string bucketName,
        string objectName,
        string sourcePath,
        IReadOnlyDictionary<string, string> metadata,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var obj = new GcsObject
        {
            Bucket = bucketName,
            Name = objectName,
            ContentType = contentType,
            Metadata = metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

        await Client.UploadObjectAsync(obj, stream, cancellationToken: cancellationToken);
    }

    private static MarketDataSnapshotObject ToSnapshotObject(GcsObject obj)
    {
        obj.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        obj.Metadata.TryGetValue(MetadataSha256, out var sha256);
        obj.Metadata.TryGetValue(MetadataLatestBarTicks, out var latestTicksText);

        return new MarketDataSnapshotObject(
            obj.Bucket,
            obj.Name,
            obj.Generation?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            obj.ETag,
            sha256,
            obj.UpdatedDateTimeOffset,
            obj.Size is null ? null : checked((long)obj.Size.Value),
            long.TryParse(latestTicksText, out var latestTicks)
                ? new DateTimeOffset(new DateTime(latestTicks, DateTimeKind.Utc))
                : null);
    }
}
