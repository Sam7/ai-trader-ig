using Trading.AI.DailyBriefing;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Rules;
using Trading.Automation.Configuration;
using Microsoft.Extensions.Options;

namespace Trading.Automation.Execution;

public sealed class DailyBriefingResearchService
{
    private readonly DailyBriefResearcher _researcher;
    private readonly StrategyRules _strategyRules;
    private readonly SystemTradingClock _clock;
    private readonly AutomationOptions _options;

    public DailyBriefingResearchService(
        DailyBriefResearcher researcher,
        StrategyRules strategyRules,
        SystemTradingClock clock,
        IOptions<AutomationOptions> options)
    {
        _researcher = researcher;
        _strategyRules = strategyRules;
        _clock = clock;
        _options = options.Value;
    }

    public Task<DailyBriefResearchResult> RunForTodayAsync(CancellationToken cancellationToken = default)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_options.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        return RunAsync(DateOnly.FromDateTime(localNow.DateTime), cancellationToken);
    }

    public Task<DailyBriefResearchResult> RunAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
        => _researcher.ResearchAsync(new DailyBriefingRequest(new TradingDayRequest(tradingDate), _strategyRules, _clock.UtcNow), cancellationToken);
}
