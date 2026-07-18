using FluentAssertions;
using Trading.Abstractions;

namespace Trading.IG.Tests;

public sealed class IgStreamingConversionsTests
{
    [Fact]
    public void ToIgChartScale_ShouldMapFiveMinutesToStreamingScale()
    {
        var scale = IgStreamingConversions.ToIgChartScale(PriceResolution.FiveMinutes);

        scale.Should().Be("5MINUTE");
    }

    [Fact]
    public void ToIgChartScale_ShouldMapOneMinuteToStreamingScale()
    {
        var scale = IgStreamingConversions.ToIgChartScale(PriceResolution.Minute);

        scale.Should().Be("1MINUTE");
    }

    [Fact]
    public void ToIgChartScale_WithUnsupportedResolution_ShouldThrow()
    {
        var action = () => IgStreamingConversions.ToIgChartScale(PriceResolution.TenMinutes);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*TenMinutes*");
    }
}
