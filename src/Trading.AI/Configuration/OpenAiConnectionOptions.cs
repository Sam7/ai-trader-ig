namespace Trading.AI.Configuration;

public sealed class OpenAiConnectionOptions
{
    public const string SectionName = "AI:OpenAI";

    public string ApiKey { get; init; } = string.Empty;
}
