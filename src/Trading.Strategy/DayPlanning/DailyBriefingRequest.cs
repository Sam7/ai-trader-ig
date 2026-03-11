using Trading.Strategy.Inputs;
using Trading.Strategy.Rules;

namespace Trading.Strategy.DayPlanning;

public sealed record DailyBriefingRequest(
    TradingDayRequest TradingDay,
    StrategyRules Rules,
    MarketUniverseSnapshot Universe,
    IReadOnlyList<NewsHeadline> Headlines,
    IReadOnlyList<EconomicEvent> CalendarEvents,
    DateTimeOffset RequestedAtUtc);
