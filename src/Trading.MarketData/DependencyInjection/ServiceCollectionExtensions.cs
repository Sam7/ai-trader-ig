using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Trading.MarketData.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketDataOptions>()
            .Bind(configuration.GetSection(MarketDataOptions.SectionName));
        services.AddOptions<MarketDataCollectorOptions>()
            .Bind(configuration.GetSection($"{MarketDataOptions.SectionName}:Collector"));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
            return new SqliteMarketDataStore(options.StorePath);
        });
        services.AddSingleton<IMarketDataStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataHealthStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataClock, SystemMarketDataClock>();
        services.AddSingleton<MarketDataService>();
        services.AddSingleton<MarketDataStreamIngestor>();
        services.AddSingleton<MarketDataCollector>();
        services.AddSingleton<IMarketDataCollector>(sp => sp.GetRequiredService<MarketDataCollector>());
        return services;
    }
}
