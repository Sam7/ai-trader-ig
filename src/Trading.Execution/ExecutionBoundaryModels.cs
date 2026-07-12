using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Execution;

public sealed record ExecutionBoundaryRecord(
    string DecisionId,
    ExecutionBoundaryState State,
    string SourceDecisionAuditId,
    string? SourceDecisionAuditPath,
    ExecutionReadyTradeIntent Intent,
    DateOnly TradingDate,
    InstrumentId Instrument,
    TradeDirection Direction,
    TradeEntryMethod EntryMethod,
    string DealReference,
    string? DealId,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    int AttemptCount,
    string? LastError);

public sealed record ExecutionReservationResult(
    ExecutionBoundaryRecord Record,
    bool Created);

public sealed record ExecutionSubmissionLease(
    ExecutionBoundaryRecord Record,
    int AttemptNumber);

public sealed record ExecutionAttemptCompletion(
    string DecisionId,
    int AttemptNumber,
    ExecutionBoundaryState State,
    DateTimeOffset CompletedAtUtc,
    string? DealReference = null,
    string? DealId = null,
    OrderStatus? BrokerStatus = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ExecutionBoundarySnapshot(
    string DecisionId,
    ExecutionBoundaryState State,
    string DealReference,
    string? DealId,
    int AttemptCount,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? LastError)
{
    public static ExecutionBoundarySnapshot From(ExecutionBoundaryRecord record)
        => new(
            record.DecisionId,
            record.State,
            record.DealReference,
            record.DealId,
            record.AttemptCount,
            record.ReservedAtUtc,
            record.UpdatedAtUtc,
            record.LastError);
}

public enum ExecutionOperationKind
{
    MarketOpen = 1,
    PositionClose = 2,
    PositionUpdate = 3,
    WorkingOrderCreate = 4,
    WorkingOrderUpdate = 5,
    WorkingOrderCancel = 6,
}

public enum ExecutionOperationSource
{
    AutomatedDecision = 1,
    ManualCli = 2,
}

public sealed record ExecutionOperationRequest(
    string OperationId,
    ExecutionOperationKind Kind,
    ExecutionOperationSource Source,
    string? SourceDecisionAuditId = null,
    string? SourceDecisionAuditPath = null,
    ExecutionReadyTradeIntent? Intent = null,
    DateOnly? TradingDate = null,
    InstrumentId? Instrument = null,
    TradeDirection? Direction = null,
    TradeEntryMethod? EntryMethod = null,
    decimal? Size = null,
    decimal? StopLevel = null,
    decimal? LimitLevel = null,
    string? RelatedDealId = null);

public sealed record ExecutionOperationRecord(
    string OperationId,
    ExecutionOperationKind Kind,
    ExecutionOperationSource Source,
    ExecutionBoundaryState State,
    string? SourceDecisionAuditId,
    string? SourceDecisionAuditPath,
    ExecutionReadyTradeIntent? Intent,
    DateOnly? TradingDate,
    InstrumentId? Instrument,
    TradeDirection? Direction,
    TradeEntryMethod? EntryMethod,
    decimal? Size,
    decimal? StopLevel,
    decimal? LimitLevel,
    string? RelatedDealId,
    string DealReference,
    string? DealId,
    OrderStatus? BrokerStatus,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    int AttemptCount,
    string? LastError);

public sealed record ExecutionOperationReservationResult(
    ExecutionOperationRecord Record,
    bool Created);

public sealed record ExecutionOperationSubmissionLease(
    ExecutionOperationRecord Record,
    int AttemptNumber);

public sealed record ExecutionOperationAttemptCompletion(
    string OperationId,
    int AttemptNumber,
    ExecutionBoundaryState State,
    DateTimeOffset CompletedAtUtc,
    string? DealReference = null,
    string? DealId = null,
    OrderStatus? BrokerStatus = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ExecutionSubmissionResult(
    ExecutionOperationRecord Record,
    string DealReference,
    string? DealId,
    OrderStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc);
