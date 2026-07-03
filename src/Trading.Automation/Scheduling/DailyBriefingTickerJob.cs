using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Base;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class DailyBriefingTickerJob
{
    private readonly DailyPlanEnsureService _planEnsureService;
    private readonly ILogger<DailyBriefingTickerJob> _logger;

    public DailyBriefingTickerJob(
        DailyPlanEnsureService planEnsureService,
        ILogger<DailyBriefingTickerJob> logger)
    {
        _planEnsureService = planEnsureService;
        _logger = logger;
    }

    [TickerFunction(DailyBriefingConstants.JobName)]
    public async Task RunAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running scheduled daily briefing job {JobId}.", context.Id);
        await _planEnsureService.EnsureForTodayAsync(cancellationToken);
    }
}
