using FluentAssertions;
using Trading.Automation.Health;

public sealed class WorkerOperationMetricsTests
{
    [Fact]
    public void Record_should_keep_the_latest_operation_and_the_highest_payload()
    {
        var metrics = new WorkerOperationMetrics();

        metrics.Record("charts", 2, 100, TimeSpan.FromMilliseconds(5), 200);
        metrics.Record("evidence", 1, 250, TimeSpan.FromMilliseconds(7), 300);

        var snapshot = metrics.Snapshot();

        snapshot.LastOperation.Should().Be("evidence");
        snapshot.LastItemCount.Should().Be(1);
        snapshot.LastPayloadBytes.Should().Be(250);
        snapshot.MaxPayloadBytes.Should().Be(250);
        snapshot.LastDuration.Should().Be(TimeSpan.FromMilliseconds(7));
        snapshot.LastWorkingSetBytes.Should().Be(300);
        snapshot.OperationCount.Should().Be(2);
    }
}
