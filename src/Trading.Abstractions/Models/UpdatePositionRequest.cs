namespace Trading.Abstractions;

public sealed record UpdatePositionRequest(
    string DealId,
    decimal? StopLevel,
    decimal? LimitLevel,
    decimal? TrailingStopDistance = null,
    decimal? TrailingStopIncrement = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DealId))
        {
            throw new ArgumentException("DealId is required.", nameof(DealId));
        }

        if (StopLevel is null
            && LimitLevel is null
            && TrailingStopDistance is null
            && TrailingStopIncrement is null)
        {
            throw new ArgumentException("At least one amendment must be provided.", nameof(StopLevel));
        }

        if (TrailingStopIncrement is not null && TrailingStopDistance is null)
        {
            throw new ArgumentException("TrailingStopIncrement requires TrailingStopDistance.", nameof(TrailingStopIncrement));
        }
    }
}
