using FluentAssertions;
using Trading.Abstractions;
using Trading.Charting;
using Trading.Charting.MemoryLab;

namespace Trading.Charting.MemoryLab.Tests;

public sealed class ChartMemoryScenarioTests
{
    [Fact]
    public void ProductionProfile_ShouldIncludeCurrentIntradayProfiles()
    {
        var scenarios = ChartMemoryScenarioCatalog.Create("production");

        scenarios.Should().Contain(scenario => scenario.Name == "production-96h-5m" && scenario.BarCount == 1_152);
        scenarios.Should().Contain(scenario => scenario.Name == "production-96h-10m" && scenario.BarCount == 576);
    }

    [Fact]
    public void ResolutionProfile_ShouldSeparateResolutionFromDimensions()
    {
        var scenarios = ChartMemoryScenarioCatalog.Create("resolution");

        scenarios.Should().Contain(scenario => scenario.Resolution == PriceResolution.Second);
        scenarios.Select(scenario => (scenario.Width, scenario.Height)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void ScenarioValidation_ShouldRejectIndicatorLargerThanBarCount()
    {
        var scenario = new ChartMemoryScenario(
            "invalid",
            10,
            PriceResolution.Minute,
            800,
            600,
            SmaWindows: [20]);

        var action = () => scenario.Validate();

        action.Should().Throw<InvalidOperationException>().WithMessage("*SMA window*");
    }

    [Fact]
    public void FixtureFactory_ShouldProduceDeterministicBarsAndResolution()
    {
        var scenario = new ChartMemoryScenario("fixture", 12, PriceResolution.FiveMinutes, 800, 600);

        var first = PriceSeriesFixtureFactory.Create(scenario);
        var second = PriceSeriesFixtureFactory.Create(scenario);

        first.Resolution.Should().Be(PriceResolution.FiveMinutes);
        first.Bars.Should().Equal(second.Bars);
        first.Bars.Should().HaveCount(12);
    }
}
