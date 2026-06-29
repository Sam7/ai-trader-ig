namespace Trading.MarketData;

public sealed record MarketDataIngestResult(
    MarketDataIngestStatus Status,
    string? Reason = null);
