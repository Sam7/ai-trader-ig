using System.Globalization;

namespace Trading.Automation.Diagnostics;

internal sealed record LinuxProcessMemorySnapshot(
    long? ResidentSetBytes,
    long? PssBytes,
    long? VirtualBytes,
    long? AnonymousBytes,
    long? FileBytes,
    long? SharedMemoryBytes,
    long? PrivateCleanBytes,
    long? PrivateDirtyBytes,
    long? SharedCleanBytes,
    long? SharedDirtyBytes,
    long? SwapBytes,
    long? LockedBytes,
    long? StackBytes,
    long? ExecutableBytes,
    long? LibraryBytes,
    long? DataBytes,
    int? ThreadCount,
    int? FileDescriptorCount,
    int? SocketCount,
    int? MappedFileCount);

internal sealed record LinuxMemoryPressureSnapshot(
    double? SomeAverage10,
    double? SomeAverage60,
    double? SomeAverage300,
    long? SomeTotalMicroseconds,
    double? FullAverage10,
    double? FullAverage60,
    double? FullAverage300,
    long? FullTotalMicroseconds);

internal sealed record HostProcessSnapshot(
    int ProcessId,
    int? ParentProcessId,
    int? UserId,
    DateTimeOffset? StartedAtUtc,
    string ExecutableName,
    string? Cgroup,
    long? ResidentSetBytes,
    long? PssBytes);

internal sealed record LinuxHostMemorySnapshot(
    long? TotalBytes,
    long? AvailableBytes,
    long? CachedBytes,
    long? DirtyBytes,
    long? SlabBytes,
    long? SwapTotalBytes,
    long? SwapFreeBytes,
    LinuxMemoryPressureSnapshot? MemoryPressure,
    int ProcessCount,
    IReadOnlyList<HostProcessSnapshot> TopProcesses);

/// <summary>Reads one process's numeric Linux memory accounting without reading its command line or environment.</summary>
internal interface ILinuxProcessMemoryReader
{
    LinuxProcessMemorySnapshot? TryRead(int processId);
}

internal sealed class LinuxProcessMemoryReader : ILinuxProcessMemoryReader
{
    private readonly string _procRoot;

    public LinuxProcessMemoryReader(string procRoot = "/proc")
    {
        _procRoot = procRoot;
    }

    public LinuxProcessMemorySnapshot? TryRead(int processId)
    {
        var processDirectory = Path.Combine(_procRoot, processId.ToString(CultureInfo.InvariantCulture));
        try
        {
            if (!Directory.Exists(processDirectory))
            {
                return null;
            }

            var status = ReadKilobyteValues(Path.Combine(processDirectory, "status"));
            var smaps = ReadKilobyteValues(Path.Combine(processDirectory, "smaps_rollup"));
            return new LinuxProcessMemorySnapshot(
                ReadValue(smaps, "Rss") ?? ReadValue(status, "VmRSS"),
                ReadValue(smaps, "Pss"),
                ReadValue(status, "VmSize"),
                ReadValue(smaps, "Anonymous") ?? ReadValue(status, "RssAnon"),
                ReadValue(status, "RssFile"),
                ReadValue(status, "RssShmem"),
                ReadValue(smaps, "Private_Clean"),
                ReadValue(smaps, "Private_Dirty"),
                ReadValue(smaps, "Shared_Clean"),
                ReadValue(smaps, "Shared_Dirty"),
                ReadValue(smaps, "Swap") ?? ReadValue(status, "VmSwap"),
                ReadValue(smaps, "Locked") ?? ReadValue(status, "VmLck"),
                ReadValue(status, "VmStk"),
                ReadValue(status, "VmExe"),
                ReadValue(status, "VmLib"),
                ReadValue(status, "VmData"),
                ReadIntValue(status, "Threads"),
                CountFileDescriptors(processDirectory),
                CountSockets(processDirectory),
                CountMappedFiles(processDirectory));
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

    internal static IReadOnlyDictionary<string, long> ReadKilobyteValues(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var numeric = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                values[key] = checked(parsed * 1024);
            }
        }

        return values;
    }

    internal static long? ReadKilobyteValue(string path, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || !string.Equals(line[..separator].Trim(), key, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            var numeric = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? checked(parsed * 1024)
                : null;
        }

        return null;
    }

    private static long? ReadValue(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static int? ReadIntValue(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) && value is >= 0 and <= int.MaxValue
            ? (int)(value / 1024)
            : null;

    private static int? CountFileDescriptors(string processDirectory)
    {
        var fdDirectory = Path.Combine(processDirectory, "fd");
        return Directory.Exists(fdDirectory) ? Directory.EnumerateFileSystemEntries(fdDirectory).Count() : null;
    }

    private static int? CountSockets(string processDirectory)
    {
        var fdDirectory = Path.Combine(processDirectory, "fd");
        if (!Directory.Exists(fdDirectory))
        {
            return null;
        }

        var sockets = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(fdDirectory))
        {
            try
            {
                if (File.ResolveLinkTarget(path, returnFinalTarget: false)?.Name.StartsWith("socket:[", StringComparison.Ordinal) == true)
                {
                    sockets++;
                }
            }
            catch (IOException)
            {
                // A file descriptor can disappear while the process is being sampled.
            }
        }

        return sockets;
    }

    private static int? CountMappedFiles(string processDirectory)
    {
        var mapsPath = Path.Combine(processDirectory, "maps");
        if (!File.Exists(mapsPath))
        {
            return null;
        }

        return File.ReadLines(mapsPath)
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Count(parts => parts.Length == 6 && parts[5].StartsWith("/", StringComparison.Ordinal));
    }
}

/// <summary>Builds a bounded host census from procfs, deliberately excluding command lines and environments.</summary>
internal sealed class LinuxProcessCensusReader
{
    private readonly string _procRoot;
    private readonly int _maximumProcesses;
    private readonly int _clockTicksPerSecond;
    private readonly LinuxProcessMemoryReader _memoryReader;

    public LinuxProcessCensusReader(string procRoot = "/proc", int maximumProcesses = 15, int clockTicksPerSecond = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumProcesses, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(clockTicksPerSecond, 0);

        _procRoot = procRoot;
        _maximumProcesses = maximumProcesses;
        _clockTicksPerSecond = clockTicksPerSecond;
        _memoryReader = new LinuxProcessMemoryReader(procRoot);
    }

    public (int ProcessCount, IReadOnlyList<HostProcessSnapshot> TopProcesses) TryRead()
    {
        try
        {
            var bootTimeUtc = TryReadBootTimeUtc();
            var processDirectories = Directory.EnumerateDirectories(_procRoot)
                .Select(path => new { Path = path, Name = Path.GetFileName(path) })
                .Where(entry => int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .ToArray();

            // Read only inexpensive status files for all processes. smaps_rollup is opened solely
            // for the bounded RSS leaders, which keeps the normal five-second sample inexpensive.
            var candidates = processDirectories
                .Select(entry => TryReadRssCandidate(entry.Path))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderByDescending(candidate => candidate.ResidentSetBytes ?? 0)
                .ThenBy(candidate => candidate.ProcessId)
                .Take(_maximumProcesses)
                .ToArray();

            var top = candidates
                .Select(candidate => TryReadProcess(candidate.Path, bootTimeUtc))
                .Where(process => process is not null)
                .Select(process => process!)
                .OrderByDescending(process => process.PssBytes ?? process.ResidentSetBytes ?? 0)
                .ThenBy(process => process.ProcessId)
                .ToArray();
            return (processDirectories.Length, top);
        }
        catch (IOException)
        {
            return (0, []);
        }
        catch (UnauthorizedAccessException)
        {
            return (0, []);
        }
    }

    private RssCandidate? TryReadRssCandidate(string processDirectory)
    {
        if (!int.TryParse(Path.GetFileName(processDirectory), NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            return null;
        }

        try
        {
            return new RssCandidate(
                processId,
                processDirectory,
                LinuxProcessMemoryReader.ReadKilobyteValue(Path.Combine(processDirectory, "status"), "VmRSS"));
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

    private HostProcessSnapshot? TryReadProcess(string processDirectory, DateTimeOffset? bootTimeUtc)
    {
        if (!int.TryParse(Path.GetFileName(processDirectory), NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            return null;
        }

        try
        {
            var memory = _memoryReader.TryRead(processId);
            var executable = ReadSingleLine(Path.Combine(processDirectory, "comm"));
            if (string.IsNullOrWhiteSpace(executable))
            {
                return null;
            }

            var status = LinuxProcessMemoryReader.ReadKilobyteValues(Path.Combine(processDirectory, "status"));
            var userId = TryReadUserId(Path.Combine(processDirectory, "status"));
            var (parentProcessId, startedAtUtc) = TryReadProcessTiming(Path.Combine(processDirectory, "stat"), bootTimeUtc);
            return new HostProcessSnapshot(
                processId,
                parentProcessId,
                userId,
                startedAtUtc,
                executable,
                TryReadCgroup(Path.Combine(processDirectory, "cgroup")),
                memory?.ResidentSetBytes ?? ReadStatusKilobytes(status, "VmRSS"),
                memory?.PssBytes);
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

    private DateTimeOffset? TryReadBootTimeUtc()
    {
        var path = Path.Combine(_procRoot, "stat");
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && parts[0] == "btime"
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return null;
    }

    private (int? ParentProcessId, DateTimeOffset? StartedAtUtc) TryReadProcessTiming(string path, DateTimeOffset? bootTimeUtc)
    {
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var stat = File.ReadAllText(path).Trim();
        var closingParenthesis = stat.LastIndexOf(')');
        if (closingParenthesis < 0 || closingParenthesis == stat.Length - 1)
        {
            return (null, null);
        }

        var fields = stat[(closingParenthesis + 1)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parentProcessId = fields.Length > 1
            && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent)
            ? (int?)parent
            : null;
        var startedAtUtc = fields.Length > 19
            && bootTimeUtc is not null
            && long.TryParse(fields[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startTicks)
            ? (DateTimeOffset?)bootTimeUtc.Value.AddSeconds(startTicks / (double)_clockTicksPerSecond)
            : null;
        return (parentProcessId, startedAtUtc);
    }

    private static string? ReadSingleLine(string path)
        => File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;

    private static int? TryReadUserId(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var line = File.ReadLines(path).FirstOrDefault(candidate => candidate.StartsWith("Uid:", StringComparison.Ordinal));
        var uid = line?.Split(':', 2)[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(uid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? TryReadCgroup(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadLines(path)
            .Select(line => line.Split(':', 3))
            .Where(parts => parts.Length == 3 && string.IsNullOrEmpty(parts[1]))
            .Select(parts => parts[2])
            .FirstOrDefault();
    }

    private static long? ReadStatusKilobytes(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private sealed record RssCandidate(int ProcessId, string Path, long? ResidentSetBytes);
}

/// <summary>Reads bounded host memory and pressure evidence from procfs.</summary>
internal sealed class LinuxHostMemoryReader
{
    private readonly string _procRoot;
    private readonly LinuxProcessCensusReader _censusReader;
    private readonly TimeSpan _censusInterval;
    private DateTimeOffset? _lastCensusAtUtc;
    private (int ProcessCount, IReadOnlyList<HostProcessSnapshot> TopProcesses)? _lastCensus;

    public LinuxHostMemoryReader(
        string procRoot = "/proc",
        int maximumProcesses = 15,
        TimeSpan? censusInterval = null)
    {
        _procRoot = procRoot;
        _censusReader = new LinuxProcessCensusReader(procRoot, maximumProcesses);
        _censusInterval = censusInterval ?? TimeSpan.FromSeconds(30);
    }

    public LinuxHostMemorySnapshot? TryRead()
    {
        try
        {
            var memory = LinuxProcessMemoryReader.ReadKilobyteValues(Path.Combine(_procRoot, "meminfo"));
            if (memory.Count == 0)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var census = _lastCensus is not null
                && _lastCensusAtUtc is not null
                && now - _lastCensusAtUtc.Value < _censusInterval
                ? _lastCensus.Value
                : _censusReader.TryRead();
            _lastCensus = census;
            _lastCensusAtUtc = now;
            return new LinuxHostMemorySnapshot(
                ReadValue(memory, "MemTotal"),
                ReadValue(memory, "MemAvailable"),
                ReadValue(memory, "Cached"),
                ReadValue(memory, "Dirty"),
                ReadValue(memory, "Slab"),
                ReadValue(memory, "SwapTotal"),
                ReadValue(memory, "SwapFree"),
                TryReadPressure(Path.Combine(_procRoot, "pressure", "memory")),
                census.ProcessCount,
                census.TopProcesses);
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

    internal static LinuxMemoryPressureSnapshot? TryReadPressure(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var values = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1 && (parts[0] == "some" || parts[0] == "full"))
            {
                values[parts[0]] = ParsePressureValues(parts.Skip(1));
            }
        }
        return new LinuxMemoryPressureSnapshot(
            ReadDouble(values, "some", "avg10"),
            ReadDouble(values, "some", "avg60"),
            ReadDouble(values, "some", "avg300"),
            ReadLong(values, "some", "total"),
            ReadDouble(values, "full", "avg10"),
            ReadDouble(values, "full", "avg60"),
            ReadDouble(values, "full", "avg300"),
            ReadLong(values, "full", "total"));
    }

    private static IReadOnlyDictionary<string, string> ParsePressureValues(IEnumerable<string> fields)
        => fields.Select(field => field.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static long? ReadValue(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static double? ReadDouble(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values, string category, string key)
        => values.TryGetValue(category, out var categoryValues)
            && categoryValues.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static long? ReadLong(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values, string category, string key)
        => values.TryGetValue(category, out var categoryValues)
            && categoryValues.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
}

/// <summary>Reads only the host pressure signals used by the one-second sentry.</summary>
internal sealed class LinuxHostPressureReader
{
    private readonly string _procRoot;

    public LinuxHostPressureReader(string procRoot = "/proc")
    {
        _procRoot = procRoot;
    }

    public WorkerHostPressureSnapshot? TryRead()
    {
        try
        {
            var memory = LinuxProcessMemoryReader.ReadKilobyteValues(Path.Combine(_procRoot, "meminfo"));
            if (memory.Count == 0)
            {
                return null;
            }

            var processCount = Directory.EnumerateDirectories(_procRoot)
                .Count(path => int.TryParse(Path.GetFileName(path), NumberStyles.None, CultureInfo.InvariantCulture, out _));
            var pressure = LinuxHostMemoryReader.TryReadPressure(Path.Combine(_procRoot, "pressure", "memory"));
            return new WorkerHostPressureSnapshot(
                memory.TryGetValue("MemAvailable", out var available) ? available : null,
                pressure?.SomeAverage10,
                processCount);
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
}
