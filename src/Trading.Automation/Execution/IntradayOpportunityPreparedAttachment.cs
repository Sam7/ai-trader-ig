namespace Trading.Automation.Execution;

public sealed record IntradayOpportunityPreparedAttachment(
    string Label,
    string MediaType,
    ArtifactReference Artifact);
