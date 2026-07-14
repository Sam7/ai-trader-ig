namespace Trading.Strategy.DayPlanning;

public sealed record DailyBriefingRequest(
    TradingDayRequest TradingDay,
    DailyPlanningPolicy Policy,
    DateTimeOffset RequestedAtUtc);
