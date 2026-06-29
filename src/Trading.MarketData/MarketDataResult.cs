using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record MarketDataResult(
    MarketDataStatus Status,
    PriceSeries Series,
    MarketDataResultSource Source,
    IReadOnlyList<MarketDataGap> Gaps,
    int BrokerRequestCount,
    int BackfilledBarCount,
    string? Message = null);
