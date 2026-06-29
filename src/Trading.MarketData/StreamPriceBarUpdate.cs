using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record StreamPriceBarUpdate(
    InstrumentId Instrument,
    PriceResolution Resolution,
    PriceBar Bar,
    bool IsFinal,
    DateTimeOffset ObservedAtUtc);
