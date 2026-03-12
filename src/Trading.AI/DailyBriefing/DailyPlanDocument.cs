using Trading.Strategy.Inputs;

namespace Trading.AI.DailyBriefing;

public sealed record DailyPlanDocument(
    string MacroSummary,
    string MarketRegimeSummary,
    MarketRegime MarketRegime,
    PlannedMarketDocument[] RankedMarkets,
    PlannedMarketDocument[] WatchList,
    PlannedCalendarEventDocument[] CalendarEvents);

public sealed record PlannedMarketDocument(
    string InstrumentId,
    int Rank,
    string Rationale,
    PlannedTradeScenarioDocument LongScenario,
    PlannedTradeScenarioDocument ShortScenario);

public sealed record PlannedTradeScenarioDocument(
    string Thesis,
    string Confirmation,
    string Invalidation,
    string[] ExpectedCatalysts,
    DateTimeOffset? AvoidTradingUntilUtc);

public sealed record PlannedCalendarEventDocument(
    string Id,
    string Title,
    DateTimeOffset ScheduledAtUtc,
    string Impact,
    string[] AffectedInstrumentIds);
