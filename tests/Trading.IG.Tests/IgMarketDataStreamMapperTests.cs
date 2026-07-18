using FluentAssertions;
using Ig.Trading.Sdk.Streaming;
using Trading.Abstractions;

namespace Trading.IG.Tests;

public sealed class IgMarketDataStreamMapperTests
{
    [Fact]
    public void ToStreamPriceBarUpdate_ShouldMapIgChartCandleToBrokerNeutralUpdate()
    {
        var candle = new IgChartCandleUpdate(
            "CS.D.BITCOIN.CFD.IP",
            "5MINUTE",
            DateTimeOffset.Parse("2026-06-29T00:05:00Z"),
            100m,
            105m,
            98m,
            103m,
            101m,
            106m,
            99m,
            104m,
            IsComplete: true,
            TickCount: 42);

        var update = IgMarketDataStreamMapper.ToStreamPriceBarUpdate(
            candle,
            DateTimeOffset.Parse("2026-06-29T00:05:01Z"));

        update.Instrument.Should().Be(new InstrumentId("CS.D.BITCOIN.CFD.IP"));
        update.Resolution.Should().Be(PriceResolution.FiveMinutes);
        update.Bar.TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:00Z"));
        update.Bar.BidOpen.Should().Be(100m);
        update.Bar.AskOpen.Should().Be(101m);
        update.Bar.Volume.Should().Be(42);
        update.IsFinal.Should().BeTrue();
        update.ObservedAtUtc.Should().Be(DateTimeOffset.Parse("2026-06-29T00:05:01Z"));
    }

    [Fact]
    public void ToStreamPriceBarUpdate_ShouldMapOneMinuteScale()
    {
        var candle = new IgChartCandleUpdate(
            "CS.D.BITCOIN.CFD.IP",
            "1MINUTE",
            DateTimeOffset.Parse("2026-06-29T00:01:00Z"),
            100m,
            105m,
            98m,
            103m,
            101m,
            106m,
            99m,
            104m,
            IsComplete: true,
            TickCount: 42);

        var update = IgMarketDataStreamMapper.ToStreamPriceBarUpdate(
            candle,
            DateTimeOffset.Parse("2026-06-29T00:01:01Z"));

        update.Resolution.Should().Be(PriceResolution.Minute);
    }
}
