using Trading.AI.DailyBriefing;
using Trading.AI.Prompts;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed record IntradayPreparedRun(
    IntradayOpportunityReviewRequest Request,
    string RequestText,
    IReadOnlyList<PreparedIntradayMarket> Markets,
    PromptContractProvenance PromptContract,
    IntradayPreparationProfileReference PreparationProfile);

public sealed record PreparedIntradayMarket(
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
    DateTimeOffset LatestBarAtUtc,
    PriceSeriesRefreshMode PriceSeriesRefreshMode,
    int FetchedBarCount,
    IReadOnlyList<PreparedDecisionEvidence> Evidence);
