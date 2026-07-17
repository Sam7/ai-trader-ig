using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;

namespace Trading.Automation.Diagnostics;

/// <summary>
/// Runs a one-second in-memory sentry and a five-second bounded forensic trace.
/// Closed artifacts are uploaded only by a later worker process.
/// </summary>
internal sealed class WorkerDiagnosticsHostedService : BackgroundService
{
    private readonly WorkerDiagnosticsOptions _options;
    private readonly WorkerDiagnosticsCoordinator _coordinator;
    private readonly RollingWorkerTraceStore _traces;
    private readonly IWorkerDiagnosticsArtifactUploader _uploader;
    private readonly ILogger<WorkerDiagnosticsHostedService> _logger;
    // Retained for the host lifetime but deliberately not awaited by the sentry path.
    private Task? _artifactUploadTask;
    private CancellationTokenSource? _artifactUploadCancellation;

    public WorkerDiagnosticsHostedService(
        IOptions<WorkerDiagnosticsOptions> options,
        WorkerDiagnosticsCoordinator coordinator,
        RollingWorkerTraceStore traces,
        IWorkerDiagnosticsArtifactUploader uploader,
        ILogger<WorkerDiagnosticsHostedService> logger)
    {
        _options = options.Value;
        _coordinator = coordinator;
        _traces = traces;
        _uploader = uploader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _options.Validate();
        await RunSafelyAsync("initialization", InitializeTraceStoreAsync, stoppingToken).ConfigureAwait(false);
        await RunSafelyAsync("initial forensic sample", _coordinator.CaptureForensicSnapshotAsync, stoppingToken).ConfigureAwait(false);
        RequestArtifactUploadIfHealthy(stoppingToken);

        var nextForensicSampleUtc = DateTimeOffset.UtcNow.Add(_options.SampleInterval);
        var nextArtifactUploadUtc = DateTimeOffset.UtcNow.Add(_options.ArtifactUploadInterval);
        using var sentryTimer = new PeriodicTimer(_options.SentryInterval);
        while (await sentryTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var containmentRequested = false;
            try
            {
                containmentRequested = await _coordinator.ObserveSentryAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Worker diagnostics sentry failed.");
            }

            if (_coordinator.IsPressureMode)
            {
                _artifactUploadCancellation?.Cancel();
            }

            if (containmentRequested)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var samplingInterval = _coordinator.IsPressureMode
                ? _options.SentryInterval
                : _options.SampleInterval;
            var needsPressureSample = _coordinator.IsPressureMode
                && (_coordinator.LastForensicSnapshotUtc is null
                    || now - _coordinator.LastForensicSnapshotUtc >= _options.SentryInterval);
            if (now >= nextForensicSampleUtc || needsPressureSample)
            {
                await RunSafelyAsync("forensic sample", _coordinator.CaptureForensicSnapshotAsync, stoppingToken).ConfigureAwait(false);
                nextForensicSampleUtc = DateTimeOffset.UtcNow.Add(samplingInterval);
            }

            if (now >= nextArtifactUploadUtc)
            {
                RequestArtifactUploadIfHealthy(stoppingToken);
                nextArtifactUploadUtc = now.Add(_options.ArtifactUploadInterval);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _traces.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to close the worker diagnostics trace during shutdown.");
        }
    }

    private Task InitializeTraceStoreAsync(CancellationToken cancellationToken)
    {
        _traces.Initialize();
        return Task.CompletedTask;
    }

    private void RequestArtifactUploadIfHealthy(CancellationToken stoppingToken)
    {
        if (_coordinator.IsPressureMode
            || (_artifactUploadTask is { IsCompleted: false }))
        {
            return;
        }

        _artifactUploadCancellation?.Dispose();
        _artifactUploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _artifactUploadTask = UploadClosedArtifactsSafelyAsync(stoppingToken, _artifactUploadCancellation.Token);
    }

    private async Task UploadClosedArtifactsSafelyAsync(CancellationToken stoppingToken, CancellationToken uploadCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(uploadCancellationToken);
        timeout.CancelAfter(_options.ArtifactUploadTimeout);
        try
        {
            await UploadClosedArtifactsAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown does not make diagnostics upload a service failure.
        }
        catch (OperationCanceledException) when (_coordinator.IsPressureMode)
        {
            _logger.LogInformation("Worker diagnostics artifact upload paused for memory pressure; closed artifacts remain local for retry.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Worker diagnostics artifact upload timed out after {Timeout}.", _options.ArtifactUploadTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Worker diagnostics artifact upload failed; closed artifacts remain local for retry.");
        }
    }

    private async Task UploadClosedArtifactsAsync(CancellationToken cancellationToken)
    {
        var uploaded = await _uploader.UploadAsync(_traces.GetUploadCandidates(), cancellationToken).ConfigureAwait(false);
        foreach (var path in uploaded)
        {
            if (!_traces.TryDeleteUploadedArtifact(path))
            {
                _logger.LogWarning(
                    "Uploaded worker diagnostics artifact {ArtifactName} could not be removed locally; it may be retried.",
                    Path.GetFileName(path));
            }
        }
    }

    private async Task RunSafelyAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Worker diagnostics {OperationName} failed.", operationName);
        }
    }
}
