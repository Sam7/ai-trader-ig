namespace Trading.AI.PromptExecution;

public sealed record PromptAttachment(
    string Label,
    string MediaType,
    byte[] Data);
