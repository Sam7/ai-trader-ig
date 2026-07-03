using Trading.Abstractions;

namespace Trading.MarketData;

public enum MarketSessionEvidenceSource
{
    Unknown = 0,
    BrokerSnapshot = 1,
    BrokerOpeningHours = 2,
    Manual = 3,
}

public sealed record MarketSessionStatusRecord(
    InstrumentId Instrument,
    MarketStatus Status,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ValidUntilUtc,
    MarketSessionEvidenceSource Source,
    string? Message = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument.Value))
        {
            throw new ArgumentException("Instrument is required.", nameof(Instrument));
        }

        if (ObservedAtUtc >= ValidUntilUtc)
        {
            throw new ArgumentException("Session status validity must end after the observed timestamp.");
        }
    }
}
