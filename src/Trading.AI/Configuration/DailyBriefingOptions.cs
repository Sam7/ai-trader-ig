namespace Trading.AI.Configuration;

public sealed class DailyBriefingOptions
{
    public const string SectionName = "AI:DailyBriefing";

    public DailyBriefingModelOptions Research { get; init; } = new();

    public DailyBriefingModelOptions PlanJson { get; init; } = new();

    public string ObservabilityRootPath { get; init; } = Path.Combine("Logs", "Observability");

    public string DefaultTimezone { get; init; } = "Australia/Melbourne";

    public TrackedMarketOptions[] TrackedMarkets { get; init; } = [];
}
