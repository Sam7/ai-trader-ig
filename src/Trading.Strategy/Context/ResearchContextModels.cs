using Trading.Abstractions;
using Trading.Strategy.Configuration;

namespace Trading.Strategy.Context;

public enum MarketRegime
{
    Unknown = 0,
    RiskOn = 1,
    RiskOff = 2,
    Mixed = 3,
    EventDriven = 4,
    RangeBound = 5,
    TrendDayCandidate = 6,
}

public enum EconomicEventImpact
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public sealed record TradingDayRequest(DateOnly TradingDate);

public sealed record MarketUniverseSnapshot(
    DateOnly TradingDate,
    IReadOnlyList<MarketSnapshot> Markets);

public sealed record MarketSnapshot(
    InstrumentId Instrument,
    decimal LastPrice,
    decimal Bid,
    decimal Ask,
    decimal Atr,
    decimal VolatilityRatio,
    DateTimeOffset TimestampUtc)
{
    public decimal Spread => Math.Max(0m, Ask - Bid);
}

public sealed record HeadlineQuery(
    IReadOnlyList<InstrumentId> Instruments,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);

public sealed record HeadlineItem(
    string Id,
    InstrumentId? Instrument,
    string Title,
    DateTimeOffset PublishedAtUtc,
    bool IsBreaking);

public sealed record CalendarWindow(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);

public sealed record EconomicEvent(
    string Id,
    string Title,
    DateTimeOffset ScheduledAtUtc,
    EconomicEventImpact Impact,
    IReadOnlyList<InstrumentId> AffectedInstruments);

public sealed record ExposureState(
    decimal AccountEquity,
    decimal AvailableRiskBudget,
    IReadOnlyList<InstrumentId> OpenPositionInstruments,
    IReadOnlyList<InstrumentId> WorkingOrderInstruments)
{
    public int ActiveExposureCount => OpenPositionInstruments.Count + WorkingOrderInstruments.Count;
}

public sealed record ResearchBriefingInput(
    TradingDayRequest Request,
    StrategyProfile Profile,
    MarketUniverseSnapshot Universe,
    IReadOnlyList<HeadlineItem> Headlines,
    IReadOnlyList<EconomicEvent> CalendarEvents,
    DateTimeOffset GeneratedAtUtc);

public sealed record TradePlanningInput(
    StrategyProfile Profile,
    WatchlistEntry WatchlistEntry,
    MarketEvent MarketEvent,
    IReadOnlyList<HeadlineItem> RecentHeadlines,
    OpenTradeState? OpenTrade,
    DateTimeOffset RequestedAtUtc);

public sealed record RiskGateInput(
    StrategyProfile Profile,
    TradingDayState TradingDayState,
    ExposureState ExposureState,
    TradeProposal Proposal,
    StrategyTrigger Trigger,
    DateTimeOffset RequestedAtUtc);
