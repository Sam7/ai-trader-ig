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
    int Gen2Collections);

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
    long? OomKillEvents);

internal sealed record WorkerDiagnosticsSentrySample(
    DateTimeOffset ObservedAtUtc,
    long WorkingSetBytes,
    long? CgroupCurrentBytes,
    long? HighEvents,
    long? MaxEvents,
    long? OomEvents,
    long? OomKillEvents);

internal sealed record WorkerDiagnosticSnapshot(
    DateTimeOffset ObservedAtUtc,
    long Sequence,
    WorkerProcessMemorySnapshot Process,
    CgroupMemorySnapshot? Cgroup,
    MarketDataStreamPipelineSnapshot? Stream,
    WorkerOperationMetricsSnapshot? Operations,
    MarketDataRuntimeActivitySnapshot? Activity);
