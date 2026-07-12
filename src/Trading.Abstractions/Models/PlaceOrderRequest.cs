namespace Trading.Abstractions;

public sealed record PlaceOrderRequest(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Size,
    string? DealReference = null,
    decimal? StopLevel = null,
    decimal? LimitLevel = null);
