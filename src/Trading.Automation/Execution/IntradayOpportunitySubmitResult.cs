using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed record IntradayOpportunitySubmitResult(
    IntradayOpportunityPreparationDocument PreparedRun,
    IntradayOpportunityExecutionArtifacts ExecutionArtifacts,
    IntradayOpportunityBatch Batch,
    IntradayOpportunityReviewResult WorkflowResult);
