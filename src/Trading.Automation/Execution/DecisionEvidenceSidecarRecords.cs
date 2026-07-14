using Trading.Abstractions;

namespace Trading.Automation.Execution;

public sealed record DecisionEvaluationRecord(
    string SchemaVersion,
    string EvaluationId,
    string SourceAuditId,
    ArtifactReference SourceAuditArtifact,
    string SourceAuditSha256,
    DateTimeOffset EvaluatedAtUtc,
    PriceResolution Resolution,
    AuditDataQualityPolicy DataQualityPolicy,
    IReadOnlyList<PaperTradeOutcome> PaperOutcomes,
    IReadOnlyList<PaperMarketAssessmentOutcome> MarketAssessmentOutcomes,
    DecisionBiasSummary BiasSummary,
    DecisionAuditDataQualitySummary DataQuality,
    string CalculationVersion);

public sealed record DemoCanaryExecutionRecord(
    string SchemaVersion,
    string RecordId,
    string SourceAuditId,
    ArtifactReference SourceAuditArtifact,
    string SourceAuditSha256,
    DateTimeOffset RecordedAtUtc,
    DemoCanaryExecutionSnapshot Execution);

public sealed record DecisionEvaluationWriteResult(
    DecisionEvaluationRecord Record,
    ArtifactReference Artifact);
