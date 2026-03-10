namespace Trading.Abstractions;

public sealed record CreateWorkingOrderRequest(
    InstrumentId Instrument,
    TradeDirection Direction,
    WorkingOrderType Type,
    decimal Size,
    decimal Level,
    WorkingOrderTimeInForce TimeInForce,
    DateTimeOffset? GoodTillDateUtc = null);
