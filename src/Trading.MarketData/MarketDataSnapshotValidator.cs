using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;

namespace Trading.MarketData;

public sealed class MarketDataSnapshotValidator
{
    private static readonly string[] RequiredPriceBarColumns =
    [
        "instrument_fk",
        "resolution",
        "bucket_start_utc_ticks",
        "bid_open",
        "bid_high",
        "bid_low",
        "bid_close",
        "ask_open",
        "ask_high",
        "ask_low",
        "ask_close",
        "is_final",
        "source",
        "first_seen_utc_ticks",
        "last_seen_utc_ticks",
    ];

    public async Task<MarketDataSnapshotValidationResult> ValidateAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(snapshotPath);
        if (!File.Exists(fullPath))
        {
            throw new MarketDataSnapshotValidationException($"Snapshot file was not found: {fullPath}");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length == 0)
        {
            throw new MarketDataSnapshotValidationException($"Snapshot file is empty: {fullPath}");
        }

        SQLitePCL.Batteries_V2.Init();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var quickCheck = await ExecuteScalarStringAsync(connection, "PRAGMA quick_check;", cancellationToken);
            if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new MarketDataSnapshotValidationException($"Snapshot quick_check failed: {quickCheck}");
            }

            await RequireTableAsync(connection, "instruments", cancellationToken);
            await RequireTableAsync(connection, "price_bars", cancellationToken);
            await RequireColumnAsync(connection, "instruments", "id", cancellationToken);
            await RequireColumnAsync(connection, "instruments", "instrument_value", cancellationToken);

            foreach (var column in RequiredPriceBarColumns)
            {
                await RequireColumnAsync(connection, "price_bars", column, cancellationToken);
            }

            var foreignKeyErrors = await ExecuteScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;", cancellationToken);
            if (foreignKeyErrors > 0)
            {
                throw new MarketDataSnapshotValidationException($"Snapshot foreign_key_check found {foreignKeyErrors} error(s).");
            }

            var barCount = checked((int)await ExecuteScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM price_bars WHERE is_final = 1;",
                cancellationToken));
            var latestTicks = await ExecuteNullableScalarLongAsync(
                connection,
                "SELECT MAX(bucket_start_utc_ticks) FROM price_bars WHERE is_final = 1;",
                cancellationToken);

            return new MarketDataSnapshotValidationResult(
                fullPath,
                await ComputeSha256Async(fullPath, cancellationToken),
                fileInfo.Length,
                barCount,
                latestTicks is null ? null : FromDbTicks(latestTicks.Value));
        }
        catch (SqliteException exception)
        {
            throw new MarketDataSnapshotValidationException($"Snapshot is not a readable SQLite database: {fullPath}", exception);
        }
    }

    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task RequireTableAsync(
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
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new MarketDataSnapshotValidationException($"Snapshot is missing required table '{tableName}'.");
        }
    }

    private static async Task RequireColumnAsync(
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
                return;
            }
        }

        throw new MarketDataSnapshotValidationException($"Snapshot table '{tableName}' is missing required column '{columnName}'.");
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<long?> ExecuteNullableScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset FromDbTicks(long ticks)
        => new(new DateTime(ticks, DateTimeKind.Utc));
}
