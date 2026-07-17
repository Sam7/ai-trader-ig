using FluentAssertions;
using Trading.MarketData;

public sealed class MarketDataRuntimeActivityMetricsTests
{
    [Fact]
    public void RecordSnapshotCompleted_should_preserve_the_latest_snapshot_outcome()
    {
        var metrics = new MarketDataRuntimeActivityMetrics();

        metrics.RecordSnapshotStarted();
        metrics.Snapshot().ActiveSnapshotCount.Should().Be(1);
        metrics.RecordSnapshotCompleted(TimeSpan.FromSeconds(2));

        var snapshot = metrics.Snapshot();

        snapshot.SnapshotStartedCount.Should().Be(1);
        snapshot.SnapshotCompletedCount.Should().Be(1);
        snapshot.SnapshotFailedCount.Should().Be(0);
        snapshot.LastSnapshotDuration.Should().Be(TimeSpan.FromSeconds(2));
        snapshot.ActiveSnapshotCount.Should().Be(0);
    }

    [Fact]
    public void RecordRecoveryFailed_should_increment_only_the_failure_counter()
    {
        var metrics = new MarketDataRuntimeActivityMetrics();

        metrics.RecordRecoveryStarted();
        metrics.Snapshot().ActiveRecoveryCount.Should().Be(1);
        metrics.RecordRecoveryFailed(TimeSpan.FromMilliseconds(100));

        var snapshot = metrics.Snapshot();

        snapshot.RecoveryStartedCount.Should().Be(1);
        snapshot.RecoveryCompletedCount.Should().Be(0);
        snapshot.RecoveryFailedCount.Should().Be(1);
        snapshot.LastRecoveryDuration.Should().Be(TimeSpan.FromMilliseconds(100));
        snapshot.ActiveRecoveryCount.Should().Be(0);
    }
}
