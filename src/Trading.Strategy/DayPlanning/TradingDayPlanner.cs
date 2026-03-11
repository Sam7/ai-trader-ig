using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

namespace Trading.Strategy.DayPlanning;

/// <summary>
/// Builds the day's plan before any intraday decision-making happens.
/// This is the once-per-day step that gathers broad context, composes the watch list, and resets persisted strategy state for that trading date.
/// </summary>
public sealed class TradingDayPlanner
{
    private readonly StrategyRules _rules;
    private readonly IDailyBriefingComposer _dailyBriefingComposer;
    private readonly IMarketSnapshotSource _marketSnapshotSource;
    private readonly INewsHeadlineSource _newsHeadlineSource;
    private readonly IEconomicCalendarSource _economicCalendarSource;
    private readonly ITradingClock _tradingClock;
    private readonly ITradingDayStore _tradingDayStore;

    public TradingDayPlanner(
        StrategyRules rules,
        IDailyBriefingComposer dailyBriefingComposer,
        IMarketSnapshotSource marketSnapshotSource,
        INewsHeadlineSource newsHeadlineSource,
        IEconomicCalendarSource economicCalendarSource,
        ITradingClock tradingClock,
        ITradingDayStore tradingDayStore)
    {
        _rules = rules;
        _dailyBriefingComposer = dailyBriefingComposer;
        _marketSnapshotSource = marketSnapshotSource;
        _newsHeadlineSource = newsHeadlineSource;
        _economicCalendarSource = economicCalendarSource;
        _tradingClock = tradingClock;
        _tradingDayStore = tradingDayStore;
    }

    public async Task<TradingDayPlan> PlanAsync(TradingDayRequest request, CancellationToken cancellationToken = default)
    {
        _rules.Validate();

        // Gather the broad market context first so the briefing composer can think in one pass.
        var nowUtc = _tradingClock.UtcNow;
        var universe = await _marketSnapshotSource.GetUniverseSnapshotAsync(request.TradingDate, cancellationToken);
        var headlines = await _newsHeadlineSource.GetHeadlinesAsync(new HeadlineQuery([], nowUtc.AddHours(-24), nowUtc), cancellationToken);
        var calendarEvents = await _economicCalendarSource.GetEventsAsync(new CalendarWindow(nowUtc, nowUtc.AddHours(24)), cancellationToken);

        var plan = await _dailyBriefingComposer.ComposeAsync(
            new DailyBriefingRequest(request, _rules, universe, headlines, calendarEvents, nowUtc),
            cancellationToken);

        plan.Validate(_rules.MarketWatch.ShortlistSize);

        // Planning the day starts a fresh record for that trading date; intraday steps build on this snapshot.
        await _tradingDayStore.SaveAsync(TradingDayRecord.StartNew(plan), cancellationToken);
        return plan;
    }
}
