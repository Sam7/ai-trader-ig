using Trading.Strategy.Persistence;

namespace Trading.Strategy.OpportunityReview;

public interface ITradeSetupPlanner
{
    Task<TradeSetupPlanningResult> PlanAsync(PendingOpportunityReview review, CancellationToken cancellationToken = default);
}
