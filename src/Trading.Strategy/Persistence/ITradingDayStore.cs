namespace Trading.Strategy.Persistence;

public interface ITradingDayStore
{
    Task<TradingDayRecord?> GetAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);

    Task SaveAsync(TradingDayRecord record, CancellationToken cancellationToken = default);
}
