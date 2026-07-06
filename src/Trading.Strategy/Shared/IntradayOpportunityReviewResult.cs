namespace Trading.Strategy.Shared;

public sealed record IntradayOpportunityReviewResult(
    DateOnly TradingDate,
    IReadOnlyList<IntradayMarketAssessment> MarketAssessments,
    IReadOnlyList<IntradayOpportunityCandidate> CandidateOpportunities,
    TradingExecutionMode ExecutionMode,
    IReadOnlyList<IntradayCandidateDecision> CandidateDecisions,
    ExecutionReadyTradeIntent? SelectedShadowIntent,
    IntradayCandidateDecisionSummary DecisionSummary,
    DateTimeOffset ReviewedAtUtc,
    string Outcome);
