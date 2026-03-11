using Trading.Abstractions;

namespace Trading.Strategy.Context;

public interface IMarketSnapshotSource
{
    Task<MarketUniverseSnapshot> GetUniverseSnapshotAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);

    Task<MarketSnapshot?> GetSnapshotAsync(InstrumentId instrument, CancellationToken cancellationToken = default);
}

public interface IHeadlineSource
{
    Task<IReadOnlyList<HeadlineItem>> GetHeadlinesAsync(HeadlineQuery query, CancellationToken cancellationToken = default);
}

public interface IEconomicCalendarSource
{
    Task<IReadOnlyList<EconomicEvent>> GetEventsAsync(CalendarWindow window, CancellationToken cancellationToken = default);
}

public interface ITradingClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IExposureStateSource
{
    Task<ExposureState> GetExposureStateAsync(CancellationToken cancellationToken = default);
}
