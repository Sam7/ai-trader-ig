using Trading.AI.Prompts.IntradayOpportunityReview;

namespace Trading.Automation.Execution;

public sealed record IntradayOpportunityPreparationDocument(
    DateOnly TradingDate,
    DateTimeOffset RequestedAtUtc,
    string PromptId,
    IntradayOpportunityReviewInput Input,
    string RenderedRequestText,
    IReadOnlyList<IntradayOpportunityPreparedMarket> Markets,
    IReadOnlyList<IntradayOpportunityPreparedAttachment> Attachments,
    ArtifactReference PreparedArtifact,
    ArtifactReference RequestTextArtifact);
