using Trading.Abstractions;

namespace Trading.MarketData;

public enum MarketDataConnectionState
{
    Unknown = 0,
    Connected = 1,
    Disconnected = 2,
    Failed = 3,
}
public enum MarketDataRepairState
{
    Unknown = 0,
    Idle = 1,
    InProgress = 2,
    Degraded = 3,
    Failed = 4,
}

public sealed record MarketDataHealthRecord(
    InstrumentId Instrument,
    PriceResolution Resolution,
    MarketDataConnectionState ConnectionState,
    DateTimeOffset? LastReceivedUpdateUtc,
    DateTimeOffset? LatestCompletedCandleUtc,
    MarketDataRepairState RepairState,
    IReadOnlyList<MarketDataGap> UnresolvedGaps,
    MarketDataCoverageStatus? LastHistoricalRepairStatus,
    string? LastHistoricalRepairMessage,
    DateTimeOffset UpdatedAtUtc);

public interface IMarketDataHealthStore
{
    Task UpsertAsync(MarketDataHealthRecord health, CancellationToken cancellationToken = default);

    Task<MarketDataHealthRecord?> GetAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default);
}
