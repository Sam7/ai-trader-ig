using Trading.Strategy.Inputs;

namespace Trading.AI.DailyBriefing;

public sealed record DailyPlanDocument(
    string MacroSummary,
    string MarketRegimeSummary,
    MarketRegime MarketRegime,
    PlannedMarketDocument[] RankedMarkets,
    PlannedCatalystDocument[] Catalysts,
    PlannedOpportunityDocument[] Opportunities,
    PlannedRiskDocument[] Risks,
    PlannedCalendarEventDocument[] CalendarEvents);

public sealed record PlannedMarketDocument(
    string InstrumentId,
    string InstrumentName,
    int Rank,
    string Rationale,
    PlannedTradeScenarioDocument LongScenario,
    PlannedTradeScenarioDocument ShortScenario);

public sealed record PlannedCatalystDocument(
    string Id,
    string Event,
    string Status,
    string[] AffectedMarkets,
    string DirectionalPressure,
    string LikelyTimeHorizon,
    string MarketPricingView,
    string ConfirmationSignals,
    string WeakeningSignals,
    bool FollowUpCandidate);

public sealed record PlannedOpportunityDocument(
    string Id,
    string MarketArea,
    string CurrentBias,
    string WhyItIsInterestingNow,
    string WhatNeedsDeeperResearch,
    string WhatStrengthensConviction,
    string WhatReducesConviction,
    string PrimaryDependency,
    string TimeHorizon,
    string Priority);

public sealed record PlannedRiskDocument(
    string Id,
    string Risk,
    string WhyItMattersNow,
    string WhatItCouldBreak,
    string EarlyWarningSign,
    string ResolutionSignal);

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
