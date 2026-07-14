namespace Trading.Strategy.DayPlanning;

public sealed record DailyPlanningPolicy(
    int ShortlistSize,
    decimal MinimumRewardRiskRatio)
{
    public static DailyPlanningPolicy Default { get; } = new(3, 2.0m);

    public void Validate()
    {
        if (ShortlistSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ShortlistSize), "ShortlistSize must be greater than zero.");
        }

        if (MinimumRewardRiskRatio <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumRewardRiskRatio),
                "MinimumRewardRiskRatio must be greater than zero.");
        }
    }
}
