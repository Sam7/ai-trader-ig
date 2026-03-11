using FluentAssertions;
using Trading.Abstractions;

namespace Trading.Charting.Tests;

public sealed class ScottPlotPriceChartRendererTests
{
    private readonly ScottPlotPriceChartRenderer _renderer = new();

    [Fact]
    public void RenderPng_WithCandlesticks_ShouldReturnPngBytes()
    {
        var series = CreateSeries();

        var imageBytes = _renderer.RenderPng(series);

        imageBytes.Should().NotBeEmpty();
        imageBytes.Take(8).Should().Equal(137, 80, 78, 71, 13, 10, 26, 10);
    }

    [Fact]
    public void RenderPng_WithOhlcAndIndicators_ShouldReturnPngBytes()
    {
        var series = CreateSeries();

        var imageBytes = _renderer.RenderPng(
            series,
            PriceChartStyle.Ohlc,
            PriceGapMode.Preserve,
            [3, 5],
            bollingerPeriod: 5,
            width: 800,
            height: 600);

        imageBytes.Should().NotBeEmpty();
        imageBytes.Take(8).Should().Equal(137, 80, 78, 71, 13, 10, 26, 10);
    }

    [Fact]
    public void RenderPng_WithEmptySeries_ShouldThrow()
    {
        var series = new PriceSeries(new InstrumentId("CC.D.VIX.UMA.IP"), PriceResolution.Minute, []);

        var action = () => _renderer.RenderPng(series);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*at least one bar*");
    }

    [Fact]
    public void RenderPng_WithIndicatorPeriodLongerThanSeries_ShouldThrow()
    {
        var series = CreateSeries(barCount: 3);

        var action = () => _renderer.RenderPng(series, simpleMovingAverageWindows: [5]);

        var exception = action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*cannot exceed the number of bars*")
            .Which;

        exception.ParamName.Should().Be("windows");
    }

    [Fact]
    public void ResolveTimeSpan_WithoutResolution_ShouldInferFromBars()
    {
        var bars = CreateBars(barCount: 3, resolution: null, spacing: TimeSpan.FromMinutes(15));

        var timeSpan = ScottPlotPriceChartRenderer.ResolveTimeSpan(null, bars);

        timeSpan.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void CreateOhlcs_ShouldUseMidPrices()
    {
        var bars = new[]
        {
            new PriceBar(
                DateTimeOffset.Parse("2026-03-11T00:00:00Z"),
                10m,
                12m,
                9m,
                11m,
                14m,
                16m,
                13m,
                15m,
                100),
        };

        var ohlcs = ScottPlotPriceChartRenderer.CreateOhlcs(bars, TimeSpan.FromMinutes(1));

        ohlcs.Should().ContainSingle();
        ohlcs[0].Open.Should().Be(12);
        ohlcs[0].High.Should().Be(14);
        ohlcs[0].Low.Should().Be(11);
        ohlcs[0].Close.Should().Be(13);
        ohlcs[0].TimeSpan.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ResolveIndicatorXPositions_WithPreserveMode_ShouldReturnDateCoordinates()
    {
        var ohlcs = ScottPlotPriceChartRenderer.CreateOhlcs(CreateBars(4, PriceResolution.Hour, TimeSpan.FromHours(1)), TimeSpan.FromHours(1));
        var dates = ohlcs.Skip(1).Select(ohlc => ohlc.DateTime.ToOADate()).ToArray();

        var xs = ScottPlotPriceChartRenderer.ResolveIndicatorXPositions(dates, ohlcs, PriceGapMode.Preserve);

        xs.Should().Equal(dates);
    }

    [Fact]
    public void ResolveIndicatorXPositions_WithCompressedMode_ShouldReturnSequentialIndices()
    {
        var ohlcs = ScottPlotPriceChartRenderer.CreateOhlcs(CreateBars(5, PriceResolution.Hour, TimeSpan.FromHours(1)), TimeSpan.FromHours(1));
        var sma = new ScottPlot.Finance.SimpleMovingAverage(ohlcs.ToList(), 3);
        var expected = ohlcs
            .Select((ohlc, index) => new { X = ohlc.DateTime.ToOADate(), Index = (double)index })
            .Where(item => sma.Dates.Contains(item.X))
            .Select(item => item.Index)
            .ToArray();

        var xs = ScottPlotPriceChartRenderer.ResolveIndicatorXPositions(sma.Dates, ohlcs, PriceGapMode.Compress);

        xs.Should().Equal(expected);
    }

    [Fact]
    public void CreateBollingerLegendItem_ShouldUseDottedSample()
    {
        var item = ScottPlotPriceChartRenderer.CreateBollingerLegendItem(20);

        item.LabelText.Should().Be("Bollinger 20");
        item.LinePattern.Should().Be(ScottPlot.LinePattern.Dotted);
        item.LineColor.Should().Be(ScottPlot.Colors.DarkBlue);
    }

    [Fact]
    public void ShouldSkipBollingerMean_WhenMatchingSmaExists_ShouldReturnTrue()
    {
        var shouldSkip = ScottPlotPriceChartRenderer.ShouldSkipBollingerMean(20, [5, 10, 20]);

        shouldSkip.Should().BeTrue();
    }

    [Fact]
    public void ShouldSkipBollingerMean_WhenMatchingSmaDoesNotExist_ShouldReturnFalse()
    {
        var shouldSkip = ScottPlotPriceChartRenderer.ShouldSkipBollingerMean(20, [5, 10]);

        shouldSkip.Should().BeFalse();
    }

    private static PriceSeries CreateSeries(int barCount = 10, PriceResolution? resolution = PriceResolution.Hour)
        => new(new InstrumentId("CC.D.VIX.UMA.IP"), resolution, CreateBars(barCount, resolution, TimeSpan.FromHours(1)));

    private static IReadOnlyList<PriceBar> CreateBars(int barCount, PriceResolution? resolution, TimeSpan spacing)
    {
        var start = DateTimeOffset.Parse("2026-03-11T00:00:00Z");
        var bars = new List<PriceBar>(barCount);

        for (var i = 0; i < barCount; i++)
        {
            var open = 20m + i;
            bars.Add(new PriceBar(
                start.AddTicks(spacing.Ticks * i),
                open,
                open + 2m,
                open - 1m,
                open + 1m,
                open + 0.2m,
                open + 2.2m,
                open - 0.8m,
                open + 1.2m,
                100 + i));
        }

        return bars;
    }
}
