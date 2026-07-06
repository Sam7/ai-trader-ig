using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record IntradayOpportunityCandidate(
    InstrumentId Instrument,
    string InstrumentName,
    TradeDirection Direction,
    int OpportunityScore,
    TradeEntryMethod EntryMethod,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TakeProfitPrice,
    decimal RewardRiskRatio,
    decimal CurrentPrice,
    decimal CurrentSpread,
    string Thesis,
    string Invalidation,
    string WhyNow,
    DateTimeOffset SetupExpiresAtUtc)
{
    public void Validate()
    {
        if (OpportunityScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(OpportunityScore), "Opportunity score must be between 0 and 100.");
        }

        if (EntryPrice <= 0m || StopLossPrice <= 0m || TakeProfitPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryPrice), "Entry, stop-loss, and take-profit prices must be greater than zero.");
        }

        if (CurrentPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(CurrentPrice), "Current price must be greater than zero.");
        }

        if (CurrentSpread < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(CurrentSpread), "Current spread must be zero or greater.");
        }

        if (RewardRiskRatio <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(RewardRiskRatio), "Reward-to-risk ratio must be greater than zero.");
        }

        // Directional geometry is a phase-one decision rule so invalid model output can be audited as a rejection.
    }
}
