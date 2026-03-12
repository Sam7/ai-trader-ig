namespace Trading.AI.Prompts.IntradayOpportunityReview;

public sealed record IntradayOpportunityReviewInput(
    DateOnly TradingDate,
    DateTimeOffset LookbackStartUtc,
    DateTimeOffset LookbackEndUtc,
    int WatchedMarketCount,
    int MaxCandidatesPerRun,
    string TradingTimezone,
    string DailyPlanSummary,
    string WatchedMarketsContext,
    string CalendarEventsContext,
    DateOnly PromptDate,
    DateTimeOffset RequestedAtUtc);
