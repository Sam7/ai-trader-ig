using Trading.AI.DailyBriefing;

namespace Trading.Automation.Execution;

public interface IIntradayOpportunityDecisionCoordinator
{
    Task<IntradayOpportunitySubmitResult> CoordinateAsync(
        IntradayOpportunityPreparationDocument prepared,
        IntradayOpportunityReviewExecution execution,
        CancellationToken cancellationToken = default);
}
