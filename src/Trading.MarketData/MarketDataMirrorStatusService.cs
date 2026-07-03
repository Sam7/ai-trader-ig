using Microsoft.Extensions.Options;

namespace Trading.MarketData;

public sealed class MarketDataMirrorStatusService
{
    private readonly FileMarketDataMirrorStateStore _stateStore;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataOptions _options;

    public MarketDataMirrorStatusService(
        FileMarketDataMirrorStateStore stateStore,
        IMarketDataClock clock,
        IOptions<MarketDataOptions> options)
    {
        _stateStore = stateStore;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<MarketDataMirrorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var mirror = _options.CloudSnapshot.Mirror;
        var snapshot = _options.CloudSnapshot;
        var state = await _stateStore.LoadAsync(cancellationToken);
        var isConfigured = !string.IsNullOrWhiteSpace(snapshot.BucketName)
            && !string.IsNullOrWhiteSpace(snapshot.ObjectName);
        var isStale = mirror.Enabled
            && (state?.LastSuccessfulSyncUtc is not DateTimeOffset lastSuccess
                || _clock.UtcNow - lastSuccess > mirror.StaleAfter);

        return new MarketDataMirrorStatus(
            mirror.Enabled,
            isConfigured,
            isStale,
            state?.LastAttemptUtc,
            state?.LastSuccessfulSyncUtc,
            state?.LatestBarUtc,
            state?.RemoteGeneration,
            state?.RemoteSha256,
            state?.LocalSnapshotPath,
            state?.LastStatus ?? MarketDataSnapshotRefreshStatus.Disabled,
            state?.LastMessage);
    }
}
