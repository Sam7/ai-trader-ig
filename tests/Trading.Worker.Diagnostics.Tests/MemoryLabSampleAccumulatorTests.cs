using FluentAssertions;
using Trading.Worker.Diagnostics;

public sealed class MemoryLabSampleAccumulatorTests
{
    [Fact]
    public void Build_should_report_peaks_without_retaining_individual_samples()
    {
        var accumulator = new MemoryLabSampleAccumulator();
        accumulator.Add(new MemoryLabSample(100, 200, 300, 400, 500, 600));
        accumulator.Add(new MemoryLabSample(90, 250, 280, 450, 480, 700));

        var summary = accumulator.Build(
            TimeSpan.FromSeconds(1),
            retainedBytes: 64,
            churnAllocatedBytes: 128,
            usesServerGarbageCollection: false);

        summary.BaselineWorkingSetBytes.Should().Be(100);
        summary.PeakWorkingSetBytes.Should().Be(100);
        summary.PeakPrivateMemoryBytes.Should().Be(250);
        summary.PeakManagedMemoryBytes.Should().Be(300);
        summary.PeakCgroupCurrentBytes.Should().Be(700);
        summary.RetainedBytes.Should().Be(64);
        summary.ChurnAllocatedBytes.Should().Be(128);
        summary.UsesServerGarbageCollection.Should().BeFalse();
    }
}
