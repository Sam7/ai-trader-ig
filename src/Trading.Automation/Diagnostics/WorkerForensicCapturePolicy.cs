namespace Trading.Automation.Diagnostics;

/// <summary>One-shot memory thresholds that produce durable, local evidence without changing the worker's behavior.</summary>
internal static class WorkerForensicCapturePolicy
{
    public static readonly IReadOnlyList<long> ThresholdBytes =
    [
        256L * 1024 * 1024,
        320L * 1024 * 1024,
        384L * 1024 * 1024,
    ];

    public static IReadOnlyList<long> GetNewCrossings(long? cgroupCurrentBytes, ISet<long> capturedThresholds)
    {
        ArgumentNullException.ThrowIfNull(capturedThresholds);
        if (cgroupCurrentBytes is null)
        {
            return [];
        }

        var crossings = new List<long>(ThresholdBytes.Count);
        foreach (var threshold in ThresholdBytes)
        {
            if (cgroupCurrentBytes >= threshold && capturedThresholds.Add(threshold))
            {
                crossings.Add(threshold);
            }
        }

        return crossings;
    }
}
