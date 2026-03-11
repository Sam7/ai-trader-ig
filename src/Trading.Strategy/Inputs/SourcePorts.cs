using Trading.Abstractions;

namespace Trading.Strategy.Inputs;

public interface IMarketSnapshotSource
{
    Task<MarketUniverseSnapshot> GetUniverseSnapshotAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);

    Task<MarketSnapshot?> GetSnapshotAsync(InstrumentId instrument, CancellationToken cancellationToken = default);
}

public interface INewsHeadlineSource
{
    Task<IReadOnlyList<NewsHeadline>> GetHeadlinesAsync(HeadlineQuery query, CancellationToken cancellationToken = default);
}

public interface IEconomicCalendarSource
{
    Task<IReadOnlyList<EconomicEvent>> GetEventsAsync(CalendarWindow window, CancellationToken cancellationToken = default);
}

public interface ITradingClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IRiskContextSource
{
    Task<RiskContext> GetRiskContextAsync(CancellationToken cancellationToken = default);
}
