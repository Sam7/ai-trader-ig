namespace Trading.Strategy.Shared;

public sealed record TradeApproval(
    string Summary,
    DateTimeOffset ApprovedAtUtc);
