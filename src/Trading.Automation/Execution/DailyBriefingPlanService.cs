using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;
using Trading.Strategy.Workflow;

namespace Trading.Automation.Execution;

public sealed class DailyBriefingPlanService
{
    private readonly ITradingDayWorkflow _workflow;
    private readonly AutomationOptions _options;
    private readonly ILogger<DailyBriefingPlanService> _logger;

    public DailyBriefingPlanService(
        ITradingDayWorkflow workflow,
        IOptions<AutomationOptions> options,
        ILogger<DailyBriefingPlanService> logger)
    {
        _workflow = workflow;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TradingDayPlan> RunForTodayAsync(CancellationToken cancellationToken = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_options.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        return await RunAsync(DateOnly.FromDateTime(localNow.DateTime), cancellationToken);
    }

    public async Task<TradingDayPlan> RunAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Planning trading day for {TradingDate}.", tradingDate);
        return await _workflow.PlanTradingDayAsync(new TradingDayRequest(tradingDate), cancellationToken);
    }
}
