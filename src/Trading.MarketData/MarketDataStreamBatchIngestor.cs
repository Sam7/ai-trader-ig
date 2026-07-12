using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataStreamBatchIngestor : IAsyncDisposable
{
    private readonly Channel<StreamPriceBarUpdate> _channel;
    private readonly IMarketDataStore _store;
    private readonly IMarketDataHealthStore _healthStore;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataCollectorOptions _collectorOptions;
    private readonly MarketDataStreamIngestionOptions _ingestionOptions;
    private readonly MarketDataStreamPipelineMetrics _metrics;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;
    private readonly Dictionary<HealthKey, DateTimeOffset> _lastHealthWriteByKey = [];
    private int _depth;
    private int _disposed;

    public MarketDataStreamBatchIngestor(
        IMarketDataStore store,
        IMarketDataHealthStore healthStore,
        IMarketDataClock clock,
        MarketDataCollectorOptions collectorOptions,
        MarketDataStreamIngestionOptions ingestionOptions,
        MarketDataStreamPipelineMetrics metrics,
        ILogger logger)
    {
        ingestionOptions.Validate();
        _store = store;
        _healthStore = healthStore;
        _clock = clock;
        _collectorOptions = collectorOptions;
        _ingestionOptions = ingestionOptions;
        _metrics = metrics;
        _logger = logger;
        _channel = Channel.CreateBounded<StreamPriceBarUpdate>(new BoundedChannelOptions(ingestionOptions.DispatcherCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _consumer = Task.Run(ProcessAsync);
    }

    public async Task EnqueueAsync(StreamPriceBarUpdate update, CancellationToken cancellationToken)
    {
        if (update.Resolution != _collectorOptions.Resolution)
        {
            _logger.LogWarning(
                "Ignoring stream update for {Instrument} at unsupported resolution {Resolution}.",
                update.Instrument,
                update.Resolution);
            return;
        }

        var depth = Interlocked.Increment(ref _depth);
        _metrics.RecordIngestorDepth(depth);
        try
        {
            await _channel.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            depth = Interlocked.Decrement(ref _depth);
            _metrics.RecordIngestorDepth(depth);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();

        try
        {
            await _consumer.WaitAsync(_ingestionOptions.DrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timed out while draining market-data stream batch ingestor. Cancelling remaining queued updates.");
            _shutdown.Cancel();
            try
            {
                await _consumer.WaitAsync(_ingestionOptions.DrainTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogError("Market-data stream batch ingestor did not stop after cancellation.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task ProcessAsync()
    {
        var batch = new List<StreamPriceBarUpdate>(_ingestionOptions.BatchSize);

        while (await _channel.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
        {
            DrainAvailable(batch, maxCount: 1);
            await DrainUntilBatchReadyAsync(batch).ConfigureAwait(false);

            if (batch.Count > 0)
            {
                await FlushAsync(batch, _shutdown.Token).ConfigureAwait(false);
                batch.Clear();
            }
        }

        DrainAvailable(batch, int.MaxValue);
        if (batch.Count > 0)
        {
            await FlushAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task DrainUntilBatchReadyAsync(List<StreamPriceBarUpdate> batch)
    {
        if (batch.Count >= _ingestionOptions.BatchSize)
        {
            return;
        }

        using var flushDelay = new CancellationTokenSource(_ingestionOptions.FlushInterval);
        while (batch.Count < _ingestionOptions.BatchSize)
        {
            DrainAvailable(batch);
            if (batch.Count >= _ingestionOptions.BatchSize)
            {
                return;
            }

            var readTask = _channel.Reader.WaitToReadAsync(_shutdown.Token).AsTask();
            var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, flushDelay.Token);
            var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                return;
            }

            if (!await readTask.ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void DrainAvailable(List<StreamPriceBarUpdate> batch, int? maxCount = null)
    {
        var limit = maxCount ?? _ingestionOptions.BatchSize;
        while (batch.Count < limit && _channel.Reader.TryRead(out var update))
        {
            batch.Add(update);
            var depth = Interlocked.Decrement(ref _depth);
            _metrics.RecordIngestorDepth(depth);
        }
    }

    private async Task FlushAsync(IReadOnlyList<StreamPriceBarUpdate> updates, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var normalized = Normalize(updates);
            var bars = normalized
                .Select(update => new StoredPriceBar(
                    update.Update.Instrument,
                    update.Update.Resolution,
                    update.Update.Bar,
                    update.Update.IsFinal,
                    MarketDataSource.Stream,
                    update.FirstObservedAtUtc,
                    update.Update.ObservedAtUtc))
                .ToArray();
            var persistedUpdates = normalized.Select(update => update.Update).ToArray();

            await _store.UpsertAsync(bars, cancellationToken).ConfigureAwait(false);
            await UpsertHealthAsync(persistedUpdates, cancellationToken).ConfigureAwait(false);
            _metrics.RecordPersisted(persistedUpdates, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics.RecordFailed(updates.Count);
            _logger.LogError(exception, "Failed to persist {UpdateCount} market-data stream update(s).", updates.Count);
            throw;
        }
    }

    private static IReadOnlyList<PendingStreamUpdate> Normalize(IReadOnlyList<StreamPriceBarUpdate> updates)
    {
        var result = new List<PendingStreamUpdate>(updates.Count);
        var formingIndexes = new Dictionary<UpdateKey, int>();
        var finalizedKeys = new HashSet<UpdateKey>();

        foreach (var update in updates)
        {
            var key = UpdateKey.From(update);
            if (update.IsFinal)
            {
                finalizedKeys.Add(key);
                if (formingIndexes.Remove(key, out var existingIndex))
                {
                    result[existingIndex] = result[existingIndex] with { Update = update };
                }
                else
                {
                    result.Add(new PendingStreamUpdate(update, update.ObservedAtUtc));
                }

                continue;
            }

            if (finalizedKeys.Contains(key))
            {
                continue;
            }

            if (formingIndexes.TryGetValue(key, out var index))
            {
                result[index] = result[index] with { Update = update };
            }
            else
            {
                formingIndexes[key] = result.Count;
                result.Add(new PendingStreamUpdate(update, update.ObservedAtUtc));
            }
        }

        return result;
    }

    private async Task UpsertHealthAsync(IReadOnlyList<StreamPriceBarUpdate> updates, CancellationToken cancellationToken)
    {
        var byInstrument = updates
            .GroupBy(update => new HealthKey(update.Instrument, update.Resolution))
            .ToArray();

        foreach (var group in byInstrument)
        {
            var lastReceived = group.Max(update => update.ObservedAtUtc);
            var latestFinal = group
                .Where(update => update.IsFinal)
                .Select(update => (DateTimeOffset?)update.Bar.TimestampUtc)
                .Max();
            var shouldWrite = latestFinal is not null || ShouldWriteThrottledHealth(group.Key, lastReceived);
            if (!shouldWrite)
            {
                continue;
            }

            var existing = await _healthStore.GetAsync(group.Key.Instrument, group.Key.Resolution, cancellationToken)
                .ConfigureAwait(false);
            await _healthStore.UpsertAsync(
                new MarketDataHealthRecord(
                    group.Key.Instrument,
                    group.Key.Resolution,
                    MarketDataConnectionState.Connected,
                    lastReceived,
                    latestFinal ?? existing?.LatestCompletedCandleUtc,
                    existing?.RepairState ?? MarketDataRepairState.Idle,
                    existing?.UnresolvedGaps ?? [],
                    existing?.LastHistoricalRepairStatus,
                    existing?.LastHistoricalRepairMessage,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
            _lastHealthWriteByKey[group.Key] = lastReceived;
        }
    }

    private bool ShouldWriteThrottledHealth(HealthKey key, DateTimeOffset observedAtUtc)
        => !_lastHealthWriteByKey.TryGetValue(key, out var lastWrite)
            || observedAtUtc - lastWrite >= _ingestionOptions.HealthUpdateThrottle;

    private readonly record struct UpdateKey(string Instrument, PriceResolution Resolution, DateTimeOffset BucketUtc)
    {
        public static UpdateKey From(StreamPriceBarUpdate update)
            => new(update.Instrument.Value, update.Resolution, update.Bar.TimestampUtc);
    }

    private sealed record PendingStreamUpdate(StreamPriceBarUpdate Update, DateTimeOffset FirstObservedAtUtc);

    private readonly record struct HealthKey(InstrumentId Instrument, PriceResolution Resolution);
}
