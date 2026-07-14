using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;
using Trading.MarketData;

public sealed class GcsWorkerDiagnosticsArtifactUploaderTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-diagnostics-upload-{Guid.NewGuid():N}");

    [Fact]
    public async Task UploadAsync_should_upload_closed_trace_with_only_operational_metadata()
    {
        var options = CreateOptions(uploadEnabled: true);
        await using var traces = new RollingWorkerTraceStore(options, "worker");
        await traces.AppendAsync(CreateSnapshot());
        await traces.CompleteAsync();
        var objectStore = new RecordingObjectStore();
        var uploader = new GcsWorkerDiagnosticsArtifactUploader(
            Options.Create(options),
            Options.Create(new MarketDataOptions
            {
                CloudSnapshot = new MarketDataCloudSnapshotOptions { BucketName = "test-bucket" },
            }),
            objectStore,
            NullLogger<GcsWorkerDiagnosticsArtifactUploader>.Instance);

        var uploaded = await uploader.UploadAsync(traces.GetUploadCandidates());

        uploaded.Should().ContainSingle();
        objectStore.Uploads.Should().ContainSingle();
        var upload = objectStore.Uploads.Single();
        upload.BucketName.Should().Be("test-bucket");
        upload.ObjectName.Should().StartWith("market-data/diagnostics/");
        upload.ContentType.Should().Be("application/x-ndjson");
        upload.Metadata.Keys.Should().OnlyContain(key =>
            key == "diagnostic-kind" || key == "created-at-utc" || key == "size-bytes");
    }

    [Fact]
    public async Task UploadAsync_should_not_call_cloud_storage_when_artifact_upload_is_disabled()
    {
        var objectStore = new RecordingObjectStore();
        var uploader = new GcsWorkerDiagnosticsArtifactUploader(
            Options.Create(CreateOptions(uploadEnabled: false)),
            Options.Create(new MarketDataOptions
            {
                CloudSnapshot = new MarketDataCloudSnapshotOptions { BucketName = "test-bucket" },
            }),
            objectStore,
            NullLogger<GcsWorkerDiagnosticsArtifactUploader>.Instance);

        var uploaded = await uploader.UploadAsync([Path.Combine(_root, "worker.jsonl")]);

        uploaded.Should().BeEmpty();
        objectStore.Uploads.Should().BeEmpty();
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private WorkerDiagnosticsOptions CreateOptions(bool uploadEnabled)
        => new()
        {
            Enabled = true,
            LocalDirectory = _root,
            SegmentMaximumBytes = 4 * 1024,
            RetentionMaximumBytes = 12 * 1024,
            FlushInterval = TimeSpan.Zero,
            UploadClosedSegments = uploadEnabled,
        };

    private static WorkerDiagnosticSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            1,
            new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 100, 200, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            null,
            null,
            null,
            null);

    private sealed class RecordingObjectStore : IMarketDataObjectStore
    {
        public List<Upload> Uploads { get; } = [];

        public Task<MarketDataSnapshotObject?> GetAsync(string bucketName, string objectName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DownloadAsync(string bucketName, string objectName, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UploadAsync(
            string bucketName,
            string objectName,
            string sourcePath,
            IReadOnlyDictionary<string, string> metadata,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Uploads.Add(new Upload(bucketName, objectName, sourcePath, metadata, contentType));
            return Task.CompletedTask;
        }
    }

    private sealed record Upload(
        string BucketName,
        string ObjectName,
        string SourcePath,
        IReadOnlyDictionary<string, string> Metadata,
        string ContentType);
}
