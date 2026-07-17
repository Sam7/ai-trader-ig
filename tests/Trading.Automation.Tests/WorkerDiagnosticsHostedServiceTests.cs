using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class WorkerDiagnosticsHostedServiceTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-diagnostics-hosted-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExecuteAsync_should_start_the_memory_sentry_before_a_prior_artifact_upload_finishes()
    {
        var options = new WorkerDiagnosticsOptions
        {
            Enabled = true,
            LocalDirectory = _root,
            SentryInterval = TimeSpan.FromSeconds(1),
            SampleInterval = TimeSpan.FromSeconds(5),
            SegmentMaximumBytes = 4 * 1024,
            RetentionMaximumBytes = 12 * 1024,
        };
        await using var traces = new RollingWorkerTraceStore(options, "worker");
        var uploader = new BlockingUploader();
        var coordinator = new WorkerDiagnosticsCoordinator(
            options,
            new StubSampler(),
            traces,
            new RecordingTerminator());
        var service = new WorkerDiagnosticsHostedService(
            Options.Create(options),
            coordinator,
            traces,
            uploader,
            NullLogger<WorkerDiagnosticsHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await uploader.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Directory.EnumerateFiles(_root, "*.jsonl.active").Should().ContainSingle();

        uploader.Release.TrySetResult([]);
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StopAsync(stopping.Token);
    }

    [Fact]
    public async Task ExecuteAsync_should_cancel_an_inflight_artifact_upload_when_pressure_mode_starts()
    {
        var options = new WorkerDiagnosticsOptions
        {
            Enabled = true,
            LocalDirectory = _root,
            SentryInterval = TimeSpan.FromSeconds(1),
            SampleInterval = TimeSpan.FromSeconds(5),
            SegmentMaximumBytes = 4 * 1024,
            RetentionMaximumBytes = 12 * 1024,
        };
        await using var traces = new RollingWorkerTraceStore(options, "pressure-worker");
        var uploader = new BlockingUploader();
        var coordinator = new WorkerDiagnosticsCoordinator(
            options,
            new PressureSampler(),
            traces,
            new RecordingTerminator());
        var service = new WorkerDiagnosticsHostedService(
            Options.Create(options),
            coordinator,
            traces,
            uploader,
            NullLogger<WorkerDiagnosticsHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await uploader.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await uploader.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StopAsync(stopping.Token);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class StubSampler : IWorkerDiagnosticsSampler
    {
        public WorkerDiagnosticsSentrySample CaptureSentry(DateTimeOffset observedAtUtc)
            => new(observedAtUtc, 100, 200, 0, 0, 0, 0);

        public WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc)
            => new(
                observedAtUtc,
                sequence,
                new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11),
                null,
                null,
                null,
                null);
    }

    private sealed class RecordingTerminator : IWorkerProcessTerminator
    {
        public void Exit(int exitCode) => throw new InvalidOperationException("Containment should not be enabled in this test.");
    }

    private sealed class PressureSampler : IWorkerDiagnosticsSampler
    {
        public WorkerDiagnosticsSentrySample CaptureSentry(DateTimeOffset observedAtUtc)
            => new(observedAtUtc, 100, 256L * 1024 * 1024, 0, 0, 0, 0);

        public WorkerDiagnosticSnapshot CaptureSnapshot(long sequence, DateTimeOffset observedAtUtc)
            => new(
                observedAtUtc,
                sequence,
                new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11),
                new CgroupMemorySnapshot(256L * 1024 * 1024, null, null, null, null, null, 0, 0, 0, 0),
                null,
                null,
                null);
    }

    private sealed class BlockingUploader : IWorkerDiagnosticsArtifactUploader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IReadOnlyList<string>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<string>> UploadAsync(
            IReadOnlyList<string> artifactPaths,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                return await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
