using Microsoft.Extensions.Logging;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.IG;

internal sealed class BoundedMarketDataStreamDispatcher : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly LinkedList<StreamPriceBarUpdate> _queue = new();
    private readonly Dictionary<StreamUpdateKey, LinkedListNode<StreamPriceBarUpdate>> _formingUpdates = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Func<StreamPriceBarUpdate, CancellationToken, Task> _handler;
    private readonly MarketDataStreamPipelineMetrics _metrics;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;
    private readonly int _capacity;
    private readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    public BoundedMarketDataStreamDispatcher(
        Func<StreamPriceBarUpdate, CancellationToken, Task> handler,
        int capacity,
        MarketDataStreamPipelineMetrics metrics,
        ILogger logger)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Dispatcher capacity must be greater than zero.");
        }

        _handler = handler;
        _capacity = capacity;
        _metrics = metrics;
        _logger = logger;
        _consumer = Task.Run(ProcessAsync);
    }

    public bool TryPost(StreamPriceBarUpdate update)
    {
        _metrics.RecordReceived(update.ObservedAtUtc);

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var key = StreamUpdateKey.From(update);
            if (!update.IsFinal && _formingUpdates.TryGetValue(key, out var existing))
            {
                existing.Value = update;
                _metrics.RecordCoalesced();
                return true;
            }

            if (_queue.Count >= _capacity && !TryMakeRoomFor(update))
            {
                if (update.IsFinal)
                {
                    _metrics.RecordRejectedFinal();
                    _logger.LogCritical(
                        "Rejected final market-data stream update for {Instrument} at {TimestampUtc} because the dispatcher queue is full.",
                        update.Instrument,
                        update.Bar.TimestampUtc);
                }
                else
                {
                    _metrics.RecordDroppedForming();
                }

                return false;
            }

            var node = _queue.AddLast(update);
            if (!update.IsFinal)
            {
                _formingUpdates[key] = node;
            }

            _metrics.RecordEnqueued();
            _metrics.RecordDispatcherDepth(_queue.Count);
            _signal.Release();
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        _signal.Release();

        try
        {
            await _consumer.WaitAsync(_drainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timed out while draining IG market-data stream dispatcher. Cancelling remaining queued updates.");
            _shutdown.Cancel();
            _signal.Release();
            try
            {
                await _consumer.WaitAsync(_drainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogError("IG market-data stream dispatcher did not stop after cancellation.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
            _signal.Dispose();
        }
    }

    private bool TryMakeRoomFor(StreamPriceBarUpdate incoming)
    {
        if (!incoming.IsFinal)
        {
            return false;
        }

        for (var node = _queue.First; node is not null; node = node.Next)
        {
            if (node.Value.IsFinal)
            {
                continue;
            }

            _formingUpdates.Remove(StreamUpdateKey.From(node.Value));
            _queue.Remove(node);
            _metrics.RecordDroppedForming();
            _metrics.RecordDispatcherDepth(_queue.Count);
            return true;
        }

        return false;
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);

            StreamPriceBarUpdate? update;
            lock (_sync)
            {
                if (_queue.First is null)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    continue;
                }

                var node = _queue.First;
                update = node.Value;
                _queue.RemoveFirst();
                if (!update.IsFinal)
                {
                    _formingUpdates.Remove(StreamUpdateKey.From(update));
                }

                _metrics.RecordDispatcherDepth(_queue.Count);
            }

            try
            {
                await _handler(update, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _metrics.RecordFailed(1);
                _logger.LogError(
                    exception,
                    "Failed to process IG Lightstreamer chart update for {Instrument}.",
                    update.Instrument);
            }
        }
    }

    private readonly record struct StreamUpdateKey(string Instrument, PriceResolution Resolution, DateTimeOffset BucketUtc)
    {
        public static StreamUpdateKey From(StreamPriceBarUpdate update)
            => new(update.Instrument.Value, update.Resolution, update.Bar.TimestampUtc);
    }
}
