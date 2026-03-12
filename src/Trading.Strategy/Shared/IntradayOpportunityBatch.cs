namespace Trading.Strategy.Shared;

public sealed record IntradayOpportunityBatch(
    DateOnly TradingDate,
    DateTimeOffset ReviewedAtUtc,
    DateTimeOffset LookbackStartUtc,
    DateTimeOffset LookbackEndUtc,
    IReadOnlyList<IntradayMarketAssessment> MarketAssessments,
    IReadOnlyList<IntradayOpportunityCandidate> CandidateOpportunities)
{
    public void Validate(int maxCandidatesPerRun)
    {
        if (MarketAssessments.Count == 0)
        {
            throw new ArgumentException("Intraday opportunity batch must contain at least one market assessment.", nameof(MarketAssessments));
        }

        if (CandidateOpportunities.Count > maxCandidatesPerRun)
        {
            throw new ArgumentException(
                $"Intraday opportunity batch must contain at most {maxCandidatesPerRun} actionable candidates.",
                nameof(CandidateOpportunities));
        }

        foreach (var assessment in MarketAssessments)
        {
            assessment.Validate();
        }

        foreach (var candidate in CandidateOpportunities)
        {
            candidate.Validate();
        }
    }
}
