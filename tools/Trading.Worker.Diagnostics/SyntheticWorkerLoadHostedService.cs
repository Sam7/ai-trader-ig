using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trading.Worker.Diagnostics;

internal sealed class SyntheticWorkerLoadHostedService : BackgroundService
{
    private const int BytesPerMegabyte = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SyntheticWorkerLoadOptions _options;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<SyntheticWorkerLoadHostedService> _logger;

    public SyntheticWorkerLoadHostedService(
        IOptions<SyntheticWorkerLoadOptions> options,
        IHostApplicationLifetime applicationLifetime,
        ILogger<SyntheticWorkerLoadHostedService> logger)
    {
        _options = options.Value;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var samples = new MemoryLabSampleAccumulator();
        var retained = _options.Enabled ? AllocateMegabytes(_options.RetainedMegabytes) : [];
        var retainedBytes = (long)retained.Count * BytesPerMegabyte;
        var churnAllocatedBytes = 0L;
        var nextBurstUtc = startedAtUtc.Add(_options.BurstInterval);

        try
        {
            CaptureSample(samples);
            while (stopwatch.Elapsed < _options.Duration)
            {
                if (_options.Enabled && _options.ChurnMegabytesPerInterval > 0)
                {
                    var churn = AllocateMegabytes(_options.ChurnMegabytesPerInterval);
                    churnAllocatedBytes += (long)churn.Count * BytesPerMegabyte;
                    CaptureSample(samples);
                    GC.KeepAlive(churn);
                }

                if (_options.Enabled
                    && _options.BurstMegabytes > 0
                    && DateTimeOffset.UtcNow >= nextBurstUtc)
                {
                    var burst = AllocateMegabytes(_options.BurstMegabytes);
                    CaptureSample(samples);
                    if (_options.BurstHold > TimeSpan.Zero)
                    {
                        await Task.Delay(_options.BurstHold, stoppingToken).ConfigureAwait(false);
                    }

                    GC.KeepAlive(burst);
                    nextBurstUtc = DateTimeOffset.UtcNow.Add(_options.BurstInterval);
                }

                CaptureSample(samples);
                await Task.Delay(_options.AllocationInterval, stoppingToken).ConfigureAwait(false);
            }

            GC.KeepAlive(retained);
            CaptureSample(samples);
            var summary = samples.Build(
                stopwatch.Elapsed,
                retainedBytes,
                churnAllocatedBytes,
                System.Runtime.GCSettings.IsServerGC);
            await WriteSummaryAsync(summary, stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Synthetic worker memory lab completed in {Duration}. Peak working set: {PeakWorkingSetBytes}. Peak cgroup memory: {PeakCgroupMemoryBytes}.",
                summary.Duration,
                summary.PeakWorkingSetBytes,
                summary.PeakCgroupMemoryBytes);
        }
        finally
        {
            _applicationLifetime.StopApplication();
        }
    }

    private void CaptureSample(MemoryLabSampleAccumulator samples)
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        var cgroup = TryReadCgroupMemory();
        samples.Add(new MemoryLabSample(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            cgroup.CurrentBytes,
            cgroup.PeakBytes));
    }

    private async Task WriteSummaryAsync(SyntheticMemoryLabSummary summary, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(_options.ResultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(summary, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<byte[]> AllocateMegabytes(int megabytes)
    {
        var buffers = new List<byte[]>(megabytes);
        for (var index = 0; index < megabytes; index++)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(BytesPerMegabyte);
            for (var offset = 0; offset < buffer.Length; offset += 4096)
            {
                buffer[offset] = unchecked((byte)offset);
            }

            buffers.Add(buffer);
        }

        return buffers;
    }

    private static CgroupMemory TryReadCgroupMemory()
    {
        try
        {
            var relativePath = File.ReadLines("/proc/self/cgroup")
                .Select(line => line.Split(':', 3))
                .Where(parts => parts.Length == 3 && string.IsNullOrEmpty(parts[1]))
                .Select(parts => parts[2])
                .FirstOrDefault();
            if (relativePath is null)
            {
                return new CgroupMemory(null, null);
            }

            var path = "/sys/fs/cgroup" + (relativePath == "/" ? string.Empty : relativePath);
            return new CgroupMemory(
                ReadLong(Path.Combine(path, "memory.current")),
                ReadLong(Path.Combine(path, "memory.peak")));
        }
        catch (IOException)
        {
            return new CgroupMemory(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new CgroupMemory(null, null);
        }
    }

    private static long? ReadLong(string path)
        => File.Exists(path)
           && long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private sealed record CgroupMemory(long? CurrentBytes, long? PeakBytes);
}
