using System.Text.Json;
using FluentAssertions;
using Trading.Automation.Diagnostics;

public sealed class WorkerDiagnosticTraceReaderTests
{
    [Fact]
    public void TryParse_should_preserve_a_version_one_trace_without_guessing_new_fields()
    {
        const string versionOne = """
            {"observedAtUtc":"2026-07-15T00:01:00+00:00","sequence":1,"process":{"processId":7,"uptime":"00:00:01","workingSetBytes":1,"privateMemoryBytes":2,"threadCount":3,"handleCount":4,"totalManagedMemoryBytes":5,"heapSizeBytes":6,"fragmentedBytes":7,"totalCommittedBytes":8,"gen0Collections":9,"gen1Collections":10,"gen2Collections":11},"cgroup":null,"stream":null,"operations":null,"activity":null}
            """;

        var parsed = WorkerDiagnosticTraceReader.TryParse(versionOne, out var snapshot);

        parsed.Should().BeTrue();
        snapshot.Should().NotBeNull();
        snapshot!.SchemaVersion.Should().Be(1);
        snapshot.Host.Should().BeNull();
        snapshot.Process.ManagedRuntime.Should().BeNull();
    }

    [Fact]
    public void TryParse_should_round_trip_a_version_two_trace_with_extended_attribution()
    {
        var source = new WorkerDiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            2,
            new WorkerProcessMemorySnapshot(1, TimeSpan.Zero, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)
            {
                Linux = new LinuxProcessMemorySnapshot(2, 1, 3, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5, 0, 1),
            },
            null,
            null,
            null,
            null)
        {
            Host = new LinuxHostMemorySnapshot(100, 50, 20, 1, 2, 3, 2, null, 1, []),
        };
        var json = JsonSerializer.Serialize(source, WorkerDiagnosticsJsonContext.Default.WorkerDiagnosticSnapshot);

        var parsed = WorkerDiagnosticTraceReader.TryParse(json, out var snapshot);

        parsed.Should().BeTrue();
        snapshot.Should().BeEquivalentTo(source);
    }
}
