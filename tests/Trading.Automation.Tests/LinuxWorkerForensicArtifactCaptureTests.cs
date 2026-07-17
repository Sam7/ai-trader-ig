using System.IO.Compression;
using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class LinuxWorkerForensicArtifactCaptureTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ai-trader-forensic-capture-{Guid.NewGuid():N}");

    [Fact]
    public async Task CaptureAsync_should_write_compressed_procfs_cgroup_host_and_activity_evidence_without_command_lines()
    {
        var procRoot = Path.Combine(_root, "proc");
        var processDirectory = Path.Combine(procRoot, "4242");
        Directory.CreateDirectory(Path.Combine(processDirectory, "fd"));
        Directory.CreateDirectory(Path.Combine(procRoot, "pressure"));
        File.WriteAllText(Path.Combine(processDirectory, "smaps"), "Anonymous: 32 kB\n");
        File.WriteAllText(Path.Combine(processDirectory, "smaps_rollup"), "Pss: 16 kB\n");
        File.WriteAllText(Path.Combine(processDirectory, "maps"), "00400000-00452000 r-xp 00000000 08:02 123 /opt/ai-trader/app/Trading.Worker\n");
        File.WriteAllText(Path.Combine(processDirectory, "cmdline"), "never-read-secret-command-line");
        File.WriteAllText(Path.Combine(procRoot, "meminfo"), "MemAvailable: 1024 kB\n");
        File.WriteAllText(Path.Combine(procRoot, "pressure", "memory"), "some avg10=0.00 avg60=0.00 avg300=0.00 total=0\n");

        var cgroup = Path.Combine(_root, "cgroup");
        Directory.CreateDirectory(cgroup);
        File.WriteAllText(Path.Combine(cgroup, "memory.current"), "268435456\n");
        File.WriteAllText(Path.Combine(cgroup, "memory.stat"), "anon 1024\nfile 32\n");
        File.WriteAllText(Path.Combine(cgroup, "memory.events"), "high 0\nmax 0\n");
        File.WriteAllText(Path.Combine(cgroup, "memory.pressure"), "some avg10=0.00 avg60=0.00 avg300=0.00 total=0\n");

        var options = new WorkerDiagnosticsOptions { LocalDirectory = Path.Combine(_root, "diagnostics") };
        var capture = new LinuxWorkerForensicArtifactCapture(options, processId: 4242, procRoot, cgroup);

        await capture.CaptureAsync(CreateSnapshot(), 256L * 1024 * 1024);

        var artifacts = Directory.EnumerateFiles(options.LocalDirectory, "forensic-*.gz").ToArray();
        artifacts.Should().HaveCount(5);
        var content = string.Concat(artifacts.Select(ReadGzipText));
        content.Should().Contain("Anonymous: 32 kB");
        content.Should().Contain("memoryCurrentBytes");
        content.Should().NotContain("never-read-secret-command-line");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static WorkerDiagnosticSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
            1,
            new WorkerProcessMemorySnapshot(4242, TimeSpan.Zero, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0),
            new CgroupMemorySnapshot(256L * 1024 * 1024, null, null, null, null, null, 0, 0, 0, 0),
            null,
            null,
            null);

    private static string ReadGzipText(string path)
    {
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
