using Trading.Abstractions;

namespace Trading.MarketData;

public interface IMarketDataCollector
{
    Task RunAsync(
        IReadOnlyList<InstrumentId> instruments,
        TimeSpan? duration,
        CancellationToken cancellationToken = default);
}
