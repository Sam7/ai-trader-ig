using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class IntradayOpportunityMapper
{
    public IntradayOpportunityBatch Map(
        IntradayOpportunityReviewDocument document,
        DateOnly tradingDate,
        DateTimeOffset lookbackStartUtc,
        DateTimeOffset lookbackEndUtc,
        DateTimeOffset reviewedAtUtc,
        int maxCandidatesPerRun)
    {
        var assessments = document.MarketAssessments
            .Select(assessment => new IntradayMarketAssessment(
                new InstrumentId(assessment.InstrumentId),
                assessment.InstrumentName,
                assessment.OpportunityScore,
                assessment.DirectionalBias,
                assessment.Summary,
                assessment.WhyNow,
                assessment.StandAsideReason))
            .ToArray();

        var candidates = document.CandidateOpportunities
            .Select(candidate => new IntradayOpportunityCandidate(
                new InstrumentId(candidate.InstrumentId),
                candidate.InstrumentName,
                candidate.Direction,
                candidate.OpportunityScore,
                candidate.EntryMethod,
                candidate.EntryPrice,
                candidate.StopLossPrice,
                candidate.TakeProfitPrice,
                candidate.RewardRiskRatio,
                candidate.CurrentPrice,
                candidate.CurrentSpread,
                candidate.Thesis,
                candidate.Invalidation,
                candidate.WhyNow,
                candidate.SetupExpiresAtUtc))
            .ToArray();

        var batch = new IntradayOpportunityBatch(
            tradingDate,
            reviewedAtUtc,
            lookbackStartUtc,
            lookbackEndUtc,
            assessments,
            candidates);

        batch.Validate(maxCandidatesPerRun);
        return batch;
    }
}
