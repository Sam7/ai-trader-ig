namespace Trading.AI.Configuration;

public sealed class PromptObservabilityOptions
{
    public const string SectionName = "AI:Prompts";

    public string ObservabilityRootPath { get; set; } = Path.Combine("Logs", "Observability");
}
