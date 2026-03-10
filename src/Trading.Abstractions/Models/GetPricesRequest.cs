namespace Trading.Abstractions;

public sealed record GetPricesRequest(
    InstrumentId Instrument,
    PriceResolution? Resolution = null,
    int? MaxPoints = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Instrument.Value))
        {
            throw new ArgumentException("Instrument is required.", nameof(Instrument));
        }

        if (MaxPoints is not null && MaxPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPoints), "MaxPoints must be greater than zero.");
        }

        var hasRange = FromUtc is not null || ToUtc is not null;
        if (hasRange && (FromUtc is null || ToUtc is null))
        {
            throw new ArgumentException("Both FromUtc and ToUtc must be provided together.");
        }

        if (hasRange && MaxPoints is not null)
        {
            throw new ArgumentException("Price queries cannot specify both a time range and MaxPoints.");
        }

        if ((hasRange || MaxPoints is not null) && Resolution is null)
        {
            throw new ArgumentException("Resolution is required when querying a range or a fixed number of points.");
        }

        if (FromUtc is not null && ToUtc is not null && FromUtc > ToUtc)
        {
            throw new ArgumentException("FromUtc must be earlier than ToUtc.");
        }
    }
}
