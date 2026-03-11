using Trading.Strategy.Rules;

namespace Trading.Strategy.DayPlanning;

public sealed record DailyBriefingRequest(
    TradingDayRequest TradingDay,
    StrategyRules Rules,
    DateTimeOffset RequestedAtUtc);
