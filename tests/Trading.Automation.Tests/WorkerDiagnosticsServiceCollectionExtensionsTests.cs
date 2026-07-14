using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trading.Automation.Diagnostics;

public sealed class WorkerDiagnosticsServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddWorkerDiagnostics_should_resolve_its_hosted_service_from_standard_options_registration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkerDiagnostics:Enabled"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkerDiagnostics(configuration);

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        hostedServices.Should().ContainSingle(service => service.GetType().Name == "WorkerDiagnosticsHostedService");
    }
}
