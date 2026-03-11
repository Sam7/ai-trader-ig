using Trading.Strategy.Shared;

namespace Trading.Strategy.Rules;

public sealed class BreakEvenStopRule
{
    public decimal? TrySuggestStop(ActiveTrade activeTrade, decimal currentPrice, RiskRules rules)
    {
        ArgumentNullException.ThrowIfNull(activeTrade);
        ArgumentNullException.ThrowIfNull(rules);

        if (!rules.MoveStopToBreakEvenOnHalfTarget)
        {
            return null;
        }

        var halfTarget = activeTrade.Direction == Trading.Abstractions.TradeDirection.Buy
            ? activeTrade.EntryPrice + ((activeTrade.TargetPrice - activeTrade.EntryPrice) / 2m)
            : activeTrade.EntryPrice - ((activeTrade.EntryPrice - activeTrade.TargetPrice) / 2m);

        var reachedHalfTarget = activeTrade.Direction == Trading.Abstractions.TradeDirection.Buy
            ? currentPrice >= halfTarget
            : currentPrice <= halfTarget;

        return reachedHalfTarget ? activeTrade.EntryPrice : null;
    }
}
