using FluentAssertions;
using Trading.Automation.Diagnostics;
using Trading.MarketData;

public sealed class WorkerSqliteRuntimeMetricsProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-sqlite-runtime-tests-{Guid.NewGuid():N}");

    [Fact]
    public void TryRead_should_report_database_sidecar_and_allocator_metrics_without_opening_a_connection()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "market.sqlite");
        File.WriteAllBytes(databasePath, new byte[123]);
        File.WriteAllBytes(databasePath + "-wal", new byte[45]);
        File.WriteAllBytes(databasePath + "-shm", new byte[67]);
        var probe = new WorkerSqliteRuntimeMetricsProbe(new MarketDataOptions { StorePath = databasePath });

        var snapshot = probe.TryRead();

        snapshot.Should().NotBeNull();
        snapshot!.DatabaseBytes.Should().Be(123);
        snapshot.WalBytes.Should().Be(45);
        snapshot.SharedMemoryBytes.Should().Be(67);
        snapshot.ConnectionPoolingEnabled.Should().BeFalse();
        snapshot.ActiveConnectionCount.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
