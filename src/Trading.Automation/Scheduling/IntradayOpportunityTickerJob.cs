using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Base;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class IntradayOpportunityTickerJob
{
    private readonly IntradayOpportunityScanService _scanService;
    private readonly ILogger<IntradayOpportunityTickerJob> _logger;

    public IntradayOpportunityTickerJob(
        IntradayOpportunityScanService scanService,
        ILogger<IntradayOpportunityTickerJob> logger)
    {
        _scanService = scanService;
        _logger = logger;
    }

    [TickerFunction(IntradayOpportunityConstants.JobName)]
    public async Task RunAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running scheduled intraday opportunity scan job {JobId}.", context.Id);
        await _scanService.RunForTodayAsync(cancellationToken);
    }
}
