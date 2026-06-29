namespace Trading.MarketData;

public interface IMarketDataClock
{
    DateTimeOffset UtcNow { get; }
}
public sealed class SystemMarketDataClock : IMarketDataClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedMarketDataClock : IMarketDataClock
{
    public FixedMarketDataClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public DateTimeOffset UtcNow { get; }
}
