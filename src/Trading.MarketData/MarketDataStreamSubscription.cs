using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record MarketDataStreamSubscription(
    InstrumentId Instrument,
    PriceResolution Resolution);
