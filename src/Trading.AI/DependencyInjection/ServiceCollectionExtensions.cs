using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.AI.Configuration;
using Trading.AI.DailyBriefing;
using Trading.AI.Observability;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.Strategy.DayPlanning;

namespace Trading.AI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenAiConnectionOptions>()
            .Bind(configuration.GetSection(OpenAiConnectionOptions.SectionName));

        services.AddOptions<DailyBriefingOptions>()
            .Bind(configuration.GetSection(DailyBriefingOptions.SectionName));

        services.AddOptions<PromptObservabilityOptions>()
            .Bind(configuration.GetSection(PromptObservabilityOptions.SectionName));
        services.PostConfigure<PromptObservabilityOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ObservabilityRootPath))
            {
                options.ObservabilityRootPath =
                    configuration[$"{DailyBriefingOptions.SectionName}:ObservabilityRootPath"]
                    ?? Path.Combine("Logs", "Observability");
            }
        });

        services.AddSingleton<PromptRegistry>();
        services.AddSingleton<PromptTemplateRenderer>();
        services.AddSingleton<TrackedMarketsFormatter>();
        services.AddSingleton<PromptInputConverter>();
        services.AddSingleton<PromptObservabilityWriter>();
        services.AddSingleton<DailyPlanMapper>();
        services.AddSingleton<IChatClientFactory, OpenAiChatClientFactory>();
        services.AddTransient<PromptExecutor>();
        services.AddTransient<DailyBriefResearcher>();
        services.AddTransient<DailyPlanConverter>();
        services.AddTransient<IDailyBriefingComposer, OpenAiDailyBriefingComposer>();
        return services;
    }
}
