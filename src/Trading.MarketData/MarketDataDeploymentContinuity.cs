using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;

namespace Trading.MarketData;

/// <summary>Bounds deployment-time market-data repair to the restart window only.</summary>
public sealed class MarketDataDeploymentContinuityService
{
    private readonly IMarketDataStore _store;
    private readonly IMarketDataHealthStore _healthStore;
    private readonly IMarketDataRecoveryStore _recoveryStore;
    private readonly IMarketSessionEvidenceStore _sessionEvidenceStore;
    private readonly MarketDataRecoveryCoordinator _recovery;
    private readonly MarketDataSnapshotPublisher _publisher;
    private readonly IMarketDataSnapshotObjectStore _snapshotObjectStore;
    private readonly IMarketDataObjectStore _objectStore;
    private readonly ITradingGateway _gateway;
    private readonly IMarketDataClock _clock;
    private readonly MarketDataOptions _marketData;
    private readonly MarketDataDeploymentContinuityOptions _options;
    private readonly MarketDataDeploymentContinuityStore _files;
    private readonly ILogger<MarketDataDeploymentContinuityService> _logger;

    public MarketDataDeploymentContinuityService(
        IMarketDataStore store,
        IMarketDataHealthStore healthStore,
        IMarketDataRecoveryStore recoveryStore,
        IMarketSessionEvidenceStore sessionEvidenceStore,
        MarketDataRecoveryCoordinator recovery,
        MarketDataSnapshotPublisher publisher,
        IMarketDataSnapshotObjectStore snapshotObjectStore,
        IMarketDataObjectStore objectStore,
        ITradingGateway gateway,
        IMarketDataClock clock,
        IOptions<MarketDataOptions> marketData,
        MarketDataDeploymentContinuityStore files,
        ILogger<MarketDataDeploymentContinuityService> logger)
    {
        _store = store;
        _healthStore = healthStore;
        _recoveryStore = recoveryStore;
        _sessionEvidenceStore = sessionEvidenceStore;
        _recovery = recovery;
        _publisher = publisher;
        _snapshotObjectStore = snapshotObjectStore;
        _objectStore = objectStore;
        _gateway = gateway;
        _clock = clock;
        _marketData = marketData.Value;
        _options = _marketData.DeploymentContinuity;
        _files = files;
        _logger = logger;
    }

    public async Task<MarketDataDeploymentCheckpoint> CreateCheckpointAsync(
        string deploymentId,
        IReadOnlyList<InstrumentId> instruments,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        _options.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        if (instruments.Count == 0)
        {
            throw new InvalidOperationException("Deployment continuity requires at least one tracked instrument.");
        }

        var capturedAtUtc = _clock.UtcNow;
        var markets = new List<MarketDataDeploymentCheckpointMarket>();
        foreach (var instrument in instruments.Distinct())
        {
            var latest = await _store.GetLatestFinalAsync(instrument, resolution, cancellationToken);
            if (latest is null)
            {
                throw new InvalidOperationException($"Cannot checkpoint {instrument}: no final market-data bar is available.");
            }

            markets.Add(new MarketDataDeploymentCheckpointMarket(instrument.Value, latest.Bar.TimestampUtc));
        }

        var snapshot = await _publisher.PublishOnceAsync(cancellationToken);
        if (snapshot.Status != MarketDataSnapshotRefreshStatus.Succeeded)
        {
            throw new InvalidOperationException($"Pre-deployment market-data snapshot failed: {snapshot.Message}");
        }

        var remote = await _snapshotObjectStore.GetAsync(
            _marketData.CloudSnapshot.BucketName,
            _marketData.CloudSnapshot.ObjectName,
            cancellationToken);
        var checkpointLatest = markets.Max(market => market.LatestFinalBarUtc);
        if (remote is null
            || string.IsNullOrWhiteSpace(snapshot.RemoteSha256)
            || string.IsNullOrWhiteSpace(remote.Sha256)
            || remote.LatestBarUtc is null
            || !string.Equals(remote.Sha256, snapshot.RemoteSha256, StringComparison.OrdinalIgnoreCase)
            || remote.LatestBarUtc < checkpointLatest)
        {
            throw new InvalidOperationException("Pre-deployment market-data snapshot could not be verified against the checkpoint.");
        }

        var checkpoint = new MarketDataDeploymentCheckpoint(
            SchemaVersion: 1,
            DeploymentId: deploymentId,
            CapturedAtUtc: capturedAtUtc,
            Resolution: resolution,
            Markets: markets,
            Snapshot: new MarketDataDeploymentSnapshot(
                remote.BucketName,
                remote.ObjectName,
                remote.Generation,
                remote.Sha256,
                remote.UpdatedUtc,
                remote.LatestBarUtc));
        await _files.WriteCheckpointAsync(checkpoint, cancellationToken);
        return checkpoint;
    }

    public Task<MarketDataDeploymentCheckpoint?> GetActiveCheckpointAsync(CancellationToken cancellationToken = default)
        => _files.GetCheckpointAsync(cancellationToken);

    public async Task<bool> WaitForPostRestartStreamAsync(
        MarketDataDeploymentCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        var deadline = _clock.UtcNow.Add(_options.ReadinessTimeout);
        while (_clock.UtcNow <= deadline)
        {
            var ready = true;
            foreach (var market in checkpoint.Markets)
            {
                var health = await _healthStore.GetAsync(new InstrumentId(market.Instrument), checkpoint.Resolution, cancellationToken);
                if (health?.ConnectionState != MarketDataConnectionState.Connected)
                {
                    ready = false;
                    break;
                }
            }

            if (ready)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        return false;
    }

    public async Task<MarketDataDeploymentContinuityReport> ReconcileAsync(
        MarketDataDeploymentCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        _options.Validate();
        var startedAtUtc = _clock.UtcNow;
        var failures = new List<string>();
        var ranges = new List<MarketDataDeploymentContinuityRange>();
        var interval = PriceResolutionIntervals.ToTimeSpan(checkpoint.Resolution);
        var cutoffUtc = PriceResolutionIntervals.AlignDown(startedAtUtc, interval);
        if (startedAtUtc - checkpoint.CapturedAtUtc > _options.MaximumGapWindow)
        {
            return await CompleteAsync(CreateReport(checkpoint, startedAtUtc, cutoffUtc, ranges, ["Deployment checkpoint is older than the maximum repair window."], MarketDataDeploymentContinuityStatus.Failed), cancellationToken);
        }

        var deadline = startedAtUtc.Add(_options.RepairTimeout);
        foreach (var market in checkpoint.Markets)
        {
            var instrument = new InstrumentId(market.Instrument);
            var fromUtc = market.LatestFinalBarUtc.Add(interval);
            var missing = await _store.FindMissingCompletedRangesAsync(instrument, checkpoint.Resolution, fromUtc, cutoffUtc, cancellationToken);
            if (missing.Count == 0)
            {
                continue;
            }

            var work = new MarketDataRecoveryWorkItem(
                instrument,
                checkpoint.Resolution,
                MarketDataRecoveryReason.DeploymentContinuity,
                Priority: -1_000_000,
                FromUtc: missing.Min(gap => gap.FromUtc),
                ToUtc: missing.Max(gap => gap.ToUtc),
                CursorUtc: missing.Min(gap => gap.FromUtc),
                Status: MarketDataRecoveryWorkStatus.Pending,
                NextAttemptUtc: _clock.UtcNow,
                AttemptCount: 0,
                ReturnedPoints: 0);
            if (work.ToUtc - work.FromUtc > _options.MaximumGapWindow)
            {
                failures.Add($"{instrument} requires a repair range larger than {_options.MaximumGapWindow}.");
                continue;
            }

            await _recoveryStore.UpsertRecoveryWorkItemAsync(work, cancellationToken);
            var result = await RepairRangeAsync(work, deadline, cancellationToken);
            ranges.Add(result);
            if (!result.Succeeded)
            {
                failures.Add(result.Message ?? $"{instrument} continuity repair failed.");
            }
        }

        foreach (var market in checkpoint.Markets)
        {
            var instrument = new InstrumentId(market.Instrument);
            var remaining = await _store.FindMissingCompletedRangesAsync(
                instrument,
                checkpoint.Resolution,
                market.LatestFinalBarUtc.Add(interval),
                cutoffUtc,
                cancellationToken);
            if (remaining.Count > 0)
            {
                failures.Add($"{instrument} still has {remaining.Count} unresolved deployment continuity range(s).");
            }
        }

        var status = failures.Count == 0
            ? MarketDataDeploymentContinuityStatus.Succeeded
            : MarketDataDeploymentContinuityStatus.Failed;
        return await CompleteAsync(CreateReport(checkpoint, startedAtUtc, cutoffUtc, ranges, failures, status), cancellationToken);
    }

    public async Task<MarketDataDeploymentContinuityReport> FailAsync(
        MarketDataDeploymentCheckpoint checkpoint,
        string message,
        CancellationToken cancellationToken = default)
        => await CompleteAsync(
            CreateReport(checkpoint, _clock.UtcNow, null, [], [message], MarketDataDeploymentContinuityStatus.Failed),
            cancellationToken);

    private async Task<MarketDataDeploymentContinuityRange> RepairRangeAsync(
        MarketDataRecoveryWorkItem original,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (_clock.UtcNow <= deadline)
        {
            var items = await _recoveryStore.GetRecoveryWorkItemsAsync(cancellationToken);
            var item = items.Single(item => item.Instrument == original.Instrument
                && item.Resolution == original.Resolution
                && item.Reason == MarketDataRecoveryReason.DeploymentContinuity);
            if (item.Status == MarketDataRecoveryWorkStatus.Blocked)
            {
                return new MarketDataDeploymentContinuityRange(item.Instrument.Value, item.FromUtc, item.ToUtc, false, false, item.ReturnedPoints, item.LastFailure);
            }

            if (item.Status == MarketDataRecoveryWorkStatus.Completed)
            {
                var noBars = (await _store.GetCoverageAsync(item.Instrument, item.Resolution, item.FromUtc, item.ToUtc, cancellationToken))
                    .Any(coverage => coverage.Status == MarketDataCoverageStatus.NoBars
                        && coverage.FromUtc <= item.FromUtc
                        && coverage.ToUtc >= item.ToUtc);
                if (!noBars)
                {
                    return new MarketDataDeploymentContinuityRange(item.Instrument.Value, item.FromUtc, item.ToUtc, true, false, item.ReturnedPoints, null);
                }

                var market = await _gateway.GetMarketDetailsAsync(item.Instrument, cancellationToken);
                if (market.Status is not (MarketStatus.Closed or MarketStatus.Suspended))
                {
                    return new MarketDataDeploymentContinuityRange(item.Instrument.Value, item.FromUtc, item.ToUtc, false, false, item.ReturnedPoints, "IG returned no bars while the market was not closed or suspended.");
                }

                var observedAtUtc = _clock.UtcNow;
                await _sessionEvidenceStore.UpsertSessionStatusAsync(
                    new MarketSessionStatusRecord(
                        item.Instrument,
                        market.Status,
                        observedAtUtc,
                        observedAtUtc.Add(PriceResolutionIntervals.ToTimeSpan(item.Resolution) * 2),
                        MarketSessionEvidenceSource.BrokerSnapshot,
                        $"Deployment continuity confirmed market status: {market.Status}."),
                    cancellationToken);
                return new MarketDataDeploymentContinuityRange(item.Instrument.Value, item.FromUtc, item.ToUtc, true, true, item.ReturnedPoints, null);
            }

            await _recovery.ProcessNextAsync(MarketDataRecoveryMode.RecentOnly, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var pending = (await _recoveryStore.GetRecoveryWorkItemsAsync(cancellationToken))
            .Single(item => item.Instrument == original.Instrument
                && item.Resolution == original.Resolution
                && item.Reason == MarketDataRecoveryReason.DeploymentContinuity);
        return new MarketDataDeploymentContinuityRange(pending.Instrument.Value, pending.FromUtc, pending.ToUtc, false, false, pending.ReturnedPoints, "Deployment continuity repair timed out.");
    }

    private async Task<MarketDataDeploymentContinuityReport> CompleteAsync(
        MarketDataDeploymentContinuityReport report,
        CancellationToken cancellationToken)
    {
        var path = await _files.WriteReportAsync(report, cancellationToken);
        report = report with { LocalReportPath = path };
        await _files.WriteReportAsync(report, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_marketData.CloudSnapshot.BucketName))
        {
            try
            {
                await _objectStore.UploadAsync(
                    _marketData.CloudSnapshot.BucketName,
                    $"market-data/deployment-continuity/{report.DeploymentId}.json",
                    path,
                    new Dictionary<string, string> { ["status"] = report.Status.ToString(), ["deployment-id"] = report.DeploymentId },
                    "application/json",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not upload deployment continuity report for {DeploymentId}.", report.DeploymentId);
            }
        }

        if (report.Status == MarketDataDeploymentContinuityStatus.Succeeded)
        {
            await _files.ArchiveCheckpointAsync(report.DeploymentId, cancellationToken);
        }

        return report;
    }

    private static MarketDataDeploymentContinuityReport CreateReport(
        MarketDataDeploymentCheckpoint checkpoint,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? cutoffUtc,
        IReadOnlyList<MarketDataDeploymentContinuityRange> ranges,
        IReadOnlyList<string> failures,
        MarketDataDeploymentContinuityStatus status)
        => new(1, checkpoint.DeploymentId, checkpoint.CapturedAtUtc, startedAtUtc, cutoffUtc, status, ranges, failures);
}

public sealed class MarketDataDeploymentContinuityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly MarketDataDeploymentContinuityOptions _options;

    public MarketDataDeploymentContinuityStore(IOptions<MarketDataOptions> options)
        => _options = options.Value.DeploymentContinuity;

    public async Task WriteCheckpointAsync(MarketDataDeploymentCheckpoint checkpoint, CancellationToken cancellationToken = default)
        => await WriteAtomicallyAsync(_options.CheckpointPath, checkpoint, cancellationToken);

    public async Task<MarketDataDeploymentCheckpoint?> GetCheckpointAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.CheckpointPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_options.CheckpointPath);
        return await JsonSerializer.DeserializeAsync<MarketDataDeploymentCheckpoint>(stream, JsonOptions, cancellationToken);
    }

    public async Task<string> WriteReportAsync(MarketDataDeploymentContinuityReport report, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_options.ReportDirectory, $"{report.DeploymentId}.json");
        await WriteAtomicallyAsync(path, report, cancellationToken);
        return Path.GetFullPath(path);
    }

    public Task ArchiveCheckpointAsync(string deploymentId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.CheckpointPath))
        {
            return Task.CompletedTask;
        }

        var archivePath = Path.Combine(_options.ArchiveDirectory, $"{deploymentId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.Move(_options.CheckpointPath, archivePath, overwrite: true);
        return Task.CompletedTask;
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed class MarketDataDeploymentContinuityOptions
{
    public string CheckpointPath { get; init; } = Path.Combine("Logs", "MarketData", "deployment-continuity", "active.json");
    public string ReportDirectory { get; init; } = Path.Combine("Logs", "MarketData", "deployment-continuity", "reports");
    public string ArchiveDirectory { get; init; } = Path.Combine("Logs", "MarketData", "deployment-continuity", "archive");
    public TimeSpan MaximumGapWindow { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan ReadinessTimeout { get; init; } = TimeSpan.FromMinutes(7);
    public TimeSpan RepairTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CheckpointPath) || string.IsNullOrWhiteSpace(ReportDirectory) || string.IsNullOrWhiteSpace(ArchiveDirectory))
        {
            throw new InvalidOperationException("Deployment continuity paths are required.");
        }

        if (MaximumGapWindow <= TimeSpan.Zero || ReadinessTimeout <= TimeSpan.Zero || RepairTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Deployment continuity timeouts must be greater than zero.");
        }
    }
}

public enum MarketDataDeploymentContinuityStatus
{
    Succeeded = 1,
    Failed = 2,
}

public sealed record MarketDataDeploymentCheckpoint(
    int SchemaVersion,
    string DeploymentId,
    DateTimeOffset CapturedAtUtc,
    PriceResolution Resolution,
    IReadOnlyList<MarketDataDeploymentCheckpointMarket> Markets,
    MarketDataDeploymentSnapshot Snapshot);

public sealed record MarketDataDeploymentCheckpointMarket(string Instrument, DateTimeOffset LatestFinalBarUtc);

public sealed record MarketDataDeploymentSnapshot(
    string BucketName,
    string ObjectName,
    string? Generation,
    string? Sha256,
    DateTimeOffset? UpdatedUtc,
    DateTimeOffset? LatestBarUtc);

public sealed record MarketDataDeploymentContinuityRange(
    string Instrument,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool Succeeded,
    bool ConfirmedClosedMarket,
    int ReturnedPoints,
    string? Message);

public sealed record MarketDataDeploymentContinuityReport(
    int SchemaVersion,
    string DeploymentId,
    DateTimeOffset CheckpointCapturedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CutoffUtc,
    MarketDataDeploymentContinuityStatus Status,
    IReadOnlyList<MarketDataDeploymentContinuityRange> Ranges,
    IReadOnlyList<string> Failures)
{
    public string? LocalReportPath { get; init; }
}
