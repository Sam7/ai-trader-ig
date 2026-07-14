using Trading.Strategy.Shared;

namespace Trading.Strategy.DayPlanning;

public interface ITradingDayPlanner
{
    Task<TradingDayPlan> PlanAsync(
        TradingDayRequest request,
        CancellationToken cancellationToken = default);
}
