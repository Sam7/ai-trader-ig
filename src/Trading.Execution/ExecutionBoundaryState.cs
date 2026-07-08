namespace Trading.Execution;

public enum ExecutionBoundaryState
{
    Reserved = 1,
    Submitting = 2,
    Submitted = 3,
    Confirmed = 4,
    BrokerRejected = 5,
    FailedBeforeSubmission = 6,
    OutcomeUncertain = 7,
    Closed = 8,
}
