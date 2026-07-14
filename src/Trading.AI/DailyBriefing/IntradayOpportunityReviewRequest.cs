using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed record IntradayOpportunityReviewRequest(
    DateOnly TradingDate,
    DateTimeOffset LookbackStartUtc,
    DateTimeOffset LookbackEndUtc,
    int MaxCandidatesPerRun,
    string TradingTimezone,
    TradingDayPlan DailyPlan,
    IReadOnlyList<IntradayMarketReviewContext> Markets,
    DateTimeOffset RequestedAtUtc);

public sealed record IntradayMarketReviewContext(
    InstrumentId Instrument,
    string InstrumentName,
    int Rank,
    string Rationale,
    TradeScenario LongScenario,
    TradeScenario ShortScenario,
    decimal CurrentBid,
    decimal CurrentAsk,
    decimal CurrentPrice,
    decimal CurrentSpread,
    DateTimeOffset LatestBarAtUtc);
