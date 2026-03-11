using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Strategy.Tests;

public class TradeProposalTests
{
    [Fact]
    public void Validate_WhenBuyStopIsAboveEntry_ShouldThrow()
    {
        var proposal = new TradeProposal(
            new InstrumentId("CS.D.EURUSD.CFD.IP"),
            TradeDirection.Buy,
            1.10m,
            1.11m,
            1.12m,
            0.8m,
            "breakout",
            "Trend continuation",
            "Loses support",
            DateTimeOffset.UtcNow);

        var action = proposal.Validate;

        action.Should().Throw<ArgumentException>();
    }
}
