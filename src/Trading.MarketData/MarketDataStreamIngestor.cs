using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trading.MarketData;

public sealed class MarketDataStreamIngestor
{
    private readonly IMarketDataStore _store;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataStreamIngestor> _logger;

    public MarketDataStreamIngestor(
        IMarketDataStore store,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataStreamIngestor> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MarketDataIngestResult> IngestAsync(
        StreamPriceBarUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (update.Resolution != _options.CanonicalResolution)
        {
            var reason = $"Stream resolution '{update.Resolution}' does not match canonical resolution '{_options.CanonicalResolution}'.";
            _logger.LogWarning(
                "Rejected stream price bar for {Instrument}. {Reason}",
                update.Instrument,
                reason);
            return new MarketDataIngestResult(MarketDataIngestStatus.UnsupportedResolution, reason);
        }

        await _store.UpsertAsync(
        [
            StoredPriceBar.FromPriceBar(
                update.Instrument,
                update.Resolution,
                update.Bar,
                MarketDataSource.Stream,
                update.IsFinal,
                update.ObservedAtUtc),
        ], cancellationToken);

        return new MarketDataIngestResult(MarketDataIngestStatus.Stored);
    }
}
