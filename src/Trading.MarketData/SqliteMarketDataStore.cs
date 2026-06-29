using Microsoft.Data.Sqlite;
using System.Text.Json;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class SqliteMarketDataStore : IMarketDataStore, IMarketDataHealthStore
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;
    private bool _initialized;

    public SqliteMarketDataStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Pooling = false,
        }.ToString();
    }

    public async Task UpsertAsync(
        IReadOnlyList<StoredPriceBar> bars,
        CancellationToken cancellationToken = default)
    {
        if (bars.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var bar in bars)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO price_bars (
                        instrument_id, resolution, bucket_start_utc, bid_open, bid_high, bid_low, bid_close,
                        ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc, last_seen_utc)
                    VALUES (
                        $instrument_id, $resolution, $bucket_start_utc, $bid_open, $bid_high, $bid_low, $bid_close,
                        $ask_open, $ask_high, $ask_low, $ask_close, $volume, $is_final, $source, $first_seen_utc, $last_seen_utc)
                    ON CONFLICT(instrument_id, resolution, bucket_start_utc) DO UPDATE SET
                        bid_open = excluded.bid_open,
                        bid_high = excluded.bid_high,
                        bid_low = excluded.bid_low,
                        bid_close = excluded.bid_close,
                        ask_open = excluded.ask_open,
                        ask_high = excluded.ask_high,
                        ask_low = excluded.ask_low,
                        ask_close = excluded.ask_close,
                        volume = excluded.volume,
                        is_final = excluded.is_final,
                        source = excluded.source,
                        last_seen_utc = excluded.last_seen_utc;
                    """;

                AddParameters(command, bar);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredPriceBar>> GetRangeAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT instrument_id, resolution, bucket_start_utc, bid_open, bid_high, bid_low, bid_close,
                       ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc, last_seen_utc
                FROM price_bars
                WHERE instrument_id = $instrument_id
                  AND resolution = $resolution
                  AND bucket_start_utc >= $from_utc
                  AND bucket_start_utc < $to_utc
                ORDER BY bucket_start_utc ASC;
                """;
            command.Parameters.AddWithValue("$instrument_id", instrument.Value);
            command.Parameters.AddWithValue("$resolution", resolution.ToString());
            command.Parameters.AddWithValue("$from_utc", fromUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$to_utc", toUtc.ToUniversalTime().ToString("O"));

            var results = new List<StoredPriceBar>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadBar(reader));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredPriceBar?> GetLatestFinalAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT instrument_id, resolution, bucket_start_utc, bid_open, bid_high, bid_low, bid_close,
                       ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc, last_seen_utc
                FROM price_bars
                WHERE instrument_id = $instrument_id
                  AND resolution = $resolution
                  AND is_final = 1
                ORDER BY bucket_start_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$instrument_id", instrument.Value);
            command.Parameters.AddWithValue("$resolution", resolution.ToString());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadBar(reader)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketDataGap>> FindMissingCompletedRangesAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
            var completedToUtc = PriceResolutionIntervals.AlignDown(toUtc, interval);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var present = await ReadFinalBucketStartsAsync(
                connection,
                instrument,
                resolution,
                fromUtc,
                completedToUtc,
                cancellationToken);
            var covered = await ReadNoBarsCoverageAsync(
                connection,
                instrument,
                resolution,
                fromUtc,
                completedToUtc,
                cancellationToken);

            return FindMissingRanges(fromUtc, completedToUtc, interval, present, covered);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordCoverageAsync(
        MarketDataCoverageRecord coverage,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO market_data_coverage (
                    instrument_id, resolution, from_utc, to_utc, status, checked_at_utc, message, broker_error_code)
                VALUES (
                    $instrument_id, $resolution, $from_utc, $to_utc, $status, $checked_at_utc, $message, $broker_error_code)
                ON CONFLICT(instrument_id, resolution, from_utc, to_utc) DO UPDATE SET
                    status = excluded.status,
                    checked_at_utc = excluded.checked_at_utc,
                    message = excluded.message,
                    broker_error_code = excluded.broker_error_code;
                """;
            command.Parameters.AddWithValue("$instrument_id", coverage.Instrument.Value);
            command.Parameters.AddWithValue("$resolution", coverage.Resolution.ToString());
            command.Parameters.AddWithValue("$from_utc", coverage.FromUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$to_utc", coverage.ToUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$status", coverage.Status.ToString());
            command.Parameters.AddWithValue("$checked_at_utc", coverage.CheckedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$message", (object?)coverage.Message ?? DBNull.Value);
            command.Parameters.AddWithValue("$broker_error_code", (object?)coverage.BrokerErrorCode ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(
        MarketDataHealthRecord health,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO market_data_health (
                    instrument_id, resolution, connection_state, last_received_update_utc,
                    latest_completed_candle_utc, repair_state, unresolved_gaps_json,
                    last_historical_repair_status, last_historical_repair_message, updated_at_utc)
                VALUES (
                    $instrument_id, $resolution, $connection_state, $last_received_update_utc,
                    $latest_completed_candle_utc, $repair_state, $unresolved_gaps_json,
                    $last_historical_repair_status, $last_historical_repair_message, $updated_at_utc)
                ON CONFLICT(instrument_id, resolution) DO UPDATE SET
                    connection_state = excluded.connection_state,
                    last_received_update_utc = excluded.last_received_update_utc,
                    latest_completed_candle_utc = excluded.latest_completed_candle_utc,
                    repair_state = excluded.repair_state,
                    unresolved_gaps_json = excluded.unresolved_gaps_json,
                    last_historical_repair_status = excluded.last_historical_repair_status,
                    last_historical_repair_message = excluded.last_historical_repair_message,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$instrument_id", health.Instrument.Value);
            command.Parameters.AddWithValue("$resolution", health.Resolution.ToString());
            command.Parameters.AddWithValue("$connection_state", health.ConnectionState.ToString());
            command.Parameters.AddWithValue("$last_received_update_utc", ToDbValue(health.LastReceivedUpdateUtc));
            command.Parameters.AddWithValue("$latest_completed_candle_utc", ToDbValue(health.LatestCompletedCandleUtc));
            command.Parameters.AddWithValue("$repair_state", health.RepairState.ToString());
            command.Parameters.AddWithValue("$unresolved_gaps_json", JsonSerializer.Serialize(health.UnresolvedGaps));
            command.Parameters.AddWithValue("$last_historical_repair_status", health.LastHistoricalRepairStatus?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$last_historical_repair_message", (object?)health.LastHistoricalRepairMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at_utc", health.UpdatedAtUtc.ToUniversalTime().ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MarketDataHealthRecord?> GetAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT instrument_id, resolution, connection_state, last_received_update_utc,
                       latest_completed_candle_utc, repair_state, unresolved_gaps_json,
                       last_historical_repair_status, last_historical_repair_message, updated_at_utc
                FROM market_data_health
                WHERE instrument_id = $instrument_id
                  AND resolution = $resolution
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$instrument_id", instrument.Value);
            command.Parameters.AddWithValue("$resolution", resolution.ToString());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadHealth(reader)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await InitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS price_bars (
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

                CREATE INDEX IF NOT EXISTS ix_price_bars_range
                    ON price_bars (instrument_id, resolution, bucket_start_utc);

                CREATE TABLE IF NOT EXISTS market_data_coverage (
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

                CREATE INDEX IF NOT EXISTS ix_market_data_coverage_range
                    ON market_data_coverage (instrument_id, resolution, from_utc, to_utc);

                CREATE TABLE IF NOT EXISTS market_data_health (
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
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private static void AddParameters(SqliteCommand command, StoredPriceBar stored)
    {
        var bar = stored.Bar;
        command.Parameters.AddWithValue("$instrument_id", stored.Instrument.Value);
        command.Parameters.AddWithValue("$resolution", stored.Resolution.ToString());
        command.Parameters.AddWithValue("$bucket_start_utc", bar.TimestampUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$bid_open", bar.BidOpen.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bid_high", bar.BidHigh.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bid_low", bar.BidLow.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bid_close", bar.BidClose.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ask_open", bar.AskOpen.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ask_high", bar.AskHigh.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ask_low", bar.AskLow.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ask_close", bar.AskClose.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$volume", bar.Volume is null ? DBNull.Value : bar.Volume.Value);
        command.Parameters.AddWithValue("$is_final", stored.IsFinal ? 1 : 0);
        command.Parameters.AddWithValue("$source", stored.Source.ToString());
        command.Parameters.AddWithValue("$first_seen_utc", stored.FirstSeenUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$last_seen_utc", stored.LastSeenUtc.ToUniversalTime().ToString("O"));
    }

    private static StoredPriceBar ReadBar(SqliteDataReader reader)
    {
        var instrument = new InstrumentId(reader.GetString(0));
        var resolution = Enum.Parse<PriceResolution>(reader.GetString(1));
        var timestampUtc = DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime();
        long? volume = reader.IsDBNull(11) ? null : reader.GetInt64(11);
        var source = Enum.Parse<MarketDataSource>(reader.GetString(13));
        return new StoredPriceBar(
            instrument,
            resolution,
            new PriceBar(
                timestampUtc,
                ParseDecimal(reader.GetString(3)),
                ParseDecimal(reader.GetString(4)),
                ParseDecimal(reader.GetString(5)),
                ParseDecimal(reader.GetString(6)),
                ParseDecimal(reader.GetString(7)),
                ParseDecimal(reader.GetString(8)),
                ParseDecimal(reader.GetString(9)),
                ParseDecimal(reader.GetString(10)),
                volume),
            reader.GetInt32(12) == 1,
            source,
            DateTimeOffset.Parse(reader.GetString(14)).ToUniversalTime(),
            DateTimeOffset.Parse(reader.GetString(15)).ToUniversalTime());
    }

    private static decimal ParseDecimal(string value)
        => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static object ToDbValue(DateTimeOffset? value)
        => value is null ? DBNull.Value : value.Value.ToUniversalTime().ToString("O");

    private static MarketDataHealthRecord ReadHealth(SqliteDataReader reader)
    {
        var gaps = JsonSerializer.Deserialize<MarketDataGap[]>(reader.GetString(6)) ?? [];
        return new MarketDataHealthRecord(
            new InstrumentId(reader.GetString(0)),
            Enum.Parse<PriceResolution>(reader.GetString(1)),
            Enum.Parse<MarketDataConnectionState>(reader.GetString(2)),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)).ToUniversalTime(),
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)).ToUniversalTime(),
            Enum.Parse<MarketDataRepairState>(reader.GetString(5)),
            gaps,
            reader.IsDBNull(7) ? null : Enum.Parse<MarketDataCoverageStatus>(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)).ToUniversalTime());
    }

    private static async Task<HashSet<DateTimeOffset>> ReadFinalBucketStartsAsync(
        SqliteConnection connection,
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start_utc
            FROM price_bars
            WHERE instrument_id = $instrument_id
              AND resolution = $resolution
              AND is_final = 1
              AND bucket_start_utc >= $from_utc
              AND bucket_start_utc < $to_utc;
            """;
        command.Parameters.AddWithValue("$instrument_id", instrument.Value);
        command.Parameters.AddWithValue("$resolution", resolution.ToString());
        command.Parameters.AddWithValue("$from_utc", fromUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$to_utc", toUtc.ToUniversalTime().ToString("O"));

        var results = new HashSet<DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(DateTimeOffset.Parse(reader.GetString(0)).ToUniversalTime());
        }

        return results;
    }

    private static async Task<IReadOnlyList<MarketDataCoverageRecord>> ReadNoBarsCoverageAsync(
        SqliteConnection connection,
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT instrument_id, resolution, from_utc, to_utc, status, checked_at_utc, message, broker_error_code
            FROM market_data_coverage
            WHERE instrument_id = $instrument_id
              AND resolution = $resolution
              AND status = $status
              AND from_utc < $to_utc
              AND to_utc > $from_utc;
            """;
        command.Parameters.AddWithValue("$instrument_id", instrument.Value);
        command.Parameters.AddWithValue("$resolution", resolution.ToString());
        command.Parameters.AddWithValue("$status", MarketDataCoverageStatus.NoBars.ToString());
        command.Parameters.AddWithValue("$from_utc", fromUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$to_utc", toUtc.ToUniversalTime().ToString("O"));

        var results = new List<MarketDataCoverageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarketDataCoverageRecord(
                new InstrumentId(reader.GetString(0)),
                Enum.Parse<PriceResolution>(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime(),
                DateTimeOffset.Parse(reader.GetString(3)).ToUniversalTime(),
                Enum.Parse<MarketDataCoverageStatus>(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5)).ToUniversalTime(),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    private static IReadOnlyList<MarketDataGap> FindMissingRanges(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan interval,
        HashSet<DateTimeOffset> present,
        IReadOnlyList<MarketDataCoverageRecord> covered)
    {
        var gaps = new List<MarketDataGap>();
        DateTimeOffset? gapStart = null;
        var cursor = fromUtc;

        while (cursor < toUtc)
        {
            var missing = !present.Contains(cursor) && !IsCovered(cursor, covered);
            if (missing)
            {
                gapStart ??= cursor;
            }
            else if (gapStart is not null)
            {
                gaps.Add(new MarketDataGap(gapStart.Value, cursor));
                gapStart = null;
            }

            cursor = cursor.Add(interval);
        }

        if (gapStart is not null)
        {
            gaps.Add(new MarketDataGap(gapStart.Value, toUtc));
        }

        return gaps;
    }

    private static bool IsCovered(DateTimeOffset bucketStartUtc, IReadOnlyList<MarketDataCoverageRecord> covered)
        => covered.Any(record => bucketStartUtc >= record.FromUtc && bucketStartUtc < record.ToUtc);
}
