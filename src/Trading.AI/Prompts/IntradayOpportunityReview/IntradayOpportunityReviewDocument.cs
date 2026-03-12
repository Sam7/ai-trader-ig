using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.AI.Prompts.IntradayOpportunityReview;

public sealed record IntradayOpportunityReviewDocument(
    string RecentDevelopmentsSummary,
    IntradayMarketAssessmentDocument[] MarketAssessments,
    IntradayOpportunityCandidateDocument[] CandidateOpportunities);

public sealed record IntradayMarketAssessmentDocument(
    string InstrumentId,
    string InstrumentName,
    int OpportunityScore,
    TradeDirection DirectionalBias,
    string Summary,
    string WhyNow,
    string StandAsideReason);

public sealed record IntradayOpportunityCandidateDocument(
    string InstrumentId,
    string InstrumentName,
    TradeDirection Direction,
    int OpportunityScore,
    TradeEntryMethod EntryMethod,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TakeProfitPrice,
    decimal RewardRiskRatio,
    decimal CurrentPrice,
    decimal CurrentSpread,
    string Thesis,
    string Invalidation,
    string WhyNow,
    DateTimeOffset SetupExpiresAtUtc);
