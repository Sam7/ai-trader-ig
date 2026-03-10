namespace Trading.Abstractions;

public sealed record OrderSummary(
    string DealReference,
    string? DealId,
    InstrumentId? Instrument,
    TradeDirection? Direction,
    decimal? Size,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
