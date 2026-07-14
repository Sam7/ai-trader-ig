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
    private Task? _priorArtifactUploadTask;

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
        _priorArtifactUploadTask = RunSafelyAsync("prior artifact upload", UploadPriorArtifactsAsync, stoppingToken);

        var nextForensicSampleUtc = DateTimeOffset.UtcNow.Add(_options.SampleInterval);
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

            if (containmentRequested)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= nextForensicSampleUtc)
            {
                await RunSafelyAsync("forensic sample", _coordinator.CaptureForensicSnapshotAsync, stoppingToken).ConfigureAwait(false);
                nextForensicSampleUtc = DateTimeOffset.UtcNow.Add(_options.SampleInterval);
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

    private async Task UploadPriorArtifactsAsync(CancellationToken cancellationToken)
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
