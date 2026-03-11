using Trading.Strategy.Inputs;

namespace Trading.Strategy.Shared;

public sealed record TradingDayPlan(
    DateOnly TradingDate,
    string MacroSummary,
    string MarketRegimeSummary,
    MarketRegime MarketRegime,
    IReadOnlyList<MarketWatch> RankedMarkets,
    IReadOnlyList<MarketWatch> WatchList,
    IReadOnlyList<EconomicEvent> CalendarEvents,
    DateTimeOffset PlannedAtUtc)
{
    public void Validate(int expectedWatchListSize)
    {
        if (RankedMarkets.Count == 0)
        {
            throw new ArgumentException("Trading day plan must contain at least one ranked market.", nameof(RankedMarkets));
        }

        if (WatchList.Count != expectedWatchListSize)
        {
            throw new ArgumentException($"Trading day plan must contain exactly {expectedWatchListSize} watched markets.", nameof(WatchList));
        }
    }
}
