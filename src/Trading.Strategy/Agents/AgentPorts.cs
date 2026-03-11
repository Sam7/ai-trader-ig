using Trading.Strategy.Context;

namespace Trading.Strategy.Agents;

public interface IResearchBriefingAgent
{
    Task<DailyBriefing> CreateDailyBriefingAsync(ResearchBriefingInput input, CancellationToken cancellationToken = default);
}

public interface ITradePlannerAgent
{
    Task<TradePlanningResult> CreateTradePlanAsync(TradePlanningInput input, CancellationToken cancellationToken = default);
}

public interface IRiskGateAgent
{
    Task<RiskGateDecision> EvaluateAsync(RiskGateInput input, CancellationToken cancellationToken = default);
}
