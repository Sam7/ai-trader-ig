using FluentAssertions;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Strategy.Shared;

public sealed class ExecutionOptionsTests
{
    [Fact]
    public void CreateShadowDecisionPolicy_WithDefaults_ShouldFailClosed()
    {
        var options = new ExecutionOptions();

        var policy = options.CreateShadowDecisionPolicy("Australia/Melbourne");

        policy.Mode.Should().Be(TradingExecutionMode.Disabled);
        policy.SupportedInstruments.Should().BeEmpty();
        policy.SupportedEntryMethods.Should().ContainSingle().Which.Should().Be(TradeEntryMethod.Market);
        policy.QuantityPolicy.Should().Be("BrokerMinimum");
        options.StorePath.Should().Be(Path.Combine("Logs", "Execution", "execution-boundary.sqlite"));
    }

    [Fact]
    public void CreateShadowDecisionPolicy_WithShadowOptions_ShouldMapAllowlistAndRules()
    {
        var options = new ExecutionOptions
        {
            Mode = TradingExecutionMode.Shadow,
            Shadow = new ShadowExecutionOptions
            {
                SupportedInstruments = [" CC.D.TEST.IP "],
                SupportedEntryMethods = [TradeEntryMethod.Market],
                MinimumOpportunityScore = 75,
                MinimumRewardRiskRatio = 2.5m,
                MaxSpreadRiskRatio = 0.10m,
                MaxPriceMovementRiskRatio = 0.15m,
                FreshQuoteMaxAge = TimeSpan.FromMinutes(10),
                BlockBeforeHighImpactEvent = TimeSpan.FromMinutes(45),
                QuantityPolicy = "BrokerMinimum",
            },
        };

        var policy = options.CreateShadowDecisionPolicy("Australia/Melbourne");

        policy.Mode.Should().Be(TradingExecutionMode.Shadow);
        policy.SupportedInstruments.Should().ContainSingle().Which.Should().Be(new InstrumentId("CC.D.TEST.IP"));
        policy.MinimumOpportunityScore.Should().Be(75);
        policy.MinimumRewardRiskRatio.Should().Be(2.5m);
        policy.FreshQuoteMaxAge.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void CreateShadowDecisionPolicy_WithDemoMode_ShouldAllowDemoCanarySelection()
    {
        var options = new ExecutionOptions
        {
            Mode = TradingExecutionMode.Demo,
            Shadow = new ShadowExecutionOptions
            {
                SupportedInstruments = ["CC.D.TEST.IP"],
                SupportedEntryMethods = [TradeEntryMethod.Market],
            },
        };

        var policy = options.CreateShadowDecisionPolicy("Australia/Melbourne");

        policy.Mode.Should().Be(TradingExecutionMode.Demo);
    }

    [Fact]
    public void CreateShadowDecisionPolicy_WithLiveMode_ShouldFailClosed()
    {
        var options = new ExecutionOptions { Mode = TradingExecutionMode.Live };

        var action = () => options.CreateShadowDecisionPolicy("Australia/Melbourne");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Execution mode must be Disabled, Shadow, or Demo*");
    }
}
