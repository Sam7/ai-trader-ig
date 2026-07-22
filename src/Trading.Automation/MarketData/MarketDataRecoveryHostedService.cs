using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

public sealed class MarketDataRecoveryHostedService : BackgroundService
{
    private readonly MarketDataRecoveryCoordinator _recovery;
    private readonly MarketDataRecoveryPlanner _planner;
    private readonly DailyBriefingOptions _briefing;
    private readonly MarketDataCollectorOptions _collector;
    private readonly MarketDataOptions _marketData;
    private readonly MarketDataRecoveryOptions _options;
    private readonly ILogger<MarketDataRecoveryHostedService> _logger;

    public MarketDataRecoveryHostedService(
        MarketDataRecoveryCoordinator recovery,
        MarketDataRecoveryPlanner planner,
        IOptions<DailyBriefingOptions> briefing,
        IOptions<MarketDataCollectorOptions> collector,
        IOptions<MarketDataOptions> marketData,
        IOptions<MarketDataRecoveryOptions> options,
        ILogger<MarketDataRecoveryHostedService> logger)
        => (_recovery, _planner, _briefing, _collector, _marketData, _options, _logger) =
            (recovery, planner, briefing.Value, collector.Value, marketData.Value, options.Value, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        if (_options.Mode == MarketDataRecoveryMode.Disabled)
        {
            _logger.LogInformation("Automatic market-data recovery is disabled.");
            return;
        }

        if (_marketData.CloudSnapshot.Mirror.Enabled)
        {
            _logger.LogInformation("Skipping IG recovery in read-only cloud mirror mode.");
            return;
        }

        var targets = _briefing.TrackedMarkets
            .Where(market => !string.IsNullOrWhiteSpace(market.InstrumentId))
            .Select(market => new MarketDataRecoveryTarget(
                new Trading.Abstractions.InstrumentId(market.InstrumentId),
                market.SelectionPriority))
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }
        var nextTailAuditUtc = DateTimeOffset.MinValue;
        var nextHistoricalAuditUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        using var timer = new PeriodicTimer(NormalizeInterval(_options.PollInterval, TimeSpan.FromMinutes(1)));
        do
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextTailAuditUtc)
            {
                await _planner.PlanRecentAsync(targets, _collector.Resolution, stoppingToken);
                nextTailAuditUtc = now.Add(NormalizeInterval(_options.TailAuditInterval, TimeSpan.FromMinutes(5)));
            }

            if (_options.Mode == MarketDataRecoveryMode.RecentAndHistorical && now >= nextHistoricalAuditUtc)
            {
                await _planner.PlanHistoricalAsync(targets, _collector.Resolution, stoppingToken);
                nextHistoricalAuditUtc = now.Add(NormalizeInterval(_options.HistoricalAuditInterval, TimeSpan.FromDays(1)));
            }

            await RecoverOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecoverOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _recovery.ProcessNextAsync(_options.Mode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static TimeSpan NormalizeInterval(TimeSpan configured, TimeSpan fallback)
        => configured > TimeSpan.Zero ? configured : fallback;
}
