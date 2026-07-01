using FluentAssertions;
using Trading.Abstractions;
using Trading.Automation.Execution;

public sealed class PaperMarketAssessmentEvaluatorTests
{
    [Fact]
    public void Evaluate_ShouldRecordFollowedBiasWhenPriceMovesWithDirection()
    {
        var evaluator = new PaperMarketAssessmentEvaluator();
        var assessment = new DecisionAuditAssessment(
            new InstrumentId("CC.D.TEST.IP"),
            "Test Market",
            72,
            TradeDirection.Buy,
            "Constructive",
            "Momentum improved",
            "");
        var series = new PriceSeries(
            assessment.Instrument,
            PriceResolution.FiveMinutes,
            [
                Bar("2026-03-12T10:00:00Z", bidLow: 99m, bidHigh: 101m, bidClose: 100m, askLow: 99.2m, askHigh: 101.2m, askClose: 100.2m),
                Bar("2026-03-12T11:00:00Z", bidLow: 104m, bidHigh: 106m, bidClose: 105m, askLow: 104.2m, askHigh: 106.2m, askClose: 105.2m),
            ]);

        var outcome = evaluator.Evaluate(
            assessment,
            DateTimeOffset.Parse("2026-03-12T10:00:00Z"),
            DateTimeOffset.Parse("2026-03-12T11:00:00Z"),
            series,
            DateTimeOffset.Parse("2026-03-12T11:05:00Z"));

        outcome.Status.Should().Be(PaperMarketAssessmentOutcomeStatus.FollowedBias);
        outcome.DirectionalMove.Should().Be(5m);
        outcome.MaxFavorableExcursion.Should().Be(6m);
        outcome.MaxAdverseExcursion.Should().Be(1m);
    }

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
