using System.Globalization;

namespace Trading.Automation.Diagnostics;

/// <summary>Reads cgroup v2 memory accounting without retaining process payloads.</summary>
internal sealed class LinuxCgroupMemoryReader : IWorkerCgroupMemoryReader
{
    private readonly string _cgroupRoot;
    private readonly string _procSelfCgroupPath;

    public LinuxCgroupMemoryReader(
        string cgroupRoot = "/sys/fs/cgroup",
        string procSelfCgroupPath = "/proc/self/cgroup")
    {
        _cgroupRoot = cgroupRoot;
        _procSelfCgroupPath = procSelfCgroupPath;
    }

    public CgroupMemorySnapshot? TryRead()
    {
        try
        {
            var path = ResolveCurrentCgroupPath();
            if (path is null)
            {
                return null;
            }

            var current = ReadLong(Path.Combine(path, "memory.current"));
            if (current is null)
            {
                return null;
            }

            var events = ReadKeyValues(Path.Combine(path, "memory.events"));
            var statistics = ReadKeyValues(Path.Combine(path, "memory.stat"));
            return new CgroupMemorySnapshot(
                current.Value,
                ReadLong(Path.Combine(path, "memory.peak")),
                ReadValue(statistics, "anon"),
                ReadValue(statistics, "file"),
                ReadValue(statistics, "kernel_stack"),
                ReadValue(statistics, "slab"),
                ReadValue(events, "high"),
                ReadValue(events, "max"),
                ReadValue(events, "oom"),
                ReadValue(events, "oom_kill"));
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

    private string? ResolveCurrentCgroupPath()
    {
        if (!File.Exists(_procSelfCgroupPath))
        {
            return null;
        }

        var relativePath = File.ReadLines(_procSelfCgroupPath)
            .Select(line => line.Split(':', 3))
            .Where(parts => parts.Length == 3 && string.IsNullOrEmpty(parts[1]))
            .Select(parts => parts[2])
            .FirstOrDefault();
        if (relativePath is null)
        {
            return null;
        }

        var segments = relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var path = _cgroupRoot;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static long? ReadLong(string path)
        => File.Exists(path)
            && long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

    private static IReadOnlyDictionary<string, long> ReadKeyValues(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values[parts[0]] = value;
            }
        }

        return values;
    }

    private static long? ReadValue(IReadOnlyDictionary<string, long> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;
}
