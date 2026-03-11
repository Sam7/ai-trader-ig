using Trading.Abstractions;

namespace Trading.Charting;

public interface IPriceChartRenderer
{
    byte[] RenderPng(
        PriceSeries series,
        PriceChartStyle style = PriceChartStyle.Candlestick,
        PriceGapMode gapMode = PriceGapMode.Compress,
        IReadOnlyList<int>? simpleMovingAverageWindows = null,
        int? bollingerPeriod = null,
        int width = 1200,
        int height = 800);
}
