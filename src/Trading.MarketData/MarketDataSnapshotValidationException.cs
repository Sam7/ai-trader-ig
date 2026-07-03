namespace Trading.MarketData;

public sealed class MarketDataSnapshotValidationException : Exception
{
    public MarketDataSnapshotValidationException(string message)
        : base(message)
    {
    }

    public MarketDataSnapshotValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
