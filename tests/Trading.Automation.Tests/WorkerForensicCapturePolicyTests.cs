using FluentAssertions;
using Trading.Automation.Diagnostics;

public sealed class WorkerForensicCapturePolicyTests
{
    [Fact]
    public void GetNewCrossings_should_return_each_threshold_once_per_process_instance()
    {
        var captured = new HashSet<long>();

        WorkerForensicCapturePolicy.GetNewCrossings(255L * 1024 * 1024, captured).Should().BeEmpty();
        WorkerForensicCapturePolicy.GetNewCrossings(256L * 1024 * 1024, captured)
            .Should().Equal(256L * 1024 * 1024);
        WorkerForensicCapturePolicy.GetNewCrossings(319L * 1024 * 1024, captured).Should().BeEmpty();
        WorkerForensicCapturePolicy.GetNewCrossings(320L * 1024 * 1024, captured)
            .Should().Equal(320L * 1024 * 1024);
        WorkerForensicCapturePolicy.GetNewCrossings(384L * 1024 * 1024, captured)
            .Should().Equal(384L * 1024 * 1024);
        WorkerForensicCapturePolicy.GetNewCrossings(512L * 1024 * 1024, captured).Should().BeEmpty();
    }
}
