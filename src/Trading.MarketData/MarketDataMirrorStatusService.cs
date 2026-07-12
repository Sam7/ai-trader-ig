using Microsoft.Extensions.Options;

namespace Trading.MarketData;

public sealed class MarketDataMirrorStatusService
{
    private readonly FileMarketDataMirrorStateStore _stateStore;
    private readonly IMarketDataSnapshotObjectStore _objectStore;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataOptions _options;

    public MarketDataMirrorStatusService(
        FileMarketDataMirrorStateStore stateStore,
        IMarketDataSnapshotObjectStore objectStore,
        IMarketDataClock clock,
        IOptions<MarketDataOptions> options)
    {
        _stateStore = stateStore;
        _objectStore = objectStore;
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
        MarketDataSnapshotObject? remote = null;
        var remoteChecked = false;
        if (isConfigured)
        {
            remoteChecked = true;
            remote = await _objectStore.GetAsync(snapshot.BucketName, snapshot.ObjectName, cancellationToken);
        }

        var remoteUpdatedUtc = remote?.UpdatedUtc;
        var remoteLatestBarUtc = remote?.LatestBarUtc;
        var isRemoteObjectStale = remoteUpdatedUtc is not null && _clock.UtcNow - remoteUpdatedUtc > mirror.StaleAfter;
        var isRemoteLatestBarStale = remoteLatestBarUtc is not null && _clock.UtcNow - remoteLatestBarUtc > mirror.StaleAfter;
        var diagnosis = BuildDiagnosis(mirror.Enabled, isConfigured, isStale, remote, isRemoteObjectStale, isRemoteLatestBarStale);

        return new MarketDataMirrorStatus(
            mirror.Enabled,
            isConfigured,
            isStale,
            remoteChecked,
            isRemoteObjectStale,
            isRemoteLatestBarStale,
            state?.LastAttemptUtc,
            state?.LastSuccessfulSyncUtc,
            state?.LatestBarUtc,
            remoteUpdatedUtc,
            remoteLatestBarUtc,
            remote?.Generation ?? state?.RemoteGeneration,
            remote?.Sha256 ?? state?.RemoteSha256,
            state?.LocalSnapshotPath,
            state?.LastStatus ?? MarketDataSnapshotRefreshStatus.Disabled,
            state?.LastMessage,
            diagnosis);
    }

    private static string BuildDiagnosis(
        bool enabled,
        bool configured,
        bool localStale,
        MarketDataSnapshotObject? remote,
        bool remoteObjectStale,
        bool remoteLatestBarStale)
    {
        if (!enabled)
        {
            return "Cloud mirror is disabled.";
        }

        if (!configured)
        {
            return "Cloud mirror bucket/object is not configured.";
        }

        if (remote is null)
        {
            return "Remote snapshot object was not found.";
        }

        if (remoteObjectStale || remoteLatestBarStale)
        {
            return "Remote snapshot is stale; the publisher or worker is not producing fresh market data.";
        }

        if (localStale)
        {
            return "Local mirror synchronization is stale even though the remote object appears fresh.";
        }

        return "Remote snapshot and local mirror state appear fresh.";
    }
}
