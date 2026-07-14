namespace Trading.Automation.Health;

public sealed class WorkerOperationMetrics
{
    private readonly object _sync = new();
    private WorkerOperationMetricsSnapshot _snapshot = new(
        null,
        0,
        0,
        0,
        TimeSpan.Zero,
        0,
        0);

    public void Record(
        string operation,
        int itemCount,
        long payloadBytes,
        TimeSpan duration,
        long workingSetBytes)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("Operation name is required.", nameof(operation));
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        if (payloadBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (workingSetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingSetBytes));
        }

        lock (_sync)
        {
            _snapshot = new WorkerOperationMetricsSnapshot(
                operation,
                itemCount,
                payloadBytes,
                Math.Max(_snapshot.MaxPayloadBytes, payloadBytes),
                duration,
                workingSetBytes,
                _snapshot.OperationCount + 1);
        }
    }

    public WorkerOperationMetricsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }
}

public sealed record WorkerOperationMetricsSnapshot(
    string? LastOperation,
    int LastItemCount,
    long LastPayloadBytes,
    long MaxPayloadBytes,
    TimeSpan LastDuration,
    long LastWorkingSetBytes,
    long OperationCount);
