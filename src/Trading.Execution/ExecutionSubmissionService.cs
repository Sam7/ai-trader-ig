using Trading.Abstractions;

namespace Trading.Execution;

public interface IExecutionSubmissionService
{
    Task<ExecutionSubmissionResult> SubmitMarketOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionResult> SubmitClosePositionAsync(
        string operationId,
        ExecutionOperationSource source,
        ClosePositionRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionResult> SubmitUpdatePositionAsync(
        string operationId,
        ExecutionOperationSource source,
        UpdatePositionRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionResult> SubmitCreateWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        CreateWorkingOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionResult> SubmitUpdateWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        UpdateWorkingOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<ExecutionSubmissionResult> SubmitCancelWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        string dealId,
        CancellationToken cancellationToken = default);

    Task<ExecutionOperationRecord?> ReconcileAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutionSubmissionService : IExecutionSubmissionService
{
    private readonly IExecutionBoundaryStore _store;
    private readonly IExecutionDealReferenceFactory _dealReferenceFactory;
    private readonly IExecutionClock _clock;
    private readonly ITradingGateway _gateway;

    public ExecutionSubmissionService(
        IExecutionBoundaryStore store,
        IExecutionDealReferenceFactory dealReferenceFactory,
        IExecutionClock clock,
        ITradingGateway gateway)
    {
        _store = store;
        _dealReferenceFactory = dealReferenceFactory;
        _clock = clock;
        _gateway = gateway;
    }

    public Task<ExecutionSubmissionResult> SubmitMarketOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.MarketOpen,
                source,
                Instrument: request.Instrument,
                Direction: request.Direction,
                Size: request.Size),
            async (dealReference, token) =>
            {
                var result = await _gateway.PlaceMarketOrderAsync(request with { DealReference = dealReference }, token);
                return FromPlaceOrderResult(result);
            },
            cancellationToken);

    public Task<ExecutionSubmissionResult> SubmitClosePositionAsync(
        string operationId,
        ExecutionOperationSource source,
        ClosePositionRequest request,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.PositionClose,
                source,
                Size: request.Size,
                RelatedDealId: request.DealId),
            async (dealReference, token) =>
            {
                var result = await _gateway.ClosePositionAsync(request with { DealReference = dealReference }, token);
                return FromClosePositionResult(result);
            },
            cancellationToken);

    public Task<ExecutionSubmissionResult> SubmitUpdatePositionAsync(
        string operationId,
        ExecutionOperationSource source,
        UpdatePositionRequest request,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.PositionUpdate,
                source,
                RelatedDealId: request.DealId),
            async (_, token) =>
            {
                var result = await _gateway.UpdatePositionAsync(request, token);
                return FromUpdatePositionResult(result);
            },
            cancellationToken);

    public Task<ExecutionSubmissionResult> SubmitCreateWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        CreateWorkingOrderRequest request,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.WorkingOrderCreate,
                source,
                Instrument: request.Instrument,
                Direction: request.Direction,
                Size: request.Size),
            async (_, token) =>
            {
                var result = await _gateway.PlaceWorkingOrderAsync(request, token);
                return FromWorkingOrderResult(result);
            },
            cancellationToken);

    public Task<ExecutionSubmissionResult> SubmitUpdateWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        UpdateWorkingOrderRequest request,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.WorkingOrderUpdate,
                source,
                RelatedDealId: request.DealId),
            async (_, token) =>
            {
                var result = await _gateway.UpdateWorkingOrderAsync(request, token);
                return FromWorkingOrderResult(result);
            },
            cancellationToken);

    public Task<ExecutionSubmissionResult> SubmitCancelWorkingOrderAsync(
        string operationId,
        ExecutionOperationSource source,
        string dealId,
        CancellationToken cancellationToken = default)
        => SubmitOnceAsync(
            new ExecutionOperationRequest(
                operationId,
                ExecutionOperationKind.WorkingOrderCancel,
                source,
                RelatedDealId: dealId),
            async (_, token) =>
            {
                var result = await _gateway.CancelWorkingOrderAsync(dealId, token);
                return FromWorkingOrderResult(result);
            },
            cancellationToken);

    public async Task<ExecutionOperationRecord?> ReconcileAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.GetOperationAsync(operationId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.State is ExecutionBoundaryState.Reserved or ExecutionBoundaryState.FailedBeforeSubmission)
        {
            return record;
        }

        var status = await _gateway.GetOrderStatusAsync(record.DealReference, cancellationToken);
        return await _store.CompleteOperationAttemptAsync(
            new ExecutionOperationAttemptCompletion(
                record.OperationId,
                Math.Max(1, record.AttemptCount),
                ResolveState(status?.Status ?? OrderStatus.Unknown, status?.DealId),
                _clock.UtcNow,
                status?.DealReference,
                status?.DealId,
                status?.Status,
                ErrorMessage: status?.Message),
            cancellationToken);
    }

    private async Task<ExecutionSubmissionResult> SubmitOnceAsync(
        ExecutionOperationRequest request,
        Func<string, CancellationToken, Task<BrokerMutationResult>> submitter,
        CancellationToken cancellationToken)
    {
        var dealReference = _dealReferenceFactory.CreateReference(request.OperationId, request.Kind);
        var reservation = await _store.ReserveOperationAsync(request, dealReference, _clock.UtcNow, cancellationToken);
        var lease = await _store.TryBeginOperationSubmissionAsync(request.OperationId, _clock.UtcNow, cancellationToken);
        if (lease is null)
        {
            var current = await _store.GetOperationAsync(request.OperationId, cancellationToken) ?? reservation.Record;
            return FromOperationRecord(current);
        }

        try
        {
            var result = await submitter(lease.Record.DealReference, cancellationToken);
            var completed = await _store.CompleteOperationAttemptAsync(
                new ExecutionOperationAttemptCompletion(
                    request.OperationId,
                    lease.AttemptNumber,
                    ResolveState(result.Status, result.DealId),
                    result.TimestampUtc,
                    result.DealReference,
                    result.DealId,
                    result.Status,
                    ErrorMessage: result.Message),
                cancellationToken);

            return new ExecutionSubmissionResult(
                completed,
                completed.DealReference,
                completed.DealId,
                result.Status,
                result.Message,
                result.TimestampUtc);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var completed = await _store.CompleteOperationAttemptAsync(
                new ExecutionOperationAttemptCompletion(
                    request.OperationId,
                    lease.AttemptNumber,
                    ExecutionBoundaryState.OutcomeUncertain,
                    _clock.UtcNow,
                    ErrorCode: exception.GetType().Name,
                    ErrorMessage: exception.Message),
                cancellationToken);

            return FromOperationRecord(completed);
        }
    }

    private static ExecutionSubmissionResult FromOperationRecord(ExecutionOperationRecord record)
        => new(
            record,
            record.DealReference,
            record.DealId,
            record.BrokerStatus ?? ToOrderStatus(record.State),
            record.LastError,
            record.UpdatedAtUtc);

    private static BrokerMutationResult FromPlaceOrderResult(PlaceOrderResult result)
        => new(result.DealReference, result.DealId, result.Status, result.Message, result.TimestampUtc);

    private static BrokerMutationResult FromClosePositionResult(ClosePositionResult result)
        => new(result.DealReference, result.DealId, result.Status, result.Message, result.TimestampUtc);

    private static BrokerMutationResult FromUpdatePositionResult(UpdatePositionResult result)
        => new(result.DealReference, result.DealId, result.Status, result.Message, result.TimestampUtc);

    private static BrokerMutationResult FromWorkingOrderResult(WorkingOrderResult result)
        => new(result.DealReference, result.DealId, result.Status, result.Message, result.TimestampUtc);

    private static ExecutionBoundaryState ResolveState(OrderStatus status, string? dealId)
        => status switch
        {
            OrderStatus.Rejected => ExecutionBoundaryState.BrokerRejected,
            OrderStatus.Closed => ExecutionBoundaryState.Closed,
            OrderStatus.Open or OrderStatus.Accepted when !string.IsNullOrWhiteSpace(dealId) => ExecutionBoundaryState.Confirmed,
            OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Open => ExecutionBoundaryState.Submitted,
            _ => ExecutionBoundaryState.OutcomeUncertain,
        };

    private static OrderStatus ToOrderStatus(ExecutionBoundaryState state)
        => state switch
        {
            ExecutionBoundaryState.Confirmed => OrderStatus.Accepted,
            ExecutionBoundaryState.BrokerRejected => OrderStatus.Rejected,
            ExecutionBoundaryState.Closed => OrderStatus.Closed,
            ExecutionBoundaryState.Submitted or ExecutionBoundaryState.Submitting or ExecutionBoundaryState.Reserved => OrderStatus.Pending,
            _ => OrderStatus.Unknown,
        };

    private sealed record BrokerMutationResult(
        string DealReference,
        string? DealId,
        OrderStatus Status,
        string? Message,
        DateTimeOffset TimestampUtc);
}
