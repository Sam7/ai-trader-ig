using Microsoft.Extensions.AI;
using Trading.AI.Configuration;
using Trading.AI.Prompts;

namespace Trading.AI.PromptExecution;

internal sealed record PromptInvocation(
    PromptDefinition Prompt,
    PromptModelOptions Model,
    IReadOnlyDictionary<string, string> Variables,
    DateOnly PromptDate,
    DateTimeOffset RequestedAtUtc,
    ChatResponseFormat? ResponseFormat,
    PromptTextArtifactKind TextArtifactKind);
