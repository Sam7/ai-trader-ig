namespace Trading.Abstractions;

public sealed record PlaceOrderRequest(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Size);
