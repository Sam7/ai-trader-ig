using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataRecoveryOptions
{
    public MarketDataRecoveryMode Mode { get; init; } = MarketDataRecoveryMode.Disabled;
    public TimeSpan Horizon { get; init; } = TimeSpan.FromDays(14);
    public TimeSpan RecentLookback { get; init; } = TimeSpan.FromHours(2);
    public TimeSpan TailAuditInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan HistoricalAuditInterval { get; init; } = TimeSpan.FromDays(1);
    public int BarsPerRequest { get; init; } = 250;
    public int MaximumRequestsPerMinute { get; init; } = 3;
    public int UrgentAllowanceReservePoints { get; init; } = 2_000;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException("Market-data recovery mode is invalid.");
        }

        if (Horizon <= TimeSpan.Zero || RecentLookback <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Market-data recovery horizons must be greater than zero.");
        }

        if (TailAuditInterval <= TimeSpan.Zero || HistoricalAuditInterval <= TimeSpan.Zero || PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Market-data recovery intervals must be greater than zero.");
        }

        if (BarsPerRequest <= 0 || MaximumRequestsPerMinute <= 0 || UrgentAllowanceReservePoints < 0)
        {
            throw new InvalidOperationException("Market-data recovery request and allowance settings are invalid.");
        }
    }
}

public enum MarketDataRecoveryMode
{
    Disabled = 0,
    Observe = 1,
    RecentOnly = 2,
    RecentAndHistorical = 3,
}

public enum MarketDataRecoveryReason
{
    RecentTail = 1,
    HistoricalAudit = 2,
    DeploymentContinuity = 3,
}

public enum MarketDataRecoveryWorkStatus
{
    Pending = 1,
    Completed = 2,
    Blocked = 3,
}

public sealed record MarketDataRecoveryWorkItem(
    InstrumentId Instrument,
    PriceResolution Resolution,
    MarketDataRecoveryReason Reason,
    int Priority,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset CursorUtc,
    MarketDataRecoveryWorkStatus Status,
    DateTimeOffset NextAttemptUtc,
    int AttemptCount,
    int ReturnedPoints,
    string? LastFailure = null);

public sealed record HistoricalAllowanceBudget(
    int? RemainingPoints,
    DateTimeOffset? ResetAtUtc,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? NextBackgroundAttemptUtc = null,
    bool ResetEstimated = false);

public sealed record MarketDataRecoveryTarget(InstrumentId Instrument, int Priority);

public sealed record MarketDataRecoveryState(
    InstrumentId Instrument,
    PriceResolution Resolution,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset CursorUtc,
    bool IsComplete,
    int ReturnedPoints,
    int? RemainingAllowance,
    DateTimeOffset? AllowanceExpiresAtUtc,
    string? LastFailure);

public sealed record MarketDataRecoveryStatus(
    MarketDataRecoveryState? Active,
    int PendingRanges,
    int? RemainingAllowance,
    DateTimeOffset? AllowanceExpiresAtUtc,
    IReadOnlyList<MarketDataRecoveryState> BlockedRanges);

public interface IMarketDataRecoveryStore
{
    Task UpsertRecoveryStateAsync(MarketDataRecoveryState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketDataRecoveryState>> GetRecoveryStatesAsync(CancellationToken cancellationToken = default);
    Task UpsertRecoveryWorkItemAsync(MarketDataRecoveryWorkItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketDataRecoveryWorkItem>> GetRecoveryWorkItemsAsync(CancellationToken cancellationToken = default);
    Task<HistoricalAllowanceBudget?> GetHistoricalAllowanceBudgetAsync(CancellationToken cancellationToken = default);
    Task UpsertHistoricalAllowanceBudgetAsync(HistoricalAllowanceBudget budget, CancellationToken cancellationToken = default);
}
