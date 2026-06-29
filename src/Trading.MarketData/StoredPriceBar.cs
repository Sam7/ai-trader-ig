using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record StoredPriceBar(
    InstrumentId Instrument,
    PriceResolution Resolution,
    PriceBar Bar,
    bool IsFinal,
    MarketDataSource Source,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc)
{
    public static StoredPriceBar FromPriceBar(
        InstrumentId instrument,
        PriceResolution resolution,
        PriceBar bar,
        MarketDataSource source,
        bool isFinal = true,
        DateTimeOffset? observedAtUtc = null)
    {
        var observedAt = observedAtUtc ?? DateTimeOffset.UtcNow;
        return new StoredPriceBar(instrument, resolution, bar, isFinal, source, observedAt, observedAt);
    }
}
