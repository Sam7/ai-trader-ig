namespace Trading.AI.Prompts.DailyPlanJson;

public sealed record DailyPlanJsonInput(
    DateOnly TradingDate,
    string ReportTimezone,
    int WatchlistSize,
    decimal MinRewardRiskRatio,
    string TrackedMarkets,
    string ResearchBrief,
    DateTimeOffset RequestedAtUtc);
