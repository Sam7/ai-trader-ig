namespace Trading.Strategy;

public sealed class InMemoryTradingDayStateStore : ITradingDayStateStore
{
    private readonly Dictionary<DateOnly, TradingDayState> _states = [];

    public Task<TradingDayState?> GetAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
        => Task.FromResult(_states.GetValueOrDefault(tradingDate));

    public Task SaveAsync(TradingDayState state, CancellationToken cancellationToken = default)
    {
        _states[state.TradingDate] = state;
        return Task.CompletedTask;
    }
}
