namespace Trading.AI.Prompts;

public sealed record PromptDefinition(
    string Id,
    string Name,
    string ResourceName,
    string Version,
    string? ResponseSchemaResourceName = null,
    string? ResponseSchemaVersion = null);

public sealed record PromptContractProvenance(
    string PromptId,
    string PromptVersion,
    string PromptSha256,
    string? ResponseSchemaVersion,
    string? ResponseSchemaSha256);
