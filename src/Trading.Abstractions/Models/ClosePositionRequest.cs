namespace Trading.Abstractions;

public sealed record ClosePositionRequest(
    string DealId,
    decimal? Size = null,
    string? DealReference = null);
