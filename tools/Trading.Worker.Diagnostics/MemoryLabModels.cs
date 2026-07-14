namespace Trading.Worker.Diagnostics;

internal sealed record MemoryLabSample(
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedMemoryBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    long? CgroupCurrentBytes,
    long? CgroupPeakBytes = null);

internal sealed class MemoryLabSampleAccumulator
{
    private MemoryLabSample? _baseline;
    private long _peakWorkingSetBytes;
    private long _peakPrivateMemoryBytes;
    private long _peakManagedMemoryBytes;
    private long _peakHeapSizeBytes;
    private long _peakFragmentedBytes;
    private long? _peakCgroupCurrentBytes;
    private long? _peakCgroupMemoryBytes;

    public void Add(MemoryLabSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _baseline ??= sample;
        _peakWorkingSetBytes = Math.Max(_peakWorkingSetBytes, sample.WorkingSetBytes);
        _peakPrivateMemoryBytes = Math.Max(_peakPrivateMemoryBytes, sample.PrivateMemoryBytes);
        _peakManagedMemoryBytes = Math.Max(_peakManagedMemoryBytes, sample.ManagedMemoryBytes);
        _peakHeapSizeBytes = Math.Max(_peakHeapSizeBytes, sample.HeapSizeBytes);
        _peakFragmentedBytes = Math.Max(_peakFragmentedBytes, sample.FragmentedBytes);
        _peakCgroupCurrentBytes = Max(_peakCgroupCurrentBytes, sample.CgroupCurrentBytes);
        _peakCgroupMemoryBytes = Max(_peakCgroupMemoryBytes, sample.CgroupPeakBytes ?? sample.CgroupCurrentBytes);
    }

    public SyntheticMemoryLabSummary Build(
        TimeSpan duration,
        long retainedBytes,
        long churnAllocatedBytes,
        bool usesServerGarbageCollection)
    {
        if (_baseline is null)
        {
            throw new InvalidOperationException("At least one memory sample is required before building the lab summary.");
        }

        return new SyntheticMemoryLabSummary(
            DateTimeOffset.UtcNow,
            duration,
            retainedBytes,
            churnAllocatedBytes,
            _baseline.WorkingSetBytes,
            _baseline.PrivateMemoryBytes,
            _baseline.ManagedMemoryBytes,
            _baseline.HeapSizeBytes,
            _peakWorkingSetBytes,
            _peakPrivateMemoryBytes,
            _peakManagedMemoryBytes,
            _peakHeapSizeBytes,
            _peakFragmentedBytes,
            _peakCgroupCurrentBytes,
            _peakCgroupMemoryBytes,
            usesServerGarbageCollection);
    }

    private static long? Max(long? left, long? right)
        => left is null ? right : right is null ? left : Math.Max(left.Value, right.Value);
}

public sealed record SyntheticMemoryLabSummary(
    DateTimeOffset ObservedAtUtc,
    TimeSpan Duration,
    long RetainedBytes,
    long ChurnAllocatedBytes,
    long BaselineWorkingSetBytes,
    long BaselinePrivateMemoryBytes,
    long BaselineManagedMemoryBytes,
    long BaselineHeapSizeBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateMemoryBytes,
    long PeakManagedMemoryBytes,
    long PeakHeapSizeBytes,
    long PeakFragmentedBytes,
    long? PeakCgroupCurrentBytes,
    long? PeakCgroupMemoryBytes,
    bool UsesServerGarbageCollection);
