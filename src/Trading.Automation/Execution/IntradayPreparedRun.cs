using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed record IntradayPreparedRun(
    IntradayOpportunityReviewInput Input,
    string RequestText,
    IReadOnlyList<PreparedIntradayMarket> Markets);

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
    string AttachmentLabel,
    byte[] ChartBytes);
