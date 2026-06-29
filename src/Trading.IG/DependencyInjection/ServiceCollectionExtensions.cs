using Ig.Trading.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.IG.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIgTradingGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIgTradingSdk(configuration);
        services.AddSingleton<IOrderReferenceJournal, NullOrderReferenceJournal>();
        services.AddTransient<ITradingGateway, IgTradingGateway>();
        services.AddTransient<IMarketDataStreamClient, IgMarketDataStreamClient>();
        return services;
    }
}
