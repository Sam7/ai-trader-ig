namespace Trading.Strategy.Rules;

public sealed record RiskRules(
    decimal RiskPerTradeFraction,
    decimal MinimumRewardRiskRatio,
    bool MoveStopToBreakEvenOnHalfTarget = true)
{
    public void Validate()
    {
        if (RiskPerTradeFraction <= 0m || RiskPerTradeFraction > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(RiskPerTradeFraction), "RiskPerTradeFraction must be greater than zero and not exceed one.");
        }

        if (MinimumRewardRiskRatio <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRewardRiskRatio), "MinimumRewardRiskRatio must be greater than zero.");
        }
    }
}
