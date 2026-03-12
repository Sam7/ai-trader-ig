using Trading.Abstractions;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public sealed class IntradayOpportunityReviewService
{
    private readonly ITradingDayStore _tradingDayStore;

    public IntradayOpportunityReviewService(ITradingDayStore tradingDayStore)
    {
        _tradingDayStore = tradingDayStore;
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

        foreach (var candidate in batch.CandidateOpportunities)
        {
            if (!watchedMarkets.ContainsKey(candidate.Instrument))
            {
                throw new InvalidOperationException(
                    $"Intraday opportunity instrument '{candidate.Instrument}' is not on the watch list for {batch.TradingDate:yyyy-MM-dd}.");
            }
        }

        // TODO: Introduce deterministic decision logic that decides whether to ignore, queue, or execute any returned opportunity.
        return new IntradayOpportunityReviewResult(
            batch.TradingDate,
            batch.MarketAssessments,
            batch.CandidateOpportunities,
            batch.ReviewedAtUtc,
            "Validated intraday opportunity batch. Decision logic pending.");
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
