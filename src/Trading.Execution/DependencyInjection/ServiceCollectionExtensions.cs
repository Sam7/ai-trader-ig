using Microsoft.Extensions.DependencyInjection;

namespace Trading.Execution.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingExecution(
        this IServiceCollection services,
        string storePath)
    {
        services.AddSingleton<IExecutionClock, SystemExecutionClock>();
        services.AddSingleton<IExecutionDealReferenceFactory, ExecutionDealReferenceFactory>();
        services.AddSingleton<IExecutionBoundaryStore>(_ => new SqliteExecutionBoundaryStore(storePath));
        services.AddSingleton<ExecutionBoundaryService>();
        services.AddSingleton<ExecutionSubmissionService>();
        services.AddSingleton<IExecutionSubmissionService>(sp => sp.GetRequiredService<ExecutionSubmissionService>());
        return services;
    }
}
