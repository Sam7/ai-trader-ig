using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class DailyPlanMapper
{
    public TradingDayPlan Map(
        DailyPlanDocument document,
        DailyBriefingRequest request,
        IReadOnlyDictionary<string, TrackedMarketOptions> trackedMarkets,
        DateTimeOffset plannedAtUtc)
    {
        var rankedMarkets = document.RankedMarkets.Select(x => MapMarket(x, trackedMarkets)).ToArray();
        var watchList = document.WatchList.Select(x => MapMarket(x, trackedMarkets)).ToArray();

        return new TradingDayPlan(
            request.TradingDay.TradingDate,
            document.MacroSummary,
            document.MarketRegimeSummary,
            document.MarketRegime,
            rankedMarkets,
            watchList,
            [],
            plannedAtUtc);
    }

    public void ValidateTrackedMarkets(DailyPlanDocument document, IReadOnlyDictionary<string, TrackedMarketOptions> trackedMarkets)
    {
        foreach (var market in document.RankedMarkets.Concat(document.WatchList))
        {
            if (!trackedMarkets.ContainsKey(market.InstrumentId))
            {
                throw new InvalidOperationException($"Plan referenced untracked market '{market.InstrumentId}'.");
            }
        }
    }

    private static MarketWatch MapMarket(PlannedMarketDocument document, IReadOnlyDictionary<string, TrackedMarketOptions> trackedMarkets)
    {
        if (!trackedMarkets.TryGetValue(document.InstrumentId, out var trackedMarket))
        {
            throw new InvalidOperationException($"Tracked market '{document.InstrumentId}' was not configured.");
        }

        return new MarketWatch(
            new InstrumentId(trackedMarket.InstrumentId),
            document.Rank,
            document.Rationale,
            MapScenario(document.LongScenario, TradeDirection.Buy),
            MapScenario(document.ShortScenario, TradeDirection.Sell));
    }

    private static TradeScenario MapScenario(PlannedTradeScenarioDocument document, TradeDirection direction)
        => new(
            direction,
            document.Thesis,
            document.Confirmation,
            document.Invalidation,
            document.ExpectedCatalysts,
            document.AvoidTradingUntilUtc);
}
