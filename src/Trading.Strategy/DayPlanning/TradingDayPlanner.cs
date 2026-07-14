using Trading.Strategy.Persistence;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

namespace Trading.Strategy.DayPlanning;

/// <summary>
/// Builds the day's plan before any intraday decision-making happens.
/// This is the once-per-day step that gathers broad context, composes the watch list, and resets persisted strategy state for that trading date.
/// </summary>
public sealed class TradingDayPlanner : ITradingDayPlanner
{
    private readonly DailyPlanningPolicy _policy;
    private readonly IDailyBriefingComposer _dailyBriefingComposer;
    private readonly ITradingClock _tradingClock;
    private readonly ITradingDayStore _tradingDayStore;

    public TradingDayPlanner(
        DailyPlanningPolicy policy,
        IDailyBriefingComposer dailyBriefingComposer,
        ITradingClock tradingClock,
        ITradingDayStore tradingDayStore)
    {
        _policy = policy;
        _dailyBriefingComposer = dailyBriefingComposer;
        _tradingClock = tradingClock;
        _tradingDayStore = tradingDayStore;
    }

    public async Task<TradingDayPlan> PlanAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
    {
        _policy.Validate();

        var nowUtc = _tradingClock.UtcNow;
        var plan = await _dailyBriefingComposer.ComposeAsync(
            new DailyBriefingRequest(request, _policy, nowUtc),
            cancellationToken);

        plan.Validate(_policy.ShortlistSize);

        // Planning the day starts a fresh record for that trading date; intraday steps build on this snapshot.
        await _tradingDayStore.SaveAsync(TradingDayRecord.StartNew(plan), cancellationToken);
        return plan;
    }
}
