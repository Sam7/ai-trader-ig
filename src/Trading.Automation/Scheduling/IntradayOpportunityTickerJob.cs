using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Base;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;

namespace Trading.Automation.Scheduling;

public sealed class IntradayOpportunityTickerJob
{
    private readonly IntradayOpportunityScanService _scanService;
    private readonly IOptions<AutomationOptions> _options;
    private readonly ILogger<IntradayOpportunityTickerJob> _logger;

    public IntradayOpportunityTickerJob(
        IntradayOpportunityScanService scanService,
        IOptions<AutomationOptions> options,
        ILogger<IntradayOpportunityTickerJob> logger)
    {
        _scanService = scanService;
        _options = options;
        _logger = logger;
    }

    [TickerFunction(IntradayOpportunityConstants.JobName)]
    public async Task RunAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled || !_options.Value.IntradayOpportunities.Enabled)
        {
            _logger.LogInformation("Skipping scheduled intraday opportunity job {JobId}; automation is disabled.", context.Id);
            return;
        }

        _logger.LogInformation("Running scheduled intraday opportunity scan job {JobId}.", context.Id);
        await _scanService.RunForTodayAsync(cancellationToken);
    }
}
