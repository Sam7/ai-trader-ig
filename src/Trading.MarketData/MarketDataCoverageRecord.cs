using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record MarketDataCoverageRecord(
    InstrumentId Instrument,
    PriceResolution Resolution,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    MarketDataCoverageStatus Status,
    DateTimeOffset CheckedAtUtc,
    string? Message,
    string? BrokerErrorCode);
