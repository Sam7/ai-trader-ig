namespace Trading.Abstractions;

public sealed record ClosePositionResult(
    string DealReference,
    string? DealId,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
