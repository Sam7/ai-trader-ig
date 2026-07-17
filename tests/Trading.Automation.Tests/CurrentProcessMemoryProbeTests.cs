using FluentAssertions;
using Trading.Automation.Diagnostics;

public sealed class CurrentProcessMemoryProbeTests
{
    [Fact]
    public void Capture_should_include_managed_generation_and_thread_pool_attribution()
    {
        var probe = new CurrentProcessMemoryProbe();

        var snapshot = probe.Capture(DateTimeOffset.UtcNow);

        snapshot.ManagedRuntime.Should().NotBeNull();
        snapshot.ManagedRuntime!.TotalAllocatedBytes.Should().BeGreaterThan(0);
        snapshot.ManagedRuntime.Gen0.SizeAfterBytes.Should().BeGreaterThanOrEqualTo(0);
        snapshot.ManagedRuntime.LargeObjectHeap.SizeAfterBytes.Should().BeGreaterThanOrEqualTo(0);
        snapshot.ManagedRuntime.PinnedObjectCount.Should().BeGreaterThanOrEqualTo(0);
        snapshot.ManagedRuntime.ThreadPool.ThreadCount.Should().BeGreaterThanOrEqualTo(0);
    }
}
