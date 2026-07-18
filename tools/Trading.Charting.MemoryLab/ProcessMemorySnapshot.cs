using System.Diagnostics;
using System.Globalization;

namespace Trading.Charting.MemoryLab;

public sealed record ProcessMemorySnapshot(
    long ManagedBytes,
    long ManagedAllocatedBytes,
    long HeapSizeBytes,
    long CommittedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    long? PssBytes,
    long? AnonymousBytes,
    long? FileBytes,
    long? CgroupCurrentBytes,
    long? CgroupPeakBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public static ProcessMemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        var smaps = ReadSmapsRollup();
        return new ProcessMemorySnapshot(
            GC.GetTotalMemory(false),
            GC.GetTotalAllocatedBytes(false),
            gc.HeapSizeBytes,
            gc.TotalCommittedBytes,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            smaps?.PssBytes,
            smaps?.AnonymousBytes,
            smaps?.FileBytes,
            ReadCgroupValue("memory.current"),
            ReadCgroupValue("memory.peak"),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private static SmapsSnapshot? ReadSmapsRollup()
    {
        const string path = "/proc/self/smaps_rollup";
        if (!File.Exists(path))
        {
            return null;
        }

        long? pss = null;
        long? anonymous = null;
        long? file = null;
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kibibytes))
            {
                continue;
            }

            var bytes = kibibytes * 1024;
            if (line.StartsWith("Pss:", StringComparison.Ordinal)) pss = bytes;
            else if (line.StartsWith("Anonymous:", StringComparison.Ordinal)) anonymous = bytes;
            else if (line.StartsWith("File:", StringComparison.Ordinal)) file = bytes;
        }

        return new SmapsSnapshot(pss, anonymous, file);
    }

    private static long? ReadLong(string path)
    {
        try
        {
            return File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? ReadCgroupValue(string fileName)
    {
        try
        {
            var cgroupLine = File.ReadLines("/proc/self/cgroup")
                .FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal));
            if (cgroupLine is not null)
            {
                var relativePath = cgroupLine[3..].TrimStart('/');
                var scopedPath = Path.Combine("/sys/fs/cgroup", relativePath, fileName);
                var scopedValue = ReadLong(scopedPath);
                if (scopedValue is not null)
                {
                    return scopedValue;
                }
            }

            return ReadLong(Path.Combine("/sys/fs/cgroup", fileName));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record SmapsSnapshot(long? PssBytes, long? AnonymousBytes, long? FileBytes);
}

public sealed class PeakMemorySampler : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _samplingTask;
    private readonly int _intervalMilliseconds;
    private ProcessMemorySnapshot _peak;

    public PeakMemorySampler(int intervalMilliseconds)
    {
        _intervalMilliseconds = intervalMilliseconds;
        _peak = ProcessMemorySnapshot.Capture();
        _samplingTask = Task.Run(SampleAsync);
    }

    public ProcessMemorySnapshot Peak => _peak;

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _samplingTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
    }

    private async Task SampleAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            Update(ProcessMemorySnapshot.Capture());
            await Task.Delay(_intervalMilliseconds, _cancellation.Token).ConfigureAwait(false);
        }
    }

    private void Update(ProcessMemorySnapshot sample)
    {
        var current = _peak;
        _peak = current with
        {
            ManagedBytes = Math.Max(current.ManagedBytes, sample.ManagedBytes),
            ManagedAllocatedBytes = Math.Max(current.ManagedAllocatedBytes, sample.ManagedAllocatedBytes),
            HeapSizeBytes = Math.Max(current.HeapSizeBytes, sample.HeapSizeBytes),
            CommittedBytes = Math.Max(current.CommittedBytes, sample.CommittedBytes),
            WorkingSetBytes = Math.Max(current.WorkingSetBytes, sample.WorkingSetBytes),
            PrivateBytes = Math.Max(current.PrivateBytes, sample.PrivateBytes),
            PssBytes = Max(current.PssBytes, sample.PssBytes),
            AnonymousBytes = Max(current.AnonymousBytes, sample.AnonymousBytes),
            FileBytes = Max(current.FileBytes, sample.FileBytes),
            CgroupCurrentBytes = Max(current.CgroupCurrentBytes, sample.CgroupCurrentBytes),
            CgroupPeakBytes = Max(current.CgroupPeakBytes, sample.CgroupPeakBytes),
            Gen0Collections = Math.Max(current.Gen0Collections, sample.Gen0Collections),
            Gen1Collections = Math.Max(current.Gen1Collections, sample.Gen1Collections),
            Gen2Collections = Math.Max(current.Gen2Collections, sample.Gen2Collections),
        };
    }

    private static long? Max(long? left, long? right)
        => left is null ? right : right is null ? left : Math.Max(left.Value, right.Value);
}
