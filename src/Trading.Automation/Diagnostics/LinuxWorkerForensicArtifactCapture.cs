using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Trading.Automation.Configuration;

namespace Trading.Automation.Diagnostics;

internal interface IWorkerForensicArtifactCapture
{
    Task CaptureAsync(
        WorkerDiagnosticSnapshot snapshot,
        long thresholdBytes,
        CancellationToken cancellationToken = default);
}

internal sealed class NoOpWorkerForensicArtifactCapture : IWorkerForensicArtifactCapture
{
    public Task CaptureAsync(WorkerDiagnosticSnapshot snapshot, long thresholdBytes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Writes bounded, compressed Linux evidence without ever reading command lines or environments.</summary>
internal sealed class LinuxWorkerForensicArtifactCapture : IWorkerForensicArtifactCapture
{
    private const int MaximumRawArtifactBytes = 4 * 1024 * 1024;
    private readonly string _localDirectory;
    private readonly int _processId;
    private readonly string _procRoot;
    private readonly string _cgroupDirectory;

    public LinuxWorkerForensicArtifactCapture(WorkerDiagnosticsOptions options)
        : this(options, Environment.ProcessId, "/proc", ResolveCgroupDirectory("/proc", "/sys/fs/cgroup"))
    {
    }

    internal LinuxWorkerForensicArtifactCapture(
        WorkerDiagnosticsOptions options,
        int processId,
        string procRoot,
        string cgroupDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentException.ThrowIfNullOrWhiteSpace(procRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(cgroupDirectory);

        _localDirectory = Path.GetFullPath(options.LocalDirectory);
        _processId = processId;
        _procRoot = procRoot;
        _cgroupDirectory = cgroupDirectory;
    }

    public async Task CaptureAsync(
        WorkerDiagnosticSnapshot snapshot,
        long thresholdBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdBytes);

        Directory.CreateDirectory(_localDirectory);
        var stamp = snapshot.ObservedAtUtc.ToUniversalTime().ToString("yyyyMMddTHHmmss.fffffffZ");
        var name = $"forensic-{stamp}-{_processId}-{thresholdBytes / (1024 * 1024)}m";
        var processDirectory = Path.Combine(_procRoot, _processId.ToString());

        await WriteRawFilesAsync(
            Path.Combine(_localDirectory, $"{name}-smaps.txt.gz"),
            [
                ("smaps", Path.Combine(processDirectory, "smaps")),
                ("smaps_rollup", Path.Combine(processDirectory, "smaps_rollup")),
            ],
            cancellationToken).ConfigureAwait(false);
        await WriteRawFilesAsync(
            Path.Combine(_localDirectory, $"{name}-maps.txt.gz"),
            [("maps", Path.Combine(processDirectory, "maps"))],
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(_localDirectory, $"{name}-fds.json.gz"),
            new { processId = _processId, descriptors = ClassifyFileDescriptors(processDirectory) },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(_localDirectory, $"{name}-cgroup.json.gz"),
            new
            {
                memoryCurrentBytes = ReadNumericFile("memory.current"),
                memoryPeakBytes = ReadNumericFile("memory.peak"),
                memorySwapCurrentBytes = ReadNumericFile("memory.swap.current"),
                memorySwapPeakBytes = ReadNumericFile("memory.swap.peak"),
                memoryStat = ReadBoundedText("memory.stat"),
                memoryEvents = ReadBoundedText("memory.events"),
                memoryPressure = ReadBoundedText("memory.pressure"),
            },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(_localDirectory, $"{name}-host-activity.json.gz"),
            new
            {
                schemaVersion = snapshot.SchemaVersion,
                observedAtUtc = snapshot.ObservedAtUtc,
                thresholdBytes,
                host = new LinuxHostMemoryReader(_procRoot).TryRead(),
                operation = snapshot.Operations,
                marketDataActivity = snapshot.Activity,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteRawFilesAsync(
        string destination,
        IReadOnlyList<(string Name, string Path)> sources,
        CancellationToken cancellationToken)
    {
        await using var output = CreateOutput(destination);
        {
            await using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            foreach (var (name, path) in sources)
            {
                var heading = Encoding.UTF8.GetBytes($"[{name}]\n");
                await gzip.WriteAsync(heading, cancellationToken).ConfigureAwait(false);
                await CopyBoundedFileAsync(path, gzip, cancellationToken).ConfigureAwait(false);
                await gzip.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }

            await gzip.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        output.Flush(flushToDisk: true);
    }

    private static async Task CopyBoundedFileAsync(string path, Stream destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            var remaining = MaximumRawArtifactBytes;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task WriteJsonAsync(string destination, object value, CancellationToken cancellationToken)
    {
        await using var output = CreateOutput(destination);
        {
            await using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            await JsonSerializer.SerializeAsync(gzip, value, cancellationToken: cancellationToken).ConfigureAwait(false);
            await gzip.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        output.Flush(flushToDisk: true);
    }

    private FileStream CreateOutput(string destination)
        => new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

    private IReadOnlyDictionary<string, int> ClassifyFileDescriptors(string processDirectory)
    {
        var directory = Path.Combine(processDirectory, "fd");
        if (!Directory.Exists(directory))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var kind = "other";
            try
            {
                var target = new FileInfo(path).LinkTarget;
                kind = target?.StartsWith("socket:[", StringComparison.Ordinal) == true ? "socket"
                    : target?.StartsWith("pipe:[", StringComparison.Ordinal) == true ? "pipe"
                    : target?.StartsWith("anon_inode:", StringComparison.Ordinal) == true ? "anon_inode"
                    : target?.StartsWith("/", StringComparison.Ordinal) == true ? "file"
                    : "other";
            }
            catch (IOException)
            {
                // Descriptors are expected to disappear during a pressure capture.
            }

            counts[kind] = counts.TryGetValue(kind, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private long? ReadNumericFile(string fileName)
        => long.TryParse(ReadBoundedText(fileName)?.Trim(), out var value) ? value : null;

    private string? ReadBoundedText(string fileName)
    {
        var path = Path.Combine(_cgroupDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan));
        var buffer = new char[32 * 1024];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static string ResolveCgroupDirectory(string procRoot, string cgroupRoot)
    {
        var selfCgroup = Path.Combine(procRoot, "self", "cgroup");
        if (!File.Exists(selfCgroup))
        {
            return cgroupRoot;
        }

        var relative = File.ReadLines(selfCgroup)
            .Select(line => line.Split(':', 3))
            .Where(parts => parts.Length == 3 && string.IsNullOrEmpty(parts[1]))
            .Select(parts => parts[2])
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(relative) || relative == "/"
            ? cgroupRoot
            : Path.Combine(cgroupRoot, relative.TrimStart('/'));
    }
}
