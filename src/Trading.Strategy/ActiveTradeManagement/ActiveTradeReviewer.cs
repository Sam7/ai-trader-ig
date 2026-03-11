using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

namespace Trading.Strategy.ActiveTradeManagement;

/// <summary>
/// Reviews a live trade after entry.
/// It keeps the default posture mechanical, only recommending exits, escalation, or stop tightening when a meaningful trigger arrives.
/// </summary>
public sealed class ActiveTradeReviewer
{
    private readonly ITradingClock _tradingClock;
    private readonly ITradingDayStore _tradingDayStore;
    private readonly StrategyRules _rules;
    private readonly BreakEvenStopRule _breakEvenStopRule;

    public ActiveTradeReviewer(
        ITradingClock tradingClock,
        ITradingDayStore tradingDayStore,
        StrategyRules rules,
        BreakEvenStopRule breakEvenStopRule)
    {
        _tradingClock = tradingClock;
        _tradingDayStore = tradingDayStore;
        _rules = rules;
        _breakEvenStopRule = breakEvenStopRule;
    }

    public async Task<ActiveTradeDecision> ReviewAsync(ActiveTradeReviewRequest request, CancellationToken cancellationToken = default)
    {
        var tradingDate = DateOnly.FromDateTime(_tradingClock.UtcNow.UtcDateTime);
        var record = await _tradingDayStore.GetAsync(tradingDate, cancellationToken);
        if (record?.ActiveTrade is null)
        {
            return new NoActiveTrade("No active trade exists.", _tradingClock.UtcNow);
        }

        var activeTrade = record.ActiveTrade;
        if (request.Instrument is not null && request.Instrument != activeTrade.Instrument)
        {
            return new NoActiveTrade("Requested instrument does not match the active trade.", _tradingClock.UtcNow);
        }

        if (request.Signal == MarketSignal.OpenTradeAnomaly)
        {
            return new ExitTrade(activeTrade.Instrument, "Execution anomaly detected. Exit is preferred.", _tradingClock.UtcNow);
        }

        if (request.Signal is MarketSignal.FreshHeadline or MarketSignal.ScheduledEventReleased or MarketSignal.VolatilityExpanded)
        {
            return new EscalateTradeReview(activeTrade.Instrument, "Meaningful new information warrants a focused review.", _tradingClock.UtcNow);
        }

        // Tightening the stop is the only proactive management rule here; otherwise the system simply holds.
        var suggestedStop = request.Snapshot is null
            ? null
            : _breakEvenStopRule.TrySuggestStop(activeTrade, request.Snapshot.LastPrice, _rules.Risk);

        return suggestedStop is not null
            ? new TightenStop(activeTrade.Instrument, suggestedStop.Value, "Trade has moved far enough to tighten risk.", _tradingClock.UtcNow)
            : new HoldTrade(activeTrade.Instrument, "Current management remains mechanical.", _tradingClock.UtcNow);
    }
}
