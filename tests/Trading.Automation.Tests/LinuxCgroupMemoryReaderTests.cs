using FluentAssertions;
using Trading.Automation.Diagnostics;

public sealed class LinuxCgroupMemoryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-cgroup-tests-{Guid.NewGuid():N}");

    [Fact]
    public void TryRead_should_read_v2_memory_accounting_for_the_current_cgroup()
    {
        var cgroupDirectory = Path.Combine(_root, "system.slice", "ai-trader.service");
        Directory.CreateDirectory(cgroupDirectory);
        File.WriteAllText(Path.Combine(_root, "proc-self-cgroup"), "0::/system.slice/ai-trader.service\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.current"), "1048576\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.peak"), "2097152\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.events"), "low 0\nhigh 2\nmax 3\noom 4\noom_kill 5\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.stat"), "anon 100\nfile 200\nkernel_stack 300\nslab 400\n");

        var snapshot = new LinuxCgroupMemoryReader(
            cgroupRoot: _root,
            procSelfCgroupPath: Path.Combine(_root, "proc-self-cgroup")).TryRead();

        snapshot.Should().BeEquivalentTo(new CgroupMemorySnapshot(
            CurrentBytes: 1_048_576,
            PeakBytes: 2_097_152,
            AnonymousBytes: 100,
            FileBytes: 200,
            KernelStackBytes: 300,
            SlabBytes: 400,
            HighEvents: 2,
            MaxEvents: 3,
            OomEvents: 4,
            OomKillEvents: 5));
    }

    [Fact]
    public void TryRead_should_return_null_when_the_memory_controller_files_are_unavailable()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "proc-self-cgroup"), "0::/system.slice/ai-trader.service\n");

        var snapshot = new LinuxCgroupMemoryReader(
            cgroupRoot: _root,
            procSelfCgroupPath: Path.Combine(_root, "proc-self-cgroup")).TryRead();

        snapshot.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
