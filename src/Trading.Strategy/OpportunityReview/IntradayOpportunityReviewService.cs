using Trading.Abstractions;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public sealed class IntradayOpportunityReviewService : IIntradayDecisionService
{
    private readonly ITradingDayStore _tradingDayStore;
    private readonly IntradayCandidateDecisionService _candidateDecisionService;

    public IntradayOpportunityReviewService(
        ITradingDayStore tradingDayStore,
        IntradayCandidateDecisionService candidateDecisionService)
    {
        _tradingDayStore = tradingDayStore;
        _candidateDecisionService = candidateDecisionService;
    }

    public async Task<IntradayOpportunityReviewResult> ReviewAsync(
        IntradayOpportunityBatch batch,
        CancellationToken cancellationToken = default)
    {
        var record = await _tradingDayStore.GetAsync(batch.TradingDate, cancellationToken)
            ?? throw new InvalidOperationException($"No trading day plan exists for {batch.TradingDate:yyyy-MM-dd}.");
        var plan = record.Plan
            ?? throw new InvalidOperationException($"No trading day plan exists for {batch.TradingDate:yyyy-MM-dd}.");

        var watchedMarkets = plan.WatchList
            .ToDictionary(market => market.Instrument, market => market, InstrumentIdComparer.Instance);

        foreach (var assessment in batch.MarketAssessments)
        {
            if (!watchedMarkets.ContainsKey(assessment.Instrument))
            {
                throw new InvalidOperationException(
                    $"Intraday assessment instrument '{assessment.Instrument}' is not on the watch list for {batch.TradingDate:yyyy-MM-dd}.");
            }
        }

        var decisionReview = _candidateDecisionService.Review(record, batch);
        if (decisionReview.SelectedShadowIntent is { } selectedIntent)
        {
            await _tradingDayStore.SaveAsync(record.MarkShadowDecisionHandled(selectedIntent.DecisionId), cancellationToken);
        }

        return new IntradayOpportunityReviewResult(
            batch.TradingDate,
            batch.MarketAssessments,
            batch.CandidateOpportunities,
            decisionReview.ExecutionMode,
            decisionReview.Decisions,
            decisionReview.SelectedShadowIntent,
            decisionReview.Summary,
            batch.ReviewedAtUtc,
            FormatOutcome(decisionReview));
    }

    private static string FormatOutcome(IntradayCandidateDecisionReview decisionReview)
    {
        if (decisionReview.ExecutionMode == TradingExecutionMode.Disabled)
        {
            return "Validated intraday opportunity batch. Execution mode is Disabled; no shadow intent was approved.";
        }

        if (decisionReview.SelectedShadowIntent is null)
        {
            if (decisionReview.ExecutionMode == TradingExecutionMode.Demo)
            {
                return "Validated intraday opportunity batch. No candidate was approved for demo canary execution.";
            }

            return "Validated intraday opportunity batch. No candidate was approved for shadow execution.";
        }

        return decisionReview.ExecutionMode == TradingExecutionMode.Demo
            ? $"Validated intraday opportunity batch. Selected demo canary intent {decisionReview.SelectedShadowIntent.DecisionId}."
            : $"Validated intraday opportunity batch. Selected shadow intent {decisionReview.SelectedShadowIntent.DecisionId}.";
    }

    private sealed class InstrumentIdComparer : IEqualityComparer<InstrumentId>
    {
        public static InstrumentIdComparer Instance { get; } = new();

        public bool Equals(InstrumentId x, InstrumentId y)
            => StringComparer.Ordinal.Equals(x.Value, y.Value);

        public int GetHashCode(InstrumentId obj)
            => StringComparer.Ordinal.GetHashCode(obj.Value);
    }
}
