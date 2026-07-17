using Trading.Automation.Configuration;

namespace Trading.Automation.Diagnostics;

internal enum DiagnosticPressureReason
{
    WorkerCgroup,
    HostAvailableMemory,
    MemoryPressureStall,
    ExternalProcessGrowth,
}

internal sealed record WorkerDiagnosticPressureAssessment(
    bool IsPressureMode,
    IReadOnlyList<DiagnosticPressureReason> Reasons);

/// <summary>Keeps adaptive sampling state without altering worker lifecycle or memory limits.</summary>
internal sealed class WorkerDiagnosticPressurePolicy
{
    private readonly WorkerDiagnosticsPressureOptions _options;
    private int? _baselineProcessCount;
    private bool _isPressureMode;
    private DateTimeOffset? _belowAllThresholdsSinceUtc;

    public WorkerDiagnosticPressurePolicy(WorkerDiagnosticsPressureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public WorkerDiagnosticPressureAssessment Assess(WorkerDiagnosticsSentrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var host = sample.HostPressure;
        if (host?.ProcessCount is { } processCount)
        {
            _baselineProcessCount ??= processCount;
        }

        var reasons = new List<DiagnosticPressureReason>(4);
        if (sample.CgroupCurrentBytes >= _options.WorkerCgroupWarningBytes)
        {
            reasons.Add(DiagnosticPressureReason.WorkerCgroup);
        }

        if (host?.AvailableBytes < _options.HostAvailableWarningBytes)
        {
            reasons.Add(DiagnosticPressureReason.HostAvailableMemory);
        }

        if (host?.MemoryPressureSomeAverage10 > 0)
        {
            reasons.Add(DiagnosticPressureReason.MemoryPressureStall);
        }

        if (host?.ProcessCount is { } currentProcessCount
            && _baselineProcessCount is { } baselineProcessCount
            && currentProcessCount >= baselineProcessCount + _options.ExternalProcessCountGrowth)
        {
            reasons.Add(DiagnosticPressureReason.ExternalProcessGrowth);
        }

        if (reasons.Count > 0)
        {
            _isPressureMode = true;
            _belowAllThresholdsSinceUtc = null;
            return new WorkerDiagnosticPressureAssessment(true, reasons);
        }

        if (!_isPressureMode)
        {
            return new WorkerDiagnosticPressureAssessment(false, []);
        }

        _belowAllThresholdsSinceUtc ??= sample.ObservedAtUtc;
        if (sample.ObservedAtUtc - _belowAllThresholdsSinceUtc.Value >= _options.Cooldown)
        {
            _isPressureMode = false;
            _belowAllThresholdsSinceUtc = null;
        }

        return new WorkerDiagnosticPressureAssessment(_isPressureMode, []);
    }
}
