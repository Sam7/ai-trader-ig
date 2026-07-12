namespace Trading.MarketData;

public sealed class MarketDataStreamPipelineMetrics
{
    private readonly object _sync = new();
    private int _dispatcherDepth;
    private int _ingestorDepth;
    private long _receivedUpdates;
    private long _enqueuedUpdates;
    private long _coalescedUpdates;
    private long _droppedFormingUpdates;
    private long _rejectedFinalUpdates;
    private long _persistedUpdates;
    private long _persistedBatches;
    private long _failedUpdates;
    private DateTimeOffset? _lastReceivedUpdateUtc;
    private DateTimeOffset? _lastPersistedUpdateUtc;
    private DateTimeOffset? _latestFinalCandleUtc;
    private TimeSpan _lastBatchLatency;

    public void RecordReceived(DateTimeOffset observedAtUtc)
    {
        lock (_sync)
        {
            _receivedUpdates++;
            _lastReceivedUpdateUtc = observedAtUtc;
        }
    }

    public void RecordDispatcherDepth(int depth)
    {
        lock (_sync)
        {
            _dispatcherDepth = Math.Max(0, depth);
        }
    }

    public void RecordIngestorDepth(int depth)
    {
        lock (_sync)
        {
            _ingestorDepth = Math.Max(0, depth);
        }
    }

    public void RecordEnqueued()
    {
        lock (_sync)
        {
            _enqueuedUpdates++;
        }
    }

    public void RecordCoalesced()
    {
        lock (_sync)
        {
            _coalescedUpdates++;
        }
    }

    public void RecordDroppedForming()
    {
        lock (_sync)
        {
            _droppedFormingUpdates++;
        }
    }

    public void RecordRejectedFinal()
    {
        lock (_sync)
        {
            _rejectedFinalUpdates++;
        }
    }

    public void RecordPersisted(IReadOnlyList<StreamPriceBarUpdate> updates, TimeSpan latency)
    {
        if (updates.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            _persistedUpdates += updates.Count;
            _persistedBatches++;
            _lastBatchLatency = latency;
            _lastPersistedUpdateUtc = updates.Max(update => update.ObservedAtUtc);
            var latestFinal = updates
                .Where(update => update.IsFinal)
                .Select(update => (DateTimeOffset?)update.Bar.TimestampUtc)
                .Max();
            if (latestFinal is not null
                && (_latestFinalCandleUtc is null || latestFinal > _latestFinalCandleUtc))
            {
                _latestFinalCandleUtc = latestFinal;
            }
        }
    }

    public void RecordFailed(int updateCount)
    {
        lock (_sync)
        {
            _failedUpdates += Math.Max(0, updateCount);
        }
    }

    public MarketDataStreamPipelineSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new MarketDataStreamPipelineSnapshot(
                _dispatcherDepth,
                _ingestorDepth,
                _receivedUpdates,
                _enqueuedUpdates,
                _coalescedUpdates,
                _droppedFormingUpdates,
                _rejectedFinalUpdates,
                _persistedUpdates,
                _persistedBatches,
                _failedUpdates,
                _lastReceivedUpdateUtc,
                _lastPersistedUpdateUtc,
                _latestFinalCandleUtc,
                _lastBatchLatency);
        }
    }
}

public sealed record MarketDataStreamPipelineSnapshot(
    int DispatcherDepth,
    int IngestorDepth,
    long ReceivedUpdates,
    long EnqueuedUpdates,
    long CoalescedUpdates,
    long DroppedFormingUpdates,
    long RejectedFinalUpdates,
    long PersistedUpdates,
    long PersistedBatches,
    long FailedUpdates,
    DateTimeOffset? LastReceivedUpdateUtc,
    DateTimeOffset? LastPersistedUpdateUtc,
    DateTimeOffset? LatestFinalCandleUtc,
    TimeSpan LastBatchLatency);
