using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Trading.AI.Prompts;

public sealed class PromptRegistry
{
    public static PromptDefinition DailyBriefResearch { get; } = new(
        "daily-brief-research",
        "daily-brief-research",
        "Trading.AI.Prompts.DailyBriefResearch.DailyBriefResearch.md",
        "1");

    public static PromptDefinition DailyPlanJson { get; } = new(
        "daily-plan-json",
        "daily-plan-json",
        "Trading.AI.Prompts.DailyPlanJson.DailyPlanJson.md",
        "1",
        "Trading.AI.Prompts.DailyPlanJson.DailyPlanJson.schema.json",
        "1");

    public static PromptDefinition IntradayOpportunityReview { get; } = new(
        "intraday-opportunity-review",
        "intraday-opportunity-review",
        "Trading.AI.Prompts.IntradayOpportunityReview.IntradayOpportunityReview.md",
        "1",
        "Trading.AI.Prompts.IntradayOpportunityReview.IntradayOpportunityReview.schema.json",
        "1");

    private static readonly IReadOnlyDictionary<string, PromptDefinition> Definitions = new Dictionary<string, PromptDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [DailyBriefResearch.Id] = DailyBriefResearch,
        [DailyPlanJson.Id] = DailyPlanJson,
        [IntradayOpportunityReview.Id] = IntradayOpportunityReview,
    };

    private readonly Assembly _assembly = typeof(PromptRegistry).Assembly;

    public string GetPromptText(PromptDefinition definition)
        => GetResourceText(definition.ResourceName);

    public PromptContractProvenance GetProvenance(PromptDefinition definition)
    {
        var promptSha256 = ComputeSha256(GetPromptText(definition));
        var schemaSha256 = definition.ResponseSchemaResourceName is null
            ? null
            : ComputeSha256(GetResourceText(definition.ResponseSchemaResourceName));
        return new PromptContractProvenance(
            definition.Id,
            definition.Version,
            promptSha256,
            definition.ResponseSchemaVersion,
            schemaSha256);
    }

    private string GetResourceText(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Prompt resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ComputeSha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public PromptDefinition GetById(string promptId)
        => Definitions.TryGetValue(promptId, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Prompt '{promptId}' is not registered.");
}
