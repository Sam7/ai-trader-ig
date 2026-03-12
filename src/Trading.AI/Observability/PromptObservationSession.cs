namespace Trading.AI.Observability;

public sealed class PromptObservationSession
{
    public PromptObservationSession(
        string jsonPath,
        string basePath,
        string? textArtifactPath,
        string structuredArtifactPath)
    {
        JsonPath = jsonPath;
        BasePath = basePath;
        TextArtifactPath = textArtifactPath;
        StructuredArtifactPath = structuredArtifactPath;
    }

    public string JsonPath { get; }

    public string BasePath { get; }

    public string? TextArtifactPath { get; }

    public string StructuredArtifactPath { get; }

    public List<string> AttachmentArtifactPaths { get; } = [];
}
