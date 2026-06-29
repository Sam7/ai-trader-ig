using FluentAssertions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class SqliteMarketDataStoreTests
{
    [Fact]
    public async Task UpsertAsync_ShouldReplaceExistingBucketWithoutDuplicates()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z", 100m),
                MarketDataSource.RestBackfill,
                observedAtUtc: DateTimeOffset.Parse("2026-06-29T00:01:00Z")),
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z", 110m),
                MarketDataSource.Stream,
                observedAtUtc: DateTimeOffset.Parse("2026-06-29T00:05:01Z")),
        ]);

        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));

        bars.Should().ContainSingle();
        bars[0].Bar.BidOpen.Should().Be(110m);
        bars[0].Source.Should().Be(MarketDataSource.Stream);
        bars[0].FirstSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:01:00Z"));
        bars[0].LastSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:01Z"));
    }

    [Fact]
    public async Task GetRangeAsync_ShouldReturnOrderedBarsWithinHalfOpenRange()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, CreateBar("2026-06-29T00:10:00Z"), MarketDataSource.RestBackfill),
            StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, CreateBar("2026-06-29T00:00:00Z"), MarketDataSource.RestBackfill),
            StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, CreateBar("2026-06-29T00:05:00Z"), MarketDataSource.RestBackfill),
        ]);

        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"));

        bars.Select(bar => bar.Bar.TimestampUtc)
            .Should().Equal(
                DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
    }

    [Fact]
    public async Task GetLatestFinalAsync_ShouldIgnoreFormingCandles()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z"),
                MarketDataSource.Stream,
                isFinal: true),
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:05:00Z"),
                MarketDataSource.Stream,
                isFinal: false),
        ]);

        var latest = await store.GetLatestFinalAsync(instrument, PriceResolution.FiveMinutes);

        latest.Should().NotBeNull();
        latest!.Bar.TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:00:00Z"));
    }

    [Fact]
    public async Task FindMissingCompletedRangesAsync_ShouldExcludePresentFinalBarsAndCoveredNoBarsRanges()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, CreateBar("2026-06-29T00:00:00Z"), MarketDataSource.Stream),
            StoredPriceBar.FromPriceBar(instrument, PriceResolution.FiveMinutes, CreateBar("2026-06-29T00:15:00Z"), MarketDataSource.Stream),
        ]);
        await store.RecordCoverageAsync(new MarketDataCoverageRecord(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"),
            MarketDataCoverageStatus.NoBars,
            DateTimeOffset.Parse("2026-06-29T00:20:00Z"),
            null,
            null));

        var gaps = await store.FindMissingCompletedRangesAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:20:00Z"));

        gaps.Should().ContainSingle();
        gaps[0].Should().Be(new MarketDataGap(
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:15:00Z")));
    }

    [Fact]
    public async Task FindMissingCompletedRangesAsync_ShouldNotReportCurrentFormingBucket()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        var gaps = await store.FindMissingCompletedRangesAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:07:00Z"));

        gaps.Should().ContainSingle();
        gaps[0].Should().Be(new MarketDataGap(
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z")));
    }

    [Fact]
    public async Task InitializeAsync_ShouldEnableWalMode()
    {
        using var database = TestDatabase.Create();
        var store = new SqliteMarketDataStore(database.Path);

        await store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z"),
                MarketDataSource.Stream),
        ]);

        var journalMode = await ReadJournalModeAsync(database.Path);

        journalMode.Should().Be("wal");
    }

    [Fact]
    public async Task HealthStore_ShouldPersistCollectorHealthAcrossStoreInstances()
    {
        using var database = TestDatabase.Create();
        IMarketDataHealthStore writer = new SqliteMarketDataStore(database.Path);
        IMarketDataHealthStore reader = new SqliteMarketDataStore(database.Path);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        await writer.UpsertAsync(new MarketDataHealthRecord(
            instrument,
            PriceResolution.FiveMinutes,
            MarketDataConnectionState.Connected,
            DateTimeOffset.Parse("2026-06-29T00:16:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:10:00Z"),
            MarketDataRepairState.Degraded,
            [new MarketDataGap(DateTimeOffset.Parse("2026-06-29T00:05:00Z"), DateTimeOffset.Parse("2026-06-29T00:10:00Z"))],
            MarketDataCoverageStatus.AllowanceBlocked,
            "allowance exceeded",
            DateTimeOffset.Parse("2026-06-29T00:17:00Z")));

        var health = await reader.GetAsync(instrument, PriceResolution.FiveMinutes);

        health.Should().NotBeNull();
        health!.ConnectionState.Should().Be(MarketDataConnectionState.Connected);
        health.RepairState.Should().Be(MarketDataRepairState.Degraded);
        health.LatestCompletedCandleUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:10:00Z"));
        health.UnresolvedGaps.Should().ContainSingle()
            .Which.Should().Be(new MarketDataGap(
                DateTimeOffset.Parse("2026-06-29T00:05:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:10:00Z")));
        health.LastHistoricalRepairStatus.Should().Be(MarketDataCoverageStatus.AllowanceBlocked);
    }

    private static PriceBar CreateBar(string timestampUtc, decimal bidOpen = 100m)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            bidOpen,
            bidOpen + 1m,
            bidOpen - 1m,
            bidOpen + 0.5m,
            bidOpen + 0.2m,
            bidOpen + 1.2m,
            bidOpen - 0.8m,
            bidOpen + 0.7m,
            10);

    private static async Task<string> ReadJournalModeAsync(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _directory;

        private TestDatabase(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static TestDatabase Create()
        {
            var directory = Directory.CreateTempSubdirectory();
            return new TestDatabase(directory.FullName, System.IO.Path.Combine(directory.FullName, "market-data.sqlite"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
