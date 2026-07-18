using Trading.Abstractions;
using Trading.Charting;

namespace Trading.Charting.MemoryLab;

public enum ChartMemoryRetentionMode
{
    Discard,
    Retain,
    WriteAndDiscard,
}

public sealed record ChartMemoryScenario(
    string Name,
    int BarCount,
    PriceResolution Resolution,
    int Width,
    int Height,
    PriceChartStyle Style = PriceChartStyle.Ohlc,
    PriceGapMode GapMode = PriceGapMode.Compress,
    IReadOnlyList<int>? SmaWindows = null,
    int? BollingerPeriod = null,
    ChartMemoryRetentionMode Retention = ChartMemoryRetentionMode.Discard,
    int Concurrency = 1)
{
    public IReadOnlyList<int> EffectiveSmaWindows => SmaWindows ?? [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Chart memory scenario name is required.");
        }

        if (BarCount < 1)
        {
            throw new InvalidOperationException("Chart memory scenario bar count must be positive.");
        }

        if (Width < 1 || Height < 1)
        {
            throw new InvalidOperationException("Chart memory scenario dimensions must be positive.");
        }

        if (Concurrency < 1)
        {
            throw new InvalidOperationException("Chart memory scenario concurrency must be positive.");
        }

        foreach (var window in EffectiveSmaWindows)
        {
            if (window < 2 || window > BarCount)
            {
                throw new InvalidOperationException($"SMA window {window} is invalid for {BarCount} bars.");
            }
        }

        if (BollingerPeriod is { } period && (period < 2 || period > BarCount))
        {
            throw new InvalidOperationException("Bollinger period is invalid for the scenario bar count.");
        }
    }

    public string FileSafeName()
        => string.Concat(Name.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
}

public static class ChartMemoryScenarioCatalog
{
    public static IReadOnlyList<ChartMemoryScenario> Create(string profile)
    {
        return profile.Trim().ToLowerInvariant() switch
        {
            "production" => Production(),
            "resolution" => Resolution(),
            "dimensions" => Dimensions(),
            "features" => Features(),
            "retention" => Retention(),
            "all" => Production().Concat(Resolution()).Concat(Dimensions()).Concat(Features()).Concat(Retention()).ToArray(),
            _ => throw new ArgumentException($"Unknown chart memory profile '{profile}'. Use production, resolution, dimensions, features, retention, or all.", nameof(profile)),
        };
    }

    private static IReadOnlyList<ChartMemoryScenario> Production()
        =>
        [
            new("production-96h-5m", 1_152, PriceResolution.FiveMinutes, 1_200, 800),
            new("production-96h-10m", 576, PriceResolution.TenMinutes, 1_200, 800),
            new("production-96h-15m", 384, PriceResolution.FifteenMinutes, 1_200, 800),
            new("production-96h-1h", 96, PriceResolution.Hour, 1_200, 800),
        ];

    private static IReadOnlyList<ChartMemoryScenario> Resolution()
        =>
        [
            new("resolution-1m-1h", 60, PriceResolution.Minute, 1_200, 800),
            new("resolution-5m-1h", 12, PriceResolution.FiveMinutes, 1_200, 800),
            new("resolution-10m-1h", 6, PriceResolution.TenMinutes, 1_200, 800),
            new("resolution-15m-1h", 4, PriceResolution.FifteenMinutes, 1_200, 800),
            new("resolution-30m-4h", 8, PriceResolution.ThirtyMinutes, 1_200, 800),
            new("resolution-1h-24h", 24, PriceResolution.Hour, 1_200, 800),
            new("resolution-second-10m", 600, PriceResolution.Second, 1_200, 800),
        ];

    private static IReadOnlyList<ChartMemoryScenario> Dimensions()
        =>
        [
            new("dimensions-400x300", 576, PriceResolution.TenMinutes, 400, 300),
            new("dimensions-800x600", 576, PriceResolution.TenMinutes, 800, 600),
            new("dimensions-1200x800", 576, PriceResolution.TenMinutes, 1_200, 800),
            new("dimensions-1600x1200", 576, PriceResolution.TenMinutes, 1_600, 1_200),
        ];

    private static IReadOnlyList<ChartMemoryScenario> Features()
        =>
        [
            new("features-ohlc-compressed", 576, PriceResolution.TenMinutes, 1_200, 800),
            new("features-candlestick-compressed", 576, PriceResolution.TenMinutes, 1_200, 800, PriceChartStyle.Candlestick),
            new("features-ohlc-preserved", 576, PriceResolution.TenMinutes, 1_200, 800, GapMode: PriceGapMode.Preserve),
            new("features-sma", 576, PriceResolution.TenMinutes, 1_200, 800, SmaWindows: [20, 50, 100]),
            new("features-bollinger", 576, PriceResolution.TenMinutes, 1_200, 800, BollingerPeriod: 20),
            new("features-sma-and-bollinger", 576, PriceResolution.TenMinutes, 1_200, 800, SmaWindows: [20, 50, 100], BollingerPeriod: 20),
        ];

    private static IReadOnlyList<ChartMemoryScenario> Retention()
        =>
        [
            new("retention-discard", 576, PriceResolution.TenMinutes, 1_200, 800),
            new("retention-retain", 576, PriceResolution.TenMinutes, 1_200, 800, Retention: ChartMemoryRetentionMode.Retain),
            new("retention-write-and-discard", 576, PriceResolution.TenMinutes, 1_200, 800, Retention: ChartMemoryRetentionMode.WriteAndDiscard),
            new("retention-concurrent-4", 576, PriceResolution.TenMinutes, 1_200, 800, Concurrency: 4),
        ];
}

public sealed record ChartMemoryLabOptions(
    string Profile,
    int Iterations,
    int WarmupIterations,
    string OutputDirectory,
    int PeakSampleMilliseconds,
    bool ForceFullCollection,
    bool WriteCharts)
{
    public void Validate()
    {
        if (Iterations < 1 || WarmupIterations < 0)
        {
            throw new InvalidOperationException("Chart memory iteration counts are invalid.");
        }

        if (PeakSampleMilliseconds is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Chart memory peak sample interval must be between 1 and 1000 milliseconds.");
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            throw new InvalidOperationException("Chart memory output directory is required.");
        }
    }
}
