using FluentAssertions;
using Trading.Abstractions;
using Trading.Automation.Execution;

public sealed class IntradayChartAttachmentLabelTests
{
    [Fact]
    public void Format_ShouldUseConfiguredHourLookbackAndResolution()
    {
        var label = IntradayChartAttachmentLabel.Format("WTI Crude Oil", 6, PriceResolution.TenMinutes);

        label.Should().Be("WTI Crude Oil 6-hour 10-minute chart");
    }

    [Fact]
    public void Format_ShouldUseDayLookbackWhenHoursAreWholeDays()
    {
        var label = IntradayChartAttachmentLabel.Format("Gold", 96, PriceResolution.TenMinutes);

        label.Should().Be("Gold 4-day 10-minute chart");
    }
}
