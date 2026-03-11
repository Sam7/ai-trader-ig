using Trading.Abstractions;
using Trading.Strategy.Context;

namespace Trading.Strategy;

public enum ExecutionLifecycleStatus
{
    Proposed = 0,
    Submitted = 1,
    Filled = 2,
    PartiallyFilled = 3,
    Rejected = 4,
    Amended = 5,
    StoppedOut = 6,
    TargetHit = 7,
    Closed = 8,
}

public enum StrategyTrigger
{
    None = 0,
    EntryZoneTouched = 1,
    VolatilityExpanded = 2,
    FreshHeadline = 3,
    ScheduledEventReleased = 4,
    ThesisInvalidated = 5,
    OpenTradeAnomaly = 6,
}

public enum MarketEventKind
{
    PriceTick = 0,
    CandleClosed = 1,
    VolatilityChanged = 2,
    HeadlinePublished = 3,
    EconomicRelease = 4,
    OpenTradeAnomaly = 5,
}

public enum MarketReactionKind
{
    Ignored = 0,
    NoTrade = 1,
    ProposalRejected = 2,
    ExecutionReady = 3,
}

public enum TradeManagementAction
{
    NoOpenTrade = 0,
    Hold = 1,
    TightenRisk = 2,
    ExitTrade = 3,
    Escalate = 4,
}

public enum NoTradeReasonCode
{
    None = 0,
    NoSetup = 1,
    WeakEdge = 2,
    ContradictoryNews = 3,
    VolatilityTooErratic = 4,
    RewardRiskTooLow = 5,
    DuplicateExposure = 6,
    DailyLimitReached = 7,
    ExposureLimitReached = 8,
    SpreadTooWide = 9,
    SlippageTooHigh = 10,
    EventWindowBlocked = 11,
    RiskGateRejected = 12,
}

public sealed record TradeHypothesis(
    TradeDirection Direction,
    string Thesis,
    string Confirmation,
    string Invalidation,
    IReadOnlyList<string> ExpectedCatalysts,
    DateTimeOffset? AvoidTradingUntilUtc);

public sealed record WatchlistEntry(
    InstrumentId Instrument,
    int Rank,
    string Rationale,
    decimal EntryZoneLowerBound,
    decimal EntryZoneUpperBound,
    TradeHypothesis LongHypothesis,
    TradeHypothesis ShortHypothesis);

public sealed record DailyBriefing(
    DateOnly TradingDate,
    string MacroSummary,
    string MarketRegimeSummary,
    MarketRegime MarketRegime,
    IReadOnlyList<WatchlistEntry> RankedMarkets,
    IReadOnlyList<WatchlistEntry> Shortlist,
    IReadOnlyList<EconomicEvent> CalendarEvents,
    DateTimeOffset GeneratedAtUtc)
{
    public void Validate(int expectedShortlistSize)
    {
        if (RankedMarkets.Count == 0)
        {
            throw new ArgumentException("Daily briefing must contain at least one ranked market.", nameof(RankedMarkets));
        }

        if (Shortlist.Count != expectedShortlistSize)
        {
            throw new ArgumentException($"Daily briefing shortlist must contain exactly {expectedShortlistSize} markets.", nameof(Shortlist));
        }
    }
}

public sealed record TradeProposal(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal Confidence,
    string Catalyst,
    string Thesis,
    string Invalidation,
    DateTimeOffset ProposedAtUtc)
{
    public void Validate()
    {
        if (Confidence < 0m || Confidence > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between zero and one.");
        }

        if (EntryPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryPrice), "EntryPrice must be greater than zero.");
        }

        if (StopPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(StopPrice), "StopPrice must be greater than zero.");
        }

        if (TargetPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetPrice), "TargetPrice must be greater than zero.");
        }

        if (Direction == TradeDirection.Buy)
        {
            if (StopPrice >= EntryPrice)
            {
                throw new ArgumentException("Buy trades require StopPrice below EntryPrice.");
            }

            if (TargetPrice <= EntryPrice)
            {
                throw new ArgumentException("Buy trades require TargetPrice above EntryPrice.");
            }
        }

        if (Direction == TradeDirection.Sell)
        {
            if (StopPrice <= EntryPrice)
            {
                throw new ArgumentException("Sell trades require StopPrice above EntryPrice.");
            }

            if (TargetPrice >= EntryPrice)
            {
                throw new ArgumentException("Sell trades require TargetPrice below EntryPrice.");
            }
        }
    }
}

public sealed record NoTradeDecision(
    NoTradeReasonCode ReasonCode,
    string Summary,
    DateTimeOffset DecidedAtUtc);

public sealed record TradePlanningResult(
    TradeProposal? Proposal,
    NoTradeDecision? NoTradeDecision)
{
    public static TradePlanningResult FromProposal(TradeProposal proposal) => new(proposal, null);

    public static TradePlanningResult FromNoTrade(NoTradeDecision noTradeDecision) => new(null, noTradeDecision);
}

public sealed record RiskGateDecision(
    bool IsApproved,
    string Summary,
    DateTimeOffset EvaluatedAtUtc,
    NoTradeReasonCode RejectionCode = NoTradeReasonCode.None);

public sealed record ExecutionIntent(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal RiskAmount,
    decimal RewardRiskRatio,
    DateTimeOffset CreatedAtUtc);

public sealed record OpenTradeState(
    ExecutionIntent ExecutionIntent,
    ExecutionLifecycleStatus Status,
    DateTimeOffset UpdatedAtUtc,
    string? BrokerReference = null,
    decimal? FilledQuantity = null);

public sealed record ConsumedTrigger(
    string EventId,
    InstrumentId Instrument,
    StrategyTrigger Trigger,
    DateTimeOffset ConsumedAtUtc);

public sealed record TradingDayState(
    DateOnly TradingDate,
    DailyBriefing? DailyBriefing,
    int DailyTradeCount,
    TradeProposal? PendingProposal,
    ExecutionIntent? PendingExecutionIntent,
    OpenTradeState? OpenTrade,
    IReadOnlyList<ConsumedTrigger> ConsumedTriggers)
{
    public static TradingDayState Empty(DateOnly tradingDate)
        => new(tradingDate, null, 0, null, null, null, []);
}

public sealed record MarketEvent(
    string EventId,
    InstrumentId Instrument,
    MarketEventKind Kind,
    MarketSnapshot? Snapshot,
    DateTimeOffset OccurredAtUtc,
    HeadlineItem? Headline = null,
    EconomicEvent? EconomicEvent = null);

public sealed record MarketReaction(
    MarketReactionKind Kind,
    string Summary,
    StrategyTrigger Trigger,
    TradeProposal? Proposal = null,
    RiskGateDecision? RiskGateDecision = null,
    ExecutionIntent? ExecutionIntent = null,
    NoTradeDecision? NoTradeDecision = null);

public sealed record OpenTradeReviewRequest(
    StrategyTrigger Trigger,
    InstrumentId? Instrument = null,
    MarketSnapshot? Snapshot = null,
    HeadlineItem? Headline = null,
    EconomicEvent? EconomicEvent = null);

public sealed record TradeManagementDecision(
    TradeManagementAction Action,
    string Summary,
    DateTimeOffset DecidedAtUtc,
    decimal? SuggestedStopPrice = null);

public sealed record ExecutionOutcome(
    InstrumentId Instrument,
    ExecutionLifecycleStatus Status,
    DateTimeOffset OccurredAtUtc,
    string? BrokerReference = null,
    decimal? FilledQuantity = null);
