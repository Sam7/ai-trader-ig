namespace Trading.Abstractions;

public sealed record UpdatePositionResult(
    string DealReference,
    string DealId,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
