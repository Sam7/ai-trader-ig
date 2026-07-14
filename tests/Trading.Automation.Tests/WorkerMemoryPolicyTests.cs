using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Health;

public sealed class WorkerMemoryPolicyTests
{
    [Fact]
    public void Assess_should_report_healthy_and_reset_critical_samples_below_warning()
    {
        var options = CreateOptions();

        var assessment = WorkerMemoryPolicy.Assess(100, options, previousCriticalSamples: 2);

        assessment.Status.Should().Be(WorkerHealthStatus.Healthy);
        assessment.Reason.Should().BeNull();
        assessment.ConsecutiveCriticalSamples.Should().Be(0);
        assessment.ShouldFailFast.Should().BeFalse();
    }

    [Fact]
    public void Assess_should_report_warning_without_incrementing_critical_samples()
    {
        var options = CreateOptions();

        var assessment = WorkerMemoryPolicy.Assess(250, options, previousCriticalSamples: 1);

        assessment.Status.Should().Be(WorkerHealthStatus.Warning);
        assessment.Reason.Should().Contain("elevated");
        assessment.ConsecutiveCriticalSamples.Should().Be(0);
        assessment.ShouldFailFast.Should().BeFalse();
    }

    [Fact]
    public void Assess_should_fail_fast_only_after_the_configured_critical_samples()
    {
        var options = CreateOptions();

        var first = WorkerMemoryPolicy.Assess(450, options, previousCriticalSamples: 0);
        var second = WorkerMemoryPolicy.Assess(450, options, first.ConsecutiveCriticalSamples);

        first.Status.Should().Be(WorkerHealthStatus.Critical);
        first.ConsecutiveCriticalSamples.Should().Be(1);
        first.ShouldFailFast.Should().BeFalse();
        second.ConsecutiveCriticalSamples.Should().Be(2);
        second.ShouldFailFast.Should().BeTrue();
    }

    private static WorkerHealthOptions CreateOptions()
        => new()
        {
            WarningWorkingSetBytes = 200,
            CriticalWorkingSetBytes = 300,
            FailFastWorkingSetBytes = 400,
            FailFastEnabled = true,
            CriticalSampleCount = 2,
        };
}
