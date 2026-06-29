namespace Trading.MarketData;

public sealed record MarketDataGap(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);
