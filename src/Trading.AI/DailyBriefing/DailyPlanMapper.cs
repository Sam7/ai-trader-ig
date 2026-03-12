using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
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
        var watchList = rankedMarkets;
        var calendarEvents = document.CalendarEvents.Select(MapCalendarEvent).ToArray();

        return new TradingDayPlan(
            request.TradingDay.TradingDate,
            document.MacroSummary,
            document.MarketRegimeSummary,
            document.MarketRegime,
            rankedMarkets,
            watchList,
            calendarEvents,
            plannedAtUtc);
    }

    public void ValidateTrackedMarkets(DailyPlanDocument document, IReadOnlyDictionary<string, TrackedMarketOptions> trackedMarkets)
    {
        foreach (var market in document.RankedMarkets)
        {
            if (!trackedMarkets.ContainsKey(market.InstrumentId))
            {
                throw new InvalidOperationException($"Plan referenced untracked market '{market.InstrumentId}'.");
            }
        }

        foreach (var calendarEvent in document.CalendarEvents)
        {
            foreach (var instrumentId in calendarEvent.AffectedInstrumentIds)
            {
                if (!trackedMarkets.ContainsKey(instrumentId))
                {
                    throw new InvalidOperationException($"Calendar event '{calendarEvent.Id}' referenced untracked market '{instrumentId}'.");
                }
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

    private static EconomicEvent MapCalendarEvent(PlannedCalendarEventDocument document)
        => new(
            document.Id,
            document.Title,
            document.ScheduledAtUtc,
            MapImpact(document.Impact),
            document.AffectedInstrumentIds.Select(x => new InstrumentId(x)).ToArray());

    private static EconomicEventImpact MapImpact(string impact)
        => impact switch
        {
            "Low" => EconomicEventImpact.Low,
            "Medium" => EconomicEventImpact.Medium,
            "High" => EconomicEventImpact.High,
            _ => throw new InvalidOperationException($"Unknown calendar event impact '{impact}'."),
        };
}
