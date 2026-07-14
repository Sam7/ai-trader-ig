using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public interface IIntradayDecisionService
{
    Task<IntradayOpportunityReviewResult> ReviewAsync(
        IntradayOpportunityBatch batch,
        CancellationToken cancellationToken = default);
}
