using FluentAssertions;
using Trading.Automation.Configuration;
using Trading.Automation.Diagnostics;

public sealed class WorkerDiagnosticPressurePolicyTests
{
    [Fact]
    public void Assess_should_enter_pressure_mode_when_the_worker_reaches_the_warning_threshold()
    {
        var policy = new WorkerDiagnosticPressurePolicy(CreateOptions());
        var observedAt = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

        var assessment = policy.Assess(new WorkerDiagnosticsSentrySample(
            observedAt,
            WorkingSetBytes: 1,
            CgroupCurrentBytes: 256L * 1024 * 1024,
            HighEvents: 0,
            MaxEvents: 0,
            OomEvents: 0,
            OomKillEvents: 0));

        assessment.IsPressureMode.Should().BeTrue();
        assessment.Reasons.Should().ContainSingle().Which.Should().Be(DiagnosticPressureReason.WorkerCgroup);
    }

    [Fact]
    public void Assess_should_hold_pressure_mode_for_the_configured_cooldown_after_all_signals_clear()
    {
        var policy = new WorkerDiagnosticPressurePolicy(CreateOptions());
        var enteredAt = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);
        policy.Assess(CreateSample(enteredAt, cgroupBytes: 256L * 1024 * 1024));

        var duringCooldown = policy.Assess(CreateSample(enteredAt.AddMinutes(4), cgroupBytes: 1));
        var afterCooldown = policy.Assess(CreateSample(enteredAt.AddMinutes(9).AddSeconds(1), cgroupBytes: 1));

        duringCooldown.IsPressureMode.Should().BeTrue();
        afterCooldown.IsPressureMode.Should().BeFalse();
    }

    [Fact]
    public void Assess_should_detect_material_external_process_growth_after_recording_a_baseline()
    {
        var policy = new WorkerDiagnosticPressurePolicy(CreateOptions());
        var observedAt = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

        policy.Assess(CreateSample(observedAt, cgroupBytes: 1, processCount: 10));
        var assessment = policy.Assess(CreateSample(observedAt.AddSeconds(1), cgroupBytes: 1, processCount: 14));

        assessment.IsPressureMode.Should().BeTrue();
        assessment.Reasons.Should().ContainSingle().Which.Should().Be(DiagnosticPressureReason.ExternalProcessGrowth);
    }

    [Fact]
    public void Assess_should_keep_the_initial_warmed_process_baseline_when_transient_processes_exit()
    {
        var policy = new WorkerDiagnosticPressurePolicy(CreateOptions());
        var observedAt = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

        policy.Assess(CreateSample(observedAt, cgroupBytes: 1, processCount: 10));
        policy.Assess(CreateSample(observedAt.AddSeconds(1), cgroupBytes: 1, processCount: 6));
        var assessment = policy.Assess(CreateSample(observedAt.AddSeconds(2), cgroupBytes: 1, processCount: 10));

        assessment.IsPressureMode.Should().BeFalse();
    }

    private static WorkerDiagnosticsPressureOptions CreateOptions()
        => new()
        {
            WorkerCgroupWarningBytes = 256L * 1024 * 1024,
            HostAvailableWarningBytes = 256L * 1024 * 1024,
            ExternalProcessCountGrowth = 4,
            Cooldown = TimeSpan.FromMinutes(5),
        };

    private static WorkerDiagnosticsSentrySample CreateSample(DateTimeOffset observedAt, long cgroupBytes, int? processCount = null)
        => new(observedAt, 1, cgroupBytes, 0, 0, 0, 0)
        {
            HostPressure = new WorkerHostPressureSnapshot(
                AvailableBytes: 512L * 1024 * 1024,
                MemoryPressureSomeAverage10: 0,
                ProcessCount: processCount),
        };
}
