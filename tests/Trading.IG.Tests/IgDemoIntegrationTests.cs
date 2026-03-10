using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.Abstractions;
using Trading.IG.DependencyInjection;

namespace Trading.IG.Tests;

public class IgDemoIntegrationTests
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public async Task AuthenticateAsync_WithValidDemoCredentials_ShouldReturnSession()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIgTradingGateway(configuration);

        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<ITradingGateway>();

        var session = await gateway.AuthenticateAsync();

        session.BrokerName.Should().Be("IG");
    }
}
