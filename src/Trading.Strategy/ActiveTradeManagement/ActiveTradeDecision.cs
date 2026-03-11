using Trading.Abstractions;

namespace Trading.Strategy.ActiveTradeManagement;

public abstract record ActiveTradeDecision(
    string Summary,
    DateTimeOffset DecidedAtUtc);

public sealed record NoActiveTrade(
    string Summary,
    DateTimeOffset DecidedAtUtc)
    : ActiveTradeDecision(Summary, DecidedAtUtc);

public sealed record HoldTrade(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset DecidedAtUtc)
    : ActiveTradeDecision(Summary, DecidedAtUtc);

public sealed record TightenStop(
    InstrumentId Instrument,
    decimal SuggestedStopPrice,
    string Summary,
    DateTimeOffset DecidedAtUtc)
    : ActiveTradeDecision(Summary, DecidedAtUtc);

public sealed record ExitTrade(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset DecidedAtUtc)
    : ActiveTradeDecision(Summary, DecidedAtUtc);

public sealed record EscalateTradeReview(
    InstrumentId Instrument,
    string Summary,
    DateTimeOffset DecidedAtUtc)
    : ActiveTradeDecision(Summary, DecidedAtUtc);
