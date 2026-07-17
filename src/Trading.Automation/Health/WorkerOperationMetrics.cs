using System.Diagnostics;
using Trading.Automation.Diagnostics;

namespace Trading.Automation.Health;

/// <summary>Tracks a bounded set of named worker operations without retaining request or market payloads.</summary>
public sealed class WorkerOperationMetrics
{
    private const int MaximumRecentCheckpoints = 32;
    private readonly object _sync = new();
    private readonly IWorkerOperationMemoryProbe _memory;
    private readonly Dictionary<string, ActiveOperation> _active = new(StringComparer.Ordinal);
    private readonly Queue<WorkerOperationCheckpointSnapshot> _checkpoints = new();
    private WorkerOperationMetricsSnapshot _snapshot = new(
        null,
        0,
        0,
        0,
        TimeSpan.Zero,
        0,
        0);

    public WorkerOperationMetrics()
        : this(new RuntimeWorkerOperationMemoryProbe())
    {
    }

    internal WorkerOperationMetrics(IWorkerOperationMemoryProbe memory)
    {
        _memory = memory;
    }

    /// <summary>Begins an operation marker. Call <see cref="WorkerOperationScope.Complete"/> or <see cref="WorkerOperationScope.Fail"/> exactly once.</summary>
    public WorkerOperationScope Begin(string operation, int itemCount, string? correlationId = null)
    {
        Validate(operation, itemCount, payloadBytes: 0, TimeSpan.Zero, workingSetBytes: 0);
        var id = SanitizeCorrelationId(correlationId ?? Guid.NewGuid().ToString("N"));
        var startedAtUtc = DateTimeOffset.UtcNow;
        var before = _memory.Capture();
        lock (_sync)
        {
            if (_active.ContainsKey(id))
            {
                throw new InvalidOperationException($"Worker operation correlation ID '{id}' is already active.");
            }

            _active[id] = new ActiveOperation(operation, id, itemCount, startedAtUtc, before);
            AddCheckpoint(new WorkerOperationCheckpointSnapshot(
                operation,
                id,
                WorkerOperationOutcome.Started,
                itemCount,
                0,
                startedAtUtc,
                null,
                TimeSpan.Zero,
                before,
                null));
            RefreshSnapshot();
        }

        return new WorkerOperationScope(this, id);
    }

    /// <summary>Compatibility counter for callers that have only a completed operation observation.</summary>
    public void Record(
        string operation,
        int itemCount,
        long payloadBytes,
        TimeSpan duration,
        long workingSetBytes)
    {
        Validate(operation, itemCount, payloadBytes, duration, workingSetBytes);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                LastOperation = operation,
                LastItemCount = itemCount,
                LastPayloadBytes = payloadBytes,
                MaxPayloadBytes = Math.Max(_snapshot.MaxPayloadBytes, payloadBytes),
                LastDuration = duration,
                LastWorkingSetBytes = workingSetBytes,
                OperationCount = _snapshot.OperationCount + 1,
            };
        }
    }

    public WorkerOperationMetricsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    private void Complete(string correlationId, long payloadBytes)
        => End(correlationId, WorkerOperationOutcome.Completed, payloadBytes);

    private void Fail(string correlationId)
        => End(correlationId, WorkerOperationOutcome.Failed, payloadBytes: 0);

    private void End(string correlationId, WorkerOperationOutcome outcome, long payloadBytes)
    {
        if (payloadBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var after = _memory.Capture();
        lock (_sync)
        {
            if (!_active.Remove(correlationId, out var active))
            {
                return;
            }

            var duration = completedAtUtc - active.StartedAtUtc;
            AddCheckpoint(new WorkerOperationCheckpointSnapshot(
                active.Operation,
                correlationId,
                outcome,
                active.ItemCount,
                payloadBytes,
                active.StartedAtUtc,
                completedAtUtc,
                duration,
                active.BeforeMemory,
                after));
            _snapshot = _snapshot with
            {
                LastOperation = active.Operation,
                LastItemCount = active.ItemCount,
                LastPayloadBytes = payloadBytes,
                MaxPayloadBytes = Math.Max(_snapshot.MaxPayloadBytes, payloadBytes),
                LastDuration = duration,
                LastWorkingSetBytes = after.WorkingSetBytes,
                OperationCount = _snapshot.OperationCount + 1,
                FailedOperationCount = _snapshot.FailedOperationCount + (outcome == WorkerOperationOutcome.Failed ? 1 : 0),
            };
            RefreshSnapshot();
        }
    }

    private void AddCheckpoint(WorkerOperationCheckpointSnapshot checkpoint)
    {
        _checkpoints.Enqueue(checkpoint);
        while (_checkpoints.Count > MaximumRecentCheckpoints)
        {
            _checkpoints.Dequeue();
        }
    }

    private void RefreshSnapshot()
        => _snapshot = _snapshot with
        {
            ActiveOperations = _active.Values
                .OrderBy(operation => operation.StartedAtUtc)
                .ThenBy(operation => operation.CorrelationId, StringComparer.Ordinal)
                .Select(operation => new WorkerActiveOperationSnapshot(
                    operation.Operation,
                    operation.CorrelationId,
                    operation.ItemCount,
                    operation.StartedAtUtc,
                    operation.BeforeMemory))
                .ToArray(),
            RecentCheckpoints = _checkpoints.ToArray(),
        };

    private static void Validate(string operation, int itemCount, long payloadBytes, TimeSpan duration, long workingSetBytes)
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
    }

    private static string SanitizeCorrelationId(string value)
    {
        var sanitized = string.Concat(value.Take(64).Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        return string.IsNullOrWhiteSpace(sanitized)
            ? Guid.NewGuid().ToString("N")
            : sanitized;
    }

    private sealed record ActiveOperation(
        string Operation,
        string CorrelationId,
        int ItemCount,
        DateTimeOffset StartedAtUtc,
        WorkerOperationMemorySnapshot BeforeMemory);

    public sealed class WorkerOperationScope
    {
        private readonly WorkerOperationMetrics _owner;
        private readonly string _correlationId;
        private int _ended;

        internal WorkerOperationScope(WorkerOperationMetrics owner, string correlationId)
        {
            _owner = owner;
            _correlationId = correlationId;
        }

        public void Complete(long payloadBytes = 0)
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _owner.Complete(_correlationId, payloadBytes);
            }
        }

        public void Fail()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _owner.Fail(_correlationId);
            }
        }
    }
}

public enum WorkerOperationOutcome
{
    Started,
    Completed,
    Failed,
}

public sealed record WorkerOperationMemorySnapshot(
    long ManagedAllocatedBytes,
    long ManagedCommittedBytes,
    long WorkingSetBytes,
    long? PssBytes,
    long? CgroupCurrentBytes);

public sealed record WorkerActiveOperationSnapshot(
    string Operation,
    string CorrelationId,
    int ItemCount,
    DateTimeOffset StartedAtUtc,
    WorkerOperationMemorySnapshot BeforeMemory);

public sealed record WorkerOperationCheckpointSnapshot(
    string Operation,
    string CorrelationId,
    WorkerOperationOutcome Outcome,
    int ItemCount,
    long PayloadBytes,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TimeSpan Duration,
    WorkerOperationMemorySnapshot BeforeMemory,
    WorkerOperationMemorySnapshot? AfterMemory);

public sealed record WorkerOperationMetricsSnapshot(
    string? LastOperation,
    int LastItemCount,
    long LastPayloadBytes,
    long MaxPayloadBytes,
    TimeSpan LastDuration,
    long LastWorkingSetBytes,
    long OperationCount)
{
    public IReadOnlyList<WorkerActiveOperationSnapshot> ActiveOperations { get; init; } = [];

    public IReadOnlyList<WorkerOperationCheckpointSnapshot> RecentCheckpoints { get; init; } = [];

    public long FailedOperationCount { get; init; }
}

internal interface IWorkerOperationMemoryProbe
{
    WorkerOperationMemorySnapshot Capture();
}

internal sealed class RuntimeWorkerOperationMemoryProbe : IWorkerOperationMemoryProbe
{
    private readonly ILinuxProcessMemoryReader _processMemory = new LinuxProcessMemoryReader();
    private readonly IWorkerCgroupMemoryReader _cgroupMemory = new LinuxCgroupMemoryReader();

    public WorkerOperationMemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        return new WorkerOperationMemorySnapshot(
            GC.GetTotalAllocatedBytes(precise: false),
            gc.TotalCommittedBytes,
            process.WorkingSet64,
            _processMemory.TryRead(process.Id)?.PssBytes,
            _cgroupMemory.TryRead()?.CurrentBytes);
    }
}
