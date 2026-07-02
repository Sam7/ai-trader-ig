using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
                CreateBar("2026-06-29T00:00:00Z", 100.12345m),
                MarketDataSource.RestBackfill,
                observedAtUtc: DateTimeOffset.Parse("2026-06-29T00:01:00Z")),
            StoredPriceBar.FromPriceBar(
                instrument,
                PriceResolution.FiveMinutes,
                CreateBar("2026-06-29T00:00:00Z", 110.54321m),
                MarketDataSource.Stream,
                observedAtUtc: DateTimeOffset.Parse("2026-06-29T00:05:01Z")),
        ]);

        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));

        bars.Should().ContainSingle();
        bars[0].Bar.BidOpen.Should().Be(110.54321m);
        bars[0].Bar.BidHigh.Should().Be(111.54321m);
        bars[0].Bar.BidLow.Should().Be(109.54321m);
        bars[0].Bar.BidClose.Should().Be(111.04321m);
        bars[0].Bar.AskOpen.Should().Be(110.74321m);
        bars[0].Bar.AskHigh.Should().Be(111.74321m);
        bars[0].Bar.AskLow.Should().Be(109.74321m);
        bars[0].Bar.AskClose.Should().Be(111.24321m);
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

    [Fact]
    public async Task StreamIngestor_ShouldPersistCanonicalStreamBarsIntoSqliteStore()
    {
        using var database = TestDatabase.Create();
        IMarketDataStore store = new SqliteMarketDataStore(database.Path);
        var ingestor = new MarketDataStreamIngestor(
            store,
            Options.Create(new MarketDataOptions()),
            NullLogger<MarketDataStreamIngestor>.Instance);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        var result = await ingestor.IngestAsync(new StreamPriceBarUpdate(
            instrument,
            PriceResolution.FiveMinutes,
            CreateBar("2026-06-29T00:00:00Z", 100.12345m),
            IsFinal: false,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:03:00Z")));

        result.Status.Should().Be(MarketDataIngestStatus.Stored);

        result = await ingestor.IngestAsync(new StreamPriceBarUpdate(
            instrument,
            PriceResolution.FiveMinutes,
            CreateBar("2026-06-29T00:00:00Z", 101.54321m),
            IsFinal: true,
            ObservedAtUtc: DateTimeOffset.Parse("2026-06-29T00:05:01Z")));

        result.Status.Should().Be(MarketDataIngestStatus.Stored);

        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"));

        bars.Should().ContainSingle();
        bars[0].Source.Should().Be(MarketDataSource.Stream);
        bars[0].IsFinal.Should().BeTrue();
        bars[0].Bar.BidClose.Should().Be(102.04321m);
        bars[0].FirstSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:03:00Z"));
        bars[0].LastSeenUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:01Z"));
    }

    [Fact]
    public async Task Migration_ShouldUpgradeLegacyTextSchemaAndShrinkTheDatabase()
    {
        using var database = TestDatabase.Create();
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");
        await SeedLegacyDatabaseAsync(database.Path, instrument, barCount: 400);
        var beforePageCount = await ReadPageCountAsync(database.Path);

        var store = new SqliteMarketDataStore(database.Path);

        var bars = await store.GetRangeAsync(
            instrument,
            PriceResolution.FiveMinutes,
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-29T00:25:00Z"));

        bars.Select(bar => bar.Bar.TimestampUtc)
            .Should().Equal(
                DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:05:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:10:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:15:00Z"),
                DateTimeOffset.Parse("2026-06-29T00:20:00Z"));
        bars[0].Bar.BidOpen.Should().Be(100.12345m);
        bars.Last().Source.Should().Be(MarketDataSource.Stream);

        var health = await store.GetAsync(instrument, PriceResolution.FiveMinutes);
        health.Should().NotBeNull();
        health!.ConnectionState.Should().Be(MarketDataConnectionState.Connected);
        health.UnresolvedGaps.Should().ContainSingle();

        var afterPageCount = await ReadPageCountAsync(database.Path);

        afterPageCount.Should().BeLessThan(beforePageCount);
        (await IndexExistsAsync(database.Path, "ix_price_bars_range")).Should().BeFalse();
        (await TableExistsAsync(database.Path, "price_bars_legacy")).Should().BeFalse();
        (await ReadSqlAsync(database.Path, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'price_bars';"))
            .Should().Contain("WITHOUT ROWID");
        (await ReadSqlAsync(database.Path, "SELECT COUNT(*) FROM instruments;"))
            .Should().Be("1");
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

    private static async Task SeedLegacyDatabaseAsync(
        string databasePath,
        InstrumentId instrument,
        int barCount)
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
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = OFF;

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
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY (instrument_id, resolution, bucket_start_utc)
            );

            CREATE INDEX ix_price_bars_range
                ON price_bars (instrument_id, resolution, bucket_start_utc);

            CREATE TABLE market_data_coverage (
                instrument_id TEXT NOT NULL,
                resolution TEXT NOT NULL,
                from_utc TEXT NOT NULL,
                to_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                checked_at_utc TEXT NOT NULL,
                message TEXT NULL,
                broker_error_code TEXT NULL,
                PRIMARY KEY (instrument_id, resolution, from_utc, to_utc)
            );

            CREATE INDEX ix_market_data_coverage_range
                ON market_data_coverage (instrument_id, resolution, from_utc, to_utc);

            CREATE TABLE market_data_health (
                instrument_id TEXT NOT NULL,
                resolution TEXT NOT NULL,
                connection_state TEXT NOT NULL,
                last_received_update_utc TEXT NULL,
                latest_completed_candle_utc TEXT NULL,
                repair_state TEXT NOT NULL,
                unresolved_gaps_json TEXT NOT NULL,
                last_historical_repair_status TEXT NULL,
                last_historical_repair_message TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (instrument_id, resolution)
            );
            """;
        await command.ExecuteNonQueryAsync();

        await using (var insertBar = connection.CreateCommand())
        {
            insertBar.CommandText = """
                INSERT INTO price_bars (
                    instrument_id, resolution, bucket_start_utc, bid_open, bid_high, bid_low, bid_close,
                    ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc, last_seen_utc)
                VALUES (
                    $instrument_id, $resolution, $bucket_start_utc, $bid_open, $bid_high, $bid_low, $bid_close,
                    $ask_open, $ask_high, $ask_low, $ask_close, $volume, $is_final, $source, $first_seen_utc, $last_seen_utc);
                """;

            for (var index = 0; index < barCount; index++)
            {
                var timestamp = DateTimeOffset.Parse("2026-06-29T00:00:00Z").AddMinutes(index * 5);
                var open = 100.12345m + index;
                insertBar.Parameters.Clear();
                insertBar.Parameters.AddWithValue("$instrument_id", instrument.Value);
                insertBar.Parameters.AddWithValue("$resolution", PriceResolution.FiveMinutes.ToString());
                insertBar.Parameters.AddWithValue("$bucket_start_utc", timestamp.ToUniversalTime().ToString("O"));
                insertBar.Parameters.AddWithValue("$bid_open", open.ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$bid_high", (open + 1m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$bid_low", (open - 1m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$bid_close", (open + 0.5m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$ask_open", (open + 0.2m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$ask_high", (open + 1.2m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$ask_low", (open - 0.8m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$ask_close", (open + 0.7m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                insertBar.Parameters.AddWithValue("$volume", 10 + index);
                insertBar.Parameters.AddWithValue("$is_final", index < barCount - 1 ? 1 : 0);
                insertBar.Parameters.AddWithValue("$source", MarketDataSource.Stream.ToString());
                insertBar.Parameters.AddWithValue("$first_seen_utc", timestamp.ToUniversalTime().ToString("O"));
                insertBar.Parameters.AddWithValue("$last_seen_utc", timestamp.AddMinutes(1).ToUniversalTime().ToString("O"));
                await insertBar.ExecuteNonQueryAsync();
            }
        }

        await using (var insertCoverage = connection.CreateCommand())
        {
            insertCoverage.CommandText = """
                INSERT INTO market_data_coverage (
                    instrument_id, resolution, from_utc, to_utc, status, checked_at_utc, message, broker_error_code)
                VALUES (
                    $instrument_id, $resolution, $from_utc, $to_utc, $status, $checked_at_utc, $message, $broker_error_code);
                """;
            insertCoverage.Parameters.AddWithValue("$instrument_id", instrument.Value);
            insertCoverage.Parameters.AddWithValue("$resolution", PriceResolution.FiveMinutes.ToString());
            insertCoverage.Parameters.AddWithValue("$from_utc", "2026-06-29T00:30:00.0000000+00:00");
            insertCoverage.Parameters.AddWithValue("$to_utc", "2026-06-29T00:35:00.0000000+00:00");
            insertCoverage.Parameters.AddWithValue("$status", MarketDataCoverageStatus.NoBars.ToString());
            insertCoverage.Parameters.AddWithValue("$checked_at_utc", "2026-06-29T00:36:00.0000000+00:00");
            insertCoverage.Parameters.AddWithValue("$message", "legacy coverage");
            insertCoverage.Parameters.AddWithValue("$broker_error_code", DBNull.Value);
            await insertCoverage.ExecuteNonQueryAsync();
        }

        await using (var insertHealth = connection.CreateCommand())
        {
            insertHealth.CommandText = """
                INSERT INTO market_data_health (
                    instrument_id, resolution, connection_state, last_received_update_utc,
                    latest_completed_candle_utc, repair_state, unresolved_gaps_json,
                    last_historical_repair_status, last_historical_repair_message, updated_at_utc)
                VALUES (
                    $instrument_id, $resolution, $connection_state, $last_received_update_utc,
                    $latest_completed_candle_utc, $repair_state, $unresolved_gaps_json,
                    $last_historical_repair_status, $last_historical_repair_message, $updated_at_utc);
                """;
            insertHealth.Parameters.AddWithValue("$instrument_id", instrument.Value);
            insertHealth.Parameters.AddWithValue("$resolution", PriceResolution.FiveMinutes.ToString());
            insertHealth.Parameters.AddWithValue("$connection_state", MarketDataConnectionState.Connected.ToString());
            insertHealth.Parameters.AddWithValue("$last_received_update_utc", "2026-06-29T00:15:00.0000000+00:00");
            insertHealth.Parameters.AddWithValue("$latest_completed_candle_utc", "2026-06-29T00:10:00.0000000+00:00");
            insertHealth.Parameters.AddWithValue("$repair_state", MarketDataRepairState.Degraded.ToString());
            insertHealth.Parameters.AddWithValue("$unresolved_gaps_json", """[{"FromUtc":"2026-06-29T00:05:00+00:00","ToUtc":"2026-06-29T00:10:00+00:00"}]""");
            insertHealth.Parameters.AddWithValue("$last_historical_repair_status", MarketDataCoverageStatus.AllowanceBlocked.ToString());
            insertHealth.Parameters.AddWithValue("$last_historical_repair_message", "legacy health");
            insertHealth.Parameters.AddWithValue("$updated_at_utc", "2026-06-29T00:20:00.0000000+00:00");
            await insertHealth.ExecuteNonQueryAsync();
        }

        await using var vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        await vacuum.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadPageCountAsync(string databasePath)
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
        command.CommandText = "PRAGMA page_count;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExistsAsync(string databasePath, string tableName)
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
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> IndexExistsAsync(string databasePath, string indexName)
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
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'index'
              AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", indexName);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<string> ReadSqlAsync(string databasePath, string query)
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
        command.CommandText = query;
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

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
