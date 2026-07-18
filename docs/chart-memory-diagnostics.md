# Chart renderer memory diagnostics

This is a renderer-only attribution lab for `Trading.Charting`. It measures ScottPlot and SkiaSharp in a separate process, so the result does not include IG streaming, SQLite, OpenAI, scheduling, or worker diagnostics. Use it to establish the charting increment before changing the production worker or its cgroup limits.

Record conclusions and rejected assumptions in the [worker memory experiment log](worker-memory-experiment-log.md); keep this page focused on running and interpreting the lab.

## Run the Linux cgroup lab

From PowerShell, run the self-contained Linux lab through WSL and systemd:

```powershell
pwsh -File tools/run-chart-memory-lab.ps1 `
  -Profile all `
  -Iterations 100 `
  -WarmupIterations 10 `
  -Runs 5 `
  -MemoryMaxMiB 480
```

The runner publishes `tools/Trading.Charting.MemoryLab` for `linux-x64`, runs each repetition in its own user systemd scope, and writes ignored artifacts under `artifacts/chart-memory-lab/`. A run contains `manifest.json`, one summary and `calls.ndjson` file per scenario, `summary.json`, and `REPORT.md`. `calls.ndjson` is the detailed evidence file for plotting before/after/peak memory and detecting a repeated-render slope. Add `-WriteCharts` only when output files themselves need inspection; file output is intentionally excluded from the default memory experiment.

The `-UseWorkstationGarbageCollection` switch is a build-only comparison. It does not alter the production service or the default Server GC build.

## Scenario profiles

| Profile | Purpose | Variables |
| --- | --- | --- |
| `production` | Approximate current chart payloads | 96-hour lookbacks at 5m, 10m, 15m, and 1h |
| `resolution` | Separate bar count and interval effects | 1m through 1h plus a short second-resolution case |
| `dimensions` | Test raster surface size | 400x300 through 1600x1200 at a fixed bar count |
| `features` | Test chart options | OHLC/candlestick, gap preservation, SMA, Bollinger, and combinations |
| `retention` | Test lifecycle and concurrency | discard, retain, write-and-discard, and four concurrent renders |
| `all` | Run every scenario | the complete matrix above |

Each render records the input shape, PNG byte count, duration, managed bytes, GC heap/committed bytes, working set, private memory, PSS, anonymous/file pages, cgroup current/peak memory, and GC collection counts before/after the call. A background sampler records the maximum observed process/cgroup values during the render. The runner performs a blocking full collection between the warm-up phase and measured phase and records a post-collection snapshot after each scenario.

## How to interpret the results

- Compare scenarios within the same process and compare medians across independent runs. A single run is not enough to distinguish JIT/native-library warm-up from a sustained increment.
- `Peak PSS` is the best process attribution signal. `Peak cgroup` is the cloud-risk signal. They are not interchangeable: shared Skia/native pages may appear differently in each.
- `Maximum output` and `Total output` describe returned PNG buffers and are not the renderer's full working memory. Use the before/after/peak fields to identify temporary native/raster allocations.
- If `retention-retain` grows with iteration while `retention-discard` returns near its warm baseline after full collection, the growth is consistent with caller retention. If both grow, investigate renderer/native caches or a leak.
- `retention-write-and-discard` separates file I/O from retained image buffers. It is a diagnostic control, not a recommendation to write charts in production.
- The four-way concurrent case is intentionally separate from the sequential cases. It tests simultaneous raster surfaces and native thread/cache behavior; do not infer its peak from four times the sequential result without measurement.

For a production decision, require repeated runs and reconcile the material cgroup growth against the process snapshots. Do not use this lab alone to choose a lower GC heap limit or a higher cgroup limit: those controls affect memory outside charting as well.

## Focused local checks

The scenario catalog and deterministic fixture can be tested without WSL:

```powershell
dotnet test tests/Trading.Charting.MemoryLab.Tests/Trading.Charting.MemoryLab.Tests.csproj
```

For a quick local smoke run, use a small profile and output directory:

```powershell
dotnet run --project tools/Trading.Charting.MemoryLab/Trading.Charting.MemoryLab.csproj -- `
  --profile dimensions --iterations 2 --warmup 1 --sample-ms 5 `
  --output artifacts/chart-memory-lab/smoke
```
