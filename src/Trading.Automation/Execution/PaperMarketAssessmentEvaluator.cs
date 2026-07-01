using Trading.Abstractions;

namespace Trading.Automation.Execution;

public sealed class PaperMarketAssessmentEvaluator
{
    public PaperMarketAssessmentOutcome Evaluate(
        DecisionAuditAssessment assessment,
        DateTimeOffset reviewedAtUtc,
        DateTimeOffset horizonEndsAtUtc,
        PriceSeries series,
        DateTimeOffset evaluatedAtUtc)
    {
        var bars = series.Bars
            .Where(bar => bar.TimestampUtc >= reviewedAtUtc && bar.TimestampUtc <= horizonEndsAtUtc)
            .OrderBy(bar => bar.TimestampUtc)
            .ToArray();

        if (bars.Length == 0)
        {
            return CreateOutcome(
                assessment,
                PaperMarketAssessmentOutcomeStatus.DataInsufficient,
                evaluatedAtUtc,
                horizonEndsAtUtc,
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                "No post-signal price bars were available for the assessment horizon.");
        }

        if (bars[^1].TimestampUtc < horizonEndsAtUtc)
        {
            return CreateOutcome(
                assessment,
                PaperMarketAssessmentOutcomeStatus.DataInsufficient,
                evaluatedAtUtc,
                horizonEndsAtUtc,
                GetMidClose(bars[0]),
                GetMidClose(bars[^1]),
                null,
                null,
                null,
                null,
                bars.Length,
                "Local market data did not cover the assessment horizon.");
        }

        var startPrice = GetMidClose(bars[0]);
        var endPrice = GetMidClose(bars[^1]);
        var directionalMove = assessment.DirectionalBias == TradeDirection.Buy
            ? endPrice - startPrice
            : startPrice - endPrice;
        var status = directionalMove switch
        {
            > 0m => PaperMarketAssessmentOutcomeStatus.FollowedBias,
            < 0m => PaperMarketAssessmentOutcomeStatus.MovedAgainstBias,
            _ => PaperMarketAssessmentOutcomeStatus.Flat,
        };
        var excursions = CalculateExcursions(assessment, startPrice, bars);

        return CreateOutcome(
            assessment,
            status,
            evaluatedAtUtc,
            horizonEndsAtUtc,
            startPrice,
            endPrice,
            directionalMove,
            startPrice == 0m ? null : directionalMove / startPrice,
            excursions.MaxFavorable,
            excursions.MaxAdverse,
            bars.Length,
            status switch
            {
                PaperMarketAssessmentOutcomeStatus.FollowedBias => "Price movement followed the stated directional bias over the assessment horizon.",
                PaperMarketAssessmentOutcomeStatus.MovedAgainstBias => "Price movement went against the stated directional bias over the assessment horizon.",
                _ => "Price movement was flat over the assessment horizon.",
            });
    }

    private static PaperMarketAssessmentOutcome CreateOutcome(
        DecisionAuditAssessment assessment,
        PaperMarketAssessmentOutcomeStatus status,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset horizonEndsAtUtc,
        decimal? startPrice,
        decimal? endPrice,
        decimal? directionalMove,
        decimal? directionalMovePercent,
        decimal? maxFavorableExcursion,
        decimal? maxAdverseExcursion,
        int barsEvaluated,
        string reason)
        => new(
            assessment.Instrument,
            assessment.DirectionalBias,
            status,
            evaluatedAtUtc,
            horizonEndsAtUtc,
            startPrice,
            endPrice,
            directionalMove,
            directionalMovePercent,
            maxFavorableExcursion,
            maxAdverseExcursion,
            barsEvaluated,
            reason);

    private static (decimal MaxFavorable, decimal MaxAdverse) CalculateExcursions(
        DecisionAuditAssessment assessment,
        decimal startPrice,
        IReadOnlyList<PriceBar> bars)
    {
        var maxMidHigh = bars.Max(GetMidHigh);
        var minMidLow = bars.Min(GetMidLow);
        if (assessment.DirectionalBias == TradeDirection.Buy)
        {
            return (
                Math.Max(0m, maxMidHigh - startPrice),
                Math.Max(0m, startPrice - minMidLow));
        }

        return (
            Math.Max(0m, startPrice - minMidLow),
            Math.Max(0m, maxMidHigh - startPrice));
    }

    private static decimal GetMidClose(PriceBar bar)
        => (bar.BidClose + bar.AskClose) / 2m;

    private static decimal GetMidHigh(PriceBar bar)
        => (bar.BidHigh + bar.AskHigh) / 2m;

    private static decimal GetMidLow(PriceBar bar)
        => (bar.BidLow + bar.AskLow) / 2m;
}
