using Trading.Strategy.Shared;

namespace Trading.Strategy.Persistence;

public sealed record TradingDayRecord(
    DateOnly TradingDate,
    TradingDayPlan? Plan,
    IReadOnlyList<string> HandledEventIds,
    PendingOpportunityReview? PendingReview,
    ApprovedTrade? PendingTrade,
    ActiveTrade? ActiveTrade,
    int ExecutedTradeCount)
{
    public static TradingDayRecord StartNew(TradingDayPlan plan)
        => new(plan.TradingDate, plan, [], null, null, null, 0);

    public TradingDayRecord MarkHandled(string eventId)
        => this with
        {
            HandledEventIds = HandledEventIds.Append(eventId).Distinct(StringComparer.Ordinal).ToList(),
        };

    public TradingDayRecord WithPendingReview(PendingOpportunityReview review)
        => this with { PendingReview = review };

    public TradingDayRecord WithPendingTrade(string handledEventId, ApprovedTrade approvedTrade)
        => this with
        {
            HandledEventIds = HandledEventIds.Append(handledEventId).Distinct(StringComparer.Ordinal).ToList(),
            PendingReview = null,
            PendingTrade = approvedTrade,
        };

    public TradingDayRecord WithStandAside(string handledEventId)
        => this with
        {
            HandledEventIds = HandledEventIds.Append(handledEventId).Distinct(StringComparer.Ordinal).ToList(),
            PendingReview = null,
            PendingTrade = null,
        };

    public TradingDayRecord ApplyExecution(ActiveTrade? activeTrade, ApprovedTrade? pendingTrade, int executedTradeCount)
        => this with
        {
            PendingTrade = pendingTrade,
            ActiveTrade = activeTrade,
            ExecutedTradeCount = executedTradeCount,
        };
}
