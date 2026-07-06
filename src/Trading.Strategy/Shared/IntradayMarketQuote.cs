using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record IntradayMarketQuote(
    InstrumentId Instrument,
    decimal CurrentPrice,
    decimal CurrentSpread,
    DateTimeOffset LatestPriceAtUtc);
