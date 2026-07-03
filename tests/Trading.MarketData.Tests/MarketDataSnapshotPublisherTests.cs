using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataSnapshotPublisherTests
{
    [Fact]
    public async Task PublishOnceAsync_ShouldCreateValidatedSnapshotWhileSourceHasOpenWriteTransaction()
    {
        using var workspace = TestWorkspace.Create();
        var store = new SqliteMarketDataStore(workspace.DatabasePath);
        await store.UpsertAsync(
        [
            CreateStoredBar("2026-06-29T00:00:00Z"),
        ]);
        await using var writeConnection = await OpenConnectionAsync(workspace.DatabasePath);
        await using var transaction = await writeConnection.BeginTransactionAsync();
        await InsertUncommittedBarAsync(writeConnection, (SqliteTransaction)transaction);
        var objectStore = new FakeUploadObjectStore(workspace.UploadedSnapshotPath);
        var publisher = workspace.CreatePublisher(objectStore);

        var result = await publisher.PublishOnceAsync();

        result.Status.Should().Be(MarketDataSnapshotRefreshStatus.Succeeded);
        File.Exists(workspace.UploadedSnapshotPath).Should().BeTrue();
        var validation = await new MarketDataSnapshotValidator().ValidateAsync(workspace.UploadedSnapshotPath);
        validation.FinalPriceBarCount.Should().Be(1);
        objectStore.Uploads.Should().ContainSingle();
        objectStore.Uploads[0].Metadata.Should().ContainKey("sha256");
    }

    private static StoredPriceBar CreateStoredBar(string timestampUtc)
        => StoredPriceBar.FromPriceBar(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"),
            PriceResolution.FiveMinutes,
            new PriceBar(
                DateTimeOffset.Parse(timestampUtc),
                100m,
                101m,
                99m,
                100.5m,
                100.2m,
                101.2m,
                99.2m,
                100.7m,
                10),
            MarketDataSource.Stream);

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task InsertUncommittedBarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        await using var insertInstrument = connection.CreateCommand();
        insertInstrument.Transaction = transaction;
        insertInstrument.CommandText = """
            INSERT OR IGNORE INTO instruments (instrument_value)
            VALUES ('CS.D.UNCOMMITTED.IP');
            """;
        await insertInstrument.ExecuteNonQueryAsync();

        await using var insertBar = connection.CreateCommand();
        insertBar.Transaction = transaction;
        insertBar.CommandText = """
            INSERT INTO price_bars (
                instrument_fk, resolution, bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc_ticks, last_seen_utc_ticks)
            SELECT id, 5, $ticks, 10000000, 10100000, 9900000, 10050000,
                   10020000, 10120000, 9920000, 10070000, 10, 1, 2, $ticks, $ticks
            FROM instruments
            WHERE instrument_value = 'CS.D.UNCOMMITTED.IP';
            """;
        insertBar.Parameters.AddWithValue("$ticks", DateTimeOffset.Parse("2026-06-29T00:05:00Z").UtcTicks);
        await insertBar.ExecuteNonQueryAsync();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directoryPath;

        private TestWorkspace(string directoryPath)
        {
            _directoryPath = directoryPath;
            DatabasePath = Path.Combine(directoryPath, "market-data.sqlite");
            UploadedSnapshotPath = Path.Combine(directoryPath, "uploaded.sqlite");
        }

        public string DatabasePath { get; }

        public string UploadedSnapshotPath { get; }

        public static TestWorkspace Create()
            => new(Directory.CreateTempSubdirectory().FullName);

        public MarketDataSnapshotPublisher CreatePublisher(FakeUploadObjectStore objectStore)
            => new(
                objectStore,
                new MarketDataSnapshotValidator(),
                new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
                Options.Create(new MarketDataOptions
                {
                    StorePath = DatabasePath,
                    CloudSnapshot = new MarketDataCloudSnapshotOptions
                    {
                        BucketName = "bucket",
                        ObjectName = "market-data.sqlite",
                        Publisher = new MarketDataSnapshotPublisherOptions
                        {
                            Enabled = true,
                            StagingDirectory = Path.Combine(_directoryPath, "staging"),
                        },
                    },
                }),
                NullLogger<MarketDataSnapshotPublisher>.Instance);

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }
    }

    private sealed class FakeUploadObjectStore : IMarketDataSnapshotObjectStore
    {
        private readonly string _destinationPath;

        public FakeUploadObjectStore(string destinationPath)
        {
            _destinationPath = destinationPath;
        }

        public List<UploadCall> Uploads { get; } = [];

        public Task<MarketDataSnapshotObject?> GetAsync(
            string bucketName,
            string objectName,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DownloadAsync(
            string bucketName,
            string objectName,
            string destinationPath,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UploadAsync(
            string bucketName,
            string objectName,
            string sourcePath,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
        {
            File.Copy(sourcePath, _destinationPath, overwrite: true);
            Uploads.Add(new UploadCall(bucketName, objectName, new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)));
            return Task.CompletedTask;
        }
    }

    private sealed record UploadCall(
        string BucketName,
        string ObjectName,
        IReadOnlyDictionary<string, string> Metadata);
}
