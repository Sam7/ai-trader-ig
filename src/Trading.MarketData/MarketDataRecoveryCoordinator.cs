using Microsoft.Extensions.Logging;
using Trading.Abstractions;

namespace Trading.MarketData;

/// <summary>Serializes every automatic IG history request and persists ordered recovery progress.</summary>
public sealed class MarketDataRecoveryCoordinator
{
    private readonly IMarketDataStore _store;
    private readonly IMarketDataRecoveryStore _recoveryStore;
    private readonly ITradingGateway _gateway;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataRecoveryOptions _options;
    private readonly ILogger<MarketDataRecoveryCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _requests = [];
    private ITradingSession? _session;

    public MarketDataRecoveryCoordinator(IMarketDataStore store, IMarketDataRecoveryStore recoveryStore, ITradingGateway gateway, IMarketDataClock clock, MarketDataRecoveryOptions options, ILogger<MarketDataRecoveryCoordinator> logger)
        => (_store, _recoveryStore, _gateway, _clock, _options, _logger) = (store, recoveryStore, gateway, clock, options, logger);

    public async Task<MarketDataRecoveryStatus> RecoverOnceAsync(IReadOnlyList<MarketDataRecoveryTarget> targets, PriceResolution resolution, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var states = await _recoveryStore.GetRecoveryStatesAsync(cancellationToken);
            var now = _clock.UtcNow;
            var blocked = states.Where(s => !s.IsComplete && s.AllowanceExpiresAtUtc > now).ToArray();
            if (blocked.Length > 0)
            {
                return Status(states, blocked[0], blocked);
            }

            var candidate = await FindNextAsync(targets, resolution, states, now, cancellationToken);
            if (candidate is null)
            {
                return Status(states, null, []);
            }

            await EnforceRateAsync(cancellationToken);
            _session ??= await _gateway.AuthenticateAsync(cancellationToken);
            try
            {
                var series = await _gateway.GetPricesAsync(new GetPricesRequest(candidate.Instrument, resolution, FromUtc: candidate.CursorUtc, ToUtc: candidate.ToUtc), cancellationToken);
                _requests.Enqueue(now);
                var bars = series.Bars.Where(x => x.TimestampUtc >= candidate.CursorUtc && x.TimestampUtc < candidate.ToUtc)
                    .Select(x => StoredPriceBar.FromPriceBar(candidate.Instrument, resolution, x, MarketDataSource.RestBackfill)).ToArray();
                if (bars.Length == 0)
                {
                    await _store.RecordCoverageAsync(new MarketDataCoverageRecord(candidate.Instrument, resolution, candidate.CursorUtc, candidate.ToUtc, MarketDataCoverageStatus.NoBars, now, "IG returned no bars for recovery range.", null), cancellationToken);
                    candidate = candidate with { CursorUtc = candidate.ToUtc, IsComplete = true, RemainingAllowance = series.Allowance?.Remaining, AllowanceExpiresAtUtc = Expiry(now, series.Allowance), LastFailure = null };
                }
                else
                {
                    await _store.UpsertAsync(bars, cancellationToken);
                    var cursor = bars.Max(x => x.Bar.TimestampUtc).Add(PriceResolutionIntervals.ToTimeSpan(resolution));
                    candidate = candidate with { CursorUtc = cursor, IsComplete = cursor >= candidate.ToUtc, ReturnedPoints = candidate.ReturnedPoints + bars.Length, RemainingAllowance = series.Allowance?.Remaining, AllowanceExpiresAtUtc = Expiry(now, series.Allowance), LastFailure = null };
                }
            }
            catch (TradingGatewayException exception) when (exception.Message.Contains("allowance", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate with { RemainingAllowance = 0, AllowanceExpiresAtUtc = now.Add(TimeSpan.FromHours(1)), LastFailure = exception.Message };
            }
            catch (TradingGatewayException exception)
            {
                candidate = candidate with { LastFailure = exception.Message };
            }

            await _recoveryStore.UpsertRecoveryStateAsync(candidate, cancellationToken);
            states = await _recoveryStore.GetRecoveryStatesAsync(cancellationToken);
            return Status(states, candidate, states.Where(s => !s.IsComplete && s.AllowanceExpiresAtUtc > now).ToArray());
        }
        finally { _gate.Release(); }
    }

    private async Task<MarketDataRecoveryState?> FindNextAsync(IReadOnlyList<MarketDataRecoveryTarget> targets, PriceResolution resolution, IReadOnlyList<MarketDataRecoveryState> states, DateTimeOffset now, CancellationToken ct)
    {
        var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
        var from = PriceResolutionIntervals.AlignDown(now.Subtract(_options.Horizon), interval);
        var to = PriceResolutionIntervals.AlignDown(now, interval);
        foreach (var target in targets.OrderBy(x => x.Priority).ThenBy(x => x.Instrument.Value, StringComparer.Ordinal))
        {
            var gaps = await _store.FindMissingCompletedRangesAsync(target.Instrument, resolution, from, to, ct);
            foreach (var gap in gaps.OrderByDescending(x => x.ToUtc))
            {
                var state = states.SingleOrDefault(x => x.Instrument == target.Instrument && x.Resolution == resolution && x.FromUtc == gap.FromUtc && x.ToUtc == gap.ToUtc);
                if (state is { IsComplete: true }) continue;
                var start = Max(gap.FromUtc, gap.ToUtc.AddTicks(-_options.BarsPerRequest * interval.Ticks));
                return state ?? new MarketDataRecoveryState(target.Instrument, resolution, start, gap.ToUtc, start, false, 0, null, null, null);
            }
        }
        return null;
    }

    private async Task EnforceRateAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        while (_requests.TryPeek(out var request) && request <= now.AddMinutes(-1)) _requests.Dequeue();
        if (_requests.Count >= _options.MaximumRequestsPerMinute)
        {
            await Task.Delay(_requests.Peek().AddMinutes(1) - now, ct);
        }
    }
    private static DateTimeOffset? Expiry(DateTimeOffset now, HistoricalPriceAllowance? allowance) => allowance?.ResetAfter is { } reset ? now.Add(reset) : null;
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
    private static MarketDataRecoveryStatus Status(IReadOnlyList<MarketDataRecoveryState> states, MarketDataRecoveryState? active, IReadOnlyList<MarketDataRecoveryState> blocked)
        => new(active, states.Count(x => !x.IsComplete), active?.RemainingAllowance ?? states.LastOrDefault()?.RemainingAllowance, active?.AllowanceExpiresAtUtc ?? states.LastOrDefault()?.AllowanceExpiresAtUtc, blocked);
}
