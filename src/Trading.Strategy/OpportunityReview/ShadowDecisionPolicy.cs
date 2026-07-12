using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public sealed record ShadowDecisionPolicy(
    TradingExecutionMode Mode,
    string TradingTimezone,
    IReadOnlyList<InstrumentId> SupportedInstruments,
    IReadOnlyList<TradeEntryMethod> SupportedEntryMethods,
    int MinimumOpportunityScore,
    decimal MinimumRewardRiskRatio,
    decimal MaxSpreadRiskRatio,
    decimal MaxPriceMovementRiskRatio,
    TimeSpan FreshQuoteMaxAge,
    TimeSpan BlockBeforeHighImpactEvent,
    string QuantityPolicy)
{
    public static ShadowDecisionPolicy Disabled(string timezone = "Australia/Melbourne")
        => new(
            TradingExecutionMode.Disabled,
            timezone,
            [],
            [TradeEntryMethod.Market],
            70,
            2m,
            0.20m,
            0.25m,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(30),
            "BrokerMinimum");

    public void Validate()
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(TradingTimezone);

        if (Mode == TradingExecutionMode.Live)
        {
            throw new InvalidOperationException("Execution mode must be Disabled, Shadow, or Demo for the current roadmap phase.");
        }

        if (MinimumOpportunityScore is < 0 or > 100)
        {
            throw new InvalidOperationException("Minimum opportunity score must be between 0 and 100.");
        }

        if (MinimumRewardRiskRatio <= 0m)
        {
            throw new InvalidOperationException("Minimum reward:risk ratio must be greater than zero.");
        }

        if (MaxSpreadRiskRatio < 0m)
        {
            throw new InvalidOperationException("Maximum spread:risk ratio cannot be negative.");
        }

        if (MaxPriceMovementRiskRatio < 0m)
        {
            throw new InvalidOperationException("Maximum price-movement:risk ratio cannot be negative.");
        }

        if (FreshQuoteMaxAge <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Fresh quote max age must be greater than zero.");
        }

        if (BlockBeforeHighImpactEvent < TimeSpan.Zero)
        {
            throw new InvalidOperationException("High-impact event block window cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(QuantityPolicy))
        {
            throw new InvalidOperationException("Quantity policy must be configured.");
        }
    }

    public ShadowDecisionRulesSnapshot ToSnapshot()
        => new(
            Mode,
            SupportedInstruments,
            SupportedEntryMethods,
            MinimumOpportunityScore,
            MinimumRewardRiskRatio,
            MaxSpreadRiskRatio,
            MaxPriceMovementRiskRatio,
            FreshQuoteMaxAge,
            BlockBeforeHighImpactEvent,
            QuantityPolicy);
}
