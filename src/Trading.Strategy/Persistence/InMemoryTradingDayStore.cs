namespace Trading.Strategy.Persistence;

public sealed class InMemoryTradingDayStore : ITradingDayStore
{
    private readonly Dictionary<DateOnly, TradingDayRecord> _records = [];

    public Task<TradingDayRecord?> GetAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
        => Task.FromResult(_records.GetValueOrDefault(tradingDate));

    public Task SaveAsync(TradingDayRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.TradingDate] = record;
        return Task.CompletedTask;
    }
}
