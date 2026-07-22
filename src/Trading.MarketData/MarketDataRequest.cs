using Trading.Abstractions;

namespace Trading.MarketData;

public sealed record MarketDataRequest(
    InstrumentId Instrument,
    PriceResolution Resolution,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument.Value))
        {
            throw new ArgumentException("Instrument is required.", nameof(Instrument));
        }

        if (FromUtc >= ToUtc)
        {
            throw new ArgumentException("FromUtc must be earlier than ToUtc.");
        }
    }
}
