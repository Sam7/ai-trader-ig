using Trading.Strategy.Shared;

namespace Trading.Strategy.DayPlanning;

public interface IDailyBriefingComposer
{
    Task<TradingDayPlan> ComposeAsync(DailyBriefingRequest request, CancellationToken cancellationToken = default);
}
