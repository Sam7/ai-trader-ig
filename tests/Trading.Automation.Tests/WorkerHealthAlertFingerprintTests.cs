using FluentAssertions;
using Trading.Automation.Health;

public sealed class WorkerHealthAlertFingerprintTests
{
    [Fact]
    public void Create_should_ignore_volatile_values_for_the_same_condition()
    {
        var first = WorkerHealthAlertFingerprint.Create(
            WorkerHealthStatus.Warning,
            [
                "Working set is elevated: 314572800 bytes.",
                "Historical recovery is blocked by IG allowance until 2026-07-17T05:00:00.0000000+00:00.",
            ]);
        var second = WorkerHealthAlertFingerprint.Create(
            WorkerHealthStatus.Warning,
            [
                "Historical recovery is blocked by IG allowance until approximately 2026-07-17T06:00:00.0000000+00:00 (estimated; IG did not provide a reset time).",
                "Working set is elevated: 325000000 bytes.",
            ]);

        second.Should().Be(first);
    }

    [Fact]
    public void Create_should_distinguish_warning_categories()
    {
        var allowance = WorkerHealthAlertFingerprint.Create(
            WorkerHealthStatus.Warning,
            ["Historical recovery is blocked by IG allowance; IG did not provide a reset time."]);
        var queue = WorkerHealthAlertFingerprint.Create(
            WorkerHealthStatus.Warning,
            ["Market-data stream queue depth is elevated."]);

        queue.Should().NotBe(allowance);
    }
}
