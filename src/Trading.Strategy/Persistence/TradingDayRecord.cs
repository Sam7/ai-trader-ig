using Trading.Strategy.Shared;

namespace Trading.Strategy.Persistence;

public sealed record TradingDayRecord(
    DateOnly TradingDate,
    TradingDayPlan? Plan,
    IReadOnlyList<string> HandledShadowDecisionIds)
{
    public static TradingDayRecord StartNew(TradingDayPlan plan)
        => new(plan.TradingDate, plan, []);

    public TradingDayRecord MarkShadowDecisionHandled(string decisionId)
        => this with
        {
            HandledShadowDecisionIds = HandledShadowDecisionIds.Append(decisionId).Distinct(StringComparer.Ordinal).ToList(),
        };
}
