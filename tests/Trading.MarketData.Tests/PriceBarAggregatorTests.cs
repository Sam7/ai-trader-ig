using FluentAssertions;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.MarketData.Tests;

public sealed class PriceBarAggregatorTests
{
    [Fact]
    public void Aggregate_ShouldBuildTenMinuteBarsFromCompleteFiveMinuteBuckets()
    {
        var bars = new[]
        {
            CreateBar("2026-06-29T00:05:00Z", bidOpen: 102m, bidHigh: 106m, bidLow: 101m, bidClose: 105m, volume: 7),
            CreateBar("2026-06-29T00:00:00Z", bidOpen: 100m, bidHigh: 104m, bidLow: 99m, bidClose: 103m, volume: 5),
        };

        var aggregated = PriceBarAggregator.Aggregate(
            new PriceSeries(new InstrumentId("CS.D.BITCOIN.CFD.IP"), PriceResolution.FiveMinutes, bars),
            PriceResolution.TenMinutes);

        aggregated.Resolution.Should().Be(PriceResolution.TenMinutes);
        aggregated.Bars.Should().ContainSingle();
        var bar = aggregated.Bars[0];
        bar.TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:00:00Z"));
        bar.BidOpen.Should().Be(100m);
        bar.BidHigh.Should().Be(106m);
        bar.BidLow.Should().Be(99m);
        bar.BidClose.Should().Be(105m);
        bar.Volume.Should().Be(12);
    }

    [Fact]
    public void Aggregate_ShouldExcludeIncompleteTargetBuckets()
    {
        var bars = new[]
        {
            CreateBar("2026-06-29T00:00:00Z"),
            CreateBar("2026-06-29T00:05:00Z"),
            CreateBar("2026-06-29T00:10:00Z"),
        };

        var aggregated = PriceBarAggregator.Aggregate(
            new PriceSeries(new InstrumentId("CS.D.BITCOIN.CFD.IP"), PriceResolution.FiveMinutes, bars),
            PriceResolution.TenMinutes);

        aggregated.Bars.Select(bar => bar.TimestampUtc)
            .Should().Equal(DateTimeOffset.Parse("2026-06-29T00:00:00Z"));
    }

    private static PriceBar CreateBar(
        string timestampUtc,
        decimal bidOpen = 100m,
        decimal bidHigh = 101m,
        decimal bidLow = 99m,
        decimal bidClose = 100.5m,
        long? volume = 1)
        => new(
            DateTimeOffset.Parse(timestampUtc),
            bidOpen,
            bidHigh,
            bidLow,
            bidClose,
            bidOpen + 1m,
            bidHigh + 1m,
            bidLow + 1m,
            bidClose + 1m,
            volume);
}
