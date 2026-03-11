using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class DailyBriefingScheduleInitializer : IHostedService
{
    private readonly ICronTickerManager<CronTickerEntity> _cronTickerManager;
    private readonly AutomationOptions _options;
    private readonly ILogger<DailyBriefingScheduleInitializer> _logger;

    public DailyBriefingScheduleInitializer(
        ICronTickerManager<CronTickerEntity> cronTickerManager,
        IOptions<AutomationOptions> options,
        ILogger<DailyBriefingScheduleInitializer> logger)
    {
        _cronTickerManager = cronTickerManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Automation scheduling is disabled.");
            return;
        }

        await _cronTickerManager.AddAsync(new CronTickerEntity
        {
            Function = _options.JobName,
            Expression = _options.DailyBriefCron,
        });

        _logger.LogInformation(
            "Registered daily briefing schedule {Cron} in timezone {Timezone}.",
            _options.DailyBriefCron,
            _options.Timezone);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
