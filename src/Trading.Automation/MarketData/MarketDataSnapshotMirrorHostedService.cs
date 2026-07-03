using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

public sealed class MarketDataSnapshotMirrorHostedService : BackgroundService
{
    private readonly MarketDataSnapshotSynchronizer _synchronizer;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataSnapshotMirrorHostedService> _logger;

    public MarketDataSnapshotMirrorHostedService(
        MarketDataSnapshotSynchronizer synchronizer,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataSnapshotMirrorHostedService> logger)
    {
        _synchronizer = synchronizer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mirrorOptions = _options.CloudSnapshot.Mirror;
        if (!mirrorOptions.Enabled)
        {
            return;
        }

        await RunSynchronizationAsync(stoppingToken);
        using var timer = new PeriodicTimer(NormalizeInterval(mirrorOptions.Interval));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunSynchronizationAsync(stoppingToken);
        }
    }

    private async Task RunSynchronizationAsync(CancellationToken cancellationToken)
    {
        var result = await _synchronizer.SynchronizeOnceAsync(cancellationToken);
        _logger.LogInformation(
            "Market-data snapshot mirror completed with status {Status}: {Message}",
            result.Status,
            result.Message);
    }

    private static TimeSpan NormalizeInterval(TimeSpan interval)
        => interval > TimeSpan.Zero ? interval : TimeSpan.FromMinutes(5);
}
