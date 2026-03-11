using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public enum TradeLifecycle
{
    Submitted = 0,
    Filled = 1,
    PartiallyFilled = 2,
    Amended = 3,
    StoppedOut = 4,
    TargetHit = 5,
    Closed = 6,
}

public sealed record ActiveTrade(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal RiskAmount,
    decimal RewardRiskRatio,
    TradeLifecycle Lifecycle,
    DateTimeOffset UpdatedAtUtc,
    string? BrokerReference = null,
    decimal? FilledQuantity = null);
