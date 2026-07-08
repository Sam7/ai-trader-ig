using Trading.Strategy.Shared;

namespace Trading.Execution;

public sealed class ExecutionBoundaryService
{
    private readonly IExecutionBoundaryStore _store;
    private readonly IExecutionDealReferenceFactory _dealReferenceFactory;
    private readonly IExecutionClock _clock;

    public ExecutionBoundaryService(
        IExecutionBoundaryStore store,
        IExecutionDealReferenceFactory dealReferenceFactory,
        IExecutionClock clock)
    {
        _store = store;
        _dealReferenceFactory = dealReferenceFactory;
        _clock = clock;
    }

    public Task<ExecutionReservationResult> ReserveAsync(
        ExecutionReadyTradeIntent intent,
        CancellationToken cancellationToken = default)
        => _store.ReserveAsync(
            intent,
            _dealReferenceFactory.CreateOpenReference(intent.DecisionId),
            _clock.UtcNow,
            cancellationToken);

    public Task<ExecutionBoundaryRecord?> AttachDecisionAuditArtifactAsync(
        string decisionId,
        string decisionAuditPath,
        CancellationToken cancellationToken = default)
        => _store.AttachDecisionAuditArtifactAsync(decisionId, decisionAuditPath, _clock.UtcNow, cancellationToken);
}
