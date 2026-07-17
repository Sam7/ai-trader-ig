# Worker memory diagnostics and containment

This document describes the P1 memory-forensics design for the production `Trading.Worker`. It is intentionally operational rather than a trading-domain abstraction: the worker remains one .NET process, systemd remains the supervisor, and the diagnostic path must be cheaper and safer than the failure it investigates.

Use this alongside [the architecture overview](ARCHITECTURE.md). The local synthetic lab is in `tools/Trading.Worker.Diagnostics`; it never connects to IG, OpenAI, GCS, or Slack.

## Scope and non-goals

- Keep one persistent .NET worker. Market-data collection, automation, and scheduling are not split into duplicate hosts.
- Keep systemd as the only automatic recovery mechanism in this phase. A second .NET watchdog process, a Managed Instance Group, and VM reset automation are deliberately out of scope.
- Gather bounded evidence first. Automatic proactive containment is compiled and tested but disabled in production until the acceptance gates below are met.
- Do not change the `e2-micro` size, the current `MemoryHigh=400M`, or `MemoryMax=480M` merely to hide a regression.

A future MIG needs durable worker state, a health endpoint with meaningful semantics, deployment that targets an instance group rather than a named VM, and a deliberate data-recovery design. It is not a substitute for identifying the current allocation source.

## Runtime design

`Trading.Automation.Diagnostics` is a small module registered by `AddWorkerDiagnostics`. It has no broker contract and no prompt, price, or credential payload.

| Part | Cadence / lifecycle | Responsibility |
| --- | --- | --- |
| Memory sentry | Every second | Reads process/cgroup totals plus cheap host available-memory, PSI, and process-count signals. It does not read command lines or environments. |
| Forensic sampler | Every five seconds normally; every second in pressure mode | Appends process/GC, full cgroup, SQLite, host-memory/PSI, bounded host-process census, stream, operation, snapshot-publish, and recovery counters to a JSONL trace. |
| Trace store | Local, bounded | Uses an 8 MiB active segment and a 256 MiB production disk budget. A segment is flushed at least every 30 seconds, on rotation, and before proactive exit. |
| Prior-artifact uploader | Healthy worker only | Uploads closed traces, threshold artifacts, and exit evidence in one flight with a 30-second timeout. It pauses/cancels when pressure begins, never blocks the sentry, and retains a failed upload locally. |
| Exit-evidence hook | systemd `ExecStopPost` | Writes a small local JSON record after any worker exit: cgroup maps, host memory/PSI, top processes without command lines, and closed-artifact names. It never makes a network request and its failure cannot change service outcome. |
| Containment policy | Disabled initially | Optionally exits with code `75` after cgroup memory stays at or above its threshold for the configured one-second samples. `Restart=on-failure` then restarts the worker. |

The trace deliberately contains counters and sizes only. It never reads process command lines or environments, and does not include prompt text, API credentials, raw price updates, broker tokens, chart data, or exception payloads.

Pressure mode starts when worker cgroup memory reaches 256 MiB, host available memory drops below 256 MiB, memory PSI is non-zero, or the host process count rises materially above this process lifetime's baseline. It returns to the five-second cadence only after five continuous minutes below every signal. The first crossing of 256, 320, and 384 MiB per process instance synchronously flushes the JSONL trace then writes bounded compressed `smaps`, `smaps_rollup`, maps, descriptor classification, full cgroup, host census, and activity artifacts. No dump is taken automatically.

### Evidence fields

The forensic JSONL record includes:

- process ID, uptime, working set, private memory, thread/handle count;
- `/proc` RSS/PSS, anonymous/file/shared memory, private/shared clean and dirty pages, swap, locked/stack/executable/library/data memory, and file-descriptor/mapping counts;
- managed total/allocation rate, heap and committed bytes, per-generation/LOH/POH sizes and fragmentation, pinned/finalization counters, GC pause/load limits, and thread-pool counters;
- cgroup current, peak, swap, complete `memory.stat`/`memory.events`, cgroup PSI, and the legacy `high`, `max`, `oom`, and `oom_kill` summary fields;
- SQLite allocator/page-cache/malloc counters where the native provider supports them, plus database/WAL/shared-memory file sizes. The store deliberately uses no connection pool, so active-connection count remains unavailable rather than guessed;
- host total/available/cache/dirty/slab/swap, memory PSI, and a bounded top-process census (PID, PPID, UID, start time, executable name, cgroup, RSS, PSS only);
- bounded market-data stream queue/depth counters;
- bounded automation-operation start/completion/failure checkpoints with correlation IDs, item counts, duration, managed-allocated/committed/RSS/PSS/cgroup before/after samples, and all currently active operations; and
- bounded market-data snapshot/recovery activity counters.

The exit hook adds the systemd result, exit code/status, main PID, complete cgroup maps, host memory/PSI, process count, and top-process census. This is useful when the CLR dies before its next five-second sample.

### Files and retention

Production paths are configured in `src/Trading.Infrastructure/host/ai-trader.service`:

- local artifacts: `/var/lib/ai-trader/diagnostics`;
- cloud prefix: `market-data/diagnostics/<machine>/<UTC-date>/...`;
- local maximum: 256 MiB in production, including a reserved active-segment allowance. This is disk-only retention, not an in-memory queue;
- cloud retention: the Pulumi bucket lifecycle deletes only `market-data/diagnostics/` artifacts after 30 days.

An `.active` trace from a crash is renamed to a closed JSONL file at the next startup before upload is attempted. Failed uploads remain local for a later start, still subject to the local retention budget. The GCS uploader runs asynchronously after the initial local sample, so an unavailable bucket cannot defer the one-second sentry.

## Production settings

The service enables evidence collection with these values:

```ini
WorkerDiagnostics__Enabled=true
WorkerDiagnostics__SentryInterval=00:00:01
WorkerDiagnostics__SampleInterval=00:00:05
WorkerDiagnostics__FlushInterval=00:00:30
WorkerDiagnostics__SegmentMaximumBytes=8388608
WorkerDiagnostics__RetentionMaximumBytes=268435456
WorkerDiagnostics__Containment__Enabled=false
Automation__Enabled=false
Automation__IntradayOpportunities__Enabled=false
Automation__Execution__Mode=Disabled
Alerting__Slack__Enabled=true
```

`appsettings.example.json` shows the same schema for local use. The current production setting intentionally leaves `Containment.Enabled=false`. Existing `WorkerHealth` and Slack health reporting remain separate, slower health evidence; they are not the primary sudden-spike detector.

Health Slack notifications are state-transition alerts: an unchanged warning is
sent once, a different warning category or severity can send a new alert, and a
return to `Healthy` sends one recovery notification. The transition state is
in-memory and resets after a worker restart; the Slack cooldown remains for
non-health alerts such as fail-fast notifications.

During the collection-only production phase, market-data streaming, historical recovery, snapshots, health reporting, diagnostics, and Slack health alerts remain enabled. Daily briefing, intraday chart/AI scheduling, and execution are disabled by the systemd environment overrides above.

To inspect a production incident after the next restart:

```bash
sudo ls -lah /var/lib/ai-trader/diagnostics
sudo tail -n 20 /var/lib/ai-trader/diagnostics/*.jsonl
sudo journalctl -u ai-trader.service --since '30 minutes ago' --no-pager
```

The deployment copies `capture-worker-exit-evidence.sh`, installs it under `/opt/ai-trader/bin`, and wires it with a best-effort `ExecStopPost=-...` entry. Do not add a network call, a Slack call, or a second long-running process to that hook.

## Incident preservation and legacy-backup removal

Before guest recovery, preserve serial-console output, relevant GCS object metadata, instance/disk metadata, and a boot-disk snapshot. Do not reboot or remove guest files before that point. After SSH is available, copy only the small forensic directories and journal/cron evidence described in the incident plan; do not export the entire disk without separate approval.

The installer now removes only the obsolete `/etc/cron.d/ai-trader-backup` and `/opt/ai-trader/bin/backup-db.sh` path, first terminating that script's descendant process tree. It neither disables cron globally nor touches unrelated jobs. It then checks that the obsolete paths and an `ai-trader`-owned `gcloud` process are absent and samples the worker's cgroup process count three times. The deployment workflow runs a Linux fixture test for this idempotent cleanup before publishing.

Trace JSONL is schema version 2. The local reader preserves existing version-1 rows as version 1; it does not manufacture host, PSS, SQLite, or generation data that an older row never recorded.

## Deterministic incident analysis

Analyze downloaded closed traces (and optionally a serial capture) locally; the analyzer emits only derived values, source paths, and hashes, never raw inputs:

```powershell
pwsh -File tools/analyze-worker-memory-incident.ps1 `
  -TracePaths artifacts/forensics/trace-1.jsonl,artifacts/forensics/trace-2.jsonl `
  -SerialPaths artifacts/forensics/serial-console.txt `
  -OutputDirectory artifacts/incident-analysis
```

It writes `timeline.csv`, `summary.json`, `REPORT.md`, and `sources.sha256`. Classification is deliberately conservative: managed retention/churn, native/runtime, file/cache, threading, SQLite, and external-host-pressure are selected only when their evidence dominates. A report is conclusive only when at least 95% of material worker growth is reconciled; otherwise it says `Inconclusive` rather than choosing a root cause.

## Production history notebook

`notebooks/production_worker_memory_analysis.ipynb` reads all retained worker JSONL segments from the durable GCS diagnostics prefix by default. It also supports the current VM active files and local artifact directories. The first code cell controls the analysis window:

```python
DATA_SOURCE = 'gcs'
LOOKBACK_HOURS = None  # None = all retained history; use 48 for two days.
MAX_FILES = None       # None = every matching worker segment.
MAX_TOTAL_BYTES = None # None = no artificial byte cap.
```

The notebook writes a combined `memory.csv`, `summary.json`, and `source-manifest.json`. It deduplicates overlapping local snapshots, preserves process/segment boundaries, and fails explicitly when a configured cap would exclude data. GCS is preferred for long windows because VM `.active` files are point-in-time copies and may disappear after restart.

## Local synthetic memory lab

The local tool uses the production diagnostics module inside a Linux cgroup-v2 scope. It is the safe way to increase allocation pressure without adding IG subscriptions, historical requests, or automation activity.

From PowerShell:

```powershell
# Compare warmed idle runs. Run each at least three times and compare medians.
pwsh -File tools/run-worker-memory-lab.ps1 -Profile idle -DisableDiagnostics -DurationSeconds 60
pwsh -File tools/run-worker-memory-lab.ps1 -Profile idle -DurationSeconds 60

# Exercise retained data, LOH churn, and short bursts under a 384 MiB cgroup cap.
pwsh -File tools/run-worker-memory-lab.ps1 -Profile moderate -DurationSeconds 120 -MemoryMaxMiB 384

# Use only for an intentional cap-pressure experiment; inspect the artifact directory even on failure.
pwsh -File tools/run-worker-memory-lab.ps1 -Profile pressure -DurationSeconds 60 -MemoryMaxMiB 480

# Build the exact same local tool with workstation GC, without changing production.
pwsh -File tools/run-worker-memory-lab.ps1 -Profile moderate -DurationSeconds 120 -UseWorkstationGarbageCollection
```

The script publishes a self-contained Linux executable, runs it through `systemd-run --user`, and writes result/trace artifacts under `artifacts/diagnostics-lab/` (ignored by Git). Its final output includes the actual GC flavor, peak working set/cgroup memory, and cgroup `max`, OOM, and OOM-kill counters.

For a long-running overhead comparison, start one detached coordinator from the repository root:

```powershell
pwsh -File tools/start-worker-memory-overhead-series.ps1 -Runs 3 -DurationSeconds 600 -MemoryMaxMiB 480
```

The command prints the coordinator PID and absolute stdout/stderr paths. Monitor the printed stdout path with `Get-Content -Wait`. The launcher refuses to start when another coordinator, memory-lab unit, or series lock is active; each worker run uses a unique systemd unit so an unrelated cleanup cannot stop the active run. The series is incomplete unless its directory contains `run-01-off.log`, `run-01-on.log`, through `run-03-on.log`, plus `summary.json`.

`idle` has no synthetic allocation. `moderate` retains 96 MiB, churns 8 MiB per 100 ms, and holds 64 MiB bursts. `pressure` retains 160 MiB with the same churn/burst shape. These are allocation profiles, not a simulation of IG traffic.

### Initial calibration results

The first warmed WSL2/systemd-cgroup calibration on 2026-07-14 was intentionally conservative evidence, not a production sizing decision:

| Run | Peak working set | Peak cgroup memory | Outcome |
| --- | ---: | ---: | --- |
| Idle, diagnostics off, 20 s | 65.1 MiB | 63.5 MiB | clean |
| Idle, diagnostics on, 20 s | 72.7 MiB | 66.9 MiB | clean |
| Moderate, Server GC, 45 s, 320 MiB scope | 295.4 MiB | 294.9 MiB | no `max`, OOM, or OOM-kill events |
| Moderate, Workstation GC, 45 s, 320 MiB scope | 293.8 MiB | 290.6 MiB | no `max`, OOM, or OOM-kill events |

The idle comparison puts the observed diagnostics increment at about 3.3 MiB peak cgroup memory and 7.6 MiB peak working set in this local environment. Repeat it after meaningful diagnostic changes; do not assume the number is transferable to the VM.

### Telemetry overhead acceptance gate

For three warmed diagnostics-off/diagnostics-on comparisons, accept the normal
telemetry path only when all of these hold:

- steady cgroup overhead is no more than 8 MiB;
- average CPU delta is no more than 2 percentage points (diagnostics on minus
  diagnostics off, measured over the same workload);
- baseline diagnostic writes are below 2 KiB/s; and
- workload throughput has no material regression.

The CPU limit is an incremental diagnostics budget, not a limit on the worker's
total CPU usage. Threshold-triggered forensic captures and any separately
guarded dump are measured outside this steady-state gate.

For a bounded, read-only ingestion profile against the IG demo environment, use
`tools/run-live-ig-stream-memory.ps1`. It requires the ignored local
`appsettings.json` to point at `https://demo-api.ig.com/gateway/deal`, runs the
real Lightstreamer subscriptions and SQLite ingestion, and writes process
samples and broker logs under `artifacts/live-ig-stream-<UTC>/`. This profile is
Windows-based and is not the 480 MiB Linux-cgroup attribution lab; use it to
observe real stream/SQLite behaviour, then use the synthetic lab and enhanced
worker diagnostics for cgroup, GC, PSS and operation attribution.

```powershell
pwsh -File tools/run-live-ig-stream-memory.ps1 -DurationSeconds 1800
Get-Content artifacts/live-ig-stream-<UTC>/memory.csv -Wait
```

The runner defaults to IG's one-minute consolidated candles (`1MINUTE`) to
increase observation events. Use `-Resolution FiveMinutes` to match the
production stream resolution. The resolution applies only to this local test.

Analyze a completed live-stream run with:

```powershell
pwsh -File tools/analyze-live-ig-stream.ps1 `
  -RunDirectory artifacts/live-ig-stream-<UTC>
```

The live-stream analyzer writes `analysis.json`, `timeline.csv`,
`sources.sha256`, and `REPORT.md`. Its conclusion is intentionally limited to
the Windows RSS/private-memory and SQLite-file evidence collected by that
runner; it does not replace the Linux-cgroup attribution lab.

For production VM diagnostics, use
`notebooks/production_worker_memory_analysis.ipynb`. Its download cell discovers
the newest active JSONL segment under `/var/lib/ai-trader/diagnostics` through
read-only `gcloud compute ssh`, then derives `memory.csv` and `summary.json`
before plotting RSS, PSS, managed, cgroup-category, pressure, and operation
signals. The notebook requires an authenticated local `gcloud` CLI; it does not
read command lines, environments, credentials, prompts, broker payloads, or raw
market data.

The implementation validation also forced containment at a deliberately low local threshold: it exited with status `75` after three sentry samples, left an active trace, and the next normal process recovered that trace into closed JSONL. A transient systemd service with the installed `ExecStopPost` command then exited with `75` and produced a valid local exit-evidence JSON artifact. Neither validation enables production containment.

## GC experiments: current decision

The production worker and the lab default to Server GC. Both project files accept this build-only switch:

```powershell
dotnet publish src/Trading.Worker/Trading.Worker.csproj -r linux-x64 -p:UseWorkstationGarbageCollection=true
```

It emits `<ServerGarbageCollection>false</ServerGarbageCollection>` only for that build. It does not change the committed service configuration. The matched local workload showed a small cgroup reduction, but one synthetic run is insufficient to trade throughput/latency characteristics for a production change. Keep Server GC in production until repeated VM-representative runs and production traces show a clear benefit.

Do **not** add the originally proposed decimal environment value `DOTNET_GCHeapHardLimit=314572800`. .NET interprets GC numeric environment variables as hexadecimal, so that value is not a 300 MiB cap. The correctly expressed 300 MiB environment value is:

```ini
DOTNET_GCHeapHardLimit=0x12C00000
```

Even with correct syntax, this caps GC heap plus GC bookkeeping, not total process/cgroup memory. Thread stacks, native allocations, mapped files, SQLite, networking, and runtime overhead remain outside it. In the initial 160 MiB retained pressure test, both the correct 300 MiB hard-cap run and the uncapped run reached the 384 MiB cgroup limit repeatedly (`memory.events.max` 893 and 794 respectively) without OOM kills. The cap was not a safe substitute for cgroup headroom.

Therefore neither Workstation GC nor a GC hard limit is enabled in `ai-trader.service` today. Any future change must be a separately reviewed, reversible profile with before/after evidence and a rollback path.

The relevant runtime rules are documented by Microsoft: [GC runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector).

## Containment acceptance gate

Before setting `WorkerDiagnostics__Containment__Enabled=true` in production, require all of the following:

1. Three warmed local comparisons show the diagnostics budget is acceptable and the target profile stays below the selected threshold without `memory.events.max` growth.
2. A production trace identifies the dominant growth category (`anon`, file-backed, managed heap/committed, queue depth, snapshot/recovery activity, or external/native pressure).
3. The proposed threshold leaves measured headroom below systemd `MemoryMax`; it must be based on cgroup current memory, not just managed heap or working set.
4. A controlled local scope test verifies three sustained one-second samples cause exit code `75`, systemd restarts the process, a closed trace survives, and the exit hook records evidence.
5. The rollout has a short observation window and explicit rollback: set containment back to `false` without changing the worker package.

The default candidate is 352 MiB for three samples, but it is a hypothesis only. Do not turn it on because the configuration exists.

## Root-cause workflow

When an incident occurs, preserve evidence before rebooting or resizing:

1. Compare the latest GCS worker health object with local/serial timestamps; a stale object confirms missing progress but not why the process died.
2. Inspect the newest local trace and exit JSON after systemd restarts.
3. Check cgroup `memory.events`: `max` means the cgroup cap was hit/reclaimed; `oom`/`oom_kill` distinguish actual cgroup OOM action.
4. Correlate process/GC growth with stream queue depth and snapshot/recovery activity. A high cgroup value with a low managed heap points outside managed allocations.
5. Reproduce only the observed allocation shape in the synthetic lab before changing GC settings, queue bounds, or process topology.
6. Make one reversible change, then compare the same evidence fields again.

This is the durable path to a root cause. Restart policy protects availability; it does not explain memory growth.
