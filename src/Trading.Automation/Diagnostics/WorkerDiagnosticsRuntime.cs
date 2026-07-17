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

internal interface IWorkerHostMemoryProbe
{
    LinuxHostMemorySnapshot? TryRead();
}

internal sealed record WorkerHostPressureSnapshot(
    long? AvailableBytes,
    double? MemoryPressureSomeAverage10,
    int? ProcessCount);

internal interface IWorkerHostPressureProbe
{
    WorkerHostPressureSnapshot? TryRead();
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

internal sealed class LinuxHostMemoryProbe : IWorkerHostMemoryProbe
{
    private readonly LinuxHostMemoryReader _reader = new();

    public LinuxHostMemorySnapshot? TryRead() => _reader.TryRead();
}

internal sealed class LinuxHostPressureProbe : IWorkerHostPressureProbe
{
    private readonly LinuxHostPressureReader _reader = new();

    public WorkerHostPressureSnapshot? TryRead() => _reader.TryRead();
}

/// <summary>Captures process and managed-memory counters without retaining application payloads.</summary>
internal sealed class CurrentProcessMemoryProbe : IWorkerProcessMemoryProbe
{
    private readonly ILinuxProcessMemoryReader _linuxProcessMemory;
    private readonly object _allocationSync = new();
    private long _lastAllocatedBytes;
    private DateTimeOffset? _lastAllocationObservedAtUtc;

    public CurrentProcessMemoryProbe()
        : this(new LinuxProcessMemoryReader())
    {
    }

    public CurrentProcessMemoryProbe(ILinuxProcessMemoryReader linuxProcessMemory)
    {
        _linuxProcessMemory = linuxProcessMemory;
    }

    public WorkerProcessMemorySnapshot Capture(DateTimeOffset observedAtUtc)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        var managedRuntime = CaptureManagedRuntime(gcInfo, observedAtUtc);
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
            GC.CollectionCount(2))
        {
            Linux = _linuxProcessMemory.TryRead(process.Id),
            ManagedRuntime = managedRuntime,
        };
    }

    private ManagedRuntimeSnapshot CaptureManagedRuntime(GCMemoryInfo gcInfo, DateTimeOffset observedAtUtc)
    {
        var totalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        double? allocationRateBytesPerSecond = null;
        lock (_allocationSync)
        {
            if (_lastAllocationObservedAtUtc is { } previousObservedAtUtc
                && observedAtUtc > previousObservedAtUtc)
            {
                allocationRateBytesPerSecond = Math.Max(0, totalAllocatedBytes - _lastAllocatedBytes)
                    / (observedAtUtc - previousObservedAtUtc).TotalSeconds;
            }

            _lastAllocatedBytes = totalAllocatedBytes;
            _lastAllocationObservedAtUtc = observedAtUtc;
        }

        var generations = gcInfo.GenerationInfo;
        return new ManagedRuntimeSnapshot(
            totalAllocatedBytes,
            allocationRateBytesPerSecond,
            GC.GetTotalMemory(forceFullCollection: false),
            gcInfo.HeapSizeBytes,
            gcInfo.TotalCommittedBytes,
            gcInfo.FragmentedBytes,
            CaptureGeneration(generations, 0),
            CaptureGeneration(generations, 1),
            CaptureGeneration(generations, 2),
            CaptureGeneration(generations, 3),
            CaptureGeneration(generations, 4),
            gcInfo.PinnedObjectsCount,
            gcInfo.FinalizationPendingCount,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            gcInfo.MemoryLoadBytes,
            gcInfo.HighMemoryLoadThresholdBytes,
            gcInfo.TotalAvailableMemoryBytes,
            gcInfo.PauseTimePercentage,
            new ThreadPoolRuntimeSnapshot(
                ThreadPool.ThreadCount,
                ThreadPool.PendingWorkItemCount,
                ThreadPool.CompletedWorkItemCount));
    }

    private static GcGenerationSnapshot CaptureGeneration(ReadOnlySpan<GCGenerationInfo> generations, int index)
        => generations.Length > index
            ? new GcGenerationSnapshot(generations[index].SizeAfterBytes, generations[index].FragmentationAfterBytes)
            : new GcGenerationSnapshot(0, 0);

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
    private readonly IWorkerHostMemoryProbe _hostMemory;
    private readonly IWorkerHostPressureProbe _hostPressure;
    private readonly IWorkerSqliteRuntimeMetricsProbe _sqlite;

    public WorkerDiagnosticsSampler(
        IWorkerProcessMemoryProbe processMemory,
        IWorkerCgroupMemoryReader cgroupMemory,
        MarketDataStreamPipelineMetrics streamMetrics,
        WorkerOperationMetrics operationMetrics,
        MarketDataRuntimeActivityMetrics activityMetrics)
        : this(
            processMemory,
            cgroupMemory,
            streamMetrics,
            operationMetrics,
            activityMetrics,
            new LinuxHostMemoryProbe(),
            new LinuxHostPressureProbe(),
            new NoOpWorkerSqliteRuntimeMetricsProbe())
    {
    }

    public WorkerDiagnosticsSampler(
        IWorkerProcessMemoryProbe processMemory,
        IWorkerCgroupMemoryReader cgroupMemory,
        MarketDataStreamPipelineMetrics streamMetrics,
        WorkerOperationMetrics operationMetrics,
        MarketDataRuntimeActivityMetrics activityMetrics,
        IWorkerHostMemoryProbe hostMemory)
        : this(
            processMemory,
            cgroupMemory,
            streamMetrics,
            operationMetrics,
            activityMetrics,
            hostMemory,
            new LinuxHostPressureProbe(),
            new NoOpWorkerSqliteRuntimeMetricsProbe())
    {
    }

    public WorkerDiagnosticsSampler(
        IWorkerProcessMemoryProbe processMemory,
        IWorkerCgroupMemoryReader cgroupMemory,
        MarketDataStreamPipelineMetrics streamMetrics,
        WorkerOperationMetrics operationMetrics,
        MarketDataRuntimeActivityMetrics activityMetrics,
        IWorkerHostMemoryProbe hostMemory,
        IWorkerHostPressureProbe hostPressure,
        IWorkerSqliteRuntimeMetricsProbe sqlite)
    {
        _processMemory = processMemory;
        _cgroupMemory = cgroupMemory;
        _streamMetrics = streamMetrics;
        _operationMetrics = operationMetrics;
        _activityMetrics = activityMetrics;
        _hostMemory = hostMemory;
        _hostPressure = hostPressure;
        _sqlite = sqlite;
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
            cgroup?.OomKillEvents)
        {
            HostPressure = _hostPressure.TryRead(),
        };
    }

    public WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc)
        => new(
            observedAtUtc,
            sequence,
            _processMemory.Capture(observedAtUtc),
            _cgroupMemory.TryRead(),
            _streamMetrics.Snapshot(),
            _operationMetrics.Snapshot(),
            _activityMetrics.Snapshot())
        {
            Host = _hostMemory.TryRead(),
            Sqlite = _sqlite.TryRead(),
        };
}

/// <summary>Coordinates the low-cost sentry and the lower-frequency forensic trace.</summary>
internal sealed class WorkerDiagnosticsCoordinator
{
    internal const int ControlledRestartExitCode = 75;

    private readonly Configuration.WorkerDiagnosticsOptions _options;
    private readonly IWorkerDiagnosticsSampler _sampler;
    private readonly RollingWorkerTraceStore _traces;
    private readonly IWorkerProcessTerminator _terminator;
    private readonly WorkerDiagnosticPressurePolicy _pressurePolicy;
    private readonly IWorkerForensicArtifactCapture _forensicCapture;
    private readonly HashSet<long> _capturedThresholds = [];
    private int _sustainedSamples;
    private long _sequence;
    private bool _terminationRequested;

    public WorkerDiagnosticsCoordinator(
        Configuration.WorkerDiagnosticsOptions options,
        IWorkerDiagnosticsSampler sampler,
        RollingWorkerTraceStore traces,
        IWorkerProcessTerminator terminator,
        IWorkerForensicArtifactCapture? forensicCapture = null)
    {
        options.Validate();
        _options = options;
        _sampler = sampler;
        _traces = traces;
        _terminator = terminator;
        _pressurePolicy = new WorkerDiagnosticPressurePolicy(options.Pressure);
        _forensicCapture = forensicCapture ?? new NoOpWorkerForensicArtifactCapture();
    }

    public async Task<bool> ObserveSentryAsync(CancellationToken cancellationToken = default)
    {
        if (_terminationRequested)
        {
            return true;
        }

        var sample = _sampler.CaptureSentry(DateTimeOffset.UtcNow);
        var pressure = _pressurePolicy.Assess(sample);
        IsPressureMode = pressure.IsPressureMode;
        var thresholdCrossings = WorkerForensicCapturePolicy.GetNewCrossings(sample.CgroupCurrentBytes, _capturedThresholds);
        if (thresholdCrossings.Count > 0)
        {
            var snapshot = await CaptureForensicSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
            await _traces.FlushAsync(cancellationToken).ConfigureAwait(false);
            foreach (var threshold in thresholdCrossings)
            {
                try
                {
                    await _forensicCapture.CaptureAsync(snapshot, threshold, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    _capturedThresholds.Remove(threshold);
                }
            }
        }
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

    public bool IsPressureMode { get; private set; }

    public async Task CaptureForensicSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await CaptureForensicSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public DateTimeOffset? LastForensicSnapshotUtc { get; private set; }

    private async Task<WorkerDiagnosticSnapshot> CaptureForensicSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = _sampler.CaptureSnapshot(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow);
        await _traces.AppendAsync(snapshot, cancellationToken).ConfigureAwait(false);
        LastForensicSnapshotUtc = snapshot.ObservedAtUtc;
        return snapshot;
    }
}

internal sealed class EnvironmentWorkerProcessTerminator : IWorkerProcessTerminator
{
    public void Exit(int exitCode) => Environment.Exit(exitCode);
}
