using Trading.Automation.Configuration;

namespace Trading.Automation.Health;

public sealed record WorkerMemoryAssessment(
    WorkerHealthStatus Status,
    string? Reason,
    int ConsecutiveCriticalSamples,
    bool ShouldFailFast);

public static class WorkerMemoryPolicy
{
    public static WorkerMemoryAssessment Assess(
        long workingSetBytes,
        WorkerHealthOptions options,
        int previousCriticalSamples)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (previousCriticalSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousCriticalSamples));
        }

        var status = WorkerHealthStatus.Healthy;
        string? reason = null;
        if (workingSetBytes >= options.CriticalWorkingSetBytes)
        {
            status = WorkerHealthStatus.Critical;
            reason = $"Working set is critical: {workingSetBytes} bytes.";
        }
        else if (workingSetBytes >= options.WarningWorkingSetBytes)
        {
            status = WorkerHealthStatus.Warning;
            reason = $"Working set is elevated: {workingSetBytes} bytes.";
        }

        var consecutiveCriticalSamples = workingSetBytes >= options.CriticalWorkingSetBytes
            ? previousCriticalSamples + 1
            : 0;
        var shouldFailFast = options.FailFastEnabled
            && consecutiveCriticalSamples >= options.CriticalSampleCount
            && workingSetBytes >= options.FailFastWorkingSetBytes;

        return new WorkerMemoryAssessment(
            status,
            reason,
            consecutiveCriticalSamples,
            shouldFailFast);
    }
}
