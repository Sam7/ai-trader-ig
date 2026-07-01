using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.Automation.Configuration;
using Trading.MarketData;

namespace Trading.Automation.MarketData;

public sealed class MarketDataCollectionHostedService : BackgroundService
{
    private readonly IMarketDataCollector _collector;
    private readonly DailyBriefingOptions _dailyBriefingOptions;
    private readonly MarketDataCollectionOptions _options;
    private readonly ILogger<MarketDataCollectionHostedService> _logger;

    public MarketDataCollectionHostedService(
        IMarketDataCollector collector,
        IOptions<DailyBriefingOptions> dailyBriefingOptions,
        IOptions<MarketDataCollectionOptions> options,
        ILogger<MarketDataCollectionHostedService> logger)
    {
        _collector = collector;
        _dailyBriefingOptions = dailyBriefingOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var instruments = ResolveTrackedInstruments();
        var retryDelay = NormalizeDelay(_options.InitialRetryDelay, TimeSpan.FromSeconds(10));
        var maxRetryDelay = NormalizeDelay(_options.MaxRetryDelay, TimeSpan.FromMinutes(5));

        _logger.LogInformation(
            "Starting market-data collection for {InstrumentCount} tracked instrument(s).",
            instruments.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _collector.RunAsync(instruments, duration: null, stoppingToken);
                _logger.LogInformation("Market-data collector stopped.");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Market-data collection stopped during worker shutdown.");
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Market-data collector failed. Retrying in {RetryDelay}.",
                    retryDelay);
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    private IReadOnlyList<InstrumentId> ResolveTrackedInstruments()
    {
        var instruments = _dailyBriefingOptions.TrackedMarkets
            .Select(market => market.InstrumentId)
            .Where(instrument => !string.IsNullOrWhiteSpace(instrument))
            .Select(instrument => new InstrumentId(instrument))
            .Distinct()
            .ToArray();

        if (instruments.Length == 0)
        {
            throw new InvalidOperationException("No tracked markets are configured for market-data collection.");
        }

        return instruments;
    }

    private static TimeSpan NormalizeDelay(TimeSpan configured, TimeSpan fallback)
        => configured > TimeSpan.Zero ? configured : fallback;
}
