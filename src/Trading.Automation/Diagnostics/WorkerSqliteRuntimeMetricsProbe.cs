using Trading.MarketData;

namespace Trading.Automation.Diagnostics;

internal interface IWorkerSqliteRuntimeMetricsProbe
{
    SqliteRuntimeMetricsSnapshot? TryRead();
}

/// <summary>Reads SQLite process metrics and sidecar sizes without creating a connection or retaining database contents.</summary>
internal sealed class WorkerSqliteRuntimeMetricsProbe : IWorkerSqliteRuntimeMetricsProbe
{
    private readonly string _databasePath;
    private readonly SqliteNativeRuntimeMetricsReader _nativeReader = new();

    public WorkerSqliteRuntimeMetricsProbe(MarketDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _databasePath = Path.GetFullPath(options.StorePath);
    }

    public SqliteRuntimeMetricsSnapshot? TryRead()
    {
        var native = _nativeReader.TryRead();
        return new SqliteRuntimeMetricsSnapshot(
            TryGetLength(_databasePath),
            TryGetLength(_databasePath + "-wal"),
            TryGetLength(_databasePath + "-shm"),
            native?.Allocator?.Current,
            native?.Allocator?.HighWater,
            native?.PageCache?.Current,
            native?.PageCache?.HighWater,
            native?.MallocCount?.Current,
            native?.MallocCount?.HighWater,
            ConnectionPoolingEnabled: false,
            ActiveConnectionCount: null);
    }

    private static long? TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal sealed class NoOpWorkerSqliteRuntimeMetricsProbe : IWorkerSqliteRuntimeMetricsProbe
{
    public SqliteRuntimeMetricsSnapshot? TryRead() => null;
}
