namespace Trading.AI.Prompts.DailyBriefResearch;

public sealed record DailyBriefResearchInput(
    DateOnly ReportDate,
    string ReportTimezone,
    int WatchlistSize,
    string TrackedMarkets,
    DateOnly PromptDate,
    DateTimeOffset RequestedAtUtc);
