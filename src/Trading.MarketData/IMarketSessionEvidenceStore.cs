using Trading.Abstractions;

namespace Trading.MarketData;

public interface IMarketSessionEvidenceStore
{
    Task UpsertSessionStatusAsync(
        MarketSessionStatusRecord status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketSessionStatusRecord>> GetSessionStatusAsync(
        InstrumentId instrument,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
