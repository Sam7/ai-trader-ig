using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public interface ITradeApprover
{
    Task<TradeApproval?> ApproveAsync(PendingOpportunityReview review, IntradayTradeSetup tradeSetup, CancellationToken cancellationToken = default);
}
