using Trading.AI.Configuration;
using Trading.AI.Prompts;
using Microsoft.Extensions.AI;

namespace Trading.AI.DailyBriefing;

public sealed record PromptExecutionContext(
    PromptDefinition Prompt,
    DailyBriefingModelOptions Model,
    IReadOnlyDictionary<string, string> Variables,
    DateTimeOffset RequestedAtUtc,
    DateOnly TradingDate,
    ChatResponseFormat? ResponseFormat = null);
