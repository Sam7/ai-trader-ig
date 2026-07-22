using Microsoft.Extensions.Logging;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataHistoricalBackfillService
{
    private readonly IMarketDataStore _store;
    private readonly ITradingGateway _tradingGateway;
    private readonly ILogger<MarketDataHistoricalBackfillService> _logger;
    private ITradingSession? _session;

    public MarketDataHistoricalBackfillService(
        IMarketDataStore store,
        ITradingGateway tradingGateway,
        ILogger<MarketDataHistoricalBackfillService> logger)
    {
        _store = store;
        _tradingGateway = tradingGateway;
        _logger = logger;
    }

    public async Task<MarketDataHistoricalBackfillResult> BackfillAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc >= toUtc)
        {
            throw new ArgumentException("Backfill from time must be earlier than to time.");
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var series = await _tradingGateway.GetPricesAsync(
            new GetPricesRequest(instrument, resolution, FromUtc: fromUtc, ToUtc: toUtc),
            cancellationToken);
        var bars = series.Bars
            .Where(bar => bar.TimestampUtc >= fromUtc && bar.TimestampUtc < toUtc)
            .Select(bar => StoredPriceBar.FromPriceBar(instrument, resolution, bar, MarketDataSource.RestBackfill))
            .ToArray();

        await _store.UpsertAsync(bars, cancellationToken);

        _logger.LogInformation(
            "Explicitly backfilled {BarCount} historical bar(s) for {Instrument} from IG REST. Remaining historical allowance: {RemainingAllowance}; reset after: {ResetAfter}.",
            bars.Length,
            instrument,
            series.Allowance?.Remaining,
            series.Allowance?.ResetAfter);

        return new MarketDataHistoricalBackfillResult(bars.Length, series.Allowance);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        _session ??= await _tradingGateway.AuthenticateAsync(cancellationToken);
    }
}

public sealed record MarketDataHistoricalBackfillResult(
    int BarCount,
    HistoricalPriceAllowance? Allowance);
