using Trading.Abstractions;

namespace Trading.Automation.Execution;

public enum DecisionEvidenceKind
{
    PriceChart = 1,
    MarketData = 2,
    Research = 3,
    Other = 4,
}

public sealed record PreparedDecisionEvidence(
    DecisionEvidenceKind Kind,
    string Label,
    string MediaType,
    byte[] Data,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    DateTimeOffset? AsOfUtc,
    string RecipeId,
    string RecipeVersion,
    bool AttachToPrompt = true);

public sealed record DecisionEvidence(
    string EvidenceId,
    DecisionEvidenceKind Kind,
    string Label,
    InstrumentId? Instrument,
    string MediaType,
    ArtifactReference Artifact,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    DateTimeOffset? AsOfUtc,
    string RecipeId,
    string RecipeVersion,
    string Sha256);

public sealed record IntradayPreparationProfileReference(string Id, string Version)
{
    public static IntradayPreparationProfileReference Default { get; } = new("intraday-default", "1");
}
