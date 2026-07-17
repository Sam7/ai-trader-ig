using FluentAssertions;
using Trading.Automation.Diagnostics;

public sealed class LinuxProcfsDiagnosticsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-procfs-tests-{Guid.NewGuid():N}");

    [Fact]
    public void TryRead_should_parse_process_status_smaps_and_mapping_counts_without_command_line_data()
    {
        var processDirectory = CreateProcessDirectory(4242, rssKilobytes: 4_096, pssKilobytes: 3_000, executable: "Trading.Worker");
        var status = """
            Name:\tTrading.Worker
            VmSize:\t8192 kB
            VmRSS:\t4096 kB
            RssAnon:\t2048 kB
            RssFile:\t1024 kB
            RssShmem:\t1024 kB
            VmData:\t512 kB
            VmStk:\t64 kB
            VmExe:\t128 kB
            VmLib:\t256 kB
            VmLck:\t32 kB
            VmSwap:\t16 kB
            Threads:\t7
            """.Replace("\\t", "\t", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(processDirectory, "status"), status);
        File.WriteAllText(Path.Combine(processDirectory, "smaps_rollup"), """
            Rss:                4096 kB
            Pss:                3000 kB
            Shared_Clean:        200 kB
            Shared_Dirty:         20 kB
            Private_Clean:       300 kB
            Private_Dirty:      2500 kB
            Anonymous:          2400 kB
            Swap:                 16 kB
            Locked:               32 kB
            """);
        File.WriteAllText(Path.Combine(processDirectory, "maps"), """
            00400000-00452000 r-xp 00000000 08:02 123 /opt/ai-trader/app/Trading.Worker
            7f000000-7f001000 rw-p 00000000 00:00 0 [heap]
            """);
        Directory.CreateDirectory(Path.Combine(processDirectory, "fd"));
        File.WriteAllText(Path.Combine(processDirectory, "fd", "0"), string.Empty);
        File.WriteAllText(Path.Combine(processDirectory, "fd", "1"), string.Empty);

        var snapshot = new LinuxProcessMemoryReader(_root).TryRead(4242);

        snapshot.Should().BeEquivalentTo(new LinuxProcessMemorySnapshot(
            ResidentSetBytes: 4_194_304,
            PssBytes: 3_072_000,
            VirtualBytes: 8_388_608,
            AnonymousBytes: 2_457_600,
            FileBytes: 1_048_576,
            SharedMemoryBytes: 1_048_576,
            PrivateCleanBytes: 307_200,
            PrivateDirtyBytes: 2_560_000,
            SharedCleanBytes: 204_800,
            SharedDirtyBytes: 20_480,
            SwapBytes: 16_384,
            LockedBytes: 32_768,
            StackBytes: 65_536,
            ExecutableBytes: 131_072,
            LibraryBytes: 262_144,
            DataBytes: 524_288,
            ThreadCount: 7,
            FileDescriptorCount: 2,
            SocketCount: 0,
            MappedFileCount: 1));
    }

    [Fact]
    public void TryRead_should_report_host_memory_psi_and_a_bounded_process_census()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pressure"));
        File.WriteAllText(Path.Combine(_root, "meminfo"), """
            MemTotal:        1048576 kB
            MemAvailable:     262144 kB
            Cached:            131072 kB
            Dirty:                512 kB
            Slab:               16384 kB
            SwapTotal:          65536 kB
            SwapFree:           32768 kB
            """);
        File.WriteAllText(Path.Combine(_root, "pressure", "memory"), """
            some avg10=0.50 avg60=0.25 avg300=0.10 total=42
            full avg10=0.10 avg60=0.05 avg300=0.01 total=7
            """);
        File.WriteAllText(Path.Combine(_root, "stat"), "btime 1000\n");
        CreateProcessDirectory(11, rssKilobytes: 100, pssKilobytes: 80, executable: "python3");
        CreateProcessDirectory(12, rssKilobytes: 200, pssKilobytes: 150, executable: "gcloud");

        var snapshot = new LinuxHostMemoryReader(_root, maximumProcesses: 1).TryRead();

        snapshot.Should().NotBeNull();
        snapshot!.TotalBytes.Should().Be(1_073_741_824);
        snapshot.AvailableBytes.Should().Be(268_435_456);
        snapshot.MemoryPressure!.SomeAverage10.Should().Be(0.50);
        snapshot.MemoryPressure.FullTotalMicroseconds.Should().Be(7);
        snapshot.ProcessCount.Should().Be(2);
        snapshot.TopProcesses.Should().ContainSingle().Which.Should().BeEquivalentTo(new HostProcessSnapshot(
            ProcessId: 12,
            ParentProcessId: 1,
            UserId: 1000,
            StartedAtUtc: DateTimeOffset.FromUnixTimeSeconds(1_010),
            ExecutableName: "gcloud",
            Cgroup: "/cron.service",
            ResidentSetBytes: 204_800,
            PssBytes: 153_600));
    }

    [Fact]
    public void ReadKilobyteValue_should_stop_after_the_requested_status_field()
    {
        var statusPath = Path.Combine(_root, "status");
        Directory.CreateDirectory(_root);
        File.WriteAllText(statusPath, "VmRSS:\t4096 kB\nVmData:\t512 kB\n");

        LinuxProcessMemoryReader.ReadKilobyteValue(statusPath, "VmRSS").Should().Be(4_194_304);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateProcessDirectory(int processId, int rssKilobytes, int pssKilobytes, string executable)
    {
        var directory = Path.Combine(_root, processId.ToString());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "comm"), executable + "\n");
        File.WriteAllText(Path.Combine(directory, "cgroup"), "0::/cron.service\n");
        File.WriteAllText(Path.Combine(directory, "stat"), $"{processId} ({executable}) S 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 1000 0\n");
        File.WriteAllText(Path.Combine(directory, "status"), $"Uid:\t1000\t1000\t1000\t1000\nVmRSS:\t{rssKilobytes} kB\n");
        File.WriteAllText(Path.Combine(directory, "smaps_rollup"), $"Pss: {pssKilobytes} kB\n");
        return directory;
    }
}
