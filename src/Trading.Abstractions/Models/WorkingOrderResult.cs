namespace Trading.Abstractions;

public sealed record WorkingOrderResult(
    string DealReference,
    string? DealId,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
