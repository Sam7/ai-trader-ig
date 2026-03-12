using Trading.Strategy.ActiveTradeManagement;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.ExecutionReporting;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.Strategy.Workflow;

public interface ITradingDayWorkflow
{
    Task<TradingDayPlan> PlanTradingDayAsync(TradingDayRequest request, CancellationToken cancellationToken = default);

    Task<IntradayOpportunityReviewResult> ReviewIntradayOpportunitiesAsync(
        IntradayOpportunityBatch batch,
        CancellationToken cancellationToken = default);

    Task<MarketAssessment> AssessMarketAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default);

    Task<OpportunityReviewResult> ReviewOpportunityAsync(ReviewMarketUpdate review, CancellationToken cancellationToken = default);

    Task<ActiveTradeDecision> ReviewActiveTradeAsync(ActiveTradeReviewRequest request, CancellationToken cancellationToken = default);

    Task<TradingDayStatus> ApplyExecutionReportAsync(ExecutionReport report, CancellationToken cancellationToken = default);
}
