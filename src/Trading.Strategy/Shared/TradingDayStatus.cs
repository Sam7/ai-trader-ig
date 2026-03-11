namespace Trading.Strategy.Shared;

public sealed record TradingDayStatus(
    DateOnly TradingDate,
    TradingDayPlan? Plan,
    int ExecutedTradeCount,
    ApprovedTrade? PendingTrade,
    ActiveTrade? ActiveTrade);
