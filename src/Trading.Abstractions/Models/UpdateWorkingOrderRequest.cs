namespace Trading.Abstractions;

public sealed record UpdateWorkingOrderRequest(
    string DealId,
    decimal? Level = null,
    WorkingOrderType? Type = null,
    WorkingOrderTimeInForce? TimeInForce = null,
    DateTimeOffset? GoodTillDateUtc = null);
