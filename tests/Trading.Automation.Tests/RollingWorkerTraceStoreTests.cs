using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class RollingWorkerTraceStoreTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-trace-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CompleteAsync_should_close_a_trace_segment_for_the_next_worker_to_upload()
    {
        var options = CreateOptions();
        await using var store = new RollingWorkerTraceStore(options, "first-worker");

        await store.AppendAsync(CreateSnapshot());
        await store.CompleteAsync();

        var artifacts = store.GetUploadCandidates();

        artifacts.Should().ContainSingle();
        Path.GetExtension(artifacts[0]).Should().Be(".jsonl");
        File.ReadAllText(artifacts[0]).Should().Contain("observedAtUtc");
    }

    [Fact]
    public async Task Initialize_should_recover_a_previous_active_segment()
    {
        var options = CreateOptions();
        await using (var firstStore = new RollingWorkerTraceStore(options, "first-worker"))
        {
            await firstStore.AppendAsync(CreateSnapshot());
        }

        await using var secondStore = new RollingWorkerTraceStore(options, "second-worker");
        secondStore.Initialize();

        secondStore.GetUploadCandidates().Should().ContainSingle()
            .Which.Should().EndWith(".jsonl");
    }

    [Fact]
    public async Task Constructor_should_not_touch_the_diagnostic_directory_before_the_service_uses_it()
    {
        var options = CreateOptions();

        await using var store = new RollingWorkerTraceStore(options, "worker");

        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_should_prune_old_closed_segments_to_the_retention_budget()
    {
        var options = new WorkerDiagnosticsOptions
        {
            Enabled = true,
            LocalDirectory = _root,
            SegmentMaximumBytes = 300,
            RetentionMaximumBytes = 600,
            FlushInterval = TimeSpan.Zero,
        };

        await using var store = new RollingWorkerTraceStore(options, "worker");
        for (var index = 0; index < 10; index++)
        {
            await store.AppendAsync(CreateSnapshot(sequence: index));
        }

        await store.CompleteAsync();

        Directory.EnumerateFiles(_root, "*.jsonl").Sum(path => new FileInfo(path).Length)
            .Should().BeLessThanOrEqualTo(options.RetentionMaximumBytes);
    }

    [Fact]
    public async Task AppendAsync_should_write_only_the_defined_diagnostic_schema()
    {
        var options = CreateOptions();
        await using var store = new RollingWorkerTraceStore(options, "worker");

        await store.AppendAsync(CreateSnapshot());
        await store.CompleteAsync();

        File.ReadAllText(store.GetUploadCandidates().Single())
            .Should().NotContain("\"padding\"");
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private WorkerDiagnosticsOptions CreateOptions()
        => new()
        {
            Enabled = true,
            LocalDirectory = _root,
            SegmentMaximumBytes = 4 * 1024,
            RetentionMaximumBytes = 12 * 1024,
            FlushInterval = TimeSpan.Zero,
        };

    private static WorkerDiagnosticSnapshot CreateSnapshot(int sequence = 1)
        => new(
            ObservedAtUtc: new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
            Sequence: sequence,
            Process: new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            Cgroup: null,
            Stream: null,
            Operations: null,
            Activity: null);
}
