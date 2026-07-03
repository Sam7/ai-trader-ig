using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class DailyPlanEnsureService
{
    private readonly ITradingDayStore _tradingDayStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AutomationOptions _options;
    private readonly ILogger<DailyPlanEnsureService> _logger;
    private readonly ConcurrentDictionary<DateOnly, SemaphoreSlim> _locks = [];

    public DailyPlanEnsureService(
        ITradingDayStore tradingDayStore,
        IServiceScopeFactory scopeFactory,
        IOptions<AutomationOptions> options,
        ILogger<DailyPlanEnsureService> logger)
    {
        _tradingDayStore = tradingDayStore;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<TradingDayPlan> EnsureForTodayAsync(CancellationToken cancellationToken = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_options.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        return EnsureAsync(DateOnly.FromDateTime(localNow.DateTime), cancellationToken);
    }

    public async Task<TradingDayPlan> EnsureAsync(
        DateOnly tradingDate,
        CancellationToken cancellationToken = default)
    {
        var existing = await _tradingDayStore.GetAsync(tradingDate, cancellationToken);
        if (existing?.Plan is not null)
        {
            return existing.Plan;
        }

        var gate = _locks.GetOrAdd(tradingDate, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _tradingDayStore.GetAsync(tradingDate, cancellationToken);
            if (existing?.Plan is not null)
            {
                return existing.Plan;
            }

            _logger.LogInformation(
                "No trading day plan exists for {TradingDate}; creating one before intraday automation continues.",
                tradingDate);
            using var scope = _scopeFactory.CreateScope();
            var planService = scope.ServiceProvider.GetRequiredService<DailyBriefingPlanService>();
            return await planService.RunAsync(tradingDate, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
