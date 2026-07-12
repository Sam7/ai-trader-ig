namespace Trading.Automation.Configuration;

public sealed class WorkerHealthOptions
{
    public const string SectionName = "WorkerHealth";

    public bool Enabled { get; init; } = true;

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    public string LocalDirectory { get; init; } = Path.Combine("Logs", "Health");

    public string GcsObjectName { get; init; } = "market-data/health/worker-status.json";

    public long WarningWorkingSetBytes { get; init; } = 400L * 1024 * 1024;

    public long CriticalWorkingSetBytes { get; init; } = 520L * 1024 * 1024;

    public long FailFastWorkingSetBytes { get; init; } = 580L * 1024 * 1024;

    public bool FailFastEnabled { get; init; }

    public int CriticalSampleCount { get; init; } = 3;

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker health interval must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(LocalDirectory))
        {
            throw new InvalidOperationException("Worker health local directory is required.");
        }

        if (WarningWorkingSetBytes <= 0 || CriticalWorkingSetBytes <= 0 || FailFastWorkingSetBytes <= 0)
        {
            throw new InvalidOperationException("Worker health memory thresholds must be greater than zero.");
        }

        if (WarningWorkingSetBytes >= CriticalWorkingSetBytes || CriticalWorkingSetBytes >= FailFastWorkingSetBytes)
        {
            throw new InvalidOperationException("Worker health memory thresholds must be ordered warning < critical < fail-fast.");
        }

        if (CriticalSampleCount <= 0)
        {
            throw new InvalidOperationException("Worker health critical sample count must be greater than zero.");
        }
    }
}
