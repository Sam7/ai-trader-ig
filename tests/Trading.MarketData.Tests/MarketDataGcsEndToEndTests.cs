using FluentAssertions;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataGcsEndToEndTests
{
    [Fact]
    public async Task RealGcsMirrorWorkflow_WhenEnabled_ShouldPublishMirrorRestartAndPreserveDataAfterCorruptUpdate()
    {
        if (!IsEnabled())
        {
            return;
        }

        var bucket = Environment.GetEnvironmentVariable("MARKETDATA_GCS_E2E_BUCKET")
            ?? throw new InvalidOperationException("MARKETDATA_GCS_E2E_BUCKET is required.");
        var prefix = Environment.GetEnvironmentVariable("MARKETDATA_GCS_E2E_PREFIX") ?? "codex-e2e";
        var objectName = $"{prefix.TrimEnd('/')}/market-data-{Guid.NewGuid():N}.sqlite";

        using var workspace = TestWorkspace.Create();
        var objectStore = new GcsMarketDataSnapshotObjectStore(StorageClient.Create());
        var sourceStore = new SqliteMarketDataStore(workspace.SourceDatabasePath);
        await sourceStore.UpsertAsync(
        [
            CreateStoredBar("2026-06-29T00:00:00Z", 100m),
        ]);

        var publisher = workspace.CreatePublisher(objectStore, bucket, objectName);
        var publish = await publisher.PublishOnceAsync();
        publish.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);

        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(objectStore, localStore, bucket, objectName);
        var sync = await synchronizer.SynchronizeOnceAsync();
        sync.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);

        var mirrored = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        mirrored.Should().ContainSingle();

        await localStore.UpsertAsync(
        [
            CreateStoredBar("2026-06-29T00:05:00Z", 105m, MarketDataSource.Stream),
        ]);
        var restarted = workspace.CreateSynchronizer(objectStore, localStore, bucket, objectName);
        (await restarted.SynchronizeOnceAsync()).Status.Should().Be(MarketDataSnapshotRefreshStatus.Unchanged);

        var corruptPath = Path.Combine(workspace.DirectoryPath, "corrupt.sqlite");
        await File.WriteAllTextAsync(corruptPath, "not sqlite");
        await objectStore.UploadAsync(
            bucket,
            objectName,
            corruptPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = await MarketDataSnapshotValidator.ComputeSha256Async(corruptPath),
            });

        var corruptResult = await restarted.SynchronizeOnceAsync();
        corruptResult.Status.Should().Be(MarketDataSnapshotRefreshStatus.Failed);
        var afterCorrupt = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"));
        afterCorrupt.Should().HaveCount(2);
    }

    private static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("RUN_MARKETDATA_GCS_E2E"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static StoredPriceBar CreateStoredBar(
        string timestampUtc,
        decimal bidOpen,
        MarketDataSource source = MarketDataSource.Stream)
        => StoredPriceBar.FromPriceBar(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            new PriceBar(
                DateTimeOffset.Parse(timestampUtc),
                bidOpen,
                bidOpen + 1m,
                bidOpen - 1m,
                bidOpen + 0.5m,
                bidOpen + 0.2m,
                bidOpen + 1.2m,
                bidOpen - 0.8m,
                bidOpen + 0.7m,
                10),
            source);

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directoryPath;

        private TestWorkspace(string directoryPath)
        {
            _directoryPath = directoryPath;
            SourceDatabasePath = Path.Combine(directoryPath, "source.sqlite");
            LocalDatabasePath = Path.Combine(directoryPath, "local.sqlite");
        }

        public static InstrumentId Instrument { get; } = new("CS.D.BITCOIN.CFD.IP");

        public string DirectoryPath => _directoryPath;

        public string SourceDatabasePath { get; }

        public string LocalDatabasePath { get; }

        public static TestWorkspace Create()
            => new(Directory.CreateTempSubdirectory().FullName);

        public MarketDataSnapshotPublisher CreatePublisher(
            IMarketDataSnapshotObjectStore objectStore,
            string bucket,
            string objectName)
            => new(
                objectStore,
                new MarketDataSnapshotValidator(),
                new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
                Options.Create(CreateOptions(SourceDatabasePath, bucket, objectName)),
                NullLogger<MarketDataSnapshotPublisher>.Instance);

        public MarketDataSnapshotSynchronizer CreateSynchronizer(
            IMarketDataSnapshotObjectStore objectStore,
            SqliteMarketDataStore localStore,
            string bucket,
            string objectName)
        {
            var options = Options.Create(CreateOptions(LocalDatabasePath, bucket, objectName));
            return new MarketDataSnapshotSynchronizer(
                objectStore,
                new MarketDataSnapshotValidator(),
                localStore,
                new FileMarketDataMirrorStateStore(options),
                new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
                options,
                NullLogger<MarketDataSnapshotSynchronizer>.Instance);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }

        private MarketDataOptions CreateOptions(string storePath, string bucket, string objectName)
            => new()
            {
                StorePath = storePath,
                CloudSnapshot = new MarketDataCloudSnapshotOptions
                {
                    BucketName = bucket,
                    ObjectName = objectName,
                    Publisher = new MarketDataSnapshotPublisherOptions
                    {
                        Enabled = true,
                        StagingDirectory = Path.Combine(_directoryPath, "publisher"),
                    },
                    Mirror = new MarketDataSnapshotMirrorOptions
                    {
                        Enabled = true,
                        SnapshotDirectory = Path.Combine(_directoryPath, "snapshots"),
                        StatePath = Path.Combine(_directoryPath, "state.json"),
                        LockPath = Path.Combine(_directoryPath, "sync.lock"),
                    },
                },
            };
    }
}
