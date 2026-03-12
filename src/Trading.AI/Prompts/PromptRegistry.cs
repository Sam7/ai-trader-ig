using System.Reflection;

namespace Trading.AI.Prompts;

public sealed class PromptRegistry
{
    public static PromptDefinition DailyBriefResearch { get; } = new(
        "daily-brief-research",
        "daily-brief-research",
        "Trading.AI.Prompts.DailyBriefResearch.DailyBriefResearch.md");

    public static PromptDefinition DailyPlanJson { get; } = new(
        "daily-plan-json",
        "daily-plan-json",
        "Trading.AI.Prompts.DailyPlanJson.DailyPlanJson.md");

    public static PromptDefinition IntradayOpportunityReview { get; } = new(
        "intraday-opportunity-review",
        "intraday-opportunity-review",
        "Trading.AI.Prompts.IntradayOpportunityReview.IntradayOpportunityReview.md");

    private static readonly IReadOnlyDictionary<string, PromptDefinition> Definitions = new Dictionary<string, PromptDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [DailyBriefResearch.Id] = DailyBriefResearch,
        [DailyPlanJson.Id] = DailyPlanJson,
        [IntradayOpportunityReview.Id] = IntradayOpportunityReview,
    };

    private readonly Assembly _assembly = typeof(PromptRegistry).Assembly;

    public string GetPromptText(PromptDefinition definition)
    {
        using var stream = _assembly.GetManifestResourceStream(definition.ResourceName)
            ?? throw new InvalidOperationException($"Prompt resource '{definition.ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public PromptDefinition GetById(string promptId)
        => Definitions.TryGetValue(promptId, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Prompt '{promptId}' is not registered.");
}
