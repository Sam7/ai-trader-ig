namespace Trading.Automation.Configuration;

/// <summary>
/// Controls bounded, local-first worker memory diagnostics.
/// </summary>
public sealed class WorkerDiagnosticsOptions
{
    public const string SectionName = "WorkerDiagnostics";

    public bool Enabled { get; init; }

    public TimeSpan SentryInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(30);

    public string LocalDirectory { get; init; } = Path.Combine("Logs", "Diagnostics");

    public long SegmentMaximumBytes { get; init; } = 8L * 1024 * 1024;

    public long RetentionMaximumBytes { get; init; } = 24L * 1024 * 1024;

    public bool UploadClosedSegments { get; init; } = true;

    public string GcsPrefix { get; init; } = "market-data/diagnostics";

    public TimeSpan ArtifactUploadInterval { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ArtifactUploadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public WorkerDiagnosticsPressureOptions Pressure { get; init; } = new();

    public WorkerDiagnosticsContainmentOptions Containment { get; init; } = new();

    public void Validate()
    {
        if (SentryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker diagnostics sentry interval must be greater than zero.");
        }

        if (SampleInterval < SentryInterval)
        {
            throw new InvalidOperationException("Worker diagnostics sample interval must not be shorter than the sentry interval.");
        }

        if (FlushInterval < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker diagnostics flush interval cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(LocalDirectory))
        {
            throw new InvalidOperationException("Worker diagnostics local directory is required.");
        }

        if (SegmentMaximumBytes <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics segment maximum bytes must be greater than zero.");
        }

        if (RetentionMaximumBytes < SegmentMaximumBytes * 2)
        {
            throw new InvalidOperationException("Worker diagnostics retention must hold an active and a closed segment.");
        }

        if (UploadClosedSegments && string.IsNullOrWhiteSpace(GcsPrefix.Trim('/')))
        {
            throw new InvalidOperationException("Worker diagnostics GCS prefix is required when upload is enabled.");
        }

        if (ArtifactUploadInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker diagnostics artifact upload interval must be greater than zero.");
        }

        if (ArtifactUploadTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker diagnostics artifact upload timeout must be greater than zero.");
        }

        Pressure.Validate();
        Containment.Validate();
    }
}

/// <summary>Controls adaptive evidence collection; it never restarts or constrains the worker.</summary>
public sealed class WorkerDiagnosticsPressureOptions
{
    public long WorkerCgroupWarningBytes { get; init; } = 256L * 1024 * 1024;

    public long HostAvailableWarningBytes { get; init; } = 256L * 1024 * 1024;

    public int ExternalProcessCountGrowth { get; init; } = 8;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (WorkerCgroupWarningBytes <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics cgroup pressure threshold must be greater than zero.");
        }

        if (HostAvailableWarningBytes <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics host-available pressure threshold must be greater than zero.");
        }

        if (ExternalProcessCountGrowth <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics external process growth threshold must be greater than zero.");
        }

        if (Cooldown < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker diagnostics pressure cooldown cannot be negative.");
        }
    }
}

public sealed class WorkerDiagnosticsContainmentOptions
{
    public bool Enabled { get; init; }

    public long ExitCgroupBytes { get; init; } = 352L * 1024 * 1024;

    public int SustainedSamples { get; init; } = 3;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (ExitCgroupBytes <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics containment threshold must be greater than zero.");
        }

        if (SustainedSamples <= 0)
        {
            throw new InvalidOperationException("Worker diagnostics containment sustained samples must be greater than zero.");
        }
    }
}
