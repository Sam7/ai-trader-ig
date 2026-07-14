using System.Diagnostics;
using Trading.Automation.Health;
using Trading.MarketData;

namespace Trading.Automation.Diagnostics;

internal interface IWorkerProcessMemoryProbe
{
    WorkerProcessMemorySnapshot Capture(DateTimeOffset observedAtUtc);
}

internal interface IWorkerCgroupMemoryReader
{
    CgroupMemorySnapshot? TryRead();
}

internal interface IWorkerDiagnosticsSampler
{
    WorkerDiagnosticsSentrySample CaptureSentry(DateTimeOffset observedAtUtc);

    WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc);
}

internal interface IWorkerProcessTerminator
{
    void Exit(int exitCode);
}

/// <summary>Captures process and managed-memory counters without retaining application payloads.</summary>
internal sealed class CurrentProcessMemoryProbe : IWorkerProcessMemoryProbe
{
    public WorkerProcessMemorySnapshot Capture(DateTimeOffset observedAtUtc)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        return new WorkerProcessMemorySnapshot(
            process.Id,
            GetUptime(process, observedAtUtc),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            SafeThreadCount(process),
            SafeHandleCount(process),
            GC.GetTotalMemory(forceFullCollection: false),
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            gcInfo.TotalCommittedBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private static TimeSpan GetUptime(Process process, DateTimeOffset observedAtUtc)
    {
        try
        {
            return observedAtUtc - process.StartTime.ToUniversalTime();
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.Zero;
        }
    }

    private static int SafeThreadCount(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static int SafeHandleCount(Process process)
    {
        try
        {
            return process.HandleCount;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}

/// <summary>Combines small runtime counters into an explicit, serializable diagnostic sample.</summary>
internal sealed class WorkerDiagnosticsSampler : IWorkerDiagnosticsSampler
{
    private readonly IWorkerProcessMemoryProbe _processMemory;
    private readonly IWorkerCgroupMemoryReader _cgroupMemory;
    private readonly MarketDataStreamPipelineMetrics _streamMetrics;
    private readonly WorkerOperationMetrics _operationMetrics;
    private readonly MarketDataRuntimeActivityMetrics _activityMetrics;

    public WorkerDiagnosticsSampler(
        IWorkerProcessMemoryProbe processMemory,
        IWorkerCgroupMemoryReader cgroupMemory,
        MarketDataStreamPipelineMetrics streamMetrics,
        WorkerOperationMetrics operationMetrics,
        MarketDataRuntimeActivityMetrics activityMetrics)
    {
        _processMemory = processMemory;
        _cgroupMemory = cgroupMemory;
        _streamMetrics = streamMetrics;
        _operationMetrics = operationMetrics;
        _activityMetrics = activityMetrics;
    }

    public WorkerDiagnosticsSentrySample CaptureSentry(DateTimeOffset observedAtUtc)
    {
        var process = _processMemory.Capture(observedAtUtc);
        var cgroup = _cgroupMemory.TryRead();
        return new WorkerDiagnosticsSentrySample(
            observedAtUtc,
            process.WorkingSetBytes,
            cgroup?.CurrentBytes,
            cgroup?.HighEvents,
            cgroup?.MaxEvents,
            cgroup?.OomEvents,
            cgroup?.OomKillEvents);
    }

    public WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc)
        => new(
            observedAtUtc,
            sequence,
            _processMemory.Capture(observedAtUtc),
            _cgroupMemory.TryRead(),
            _streamMetrics.Snapshot(),
            _operationMetrics.Snapshot(),
            _activityMetrics.Snapshot());
}

/// <summary>Coordinates the low-cost sentry and the lower-frequency forensic trace.</summary>
internal sealed class WorkerDiagnosticsCoordinator
{
    internal const int ControlledRestartExitCode = 75;

    private readonly Configuration.WorkerDiagnosticsOptions _options;
    private readonly IWorkerDiagnosticsSampler _sampler;
    private readonly RollingWorkerTraceStore _traces;
    private readonly IWorkerProcessTerminator _terminator;
    private int _sustainedSamples;
    private long _sequence;
    private bool _terminationRequested;

    public WorkerDiagnosticsCoordinator(
        Configuration.WorkerDiagnosticsOptions options,
        IWorkerDiagnosticsSampler sampler,
        RollingWorkerTraceStore traces,
        IWorkerProcessTerminator terminator)
    {
        options.Validate();
        _options = options;
        _sampler = sampler;
        _traces = traces;
        _terminator = terminator;
    }

    public async Task<bool> ObserveSentryAsync(CancellationToken cancellationToken = default)
    {
        if (_terminationRequested)
        {
            return true;
        }

        var sample = _sampler.CaptureSentry(DateTimeOffset.UtcNow);
        var assessment = WorkerMemoryContainmentPolicy.Assess(
            sample.CgroupCurrentBytes,
            _options.Containment,
            _sustainedSamples);
        _sustainedSamples = assessment.ConsecutiveSamples;
        if (!assessment.ShouldExit)
        {
            return false;
        }

        _terminationRequested = true;
        try
        {
            await CaptureForensicSnapshotAsync(cancellationToken).ConfigureAwait(false);
            await _traces.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _terminator.Exit(ControlledRestartExitCode);
        }

        return true;
    }

    public async Task CaptureForensicSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _sampler.CaptureSnapshot(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow);
        await _traces.AppendAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EnvironmentWorkerProcessTerminator : IWorkerProcessTerminator
{
    public void Exit(int exitCode) => Environment.Exit(exitCode);
}
