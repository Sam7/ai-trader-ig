namespace Trading.Strategy.Configuration;

public sealed record StrategyProfile(
    RiskPolicy RiskPolicy,
    MonitoringPolicy MonitoringPolicy,
    TradeLimitsPolicy TradeLimitsPolicy,
    NoTradeWindowPolicy NoTradeWindowPolicy)
{
    public static StrategyProfile Default { get; } = new(
        new RiskPolicy(0.0025m, 2.0m),
        new MonitoringPolicy(3, 0.20m, 1.50m, 0.25m, 0.25m),
        new TradeLimitsPolicy(1, 3),
        new NoTradeWindowPolicy(TimeSpan.FromMinutes(10), 0.50m, 0.50m));

    public void Validate()
    {
        RiskPolicy.Validate();
        MonitoringPolicy.Validate();
        TradeLimitsPolicy.Validate();
        NoTradeWindowPolicy.Validate();
    }
}
