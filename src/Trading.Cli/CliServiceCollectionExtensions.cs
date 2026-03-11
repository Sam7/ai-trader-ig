using Microsoft.Extensions.DependencyInjection;

public static class CliServiceCollectionExtensions
{
    public static IServiceCollection AddTradingCli(this IServiceCollection services)
    {
        services.AddSingleton<TradingCliRenderer>();

        services.AddTransient<AuthenticateCommand>();
        services.AddTransient<BuyTradeCommand>();
        services.AddTransient<SellTradeCommand>();
        services.AddTransient<CreateWorkingOrderCommand>();
        services.AddTransient<UpdateWorkingOrderCommand>();
        services.AddTransient<CancelWorkingOrderCommand>();
        services.AddTransient<ListWorkingOrdersCommand>();
        services.AddTransient<ClosePositionCommand>();
        services.AddTransient<UpdatePositionCommand>();
        services.AddTransient<ListPositionsCommand>();
        services.AddTransient<SearchMarketsCommand>();
        services.AddTransient<BrowseMarketsCommand>();
        services.AddTransient<ShowPricesCommand>();
        services.AddTransient<RenderMarketChartCommand>();
        services.AddTransient<ListOrdersCommand>();
        services.AddTransient<ShowOrderStatusCommand>();

        return services;
    }
}
