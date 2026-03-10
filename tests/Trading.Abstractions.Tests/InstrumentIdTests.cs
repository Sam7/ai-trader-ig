using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Abstractions.Tests;

public class InstrumentIdTests
{
    [Fact]
    public void Constructor_WithEmptyValue_ShouldThrow()
    {
        var action = () => new InstrumentId(" ");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithValue_ShouldTrimAndStore()
    {
        var instrument = new InstrumentId(" IX.D.SPTRD.DAILY.IP ");

        instrument.Value.Should().Be("IX.D.SPTRD.DAILY.IP");
    }
}
