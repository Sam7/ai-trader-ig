using Trading.Abstractions;

namespace Trading.MarketData;

public interface IMarketDataStore
{
    Task UpsertAsync(
        IReadOnlyList<StoredPriceBar> bars,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredPriceBar>> GetRangeAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<StoredPriceBar?> GetLatestFinalAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketDataGap>> FindMissingCompletedRangesAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task RecordCoverageAsync(
        MarketDataCoverageRecord coverage,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketDataCoverageRecord>> GetCoverageAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
