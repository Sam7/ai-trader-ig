using FluentAssertions;
using Ig.Trading.Sdk.Streaming;

namespace Trading.IG.Tests;

public sealed class IgChartCandleUpdateAccumulatorTests
{
    [Fact]
    public void Apply_WhenUpdateIsComplete_ShouldReturnCandle()
    {
        var accumulator = new IgChartCandleUpdateAccumulator();

        var candle = accumulator.Apply(
            "CS.D.BITCOIN.CFD.IP",
            "5MINUTE",
            CreateFields());

        candle.Should().NotBeNull();
        candle!.Epic.Should().Be("CS.D.BITCOIN.CFD.IP");
        candle.TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        candle.BidOpen.Should().Be(100m);
    }

    [Theory]
    [InlineData("BID_OPEN")]
    [InlineData("OFR_OPEN")]
    public void Apply_WhenUpdateIsIncomplete_ShouldReturnNull(string missingField)
    {
        var accumulator = new IgChartCandleUpdateAccumulator();
        var fields = CreateFields();
        fields.Remove(missingField);

        var candle = accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", fields);

        candle.Should().BeNull();
    }

    [Fact]
    public void Apply_WhenPartialUpdatesCompleteSameBucket_ShouldReturnCandle()
    {
        var accumulator = new IgChartCandleUpdateAccumulator();
        var first = CreateFields();
        first.Remove("BID_OPEN");
        first.Remove("OFR_OPEN");

        var firstResult = accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", first);
        var secondResult = accumulator.Apply(
            "CC.D.CL.UMA.IP",
            "5MINUTE",
            new Dictionary<string, string?>
            {
                ["UTM"] = "1782691500000",
                ["BID_OPEN"] = "100",
                ["OFR_OPEN"] = "101",
            });

        firstResult.Should().BeNull();
        secondResult.Should().NotBeNull();
        secondResult!.BidOpen.Should().Be(100m);
        secondResult.OfferOpen.Should().Be(101m);
    }

    [Fact]
    public void Apply_WhenTimestampChanges_ShouldNotReusePreviousBucketFields()
    {
        var accumulator = new IgChartCandleUpdateAccumulator();
        accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", CreateFields())
            .Should().NotBeNull();

        var nextBucket = CreateFields(utm: "1782691800000");
        nextBucket.Remove("BID_OPEN");

        var candle = accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", nextBucket);

        candle.Should().BeNull();
    }

    [Fact]
    public void Apply_WhenFirstUpdateHasNoTimestamp_ShouldNotReuseFieldsForLaterBucket()
    {
        var accumulator = new IgChartCandleUpdateAccumulator();
        var first = CreateFields();
        first.Remove("UTM");

        var firstResult = accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", first);
        var secondResult = accumulator.Apply(
            "CC.D.CL.UMA.IP",
            "5MINUTE",
            new Dictionary<string, string?>
            {
                ["UTM"] = "1782691500000",
                ["BID_OPEN"] = "100",
            });

        firstResult.Should().BeNull();
        secondResult.Should().BeNull();
    }

    [Fact]
    public void Apply_WhenCompleteUpdateHasInvalidValue_ShouldThrowDiagnosticException()
    {
        var accumulator = new IgChartCandleUpdateAccumulator();
        var fields = CreateFields();
        fields["BID_OPEN"] = "not-a-price";

        var action = () => accumulator.Apply("CC.D.CL.UMA.IP", "5MINUTE", fields);

        action.Should().Throw<IgStreamingDataException>()
            .WithMessage("*BID_OPEN*not-a-price*");
    }

    private static Dictionary<string, string?> CreateFields(string utm = "1782691500000")
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["UTM"] = utm,
            ["BID_OPEN"] = "100",
            ["BID_HIGH"] = "105",
            ["BID_LOW"] = "98",
            ["BID_CLOSE"] = "103",
            ["OFR_OPEN"] = "101",
            ["OFR_HIGH"] = "106",
            ["OFR_LOW"] = "99",
            ["OFR_CLOSE"] = "104",
            ["CONS_END"] = "1",
            ["CONS_TICK_COUNT"] = "42",
        };
}
