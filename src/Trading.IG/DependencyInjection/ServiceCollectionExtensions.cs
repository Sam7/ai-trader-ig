using Ig.Trading.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.Abstractions;

namespace Trading.IG.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIgTradingGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIgTradingSdk(configuration);
        services.AddSingleton<IOrderReferenceJournal, NullOrderReferenceJournal>();
        services.AddTransient<ITradingGateway, IgTradingGateway>();
        return services;
    }
}
