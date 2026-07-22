using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Trading.Abstractions;

namespace Trading.MarketData;

/// <summary>Executes one already-planned recovery item at a time and owns automatic IG allowance admission.</summary>
public sealed class MarketDataRecoveryCoordinator
{
    private readonly IMarketDataStore _store;
    private readonly IMarketDataRecoveryStore _recoveryStore;
    private readonly ITradingGateway _gateway;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataRecoveryOptions _options;
    private readonly MarketDataRuntimeActivityMetrics _activityMetrics;
    private readonly ILogger<MarketDataRecoveryCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _requests = [];
    private ITradingSession? _session;

    public MarketDataRecoveryCoordinator(
        IMarketDataStore store,
        IMarketDataRecoveryStore recoveryStore,
        ITradingGateway gateway,
        IMarketDataClock clock,
        MarketDataRecoveryOptions options,
        MarketDataRuntimeActivityMetrics activityMetrics,
        ILogger<MarketDataRecoveryCoordinator> logger)
        => (_store, _recoveryStore, _gateway, _clock, _options, _activityMetrics, _logger) =
            (store, recoveryStore, gateway, clock, options, activityMetrics, logger);

    public async Task<bool> ProcessNextAsync(MarketDataRecoveryMode mode, CancellationToken cancellationToken = default)
    {
        if (mode is MarketDataRecoveryMode.Disabled or MarketDataRecoveryMode.Observe)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            var budget = await _recoveryStore.GetHistoricalAllowanceBudgetAsync(cancellationToken);
            var candidate = SelectCandidate(
                await _recoveryStore.GetRecoveryWorkItemsAsync(cancellationToken),
                mode,
                budget,
                now);
            if (candidate is null)
            {
                return false;
            }

            if ((candidate.Reason is MarketDataRecoveryReason.RecentTail or MarketDataRecoveryReason.DeploymentContinuity)
                && IsKnownExhausted(budget, now))
            {
                await DeferAsync(candidate, budget!.ResetAtUtc!.Value, "Historical allowance is exhausted.", cancellationToken);
                return false;
            }

            if (candidate.Reason == MarketDataRecoveryReason.HistoricalAudit
                && !CanSpendBackground(budget, now))
            {
                return false;
            }

            await EnforceRateAsync(cancellationToken);
            _activityMetrics.RecordRecoveryStarted();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var series = await FetchAsync(
                    new GetPricesRequest(candidate.Instrument, candidate.Resolution, FromUtc: candidate.CursorUtc, ToUtc: candidate.ToUtc),
                    cancellationToken);
                _requests.Enqueue(_clock.UtcNow);
                var bars = series.Bars
                    .Where(bar => bar.TimestampUtc >= candidate.CursorUtc && bar.TimestampUtc < candidate.ToUtc)
                    .Select(bar => StoredPriceBar.FromPriceBar(candidate.Instrument, candidate.Resolution, bar, MarketDataSource.RestBackfill))
                    .ToArray();

                var updatedBudget = UpdateBudget(budget, series.Allowance, candidate.Reason, _clock.UtcNow);
                if (updatedBudget is not null)
                {
                    await _recoveryStore.UpsertHistoricalAllowanceBudgetAsync(updatedBudget, cancellationToken);
                }

                if (bars.Length == 0)
                {
                    await _store.RecordCoverageAsync(
                        new MarketDataCoverageRecord(
                            candidate.Instrument,
                            candidate.Resolution,
                            candidate.CursorUtc,
                            candidate.ToUtc,
                            MarketDataCoverageStatus.NoBars,
                            _clock.UtcNow,
                            "IG returned no bars for recovery range.",
                            null),
                        cancellationToken);
                    await CompleteAsync(candidate, candidate.ToUtc, cancellationToken);
                    _activityMetrics.RecordRecoveryCompleted(stopwatch.Elapsed);
                    return true;
                }

                await _store.UpsertAsync(bars, cancellationToken);
                var cursor = bars.Max(bar => bar.Bar.TimestampUtc).Add(PriceResolutionIntervals.ToTimeSpan(candidate.Resolution));
                var completed = cursor >= candidate.ToUtc;
                await _recoveryStore.UpsertRecoveryWorkItemAsync(candidate with
                {
                    CursorUtc = cursor,
                    Status = completed ? MarketDataRecoveryWorkStatus.Completed : MarketDataRecoveryWorkStatus.Pending,
                    NextAttemptUtc = completed ? DateTimeOffset.MaxValue : _clock.UtcNow,
                    AttemptCount = 0,
                    ReturnedPoints = candidate.ReturnedPoints + bars.Length,
                    LastFailure = null,
                }, cancellationToken);
                _activityMetrics.RecordRecoveryCompleted(stopwatch.Elapsed);
                return true;
            }
            catch (TradingGatewayException exception) when (IsAllowanceFailure(exception))
            {
                _activityMetrics.RecordRecoveryFailed(stopwatch.Elapsed);
                var reset = now.AddHours(1);
                await _recoveryStore.UpsertHistoricalAllowanceBudgetAsync(
                    new HistoricalAllowanceBudget(0, reset, now, reset, ResetEstimated: true),
                    cancellationToken);
                await DeferAsync(candidate, reset, exception.Message, cancellationToken);
                return false;
            }
            catch (TradingGatewayException exception) when (IsPermanent(exception))
            {
                _activityMetrics.RecordRecoveryFailed(stopwatch.Elapsed);
                await _recoveryStore.UpsertRecoveryWorkItemAsync(candidate with
                {
                    Status = MarketDataRecoveryWorkStatus.Blocked,
                    NextAttemptUtc = DateTimeOffset.MaxValue,
                    AttemptCount = candidate.AttemptCount + 1,
                    LastFailure = exception.Message,
                }, cancellationToken);
                _logger.LogWarning(exception, "Blocked automatic market-data recovery for {Instrument}.", candidate.Instrument);
                return false;
            }
            catch (TradingGatewayException exception)
            {
                _activityMetrics.RecordRecoveryFailed(stopwatch.Elapsed);
                var next = NextRetry(candidate, now, exception.ErrorCode == TradingErrorCode.MarketClosed);
                await DeferAsync(candidate, next, exception.Message, cancellationToken);
                _logger.LogWarning(exception, "Deferred automatic market-data recovery for {Instrument} until {NextAttemptUtc}.", candidate.Instrument, next);
                return false;
            }
            catch
            {
                _activityMetrics.RecordRecoveryFailed(stopwatch.Elapsed);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static MarketDataRecoveryWorkItem? SelectCandidate(
        IReadOnlyList<MarketDataRecoveryWorkItem> items,
        MarketDataRecoveryMode mode,
        HistoricalAllowanceBudget? budget,
        DateTimeOffset now)
    {
        var due = items.Where(item => item.Status == MarketDataRecoveryWorkStatus.Pending && item.NextAttemptUtc <= now);
        var deployment = due
            .Where(item => item.Reason == MarketDataRecoveryReason.DeploymentContinuity)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.ToUtc)
            .FirstOrDefault();
        if (deployment is not null)
        {
            return deployment;
        }

        var recent = due
            .Where(item => item.Reason == MarketDataRecoveryReason.RecentTail)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.ToUtc)
            .FirstOrDefault();
        if (recent is not null)
        {
            return recent;
        }

        if (mode != MarketDataRecoveryMode.RecentAndHistorical || budget is null || budget.RemainingPoints is null || budget.ResetAtUtc is null)
        {
            return null;
        }

        if (budget.RemainingPoints <= 0 || budget.NextBackgroundAttemptUtc > now)
        {
            return null;
        }

        return due
            .Where(item => item.Reason == MarketDataRecoveryReason.HistoricalAudit)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.ToUtc)
            .FirstOrDefault();
    }

    private async Task<PriceSeries> FetchAsync(GetPricesRequest request, CancellationToken cancellationToken)
    {
        _session ??= await _gateway.AuthenticateAsync(cancellationToken);
        try
        {
            return await _gateway.GetPricesAsync(request, cancellationToken);
        }
        catch (TradingGatewayException exception) when (exception.ErrorCode is TradingErrorCode.AuthenticationFailed or TradingErrorCode.SessionExpired)
        {
            _session = await _gateway.AuthenticateAsync(cancellationToken);
            return await _gateway.GetPricesAsync(request, cancellationToken);
        }
    }

    private async Task EnforceRateAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        while (_requests.TryPeek(out var request) && request <= now.AddMinutes(-1))
        {
            _requests.Dequeue();
        }

        if (_requests.Count >= _options.MaximumRequestsPerMinute)
        {
            await Task.Delay(_requests.Peek().AddMinutes(1) - now, cancellationToken);
        }
    }

    private async Task CompleteAsync(MarketDataRecoveryWorkItem item, DateTimeOffset cursorUtc, CancellationToken cancellationToken)
        => await _recoveryStore.UpsertRecoveryWorkItemAsync(item with
        {
            CursorUtc = cursorUtc,
            Status = MarketDataRecoveryWorkStatus.Completed,
            NextAttemptUtc = DateTimeOffset.MaxValue,
            AttemptCount = 0,
            LastFailure = null,
        }, cancellationToken);

    private async Task DeferAsync(MarketDataRecoveryWorkItem item, DateTimeOffset nextAttemptUtc, string message, CancellationToken cancellationToken)
        => await _recoveryStore.UpsertRecoveryWorkItemAsync(item with
        {
            Status = MarketDataRecoveryWorkStatus.Pending,
            NextAttemptUtc = nextAttemptUtc,
            AttemptCount = item.AttemptCount + 1,
            LastFailure = message,
        }, cancellationToken);

    private HistoricalAllowanceBudget? UpdateBudget(
        HistoricalAllowanceBudget? current,
        HistoricalPriceAllowance? allowance,
        MarketDataRecoveryReason reason,
        DateTimeOffset now)
    {
        if (allowance is null)
        {
            return current;
        }

        var reset = allowance.ResetAfter is { } resetAfter ? now.Add(resetAfter) : current?.ResetAtUtc;
        DateTimeOffset? nextBackground = current?.NextBackgroundAttemptUtc;
        if (reason == MarketDataRecoveryReason.HistoricalAudit && allowance.Remaining is { } remaining && reset is { } resetAt)
        {
            var spendable = Math.Max(0, remaining - _options.UrgentAllowanceReservePoints);
            var chunks = Math.Max(1, spendable / Math.Max(1, _options.BarsPerRequest));
            var spacing = TimeSpan.FromTicks(Math.Max(1, (resetAt - now).Ticks / chunks));
            nextBackground = spendable >= _options.BarsPerRequest ? now.Add(spacing) : resetAt;
        }

        return new HistoricalAllowanceBudget(allowance.Remaining, reset, now, nextBackground);
    }

    private static bool IsKnownExhausted(HistoricalAllowanceBudget? budget, DateTimeOffset now)
        => budget?.RemainingPoints is <= 0 && budget.ResetAtUtc > now;

    private bool CanSpendBackground(HistoricalAllowanceBudget? budget, DateTimeOffset now)
        => budget?.RemainingPoints is { } remaining
            && budget.ResetAtUtc is { } resetAtUtc
            && resetAtUtc > now
            && (budget.NextBackgroundAttemptUtc is null || budget.NextBackgroundAttemptUtc <= now)
            && remaining - _options.UrgentAllowanceReservePoints >= _options.BarsPerRequest;

    private static bool IsAllowanceFailure(TradingGatewayException exception)
        => exception.Message.Contains("allowance", StringComparison.OrdinalIgnoreCase);

    private static bool IsPermanent(TradingGatewayException exception)
        => exception.ErrorCode is TradingErrorCode.InvalidInstrument or TradingErrorCode.InvalidRequest
            || exception.Message.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset NextRetry(MarketDataRecoveryWorkItem item, DateTimeOffset now, bool marketClosed)
    {
        if (marketClosed)
        {
            return now.AddHours(1);
        }

        var minutes = Math.Min(60, 1 << Math.Min(6, item.AttemptCount));
        return now.AddMinutes(minutes);
    }
}
