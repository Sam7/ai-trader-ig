using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class NoOpTradeApprover : ITradeApprover
{
    public Task<TradeApproval?> ApproveAsync(PendingOpportunityReview review, TradeSetup tradeSetup, CancellationToken cancellationToken = default)
        => Task.FromResult<TradeApproval?>(null);
}
