using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataRecoveryOptions
{
    public TimeSpan Horizon { get; init; } = TimeSpan.FromDays(14);
    public int BarsPerRequest { get; init; } = 250;
    public int MaximumRequestsPerMinute { get; init; } = 25;
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(3);
}

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
}
