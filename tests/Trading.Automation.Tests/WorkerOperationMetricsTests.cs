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

    [Fact]
    public void Begin_should_expose_a_bounded_active_operation_then_record_a_completion_checkpoint()
    {
        var metrics = new WorkerOperationMetrics();

        var operation = metrics.Begin("intraday-chart-render", itemCount: 96, correlationId: "chart-1");
        var active = metrics.Snapshot();
        operation.Complete(payloadBytes: 4_096);
        var completed = metrics.Snapshot();

        active.ActiveOperations.Should().ContainSingle().Which.Should().BeEquivalentTo(new WorkerActiveOperationSnapshot(
            "intraday-chart-render",
            "chart-1",
            96,
            active.ActiveOperations.Single().StartedAtUtc,
            active.ActiveOperations.Single().BeforeMemory));
        active.RecentCheckpoints.Should().ContainSingle(checkpoint => checkpoint.Outcome == WorkerOperationOutcome.Started);
        completed.ActiveOperations.Should().BeEmpty();
        completed.RecentCheckpoints.Should().Contain(checkpoint =>
            checkpoint.Outcome == WorkerOperationOutcome.Completed
            && checkpoint.CorrelationId == "chart-1"
            && checkpoint.PayloadBytes == 4_096);
    }

    [Fact]
    public void Fail_should_record_only_the_failure_marker_without_an_exception_payload()
    {
        var metrics = new WorkerOperationMetrics();

        var operation = metrics.Begin("market-data-recovery", itemCount: 1, correlationId: "recovery-1");
        operation.Fail();

        var snapshot = metrics.Snapshot();

        snapshot.FailedOperationCount.Should().Be(1);
        snapshot.RecentCheckpoints.Should().ContainSingle(checkpoint =>
            checkpoint.Outcome == WorkerOperationOutcome.Failed
            && checkpoint.Operation == "market-data-recovery");
    }
}
