using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.AI.DependencyInjection;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Automation.Scheduling;
using Trading.Strategy.DependencyInjection;
using Trading.Strategy.Inputs;
using Trading.Strategy.OpportunityReview;

namespace Trading.Automation.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingAutomation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AutomationOptions>()
            .Bind(configuration.GetSection(AutomationOptions.SectionName));

        services.AddTradingAi(configuration);
        services.AddTradingStrategyCore();

        services.AddSingleton<SystemTradingClock>();
        services.AddSingleton<ITradingClock>(sp => sp.GetRequiredService<SystemTradingClock>());
        services.AddSingleton<IRiskContextSource, PassiveRiskContextSource>();
        services.AddSingleton<ITradeSetupPlanner, NoOpTradeSetupPlanner>();
        services.AddSingleton<ITradeApprover, NoOpTradeApprover>();

        services.AddTransient<DailyBriefingResearchService>();
        services.AddTransient<DailyBriefingPlanService>();
        services.AddSingleton<IntradayPriceSeriesCache>();
        services.AddSingleton<IntradayOpportunityPreparationWriter>();
        services.AddTransient<IntradayOpportunityScanService>();
        services.AddTransient<DailyBriefingTickerJob>();
        services.AddTransient<IntradayOpportunityTickerJob>();
        services.AddHostedService<DailyBriefingScheduleInitializer>();
        services.AddHostedService<IntradayOpportunityScheduleInitializer>();
        return services;
    }
}
