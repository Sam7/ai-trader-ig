using Trading.Strategy.Configuration;
using Trading.Strategy.Context;

namespace Trading.Strategy.Monitoring;

public sealed class MarketMonitor
{
    public StrategyTrigger DetectTrigger(
        WatchlistEntry watchlistEntry,
        MarketEvent marketEvent,
        OpenTradeState? openTrade,
        MonitoringPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(watchlistEntry);
        ArgumentNullException.ThrowIfNull(marketEvent);
        ArgumentNullException.ThrowIfNull(policy);

        return marketEvent.Kind switch
        {
            MarketEventKind.HeadlinePublished when marketEvent.Headline is not null => StrategyTrigger.FreshHeadline,
            MarketEventKind.EconomicRelease when marketEvent.EconomicEvent is not null => StrategyTrigger.ScheduledEventReleased,
            MarketEventKind.OpenTradeAnomaly when openTrade is not null => StrategyTrigger.OpenTradeAnomaly,
            MarketEventKind.VolatilityChanged when marketEvent.Snapshot is not null
                && marketEvent.Snapshot.VolatilityRatio >= policy.VolatilityExpansionThreshold => StrategyTrigger.VolatilityExpanded,
            MarketEventKind.PriceTick or MarketEventKind.CandleClosed => DetectPriceTrigger(watchlistEntry, marketEvent.Snapshot, openTrade, policy),
            _ => StrategyTrigger.None,
        };
    }

    private static StrategyTrigger DetectPriceTrigger(
        WatchlistEntry watchlistEntry,
        MarketSnapshot? snapshot,
        OpenTradeState? openTrade,
        MonitoringPolicy policy)
    {
        if (snapshot is null)
        {
            return StrategyTrigger.None;
        }

        var avoidTradingUntilUtc = new[]
        {
            watchlistEntry.LongHypothesis.AvoidTradingUntilUtc,
            watchlistEntry.ShortHypothesis.AvoidTradingUntilUtc,
        }
        .Where(x => x is not null)
        .Select(x => x!.Value)
        .DefaultIfEmpty(DateTimeOffset.MinValue)
        .Max();

        if (snapshot.TimestampUtc < avoidTradingUntilUtc)
        {
            return StrategyTrigger.None;
        }

        var price = snapshot.LastPrice;

        if (price >= watchlistEntry.EntryZoneLowerBound - policy.EntryZoneDistanceThreshold
            && price <= watchlistEntry.EntryZoneUpperBound + policy.EntryZoneDistanceThreshold)
        {
            return StrategyTrigger.EntryZoneTouched;
        }

        if (openTrade is not null
            && (Math.Abs(price - openTrade.ExecutionIntent.StopPrice) <= policy.NearStopThreshold
                || Math.Abs(price - openTrade.ExecutionIntent.TargetPrice) <= policy.NearTargetThreshold))
        {
            return StrategyTrigger.OpenTradeAnomaly;
        }

        if (price <= watchlistEntry.EntryZoneLowerBound - snapshot.Atr
            || price >= watchlistEntry.EntryZoneUpperBound + snapshot.Atr)
        {
            return StrategyTrigger.ThesisInvalidated;
        }

        return StrategyTrigger.None;
    }
}
