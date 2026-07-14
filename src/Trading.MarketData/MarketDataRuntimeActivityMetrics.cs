namespace Trading.MarketData;

/// <summary>Bounded counters for expensive market-data maintenance work.</summary>
public sealed class MarketDataRuntimeActivityMetrics
{
    private readonly object _sync = new();
    private MarketDataRuntimeActivitySnapshot _snapshot = new(
        SnapshotStartedCount: 0,
        SnapshotCompletedCount: 0,
        SnapshotFailedCount: 0,
        LastSnapshotDuration: TimeSpan.Zero,
        RecoveryStartedCount: 0,
        RecoveryCompletedCount: 0,
        RecoveryFailedCount: 0,
        LastRecoveryDuration: TimeSpan.Zero);

    public void RecordSnapshotStarted()
    {
        lock (_sync)
        {
            _snapshot = _snapshot with { SnapshotStartedCount = _snapshot.SnapshotStartedCount + 1 };
        }
    }

    public void RecordSnapshotCompleted(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                SnapshotCompletedCount = _snapshot.SnapshotCompletedCount + 1,
                LastSnapshotDuration = duration,
            };
        }
    }

    public void RecordSnapshotFailed(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                SnapshotFailedCount = _snapshot.SnapshotFailedCount + 1,
                LastSnapshotDuration = duration,
            };
        }
    }

    public void RecordRecoveryStarted()
    {
        lock (_sync)
        {
            _snapshot = _snapshot with { RecoveryStartedCount = _snapshot.RecoveryStartedCount + 1 };
        }
    }

    public void RecordRecoveryCompleted(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                RecoveryCompletedCount = _snapshot.RecoveryCompletedCount + 1,
                LastRecoveryDuration = duration,
            };
        }
    }

    public void RecordRecoveryFailed(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                RecoveryFailedCount = _snapshot.RecoveryFailedCount + 1,
                LastRecoveryDuration = duration,
            };
        }
    }

    public MarketDataRuntimeActivitySnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }
}

public sealed record MarketDataRuntimeActivitySnapshot(
    long SnapshotStartedCount,
    long SnapshotCompletedCount,
    long SnapshotFailedCount,
    TimeSpan LastSnapshotDuration,
    long RecoveryStartedCount,
    long RecoveryCompletedCount,
    long RecoveryFailedCount,
    TimeSpan LastRecoveryDuration);
