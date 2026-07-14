using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class WorkerMemoryContainmentPolicyTests
{
    [Fact]
    public void Assess_should_not_exit_when_cgroup_memory_is_unavailable()
    {
        var assessment = WorkerMemoryContainmentPolicy.Assess(
            null,
            CreateOptions(),
            previousSustainedSamples: 2);

        assessment.ConsecutiveSamples.Should().Be(0);
        assessment.ShouldExit.Should().BeFalse();
    }

    [Fact]
    public void Assess_should_exit_only_after_the_configured_sustained_samples()
    {
        var options = CreateOptions();

        var first = WorkerMemoryContainmentPolicy.Assess(400, options, previousSustainedSamples: 0);
        var second = WorkerMemoryContainmentPolicy.Assess(400, options, first.ConsecutiveSamples);
        var third = WorkerMemoryContainmentPolicy.Assess(400, options, second.ConsecutiveSamples);

        first.ShouldExit.Should().BeFalse();
        second.ShouldExit.Should().BeFalse();
        third.ShouldExit.Should().BeTrue();
        third.ConsecutiveSamples.Should().Be(3);
    }

    [Fact]
    public void Assess_should_reset_the_sample_count_below_the_threshold()
    {
        var assessment = WorkerMemoryContainmentPolicy.Assess(
            399,
            CreateOptions(),
            previousSustainedSamples: 2);

        assessment.ConsecutiveSamples.Should().Be(0);
        assessment.ShouldExit.Should().BeFalse();
    }

    private static WorkerDiagnosticsContainmentOptions CreateOptions()
        => new()
        {
            Enabled = true,
            ExitCgroupBytes = 400,
            SustainedSamples = 3,
        };
}
