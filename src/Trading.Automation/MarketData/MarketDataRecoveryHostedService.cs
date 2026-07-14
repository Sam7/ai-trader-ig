using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

public sealed class MarketDataRecoveryHostedService : BackgroundService
{
    private readonly MarketDataRecoveryCoordinator _recovery;
    private readonly DailyBriefingOptions _briefing;
    private readonly MarketDataCollectorOptions _collector;
    private readonly MarketDataOptions _marketData;
    private readonly MarketDataRecoveryOptions _options;
    private readonly MarketDataRuntimeActivityMetrics _activityMetrics;
    private readonly ILogger<MarketDataRecoveryHostedService> _logger;

    public MarketDataRecoveryHostedService(MarketDataRecoveryCoordinator recovery, IOptions<DailyBriefingOptions> briefing, IOptions<MarketDataCollectorOptions> collector, IOptions<MarketDataOptions> marketData, IOptions<MarketDataRecoveryOptions> options, MarketDataRuntimeActivityMetrics activityMetrics, ILogger<MarketDataRecoveryHostedService> logger)
        => (_recovery, _briefing, _collector, _marketData, _options, _activityMetrics, _logger) = (recovery, briefing.Value, collector.Value, marketData.Value, options.Value, activityMetrics, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_marketData.CloudSnapshot.Mirror.Enabled) { _logger.LogInformation("Skipping IG recovery in read-only cloud mirror mode."); return; }
        var targets = _briefing.TrackedMarkets.Where(x => !string.IsNullOrWhiteSpace(x.InstrumentId)).Select(x => new MarketDataRecoveryTarget(new Trading.Abstractions.InstrumentId(x.InstrumentId), x.SelectionPriority)).ToArray();
        if (targets.Length == 0) return;
        using var timer = new PeriodicTimer(_options.TickInterval);
        do { await RecoverOnceAsync(targets, stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecoverOnceAsync(IReadOnlyList<MarketDataRecoveryTarget> targets, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _activityMetrics.RecordRecoveryStarted();
        try
        {
            await _recovery.RecoverOnceAsync(targets, _collector.Resolution, cancellationToken);
            _activityMetrics.RecordRecoveryCompleted(stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _activityMetrics.RecordRecoveryFailed(stopwatch.Elapsed);
            throw;
        }
    }
}
