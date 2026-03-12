using Microsoft.Extensions.AI;

namespace Trading.AI.PromptExecution;

public sealed record PromptTextResult(
    string PromptId,
    string PromptName,
    string ModelId,
    string RequestText,
    ChatResponse Response,
    string ResponseText,
    string? TextArtifactPath);
