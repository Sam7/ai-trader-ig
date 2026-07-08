using Trading.Abstractions;
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

    public async Task<ExecutionBoundaryRecord> SubmitOnceAsync(
        ExecutionReadyTradeIntent intent,
        Func<string, CancellationToken, Task<PlaceOrderResult>> submitter,
        Func<ExecutionBoundaryRecord, CancellationToken, Task>? preSubmissionCheck = null,
        CancellationToken cancellationToken = default)
    {
        var reservation = await ReserveAsync(intent, cancellationToken);
        var lease = await _store.TryBeginSubmissionAsync(intent.DecisionId, _clock.UtcNow, cancellationToken);
        if (lease is null)
        {
            return await _store.GetAsync(intent.DecisionId, cancellationToken) ?? reservation.Record;
        }

        if (preSubmissionCheck is not null)
        {
            try
            {
                await preSubmissionCheck(lease.Record, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await _store.CompleteAttemptAsync(
                    new ExecutionAttemptCompletion(
                        intent.DecisionId,
                        lease.AttemptNumber,
                        ExecutionBoundaryState.FailedBeforeSubmission,
                        _clock.UtcNow,
                        ErrorMessage: exception.Message),
                    cancellationToken);
            }
        }

        try
        {
            var result = await submitter(lease.Record.DealReference, cancellationToken);
            return await _store.CompleteAttemptAsync(
                ToCompletion(intent.DecisionId, lease.AttemptNumber, result),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await _store.CompleteAttemptAsync(
                new ExecutionAttemptCompletion(
                    intent.DecisionId,
                    lease.AttemptNumber,
                    ExecutionBoundaryState.OutcomeUncertain,
                    _clock.UtcNow,
                    ErrorCode: exception.GetType().Name,
                    ErrorMessage: exception.Message),
                cancellationToken);
        }
    }

    public async Task<ExecutionBoundaryRecord?> ReconcileAsync(
        string decisionId,
        Func<string, CancellationToken, Task<OrderSummary?>> statusProvider,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(decisionId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.State is ExecutionBoundaryState.Reserved or ExecutionBoundaryState.FailedBeforeSubmission)
        {
            return record;
        }

        var status = await statusProvider(record.DealReference, cancellationToken);
        var state = ResolveReconciledState(status);
        return await _store.CompleteAttemptAsync(
            new ExecutionAttemptCompletion(
                record.DecisionId,
                Math.Max(1, record.AttemptCount),
                state,
                _clock.UtcNow,
                DealReference: status?.DealReference,
                DealId: status?.DealId,
                BrokerStatus: status?.Status,
                ErrorMessage: status?.Message),
            cancellationToken);
    }

    private ExecutionAttemptCompletion ToCompletion(
        string decisionId,
        int attemptNumber,
        PlaceOrderResult result)
        => new(
            decisionId,
            attemptNumber,
            ResolveSubmittedState(result.Status, result.DealId),
            _clock.UtcNow,
            DealReference: result.DealReference,
            DealId: result.DealId,
            BrokerStatus: result.Status,
            ErrorMessage: result.Message);

    private static ExecutionBoundaryState ResolveSubmittedState(OrderStatus status, string? dealId)
        => status switch
        {
            OrderStatus.Rejected => ExecutionBoundaryState.BrokerRejected,
            OrderStatus.Closed => ExecutionBoundaryState.Closed,
            OrderStatus.Open or OrderStatus.Accepted when !string.IsNullOrWhiteSpace(dealId) => ExecutionBoundaryState.Confirmed,
            OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Open => ExecutionBoundaryState.Submitted,
            _ => ExecutionBoundaryState.OutcomeUncertain,
        };

    private static ExecutionBoundaryState ResolveReconciledState(OrderSummary? status)
        => status?.Status switch
        {
            OrderStatus.Rejected => ExecutionBoundaryState.BrokerRejected,
            OrderStatus.Closed => ExecutionBoundaryState.Closed,
            OrderStatus.Open or OrderStatus.Accepted when !string.IsNullOrWhiteSpace(status.DealId) => ExecutionBoundaryState.Confirmed,
            OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Open => ExecutionBoundaryState.Submitted,
            _ => ExecutionBoundaryState.OutcomeUncertain,
        };
}
