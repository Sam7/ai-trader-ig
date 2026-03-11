namespace Trading.Strategy.Configuration;

public sealed record MonitoringPolicy(
    int ShortlistSize,
    decimal EntryZoneDistanceThreshold,
    decimal VolatilityExpansionThreshold,
    decimal NearTargetThreshold,
    decimal NearStopThreshold)
{
    public void Validate()
    {
        if (ShortlistSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ShortlistSize), "ShortlistSize must be greater than zero.");
        }

        if (EntryZoneDistanceThreshold < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryZoneDistanceThreshold), "EntryZoneDistanceThreshold cannot be negative.");
        }

        if (VolatilityExpansionThreshold <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(VolatilityExpansionThreshold), "VolatilityExpansionThreshold must be greater than zero.");
        }

        if (NearTargetThreshold < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(NearTargetThreshold), "NearTargetThreshold cannot be negative.");
        }

        if (NearStopThreshold < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(NearStopThreshold), "NearStopThreshold cannot be negative.");
        }
    }
}
