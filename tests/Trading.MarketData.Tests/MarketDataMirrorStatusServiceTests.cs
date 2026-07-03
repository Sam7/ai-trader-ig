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
            new FixedMarketDataClock(DateTimeOffset.Parse("2026-06-29T01:00:00Z")),
            options);

        var status = await service.GetStatusAsync();

        status.IsStale.Should().BeTrue();
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
