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

        var result = await _cronTickerManager.AddAsync(new CronTickerEntity
        {
            Function = _options.JobName,
            Expression = _options.DailyBriefCron,
        }, cancellationToken);

        if (!result.IsSucceeded)
        {
            _logger.LogError(
                result.Exception,
                "Failed to register daily briefing schedule {Cron} for function {Function}.",
                _options.DailyBriefCron,
                _options.JobName);
            throw new InvalidOperationException(
                $"Failed to register daily briefing schedule '{_options.DailyBriefCron}' for function '{_options.JobName}'.",
                result.Exception);
        }

        _logger.LogInformation(
            "Registered daily briefing schedule {Cron} in timezone {Timezone} with cron ticker {CronTickerId}.",
            _options.DailyBriefCron,
            _options.Timezone,
            result.Result?.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
