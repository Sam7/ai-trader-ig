using Trading.AI.DailyBriefing;
using Trading.AI.Prompts;

namespace Trading.Automation.Execution;

public sealed record IntradayOpportunityPreparationDocument(
    DateOnly TradingDate,
    DateTimeOffset RequestedAtUtc,
    string PromptId,
    IntradayOpportunityReviewRequest Request,
    string RenderedRequestText,
    IReadOnlyList<IntradayOpportunityPreparedMarket> Markets,
    IReadOnlyList<IntradayOpportunityPreparedAttachment> Attachments,
    ArtifactReference PreparedArtifact,
    ArtifactReference RequestTextArtifact)
{
    public const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IntradayPreparationProfileReference PreparationProfile { get; init; }
        = IntradayPreparationProfileReference.Default;

    public PromptContractProvenance? PromptContract { get; init; }

    public string RequestSha256 { get; init; } = string.Empty;

    public IReadOnlyList<DecisionEvidence> Evidence { get; init; } = [];
}
