using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;

namespace Trading.Strategy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingStrategyCore(
        this IServiceCollection services,
        DailyPlanningPolicy? planningPolicy = null)
    {
        var policy = planningPolicy ?? DailyPlanningPolicy.Default;
        policy.Validate();

        services.AddSingleton(policy);
        services.TryAddSingleton<ITradingDayStore, InMemoryTradingDayStore>();
        services.TryAddSingleton(ShadowDecisionPolicy.Disabled());
        services.AddTransient<ITradingDayPlanner, TradingDayPlanner>();
        services.AddTransient<IntradayCandidateDecisionService>();
        services.AddTransient<IIntradayDecisionService, IntradayOpportunityReviewService>();
        return services;
    }
}
