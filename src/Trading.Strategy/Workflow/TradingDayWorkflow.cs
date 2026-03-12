using Trading.Strategy.ActiveTradeManagement;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.ExecutionReporting;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.Strategy.Workflow;

/// <summary>
/// The main narrative entrypoint for the strategy library.
/// Call it in the same order a trader would work through the day:
/// plan the session, assess incoming updates, review opportunities, review live trades, and apply execution reports.
/// </summary>
public sealed class TradingDayWorkflow : ITradingDayWorkflow
{
    private readonly TradingDayPlanner _tradingDayPlanner;
    private readonly IntradayOpportunityReviewService _intradayOpportunityReviewService;
    private readonly MarketAttentionService _marketAttentionService;
    private readonly OpportunityReviewer _opportunityReviewer;
    private readonly ActiveTradeReviewer _activeTradeReviewer;
    private readonly ExecutionReportApplier _executionReportApplier;

    public TradingDayWorkflow(
        TradingDayPlanner tradingDayPlanner,
        IntradayOpportunityReviewService intradayOpportunityReviewService,
        MarketAttentionService marketAttentionService,
        OpportunityReviewer opportunityReviewer,
        ActiveTradeReviewer activeTradeReviewer,
        ExecutionReportApplier executionReportApplier)
    {
        _tradingDayPlanner = tradingDayPlanner;
        _intradayOpportunityReviewService = intradayOpportunityReviewService;
        _marketAttentionService = marketAttentionService;
        _opportunityReviewer = opportunityReviewer;
        _activeTradeReviewer = activeTradeReviewer;
        _executionReportApplier = executionReportApplier;
    }

    /// <summary>
    /// Runs once near the start of the trading day and returns the persisted plan for that date.
    /// </summary>
    public Task<TradingDayPlan> PlanTradingDayAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
        => _tradingDayPlanner.PlanAsync(request, cancellationToken);

    /// <summary>
    /// Runs on a timed intraday cadence and gives the workflow a validated batch of AI-ranked opportunities from today's watch list.
    /// </summary>
    public Task<IntradayOpportunityReviewResult> ReviewIntradayOpportunitiesAsync(
        IntradayOpportunityBatch batch,
        CancellationToken cancellationToken = default)
        => _intradayOpportunityReviewService.ReviewAsync(batch, cancellationToken);

    /// <summary>
    /// Runs for each incoming market update and decides whether it should be ignored or promoted into a focused review.
    /// </summary>
    public Task<MarketAssessment> AssessMarketAsync(MarketEvent marketEvent, CancellationToken cancellationToken = default)
        => _marketAttentionService.AssessAsync(marketEvent, cancellationToken);

    /// <summary>
    /// Runs only after a market update has already been promoted into a review and returns either stand-aside or approved-trade output.
    /// </summary>
    public Task<OpportunityReviewResult> ReviewOpportunityAsync(ReviewMarketUpdate review, CancellationToken cancellationToken = default)
        => _opportunityReviewer.ReviewAsync(review, cancellationToken);

    /// <summary>
    /// Runs while a trade is live and keeps management mostly mechanical unless a meaningful trigger arrives.
    /// </summary>
    public Task<ActiveTradeDecision> ReviewActiveTradeAsync(ActiveTradeReviewRequest request, CancellationToken cancellationToken = default)
        => _activeTradeReviewer.ReviewAsync(request, cancellationToken);

    /// <summary>
    /// Runs after the outside execution layer reports what actually happened and returns the latest public status of the trading day.
    /// </summary>
    public Task<TradingDayStatus> ApplyExecutionReportAsync(ExecutionReport report, CancellationToken cancellationToken = default)
        => _executionReportApplier.ApplyAsync(report, cancellationToken);
}
