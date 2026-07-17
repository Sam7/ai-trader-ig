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
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.swap.current"), "123\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.swap.peak"), "456\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.events"), "low 0\nhigh 2\nmax 3\noom 4\noom_kill 5\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.stat"), "anon 100\nfile 200\nkernel_stack 300\nslab 400\nshmem 500\nsock 600\npagetables 700\nfile_dirty 800\nfile_mapped 900\n");
        File.WriteAllText(Path.Combine(cgroupDirectory, "memory.pressure"), "some avg10=0.10 avg60=0.05 avg300=0.01 total=42\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n");

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
            OomKillEvents: 5)
        {
            SwapCurrentBytes = 123,
            SwapPeakBytes = 456,
            MemoryStat = new Dictionary<string, long>
            {
                ["anon"] = 100,
                ["file"] = 200,
                ["kernel_stack"] = 300,
                ["slab"] = 400,
                ["shmem"] = 500,
                ["sock"] = 600,
                ["pagetables"] = 700,
                ["file_dirty"] = 800,
                ["file_mapped"] = 900,
            },
            MemoryEvents = new Dictionary<string, long>
            {
                ["low"] = 0,
                ["high"] = 2,
                ["max"] = 3,
                ["oom"] = 4,
                ["oom_kill"] = 5,
            },
            MemoryPressure = new LinuxMemoryPressureSnapshot(0.10, 0.05, 0.01, 42, 0, 0, 0, 0),
        });
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
