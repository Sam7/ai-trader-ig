using FluentAssertions;
using Trading.Automation.Diagnostics;
using Trading.Automation.Health;
using Trading.MarketData;

public sealed class WorkerDiagnosticsSamplerTests
{
    [Fact]
    public void CaptureSentry_should_include_memory_and_cgroup_oom_evidence()
    {
        var sampler = CreateSampler(
            cgroup: new CgroupMemorySnapshot(300, 400, 250, 10, 2, 3, 4, 5, 6, 7));

        var sample = sampler.CaptureSentry(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));

        sample.WorkingSetBytes.Should().Be(100);
        sample.CgroupCurrentBytes.Should().Be(300);
        sample.HighEvents.Should().Be(4);
        sample.MaxEvents.Should().Be(5);
        sample.OomEvents.Should().Be(6);
        sample.OomKillEvents.Should().Be(7);
    }

    [Fact]
    public void CaptureSnapshot_should_combine_bounded_runtime_metrics()
    {
        var stream = new MarketDataStreamPipelineMetrics();
        stream.RecordReceived(new DateTimeOffset(2026, 7, 15, 1, 0, 0, TimeSpan.Zero));
        stream.RecordDispatcherDepth(12);
        var operations = new WorkerOperationMetrics();
        operations.Record("synthetic", 2, 30, TimeSpan.FromMilliseconds(4), 100);
        var activity = new MarketDataRuntimeActivityMetrics();
        activity.RecordSnapshotStarted();
        activity.RecordSnapshotCompleted(TimeSpan.FromSeconds(2));
        var sampler = new WorkerDiagnosticsSampler(
            new StubProcessProbe(CreateProcess()),
            new StubCgroupReader(null),
            stream,
            operations,
            activity);

        var snapshot = sampler.CaptureSnapshot(42, new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));

        snapshot.Sequence.Should().Be(42);
        snapshot.Process.Should().Be(CreateProcess());
        snapshot.Cgroup.Should().BeNull();
        snapshot.Stream!.DispatcherDepth.Should().Be(12);
        snapshot.Operations!.LastOperation.Should().Be("synthetic");
        snapshot.Activity!.SnapshotCompletedCount.Should().Be(1);
    }

    private static WorkerDiagnosticsSampler CreateSampler(CgroupMemorySnapshot? cgroup)
        => new(
            new StubProcessProbe(CreateProcess()),
            new StubCgroupReader(cgroup),
            new MarketDataStreamPipelineMetrics(),
            new WorkerOperationMetrics(),
            new MarketDataRuntimeActivityMetrics());

    private static WorkerProcessMemorySnapshot CreateProcess()
        => new(123, TimeSpan.FromMinutes(2), 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11);

    private sealed class StubProcessProbe(WorkerProcessMemorySnapshot snapshot) : IWorkerProcessMemoryProbe
    {
        public WorkerProcessMemorySnapshot Capture(DateTimeOffset observedAtUtc) => snapshot;
    }

    private sealed class StubCgroupReader(CgroupMemorySnapshot? snapshot) : IWorkerCgroupMemoryReader
    {
        public CgroupMemorySnapshot? TryRead() => snapshot;
    }
}
