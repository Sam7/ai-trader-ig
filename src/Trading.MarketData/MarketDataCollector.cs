using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class MarketDataCollector : IMarketDataCollector
{
    private readonly IMarketDataStreamClient _streamClient;
    private readonly IMarketDataStore _store;
    private readonly IMarketDataHealthStore _healthStore;
    private readonly ITradingGateway _tradingGateway;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataCollectorOptions _options;
    private readonly ILogger<MarketDataCollector> _logger;
    private ITradingSession? _session;

    public MarketDataCollector(
        IMarketDataStreamClient streamClient,
        IMarketDataStore store,
        IMarketDataHealthStore healthStore,
        ITradingGateway tradingGateway,
        IMarketDataClock clock,
        IOptions<MarketDataCollectorOptions> options,
        ILogger<MarketDataCollector> logger)
    {
        _streamClient = streamClient;
        _store = store;
        _healthStore = healthStore;
        _tradingGateway = tradingGateway;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(
        IReadOnlyList<InstrumentId> instruments,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (instruments.Count == 0)
        {
            return;
        }

        var resolution = _options.Resolution;
        var subscriptions = instruments
            .Select(instrument => new MarketDataStreamSubscription(instrument, resolution))
            .ToArray();

        await using var session = await _streamClient.StartAsync(subscriptions, IngestStreamUpdateAsync, cancellationToken);
        foreach (var instrument in instruments)
        {
            await UpsertHealthAsync(
                instrument,
                resolution,
                MarketDataConnectionState.Connected,
                repairState: MarketDataRepairState.InProgress,
                cancellationToken: cancellationToken);
        }

        foreach (var instrument in instruments)
        {
            await RepairMissingCompletedCandlesAsync(instrument, resolution, cancellationToken);
        }

        if (duration > TimeSpan.Zero)
        {
            await Task.Delay(duration, cancellationToken);
        }
    }

    private async Task IngestStreamUpdateAsync(
        StreamPriceBarUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.Resolution != _options.Resolution)
        {
            _logger.LogWarning(
                "Ignoring stream update for {Instrument} at unsupported resolution {Resolution}.",
                update.Instrument,
                update.Resolution);
            return;
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
        ],
        cancellationToken);

        var existing = await _healthStore.GetAsync(update.Instrument, update.Resolution, cancellationToken);
        await _healthStore.UpsertAsync(
            BuildHealth(
                update.Instrument,
                update.Resolution,
                MarketDataConnectionState.Connected,
                update.ObservedAtUtc,
                update.IsFinal ? update.Bar.TimestampUtc : existing?.LatestCompletedCandleUtc,
                existing?.RepairState ?? MarketDataRepairState.Idle,
                existing?.UnresolvedGaps ?? [],
                existing?.LastHistoricalRepairStatus,
                existing?.LastHistoricalRepairMessage),
            cancellationToken);
    }

    private async Task RepairMissingCompletedCandlesAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken)
    {
        var interval = PriceResolutionIntervals.ToTimeSpan(resolution);
        var latestFinal = await _store.GetLatestFinalAsync(instrument, resolution, cancellationToken);
        var fromUtc = latestFinal is null
            ? PriceResolutionIntervals.AlignDown(_clock.UtcNow.Subtract(_options.BootstrapLookback), interval)
            : latestFinal.Bar.TimestampUtc.Add(interval);
        var toUtc = PriceResolutionIntervals.AlignDown(_clock.UtcNow, interval);

        if (fromUtc >= toUtc)
        {
            await UpsertHealthAsync(
                instrument,
                resolution,
                MarketDataConnectionState.Connected,
                repairState: MarketDataRepairState.Idle,
                latestCompletedCandleUtc: latestFinal?.Bar.TimestampUtc,
                cancellationToken: cancellationToken);
            return;
        }

        var gaps = await _store.FindMissingCompletedRangesAsync(instrument, resolution, fromUtc, toUtc, cancellationToken);
        MarketDataCoverageStatus? lastRepairStatus = null;
        string? lastRepairMessage = null;
        foreach (var gap in gaps)
        {
            try
            {
                await EnsureAuthenticatedAsync(cancellationToken);
                var series = await _tradingGateway.GetPricesAsync(
                    new GetPricesRequest(instrument, resolution, FromUtc: gap.FromUtc, ToUtc: gap.ToUtc),
                    cancellationToken);
                var bars = series.Bars
                    .Where(bar => bar.TimestampUtc >= gap.FromUtc && bar.TimestampUtc < gap.ToUtc)
                    .Select(bar => StoredPriceBar.FromPriceBar(instrument, resolution, bar, MarketDataSource.RestBackfill))
                    .ToArray();

                if (bars.Length == 0)
                {
                    lastRepairStatus = MarketDataCoverageStatus.NoBars;
                    lastRepairMessage = "IG returned no bars for completed gap.";
                    await RecordCoverageAsync(
                        instrument,
                        resolution,
                        gap,
                        MarketDataCoverageStatus.NoBars,
                        message: lastRepairMessage,
                        brokerErrorCode: null,
                        cancellationToken);
                }
                else
                {
                    lastRepairStatus = MarketDataCoverageStatus.BarsReturned;
                    lastRepairMessage = null;
                    await _store.UpsertAsync(bars, cancellationToken);
                    await RecordCoverageAsync(
                        instrument,
                        resolution,
                        gap,
                        MarketDataCoverageStatus.BarsReturned,
                        message: null,
                        brokerErrorCode: null,
                        cancellationToken);
                }
            }
            catch (TradingGatewayException exception) when (IsAllowanceFailure(exception))
            {
                await RecordCoverageAsync(
                    instrument,
                    resolution,
                    gap,
                    MarketDataCoverageStatus.AllowanceBlocked,
                    exception.Message,
                    exception.ErrorCode.ToString(),
                    cancellationToken);
                await UpsertHealthAsync(
                    instrument,
                    resolution,
                    MarketDataConnectionState.Connected,
                    repairState: MarketDataRepairState.Degraded,
                    unresolvedGaps: gaps,
                    lastHistoricalRepairStatus: MarketDataCoverageStatus.AllowanceBlocked,
                    lastHistoricalRepairMessage: exception.Message,
                    cancellationToken: cancellationToken);
                return;
            }
            catch (TradingGatewayException exception)
            {
                await RecordCoverageAsync(
                    instrument,
                    resolution,
                    gap,
                    MarketDataCoverageStatus.Failed,
                    exception.Message,
                    exception.ErrorCode.ToString(),
                    cancellationToken);
                await UpsertHealthAsync(
                    instrument,
                    resolution,
                    MarketDataConnectionState.Connected,
                    repairState: MarketDataRepairState.Failed,
                    unresolvedGaps: gaps,
                    lastHistoricalRepairStatus: MarketDataCoverageStatus.Failed,
                    lastHistoricalRepairMessage: exception.Message,
                    cancellationToken: cancellationToken);
                return;
            }
        }

        var existingHealth = await _healthStore.GetAsync(instrument, resolution, cancellationToken);
        var latestAfterRepair = await _store.GetLatestFinalAsync(instrument, resolution, cancellationToken);
        var remainingGaps = await _store.FindMissingCompletedRangesAsync(instrument, resolution, fromUtc, toUtc, cancellationToken);
        await UpsertHealthAsync(
            instrument,
            resolution,
            MarketDataConnectionState.Connected,
            repairState: remainingGaps.Count == 0 ? MarketDataRepairState.Idle : MarketDataRepairState.Degraded,
            latestCompletedCandleUtc: latestAfterRepair?.Bar.TimestampUtc,
            unresolvedGaps: remainingGaps,
            lastHistoricalRepairStatus: lastRepairStatus ?? existingHealth?.LastHistoricalRepairStatus,
            lastHistoricalRepairMessage: lastRepairMessage ?? existingHealth?.LastHistoricalRepairMessage,
            cancellationToken: cancellationToken);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        _session ??= await _tradingGateway.AuthenticateAsync(cancellationToken);
    }

    private async Task RecordCoverageAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        MarketDataGap gap,
        MarketDataCoverageStatus status,
        string? message,
        string? brokerErrorCode,
        CancellationToken cancellationToken)
    {
        await _store.RecordCoverageAsync(
            new MarketDataCoverageRecord(
                instrument,
                resolution,
                gap.FromUtc,
                gap.ToUtc,
                status,
                _clock.UtcNow,
                message,
                brokerErrorCode),
            cancellationToken);
    }

    private async Task UpsertHealthAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        MarketDataConnectionState connectionState,
        DateTimeOffset? lastReceivedUpdateUtc = null,
        DateTimeOffset? latestCompletedCandleUtc = null,
        MarketDataRepairState repairState = MarketDataRepairState.Idle,
        IReadOnlyList<MarketDataGap>? unresolvedGaps = null,
        MarketDataCoverageStatus? lastHistoricalRepairStatus = null,
        string? lastHistoricalRepairMessage = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await _healthStore.GetAsync(instrument, resolution, cancellationToken);
        await _healthStore.UpsertAsync(
            BuildHealth(
                instrument,
                resolution,
                connectionState,
                lastReceivedUpdateUtc ?? existing?.LastReceivedUpdateUtc,
                latestCompletedCandleUtc ?? existing?.LatestCompletedCandleUtc,
                repairState,
                unresolvedGaps ?? existing?.UnresolvedGaps ?? [],
                lastHistoricalRepairStatus ?? existing?.LastHistoricalRepairStatus,
                lastHistoricalRepairMessage ?? existing?.LastHistoricalRepairMessage),
            cancellationToken);
    }

    private MarketDataHealthRecord BuildHealth(
        InstrumentId instrument,
        PriceResolution resolution,
        MarketDataConnectionState connectionState,
        DateTimeOffset? lastReceivedUpdateUtc,
        DateTimeOffset? latestCompletedCandleUtc,
        MarketDataRepairState repairState,
        IReadOnlyList<MarketDataGap> unresolvedGaps,
        MarketDataCoverageStatus? lastHistoricalRepairStatus,
        string? lastHistoricalRepairMessage)
        => new(
            instrument,
            resolution,
            connectionState,
            lastReceivedUpdateUtc,
            latestCompletedCandleUtc,
            repairState,
            unresolvedGaps,
            lastHistoricalRepairStatus,
            lastHistoricalRepairMessage,
            _clock.UtcNow);

    private static bool IsAllowanceFailure(TradingGatewayException exception)
        => exception.Message.Contains("allowance", StringComparison.OrdinalIgnoreCase);
}
