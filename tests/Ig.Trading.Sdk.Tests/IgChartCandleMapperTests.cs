using FluentAssertions;
using Ig.Trading.Sdk.Streaming;

namespace Ig.Trading.Sdk.Tests;

public sealed class IgChartCandleMapperTests
{
    [Fact]
    public void Map_ShouldCreateFormingCandleWhenConsEndIsZero()
    {
        var update = IgChartCandleMapper.Map(
            "CS.D.BITCOIN.CFD.IP",
            "5MINUTE",
            CreateFields(consEnd: "0"));

        update.Epic.Should().Be("CS.D.BITCOIN.CFD.IP");
        update.Scale.Should().Be("5MINUTE");
        update.TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        update.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Map_ShouldCreateCompleteCandleWhenConsEndIsOne()
    {
        var update = IgChartCandleMapper.Map(
            "CS.D.BITCOIN.CFD.IP",
            "5MINUTE",
            CreateFields(consEnd: "1"));

        update.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Map_ShouldPreserveBidAndOfferOhlcValues()
    {
        var update = IgChartCandleMapper.Map(
            "CS.D.BITCOIN.CFD.IP",
            "5MINUTE",
            CreateFields());

        update.BidOpen.Should().Be(100m);
        update.BidHigh.Should().Be(105m);
        update.BidLow.Should().Be(98m);
        update.BidClose.Should().Be(103m);
        update.OfferOpen.Should().Be(101m);
        update.OfferHigh.Should().Be(106m);
        update.OfferLow.Should().Be(99m);
        update.OfferClose.Should().Be(104m);
        update.TickCount.Should().Be(42);
    }

    [Fact]
    public void Map_WhenTimestampIsMissing_ShouldThrowDiagnosticException()
    {
        var fields = CreateFields();
        fields.Remove("UTM");

        var action = () => IgChartCandleMapper.Map("CS.D.BITCOIN.CFD.IP", "5MINUTE", fields);

        action.Should().Throw<IgStreamingDataException>()
            .WithMessage("*UTM*");
    }

    private static Dictionary<string, string?> CreateFields(string consEnd = "1")
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["UTM"] = "1782691500000",
            ["BID_OPEN"] = "100",
            ["BID_HIGH"] = "105",
            ["BID_LOW"] = "98",
            ["BID_CLOSE"] = "103",
            ["OFR_OPEN"] = "101",
            ["OFR_HIGH"] = "106",
            ["OFR_LOW"] = "99",
            ["OFR_CLOSE"] = "104",
            ["CONS_END"] = consEnd,
            ["CONS_TICK_COUNT"] = "42",
        };
}
