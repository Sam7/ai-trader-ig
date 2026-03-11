using Microsoft.Extensions.DependencyInjection;

namespace Trading.Charting.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingCharting(this IServiceCollection services)
    {
        services.AddSingleton<IPriceChartRenderer, ScottPlotPriceChartRenderer>();
        return services;
    }
}
