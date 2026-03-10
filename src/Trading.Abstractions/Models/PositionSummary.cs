namespace Trading.Abstractions;

public sealed record PositionSummary(
    string DealId,
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Size,
    string Currency,
    DateTimeOffset CreatedAtUtc);
