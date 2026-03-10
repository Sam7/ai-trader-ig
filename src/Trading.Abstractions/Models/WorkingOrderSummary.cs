namespace Trading.Abstractions;

public sealed record WorkingOrderSummary(
    string DealId,
    InstrumentId Instrument,
    TradeDirection Direction,
    WorkingOrderType Type,
    decimal Size,
    decimal Level,
    WorkingOrderTimeInForce TimeInForce,
    DateTimeOffset? GoodTillDateUtc,
    OrderStatus Status,
    string? CurrencyCode,
    DateTimeOffset CreatedAtUtc);
