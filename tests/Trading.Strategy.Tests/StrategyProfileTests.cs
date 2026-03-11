using FluentAssertions;
using Trading.Strategy.Configuration;

namespace Trading.Strategy.Tests;

public class StrategyProfileTests
{
    [Fact]
    public void Validate_WithInvalidRiskFraction_ShouldThrow()
    {
        var profile = StrategyProfile.Default with
        {
            RiskPolicy = new RiskPolicy(0m, 2m)
        };

        var action = profile.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_WithInvalidShortlistSize_ShouldThrow()
    {
        var profile = StrategyProfile.Default with
        {
            MonitoringPolicy = new MonitoringPolicy(0, 0.2m, 1.5m, 0.25m, 0.25m)
        };

        var action = profile.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_WithNegativeNoTradeWindow_ShouldThrow()
    {
        var profile = StrategyProfile.Default with
        {
            NoTradeWindowPolicy = new NoTradeWindowPolicy(TimeSpan.FromMinutes(-1), 0.5m, 0.5m)
        };

        var action = profile.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
