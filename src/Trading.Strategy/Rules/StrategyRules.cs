namespace Trading.Strategy.Rules;

public sealed record StrategyRules(
    RiskRules Risk,
    MarketWatchRules MarketWatch,
    TradeLimitRules TradeLimits,
    EntryGuardRules EntryGuards)
{
    public static StrategyRules Default { get; } = new(
        new RiskRules(0.0025m, 2.0m),
        new MarketWatchRules(3, 0.20m, 1.50m, 0.25m, 0.25m),
        new TradeLimitRules(1, 3),
        new EntryGuardRules(TimeSpan.FromMinutes(10), 0.50m, 0.50m));

    public void Validate()
    {
        Risk.Validate();
        MarketWatch.Validate();
        TradeLimits.Validate();
        EntryGuards.Validate();
    }
}
