using Microsoft.Extensions.DependencyInjection;
using Trading.Strategy.Configuration;
using Trading.Strategy.Execution;
using Trading.Strategy.Monitoring;
using Trading.Strategy.Workflow;

namespace Trading.Strategy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingStrategyCore(
        this IServiceCollection services,
        StrategyProfile? strategyProfile = null)
    {
        var profile = strategyProfile ?? StrategyProfile.Default;
        profile.Validate();

        services.AddSingleton(profile);
        services.AddSingleton<ITradingDayStateStore, InMemoryTradingDayStateStore>();
        services.AddSingleton<MarketMonitor>();
        services.AddSingleton<ExecutionPolicy>();
        services.AddTransient<ITradingStrategyDirector, TradingStrategyDirector>();
        return services;
    }
}
