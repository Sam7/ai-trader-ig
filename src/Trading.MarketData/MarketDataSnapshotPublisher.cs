using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Trading.MarketData;

public sealed class MarketDataSnapshotPublisher
{
    private readonly IMarketDataSnapshotObjectStore _objectStore;
    private readonly MarketDataSnapshotValidator _validator;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataSnapshotPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MarketDataSnapshotPublisher(
        IMarketDataSnapshotObjectStore objectStore,
        MarketDataSnapshotValidator validator,
        IMarketDataClock clock,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataSnapshotPublisher> logger)
    {
        _objectStore = objectStore;
        _validator = validator;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MarketDataSnapshotRefreshResult> PublishOnceAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _options.CloudSnapshot;
        var publisher = snapshot.Publisher;
        if (!publisher.Enabled)
        {
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Disabled, "Snapshot publisher is disabled.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.BucketName) || string.IsNullOrWhiteSpace(snapshot.ObjectName))
        {
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, "Snapshot bucket and object name are required.");
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.AlreadyRunning, "Snapshot publish is already running.");
        }

        try
        {
            var sourcePath = Path.GetFullPath(_options.StorePath);
            if (!File.Exists(sourcePath))
            {
                return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, $"Market-data database was not found: {sourcePath}");
            }

            Directory.CreateDirectory(publisher.StagingDirectory);
            var tempSnapshotPath = Path.Combine(publisher.StagingDirectory, $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                CreateSqliteBackup(sourcePath, tempSnapshotPath);
                var validation = await _validator.ValidateAsync(tempSnapshotPath, cancellationToken);
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sha256"] = validation.Sha256,
                    ["created-at-utc"] = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["final-price-bar-count"] = validation.FinalPriceBarCount.ToString(CultureInfo.InvariantCulture),
                    ["latest-bar-utc-ticks"] = validation.LatestBarUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                };

                await _objectStore.UploadAsync(
                    snapshot.BucketName,
                    snapshot.ObjectName,
                    tempSnapshotPath,
                    metadata,
                    cancellationToken);

                _logger.LogInformation(
                    "Published market-data snapshot to gs://{Bucket}/{Object}. Bars: {BarCount}. Latest bar UTC: {LatestBarUtc}. SHA-256: {Sha256}.",
                    snapshot.BucketName,
                    snapshot.ObjectName,
                    validation.FinalPriceBarCount,
                    validation.LatestBarUtc,
                    validation.Sha256);

                return new MarketDataSnapshotRefreshResult(
                    MarketDataSnapshotRefreshStatus.Succeeded,
                    "Snapshot published.",
                    RemoteSha256: validation.Sha256,
                    ImportedBarCount: validation.FinalPriceBarCount,
                    LatestBarUtc: validation.LatestBarUtc);
            }
            finally
            {
                TryDelete(tempSnapshotPath);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to publish market-data snapshot.");
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void CreateSqliteBackup(string sourcePath, string destinationPath)
    {
        SQLitePCL.Batteries_V2.Init();
        TryDelete(destinationPath);
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Pooling = false,
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Pooling = false,
        }.ToString();

        using var source = new SqliteConnection(sourceConnectionString);
        using var destination = new SqliteConnection(destinationConnectionString);
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
