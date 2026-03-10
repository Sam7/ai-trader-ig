using Trading.Abstractions;

namespace Trading.IG;

public sealed record OrderSubmissionRecord(
    string DealReference,
    OrderSubmissionKind Kind,
    DateTimeOffset SubmittedAtUtc,
    InstrumentId? Instrument,
    TradeDirection Direction,
    decimal Size,
    string? RelatedDealId);
