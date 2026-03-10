using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Abstractions.Tests;

public class RequestValidationTests
{
    [Fact]
    public void UpdatePositionRequest_WithoutAnyAmendments_ShouldThrow()
    {
        var request = new UpdatePositionRequest("P1", null, null, null, null);

        var action = request.Validate;

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetPricesRequest_WithRangeAndMaxPoints_ShouldThrow()
    {
        var request = new GetPricesRequest(
            new InstrumentId("CC.D.VIX.UMA.IP"),
            PriceResolution.Minute,
            10,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow);

        var action = request.Validate;

        action.Should().Throw<ArgumentException>();
    }
}
