using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class InMemoryMarketDataStore : IMarketDataStore, IMarketSessionEvidenceStore, IMarketDataRecoveryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Instrument, PriceResolution Resolution, DateTimeOffset TimestampUtc), StoredPriceBar> _bars = [];
    private readonly List<MarketDataCoverageRecord> _coverage = [];
    private readonly List<MarketSessionStatusRecord> _sessionStatus = [];
    private readonly List<MarketDataRecoveryState> _recovery = [];
    private readonly List<MarketDataRecoveryWorkItem> _recoveryWork = [];
    private HistoricalAllowanceBudget? _historicalAllowanceBudget;

    public async Task UpsertRecoveryStateAsync(MarketDataRecoveryState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _recovery.RemoveAll(x => x.Instrument == state.Instrument && x.Resolution == state.Resolution && x.FromUtc == state.FromUtc && x.ToUtc == state.ToUtc);
            _recovery.Add(state);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MarketDataRecoveryState>> GetRecoveryStatesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return _recovery.OrderBy(x => x.Instrument.Value, StringComparer.Ordinal).ThenBy(x => x.FromUtc).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task UpsertRecoveryWorkItemAsync(MarketDataRecoveryWorkItem item, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _recoveryWork.RemoveAll(x => x.Instrument == item.Instrument && x.Resolution == item.Resolution && x.Reason == item.Reason);
            _recoveryWork.Add(item);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MarketDataRecoveryWorkItem>> GetRecoveryWorkItemsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _recoveryWork
                .OrderBy(x => x.Reason)
                .ThenBy(x => x.Priority)
                .ThenBy(x => x.Instrument.Value, StringComparer.Ordinal)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<HistoricalAllowanceBudget?> GetHistoricalAllowanceBudgetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return _historicalAllowanceBudget; }
        finally { _gate.Release(); }
    }

    public async Task UpsertHistoricalAllowanceBudgetAsync(HistoricalAllowanceBudget budget, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { _historicalAllowanceBudget = budget; }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(
        IReadOnlyList<StoredPriceBar> bars,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var bar in bars)
            {
                var key = (bar.Instrument.Value, bar.Resolution, bar.Bar.TimestampUtc);
                _bars[key] = _bars.TryGetValue(key, out var existing)
                    ? bar with
                    {
                        FirstSeenUtc = existing.FirstSeenUtc,
                    }
                    : bar;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredPriceBar>> GetRangeAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _bars.Values
                .Where(bar => string.Equals(bar.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && bar.Resolution == resolution
                    && bar.Bar.TimestampUtc >= fromUtc
                    && bar.Bar.TimestampUtc < toUtc)
                .OrderBy(bar => bar.Bar.TimestampUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredPriceBar?> GetLatestFinalAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _bars.Values
                .Where(bar => string.Equals(bar.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && bar.Resolution == resolution
                    && bar.IsFinal)
                .OrderByDescending(bar => bar.Bar.TimestampUtc)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketDataGap>> FindMissingCompletedRangesAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
            var completedToUtc = PriceResolutionIntervals.AlignDown(toUtc, interval);
            var present = _bars.Values
                .Where(bar => string.Equals(bar.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && bar.Resolution == resolution
                    && bar.IsFinal)
                .Select(bar => bar.Bar.TimestampUtc)
                .ToHashSet();
            var covered = _coverage
                .Where(record => string.Equals(record.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && record.Resolution == resolution
                    && record.Status == MarketDataCoverageStatus.NoBars)
                .ToArray();

            return FindMissingRanges(fromUtc, completedToUtc, interval, present, covered);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordCoverageAsync(
        MarketDataCoverageRecord coverage,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _coverage.RemoveAll(record =>
                string.Equals(record.Instrument.Value, coverage.Instrument.Value, StringComparison.Ordinal)
                && record.Resolution == coverage.Resolution
                && record.FromUtc == coverage.FromUtc
                && record.ToUtc == coverage.ToUtc);
            _coverage.Add(coverage);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketDataCoverageRecord>> GetCoverageAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _coverage
                .Where(record => string.Equals(record.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && record.Resolution == resolution
                    && record.FromUtc < toUtc
                    && record.ToUtc > fromUtc)
                .OrderBy(record => record.FromUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertSessionStatusAsync(
        MarketSessionStatusRecord status,
        CancellationToken cancellationToken = default)
    {
        status.Validate();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _sessionStatus.RemoveAll(record =>
                string.Equals(record.Instrument.Value, status.Instrument.Value, StringComparison.Ordinal)
                && record.ObservedAtUtc == status.ObservedAtUtc);
            _sessionStatus.Add(status);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MarketSessionStatusRecord>> GetSessionStatusAsync(
        InstrumentId instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _sessionStatus
                .Where(record => string.Equals(record.Instrument.Value, instrument.Value, StringComparison.Ordinal)
                    && record.ObservedAtUtc < toUtc
                    && record.ValidUntilUtc > fromUtc)
                .OrderBy(record => record.ObservedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<MarketDataGap> FindMissingRanges(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan interval,
        HashSet<DateTimeOffset> present,
        IReadOnlyList<MarketDataCoverageRecord> covered)
    {
        var gaps = new List<MarketDataGap>();
        var orderedCoverage = covered.OrderBy(record => record.FromUtc).ThenBy(record => record.ToUtc).ToArray();
        var coverageIndex = 0;
        DateTimeOffset? gapStart = null;
        var cursor = fromUtc;

        while (cursor < toUtc)
        {
            while (coverageIndex < orderedCoverage.Length && orderedCoverage[coverageIndex].ToUtc <= cursor)
            {
                coverageIndex++;
            }

            var isCovered = coverageIndex < orderedCoverage.Length
                && cursor >= orderedCoverage[coverageIndex].FromUtc
                && cursor < orderedCoverage[coverageIndex].ToUtc;
            var missing = !present.Contains(cursor) && !isCovered;
            if (missing)
            {
                gapStart ??= cursor;
            }
            else if (gapStart is not null)
            {
                gaps.Add(new MarketDataGap(gapStart.Value, cursor));
                gapStart = null;
            }

            cursor = cursor.Add(interval);
        }

        if (gapStart is not null)
        {
            gaps.Add(new MarketDataGap(gapStart.Value, toUtc));
        }

        return gaps;
    }
}
