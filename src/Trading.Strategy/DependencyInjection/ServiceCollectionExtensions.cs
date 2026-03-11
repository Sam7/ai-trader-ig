using Microsoft.Extensions.DependencyInjection;
using Trading.Strategy.ActiveTradeManagement;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.ExecutionReporting;
using Trading.Strategy.MarketAttention;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Rules;
using Trading.Strategy.Workflow;

namespace Trading.Strategy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingStrategyCore(
        this IServiceCollection services,
        StrategyRules? strategyRules = null)
    {
        var rules = strategyRules ?? StrategyRules.Default;
        rules.Validate();

        services.AddSingleton(rules);
        services.AddSingleton<ITradingDayStore, InMemoryTradingDayStore>();
        services.AddSingleton<PositionSizer>();
        services.AddSingleton<BreakEvenStopRule>();
        services.AddTransient<TradingDayPlanner>();
        services.AddTransient<MarketAttentionService>();
        services.AddTransient<OpportunityReviewer>();
        services.AddTransient<ActiveTradeReviewer>();
        services.AddTransient<ExecutionReportApplier>();
        services.AddTransient<ITradingDayWorkflow, TradingDayWorkflow>();
        return services;
    }
}
