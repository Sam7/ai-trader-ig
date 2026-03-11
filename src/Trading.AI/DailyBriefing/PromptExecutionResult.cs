using Microsoft.Extensions.AI;

namespace Trading.AI.DailyBriefing;

public sealed record PromptExecutionResult(
    string PromptId,
    string PromptName,
    string ModelId,
    string RequestText,
    ChatResponse Response,
    string ResponseText);
