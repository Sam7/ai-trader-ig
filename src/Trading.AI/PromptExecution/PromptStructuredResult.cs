using Microsoft.Extensions.AI;

namespace Trading.AI.PromptExecution;

public sealed record PromptStructuredResult<TResult>(
    string PromptId,
    string PromptName,
    string ModelId,
    string RequestText,
    ChatResponse Response,
    string ResponseText,
    TResult StructuredValue,
    string StructuredArtifactPath);
