using Trading.MarketData;

namespace Trading.Automation.Health;

public sealed record WorkerHealthSnapshot(
    DateTimeOffset ObservedAtUtc,
    string MachineName,
    string EnvironmentName,
    WorkerHealthStatus Status,
    IReadOnlyList<string> Reasons,
    ProcessHealthSnapshot Process,
    GcHealthSnapshot Gc,
    MarketDataStreamPipelineSnapshot StreamPipeline,
    MarketDataHealthSummary MarketData)
{
    public WorkerOperationMetricsSnapshot Operations { get; init; } = new(
        null,
        0,
        0,
        0,
        TimeSpan.Zero,
        0,
        0);
}

public enum WorkerHealthStatus
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
}

public sealed record ProcessHealthSnapshot(
    int ProcessId,
    TimeSpan Uptime,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int ThreadCount,
    int HandleCount);

public sealed record GcHealthSnapshot(
    long TotalManagedMemoryBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    long TotalCommittedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

public sealed record MarketDataHealthSummary(
    DateTimeOffset? LatestFinalBarUtc,
    DateTimeOffset? LastStreamUpdateUtc,
    DateTimeOffset? LastPersistedUpdateUtc,
    IReadOnlyList<MarketDataInstrumentHealth> Instruments,
    MarketDataRecoveryHealth? Recovery = null);

public sealed record MarketDataRecoveryHealth(
    int PendingRanges,
    int BlockedRanges,
    int? RemainingAllowance,
    DateTimeOffset? AllowanceExpiresAtUtc,
    string? ActiveInstrument)
{
    public bool AllowanceExpiryEstimated { get; init; }
}

public sealed record MarketDataInstrumentHealth(
    string Instrument,
    DateTimeOffset? LatestFinalBarUtc,
    DateTimeOffset? LastReceivedUpdateUtc,
    string? ConnectionState,
    string? RepairState);
