using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Strategy.MarketAttention;

public abstract record MarketAssessment(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset ObservedAtUtc);

public sealed record IgnoreMarketUpdate(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset ObservedAtUtc)
    : MarketAssessment(Instrument, Summary, ObservedAtUtc);

public sealed record ReviewMarketUpdate(
    string ReviewId,
    InstrumentId Instrument,
    MarketSignal Signal,
    string Summary,
    DateTimeOffset ObservedAtUtc)
    : MarketAssessment(Instrument, Summary, ObservedAtUtc);
