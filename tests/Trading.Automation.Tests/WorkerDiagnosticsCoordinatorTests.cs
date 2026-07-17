using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class WorkerDiagnosticsCoordinatorTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-diagnostics-coordinator-{Guid.NewGuid():N}");

    [Fact]
    public async Task ObserveSentryAsync_should_flush_a_final_forensic_sample_before_requesting_restart()
    {
        var options = CreateOptions(containmentEnabled: true);
        await using var traces = new RollingWorkerTraceStore(options, "test-worker");
        var terminator = new RecordingTerminator();
        var sampler = new StubSampler(
            new WorkerDiagnosticsSentrySample(DateTimeOffset.UtcNow, 100, 401, 0, 0, 0, 0),
            CreateSnapshot());
        var coordinator = new WorkerDiagnosticsCoordinator(options, sampler, traces, terminator);

        var first = await coordinator.ObserveSentryAsync();
        var second = await coordinator.ObserveSentryAsync();
        await traces.CompleteAsync();

        first.Should().BeFalse();
        second.Should().BeTrue();
        terminator.ExitCodes.Should().ContainSingle().Which.Should().Be(75);
        File.ReadAllText(traces.GetUploadCandidates().Single()).Should().Contain("\"sequence\":1");
    }

    [Fact]
    public async Task CaptureForensicSnapshotAsync_should_record_without_enabling_containment()
    {
        var options = CreateOptions(containmentEnabled: false);
        await using var traces = new RollingWorkerTraceStore(options, "test-worker");
        var terminator = new RecordingTerminator();
        var coordinator = new WorkerDiagnosticsCoordinator(
            options,
            new StubSampler(
                new WorkerDiagnosticsSentrySample(DateTimeOffset.UtcNow, 100, 401, 0, 0, 0, 0),
                CreateSnapshot()),
            traces,
            terminator);

        await coordinator.CaptureForensicSnapshotAsync();
        await traces.CompleteAsync();

        terminator.ExitCodes.Should().BeEmpty();
        traces.GetUploadCandidates().Should().ContainSingle();
    }

    [Fact]
    public async Task ObserveSentryAsync_should_flush_and_capture_each_pressure_threshold_once()
    {
        var options = CreateOptions(containmentEnabled: false);
        await using var traces = new RollingWorkerTraceStore(options, "test-worker");
        var capture = new RecordingForensicCapture();
        var coordinator = new WorkerDiagnosticsCoordinator(
            options,
            new StubSampler(
                new WorkerDiagnosticsSentrySample(DateTimeOffset.UtcNow, 100, 256L * 1024 * 1024, 0, 0, 0, 0),
                CreateSnapshot() with { Cgroup = new CgroupMemorySnapshot(256L * 1024 * 1024, null, null, null, null, null, 0, 0, 0, 0) }),
            traces,
            new RecordingTerminator(),
            capture);

        await coordinator.ObserveSentryAsync();
        await coordinator.ObserveSentryAsync();

        capture.Thresholds.Should().Equal(256L * 1024 * 1024);
        coordinator.IsPressureMode.Should().BeTrue();
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private WorkerDiagnosticsOptions CreateOptions(bool containmentEnabled)
        => new()
        {
            Enabled = true,
            LocalDirectory = _root,
            SegmentMaximumBytes = 4 * 1024,
            RetentionMaximumBytes = 12 * 1024,
            FlushInterval = TimeSpan.Zero,
            Containment = new WorkerDiagnosticsContainmentOptions
            {
                Enabled = containmentEnabled,
                ExitCgroupBytes = 400,
                SustainedSamples = 2,
            },
        };

    private static WorkerDiagnosticSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            0,
            new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            new CgroupMemorySnapshot(401, null, null, null, null, null, 0, 0, 0, 0),
            null,
            null,
            null);

    private sealed class StubSampler(
        WorkerDiagnosticsSentrySample sentry,
        WorkerDiagnosticSnapshot snapshot) : IWorkerDiagnosticsSampler
    {
        public WorkerDiagnosticsSentrySample CaptureSentry(DateTimeOffset observedAtUtc)
            => sentry with { ObservedAtUtc = observedAtUtc };

        public WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc)
            => snapshot with { Sequence = sequence, ObservedAtUtc = observedAtUtc };
    }

    private sealed class RecordingTerminator : IWorkerProcessTerminator
    {
        public List<int> ExitCodes { get; } = [];

        public void Exit(int exitCode) => ExitCodes.Add(exitCode);
    }

    private sealed class RecordingForensicCapture : IWorkerForensicArtifactCapture
    {
        public List<long> Thresholds { get; } = [];

        public Task CaptureAsync(WorkerDiagnosticSnapshot snapshot, long thresholdBytes, CancellationToken cancellationToken = default)
        {
            Thresholds.Add(thresholdBytes);
            return Task.CompletedTask;
        }
    }
}
