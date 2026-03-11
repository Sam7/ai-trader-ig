using Trading.Strategy.MarketAttention;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

namespace Trading.Strategy.Persistence;

public sealed record PendingOpportunityReview(
    string ReviewId,
    MarketEvent MarketEvent,
    MarketWatch MarketWatch,
    MarketSignal Signal,
    StrategyRules Rules,
    ActiveTrade? ActiveTrade,
    DateTimeOffset CreatedAtUtc);
