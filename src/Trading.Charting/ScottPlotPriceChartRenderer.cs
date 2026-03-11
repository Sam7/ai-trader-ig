using ScottPlot;
using ScottPlot.Plottables;
using Trading.Abstractions;

namespace Trading.Charting;

public sealed class ScottPlotPriceChartRenderer : IPriceChartRenderer
{
    public byte[] RenderPng(
        PriceSeries series,
        PriceChartStyle style = PriceChartStyle.Candlestick,
        PriceGapMode gapMode = PriceGapMode.Compress,
        IReadOnlyList<int>? simpleMovingAverageWindows = null,
        int? bollingerPeriod = null,
        int width = 1200,
        int height = 800)
    {
        ArgumentNullException.ThrowIfNull(series);
        ValidateDimensions(width, height);

        var orderedBars = series.Bars
            .OrderBy(bar => bar.TimestampUtc)
            .ToArray();

        if (orderedBars.Length == 0)
        {
            throw new ArgumentException("Price series must contain at least one bar.", nameof(series));
        }

        var smaWindows = NormalizeSimpleMovingAverageWindows(simpleMovingAverageWindows, orderedBars.Length);
        var normalizedBollingerPeriod = NormalizeBollingerPeriod(bollingerPeriod, orderedBars.Length);
        var timeSpan = ResolveTimeSpan(series.Resolution, orderedBars);
        var ohlcs = CreateOhlcs(orderedBars, timeSpan);

        Plot plot = new();
        plot.Title(CreateTitle(series));
        plot.XLabel("Time (UTC)");
        plot.YLabel("Price");

        AddPricePlot(plot, ohlcs, style, gapMode);
        AddSimpleMovingAverages(plot, ohlcs, smaWindows, gapMode);
        AddBollingerBands(plot, ohlcs, normalizedBollingerPeriod, smaWindows, gapMode);

        if (smaWindows.Count > 0 || normalizedBollingerPeriod is not null)
        {
            plot.ShowLegend();
        }

        return plot.GetImageBytes(width, height, ImageFormat.Png);
    }

    internal static ScottPlot.OHLC[] CreateOhlcs(IReadOnlyList<PriceBar> bars, TimeSpan timeSpan)
    {
        ArgumentNullException.ThrowIfNull(bars);

        return bars
            .Select(bar => new ScottPlot.OHLC(
                ToDouble(Mid(bar.BidOpen, bar.AskOpen)),
                ToDouble(Mid(bar.BidHigh, bar.AskHigh)),
                ToDouble(Mid(bar.BidLow, bar.AskLow)),
                ToDouble(Mid(bar.BidClose, bar.AskClose)),
                bar.TimestampUtc.UtcDateTime,
                timeSpan))
            .ToArray();
    }

    internal static TimeSpan ResolveTimeSpan(PriceResolution? resolution, IReadOnlyList<PriceBar> orderedBars)
    {
        if (resolution is not null)
        {
            return resolution.Value switch
            {
                PriceResolution.Second => TimeSpan.FromSeconds(1),
                PriceResolution.Minute => TimeSpan.FromMinutes(1),
                PriceResolution.TwoMinutes => TimeSpan.FromMinutes(2),
                PriceResolution.ThreeMinutes => TimeSpan.FromMinutes(3),
                PriceResolution.FiveMinutes => TimeSpan.FromMinutes(5),
                PriceResolution.TenMinutes => TimeSpan.FromMinutes(10),
                PriceResolution.FifteenMinutes => TimeSpan.FromMinutes(15),
                PriceResolution.ThirtyMinutes => TimeSpan.FromMinutes(30),
                PriceResolution.Hour => TimeSpan.FromHours(1),
                PriceResolution.TwoHours => TimeSpan.FromHours(2),
                PriceResolution.ThreeHours => TimeSpan.FromHours(3),
                PriceResolution.FourHours => TimeSpan.FromHours(4),
                PriceResolution.Day => TimeSpan.FromDays(1),
                PriceResolution.Week => TimeSpan.FromDays(7),
                PriceResolution.Month => TimeSpan.FromDays(30),
                _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported price resolution."),
            };
        }

        if (orderedBars.Count < 2)
        {
            throw new ArgumentException("A price resolution or at least two bars are required to determine candle width.", nameof(orderedBars));
        }

        var inferred = orderedBars
            .Zip(orderedBars.Skip(1), (previous, current) => current.TimestampUtc - previous.TimestampUtc)
            .Where(span => span > TimeSpan.Zero)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Min();

        if (inferred <= TimeSpan.Zero)
        {
            throw new ArgumentException("Unable to infer candle width from the supplied bars.", nameof(orderedBars));
        }

        return inferred;
    }

    private static void AddPricePlot(Plot plot, ScottPlot.OHLC[] ohlcs, PriceChartStyle style, PriceGapMode gapMode)
    {
        switch (style)
        {
            case PriceChartStyle.Candlestick:
            {
                var candlesticks = plot.Add.Candlestick(ohlcs);
                ConfigureGapMode(plot, candlesticks, ohlcs, gapMode);
                break;
            }
            case PriceChartStyle.Ohlc:
            {
                var ohlcPlot = plot.Add.OHLC(ohlcs.ToList());
                ConfigureGapMode(plot, ohlcPlot, ohlcs, gapMode);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, "Unsupported chart style.");
        }
    }

    private static void ConfigureGapMode(Plot plot, CandlestickPlot candlesticks, ScottPlot.OHLC[] ohlcs, PriceGapMode gapMode)
    {
        candlesticks.Sequential = gapMode == PriceGapMode.Compress;
        ConfigureBottomAxis(plot, ohlcs, gapMode);
    }

    private static void ConfigureGapMode(Plot plot, OhlcPlot ohlcPlot, ScottPlot.OHLC[] ohlcs, PriceGapMode gapMode)
    {
        ohlcPlot.Sequential = gapMode == PriceGapMode.Compress;
        ConfigureBottomAxis(plot, ohlcs, gapMode);
    }

    private static void ConfigureBottomAxis(Plot plot, ScottPlot.OHLC[] ohlcs, PriceGapMode gapMode)
    {
        if (gapMode == PriceGapMode.Preserve)
        {
            plot.Axes.DateTimeTicksBottom();
            return;
        }

        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.EmptyTickGenerator();
        plot.Axes.Bottom.MinimumSize = 30;
        plot.Add.Plottable(new FinancialTimeAxis(ohlcs.Select(x => x.DateTime).ToArray()));
    }

    internal static double[] ResolveIndicatorXPositions(
        IReadOnlyList<double> dates,
        ScottPlot.OHLC[] ohlcs,
        PriceGapMode gapMode)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(ohlcs);

        if (gapMode == PriceGapMode.Preserve)
        {
            return dates.ToArray();
        }

        var indicesByDate = ohlcs
            .Select((ohlc, index) => new { X = ohlc.DateTime.ToOADate(), Index = (double)index })
            .ToDictionary(item => item.X, item => item.Index);

        return dates
            .Select(date => indicesByDate.TryGetValue(date, out var index)
                ? index
                : throw new ArgumentException(
                    $"Indicator X coordinate '{date}' does not match a plotted price bar.",
                    nameof(dates)))
            .ToArray();
    }

    private static void AddSimpleMovingAverages(
        Plot plot,
        ScottPlot.OHLC[] ohlcs,
        IReadOnlyList<int> windows,
        PriceGapMode gapMode)
    {
        var priceList = ohlcs.ToList();
        ScottPlot.Color[] colors =
        [
            Colors.Navy,
            Colors.Teal,
            Colors.Orange,
            Colors.Purple,
        ];

        foreach (var (window, index) in windows.Select((window, index) => (window, index)))
        {
            ScottPlot.Finance.SimpleMovingAverage sma = new(priceList, window);
            var xs = ResolveIndicatorXPositions(sma.Dates, ohlcs, gapMode);
            var scatter = plot.Add.Scatter(xs, sma.Means, colors[index % colors.Length]);
            scatter.LegendText = $"SMA {window}";
            scatter.MarkerSize = 0;
            scatter.LineWidth = 2;
        }
    }

    private static void AddBollingerBands(
        Plot plot,
        ScottPlot.OHLC[] ohlcs,
        int? bollingerPeriod,
        IReadOnlyList<int> smaWindows,
        PriceGapMode gapMode)
    {
        if (bollingerPeriod is null)
        {
            return;
        }

        ScottPlot.Finance.BollingerBands bands = new(ohlcs.ToList(), bollingerPeriod.Value);
        var xs = ResolveIndicatorXPositions(bands.Dates, ohlcs, gapMode);

        if (!ShouldSkipBollingerMean(bollingerPeriod.Value, smaWindows))
        {
            var mean = plot.Add.Scatter(xs, bands.Means, Colors.DarkBlue);
            mean.MarkerSize = 0;
            mean.LineWidth = 2;
        }

        var upper = plot.Add.Scatter(xs, bands.UpperValues, Colors.DarkBlue);
        upper.MarkerSize = 0;
        upper.LinePattern = LinePattern.Dotted;

        var lower = plot.Add.Scatter(xs, bands.LowerValues, Colors.DarkBlue);
        lower.MarkerSize = 0;
        lower.LinePattern = LinePattern.Dotted;

        plot.Legend.ManualItems.Add(CreateBollingerLegendItem(bollingerPeriod.Value));
    }

    internal static LegendItem CreateBollingerLegendItem(int bollingerPeriod)
        => new()
        {
            LabelText = $"Bollinger {bollingerPeriod}",
            LineColor = Colors.DarkBlue,
            LinePattern = LinePattern.Dotted,
            LineWidth = 2,
        };

    internal static bool ShouldSkipBollingerMean(int bollingerPeriod, IReadOnlyList<int> smaWindows)
        => smaWindows.Contains(bollingerPeriod);

    private static IReadOnlyList<int> NormalizeSimpleMovingAverageWindows(IReadOnlyList<int>? windows, int barCount)
    {
        if (windows is null || windows.Count == 0)
        {
            return [];
        }

        var normalized = windows
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        foreach (var window in normalized)
        {
            ValidateIndicatorPeriod(window, barCount, nameof(windows), "Simple moving average");
        }

        return normalized;
    }

    private static int? NormalizeBollingerPeriod(int? bollingerPeriod, int barCount)
    {
        if (bollingerPeriod is null)
        {
            return null;
        }

        ValidateIndicatorPeriod(bollingerPeriod.Value, barCount, nameof(bollingerPeriod), "Bollinger");
        return bollingerPeriod.Value;
    }

    private static void ValidateIndicatorPeriod(int value, int barCount, string paramName, string indicatorName)
    {
        if (value < 2)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{indicatorName} period must be at least 2.");
        }

        if (value > barCount)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{indicatorName} period cannot exceed the number of bars.");
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Chart width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Chart height must be greater than zero.");
        }
    }

    private static string CreateTitle(PriceSeries series)
        => series.Resolution is null
            ? $"{series.Instrument.Value} Price Chart"
            : $"{series.Instrument.Value} {series.Resolution} Price Chart";

    private static decimal Mid(decimal bid, decimal ask) => (bid + ask) / 2m;

    private static double ToDouble(decimal value) => decimal.ToDouble(value);
}
