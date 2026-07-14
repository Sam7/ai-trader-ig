using Trading.Abstractions;

namespace Trading.Strategy.Inputs;

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

public sealed record EconomicEvent(
    string Id,
    string Title,
    DateTimeOffset ScheduledAtUtc,
    EconomicEventImpact Impact,
    IReadOnlyList<InstrumentId> AffectedInstruments);
