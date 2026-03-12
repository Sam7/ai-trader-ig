namespace Trading.Strategy.Shared;

public sealed record IntradayOpportunityReviewResult(
    DateOnly TradingDate,
    IReadOnlyList<IntradayMarketAssessment> MarketAssessments,
    IReadOnlyList<IntradayOpportunityCandidate> CandidateOpportunities,
    DateTimeOffset ReviewedAtUtc,
    string Outcome);
