namespace Trading.MarketData;

/// <summary>Reads SQLite's process-wide allocator counters without opening a database connection.</summary>
public sealed class SqliteNativeRuntimeMetricsReader
{
    private static readonly Lazy<bool> IsInitialized = new(Initialize);

    public SqliteNativeRuntimeMetrics? TryRead()
    {
        if (!IsInitialized.Value)
        {
            return null;
        }

        return new SqliteNativeRuntimeMetrics(
            TryReadStatus(SQLitePCL.raw.SQLITE_STATUS_MEMORY_USED),
            TryReadStatus(SQLitePCL.raw.SQLITE_STATUS_PAGECACHE_USED),
            TryReadStatus(SQLitePCL.raw.SQLITE_STATUS_PAGECACHE_OVERFLOW),
            TryReadStatus(SQLitePCL.raw.SQLITE_STATUS_MALLOC_COUNT));
    }

    private static bool Initialize()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static SqliteNativeStatus? TryReadStatus(int operation)
    {
        try
        {
            return SQLitePCL.raw.sqlite3_status(operation, out var current, out var highWater, resetFlag: 0) == 0
                ? new SqliteNativeStatus(current, highWater)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed record SqliteNativeRuntimeMetrics(
    SqliteNativeStatus? Allocator,
    SqliteNativeStatus? PageCache,
    SqliteNativeStatus? PageCacheOverflow,
    SqliteNativeStatus? MallocCount);

public sealed record SqliteNativeStatus(long Current, long HighWater);
