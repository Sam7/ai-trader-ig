using FluentAssertions;
using Microsoft.Extensions.Options;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class MarketDataMirrorStatusServiceTests
{
    [Fact]
    public async Task GetStatusAsync_WithEnabledMirrorAndNoSuccessfulSync_ShouldReportStale()
    {
        using var workspace = TestWorkspace.Create();
        var options = Options.Create(new MarketDataOptions
        {
            CloudSnapshot = new MarketDataCloudSnapshotOptions
            {
                BucketName = "bucket",
                ObjectName = "market-data.sqlite",
                Mirror = new MarketDataSnapshotMirrorOptions
                {
                    Enabled = true,
                    StatePath = Path.Combine(workspace.DirectoryPath, "state.json"),
                    StaleAfter = TimeSpan.FromMinutes(15),
                },
            },
        });
        var service = new MarketDataMirrorStatusService(
            new FileMarketDataMirrorStateStore(options),
            new FakeSnapshotObjectStore(null),
            new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
            options);

        var status = await service.GetStatusAsync();

        status.IsStale.Should().BeTrue();
        status.Diagnosis.Should().Be("Remote snapshot object was not found.");
    }

    private sealed class FakeSnapshotObjectStore : IMarketDataSnapshotObjectStore
    {
        private readonly MarketDataSnapshotObject? _snapshot;

        public FakeSnapshotObjectStore(MarketDataSnapshotObject? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<MarketDataSnapshotObject?> GetAsync(
            string bucketName,
            string objectName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task DownloadAsync(
            string bucketName,
            string objectName,
            string destinationPath,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UploadAsync(
            string bucketName,
            string objectName,
            string sourcePath,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TestWorkspace Create()
            => new(Directory.CreateTempSubdirectory().FullName);

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
