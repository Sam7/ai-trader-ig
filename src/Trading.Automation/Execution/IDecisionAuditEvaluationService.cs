namespace Trading.Automation.Execution;

public interface IDecisionAuditEvaluationService
{
    Task<DecisionAuditEvaluationReport> EvaluateAsync(
        DecisionAuditEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
