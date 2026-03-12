using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class IntradayOpportunityScheduleInitializer : IHostedService
{
    private readonly ICronTickerManager<CronTickerEntity> _cronTickerManager;
    private readonly AutomationOptions _options;
    private readonly ILogger<IntradayOpportunityScheduleInitializer> _logger;

    public IntradayOpportunityScheduleInitializer(
        ICronTickerManager<CronTickerEntity> cronTickerManager,
        IOptions<AutomationOptions> options,
        ILogger<IntradayOpportunityScheduleInitializer> logger)
    {
        _cronTickerManager = cronTickerManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.IntradayOpportunities.Enabled)
        {
            _logger.LogInformation("Intraday opportunity scheduling is disabled.");
            return;
        }

        await _cronTickerManager.AddAsync(new CronTickerEntity
        {
            Function = IntradayOpportunityConstants.JobName,
            Expression = _options.IntradayOpportunities.Cron,
        });

        _logger.LogInformation(
            "Registered intraday opportunity schedule {Cron} in timezone {Timezone}.",
            _options.IntradayOpportunities.Cron,
            _options.Timezone);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
