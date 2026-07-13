using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class SqliteMarketDataStore : IMarketDataStore, IMarketDataHealthStore, IMarketDataSnapshotImporter, IMarketSessionEvidenceStore, IMarketDataRecoveryStore
{
    private const long PriceScale = 100_000;

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
            var instrumentIds = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (var bar in bars)
            {
                var instrumentId = await GetOrCreateInstrumentIdAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    bar.Instrument.Value,
                    instrumentIds,
                    cancellationToken);

                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO price_bars (
                        instrument_fk, resolution, bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                        ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc_ticks, last_seen_utc_ticks)
                    VALUES (
                        $instrument_fk, $resolution, $bucket_start_utc_ticks, $bid_open, $bid_high, $bid_low, $bid_close,
                        $ask_open, $ask_high, $ask_low, $ask_close, $volume, $is_final, $source, $first_seen_utc_ticks, $last_seen_utc_ticks)
                    ON CONFLICT(instrument_fk, resolution, bucket_start_utc_ticks) DO UPDATE SET
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
                        last_seen_utc_ticks = excluded.last_seen_utc_ticks;
                    """;

                AddPriceBarParameters(command, bar, instrumentId);
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

            var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
            if (instrumentId is null)
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                       ask_open, ask_high, ask_low, ask_close, volume, is_final, source,
                       first_seen_utc_ticks, last_seen_utc_ticks
                FROM price_bars
                WHERE instrument_fk = $instrument_fk
                  AND resolution = $resolution
                  AND bucket_start_utc_ticks >= $from_utc_ticks
                  AND bucket_start_utc_ticks < $to_utc_ticks
                ORDER BY bucket_start_utc_ticks ASC;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
            command.Parameters.AddWithValue("$resolution", (int)resolution);
            command.Parameters.AddWithValue("$from_utc_ticks", fromUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$to_utc_ticks", toUtc.ToUniversalTime().UtcTicks);

            var results = new List<StoredPriceBar>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadBar(instrument, resolution, reader));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertRecoveryStateAsync(MarketDataRecoveryState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var instrumentId = await GetOrCreateInstrumentIdAsync(connection, (SqliteTransaction)transaction, state.Instrument.Value, new Dictionary<string, long>(StringComparer.Ordinal), cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO market_data_recovery (instrument_fk, resolution, from_utc_ticks, to_utc_ticks, cursor_utc_ticks, is_complete, returned_points, remaining_allowance, allowance_expires_utc_ticks, last_failure)
                VALUES ($instrument_fk, $resolution, $from, $to, $cursor, $complete, $points, $remaining, $expires, $failure)
                ON CONFLICT(instrument_fk, resolution, from_utc_ticks, to_utc_ticks) DO UPDATE SET
                    cursor_utc_ticks = excluded.cursor_utc_ticks, is_complete = excluded.is_complete, returned_points = excluded.returned_points,
                    remaining_allowance = excluded.remaining_allowance, allowance_expires_utc_ticks = excluded.allowance_expires_utc_ticks, last_failure = excluded.last_failure;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId); command.Parameters.AddWithValue("$resolution", (int)state.Resolution);
            command.Parameters.AddWithValue("$from", state.FromUtc.UtcTicks); command.Parameters.AddWithValue("$to", state.ToUtc.UtcTicks); command.Parameters.AddWithValue("$cursor", state.CursorUtc.UtcTicks);
            command.Parameters.AddWithValue("$complete", state.IsComplete ? 1 : 0); command.Parameters.AddWithValue("$points", state.ReturnedPoints);
            command.Parameters.AddWithValue("$remaining", (object?)state.RemainingAllowance ?? DBNull.Value); command.Parameters.AddWithValue("$expires", state.AllowanceExpiresAtUtc is null ? DBNull.Value : state.AllowanceExpiresAtUtc.Value.UtcTicks); command.Parameters.AddWithValue("$failure", (object?)state.LastFailure ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MarketDataRecoveryState>> GetRecoveryStatesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken); await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT i.instrument_value, r.resolution, r.from_utc_ticks, r.to_utc_ticks, r.cursor_utc_ticks, r.is_complete, r.returned_points, r.remaining_allowance, r.allowance_expires_utc_ticks, r.last_failure FROM market_data_recovery r JOIN instruments i ON i.id = r.instrument_fk ORDER BY i.instrument_value, r.from_utc_ticks;""";
            var result = new List<MarketDataRecoveryState>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(new MarketDataRecoveryState(new InstrumentId(reader.GetString(0)), (PriceResolution)reader.GetInt32(1), new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero), new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero), new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero), reader.GetInt64(5) != 0, reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero), reader.IsDBNull(9) ? null : reader.GetString(9)));
            return result;
        }
        finally { _gate.Release(); }
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

            var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
            if (instrumentId is null)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                       ask_open, ask_high, ask_low, ask_close, volume, is_final, source,
                       first_seen_utc_ticks, last_seen_utc_ticks
                FROM price_bars
                WHERE instrument_fk = $instrument_fk
                  AND resolution = $resolution
                  AND is_final = 1
                ORDER BY bucket_start_utc_ticks DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
            command.Parameters.AddWithValue("$resolution", (int)resolution);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadBar(instrument, resolution, reader)
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
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var instrumentIds = new Dictionary<string, long>(StringComparer.Ordinal);
            var instrumentId = await GetOrCreateInstrumentIdAsync(
                connection,
                (SqliteTransaction)transaction,
                coverage.Instrument.Value,
                instrumentIds,
                cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO market_data_coverage (
                    instrument_fk, resolution, from_utc_ticks, to_utc_ticks, status, checked_at_utc_ticks, message, broker_error_code)
                VALUES (
                    $instrument_fk, $resolution, $from_utc_ticks, $to_utc_ticks, $status, $checked_at_utc_ticks, $message, $broker_error_code)
                ON CONFLICT(instrument_fk, resolution, from_utc_ticks, to_utc_ticks) DO UPDATE SET
                    status = excluded.status,
                    checked_at_utc_ticks = excluded.checked_at_utc_ticks,
                    message = excluded.message,
                    broker_error_code = excluded.broker_error_code;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId);
            command.Parameters.AddWithValue("$resolution", (int)coverage.Resolution);
            command.Parameters.AddWithValue("$from_utc_ticks", coverage.FromUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$to_utc_ticks", coverage.ToUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$status", (int)coverage.Status);
            command.Parameters.AddWithValue("$checked_at_utc_ticks", coverage.CheckedAtUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$message", (object?)coverage.Message ?? DBNull.Value);
            command.Parameters.AddWithValue("$broker_error_code", (object?)coverage.BrokerErrorCode ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketDataCoverageRecord>> GetCoverageAsync(
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

            return await ReadCoverageAsync(connection, instrument, resolution, fromUtc, toUtc, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertSessionStatusAsync(
        MarketSessionStatusRecord status,
        CancellationToken cancellationToken = default)
    {
        status.Validate();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var instrumentIds = new Dictionary<string, long>(StringComparer.Ordinal);
            var instrumentId = await GetOrCreateInstrumentIdAsync(
                connection,
                (SqliteTransaction)transaction,
                status.Instrument.Value,
                instrumentIds,
                cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO market_session_status (
                    instrument_fk, observed_at_utc_ticks, valid_until_utc_ticks, status, source, message)
                VALUES (
                    $instrument_fk, $observed_at_utc_ticks, $valid_until_utc_ticks, $status, $source, $message)
                ON CONFLICT(instrument_fk, observed_at_utc_ticks) DO UPDATE SET
                    valid_until_utc_ticks = excluded.valid_until_utc_ticks,
                    status = excluded.status,
                    source = excluded.source,
                    message = excluded.message;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId);
            command.Parameters.AddWithValue("$observed_at_utc_ticks", status.ObservedAtUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$valid_until_utc_ticks", status.ValidUntilUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$status", (int)status.Status);
            command.Parameters.AddWithValue("$source", (int)status.Source);
            command.Parameters.AddWithValue("$message", (object?)status.Message ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketSessionStatusRecord>> GetSessionStatusAsync(
        InstrumentId instrument,
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

            var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
            if (instrumentId is null)
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT observed_at_utc_ticks, valid_until_utc_ticks, status, source, message
                FROM market_session_status
                WHERE instrument_fk = $instrument_fk
                  AND observed_at_utc_ticks < $to_utc_ticks
                  AND valid_until_utc_ticks > $from_utc_ticks
                ORDER BY observed_at_utc_ticks ASC;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
            command.Parameters.AddWithValue("$from_utc_ticks", fromUtc.ToUniversalTime().UtcTicks);
            command.Parameters.AddWithValue("$to_utc_ticks", toUtc.ToUniversalTime().UtcTicks);

            var results = new List<MarketSessionStatusRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new MarketSessionStatusRecord(
                    instrument,
                    (MarketStatus)reader.GetInt32(2),
                    FromDbTicks(reader.GetInt64(0)),
                    FromDbTicks(reader.GetInt64(1)),
                    (MarketSessionEvidenceSource)reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return results;
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
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var instrumentIds = new Dictionary<string, long>(StringComparer.Ordinal);
            var instrumentId = await GetOrCreateInstrumentIdAsync(
                connection,
                (SqliteTransaction)transaction,
                health.Instrument.Value,
                instrumentIds,
                cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO market_data_health (
                    instrument_fk, resolution, connection_state, last_received_update_utc_ticks,
                    latest_completed_candle_utc_ticks, repair_state, unresolved_gaps_json,
                    last_historical_repair_status, last_historical_repair_message, updated_at_utc_ticks)
                VALUES (
                    $instrument_fk, $resolution, $connection_state, $last_received_update_utc_ticks,
                    $latest_completed_candle_utc_ticks, $repair_state, $unresolved_gaps_json,
                    $last_historical_repair_status, $last_historical_repair_message, $updated_at_utc_ticks)
                ON CONFLICT(instrument_fk, resolution) DO UPDATE SET
                    connection_state = excluded.connection_state,
                    last_received_update_utc_ticks = excluded.last_received_update_utc_ticks,
                    latest_completed_candle_utc_ticks = excluded.latest_completed_candle_utc_ticks,
                    repair_state = excluded.repair_state,
                    unresolved_gaps_json = excluded.unresolved_gaps_json,
                    last_historical_repair_status = excluded.last_historical_repair_status,
                    last_historical_repair_message = excluded.last_historical_repair_message,
                    updated_at_utc_ticks = excluded.updated_at_utc_ticks;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId);
            command.Parameters.AddWithValue("$resolution", (int)health.Resolution);
            command.Parameters.AddWithValue("$connection_state", (int)health.ConnectionState);
            command.Parameters.AddWithValue("$last_received_update_utc_ticks", ToDbTicksOrDbNull(health.LastReceivedUpdateUtc));
            command.Parameters.AddWithValue("$latest_completed_candle_utc_ticks", ToDbTicksOrDbNull(health.LatestCompletedCandleUtc));
            command.Parameters.AddWithValue("$repair_state", (int)health.RepairState);
            command.Parameters.AddWithValue("$unresolved_gaps_json", JsonSerializer.Serialize(health.UnresolvedGaps));
            command.Parameters.AddWithValue("$last_historical_repair_status", health.LastHistoricalRepairStatus is null
                ? DBNull.Value
                : (int)health.LastHistoricalRepairStatus.Value);
            command.Parameters.AddWithValue("$last_historical_repair_message", (object?)health.LastHistoricalRepairMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at_utc_ticks", health.UpdatedAtUtc.ToUniversalTime().UtcTicks);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

            var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
            if (instrumentId is null)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT connection_state, last_received_update_utc_ticks,
                       latest_completed_candle_utc_ticks, repair_state, unresolved_gaps_json,
                       last_historical_repair_status, last_historical_repair_message, updated_at_utc_ticks
                FROM market_data_health
                WHERE instrument_fk = $instrument_fk
                  AND resolution = $resolution
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
            command.Parameters.AddWithValue("$resolution", (int)resolution);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadHealth(instrument, resolution, reader)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MarketDataSnapshotImportResult> ImportSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(snapshotPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Market-data snapshot was not found.", fullPath);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var attach = connection.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $snapshot_path AS snapshot;";
            attach.Parameters.AddWithValue("$snapshot_path", fullPath);
            await attach.ExecuteNonQueryAsync(cancellationToken);

            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await ExecuteNonQueryAsync(connection, (SqliteTransaction)transaction, """
                    INSERT OR IGNORE INTO instruments (instrument_value)
                    SELECT source.instrument_value
                    FROM snapshot.instruments AS source;
                    """, cancellationToken);

                await using var import = connection.CreateCommand();
                import.Transaction = (SqliteTransaction)transaction;
                import.CommandText = """
                    INSERT INTO price_bars (
                        instrument_fk, resolution, bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                        ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc_ticks, last_seen_utc_ticks)
                    SELECT
                        destination_instrument.id,
                        source_bar.resolution,
                        source_bar.bucket_start_utc_ticks,
                        source_bar.bid_open,
                        source_bar.bid_high,
                        source_bar.bid_low,
                        source_bar.bid_close,
                        source_bar.ask_open,
                        source_bar.ask_high,
                        source_bar.ask_low,
                        source_bar.ask_close,
                        source_bar.volume,
                        source_bar.is_final,
                        $cloud_source,
                        source_bar.first_seen_utc_ticks,
                        source_bar.last_seen_utc_ticks
                    FROM snapshot.price_bars AS source_bar
                    INNER JOIN snapshot.instruments AS source_instrument
                        ON source_instrument.id = source_bar.instrument_fk
                    INNER JOIN instruments AS destination_instrument
                        ON destination_instrument.instrument_value = source_instrument.instrument_value
                    WHERE source_bar.is_final = 1
                    ON CONFLICT(instrument_fk, resolution, bucket_start_utc_ticks) DO UPDATE SET
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
                        last_seen_utc_ticks = excluded.last_seen_utc_ticks
                    WHERE price_bars.source <> $manual_source
                      AND (
                          price_bars.is_final = 0
                          OR (
                              excluded.last_seen_utc_ticks > price_bars.last_seen_utc_ticks
                          )
                          OR (
                              excluded.last_seen_utc_ticks = price_bars.last_seen_utc_ticks
                              AND price_bars.source NOT IN ($stream_source, $manual_source, $cloud_source)
                          )
                      );
                    """;
                import.Parameters.AddWithValue("$cloud_source", (int)MarketDataSource.CloudMirror);
                import.Parameters.AddWithValue("$manual_source", (int)MarketDataSource.ManualImport);
                import.Parameters.AddWithValue("$stream_source", (int)MarketDataSource.Stream);
                await import.ExecuteNonQueryAsync(cancellationToken);

                var finalCount = checked((int)await ExecuteScalarLongAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "SELECT COUNT(*) FROM snapshot.price_bars WHERE is_final = 1;",
                    cancellationToken));
                var latestTicks = await ExecuteNullableScalarLongAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "SELECT MAX(bucket_start_utc_ticks) FROM snapshot.price_bars WHERE is_final = 1;",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new MarketDataSnapshotImportResult(
                    finalCount,
                    latestTicks is null ? null : FromDbTicks(latestTicks.Value));
            }
            finally
            {
                await using var detach = connection.CreateCommand();
                detach.CommandText = "DETACH DATABASE snapshot;";
                await detach.ExecuteNonQueryAsync(cancellationToken);
            }
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
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var hasPriceBars = await TableExistsAsync(connection, "price_bars", cancellationToken);
            var hasCoverage = await TableExistsAsync(connection, "market_data_coverage", cancellationToken);
            var hasHealth = await TableExistsAsync(connection, "market_data_health", cancellationToken);
            var priceBarsHasInstrumentFk = hasPriceBars
                && await ColumnExistsAsync(connection, "price_bars", "instrument_fk", cancellationToken);
            var coverageHasInstrumentFk = hasCoverage
                && await ColumnExistsAsync(connection, "market_data_coverage", "instrument_fk", cancellationToken);
            var healthHasInstrumentFk = hasHealth
                && await ColumnExistsAsync(connection, "market_data_health", "instrument_fk", cancellationToken);
            var hasLegacyShape = (hasPriceBars && !priceBarsHasInstrumentFk)
                || (hasCoverage && !coverageHasInstrumentFk)
                || (hasHealth && !healthHasInstrumentFk);

            if (hasLegacyShape)
            {
                await MigrateLegacySchemaAsync(connection, cancellationToken);
            }
            else
            {
                await CreateSchemaAsync(connection, cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS instruments (
                id INTEGER PRIMARY KEY,
                instrument_value TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS price_bars (
                instrument_fk INTEGER NOT NULL REFERENCES instruments(id),
                resolution INTEGER NOT NULL,
                bucket_start_utc_ticks INTEGER NOT NULL,
                bid_open INTEGER NOT NULL,
                bid_high INTEGER NOT NULL,
                bid_low INTEGER NOT NULL,
                bid_close INTEGER NOT NULL,
                ask_open INTEGER NOT NULL,
                ask_high INTEGER NOT NULL,
                ask_low INTEGER NOT NULL,
                ask_close INTEGER NOT NULL,
                volume INTEGER NULL,
                is_final INTEGER NOT NULL,
                source INTEGER NOT NULL,
                first_seen_utc_ticks INTEGER NOT NULL,
                last_seen_utc_ticks INTEGER NOT NULL,
                PRIMARY KEY (instrument_fk, resolution, bucket_start_utc_ticks)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS market_data_coverage (
                instrument_fk INTEGER NOT NULL REFERENCES instruments(id),
                resolution INTEGER NOT NULL,
                from_utc_ticks INTEGER NOT NULL,
                to_utc_ticks INTEGER NOT NULL,
                status INTEGER NOT NULL,
                checked_at_utc_ticks INTEGER NOT NULL,
                message TEXT NULL,
                broker_error_code TEXT NULL,
                PRIMARY KEY (instrument_fk, resolution, from_utc_ticks, to_utc_ticks)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS market_data_health (
                instrument_fk INTEGER NOT NULL REFERENCES instruments(id),
                resolution INTEGER NOT NULL,
                connection_state INTEGER NOT NULL,
                last_received_update_utc_ticks INTEGER NULL,
                latest_completed_candle_utc_ticks INTEGER NULL,
                repair_state INTEGER NOT NULL,
                unresolved_gaps_json TEXT NOT NULL,
                last_historical_repair_status INTEGER NULL,
                last_historical_repair_message TEXT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                PRIMARY KEY (instrument_fk, resolution)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS market_session_status (
                instrument_fk INTEGER NOT NULL REFERENCES instruments(id),
                observed_at_utc_ticks INTEGER NOT NULL,
                valid_until_utc_ticks INTEGER NOT NULL,
                status INTEGER NOT NULL,
                source INTEGER NOT NULL,
                message TEXT NULL,
                PRIMARY KEY (instrument_fk, observed_at_utc_ticks)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS market_data_recovery (
                instrument_fk INTEGER NOT NULL REFERENCES instruments(id),
                resolution INTEGER NOT NULL,
                from_utc_ticks INTEGER NOT NULL,
                to_utc_ticks INTEGER NOT NULL,
                cursor_utc_ticks INTEGER NOT NULL,
                is_complete INTEGER NOT NULL,
                returned_points INTEGER NOT NULL,
                remaining_allowance INTEGER NULL,
                allowance_expires_utc_ticks INTEGER NULL,
                last_failure TEXT NULL,
                PRIMARY KEY (instrument_fk, resolution, from_utc_ticks, to_utc_ticks)
            ) WITHOUT ROWID;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateLegacySchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteNonQueryAsync(connection, (SqliteTransaction)transaction, """
                PRAGMA foreign_keys = OFF;
                """, cancellationToken);

            await RenameTableIfExistsAsync(connection, (SqliteTransaction)transaction, "price_bars", "price_bars_legacy", cancellationToken);
            await RenameTableIfExistsAsync(connection, (SqliteTransaction)transaction, "market_data_coverage", "market_data_coverage_legacy", cancellationToken);
            await RenameTableIfExistsAsync(connection, (SqliteTransaction)transaction, "market_data_health", "market_data_health_legacy", cancellationToken);

            await CreateSchemaAsync(connection, cancellationToken);

            var instrumentIds = new Dictionary<string, long>(StringComparer.Ordinal);
            await CopyLegacyPriceBarsAsync(connection, (SqliteTransaction)transaction, instrumentIds, cancellationToken);
            await CopyLegacyCoverageAsync(connection, (SqliteTransaction)transaction, instrumentIds, cancellationToken);
            await CopyLegacyHealthAsync(connection, (SqliteTransaction)transaction, instrumentIds, cancellationToken);

            await ExecuteNonQueryAsync(connection, (SqliteTransaction)transaction, """
                DROP TABLE IF EXISTS price_bars_legacy;
                DROP TABLE IF EXISTS market_data_coverage_legacy;
                DROP TABLE IF EXISTS market_data_health_legacy;
                PRAGMA foreign_keys = ON;
                """, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await ExecuteNonQueryAsync(connection, null, "VACUUM;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken);
    }

    private static async Task CopyLegacyPriceBarsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, long> instrumentIds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "price_bars_legacy", cancellationToken))
        {
            return;
        }

        var rows = new List<LegacyPriceBarRow>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT instrument_id, resolution, bucket_start_utc, bid_open, bid_high, bid_low, bid_close,
                       ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc, last_seen_utc
                FROM price_bars_legacy;
                """;

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LegacyPriceBarRow(
                    reader.GetString(0),
                    Enum.Parse<PriceResolution>(reader.GetString(1)),
                    DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime(),
                    ParseDecimal(reader.GetString(3)),
                    ParseDecimal(reader.GetString(4)),
                    ParseDecimal(reader.GetString(5)),
                    ParseDecimal(reader.GetString(6)),
                    ParseDecimal(reader.GetString(7)),
                    ParseDecimal(reader.GetString(8)),
                    ParseDecimal(reader.GetString(9)),
                    ParseDecimal(reader.GetString(10)),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    reader.GetInt32(12) == 1,
                    Enum.Parse<MarketDataSource>(reader.GetString(13)),
                    DateTimeOffset.Parse(reader.GetString(14)).ToUniversalTime(),
                    DateTimeOffset.Parse(reader.GetString(15)).ToUniversalTime()));
            }
        }

        foreach (var row in rows)
        {
            var instrumentId = await GetOrCreateInstrumentIdAsync(connection, transaction, row.InstrumentValue, instrumentIds, cancellationToken);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO price_bars (
                    instrument_fk, resolution, bucket_start_utc_ticks, bid_open, bid_high, bid_low, bid_close,
                    ask_open, ask_high, ask_low, ask_close, volume, is_final, source, first_seen_utc_ticks, last_seen_utc_ticks)
                VALUES (
                    $instrument_fk, $resolution, $bucket_start_utc_ticks, $bid_open, $bid_high, $bid_low, $bid_close,
                    $ask_open, $ask_high, $ask_low, $ask_close, $volume, $is_final, $source, $first_seen_utc_ticks, $last_seen_utc_ticks);
                """;
            AddLegacyPriceBarParameters(insert, row, instrumentId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CopyLegacyCoverageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, long> instrumentIds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "market_data_coverage_legacy", cancellationToken))
        {
            return;
        }

        var rows = new List<LegacyCoverageRow>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT instrument_id, resolution, from_utc, to_utc, status, checked_at_utc, message, broker_error_code
                FROM market_data_coverage_legacy;
                """;

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LegacyCoverageRow(
                    reader.GetString(0),
                    Enum.Parse<PriceResolution>(reader.GetString(1)),
                    DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime(),
                    DateTimeOffset.Parse(reader.GetString(3)).ToUniversalTime(),
                    Enum.Parse<MarketDataCoverageStatus>(reader.GetString(4)),
                    DateTimeOffset.Parse(reader.GetString(5)).ToUniversalTime(),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        foreach (var row in rows)
        {
            var instrumentId = await GetOrCreateInstrumentIdAsync(connection, transaction, row.InstrumentValue, instrumentIds, cancellationToken);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO market_data_coverage (
                    instrument_fk, resolution, from_utc_ticks, to_utc_ticks, status, checked_at_utc_ticks, message, broker_error_code)
                VALUES (
                    $instrument_fk, $resolution, $from_utc_ticks, $to_utc_ticks, $status, $checked_at_utc_ticks, $message, $broker_error_code);
                """;
            insert.Parameters.AddWithValue("$instrument_fk", instrumentId);
            insert.Parameters.AddWithValue("$resolution", (int)row.Resolution);
            insert.Parameters.AddWithValue("$from_utc_ticks", row.FromUtc.UtcTicks);
            insert.Parameters.AddWithValue("$to_utc_ticks", row.ToUtc.UtcTicks);
            insert.Parameters.AddWithValue("$status", (int)row.Status);
            insert.Parameters.AddWithValue("$checked_at_utc_ticks", row.CheckedAtUtc.UtcTicks);
            insert.Parameters.AddWithValue("$message", row.Message is null ? DBNull.Value : row.Message);
            insert.Parameters.AddWithValue("$broker_error_code", row.BrokerErrorCode is null ? DBNull.Value : row.BrokerErrorCode);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CopyLegacyHealthAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, long> instrumentIds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "market_data_health_legacy", cancellationToken))
        {
            return;
        }

        var rows = new List<LegacyHealthRow>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT instrument_id, resolution, connection_state, last_received_update_utc,
                       latest_completed_candle_utc, repair_state, unresolved_gaps_json,
                       last_historical_repair_status, last_historical_repair_message, updated_at_utc
                FROM market_data_health_legacy;
                """;

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LegacyHealthRow(
                    reader.GetString(0),
                    Enum.Parse<PriceResolution>(reader.GetString(1)),
                    Enum.Parse<MarketDataConnectionState>(reader.GetString(2)),
                    ReadNullableDateTimeOffset(reader, 3),
                    ReadNullableDateTimeOffset(reader, 4),
                    Enum.Parse<MarketDataRepairState>(reader.GetString(5)),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : Enum.Parse<MarketDataCoverageStatus>(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    DateTimeOffset.Parse(reader.GetString(9)).ToUniversalTime()));
            }
        }

        foreach (var row in rows)
        {
            var instrumentId = await GetOrCreateInstrumentIdAsync(connection, transaction, row.InstrumentValue, instrumentIds, cancellationToken);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO market_data_health (
                    instrument_fk, resolution, connection_state, last_received_update_utc_ticks,
                    latest_completed_candle_utc_ticks, repair_state, unresolved_gaps_json,
                    last_historical_repair_status, last_historical_repair_message, updated_at_utc_ticks)
                VALUES (
                    $instrument_fk, $resolution, $connection_state, $last_received_update_utc_ticks,
                    $latest_completed_candle_utc_ticks, $repair_state, $unresolved_gaps_json,
                    $last_historical_repair_status, $last_historical_repair_message, $updated_at_utc_ticks);
                """;
            insert.Parameters.AddWithValue("$instrument_fk", instrumentId);
            insert.Parameters.AddWithValue("$resolution", (int)row.Resolution);
            insert.Parameters.AddWithValue("$connection_state", (int)row.ConnectionState);
            insert.Parameters.AddWithValue("$last_received_update_utc_ticks", row.LastReceivedUpdateUtc is null ? DBNull.Value : row.LastReceivedUpdateUtc.Value.UtcTicks);
            insert.Parameters.AddWithValue("$latest_completed_candle_utc_ticks", row.LatestCompletedCandleUtc is null ? DBNull.Value : row.LatestCompletedCandleUtc.Value.UtcTicks);
            insert.Parameters.AddWithValue("$repair_state", (int)row.RepairState);
            insert.Parameters.AddWithValue("$unresolved_gaps_json", row.UnresolvedGapsJson);
            insert.Parameters.AddWithValue("$last_historical_repair_status", row.LastHistoricalRepairStatus is null ? DBNull.Value : (int)row.LastHistoricalRepairStatus.Value);
            insert.Parameters.AddWithValue("$last_historical_repair_message", row.LastHistoricalRepairMessage is null ? DBNull.Value : row.LastHistoricalRepairMessage);
            insert.Parameters.AddWithValue("$updated_at_utc_ticks", row.UpdatedAtUtc.UtcTicks);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task RenameTableIfExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string newName,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, tableName, cancellationToken))
        {
            return;
        }

        await ExecuteNonQueryAsync(connection, transaction, $"ALTER TABLE {tableName} RENAME TO {newName};", cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<long?> ExecuteNullableScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<long> GetOrCreateInstrumentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string instrumentValue,
        Dictionary<string, long> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(instrumentValue, out var cached))
        {
            return cached;
        }

        var instrumentId = await TryGetInstrumentIdAsync(connection, transaction, instrumentValue, cancellationToken);
        if (instrumentId is null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO instruments (instrument_value)
                VALUES ($instrument_value)
                ON CONFLICT(instrument_value) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$instrument_value", instrumentValue);
            await insert.ExecuteNonQueryAsync(cancellationToken);

            instrumentId = await TryGetInstrumentIdAsync(connection, transaction, instrumentValue, cancellationToken);
        }

        if (instrumentId is null)
        {
            throw new InvalidOperationException($"Failed to resolve instrument surrogate for '{instrumentValue}'.");
        }

        cache[instrumentValue] = instrumentId.Value;
        return instrumentId.Value;
    }

    private static async Task<long?> TryGetInstrumentIdAsync(
        SqliteConnection connection,
        string instrumentValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM instruments
            WHERE instrument_value = $instrument_value
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$instrument_value", instrumentValue);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<long?> TryGetInstrumentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string instrumentValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM instruments
            WHERE instrument_value = $instrument_value
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$instrument_value", instrumentValue);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static void AddPriceBarParameters(SqliteCommand command, StoredPriceBar stored, long instrumentId)
    {
        var bar = stored.Bar;
        command.Parameters.AddWithValue("$instrument_fk", instrumentId);
        command.Parameters.AddWithValue("$resolution", (int)stored.Resolution);
        command.Parameters.AddWithValue("$bucket_start_utc_ticks", bar.TimestampUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$bid_open", ToDbFixed(bar.BidOpen));
        command.Parameters.AddWithValue("$bid_high", ToDbFixed(bar.BidHigh));
        command.Parameters.AddWithValue("$bid_low", ToDbFixed(bar.BidLow));
        command.Parameters.AddWithValue("$bid_close", ToDbFixed(bar.BidClose));
        command.Parameters.AddWithValue("$ask_open", ToDbFixed(bar.AskOpen));
        command.Parameters.AddWithValue("$ask_high", ToDbFixed(bar.AskHigh));
        command.Parameters.AddWithValue("$ask_low", ToDbFixed(bar.AskLow));
        command.Parameters.AddWithValue("$ask_close", ToDbFixed(bar.AskClose));
        command.Parameters.AddWithValue("$volume", bar.Volume is null ? DBNull.Value : bar.Volume.Value);
        command.Parameters.AddWithValue("$is_final", stored.IsFinal ? 1 : 0);
        command.Parameters.AddWithValue("$source", (int)stored.Source);
        command.Parameters.AddWithValue("$first_seen_utc_ticks", stored.FirstSeenUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$last_seen_utc_ticks", stored.LastSeenUtc.ToUniversalTime().UtcTicks);
    }

    private static void AddLegacyPriceBarParameters(SqliteCommand command, LegacyPriceBarRow row, long instrumentId)
    {
        command.Parameters.AddWithValue("$instrument_fk", instrumentId);
        command.Parameters.AddWithValue("$resolution", (int)row.Resolution);
        command.Parameters.AddWithValue("$bucket_start_utc_ticks", row.BucketStartUtc.UtcTicks);
        command.Parameters.AddWithValue("$bid_open", ToDbFixed(row.BidOpen));
        command.Parameters.AddWithValue("$bid_high", ToDbFixed(row.BidHigh));
        command.Parameters.AddWithValue("$bid_low", ToDbFixed(row.BidLow));
        command.Parameters.AddWithValue("$bid_close", ToDbFixed(row.BidClose));
        command.Parameters.AddWithValue("$ask_open", ToDbFixed(row.AskOpen));
        command.Parameters.AddWithValue("$ask_high", ToDbFixed(row.AskHigh));
        command.Parameters.AddWithValue("$ask_low", ToDbFixed(row.AskLow));
        command.Parameters.AddWithValue("$ask_close", ToDbFixed(row.AskClose));
        command.Parameters.AddWithValue("$volume", row.Volume is null ? DBNull.Value : row.Volume.Value);
        command.Parameters.AddWithValue("$is_final", row.IsFinal ? 1 : 0);
        command.Parameters.AddWithValue("$source", (int)row.Source);
        command.Parameters.AddWithValue("$first_seen_utc_ticks", row.FirstSeenUtc.UtcTicks);
        command.Parameters.AddWithValue("$last_seen_utc_ticks", row.LastSeenUtc.UtcTicks);
    }

    private static StoredPriceBar ReadBar(InstrumentId instrument, PriceResolution resolution, SqliteDataReader reader)
    {
        return new StoredPriceBar(
            instrument,
            resolution,
            new PriceBar(
                FromDbTicks(reader.GetInt64(0)),
                FromDbFixed(reader.GetInt64(1)),
                FromDbFixed(reader.GetInt64(2)),
                FromDbFixed(reader.GetInt64(3)),
                FromDbFixed(reader.GetInt64(4)),
                FromDbFixed(reader.GetInt64(5)),
                FromDbFixed(reader.GetInt64(6)),
                FromDbFixed(reader.GetInt64(7)),
                FromDbFixed(reader.GetInt64(8)),
                reader.IsDBNull(9) ? null : reader.GetInt64(9)),
            reader.GetInt32(10) == 1,
            (MarketDataSource)reader.GetInt32(11),
            FromDbTicks(reader.GetInt64(12)),
            FromDbTicks(reader.GetInt64(13)));
    }

    private static decimal ParseDecimal(string value)
        => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static long ToDbFixed(decimal value)
        => checked((long)decimal.Round(value * PriceScale, 0, MidpointRounding.AwayFromZero));

    private static decimal FromDbFixed(long value)
        => value / (decimal)PriceScale;

    private static object ToDbTicksOrDbNull(DateTimeOffset? value)
        => value is null ? DBNull.Value : value.Value.ToUniversalTime().UtcTicks;

    private static DateTimeOffset FromDbTicks(long ticks)
        => new(new DateTime(ticks, DateTimeKind.Utc));

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : FromDbTicks(reader.GetInt64(ordinal));

    private static MarketDataHealthRecord ReadHealth(
        InstrumentId instrument,
        PriceResolution resolution,
        SqliteDataReader reader)
    {
        var gaps = JsonSerializer.Deserialize<MarketDataGap[]>(reader.GetString(4)) ?? [];
        return new MarketDataHealthRecord(
            instrument,
            resolution,
            (MarketDataConnectionState)reader.GetInt32(0),
            reader.IsDBNull(1) ? null : FromDbTicks(reader.GetInt64(1)),
            reader.IsDBNull(2) ? null : FromDbTicks(reader.GetInt64(2)),
            (MarketDataRepairState)reader.GetInt32(3),
            gaps,
            reader.IsDBNull(5) ? null : (MarketDataCoverageStatus)reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            FromDbTicks(reader.GetInt64(7)));
    }

    private static async Task<HashSet<DateTimeOffset>> ReadFinalBucketStartsAsync(
        SqliteConnection connection,
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
        if (instrumentId is null)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start_utc_ticks
            FROM price_bars
            WHERE instrument_fk = $instrument_fk
              AND resolution = $resolution
              AND is_final = 1
              AND bucket_start_utc_ticks >= $from_utc_ticks
              AND bucket_start_utc_ticks < $to_utc_ticks;
            """;
        command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
        command.Parameters.AddWithValue("$resolution", (int)resolution);
        command.Parameters.AddWithValue("$from_utc_ticks", fromUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$to_utc_ticks", toUtc.ToUniversalTime().UtcTicks);

        var results = new HashSet<DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(FromDbTicks(reader.GetInt64(0)));
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
        var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
        if (instrumentId is null)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT from_utc_ticks, to_utc_ticks, status, checked_at_utc_ticks, message, broker_error_code
            FROM market_data_coverage
            WHERE instrument_fk = $instrument_fk
              AND resolution = $resolution
              AND status = $status
              AND from_utc_ticks < $to_utc_ticks
              AND to_utc_ticks > $from_utc_ticks;
            """;
        command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
        command.Parameters.AddWithValue("$resolution", (int)resolution);
        command.Parameters.AddWithValue("$status", (int)MarketDataCoverageStatus.NoBars);
        command.Parameters.AddWithValue("$from_utc_ticks", fromUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$to_utc_ticks", toUtc.ToUniversalTime().UtcTicks);

        var results = new List<MarketDataCoverageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarketDataCoverageRecord(
                instrument,
                resolution,
                FromDbTicks(reader.GetInt64(0)),
                FromDbTicks(reader.GetInt64(1)),
                MarketDataCoverageStatus.NoBars,
                FromDbTicks(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<MarketDataCoverageRecord>> ReadCoverageAsync(
        SqliteConnection connection,
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var instrumentId = await TryGetInstrumentIdAsync(connection, instrument.Value, cancellationToken);
        if (instrumentId is null)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT from_utc_ticks, to_utc_ticks, status, checked_at_utc_ticks, message, broker_error_code
            FROM market_data_coverage
            WHERE instrument_fk = $instrument_fk
              AND resolution = $resolution
              AND from_utc_ticks < $to_utc_ticks
              AND to_utc_ticks > $from_utc_ticks
            ORDER BY from_utc_ticks ASC;
            """;
        command.Parameters.AddWithValue("$instrument_fk", instrumentId.Value);
        command.Parameters.AddWithValue("$resolution", (int)resolution);
        command.Parameters.AddWithValue("$from_utc_ticks", fromUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$to_utc_ticks", toUtc.ToUniversalTime().UtcTicks);

        var results = new List<MarketDataCoverageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarketDataCoverageRecord(
                instrument,
                resolution,
                FromDbTicks(reader.GetInt64(0)),
                FromDbTicks(reader.GetInt64(1)),
                (MarketDataCoverageStatus)reader.GetInt32(2),
                FromDbTicks(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    private static List<MarketDataGap> FindMissingRanges(
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

    private sealed record LegacyPriceBarRow(
        string InstrumentValue,
        PriceResolution Resolution,
        DateTimeOffset BucketStartUtc,
        decimal BidOpen,
        decimal BidHigh,
        decimal BidLow,
        decimal BidClose,
        decimal AskOpen,
        decimal AskHigh,
        decimal AskLow,
        decimal AskClose,
        long? Volume,
        bool IsFinal,
        MarketDataSource Source,
        DateTimeOffset FirstSeenUtc,
        DateTimeOffset LastSeenUtc);

    private sealed record LegacyCoverageRow(
        string InstrumentValue,
        PriceResolution Resolution,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        MarketDataCoverageStatus Status,
        DateTimeOffset CheckedAtUtc,
        string? Message,
        string? BrokerErrorCode);

    private sealed record LegacyHealthRow(
        string InstrumentValue,
        PriceResolution Resolution,
        MarketDataConnectionState ConnectionState,
        DateTimeOffset? LastReceivedUpdateUtc,
        DateTimeOffset? LatestCompletedCandleUtc,
        MarketDataRepairState RepairState,
        string UnresolvedGapsJson,
        MarketDataCoverageStatus? LastHistoricalRepairStatus,
        string? LastHistoricalRepairMessage,
        DateTimeOffset UpdatedAtUtc);
}
