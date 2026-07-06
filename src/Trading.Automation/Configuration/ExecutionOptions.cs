using Trading.Abstractions;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.Automation.Configuration;

public sealed class ExecutionOptions
{
    public TradingExecutionMode Mode { get; init; } = TradingExecutionMode.Disabled;

    public ShadowExecutionOptions Shadow { get; init; } = new();

    public ShadowDecisionPolicy CreateShadowDecisionPolicy(string tradingTimezone)
    {
        var policy = new ShadowDecisionPolicy(
            Mode,
            tradingTimezone,
            Shadow.SupportedInstruments
                .Where(instrument => !string.IsNullOrWhiteSpace(instrument))
                .Select(instrument => new InstrumentId(instrument.Trim()))
                .ToArray(),
            Shadow.SupportedEntryMethods,
            Shadow.MinimumOpportunityScore,
            Shadow.MinimumRewardRiskRatio,
            Shadow.MaxSpreadRiskRatio,
            Shadow.MaxPriceMovementRiskRatio,
            Shadow.FreshQuoteMaxAge,
            Shadow.BlockBeforeHighImpactEvent,
            Shadow.QuantityPolicy);
        policy.Validate();
        return policy;
    }
}

public sealed class ShadowExecutionOptions
{
    public string[] SupportedInstruments { get; set; } = [];

    public TradeEntryMethod[] SupportedEntryMethods { get; set; } = [TradeEntryMethod.Market];

    public int MinimumOpportunityScore { get; init; } = 70;

    public decimal MinimumRewardRiskRatio { get; init; } = 2m;

    public decimal MaxSpreadRiskRatio { get; init; } = 0.20m;

    public decimal MaxPriceMovementRiskRatio { get; init; } = 0.25m;

    public TimeSpan FreshQuoteMaxAge { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan BlockBeforeHighImpactEvent { get; init; } = TimeSpan.FromMinutes(30);

    public string QuantityPolicy { get; init; } = "BrokerMinimum";
}
