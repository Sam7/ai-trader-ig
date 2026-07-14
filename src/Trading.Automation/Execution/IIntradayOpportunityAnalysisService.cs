using Trading.AI.DailyBriefing;

namespace Trading.Automation.Execution;

public interface IIntradayOpportunityAnalysisService : IIntradayOpportunityRequestRenderer
{
    Task<IntradayOpportunityReviewExecution> AnalyzeAsync(
        IntradayOpportunityPreparationDocument prepared,
        CancellationToken cancellationToken = default);
}
