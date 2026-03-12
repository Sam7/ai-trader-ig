using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed record IntradayOpportunityReviewExecution(
    IntradayOpportunityBatch Batch,
    string RequestText,
    string EnvelopeArtifactPath,
    string StructuredArtifactPath,
    IReadOnlyList<string> AttachmentArtifactPaths);
