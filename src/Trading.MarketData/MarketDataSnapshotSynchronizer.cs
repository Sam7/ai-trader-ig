using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Trading.MarketData;

public sealed class MarketDataSnapshotSynchronizer
{
    private readonly IMarketDataSnapshotObjectStore _objectStore;
    private readonly MarketDataSnapshotValidator _validator;
    private readonly IMarketDataSnapshotImporter _importer;
    private readonly FileMarketDataMirrorStateStore _stateStore;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataSnapshotSynchronizer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MarketDataSnapshotSynchronizer(
        IMarketDataSnapshotObjectStore objectStore,
        MarketDataSnapshotValidator validator,
        IMarketDataSnapshotImporter importer,
        FileMarketDataMirrorStateStore stateStore,
        IMarketDataClock clock,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataSnapshotSynchronizer> logger)
    {
        _objectStore = objectStore;
        _validator = validator;
        _importer = importer;
        _stateStore = stateStore;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MarketDataSnapshotRefreshResult> SynchronizeOnceAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _options.CloudSnapshot;
        var mirror = snapshot.Mirror;
        if (!mirror.Enabled)
        {
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Disabled, "Cloud mirror is disabled.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.BucketName) || string.IsNullOrWhiteSpace(snapshot.ObjectName))
        {
            var result = new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, "Snapshot bucket and object name are required.");
            await SaveAttemptAsync(await _stateStore.LoadAsync(cancellationToken), result, cancellationToken);
            return result;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.AlreadyRunning, "Cloud mirror synchronization is already running.");
        }

        FileStream? lockStream = null;
        try
        {
            lockStream = TryAcquireFileLock(mirror.LockPath);
            if (lockStream is null)
            {
                return new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.AlreadyRunning, "Cloud mirror synchronization is already running in another process.");
            }

            var state = await _stateStore.LoadAsync(cancellationToken);
            var remote = await _objectStore.GetAsync(snapshot.BucketName, snapshot.ObjectName, cancellationToken);
            if (remote is null)
            {
                var result = new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, $"Remote snapshot was not found: gs://{snapshot.BucketName}/{snapshot.ObjectName}");
                await SaveAttemptAsync(state, result, cancellationToken);
                return result;
            }

            if (IsUnchanged(state, remote))
            {
                var result = new MarketDataSnapshotRefreshResult(
                    MarketDataSnapshotRefreshStatus.Unchanged,
                    "Remote snapshot is unchanged.",
                    remote.Generation,
                    remote.Sha256,
                    state?.LocalSnapshotPath,
                    LatestBarUtc: state?.LatestBarUtc);
                await SaveAttemptAsync(state, result, cancellationToken);
                return result;
            }

            Directory.CreateDirectory(mirror.SnapshotDirectory);
            var tempPath = Path.Combine(mirror.SnapshotDirectory, $".download-{Guid.NewGuid():N}.sqlite.tmp");
            try
            {
                await _objectStore.DownloadAsync(snapshot.BucketName, snapshot.ObjectName, tempPath, cancellationToken);
                var validation = await _validator.ValidateAsync(tempPath, cancellationToken);

                if (state?.LatestBarUtc is DateTimeOffset latestImported
                    && validation.LatestBarUtc is DateTimeOffset candidateLatest
                    && candidateLatest < latestImported)
                {
                    var result = new MarketDataSnapshotRefreshResult(
                        MarketDataSnapshotRefreshStatus.OlderSnapshotRejected,
                        $"Remote snapshot latest bar {candidateLatest:O} is older than current mirror {latestImported:O}.",
                        remote.Generation,
                        validation.Sha256,
                        LatestBarUtc: candidateLatest);
                    await SaveAttemptAsync(state, result, cancellationToken);
                    return result;
                }

                var finalPath = BuildSnapshotPath(mirror.SnapshotDirectory, remote.Generation, validation.Sha256);
                if (!File.Exists(finalPath))
                {
                    File.Move(tempPath, finalPath);
                }

                var import = await _importer.ImportSnapshotAsync(finalPath, cancellationToken);
                var success = new MarketDataSnapshotRefreshResult(
                    MarketDataSnapshotRefreshStatus.Succeeded,
                    "Remote snapshot synchronized.",
                    remote.Generation,
                    validation.Sha256,
                    finalPath,
                    import.SnapshotFinalPriceBarCount,
                    import.LatestBarUtc);
                await SaveSuccessAsync(state, success, cancellationToken);
                PruneOldSnapshots(mirror.SnapshotDirectory, finalPath, mirror.RetainedSnapshotCount);

                _logger.LogInformation(
                    "Synchronized market-data snapshot from gs://{Bucket}/{Object}. Bars: {BarCount}. Latest bar UTC: {LatestBarUtc}. Generation: {Generation}. SHA-256: {Sha256}.",
                    snapshot.BucketName,
                    snapshot.ObjectName,
                    import.SnapshotFinalPriceBarCount,
                    import.LatestBarUtc,
                    remote.Generation,
                    validation.Sha256);

                return success;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to synchronize market-data snapshot.");
            var state = await _stateStore.LoadAsync(CancellationToken.None);
            var result = new MarketDataSnapshotRefreshResult(MarketDataSnapshotRefreshStatus.Failed, exception.Message);
            await SaveAttemptAsync(state, result, CancellationToken.None);
            return result;
        }
        finally
        {
            await (lockStream?.DisposeAsync() ?? ValueTask.CompletedTask);
            _gate.Release();
        }
    }

    private async Task SaveAttemptAsync(
        MarketDataMirrorState? previous,
        MarketDataSnapshotRefreshResult result,
        CancellationToken cancellationToken)
    {
        await _stateStore.SaveAsync(new MarketDataMirrorState(
            _clock.UtcNow,
            previous?.LastSuccessfulSyncUtc,
            previous?.RemoteGeneration,
            previous?.RemoteSha256,
            previous?.LocalSnapshotPath,
            previous?.LatestBarUtc,
            result.Status,
            result.Message), cancellationToken);
    }

    private async Task SaveSuccessAsync(
        MarketDataMirrorState? previous,
        MarketDataSnapshotRefreshResult result,
        CancellationToken cancellationToken)
    {
        _ = previous;
        await _stateStore.SaveAsync(new MarketDataMirrorState(
            _clock.UtcNow,
            _clock.UtcNow,
            result.RemoteGeneration,
            result.RemoteSha256,
            result.LocalSnapshotPath,
            result.LatestBarUtc,
            result.Status,
            result.Message), cancellationToken);
    }

    private static bool IsUnchanged(MarketDataMirrorState? state, MarketDataSnapshotObject remote)
    {
        if (state is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(remote.Generation)
            && string.Equals(state.RemoteGeneration, remote.Generation, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(remote.Sha256)
                || string.Equals(state.RemoteSha256, remote.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static FileStream? TryAcquireFileLock(string lockPath)
    {
        var fullPath = Path.GetFullPath(lockPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            return new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string BuildSnapshotPath(string snapshotDirectory, string? generation, string sha256)
    {
        var safeGeneration = string.IsNullOrWhiteSpace(generation)
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : string.Concat(generation.Where(char.IsLetterOrDigit));
        return Path.Combine(snapshotDirectory, $"snapshot-{safeGeneration}-{sha256[..Math.Min(12, sha256.Length)]}.sqlite");
    }

    private static void PruneOldSnapshots(
        string snapshotDirectory,
        string currentSnapshotPath,
        int retainedSnapshotCount)
    {
        if (retainedSnapshotCount <= 0)
        {
            return;
        }

        var current = Path.GetFullPath(currentSnapshotPath);
        var candidates = Directory.EnumerateFiles(snapshotDirectory, "snapshot-*.sqlite")
            .Select(path => new FileInfo(path))
            .Where(file => !string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(Math.Max(0, retainedSnapshotCount - 1))
            .ToArray();

        foreach (var file in candidates)
        {
            file.Delete();
        }
    }
}
