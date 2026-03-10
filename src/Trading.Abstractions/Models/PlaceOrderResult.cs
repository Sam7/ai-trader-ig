namespace Trading.Abstractions;

public sealed record PlaceOrderResult(
    string DealReference,
    string? DealId,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
