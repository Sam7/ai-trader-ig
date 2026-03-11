namespace Trading.Strategy;

public interface ITradingDayStateStore
{
    Task<TradingDayState?> GetAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);

    Task SaveAsync(TradingDayState state, CancellationToken cancellationToken = default);
}
