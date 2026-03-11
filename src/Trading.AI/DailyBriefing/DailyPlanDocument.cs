namespace Trading.AI.DailyBriefing;

public sealed record DailyPlanDocument(
    string MacroSummary,
    string MarketRegimeSummary,
    string MarketRegime,
    IReadOnlyList<PlannedMarketDocument> RankedMarkets,
    IReadOnlyList<PlannedMarketDocument> WatchList,
    IReadOnlyList<PlannedCalendarEventDocument> CalendarEvents);

public sealed record PlannedMarketDocument(
    string InstrumentId,
    int Rank,
    string Rationale,
    decimal EntryZoneLowerBound,
    decimal EntryZoneUpperBound,
    PlannedTradeScenarioDocument LongScenario,
    PlannedTradeScenarioDocument ShortScenario);

public sealed record PlannedTradeScenarioDocument(
    string Thesis,
    string Confirmation,
    string Invalidation,
    IReadOnlyList<string> ExpectedCatalysts,
    DateTimeOffset? AvoidTradingUntilUtc);

public sealed record PlannedCalendarEventDocument(
    string Id,
    string Title,
    DateTimeOffset ScheduledAtUtc,
    string Impact,
    IReadOnlyList<string> AffectedInstrumentIds);
