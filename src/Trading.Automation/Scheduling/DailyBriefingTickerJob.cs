using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class DailyBriefingTickerJob
{
    private readonly DailyPlanEnsureService _planEnsureService;
    private readonly IOptions<AutomationOptions> _options;
    private readonly ILogger<DailyBriefingTickerJob> _logger;

    public DailyBriefingTickerJob(
        DailyPlanEnsureService planEnsureService,
        IOptions<AutomationOptions> options,
        ILogger<DailyBriefingTickerJob> logger)
    {
        _planEnsureService = planEnsureService;
        _options = options;
        _logger = logger;
    }

    [TickerFunction(DailyBriefingConstants.JobName)]
    public async Task RunAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Skipping scheduled daily briefing job {JobId}; automation is disabled.", context.Id);
            return;
        }

        _logger.LogInformation("Running scheduled daily briefing job {JobId}.", context.Id);
        await _planEnsureService.EnsureForTodayAsync(cancellationToken);
    }
}
