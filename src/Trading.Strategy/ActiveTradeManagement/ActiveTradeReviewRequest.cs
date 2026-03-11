using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

namespace Trading.Strategy.ActiveTradeManagement;

public sealed record ActiveTradeReviewRequest(
    MarketSignal Signal,
    InstrumentId? Instrument = null,
    MarketSnapshot? Snapshot = null,
    NewsHeadline? Headline = null,
    EconomicEvent? EconomicEvent = null);
