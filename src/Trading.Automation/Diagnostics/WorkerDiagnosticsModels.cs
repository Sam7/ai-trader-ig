using Trading.Automation.Health;
using Trading.MarketData;

namespace Trading.Automation.Diagnostics;

internal sealed record WorkerProcessMemorySnapshot(
    int ProcessId,
    TimeSpan Uptime,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int ThreadCount,
    int HandleCount,
    long TotalManagedMemoryBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    long TotalCommittedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public LinuxProcessMemorySnapshot? Linux { get; init; }

    public ManagedRuntimeSnapshot? ManagedRuntime { get; init; }
}

internal sealed record GcGenerationSnapshot(long SizeAfterBytes, long FragmentedBytes);

internal sealed record ThreadPoolRuntimeSnapshot(
    int ThreadCount,
    long PendingWorkItemCount,
    long CompletedWorkItemCount);

internal sealed record ManagedRuntimeSnapshot(
    long TotalAllocatedBytes,
    double? AllocationRateBytesPerSecond,
    long LiveManagedBytes,
    long HeapSizeBytes,
    long TotalCommittedBytes,
    long FragmentedBytes,
    GcGenerationSnapshot Gen0,
    GcGenerationSnapshot Gen1,
    GcGenerationSnapshot Gen2,
    GcGenerationSnapshot LargeObjectHeap,
    GcGenerationSnapshot PinnedObjectHeap,
    long PinnedObjectCount,
    long FinalizationPendingCount,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long MemoryLoadBytes,
    long HighMemoryLoadThresholdBytes,
    long TotalAvailableMemoryBytes,
    double PauseTimePercentage,
    ThreadPoolRuntimeSnapshot ThreadPool);

internal sealed record SqliteRuntimeMetricsSnapshot(
    long? DatabaseBytes,
    long? WalBytes,
    long? SharedMemoryBytes,
    long? AllocatorCurrentBytes,
    long? AllocatorHighWaterBytes,
    long? PageCacheCurrentBytes,
    long? PageCacheHighWaterBytes,
    long? MallocCount,
    long? MallocCountHighWater,
    bool ConnectionPoolingEnabled,
    int? ActiveConnectionCount);

internal sealed record CgroupMemorySnapshot(
    long CurrentBytes,
    long? PeakBytes,
    long? AnonymousBytes,
    long? FileBytes,
    long? KernelStackBytes,
    long? SlabBytes,
    long? HighEvents,
    long? MaxEvents,
    long? OomEvents,
    long? OomKillEvents)
{
    public long? SwapCurrentBytes { get; init; }

    public long? SwapPeakBytes { get; init; }

    public IReadOnlyDictionary<string, long>? MemoryStat { get; init; }

    public IReadOnlyDictionary<string, long>? MemoryEvents { get; init; }

    public LinuxMemoryPressureSnapshot? MemoryPressure { get; init; }
}

internal sealed record WorkerDiagnosticsSentrySample(
    DateTimeOffset ObservedAtUtc,
    long WorkingSetBytes,
    long? CgroupCurrentBytes,
    long? HighEvents,
    long? MaxEvents,
    long? OomEvents,
    long? OomKillEvents)
{
    public WorkerHostPressureSnapshot? HostPressure { get; init; }
}

internal sealed record WorkerDiagnosticSnapshot(
    DateTimeOffset ObservedAtUtc,
    long Sequence,
    WorkerProcessMemorySnapshot Process,
    CgroupMemorySnapshot? Cgroup,
    MarketDataStreamPipelineSnapshot? Stream,
    WorkerOperationMetricsSnapshot? Operations,
    MarketDataRuntimeActivitySnapshot? Activity)
{
    /// <summary>Version two adds host-wide attribution while retaining all version-one fields.</summary>
    public int SchemaVersion { get; init; } = 2;

    public LinuxHostMemorySnapshot? Host { get; init; }

    public SqliteRuntimeMetricsSnapshot? Sqlite { get; init; }
}
