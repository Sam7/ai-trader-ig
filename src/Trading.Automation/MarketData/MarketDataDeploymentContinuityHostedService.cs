using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

/// <summary>Reconciles only the bounded gap recorded by the deployment checkpoint.</summary>
public sealed class MarketDataDeploymentContinuityHostedService : BackgroundService
{
    private readonly MarketDataDeploymentContinuityService _continuity;
    private readonly ILogger<MarketDataDeploymentContinuityHostedService> _logger;

    public MarketDataDeploymentContinuityHostedService(
        MarketDataDeploymentContinuityService continuity,
        ILogger<MarketDataDeploymentContinuityHostedService> logger)
        => (_continuity, _logger) = (continuity, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkpoint = await _continuity.GetActiveCheckpointAsync(stoppingToken);
        if (checkpoint is null)
        {
            return;
        }

        try
        {
            if (!await _continuity.WaitForPostRestartStreamAsync(checkpoint, stoppingToken))
            {
                await _continuity.FailAsync(
                    checkpoint,
                    "The restarted worker did not receive stream updates for every checkpointed market before the readiness deadline.",
                    stoppingToken);
                return;
            }

            var report = await _continuity.ReconcileAsync(checkpoint, stoppingToken);
            _logger.LogInformation(
                "Deployment continuity for {DeploymentId} completed with status {Status}. Ranges: {RangeCount}. Failures: {FailureCount}.",
                checkpoint.DeploymentId,
                report.Status,
                report.Ranges.Count,
                report.Failures.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Deployment continuity failed unexpectedly for {DeploymentId}.", checkpoint.DeploymentId);
            await _continuity.FailAsync(checkpoint, exception.Message, CancellationToken.None);
        }
    }
}
