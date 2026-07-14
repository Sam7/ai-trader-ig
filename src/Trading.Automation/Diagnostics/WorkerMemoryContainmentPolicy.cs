using Trading.Automation.Configuration;

namespace Trading.Automation.Diagnostics;

internal sealed record WorkerMemoryContainmentAssessment(
    int ConsecutiveSamples,
    bool ShouldExit);

internal static class WorkerMemoryContainmentPolicy
{
    public static WorkerMemoryContainmentAssessment Assess(
        long? cgroupCurrentBytes,
        WorkerDiagnosticsContainmentOptions options,
        int previousSustainedSamples)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (previousSustainedSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousSustainedSamples));
        }

        if (!options.Enabled || cgroupCurrentBytes is null || cgroupCurrentBytes < options.ExitCgroupBytes)
        {
            return new WorkerMemoryContainmentAssessment(0, false);
        }

        var consecutiveSamples = previousSustainedSamples + 1;
        return new WorkerMemoryContainmentAssessment(
            consecutiveSamples,
            consecutiveSamples >= options.SustainedSamples);
    }
}
