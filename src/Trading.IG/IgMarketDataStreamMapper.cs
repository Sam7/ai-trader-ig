using Ig.Trading.Sdk.Streaming;
using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.IG;

public static class IgMarketDataStreamMapper
{
    public static StreamPriceBarUpdate ToStreamPriceBarUpdate(
        IgChartCandleUpdate candle,
        DateTimeOffset observedAtUtc)
        => new(
            new InstrumentId(candle.Epic),
            FromIgChartScale(candle.Scale),
            new PriceBar(
                candle.TimestampUtc,
                candle.BidOpen,
                candle.BidHigh,
                candle.BidLow,
                candle.BidClose,
                candle.OfferOpen,
                candle.OfferHigh,
                candle.OfferLow,
                candle.OfferClose,
                candle.TickCount),
            candle.IsComplete,
            observedAtUtc);

    private static PriceResolution FromIgChartScale(string scale)
        => scale switch
        {
            "5MINUTE" => PriceResolution.FiveMinutes,
            _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unsupported IG chart streaming scale."),
        };
}
