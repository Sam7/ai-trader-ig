using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class NoOpTradeSetupPlanner : ITradeSetupPlanner
{
    public Task<TradeSetupPlanningResult> PlanAsync(PendingOpportunityReview review, CancellationToken cancellationToken = default)
        => Task.FromResult<TradeSetupPlanningResult>(
            new StandAsideSetup(new StandAsideDecision(StandAsideReason.NoSetup, "Planning-only host does not review opportunities.", DateTimeOffset.UtcNow)));
}
