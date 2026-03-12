namespace Trading.AI.Observability;

public sealed record PromptObservationSession(
    string JsonPath,
    string BasePath,
    string? TextArtifactPath,
    string StructuredArtifactPath);
