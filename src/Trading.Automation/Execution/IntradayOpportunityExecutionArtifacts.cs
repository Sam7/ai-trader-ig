namespace Trading.Automation.Execution;

public sealed record IntradayOpportunityExecutionArtifacts(
    ArtifactReference PromptEnvelopeArtifact,
    ArtifactReference ExtractedJsonArtifact,
    IReadOnlyList<ArtifactReference> AttachmentArtifacts);
