using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Configuration;
using Ig.Trading.Sdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Ig.Trading.Sdk.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIgTradingSdk(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<IgClientOptions>()
            .Bind(configuration.GetSection(IgClientOptions.SectionName));

        services.AddSingleton<IIgSessionStore, InMemoryIgSessionStore>();
        services.AddTransient<IgAuthenticationHeaderHandler>();

        static RefitSettings CreateRefitSettings() => new();

        services
            .AddRefitClient<IIgSessionApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services
            .AddRefitClient<IIgMarketsApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services
            .AddRefitClient<IIgPositionsApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services
            .AddRefitClient<IIgOrderStateApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services
            .AddRefitClient<IIgWorkingOrdersApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services
            .AddRefitClient<IIgAccountsApi>(CreateRefitSettings())
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .AddHttpMessageHandler<IgAuthenticationHeaderHandler>();

        services.AddTransient<IIgTradingApi, IgTradingApi>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IgClientOptions>>().Value;
        options.Validate();

        client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    }
}
