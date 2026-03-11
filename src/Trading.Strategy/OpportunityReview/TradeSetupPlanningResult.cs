using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public abstract record TradeSetupPlanningResult;

public sealed record PlannedTradeSetup(TradeSetup TradeSetup) : TradeSetupPlanningResult;

public sealed record StandAsideSetup(StandAsideDecision Decision) : TradeSetupPlanningResult;
