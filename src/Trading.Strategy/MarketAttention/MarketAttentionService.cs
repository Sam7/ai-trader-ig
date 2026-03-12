using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

namespace Trading.Strategy.MarketAttention;

/// <summary>
/// Performs the cheap, frequent intraday check.
/// Its job is to filter noise: most updates are ignored, while meaningful ones are promoted into a pending opportunity review.
/// </summary>
public sealed class MarketAttentionService
{
    private readonly StrategyRules _rules;
    private readonly ITradingDayStore _tradingDayStore;

    public MarketAttentionService(
        StrategyRules rules,
        ITradingDayStore tradingDayStore)
    {
        _rules = rules;
        _tradingDayStore = tradingDayStore;
    }

    public async Task<MarketAssessment> AssessAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default)
    {
        var tradingDate = DateOnly.FromDateTime(marketEvent.OccurredAtUtc.UtcDateTime);
        var record = await _tradingDayStore.GetAsync(tradingDate, cancellationToken);
        if (record?.Plan is null)
        {
            return new IgnoreMarketUpdate(marketEvent.Instrument, "Trading day has not been planned.", marketEvent.OccurredAtUtc);
        }

        if (record.HandledEventIds.Contains(marketEvent.EventId, StringComparer.Ordinal))
        {
            return new IgnoreMarketUpdate(marketEvent.Instrument, "Market event was already handled.", marketEvent.OccurredAtUtc);
        }

        var marketWatch = record.Plan.WatchList.FirstOrDefault(x => x.Instrument == marketEvent.Instrument);
        if (marketWatch is null)
        {
            return new IgnoreMarketUpdate(marketEvent.Instrument, "Instrument is not on today's watch list.", marketEvent.OccurredAtUtc);
        }

        var signal = DetectSignal(marketWatch, marketEvent, record.ActiveTrade);
        if (signal is null)
        {
            // An ignored event is still marked handled so repeated polling does not keep revisiting the same noise.
            await _tradingDayStore.SaveAsync(record.MarkHandled(marketEvent.EventId), cancellationToken);
            return new IgnoreMarketUpdate(marketEvent.Instrument, "No meaningful change detected.", marketEvent.OccurredAtUtc);
        }

        // A meaningful event is captured as a pending review so the next workflow step can consume it explicitly.
        var review = new PendingOpportunityReview(
            $"{tradingDate:yyyyMMdd}:{marketEvent.EventId}",
            marketEvent,
            marketWatch,
            signal.Value,
            _rules,
            record.ActiveTrade,
            marketEvent.OccurredAtUtc);

        await _tradingDayStore.SaveAsync(record.WithPendingReview(review), cancellationToken);
        return new ReviewMarketUpdate(review.ReviewId, marketEvent.Instrument, signal.Value, "This update deserves a trade review.", marketEvent.OccurredAtUtc);
    }

    private MarketSignal? DetectSignal(MarketWatch marketWatch, MarketEvent marketEvent, ActiveTrade? activeTrade)
    {
        // These are the fast promotion rules. They decide whether the update deserves a slower trade review.
        return marketEvent.Kind switch
        {
            MarketEventKind.HeadlinePublished when marketEvent.Headline is not null => MarketSignal.FreshHeadline,
            MarketEventKind.EconomicRelease when marketEvent.EconomicEvent is not null => MarketSignal.ScheduledEventReleased,
            MarketEventKind.OpenTradeAnomaly when activeTrade is not null => MarketSignal.OpenTradeAnomaly,
            MarketEventKind.VolatilityChanged when marketEvent.Snapshot is not null
                && marketEvent.Snapshot.VolatilityRatio >= _rules.MarketWatch.VolatilityExpansionThreshold => MarketSignal.VolatilityExpanded,
            MarketEventKind.PriceTick or MarketEventKind.CandleClosed => DetectPriceSignal(marketWatch, marketEvent.Snapshot, activeTrade),
            _ => null,
        };
    }

    private MarketSignal? DetectPriceSignal(MarketWatch marketWatch, Inputs.MarketSnapshot? snapshot, ActiveTrade? activeTrade)
    {
        if (snapshot is null)
        {
            return null;
        }

        // A watch can explicitly say "do not trade this market until after X", so price action alone is not enough.
        var avoidTradingUntilUtc = new[]
        {
            marketWatch.LongScenario.AvoidTradingUntilUtc,
            marketWatch.ShortScenario.AvoidTradingUntilUtc,
        }
        .Where(x => x is not null)
        .Select(x => x!.Value)
        .DefaultIfEmpty(DateTimeOffset.MinValue)
        .Max();

        if (snapshot.TimestampUtc < avoidTradingUntilUtc)
        {
            return null;
        }

        if (activeTrade is not null
            && (Math.Abs(snapshot.LastPrice - activeTrade.StopPrice) <= _rules.MarketWatch.NearStopThreshold
                || Math.Abs(snapshot.LastPrice - activeTrade.TargetPrice) <= _rules.MarketWatch.NearTargetThreshold))
        {
            return MarketSignal.OpenTradeAnomaly;
        }

        if (snapshot.VolatilityRatio >= _rules.MarketWatch.VolatilityExpansionThreshold)
        {
            return MarketSignal.VolatilityExpanded;
        }

        return null;
    }
}
