using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public enum ExecutionReportType
{
    Submitted = 0,
    Filled = 1,
    PartiallyFilled = 2,
    Rejected = 3,
    Amended = 4,
    StoppedOut = 5,
    TargetHit = 6,
    Closed = 7,
}

public sealed record ExecutionReport(
    InstrumentId Instrument,
    ExecutionReportType Type,
    DateTimeOffset OccurredAtUtc,
    string? BrokerReference = null,
    decimal? FilledQuantity = null);
