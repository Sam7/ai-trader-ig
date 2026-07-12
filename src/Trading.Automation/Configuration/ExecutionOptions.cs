using Trading.Abstractions;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Shared;

namespace Trading.Automation.Configuration;

public sealed class ExecutionOptions
{
    public TradingExecutionMode Mode { get; init; } = TradingExecutionMode.Disabled;

    public string StorePath { get; init; } = Path.Combine("Logs", "Execution", "execution-boundary.sqlite");

    public ShadowExecutionOptions Shadow { get; init; } = new();

    public DemoExecutionOptions Demo { get; init; } = new();

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

public sealed class DemoExecutionOptions
{
    public bool Armed { get; init; }

    public bool KillSwitchEngaged { get; init; } = true;

    public string ApprovedBaseUrl { get; init; } = "https://demo-api.ig.com/gateway/deal";

    public string ApprovedAccountId { get; init; } = string.Empty;

    public string[] AllowedInstruments { get; set; } = [];

    public int MaxTradesPerTradingDay { get; init; } = 1;

    public void Validate()
    {
        if (!Uri.TryCreate(ApprovedBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Demo approved base URL must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ApprovedAccountId))
        {
            throw new InvalidOperationException("Demo approved account ID is required.");
        }

        if (AllowedInstruments.Length == 0)
        {
            throw new InvalidOperationException("Demo allowlist must contain at least one instrument.");
        }

        if (AllowedInstruments.Any(instrument => string.IsNullOrWhiteSpace(instrument)))
        {
            throw new InvalidOperationException("Demo allowlist instruments must not be blank.");
        }

        if (MaxTradesPerTradingDay <= 0)
        {
            throw new InvalidOperationException("Demo max trades per trading day must be greater than zero.");
        }
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
