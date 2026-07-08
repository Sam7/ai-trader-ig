using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Execution;

public sealed class SqliteExecutionBoundaryStore : IExecutionBoundaryStore
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    private readonly string _connectionString;
    private bool _initialized;

    public SqliteExecutionBoundaryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Execution database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Pooling = false,
        }.ToString();
    }

    public async Task<ExecutionOperationReservationResult> ReserveOperationAsync(
        ExecutionOperationRequest request,
        string dealReference,
        DateTimeOffset reservedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(request);
        ValidateDealReference(dealReference);

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO execution_records (
                decision_id, operation_kind, operation_source, state, source_decision_audit_id,
                source_decision_audit_path, intent_json, trading_date, instrument_value, direction,
                entry_method, size, related_deal_id, deal_reference, deal_id, broker_status, reserved_at_utc_ticks,
                updated_at_utc_ticks, submitted_at_utc_ticks, confirmed_at_utc_ticks, closed_at_utc_ticks,
                attempt_count, last_error)
            VALUES (
                $decision_id, $operation_kind, $operation_source, $state, $source_decision_audit_id,
                $source_decision_audit_path, $intent_json, $trading_date, $instrument_value, $direction,
                $entry_method, $size, $related_deal_id, $deal_reference, NULL, NULL, $reserved_at_utc_ticks,
                $updated_at_utc_ticks, NULL, NULL, NULL, 0, NULL)
            ON CONFLICT(decision_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$decision_id", request.OperationId);
        command.Parameters.AddWithValue("$operation_kind", (int)request.Kind);
        command.Parameters.AddWithValue("$operation_source", (int)request.Source);
        command.Parameters.AddWithValue("$state", (int)ExecutionBoundaryState.Reserved);
        command.Parameters.AddWithValue("$source_decision_audit_id", request.SourceDecisionAuditId ?? request.OperationId);
        command.Parameters.AddWithValue("$source_decision_audit_path", ToDbNullableText(request.SourceDecisionAuditPath));
        command.Parameters.AddWithValue("$intent_json", request.Intent is null ? "{}" : JsonSerializer.Serialize(request.Intent, ExecutionBoundaryJson.Options));
        command.Parameters.AddWithValue("$trading_date", (request.TradingDate ?? DateOnly.FromDateTime(reservedAtUtc.UtcDateTime)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$instrument_value", request.Instrument?.Value ?? string.Empty);
        command.Parameters.AddWithValue("$direction", request.Direction is null ? 0 : (int)request.Direction.Value);
        command.Parameters.AddWithValue("$entry_method", request.EntryMethod is null ? -1 : (int)request.EntryMethod.Value);
        command.Parameters.AddWithValue("$size", request.Size is null ? DBNull.Value : request.Size.Value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$related_deal_id", ToDbNullableText(request.RelatedDealId));
        command.Parameters.AddWithValue("$deal_reference", dealReference);
        command.Parameters.AddWithValue("$reserved_at_utc_ticks", reservedAtUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$updated_at_utc_ticks", reservedAtUtc.ToUniversalTime().UtcTicks);

        var created = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        var record = await ReadOperationRecordAsync(connection, (SqliteTransaction)transaction, request.OperationId, cancellationToken)
            ?? throw new InvalidOperationException($"Execution operation '{request.OperationId}' could not be read after reservation.");
        await transaction.CommitAsync(cancellationToken);
        return new ExecutionOperationReservationResult(record, created);
    }

    public async Task<ExecutionOperationRecord?> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ReadOperationRecordAsync(connection, null, operationId, cancellationToken);
    }

    public async Task<ExecutionOperationSubmissionLease?> TryBeginOperationSubmissionAsync(
        string operationId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await ReadOperationRecordAsync(connection, (SqliteTransaction)transaction, operationId, cancellationToken);
        if (existing is null
            || existing.State is not (ExecutionBoundaryState.Reserved or ExecutionBoundaryState.FailedBeforeSubmission))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var attemptNumber = existing.AttemptCount + 1;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE execution_records
                SET state = $state,
                    updated_at_utc_ticks = $updated_at_utc_ticks,
                    attempt_count = $attempt_count,
                    last_error = NULL
                WHERE decision_id = $decision_id
                  AND state IN ($reserved_state, $failed_before_submission_state);
                """;
            update.Parameters.AddWithValue("$state", (int)ExecutionBoundaryState.Submitting);
            update.Parameters.AddWithValue("$updated_at_utc_ticks", startedAtUtc.ToUniversalTime().UtcTicks);
            update.Parameters.AddWithValue("$attempt_count", attemptNumber);
            update.Parameters.AddWithValue("$decision_id", operationId);
            update.Parameters.AddWithValue("$reserved_state", (int)ExecutionBoundaryState.Reserved);
            update.Parameters.AddWithValue("$failed_before_submission_state", (int)ExecutionBoundaryState.FailedBeforeSubmission);

            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO execution_submission_attempts (
                    decision_id, attempt_number, deal_reference, starting_state, started_at_utc_ticks,
                    completed_at_utc_ticks, completed_state, broker_status, broker_deal_id, error_code, error_message)
                VALUES (
                    $decision_id, $attempt_number, $deal_reference, $starting_state, $started_at_utc_ticks,
                    NULL, NULL, NULL, NULL, NULL, NULL);
                """;
            insert.Parameters.AddWithValue("$decision_id", operationId);
            insert.Parameters.AddWithValue("$attempt_number", attemptNumber);
            insert.Parameters.AddWithValue("$deal_reference", existing.DealReference);
            insert.Parameters.AddWithValue("$starting_state", (int)existing.State);
            insert.Parameters.AddWithValue("$started_at_utc_ticks", startedAtUtc.ToUniversalTime().UtcTicks);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var record = await ReadOperationRecordAsync(connection, (SqliteTransaction)transaction, operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Execution operation '{operationId}' could not be read after submission lease.");
        await transaction.CommitAsync(cancellationToken);
        return new ExecutionOperationSubmissionLease(record, attemptNumber);
    }

    public async Task<ExecutionOperationRecord> CompleteOperationAttemptAsync(
        ExecutionOperationAttemptCompletion completion,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = (SqliteTransaction)transaction;
            updateAttempt.CommandText = """
                UPDATE execution_submission_attempts
                SET deal_reference = COALESCE($deal_reference, deal_reference),
                    completed_at_utc_ticks = $completed_at_utc_ticks,
                    completed_state = $completed_state,
                    broker_status = $broker_status,
                    broker_deal_id = $broker_deal_id,
                    error_code = $error_code,
                    error_message = $error_message
                WHERE decision_id = $decision_id
                  AND attempt_number = $attempt_number;
                """;
            updateAttempt.Parameters.AddWithValue("$deal_reference", ToDbNullableText(completion.DealReference));
            updateAttempt.Parameters.AddWithValue("$completed_at_utc_ticks", completion.CompletedAtUtc.ToUniversalTime().UtcTicks);
            updateAttempt.Parameters.AddWithValue("$completed_state", (int)completion.State);
            updateAttempt.Parameters.AddWithValue("$broker_status", ToDbNullableInt(completion.BrokerStatus));
            updateAttempt.Parameters.AddWithValue("$broker_deal_id", ToDbNullableText(completion.DealId));
            updateAttempt.Parameters.AddWithValue("$error_code", ToDbNullableText(completion.ErrorCode));
            updateAttempt.Parameters.AddWithValue("$error_message", ToDbNullableText(completion.ErrorMessage));
            updateAttempt.Parameters.AddWithValue("$decision_id", completion.OperationId);
            updateAttempt.Parameters.AddWithValue("$attempt_number", completion.AttemptNumber);
            await updateAttempt.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateRecord = connection.CreateCommand())
        {
            updateRecord.Transaction = (SqliteTransaction)transaction;
            updateRecord.CommandText = """
                UPDATE execution_records
                SET state = $state,
                    deal_reference = COALESCE($deal_reference, deal_reference),
                    updated_at_utc_ticks = $updated_at_utc_ticks,
                    submitted_at_utc_ticks = CASE
                        WHEN $submitted_at_utc_ticks IS NOT NULL THEN COALESCE(submitted_at_utc_ticks, $submitted_at_utc_ticks)
                        ELSE submitted_at_utc_ticks
                    END,
                    confirmed_at_utc_ticks = CASE
                        WHEN $confirmed_at_utc_ticks IS NOT NULL THEN COALESCE(confirmed_at_utc_ticks, $confirmed_at_utc_ticks)
                        ELSE confirmed_at_utc_ticks
                    END,
                    closed_at_utc_ticks = CASE
                        WHEN $closed_at_utc_ticks IS NOT NULL THEN COALESCE(closed_at_utc_ticks, $closed_at_utc_ticks)
                        ELSE closed_at_utc_ticks
                    END,
                    deal_id = COALESCE($deal_id, deal_id),
                    broker_status = COALESCE($broker_status, broker_status),
                    last_error = $last_error
                WHERE decision_id = $decision_id;
                """;
            updateRecord.Parameters.AddWithValue("$state", (int)completion.State);
            updateRecord.Parameters.AddWithValue("$deal_reference", ToDbNullableText(completion.DealReference));
            updateRecord.Parameters.AddWithValue("$updated_at_utc_ticks", completion.CompletedAtUtc.ToUniversalTime().UtcTicks);
            updateRecord.Parameters.AddWithValue("$submitted_at_utc_ticks", ToDbTicks(ResolveSubmittedAt(completion.State, completion.CompletedAtUtc)));
            updateRecord.Parameters.AddWithValue("$confirmed_at_utc_ticks", ToDbTicks(ResolveConfirmedAt(completion.State, completion.CompletedAtUtc)));
            updateRecord.Parameters.AddWithValue("$closed_at_utc_ticks", ToDbTicks(ResolveClosedAt(completion.State, completion.CompletedAtUtc)));
            updateRecord.Parameters.AddWithValue("$deal_id", ToDbNullableText(completion.DealId));
            updateRecord.Parameters.AddWithValue("$broker_status", ToDbNullableInt(completion.BrokerStatus));
            updateRecord.Parameters.AddWithValue("$last_error", ToDbNullableText(completion.ErrorMessage));
            updateRecord.Parameters.AddWithValue("$decision_id", completion.OperationId);

            if (await updateRecord.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException($"Execution operation '{completion.OperationId}' does not exist.");
            }
        }

        var record = await ReadOperationRecordAsync(connection, (SqliteTransaction)transaction, completion.OperationId, cancellationToken)
            ?? throw new InvalidOperationException($"Execution operation '{completion.OperationId}' could not be read after completion.");
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<ExecutionReservationResult> ReserveAsync(
        ExecutionReadyTradeIntent intent,
        string dealReference,
        DateTimeOffset reservedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateIntent(intent);
        var result = await ReserveOperationAsync(
            new ExecutionOperationRequest(
                intent.DecisionId,
                ExecutionOperationKind.MarketOpen,
                ExecutionOperationSource.AutomatedDecision,
                intent.SourceDecisionAuditId,
                Intent: intent,
                TradingDate: intent.TradingDate,
                Instrument: intent.Instrument,
                Direction: intent.Direction,
                EntryMethod: intent.EntryMethod),
            dealReference,
            reservedAtUtc,
            cancellationToken);
        return new ExecutionReservationResult(ToBoundaryRecord(result.Record), result.Created);
    }

    public async Task<ExecutionBoundaryRecord?> GetAsync(
        string decisionId,
        CancellationToken cancellationToken = default)
    {
        var operation = await GetOperationAsync(decisionId, cancellationToken);
        return operation is null ? null : ToBoundaryRecord(operation);
    }

    public async Task<ExecutionBoundaryRecord?> AttachDecisionAuditArtifactAsync(
        string decisionId,
        string decisionAuditPath,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionAuditPath))
        {
            throw new ArgumentException("Decision audit path is required.", nameof(decisionAuditPath));
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE execution_records
            SET source_decision_audit_path = $source_decision_audit_path,
                updated_at_utc_ticks = $updated_at_utc_ticks
            WHERE decision_id = $decision_id;
            """;
        command.Parameters.AddWithValue("$source_decision_audit_path", Path.GetFullPath(decisionAuditPath));
        command.Parameters.AddWithValue("$updated_at_utc_ticks", updatedAtUtc.ToUniversalTime().UtcTicks);
        command.Parameters.AddWithValue("$decision_id", decisionId);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        var record = updated
            ? await ReadOperationRecordAsync(connection, (SqliteTransaction)transaction, decisionId, cancellationToken)
            : null;
        await transaction.CommitAsync(cancellationToken);
        return record is null ? null : ToBoundaryRecord(record);
    }

    public async Task<ExecutionSubmissionLease?> TryBeginSubmissionAsync(
        string decisionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var lease = await TryBeginOperationSubmissionAsync(decisionId, startedAtUtc, cancellationToken);
        return lease is null ? null : new ExecutionSubmissionLease(ToBoundaryRecord(lease.Record), lease.AttemptNumber);
    }

    public async Task<ExecutionBoundaryRecord> CompleteAttemptAsync(
        ExecutionAttemptCompletion completion,
        CancellationToken cancellationToken = default)
    {
        var operation = await CompleteOperationAttemptAsync(
            new ExecutionOperationAttemptCompletion(
                completion.DecisionId,
                completion.AttemptNumber,
                completion.State,
                completion.CompletedAtUtc,
                completion.DealReference,
                completion.DealId,
                completion.BrokerStatus,
                completion.ErrorCode,
                completion.ErrorMessage),
            cancellationToken);
        return ToBoundaryRecord(operation);
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
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS execution_records (
                    decision_id TEXT NOT NULL PRIMARY KEY,
                    operation_kind INTEGER NOT NULL DEFAULT 1,
                    operation_source INTEGER NOT NULL DEFAULT 1,
                    state INTEGER NOT NULL,
                    source_decision_audit_id TEXT NOT NULL,
                    source_decision_audit_path TEXT NULL,
                    intent_json TEXT NOT NULL,
                    trading_date TEXT NOT NULL,
                    instrument_value TEXT NOT NULL,
                    direction INTEGER NOT NULL,
                    entry_method INTEGER NOT NULL,
                    size TEXT NULL,
                    related_deal_id TEXT NULL,
                    deal_reference TEXT NOT NULL UNIQUE,
                    deal_id TEXT NULL,
                    broker_status INTEGER NULL,
                    reserved_at_utc_ticks INTEGER NOT NULL,
                    updated_at_utc_ticks INTEGER NOT NULL,
                    submitted_at_utc_ticks INTEGER NULL,
                    confirmed_at_utc_ticks INTEGER NULL,
                    closed_at_utc_ticks INTEGER NULL,
                    attempt_count INTEGER NOT NULL,
                    last_error TEXT NULL
                ) WITHOUT ROWID;

                CREATE TABLE IF NOT EXISTS execution_submission_attempts (
                    decision_id TEXT NOT NULL REFERENCES execution_records(decision_id),
                    attempt_number INTEGER NOT NULL,
                    deal_reference TEXT NOT NULL,
                    starting_state INTEGER NOT NULL,
                    started_at_utc_ticks INTEGER NOT NULL,
                    completed_at_utc_ticks INTEGER NULL,
                    completed_state INTEGER NULL,
                    broker_status INTEGER NULL,
                    broker_deal_id TEXT NULL,
                    error_code TEXT NULL,
                    error_message TEXT NULL,
                    PRIMARY KEY (decision_id, attempt_number)
                ) WITHOUT ROWID;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await EnsureColumnAsync(connection, "execution_records", "operation_kind", "operation_kind INTEGER NOT NULL DEFAULT 1", cancellationToken);
            await EnsureColumnAsync(connection, "execution_records", "operation_source", "operation_source INTEGER NOT NULL DEFAULT 1", cancellationToken);
            await EnsureColumnAsync(connection, "execution_records", "size", "size TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "execution_records", "related_deal_id", "related_deal_id TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, "execution_records", "broker_status", "broker_status INTEGER NULL", cancellationToken);

            _initialized = true;
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await info.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnDefinition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ExecutionOperationRecord?> ReadOperationRecordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string operationId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateReadOperationCommand(connection, transaction);
        command.CommandText += " WHERE decision_id = $value LIMIT 1;";
        command.Parameters.AddWithValue("$value", operationId);
        return await ReadSingleOperationRecordAsync(command, cancellationToken);
    }

    private static SqliteCommand CreateReadOperationCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT decision_id, operation_kind, operation_source, state, source_decision_audit_id,
                   source_decision_audit_path, intent_json, trading_date, instrument_value, direction,
                   entry_method, size, related_deal_id, deal_reference, deal_id, broker_status,
                   reserved_at_utc_ticks, updated_at_utc_ticks, submitted_at_utc_ticks, confirmed_at_utc_ticks,
                   closed_at_utc_ticks, attempt_count, last_error
            FROM execution_records
            """;
        return command;
    }

    private static async Task<ExecutionOperationRecord?> ReadSingleOperationRecordAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var intentJson = reader.GetString(6);
        var intent = string.IsNullOrWhiteSpace(intentJson) || intentJson == "{}"
            ? null
            : JsonSerializer.Deserialize<ExecutionReadyTradeIntent>(intentJson, ExecutionBoundaryJson.Options);

        return new ExecutionOperationRecord(
            reader.GetString(0),
            (ExecutionOperationKind)reader.GetInt32(1),
            (ExecutionOperationSource)reader.GetInt32(2),
            (ExecutionBoundaryState)reader.GetInt32(3),
            ReadNullableText(reader, 4),
            ReadNullableText(reader, 5),
            intent,
            ReadNullableDateOnly(reader, 7),
            ReadNullableInstrument(reader, 8),
            ReadNullableDirection(reader, 9),
            ReadNullableEntryMethod(reader, 10),
            ReadNullableDecimal(reader, 11),
            ReadNullableText(reader, 12),
            reader.GetString(13),
            ReadNullableText(reader, 14),
            ReadNullableOrderStatus(reader, 15),
            FromDbTicks(reader.GetInt64(16)),
            FromDbTicks(reader.GetInt64(17)),
            ReadNullableDateTimeOffset(reader, 18),
            ReadNullableDateTimeOffset(reader, 19),
            ReadNullableDateTimeOffset(reader, 20),
            reader.GetInt32(21),
            ReadNullableText(reader, 22));
    }

    private static ExecutionBoundaryRecord ToBoundaryRecord(ExecutionOperationRecord operation)
    {
        if (operation.Intent is null)
        {
            throw new InvalidOperationException($"Execution operation '{operation.OperationId}' does not contain an automated execution intent.");
        }

        return new ExecutionBoundaryRecord(
            operation.OperationId,
            operation.State,
            operation.SourceDecisionAuditId ?? operation.OperationId,
            operation.SourceDecisionAuditPath,
            operation.Intent,
            operation.TradingDate ?? operation.Intent.TradingDate,
            operation.Instrument ?? operation.Intent.Instrument,
            operation.Direction ?? operation.Intent.Direction,
            operation.EntryMethod ?? operation.Intent.EntryMethod,
            operation.DealReference,
            operation.DealId,
            operation.ReservedAtUtc,
            operation.UpdatedAtUtc,
            operation.SubmittedAtUtc,
            operation.ConfirmedAtUtc,
            operation.ClosedAtUtc,
            operation.AttemptCount,
            operation.LastError);
    }

    private static void ValidateOperationRequest(ExecutionOperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            throw new ArgumentException("Operation ID is required.", nameof(request));
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported execution operation kind.");
        }

        if (!Enum.IsDefined(request.Source))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Source, "Unsupported execution operation source.");
        }
    }

    private static void ValidateIntent(ExecutionReadyTradeIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.DecisionId))
        {
            throw new ArgumentException("Intent decision ID is required.", nameof(intent));
        }

        if (string.IsNullOrWhiteSpace(intent.SourceDecisionAuditId))
        {
            throw new ArgumentException("Intent source decision audit ID is required.", nameof(intent));
        }
    }

    private static void ValidateDealReference(string dealReference)
    {
        if (string.IsNullOrWhiteSpace(dealReference)
            || dealReference.Length > 30
            || dealReference.Any(character => !char.IsAsciiLetterOrDigit(character))
            || dealReference.Any(char.IsLower))
        {
            throw new ArgumentException("Deal reference must contain 1-30 uppercase ASCII letters or digits.", nameof(dealReference));
        }
    }

    private static DateTimeOffset? ResolveSubmittedAt(ExecutionBoundaryState state, DateTimeOffset completedAtUtc)
        => state is ExecutionBoundaryState.Submitted
            or ExecutionBoundaryState.Confirmed
            or ExecutionBoundaryState.BrokerRejected
            or ExecutionBoundaryState.OutcomeUncertain
            or ExecutionBoundaryState.Closed
            ? completedAtUtc
            : null;

    private static DateTimeOffset? ResolveConfirmedAt(ExecutionBoundaryState state, DateTimeOffset completedAtUtc)
        => state == ExecutionBoundaryState.Confirmed ? completedAtUtc : null;

    private static DateTimeOffset? ResolveClosedAt(ExecutionBoundaryState state, DateTimeOffset completedAtUtc)
        => state == ExecutionBoundaryState.Closed ? completedAtUtc : null;

    private static object ToDbNullableInt(OrderStatus? value)
        => value is null ? DBNull.Value : (int)value.Value;

    private static object ToDbNullableText(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object ToDbTicks(DateTimeOffset? value)
        => value is null ? DBNull.Value : value.Value.ToUniversalTime().UtcTicks;

    private static DateTimeOffset FromDbTicks(long ticks)
        => new(new DateTime(ticks, DateTimeKind.Utc));

    private static string? ReadNullableText(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : string.IsNullOrWhiteSpace(reader.GetString(ordinal)) ? null : reader.GetString(ordinal);

    private static DateOnly? ReadNullableDateOnly(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) || string.IsNullOrWhiteSpace(reader.GetString(ordinal))
            ? null
            : DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static InstrumentId? ReadNullableInstrument(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) || string.IsNullOrWhiteSpace(reader.GetString(ordinal))
            ? null
            : new InstrumentId(reader.GetString(ordinal));

    private static TradeDirection? ReadNullableDirection(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetInt32(ordinal);
        return value == 0 ? null : (TradeDirection)value;
    }

    private static TradeEntryMethod? ReadNullableEntryMethod(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetInt32(ordinal);
        return value < 0 ? null : (TradeEntryMethod)value;
    }

    private static OrderStatus? ReadNullableOrderStatus(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : (OrderStatus)reader.GetInt32(ordinal);

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : FromDbTicks(reader.GetInt64(ordinal));
}
