using Trading.Abstractions;

namespace Trading.Strategy.Shared;

public sealed record TradeScenario(
    TradeDirection Direction,
    string Thesis,
    string Confirmation,
    string Invalidation,
    IReadOnlyList<string> ExpectedCatalysts,
    DateTimeOffset? AvoidTradingUntilUtc);
