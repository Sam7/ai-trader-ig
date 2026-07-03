using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

public sealed class MarketDataSnapshotPublisherHostedService : BackgroundService
{
    private readonly MarketDataSnapshotPublisher _publisher;
    private readonly MarketDataOptions _options;
    private readonly ILogger<MarketDataSnapshotPublisherHostedService> _logger;

    public MarketDataSnapshotPublisherHostedService(
        MarketDataSnapshotPublisher publisher,
        IOptions<MarketDataOptions> options,
        ILogger<MarketDataSnapshotPublisherHostedService> logger)
    {
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var publisherOptions = _options.CloudSnapshot.Publisher;
        if (!publisherOptions.Enabled)
        {
            return;
        }

        await RunPublishAsync(stoppingToken);
        using var timer = new PeriodicTimer(NormalizeInterval(publisherOptions.Interval));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPublishAsync(stoppingToken);
        }
    }

    private async Task RunPublishAsync(CancellationToken cancellationToken)
    {
        var result = await _publisher.PublishOnceAsync(cancellationToken);
        _logger.LogInformation(
            "Market-data snapshot publisher completed with status {Status}: {Message}",
            result.Status,
            result.Message);
    }

    private static TimeSpan NormalizeInterval(TimeSpan interval)
        => interval > TimeSpan.Zero ? interval : TimeSpan.FromMinutes(5);
}
