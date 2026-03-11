namespace Trading.Strategy.Rules;

public sealed class PositionSizer
{
    public decimal SizePosition(decimal accountEquity, decimal availableRiskBudget, decimal riskPerUnit, RiskRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        if (riskPerUnit <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(riskPerUnit), "riskPerUnit must be greater than zero.");
        }

        var riskAmount = Math.Min(accountEquity * rules.RiskPerTradeFraction, availableRiskBudget);
        return decimal.Round(riskAmount / riskPerUnit, 4, MidpointRounding.ToZero);
    }
}
