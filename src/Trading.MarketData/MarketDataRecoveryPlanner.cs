using Trading.Abstractions;

namespace Trading.MarketData;

/// <summary>Discovers bounded repair work. It never calls the broker.</summary>
public sealed class MarketDataRecoveryPlanner
{
    private readonly IMarketDataStore _store;
    private readonly IMarketDataRecoveryStore _recoveryStore;
    private readonly IMarketSessionEvidenceStore _sessionEvidenceStore;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataRecoveryOptions _options;

    public MarketDataRecoveryPlanner(
        IMarketDataStore store,
        IMarketDataRecoveryStore recoveryStore,
        IMarketSessionEvidenceStore sessionEvidenceStore,
        IMarketDataClock clock,
        MarketDataRecoveryOptions options)
        => (_store, _recoveryStore, _sessionEvidenceStore, _clock, _options) =
            (store, recoveryStore, sessionEvidenceStore, clock, options);

    public Task PlanRecentAsync(
        IReadOnlyList<MarketDataRecoveryTarget> targets,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
        var now = _clock.UtcNow;
        return PlanAsync(
            targets,
            resolution,
            MarketDataRecoveryReason.RecentTail,
            PriceResolutionIntervals.AlignDown(now.Subtract(_options.RecentLookback), interval),
            PriceResolutionIntervals.AlignDown(now, interval),
            cancellationToken);
    }

    public Task PlanHistoricalAsync(
        IReadOnlyList<MarketDataRecoveryTarget> targets,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
        var now = _clock.UtcNow;
        return PlanAsync(
            targets,
            resolution,
            MarketDataRecoveryReason.HistoricalAudit,
            PriceResolutionIntervals.AlignDown(now.Subtract(_options.Horizon), interval),
            PriceResolutionIntervals.AlignDown(now, interval),
            cancellationToken);
    }

    private async Task PlanAsync(
        IReadOnlyList<MarketDataRecoveryTarget> targets,
        PriceResolution resolution,
        MarketDataRecoveryReason reason,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (fromUtc >= toUtc)
        {
            return;
        }

        var existing = await _recoveryStore.GetRecoveryWorkItemsAsync(cancellationToken);
        foreach (var target in targets.OrderBy(x => x.Priority).ThenBy(x => x.Instrument.Value, StringComparer.Ordinal))
        {
            if (reason == MarketDataRecoveryReason.RecentTail && await IsKnownClosedAsync(target.Instrument, fromUtc, toUtc, cancellationToken))
            {
                continue;
            }

            var gaps = await _store.FindMissingCompletedRangesAsync(target.Instrument, resolution, fromUtc, toUtc, cancellationToken);
            foreach (var gap in gaps)
            {
                var current = existing.SingleOrDefault(item =>
                    item.Instrument == target.Instrument
                    && item.Resolution == resolution
                    && item.Reason == reason);
                var now = _clock.UtcNow;
                var planned = current is null || current.Status == MarketDataRecoveryWorkStatus.Completed
                    ? new MarketDataRecoveryWorkItem(
                        target.Instrument,
                        resolution,
                        reason,
                        target.Priority,
                        gap.FromUtc,
                        gap.ToUtc,
                        gap.FromUtc,
                        MarketDataRecoveryWorkStatus.Pending,
                        now,
                        0,
                        0)
                    : current with
                    {
                        Priority = Math.Min(current.Priority, target.Priority),
                        FromUtc = Min(current.FromUtc, gap.FromUtc),
                        ToUtc = Max(current.ToUtc, gap.ToUtc),
                        CursorUtc = Min(current.CursorUtc, gap.FromUtc),
                        Status = MarketDataRecoveryWorkStatus.Pending,
                        NextAttemptUtc = Min(current.NextAttemptUtc, now),
                    };

                await _recoveryStore.UpsertRecoveryWorkItemAsync(planned, cancellationToken);
                existing = existing
                    .Where(item => item.Instrument != planned.Instrument || item.Resolution != planned.Resolution || item.Reason != planned.Reason)
                    .Append(planned)
                    .ToArray();
            }
        }
    }

    private async Task<bool> IsKnownClosedAsync(
        InstrumentId instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var status = (await _sessionEvidenceStore.GetSessionStatusAsync(instrument, fromUtc, toUtc, cancellationToken))
            .Where(item => item.ObservedAtUtc <= _clock.UtcNow && item.ValidUntilUtc > _clock.UtcNow)
            .OrderByDescending(item => item.ObservedAtUtc)
            .FirstOrDefault();

        return status?.Status is MarketStatus.Closed or MarketStatus.Suspended;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
