using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trading.Automation.Configuration;
using Trading.Automation.Health;
using Trading.MarketData;

namespace Trading.Automation.Diagnostics;

/// <summary>Registers the bounded worker diagnostics module without coupling it to a broker.</summary>
public static class WorkerDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WorkerDiagnosticsOptions>()
            .Bind(configuration.GetSection(WorkerDiagnosticsOptions.SectionName));
        services.TryAddSingleton<WorkerOperationMetrics>();
        services.TryAddSingleton<MarketDataStreamPipelineMetrics>();
        services.TryAddSingleton<MarketDataRuntimeActivityMetrics>();
        services.TryAddSingleton<IWorkerProcessMemoryProbe, CurrentProcessMemoryProbe>();
        services.TryAddSingleton<IWorkerCgroupMemoryReader, LinuxCgroupMemoryReader>();
        services.TryAddSingleton<IWorkerDiagnosticsSampler, WorkerDiagnosticsSampler>();
        services.TryAddSingleton<IWorkerProcessTerminator, EnvironmentWorkerProcessTerminator>();
        services.TryAddSingleton(sp => new RollingWorkerTraceStore(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerDiagnosticsOptions>>().Value,
            CreateBootId()));
        services.TryAddSingleton(sp => new WorkerDiagnosticsCoordinator(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerDiagnosticsOptions>>().Value,
            sp.GetRequiredService<IWorkerDiagnosticsSampler>(),
            sp.GetRequiredService<RollingWorkerTraceStore>(),
            sp.GetRequiredService<IWorkerProcessTerminator>()));
        services.TryAddSingleton<IWorkerDiagnosticsArtifactUploader, NoOpWorkerDiagnosticsArtifactUploader>();
        services.AddHostedService<WorkerDiagnosticsHostedService>();
        return services;
    }

    public static IServiceCollection UseGcsWorkerDiagnosticsArtifactUploader(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IWorkerDiagnosticsArtifactUploader, GcsWorkerDiagnosticsArtifactUploader>());
        return services;
    }

    private static string CreateBootId()
        => $"{Environment.ProcessId}-{Guid.NewGuid():N}";
}
