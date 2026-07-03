namespace Trading.AI.Configuration;

public sealed class DailyBriefingOptions
{
    public const string SectionName = "AI:DailyBriefing";

    public PromptModelOptions Research { get; init; } = new();

    public PromptModelOptions PlanJson { get; init; } = new();

    public string DefaultTimezone { get; init; } = "Australia/Melbourne";

    public string[] TrackedMarketInstrumentFilter { get; set; } = [];

    public TrackedMarketOptions[] TrackedMarkets { get; set; } = [];
}
