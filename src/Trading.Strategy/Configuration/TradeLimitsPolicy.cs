namespace Trading.Strategy.Configuration;

public sealed record TradeLimitsPolicy(
    int MaxSimultaneousPositions,
    int MaxDailyTrades)
{
    public void Validate()
    {
        if (MaxSimultaneousPositions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSimultaneousPositions), "MaxSimultaneousPositions must be greater than zero.");
        }

        if (MaxDailyTrades <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDailyTrades), "MaxDailyTrades must be greater than zero.");
        }
    }
}
