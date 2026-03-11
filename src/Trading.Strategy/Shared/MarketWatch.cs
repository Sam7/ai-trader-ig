using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record MarketWatch(
    InstrumentId Instrument,
    int Rank,
    string Rationale,
    decimal EntryZoneLowerBound,
    decimal EntryZoneUpperBound,
    TradeScenario LongScenario,
    TradeScenario ShortScenario);
