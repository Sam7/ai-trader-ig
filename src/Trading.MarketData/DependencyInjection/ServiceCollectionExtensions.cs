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
        services.AddOptions<MarketDataRecoveryOptions>()
            .Bind(configuration.GetSection($"{MarketDataOptions.SectionName}:Recovery"));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
            return new SqliteMarketDataStore(options.StorePath);
        });
        services.AddSingleton<IMarketDataStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataHealthStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataSnapshotImporter>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketSessionEvidenceStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataRecoveryStore>(sp => sp.GetRequiredService<SqliteMarketDataStore>());
        services.AddSingleton<IMarketDataClock, SystemMarketDataClock>();
        services.AddSingleton<GcsMarketDataSnapshotObjectStore>();
        services.AddSingleton<IMarketDataSnapshotObjectStore>(sp => sp.GetRequiredService<GcsMarketDataSnapshotObjectStore>());
        services.AddSingleton<IMarketDataObjectStore>(sp => sp.GetRequiredService<GcsMarketDataSnapshotObjectStore>());
        services.AddSingleton<MarketDataSnapshotValidator>();
        services.AddSingleton<FileMarketDataMirrorStateStore>();
        services.AddSingleton<MarketDataMirrorStatusService>();
        services.AddSingleton<MarketDataSnapshotPublisher>();
        services.AddSingleton<MarketDataSnapshotSynchronizer>();
        services.AddSingleton<MarketDataStreamPipelineMetrics>();
        services.AddSingleton<MarketDataService>();
        services.AddSingleton<MarketDataHistoricalBackfillService>();
        services.AddSingleton(sp => new MarketDataRecoveryCoordinator(
            sp.GetRequiredService<IMarketDataStore>(),
            sp.GetRequiredService<IMarketDataRecoveryStore>(),
            sp.GetRequiredService<Trading.Abstractions.ITradingGateway>(),
            sp.GetRequiredService<IMarketDataClock>(),
            sp.GetRequiredService<IOptions<MarketDataRecoveryOptions>>().Value,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MarketDataRecoveryCoordinator>>()));
        services.AddSingleton<MarketDataCollector>();
        services.AddSingleton<IMarketDataCollector>(sp => sp.GetRequiredService<MarketDataCollector>());
        return services;
    }
}
