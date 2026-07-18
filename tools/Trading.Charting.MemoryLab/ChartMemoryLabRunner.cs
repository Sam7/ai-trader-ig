using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text.Json;
using Trading.Charting;

namespace Trading.Charting.MemoryLab;

public sealed record ChartRenderMeasurement(
    string Scenario,
    int Iteration,
    int BarCount,
    int Width,
    int Height,
    long OutputBytes,
    TimeSpan Duration,
    ProcessMemorySnapshot Before,
    ProcessMemorySnapshot After,
    ProcessMemorySnapshot Peak);

public sealed record ChartScenarioSummary(
    string Scenario,
    int BarCount,
    string Resolution,
    int Width,
    int Height,
    string Style,
    string GapMode,
    IReadOnlyList<int> SmaWindows,
    int? BollingerPeriod,
    string Retention,
    int Concurrency,
    long TotalOutputBytes,
    long MaximumOutputBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long? PeakPssBytes,
    long? PeakCgroupBytes,
    long PeakManagedBytes,
    long PeakCommittedBytes,
    double MedianDurationMilliseconds,
    double P95DurationMilliseconds,
    long PostCollectionManagedBytes,
    long PostCollectionPssBytes,
    int Measurements);

internal sealed record ChartScenarioRun(
    IReadOnlyList<ChartRenderMeasurement> Measurements,
    ProcessMemorySnapshot PostCollection);

public sealed class ChartMemoryLabRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions NdjsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPriceChartRenderer _renderer = new ScottPlotPriceChartRenderer();

    public async Task RunAsync(ChartMemoryLabOptions options, CancellationToken cancellationToken = default)
    {
        options.Validate();
        var scenarios = ChartMemoryScenarioCatalog.Create(options.Profile);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var metadata = new
        {
            StartedAtUtc = DateTimeOffset.UtcNow,
            options.Profile,
            options.Iterations,
            options.WarmupIterations,
            options.PeakSampleMilliseconds,
            options.ForceFullCollection,
            Runtime = Environment.Version.ToString(),
            ServerGarbageCollection = GCSettings.IsServerGC,
            ChartingAssembly = typeof(ScottPlotPriceChartRenderer).Assembly.GetName().Version?.ToString(),
            ScottPlotAssembly = typeof(ScottPlot.Plot).Assembly.GetName().Version?.ToString(),
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "manifest.json"), JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);

        var summaries = new List<ChartScenarioSummary>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await MeasureScenarioAsync(scenario, options, outputDirectory, cancellationToken).ConfigureAwait(false);
            var summary = BuildSummary(scenario, run.Measurements, run.PostCollection);
            await File.WriteAllLinesAsync(
                Path.Combine(outputDirectory, $"{scenario.FileSafeName()}-calls.ndjson"),
                run.Measurements.Select(measurement => JsonSerializer.Serialize(measurement, NdjsonOptions)),
                cancellationToken);
            summaries.Add(summary);
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, $"{scenario.FileSafeName()}-summary.json"),
                JsonSerializer.Serialize(summary, JsonOptions),
                cancellationToken);
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(summaries, JsonOptions),
            cancellationToken);

        var report = BuildReport(metadata, summaries);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "REPORT.md"), report, cancellationToken);
    }

    private async Task<ChartScenarioRun> MeasureScenarioAsync(
        ChartMemoryScenario scenario,
        ChartMemoryLabOptions options,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var series = PriceSeriesFixtureFactory.Create(scenario);
        for (var warmup = 0; warmup < options.WarmupIterations; warmup++)
        {
            _ = _renderer.RenderPng(series, scenario.Style, scenario.GapMode, scenario.EffectiveSmaWindows, scenario.BollingerPeriod, scenario.Width, scenario.Height);
        }

        if (options.ForceFullCollection)
        {
            CollectFull();
        }

        var measurements = new List<ChartRenderMeasurement>(options.Iterations * scenario.Concurrency);
        var retained = scenario.Retention == ChartMemoryRetentionMode.Retain ? new List<byte[]>() : null;
        var chartDirectory = Path.Combine(outputDirectory, scenario.FileSafeName());
        if (options.WriteCharts || scenario.Retention == ChartMemoryRetentionMode.WriteAndDiscard)
        {
            Directory.CreateDirectory(chartDirectory);
        }

        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scenario.Concurrency == 1)
            {
                var measurement = MeasureOne(scenario, series, iteration, options.PeakSampleMilliseconds, chartDirectory, options.WriteCharts, retained);
                measurements.Add(measurement);
            }
            else
            {
                var before = ProcessMemorySnapshot.Capture();
                var stopwatch = Stopwatch.StartNew();
                ProcessMemorySnapshot peak;
                byte[][] outputs;
                var sampler = new PeakMemorySampler(options.PeakSampleMilliseconds);
                try
                {
                    outputs = await Task.WhenAll(Enumerable.Range(0, scenario.Concurrency).Select(_ => Task.Run(
                        () => _renderer.RenderPng(series, scenario.Style, scenario.GapMode, scenario.EffectiveSmaWindows, scenario.BollingerPeriod, scenario.Width, scenario.Height),
                        cancellationToken))).ConfigureAwait(false);
                }
                finally
                {
                    sampler.Dispose();
                }
                peak = sampler.Peak;
                stopwatch.Stop();
                var after = ProcessMemorySnapshot.Capture();
                foreach (var output in outputs)
                {
                    if (retained is not null) retained.Add(output);
                }

                measurements.Add(new ChartRenderMeasurement(
                    scenario.Name,
                    iteration,
                    scenario.BarCount,
                    scenario.Width,
                    scenario.Height,
                    outputs.Sum(output => (long)output.Length),
                    stopwatch.Elapsed,
                    before,
                    after,
                    peak));
            }
        }

        if (options.ForceFullCollection)
        {
            CollectFull();
        }

        var postCollection = ProcessMemorySnapshot.Capture();
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{scenario.FileSafeName()}-post-collection.json"),
            JsonSerializer.Serialize(postCollection, JsonOptions),
            cancellationToken);
        return new ChartScenarioRun(measurements, postCollection);
    }

    private ChartRenderMeasurement MeasureOne(
        ChartMemoryScenario scenario,
        Trading.Abstractions.PriceSeries series,
        int iteration,
        int sampleMilliseconds,
        string chartDirectory,
        bool writeCharts,
        List<byte[]>? retained)
    {
        var before = ProcessMemorySnapshot.Capture();
        var stopwatch = Stopwatch.StartNew();
        byte[] output;
        ProcessMemorySnapshot peak;
        var sampler = new PeakMemorySampler(sampleMilliseconds);
        try
        {
            output = _renderer.RenderPng(series, scenario.Style, scenario.GapMode, scenario.EffectiveSmaWindows, scenario.BollingerPeriod, scenario.Width, scenario.Height);
        }
        finally
        {
            sampler.Dispose();
        }
        peak = sampler.Peak;
        stopwatch.Stop();
        var after = ProcessMemorySnapshot.Capture();
        if (retained is not null)
        {
            retained.Add(output);
        }

        if (writeCharts || scenario.Retention == ChartMemoryRetentionMode.WriteAndDiscard)
        {
            File.WriteAllBytes(Path.Combine(chartDirectory, $"chart-{iteration + 1:D5}.png"), output);
        }

        return new ChartRenderMeasurement(
            scenario.Name,
            iteration,
            scenario.BarCount,
            scenario.Width,
            scenario.Height,
            output.Length,
            stopwatch.Elapsed,
            before,
            after,
            peak);
    }

    private static ChartScenarioSummary BuildSummary(
        ChartMemoryScenario scenario,
        IReadOnlyList<ChartRenderMeasurement> measurements,
        ProcessMemorySnapshot postCollection)
    {
        var durations = measurements.Select(measurement => measurement.Duration.TotalMilliseconds).OrderBy(value => value).ToArray();
        return new ChartScenarioSummary(
            scenario.Name,
            scenario.BarCount,
            scenario.Resolution.ToString(),
            scenario.Width,
            scenario.Height,
            scenario.Style.ToString(),
            scenario.GapMode.ToString(),
            scenario.EffectiveSmaWindows,
            scenario.BollingerPeriod,
            scenario.Retention.ToString(),
            scenario.Concurrency,
            measurements.Sum(measurement => measurement.OutputBytes),
            measurements.Max(measurement => measurement.OutputBytes),
            measurements.Max(measurement => measurement.Peak.WorkingSetBytes),
            measurements.Max(measurement => measurement.Peak.PrivateBytes),
            measurements.Max(measurement => measurement.Peak.PssBytes),
            measurements.Max(measurement => measurement.Peak.CgroupPeakBytes ?? measurement.Peak.CgroupCurrentBytes),
            measurements.Max(measurement => measurement.Peak.ManagedBytes),
            measurements.Max(measurement => measurement.Peak.CommittedBytes),
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            postCollection.ManagedBytes,
            postCollection.PssBytes ?? 0,
            measurements.Count);
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var position = (values.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper ? values[lower] : values[lower] + ((values[upper] - values[lower]) * (position - lower));
    }

    private static void CollectFull()
        => GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

    private static string BuildReport(object metadata, IReadOnlyList<ChartScenarioSummary> summaries)
    {
        var lines = new List<string>
        {
            "# Chart memory lab report",
            "",
            $"Generated: `{DateTimeOffset.UtcNow:O}`",
            "",
            "This report measures renderer output, managed/runtime counters, process memory, PSS, and cgroup memory. It does not include IG, OpenAI, SQLite, or scheduler traffic.",
            "",
            "| Scenario | Bars | Pixels | Max output | Peak PSS | Peak cgroup | P50 ms | P95 ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
        };

        foreach (var summary in summaries)
        {
            lines.Add($"| {summary.Scenario} | {summary.BarCount} | {summary.Width}x{summary.Height} | {summary.MaximumOutputBytes:N0} | {summary.PeakPssBytes?.ToString("N0", CultureInfo.InvariantCulture) ?? "n/a"} | {summary.PeakCgroupBytes?.ToString("N0", CultureInfo.InvariantCulture) ?? "n/a"} | {summary.MedianDurationMilliseconds:F2} | {summary.P95DurationMilliseconds:F2} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
