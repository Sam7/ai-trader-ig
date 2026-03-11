using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record ApprovedTrade(
    InstrumentId Instrument,
    TradeDirection Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal RiskAmount,
    decimal RewardRiskRatio,
    DateTimeOffset ApprovedAtUtc);
