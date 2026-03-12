namespace Trading.AI.PromptExecution;

public sealed record PromptInputData(
    IReadOnlyDictionary<string, string> Variables,
    DateOnly PromptDate,
    DateTimeOffset RequestedAtUtc);
