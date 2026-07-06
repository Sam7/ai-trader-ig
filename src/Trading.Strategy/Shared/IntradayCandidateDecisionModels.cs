using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public enum IntradayCandidateDecisionStatus
{
    Rejected = 1,
    ApprovedForShadowExecution = 2,
    AlreadyProcessed = 3,
    UnsupportedByCurrentExecutionScope = 4,
}

public enum IntradayCandidateDecisionReason
{
    Approved = 1,
    ExecutionDisabled = 2,
    NotOnActiveWatchlist = 3,
    Expired = 4,
    StaleQuote = 5,
    InvalidPriceGeometry = 6,
    RewardRiskTooLow = 7,
    SpreadTooWide = 8,
    PriceMovedTooFar = 9,
    OpportunityScoreTooLow = 10,
    UnsupportedInstrument = 11,
    UnsupportedEntryMethod = 12,
    AlreadyProcessed = 13,
    TradingDateMismatch = 14,
    HighImpactEventBlocked = 15,
}

public sealed record IntradayCandidateDecision(
    string DecisionId,
    InstrumentId Instrument,
    TradeDirection Direction,
    TradeEntryMethod EntryMethod,
    int OpportunityScore,
    IntradayCandidateDecisionStatus Status,
    IReadOnlyList<IntradayCandidateDecisionReason> Reasons,
    decimal? RecalculatedRewardRiskRatio,
    decimal? SpreadRiskRatio,
    decimal? PriceMovementRiskRatio,
    string Explanation,
    ExecutionReadyTradeIntent? Intent);

public sealed record ExecutionReadyTradeIntent(
    string DecisionId,
    string SourceDecisionAuditId,
    DateOnly TradingDate,
    InstrumentId Instrument,
    string InstrumentName,
    TradeDirection Direction,
    TradeEntryMethod EntryMethod,
    decimal ExpectedEntryPrice,
    decimal StopLossPrice,
    decimal TakeProfitPrice,
    DateTimeOffset SetupExpiresAtUtc,
    string QuantityPolicy,
    DateTimeOffset ApprovedAtUtc,
    IReadOnlyList<string> ApprovalReasons,
    ShadowDecisionRulesSnapshot Rules,
    ShadowDecisionContextSnapshot Context,
    IReadOnlyList<string> Limitations);

public sealed record ShadowDecisionRulesSnapshot(
    TradingExecutionMode Mode,
    IReadOnlyList<InstrumentId> SupportedInstruments,
    IReadOnlyList<TradeEntryMethod> SupportedEntryMethods,
    int MinimumOpportunityScore,
    decimal MinimumRewardRiskRatio,
    decimal MaxSpreadRiskRatio,
    decimal MaxPriceMovementRiskRatio,
    TimeSpan FreshQuoteMaxAge,
    TimeSpan BlockBeforeHighImpactEvent,
    string QuantityPolicy);

public sealed record ShadowDecisionContextSnapshot(
    string TradingTimezone,
    DateOnly ConfiguredTradingDate,
    DateOnly ResolvedTradingDate,
    DateTimeOffset ReviewedAtUtc,
    DateTimeOffset LatestQuoteAtUtc,
    decimal CurrentPrice,
    decimal CurrentSpread,
    int WatchlistRank,
    string DailyPlanMarketRegime);

public sealed record IntradayCandidateDecisionSummary(
    int Considered,
    int Approved,
    int Rejected,
    int AlreadyProcessed,
    int Unsupported);
