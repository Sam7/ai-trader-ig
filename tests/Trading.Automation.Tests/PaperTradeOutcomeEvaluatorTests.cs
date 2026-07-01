using FluentAssertions;
using Trading.Abstractions;
using Trading.Automation.Execution;
using Trading.Strategy.Shared;

public sealed class PaperTradeOutcomeEvaluatorTests
{
    private readonly PaperTradeOutcomeEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_ShouldRecordTargetHitForBuyMarketCandidate()
    {
        var candidate = CreateCandidate(
            TradeDirection.Buy,
            TradeEntryMethod.Market,
            entry: 100m,
            stop: 95m,
            target: 110m);
        var series = CreateSeries(
            Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 104m, bidClose: 102m, askLow: 99.2m, askHigh: 104.2m, askClose: 102.2m),
            Bar("2026-03-12T10:05:00Z", bidLow: 101m, bidHigh: 111m, bidClose: 110m, askLow: 101.2m, askHigh: 111.2m, askClose: 110.2m));

        var outcome = _evaluator.Evaluate(
            candidate,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T11:00:00Z"));

        outcome.Status.Should().Be(PaperTradeOutcomeStatus.TargetHit);
        outcome.EstimatedRMultiple.Should().Be(2m);
        outcome.FilledAtUtc.Should().Be(DateTimeOffset.Parse("2026-03-12T10:00:00Z"));
        outcome.ClosedAtUtc.Should().Be(DateTimeOffset.Parse("2026-03-12T10:05:00Z"));
        outcome.MaxFavorableExcursion.Should().Be(11m);
        outcome.MaxAdverseExcursion.Should().Be(1m);
    }

    [Fact]
    public void Evaluate_ShouldChooseStopWhenStopAndTargetTouchSameBar()
    {
        var candidate = CreateCandidate(
            TradeDirection.Buy,
            TradeEntryMethod.Market,
            entry: 100m,
            stop: 95m,
            target: 110m);
        var series = CreateSeries(
            Bar("2026-03-12T10:00:00Z", bidLow: 94m, bidHigh: 112m, bidClose: 101m, askLow: 94.2m, askHigh: 112.2m, askClose: 101.2m));

        var outcome = _evaluator.Evaluate(
            candidate,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T11:00:00Z"));

        outcome.Status.Should().Be(PaperTradeOutcomeStatus.StoppedOut);
        outcome.EstimatedRMultiple.Should().Be(-1m);
        outcome.Reason.Should().Contain("conservatively");
    }

    [Fact]
    public void Evaluate_ShouldRecordNoFillWhenLimitEntryIsNotTouched()
    {
        var candidate = CreateCandidate(
            TradeDirection.Buy,
            TradeEntryMethod.Limit,
            entry: 98m,
            stop: 95m,
            target: 104m);
        var series = CreateSeries(
            Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 102m, bidClose: 101m, askLow: 99.2m, askHigh: 102.2m, askClose: 101.2m),
            Bar("2026-03-12T10:05:00Z", bidLow: 100m, bidHigh: 103m, bidClose: 102m, askLow: 100.2m, askHigh: 103.2m, askClose: 102.2m),
            Bar("2026-03-12T10:30:00Z", bidLow: 101m, bidHigh: 103m, bidClose: 102m, askLow: 101.2m, askHigh: 103.2m, askClose: 102.2m));

        var outcome = _evaluator.Evaluate(
            candidate,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T11:00:00Z"));

        outcome.Status.Should().Be(PaperTradeOutcomeStatus.NoFill);
        outcome.FilledAtUtc.Should().BeNull();
        outcome.EstimatedRMultiple.Should().BeNull();
    }

    [Fact]
    public void Evaluate_ShouldNotCountNonMarketTargetTouchOnEntryBar()
    {
        var candidate = CreateCandidate(
            TradeDirection.Buy,
            TradeEntryMethod.Limit,
            entry: 98m,
            stop: 95m,
            target: 104m);
        var series = CreateSeries(
            Bar("2026-03-12T10:00:00Z", bidLow: 96m, bidHigh: 105m, bidClose: 100m, askLow: 97.8m, askHigh: 105.2m, askClose: 100.2m),
            Bar("2026-03-12T10:30:00Z", bidLow: 99m, bidHigh: 103m, bidClose: 101m, askLow: 99.2m, askHigh: 103.2m, askClose: 101.2m));

        var outcome = _evaluator.Evaluate(
            candidate,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T11:00:00Z"));

        outcome.Status.Should().Be(PaperTradeOutcomeStatus.Expired);
        outcome.Reason.Should().Contain("OHLC sequencing is unknown");
    }

    [Fact]
    public void Evaluate_ShouldKeepOutcomeDataInsufficientWhenBarsDoNotReachExpiry()
    {
        var candidate = CreateCandidate(
            TradeDirection.Buy,
            TradeEntryMethod.Market,
            entry: 100m,
            stop: 95m,
            target: 110m);
        var series = CreateSeries(
            Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 104m, bidClose: 102m, askLow: 99.2m, askHigh: 104.2m, askClose: 102.2m));

        var outcome = _evaluator.Evaluate(
            candidate,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T10:10:00Z"));

        outcome.Status.Should().Be(PaperTradeOutcomeStatus.DataInsufficient);
        outcome.Reason.Should().Contain("did not cover");
        outcome.FilledAtUtc.Should().Be(DateTimeOffset.Parse("2026-03-12T10:00:00Z"));
    }

    private static DecisionAuditCandidate CreateCandidate(
        TradeDirection direction,
        TradeEntryMethod entryMethod,
        decimal entry,
        decimal stop,
        decimal target)
        => new(
            new InstrumentId("CC.D.TEST.IP"),
            "Test Market",
            direction,
            72,
            entryMethod,
            entry,
            stop,
            target,
            Math.Abs(target - entry) / Math.Abs(entry - stop),
            entry,
            0.2m,
            "Thesis",
            "Invalidation",
            "Why now",
            DateTimeOffset.Parse("2026-03-12T10:30:00Z"));

    private static PriceSeries CreateSeries(params PriceBar[] bars)
        => new(new InstrumentId("CC.D.TEST.IP"), PriceResolution.FiveMinutes, bars);

    private static PriceBar Bar(
        string timestampUtc,
        decimal bidLow,
        decimal bidHigh,
        decimal bidClose,
        decimal askLow,
        decimal askHigh,
        decimal askClose)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            bidClose,
            bidHigh,
            bidLow,
            bidClose,
            askClose,
            askHigh,
            askLow,
            askClose,
            null);
}
