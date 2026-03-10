namespace Trading.Abstractions;

public sealed record ClosePositionRequest(
    string DealId,
    decimal? Size = null);
