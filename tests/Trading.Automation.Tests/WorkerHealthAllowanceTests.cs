using FluentAssertions;
using Trading.Abstractions;
using Trading.Automation.Health;
using Trading.MarketData;

public sealed class WorkerHealthAllowanceTests
{
    [Fact]
    public void FormatAllowanceReason_should_show_exact_expiry()
    {
        var health = new MarketDataRecoveryHealth(
            PendingRanges: 10,
            BlockedRanges: 1,
            RemainingAllowance: 0,
            AllowanceExpiresAtUtc: DateTimeOffset.Parse("2026-07-17T07:03:27Z"),
            ActiveInstrument: "CC.D.UMA.UMA.IP");

        WorkerHealthReporterHostedService.FormatAllowanceReason(health)
            .Should().Be("Historical recovery is blocked by IG allowance until 2026-07-17T07:03:27.0000000+00:00.");
    }

    [Fact]
    public void FormatAllowanceReason_should_label_fallback_expiry_as_estimated()
    {
        var health = new MarketDataRecoveryHealth(
            10,
            1,
            0,
            DateTimeOffset.Parse("2026-07-17T07:03:27Z"),
            "CC.D.UMA.UMA.IP")
        {
            AllowanceExpiryEstimated = true,
        };

        WorkerHealthReporterHostedService.FormatAllowanceReason(health)
            .Should().Contain("approximately 2026-07-17T07:03:27.0000000+00:00")
            .And.Contain("estimated")
            .And.NotContain("until .");
    }

    [Fact]
    public void FormatAllowanceReason_should_explain_when_expiry_is_unavailable()
    {
        var health = new MarketDataRecoveryHealth(10, 1, null, null, "CC.D.UMA.UMA.IP");

        WorkerHealthReporterHostedService.FormatAllowanceReason(health)
            .Should().Be("Historical recovery is blocked by IG allowance; IG did not provide a reset time.");
    }

    [Fact]
    public void BuildRecoveryHealth_should_choose_the_blocked_range_over_an_unblocked_pending_range()
    {
        var now = DateTimeOffset.Parse("2026-07-17T06:22:00Z");
        var pendingWithoutExpiry = new MarketDataRecoveryState(
            new InstrumentId("CC.D.C.UMA.IP"),
            PriceResolution.FiveMinutes,
            now.AddDays(-1),
            now,
            now.AddDays(-1),
            false,
            0,
            null,
            null,
            null);
        var blocked = new MarketDataRecoveryState(
            new InstrumentId("CC.D.UMA.UMA.IP"),
            PriceResolution.FiveMinutes,
            now.AddDays(-1),
            now,
            now.AddDays(-1),
            false,
            0,
            0,
            now.AddHours(1),
            "IG API error: exceeded historical allowance");

        var health = WorkerHealthReporterHostedService.BuildRecoveryHealth(
            [pendingWithoutExpiry, blocked],
            now);

        health.BlockedRanges.Should().Be(1);
        health.ActiveInstrument.Should().Be("CC.D.UMA.UMA.IP");
        health.AllowanceExpiresAtUtc.Should().Be(now.AddHours(1));
        health.AllowanceExpiryEstimated.Should().BeTrue();
    }

    [Fact]
    public void BuildRecoveryHealth_should_report_global_allowance_block_for_historical_work_only()
    {
        var now = DateTimeOffset.Parse("2026-07-22T00:00:00Z");
        var recent = new MarketDataRecoveryWorkItem(
            new InstrumentId("CS.D.BITCOIN.CFD.IP"), PriceResolution.FiveMinutes,
            MarketDataRecoveryReason.RecentTail, 0, now.AddHours(-1), now, now.AddHours(-1),
            MarketDataRecoveryWorkStatus.Pending, now, 0, 0);
        var historical = new MarketDataRecoveryWorkItem(
            new InstrumentId("CS.D.GBPUSD.CFD.IP"), PriceResolution.FiveMinutes,
            MarketDataRecoveryReason.HistoricalAudit, 1000, now.AddDays(-14), now.AddDays(-1), now.AddDays(-14),
            MarketDataRecoveryWorkStatus.Pending, now, 0, 0);
        var budget = new HistoricalAllowanceBudget(0, now.AddHours(3), now, now.AddHours(3), ResetEstimated: true);

        var health = WorkerHealthReporterHostedService.BuildRecoveryHealth([recent, historical], budget, now);

        health.PendingRanges.Should().Be(2);
        health.RecentPendingRanges.Should().Be(1);
        health.HistoricalPendingRanges.Should().Be(1);
        health.AllowanceBlockedRanges.Should().Be(1);
        health.PermanentlyBlockedRanges.Should().Be(0);
        health.ActiveInstrument.Should().Be("CS.D.GBPUSD.CFD.IP");
        health.AllowanceExpiryEstimated.Should().BeTrue();
    }
}
