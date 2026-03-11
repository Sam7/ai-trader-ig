using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public abstract record OpportunityReviewResult(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset ReviewedAtUtc);

public sealed record StandAsideOpportunity(
    InstrumentId Instrument,
    StandAsideDecision Decision,
    DateTimeOffset ReviewedAtUtc)
    : OpportunityReviewResult(Instrument, Decision.Summary, ReviewedAtUtc);

public sealed record ApprovedOpportunity(
    InstrumentId Instrument,
    TradeSetup TradeSetup,
    TradeApproval Approval,
    ApprovedTrade ApprovedTrade,
    DateTimeOffset ReviewedAtUtc)
    : OpportunityReviewResult(Instrument, Approval.Summary, ReviewedAtUtc);
