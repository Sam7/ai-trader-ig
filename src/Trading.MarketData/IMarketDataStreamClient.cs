namespace Trading.MarketData;

public interface IMarketDataStreamClient
{
    Task<IMarketDataStreamSession> StartAsync(
        IReadOnlyList<MarketDataStreamSubscription> subscriptions,
        Func<StreamPriceBarUpdate, CancellationToken, Task> onUpdate,
        CancellationToken cancellationToken = default);
}
public interface IMarketDataStreamSession : IAsyncDisposable;

public sealed class NoOpMarketDataStreamSession : IMarketDataStreamSession
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
