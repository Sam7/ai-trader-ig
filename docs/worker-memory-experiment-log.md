# Worker memory experiment log

This is the short, durable record of experiments investigating the worker's out-of-memory risk. Keep procedures in the diagnostics guides and raw outputs in `artifacts/`; record only the question, evidence-backed result, decision, and next step here.

## Current conclusion

Chart rendering is a material native/runtime cost, but the isolated lab does not explain the whole 480 MiB worker limit. A fresh 1,152-bar five-minute chart added approximately 20.5–25.0 MiB peak PSS. Renderer/cache growth reached approximately 26 MiB and then plateaued in a 500-render discard test. Retaining returned PNG buffers grows approximately with the number of charts retained. Production's contained mid-run spikes are consistent with the old high-frequency historical-recovery scan, so automatic recovery is now disabled in production configuration and redesigned as a bounded, quota-aware queue for controlled reintroduction.

Confidence: medium. The chart evidence is repeatable in the isolated Linux cgroup lab, but it is not a production worker trace.

## Confirmed findings

- The failed `all` chart run was a renderer exception, not an OOM or cgroup kill.
- Compressed indicator rendering can encounter duplicate ScottPlot OADate coordinates for gapped candles on Linux.
- The fix groups duplicate compressed coordinates and retains the first sequential position; chart and full-solution tests pass.
- The five-run `all` chart matrix completed successfully under a 480 MiB cgroup cap. Its maximum observed values were 118.03 MiB PSS and 129.94 MiB cgroup memory.
- A 500-render discard stress run reached approximately 26 MiB of PSS growth by about iteration 200 and remained near that level through iteration 500. This looks bounded/cache-like, not linear per-render growth.
- Retaining 500 PNG results added approximately 26–32 MiB relative to the warmed discard path, consistent with approximately 52 KiB per PNG.
- Four concurrent renders added approximately 19–20 MiB peak PSS over the sequential baseline.
- The chart PNG payload itself is small: approximately 52–62 KiB for the production scenarios tested.
- The old recovery shape inspected every tracked market across a 14-day window on a three-second cadence before REST admission, creating avoidable allocation churn even when no request could run.
- The worker source now keeps cache reads broker-free and persists recovery work and one global historical-allowance budget. Production configuration leaves automatic recovery disabled pending a measured rollout.

## Rejected assumptions

- `all` failing means the 480 MiB memory cap was reached: false; the process aborted on a duplicate-coordinate exception.
- Managed GC heap is the main chart cost: not supported by the evidence; the production chart's managed post-collection increment was approximately 0.1 MiB while PSS increased by tens of MiB.
- Discarding every chart immediately returns the process to its original baseline: false; native/runtime cache warm-up is bounded but material.
- Retaining chart PNGs alone explains a large worker OOM: not supported for the tested chart sizes, though retention is linear and must remain bounded.

## Experiment index

| ID | Date | Question | Result | Decision |
| --- | --- | --- | --- | --- |
| MEM-2026-07-18-01 | 2026-07-18 | Can the complete chart matrix run under the worker cap? | Initial run aborted in SMA rendering on duplicate OADate keys. | Fix renderer coordinate mapping before measuring. |
| MEM-2026-07-18-02 | 2026-07-18 | Does the fixed chart lab complete repeatedly? | Five complete `all` runs; maximum 118.03 MiB PSS / 129.94 MiB cgroup. | Keep charting as a bounded but material contributor. |
| MEM-2026-07-18-03 | 2026-07-18 | What is the fresh production chart increment? | Five production-only runs; 1,152-bar five-minute charts added 20.5–25.0 MiB peak PSS. | Include native chart headroom in worker sizing. |
| MEM-2026-07-18-04 | 2026-07-18 | Is repeated discard a linear leak? | 500-render discard run plateaued near 26 MiB growth after about 200 renders. | Treat as bounded cache warm-up for now; verify after library upgrades. |
| MEM-2026-07-18-05 | 2026-07-18 | Does caller retention grow with chart count? | 500 retained PNGs added approximately 26–32 MiB. | Bound retained chart count/bytes in the worker path. |
| MEM-2026-07-22-06 | 2026-07-22 | Could recovery explain the contained production spikes without spending IG quota unsafely? | Legacy planning was broad and frequent; redesign separates planning from execution and keeps all automatic REST behind one budgeted queue. | Keep production recovery disabled; reintroduce `Observe`, then `RecentOnly`, only with diagnostic evidence. |

## Experiment details

### MEM-2026-07-18-01 — Initial chart matrix failure

- Question: Can the isolated chart lab execute every resolution, dimension, feature, retention, and concurrency scenario?
- Command: `tools/run-chart-memory-lab.ps1 -Profile all -Iterations 100 -WarmupIterations 10 -Runs 5 -MemoryMaxMiB 480`
- Observed: run 1 completed earlier scenarios, then aborted in `features-sma` with `ArgumentException: An item with the same key has already been added` in `ResolveIndicatorXPositions`.
- Conclusion: this was a deterministic charting defect, not an OOM event.
- Evidence: `artifacts/chart-memory-lab/chart-memory-20260718T032529Z/run-01/`.

### MEM-2026-07-18-02 — Fixed full chart matrix

- Question: Does the renderer fix allow the matrix to complete under the production-sized cap?
- Command: same as MEM-2026-07-18-01.
- Observed: all five systemd scopes exited successfully; all 25 scenarios completed. Maximum observed PSS was 118.03 MiB and maximum cgroup memory was 129.94 MiB.
- Conclusion: the isolated chart process has substantial headroom under 480 MiB.
- Evidence: `artifacts/chart-memory-lab/chart-memory-20260718T034245Z/`.

### MEM-2026-07-18-03 — Fresh production chart cost

- Question: What does a production-shaped 96-hour chart cost before other scenario caches contaminate the process?
- Command: `tools/run-chart-memory-lab.ps1 -Profile production -Iterations 100 -WarmupIterations 10 -Runs 5 -MemoryMaxMiB 480`.
- Observed: the 1,152-bar five-minute scenario added 20.5–25.0 MiB peak PSS; PNG output was approximately 61.7 KiB. Managed post-collection growth was approximately 0.1 MiB.
- Conclusion: the dominant chart increment is native/runtime/raster memory, not retained managed heap.
- Evidence: `artifacts/chart-memory-lab/chart-memory-20260718T035438Z/`.

### MEM-2026-07-18-04/05 — Retention and repeated rendering

- Question: Does rendering repeatedly leak, and how much does caller retention add?
- Commands:
  - `tools/run-chart-memory-lab.ps1 -Profile retention -Iterations 100 -WarmupIterations 10 -Runs 5 -MemoryMaxMiB 480`
  - `tools/run-chart-memory-lab.ps1 -Profile retention -Iterations 500 -WarmupIterations 10 -Runs 1 -MemoryMaxMiB 480`
- Observed: discard growth reached approximately 26 MiB and plateaued by about iteration 200; retained PNGs continued growing with chart count; concurrent renders had approximately 19–20 MiB additional peak PSS.
- Conclusion: separate bounded renderer warm-up from caller retention. Neither result alone explains the worker OOM.
- Evidence: `artifacts/chart-memory-lab/chart-memory-20260718T035654Z/` and `artifacts/chart-memory-lab/chart-memory-20260718T035929Z/`.

### MEM-2026-07-22-06 — Recovery allocation and quota containment

- Question: Could the contained production-memory spikes be recovery work rather than charting, and can that work be made safe to reintroduce?
- Observed: the previous recovery loop considered every tracked market across a 14-day history on a three-second cadence before REST rate admission. The production worker did not run chart/AI automation during the observed segments. IG historical allowance is account-wide enough that this shape can exhaust it before a full backfill completes.
- Decision: production recovery remains `Disabled`. Cache reads never trigger REST. A future controlled rollout uses `Observe` first, then `RecentOnly`; only `RecentAndHistorical` spends the persisted global background budget after its 2,000-point reserve.
- Evidence: production worker memory traces and source inspection; no new load experiment was run for this implementation change.

## Open questions

- What is the combined peak when the real market-data baseline and the intraday chart/AI preparation path run in the same worker process?
- How many markets/charts are retained in the actual decision-evidence batch, and what is the resulting byte budget?
- Does the approximately 26 MiB native/cache plateau remain stable across ScottPlot, SkiaSharp, and runtime upgrades?
- Does a production-shaped run with the worker's normal Server GC, diagnostics, SQLite, and queues reproduce the same native-versus-managed split?

## Maintenance rules

- Add one short entry after each meaningful experiment, including failed experiments.
- Record exact command, profile, iteration/warm-up count, cgroup cap, date, and artifact path.
- Separate observation from interpretation; do not promote a hypothesis to a conclusion without repeated evidence.
- Link raw artifacts, but do not paste large tables or traces into this file.
- Update `Current conclusion`, `Confirmed findings`, and `Rejected assumptions` when the evidence changes.

## Operational safeguards implemented

### 2026-07-23 — deployment continuity guard

- A production deployment now keeps the old collector live until a staged worker has written final-bar checkpoints and published a verified market-data snapshot.
- Restart-gap repair is bounded to 30 minutes and is distinct from recurring recovery, which remains disabled in production during the OOM investigation.
- The deploy workflow requires a successful continuity report; closed/suspended no-bar intervals require IG session evidence.
