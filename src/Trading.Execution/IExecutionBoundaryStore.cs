using Trading.Strategy.Shared;

namespace Trading.Execution;

public interface IExecutionBoundaryStore
{
    Task<ExecutionOperationReservationResult> ReserveOperationAsync(
        ExecutionOperationRequest request,
        string dealReference,
        DateTimeOffset reservedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionOperationRecord?> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionOperationRecord>> GetOperationsByTradingDateAsync(
        DateOnly tradingDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionOperationRecord>> GetUnresolvedOperationsAsync(
        CancellationToken cancellationToken = default);

    Task<ExecutionOperationSubmissionLease?> TryBeginOperationSubmissionAsync(
        string operationId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionOperationRecord> CompleteOperationAttemptAsync(
        ExecutionOperationAttemptCompletion completion,
        CancellationToken cancellationToken = default);

    Task<ExecutionReservationResult> ReserveAsync(
        ExecutionReadyTradeIntent intent,
        string dealReference,
        DateTimeOffset reservedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionBoundaryRecord?> GetAsync(
        string decisionId,
        CancellationToken cancellationToken = default);

    Task<ExecutionBoundaryRecord?> AttachDecisionAuditArtifactAsync(
        string decisionId,
        string decisionAuditPath,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionLease?> TryBeginSubmissionAsync(
        string decisionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ExecutionBoundaryRecord> CompleteAttemptAsync(
        ExecutionAttemptCompletion completion,
        CancellationToken cancellationToken = default);
}
