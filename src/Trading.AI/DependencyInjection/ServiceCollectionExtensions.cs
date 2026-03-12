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

        services.AddOptions<IntradayOpportunityReviewOptions>()
            .Bind(configuration.GetSection(IntradayOpportunityReviewOptions.SectionName));
        services.PostConfigure<IntradayOpportunityReviewOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(options.ModelId))
            {
                return;
            }

            var researchSection = configuration.GetSection($"{DailyBriefingOptions.SectionName}:Research");
            options.ModelId = researchSection[nameof(PromptModelOptions.ModelId)] ?? options.ModelId;

            if (options.Temperature is null
                && decimal.TryParse(researchSection[nameof(PromptModelOptions.Temperature)], out var temperature))
            {
                options.Temperature = temperature;
            }

            if (options.MaxOutputTokens is null
                && int.TryParse(researchSection[nameof(PromptModelOptions.MaxOutputTokens)], out var maxOutputTokens))
            {
                options.MaxOutputTokens = maxOutputTokens;
            }

            if (!options.EnableWebSearch
                && bool.TryParse(researchSection[nameof(PromptModelOptions.EnableWebSearch)], out var enableWebSearch))
            {
                options.EnableWebSearch = enableWebSearch;
            }

            var pricingSection = researchSection.GetSection(nameof(PromptModelOptions.Pricing));
            options.Pricing = new ModelPricingOptions
            {
                InputUsdPerMillionTokens = options.Pricing.InputUsdPerMillionTokens != 0m
                    ? options.Pricing.InputUsdPerMillionTokens
                    : ParseDecimal(pricingSection[nameof(ModelPricingOptions.InputUsdPerMillionTokens)]) ?? 0m,
                OutputUsdPerMillionTokens = options.Pricing.OutputUsdPerMillionTokens != 0m
                    ? options.Pricing.OutputUsdPerMillionTokens
                    : ParseDecimal(pricingSection[nameof(ModelPricingOptions.OutputUsdPerMillionTokens)]) ?? 0m,
                CachedInputUsdPerMillionTokens = options.Pricing.CachedInputUsdPerMillionTokens != 0m
                    ? options.Pricing.CachedInputUsdPerMillionTokens
                    : ParseDecimal(pricingSection[nameof(ModelPricingOptions.CachedInputUsdPerMillionTokens)]) ?? 0m,
            };
        });

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
        services.AddSingleton<IntradayOpportunityMapper>();
        services.AddSingleton<IChatClientFactory, OpenAiChatClientFactory>();
        services.AddTransient<PromptExecutor>();
        services.AddTransient<DailyBriefResearcher>();
        services.AddTransient<DailyPlanConverter>();
        services.AddTransient<IntradayOpportunityReviewer>();
        services.AddTransient<IDailyBriefingComposer, OpenAiDailyBriefingComposer>();
        return services;
    }

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, out var parsed)
            ? parsed
            : null;
}
