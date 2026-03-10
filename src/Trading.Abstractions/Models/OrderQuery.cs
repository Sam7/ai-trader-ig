namespace Trading.Abstractions;

public sealed record OrderQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MaxItems = 100)
{
    public void Validate()
    {
        if (ToUtc < FromUtc)
        {
            throw new ArgumentException("ToUtc must be greater than or equal to FromUtc.");
        }

        if (MaxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxItems), "MaxItems must be greater than zero.");
        }
    }
}
