using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

namespace Trading.Strategy.MarketAttention;

public enum MarketEventKind
{
    PriceTick = 0,
    CandleClosed = 1,
    VolatilityChanged = 2,
    HeadlinePublished = 3,
    EconomicRelease = 4,
    OpenTradeAnomaly = 5,
}

public sealed record MarketEvent(
    string EventId,
    InstrumentId Instrument,
    MarketEventKind Kind,
    MarketSnapshot? Snapshot,
    DateTimeOffset OccurredAtUtc,
    NewsHeadline? Headline = null,
    EconomicEvent? EconomicEvent = null);
