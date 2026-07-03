using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataSnapshotSynchronizerTests
{
    [Fact]
    public async Task SynchronizeOnceAsync_WithUnchangedSnapshot_ShouldNotDownloadAgain()
    {
        using var workspace = TestWorkspace.Create();
        var sourceSnapshot = await workspace.CreateSnapshotAsync("source.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(sourceSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);

        var first = await synchronizer.SynchronizeOnceAsync();
        var second = await synchronizer.SynchronizeOnceAsync();

        first.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);
        second.Status.Should().Be(MarketDataSnapshotRefreshStatus.Unchanged);
        remote.DownloadCount.Should().Be(1);
        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.CloudMirror);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_WithChangedSnapshot_ShouldDownloadAndImport()
    {
        using var workspace = TestWorkspace.Create();
        var firstSnapshot = await workspace.CreateSnapshotAsync("first.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var secondSnapshot = await workspace.CreateSnapshotAsync(
            "second.sqlite",
            CreateStoredBar("2026-06-29T00:00:00Z", 100m),
            CreateStoredBar("2026-06-29T00:05:00Z", 105m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(firstSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);

        await synchronizer.SynchronizeOnceAsync();
        await remote.ReplaceAsync(secondSnapshot, generation: "2");

        var result = await synchronizer.SynchronizeOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);
        remote.DownloadCount.Should().Be(2);
        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"));
        bars.Select(bar => bar.Bar.TimestampUtc)
            .Should().Equal(
                DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
    }

    [Fact]
    public async Task SynchronizeOnceAsync_WithInvalidSnapshot_ShouldPreserveLastValidData()
    {
        using var workspace = TestWorkspace.Create();
        var validSnapshot = await workspace.CreateSnapshotAsync("valid.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(validSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);
        await synchronizer.SynchronizeOnceAsync();
        var invalidSnapshot = Path.Combine(workspace.DirectoryPath, "invalid.sqlite");
        await File.WriteAllTextAsync(invalidSnapshot, "not sqlite");
        await remote.ReplaceAsync(invalidSnapshot, generation: "2");

        var result = await synchronizer.SynchronizeOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.Failed);
        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Bar.BidOpen.Should().Be(100m);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_WithSchemaIncompatibleSnapshot_ShouldFailSafely()
    {
        using var workspace = TestWorkspace.Create();
        var incompatible = Path.Combine(workspace.DirectoryPath, "legacy.sqlite");
        await workspace.CreateLegacySnapshotAsync(incompatible);
        var remote = await FakeSnapshotObjectStore.CreateAsync(incompatible, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);

        var result = await synchronizer.SynchronizeOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.Failed);
        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task SynchronizeOnceAsync_RepeatedProcessing_ShouldBeIdempotent()
    {
        using var workspace = TestWorkspace.Create();
        var sourceSnapshot = await workspace.CreateSnapshotAsync("source.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(sourceSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);

        await synchronizer.SynchronizeOnceAsync();
        await remote.ReplaceAsync(sourceSnapshot, generation: "2");
        await synchronizer.SynchronizeOnceAsync();

        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportSnapshotAsync_WithOverlappingLocalStream_ShouldPreferNewerLocalData()
    {
        using var workspace = TestWorkspace.Create();
        var snapshot = await workspace.CreateSnapshotAsync(
            "source.sqlite",
            CreateStoredBar("2026-06-29T00:00:00Z", 100m, observedAtUtc: "2026-06-29T00:01:00Z"));
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        await localStore.UpsertAsync(
        [
            CreateStoredBar(
                "2026-06-29T00:00:00Z",
                120m,
                MarketDataSource.Stream,
                observedAtUtc: "2026-06-29T00:02:00Z"),
        ]);

        await localStore.ImportSnapshotAsync(snapshot);

        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.Stream);
        bars[0].Bar.BidOpen.Should().Be(120m);
    }

    [Fact]
    public async Task ImportSnapshotAsync_WithLocalNonFinalStream_ShouldReplaceWithCloudFinal()
    {
        using var workspace = TestWorkspace.Create();
        var snapshot = await workspace.CreateSnapshotAsync(
            "source.sqlite",
            CreateStoredBar("2026-06-29T00:00:00Z", 100m, observedAtUtc: "2026-06-29T00:01:00Z"));
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        await localStore.UpsertAsync(
        [
            CreateStoredBar(
                "2026-06-29T00:00:00Z",
                120m,
                MarketDataSource.Stream,
                isFinal: false,
                observedAtUtc: "2026-06-29T00:02:00Z"),
        ]);

        await localStore.ImportSnapshotAsync(snapshot);

        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.CloudMirror);
        bars[0].IsFinal.Should().BeTrue();
        bars[0].Bar.BidOpen.Should().Be(100m);
    }

    [Fact]
    public async Task ImportSnapshotAsync_WithConcurrentLocalWrite_ShouldCompleteSafely()
    {
        using var workspace = TestWorkspace.Create();
        var snapshotBars = Enumerable.Range(0, 200)
            .Select(index => CreateStoredBar(DateTimeOffset.Parse("2026-06-29T00:00:00Z").AddMinutes(index * 5).ToString("O"), 100m + index))
            .ToArray();
        var snapshot = await workspace.CreateSnapshotAsync("source.sqlite", snapshotBars);
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);

        await Task.WhenAll(
            localStore.ImportSnapshotAsync(snapshot),
            localStore.UpsertAsync(
            [
                CreateStoredBar(
                    "2026-06-29T20:00:00Z",
                    500m,
                    MarketDataSource.Stream,
                    observedAtUtc: "2026-06-29T20:01:00Z"),
            ]));

        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T20:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T20:05:00Z"));
        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.Stream);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_ShouldPersistStateAcrossRestart()
    {
        using var workspace = TestWorkspace.Create();
        var sourceSnapshot = await workspace.CreateSnapshotAsync("source.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(sourceSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        await workspace.CreateSynchronizer(remote, localStore).SynchronizeOnceAsync();

        var restarted = workspace.CreateSynchronizer(remote, localStore);
        var result = await restarted.SynchronizeOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.Unchanged);
        remote.DownloadCount.Should().Be(1);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_ShouldRejectOlderSnapshots()
    {
        using var workspace = TestWorkspace.Create();
        var newerSnapshot = await workspace.CreateSnapshotAsync(
            "newer.sqlite",
            CreateStoredBar("2026-06-29T00:00:00Z", 100m),
            CreateStoredBar("2026-06-29T00:05:00Z", 105m));
        var olderSnapshot = await workspace.CreateSnapshotAsync("older.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 110m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(newerSnapshot, generation: "1");
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);
        await synchronizer.SynchronizeOnceAsync();
        await remote.ReplaceAsync(olderSnapshot, generation: "2");

        var result = await synchronizer.SynchronizeOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.OlderSnapshotRejected);
        var bars = await localStore.GetRangeAsync(
            TestWorkspace.Instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"));
        bars.Should().HaveCount(2);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_ShouldPreventOverlappingRuns()
    {
        using var workspace = TestWorkspace.Create();
        var sourceSnapshot = await workspace.CreateSnapshotAsync("source.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(sourceSnapshot, generation: "1");
        remote.BlockDownloads = true;
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);

        var first = synchronizer.SynchronizeOnceAsync();
        await remote.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await synchronizer.SynchronizeOnceAsync();
        remote.ReleaseDownload();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        second.Status.Should().Be(MarketDataSnapshotRefreshStatus.AlreadyRunning);
    }

    [Fact]
    public async Task SynchronizeOnceAsync_WhenCancelledDuringDownload_ShouldReleaseRunGate()
    {
        using var workspace = TestWorkspace.Create();
        var sourceSnapshot = await workspace.CreateSnapshotAsync("source.sqlite", CreateStoredBar("2026-06-29T00:00:00Z", 100m));
        var remote = await FakeSnapshotObjectStore.CreateAsync(sourceSnapshot, generation: "1");
        remote.BlockDownloads = true;
        var localStore = new SqliteMarketDataStore(workspace.LocalDatabasePath);
        var synchronizer = workspace.CreateSynchronizer(remote, localStore);
        using var cancellation = new CancellationTokenSource();

        var first = synchronizer.SynchronizeOnceAsync(cancellation.Token);
        await remote.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        var action = async () => await first;
        await action.Should().ThrowAsync<OperationCanceledException>();
        remote.BlockDownloads = false;
        remote.ReleaseDownload();

        var second = await synchronizer.SynchronizeOnceAsync();
        second.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);
    }

    private static StoredPriceBar CreateStoredBar(
        string timestampUtc,
        decimal bidOpen,
        MarketDataSource source = MarketDataSource.Stream,
        bool isFinal = true,
        string? observedAtUtc = null)
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
            source,
            isFinal,
            observedAtUtc: observedAtUtc is null ? null : DateTimeOffset.Parse(observedAtUtc));

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directoryPath;

        private TestWorkspace(string directoryPath)
        {
            _directoryPath = directoryPath;
            LocalDatabasePath = Path.Combine(directoryPath, "local.sqlite");
        }

        public static InstrumentId Instrument { get; } = new("CS.D.BITCOIN.CFD.IP");

        public string DirectoryPath => _directoryPath;

        public string LocalDatabasePath { get; }

        public static TestWorkspace Create()
            => new(Directory.CreateTempSubdirectory().FullName);

        public MarketDataSnapshotSynchronizer CreateSynchronizer(
            FakeSnapshotObjectStore objectStore,
            SqliteMarketDataStore localStore)
        {
            var options = Options.Create(new MarketDataOptions
            {
                StorePath = LocalDatabasePath,
                CloudSnapshot = new MarketDataCloudSnapshotOptions
                {
                    BucketName = "bucket",
                    ObjectName = "market-data.sqlite",
                    Mirror = new MarketDataSnapshotMirrorOptions
                    {
                        Enabled = true,
                        SnapshotDirectory = Path.Combine(_directoryPath, "snapshots"),
                        StatePath = Path.Combine(_directoryPath, "state.json"),
                        LockPath = Path.Combine(_directoryPath, "sync.lock"),
                    },
                },
            });

            return new MarketDataSnapshotSynchronizer(
                objectStore,
                new MarketDataSnapshotValidator(),
                localStore,
                new FileMarketDataMirrorStateStore(options),
                new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
                options,
                NullLogger<MarketDataSnapshotSynchronizer>.Instance);
        }

        public async Task<string> CreateSnapshotAsync(string fileName, params StoredPriceBar[] bars)
        {
            var path = Path.Combine(_directoryPath, fileName);
            var store = new SqliteMarketDataStore(path);
            await store.UpsertAsync(bars);
            await CheckpointAsync(path);
            return path;
        }

        public async Task CreateLegacySnapshotAsync(string path)
        {
            Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new()
            {
                DataSource = path,
                Pooling = false,
            };
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE price_bars (
                    instrument_id TEXT NOT NULL,
                    resolution TEXT NOT NULL,
                    bucket_start_utc TEXT NOT NULL,
                    bid_open TEXT NOT NULL,
                    bid_high TEXT NOT NULL,
                    bid_low TEXT NOT NULL,
                    bid_close TEXT NOT NULL,
                    ask_open TEXT NOT NULL,
                    ask_high TEXT NOT NULL,
                    ask_low TEXT NOT NULL,
                    ask_close TEXT NOT NULL,
                    volume INTEGER NULL,
                    is_final INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }

        private static async Task CheckpointAsync(string databasePath)
        {
            Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new()
            {
                DataSource = databasePath,
                Pooling = false,
            };
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FakeSnapshotObjectStore : IMarketDataSnapshotObjectStore
    {
        private string _sourcePath;
        private string _sha256 = string.Empty;
        private DateTimeOffset? _latestBarUtc;

        private FakeSnapshotObjectStore(string sourcePath, string generation)
        {
            _sourcePath = sourcePath;
            Generation = generation;
        }

        public string Generation { get; private set; }

        public int DownloadCount { get; private set; }

        public bool BlockDownloads { get; set; }

        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource DownloadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<FakeSnapshotObjectStore> CreateAsync(string sourcePath, string generation)
        {
            var store = new FakeSnapshotObjectStore(sourcePath, generation);
            await store.RefreshMetadataAsync();
            return store;
        }

        public async Task ReplaceAsync(string sourcePath, string generation)
        {
            _sourcePath = sourcePath;
            Generation = generation;
            await RefreshMetadataAsync();
        }

        public void ReleaseDownload()
        {
            DownloadRelease.TrySetResult();
        }

        public Task<MarketDataSnapshotObject?> GetAsync(
            string bucketName,
            string objectName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MarketDataSnapshotObject?>(new MarketDataSnapshotObject(
                bucketName,
                objectName,
                Generation,
                ETag: Generation,
                _sha256,
                DateTimeOffset.Parse("2026-06-29T01:00:00Z"),
                new FileInfo(_sourcePath).Length,
                _latestBarUtc));

        public async Task DownloadAsync(
            string bucketName,
            string objectName,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            _ = bucketName;
            _ = objectName;
            DownloadCount++;
            DownloadStarted.TrySetResult();
            if (BlockDownloads)
            {
                await DownloadRelease.Task.WaitAsync(cancellationToken);
            }

            File.Copy(_sourcePath, destinationPath, overwrite: true);
        }

        public Task UploadAsync(
            string bucketName,
            string objectName,
            string sourcePath,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private async Task RefreshMetadataAsync()
        {
            try
            {
                var validation = await new MarketDataSnapshotValidator().ValidateAsync(_sourcePath);
                _sha256 = validation.Sha256;
                _latestBarUtc = validation.LatestBarUtc;
            }
            catch (MarketDataSnapshotValidationException)
            {
                _sha256 = await MarketDataSnapshotValidator.ComputeSha256Async(_sourcePath);
                _latestBarUtc = null;
            }
        }
    }
}
