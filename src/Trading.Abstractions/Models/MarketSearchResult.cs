namespace Trading.Abstractions;

public sealed record MarketSearchResult(
    InstrumentId Instrument,
    string Name,
    string? Type,
    string? Expiry,
    string? CurrencyCode,
    MarketStatus Status);
