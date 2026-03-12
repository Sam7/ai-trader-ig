using FluentAssertions;
using Trading.Strategy.Rules;

namespace Trading.Strategy.Tests;

public class StrategyRulesTests
{
    [Fact]
    public void Validate_should_fail_when_risk_fraction_is_invalid()
    {
        var rules = StrategyRules.Default with
        {
            Risk = new RiskRules(0m, 2m)
        };

        var action = rules.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_should_fail_when_watch_list_size_is_invalid()
    {
        var rules = StrategyRules.Default with
        {
            MarketWatch = new MarketWatchRules(0, 1.5m, 0.25m, 0.25m)
        };

        var action = rules.Validate;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Default_should_use_three_markets_for_daily_briefing()
    {
        StrategyRules.Default.MarketWatch.ShortlistSize.Should().Be(3);
    }
}
