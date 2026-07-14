using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class DailyBriefingPlanService
{
    private readonly ITradingDayPlanner _planner;
    private readonly AutomationOptions _options;
    private readonly ILogger<DailyBriefingPlanService> _logger;

    public DailyBriefingPlanService(
        ITradingDayPlanner planner,
        IOptions<AutomationOptions> options,
        ILogger<DailyBriefingPlanService> logger)
    {
        _planner = planner;
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
        var plan = await _planner.PlanAsync(new TradingDayRequest(tradingDate), cancellationToken);
        _logger.LogInformation(
            "Planned trading day for {TradingDate}. Watch list contains {WatchListCount} markets.",
            tradingDate,
            plan.WatchList.Count);
        return plan;
    }
}
