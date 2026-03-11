namespace Trading.Strategy.Rules;

public sealed record EntryGuardRules(
    TimeSpan BlockBeforeHighImpactEvent,
    decimal MaxSpread,
    decimal MaxSlippage)
{
    public void Validate()
    {
        if (BlockBeforeHighImpactEvent < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BlockBeforeHighImpactEvent), "BlockBeforeHighImpactEvent cannot be negative.");
        }

        if (MaxSpread < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSpread), "MaxSpread cannot be negative.");
        }

        if (MaxSlippage < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSlippage), "MaxSlippage cannot be negative.");
        }
    }
}
