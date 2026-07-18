# AI Trader IG: The Complete Developer Guide

Welcome to **AI Trader IG**. This guide takes you from a fresh clone to running, debugging, and deploying the system. The project is a .NET 10 algorithmic trading solution that evaluates markets, generates AI-driven trading plans, ingests live IG streaming data, supports manual broker operations, and can perform tightly gated IG demo-canary execution. Automated live trading is not implemented.

## Recommended reading path

If this is your first time in the repository:

1. Use the quickstart below to configure and run the CLI.
2. Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the canonical system overview, dependency graph, runtime flows, state ownership, evidence model, extension guide, and current roadmap limits.
3. Use [docs/cli-use.md](docs/cli-use.md) as the command reference.
4. Read [src/Ig.Trading.Sdk/README.md](src/Ig.Trading.Sdk/README.md) when changing the standalone IG SDK.
5. Treat files under `specs/plans/` as planning and roadmap context; confirm implemented behavior against the architecture guide and current code.
6. Check [the worker memory experiment log](docs/worker-memory-experiment-log.md) for current issues, investigations, findings, rejected assumptions, and next experiments.

---

## 1. Day-1 Quickstart: Local Setup & Configuration

Before diving into the code, you need to get your local environment running.

### 1.1 Prerequisites

You will need the following installed:

* **.NET 10.0.x SDK** to compile and run the solution.
* **Google Cloud CLI (`gcloud`)** and **Pulumi CLI** if you are touching the deployment infrastructure.
* **Python 3** for executing certain deployment and verification scripts.
* **SQLite3** to inspect the local market data database.

### 1.2 Secrets & Local Configuration

Do **not** commit live credentials. The application expects secrets to be loaded via environment variables, user secrets, or a git-ignored `appsettings.local.json` file.

To run the app, your configuration must include the following keys:

* `IG__BaseUrl`: Should be `https://demo-api.ig.com/gateway/deal` for local dev and testing.
* `IG__ApiKey`, `IG__Identifier`, `IG__Password`: Your IG Demo account credentials.
* `IG__AccountId`: Optional, to switch to a specific account.
* `Automation:Execution:Demo:*`: Safety gates for the demo canary, including the approved base URL, approved account, allowlisted instrument, armed flag, and kill switch.
* `AI:OpenAI:ApiKey`: Required for the LLM planning and intraday review workflows.

### 1.3 Configuring Tracked Markets

Markets are not hardcoded. The AI determines what to analyze based on a configuration file located at the repository root, defaulting to `tracked-markets.json`.
To add a new market, you must provide its IG EPIC, a display name, and its sector:

```json
{
  "AI": {
    "DailyBriefing": {
      "TrackedMarkets": [
        {
          "DisplayName": "Bitcoin",
          "InstrumentId": "CS.D.BITCOIN.CFD.IP",
          "Sector": "Crypto",
          "Aliases": [ "BTC" ]
        }
      ]
    }
  }
}

```

### 1.4 Verifying via the CLI

The `Trading.Cli` project is your main window into the system without booting the background worker.
Run the following to verify your IG connection:

```powershell
dotnet run --project src/Trading.Cli -- auth
```

#### Full CLI guide is in [docs/cli-use.md](docs/cli-use.md).

---

## 2. System Architecture & Projects

The solution strictly enforces separation of concerns to keep the business logic immune to broker quirks.

* **`Trading.Abstractions`**: The core domain. Contains zero implementation logic. Defines things like `ITradingGateway`, `PriceSeries`, and `OrderStatus`.
* **`Ig.Trading.Sdk`**: An isolated, Refit-based SDK for IG's REST API. Manages session tokens (`CST`, `X-SECURITY-TOKEN`), handles RSA encrypted passwords, and maps DTOs.
* **`Trading.IG`**: The Adapter. Implements `ITradingGateway` using the SDK, translates IG HTTP errors into domain `TradingGatewayException`s, and orchestrates order status lookups.
* **`Trading.Strategy`**: The broker-neutral decision layer. Exposes narrow daily-planning and intraday-decision APIs, deterministic shadow decisions, and execution-ready intents.
* **`Trading.Execution`**: The durable execution boundary. Tracks manual and automated broker mutations, preserves stop/limit protection intent, assigns broker-safe deal references, records submission attempts, and prevents duplicate submissions when a stable operation id is provided.
* **`Trading.AI`**: The LLM interaction layer. Owns typed review requests, prompt rendering/versioning via `PromptRegistry`, and strict JSON schema output via `Microsoft.Extensions.AI`.
* **`Trading.Automation`**: The application orchestration layer. Sequences preparation, AI analysis, deterministic decisions, audit evidence, and optional demo execution without moving those concerns into the CLI.
* **`Trading.MarketData`**: Manages price data ingestion, historical backfilling, SQLite persistence, and GCS-backed market-data snapshots.
* **`Trading.Charting`**: Generates broker-neutral price chart images (PNG) using `ScottPlot`.
* **`Trading.Worker`**: The background service running `TickerQ` to execute daily and intraday cron jobs.
* **`Trading.Worker.Diagnostics`**: A local-only synthetic worker-memory lab that composes the diagnostics module without IG or AI traffic.
* **`Trading.Infrastructure`**: Pulumi IaC definitions for GCP deployment.

The canonical and more detailed architecture guide is [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). It includes the real project-reference direction, composition roots, end-to-end flows, persistence and evidence rules, testing ownership, and guidance for adding strategies, prompts, chart recipes, or market-specific experts.

### Current issues and investigations

The active worker out-of-memory investigation is maintained in [docs/worker-memory-experiment-log.md](docs/worker-memory-experiment-log.md). It is the starting point for experiment results and current working conclusions; the linked diagnostics guides contain the procedures and raw artifact locations.

---

## 3. The Market Data & SQLite Pipeline

Market data is the lifeblood of the charting and AI analysis workflows. It is handled through a hybrid stream-and-fill approach.

* **SQLite Storage**: Price data is stored in `ig-market-data.sqlite` with Write-Ahead Logging (WAL) enabled. The schema tracks `price_bars`, connection health (`market_data_health`), and historical backfill status (`market_data_coverage`).
* **Live Streaming**: The system uses `IgMarketDataStreamClient` to connect to IG's Lightstreamer endpoint, subscribing to items like `CHART:{epic}:5MINUTE`. Stream callbacks are bounded and coalesce expendable forming-candle updates before batched SQLite writes; finalized candles are preserved or the stream fails loudly instead of silently losing data.
* **Historical Backfill**: If the system detects gaps (e.g., after a restart), the `MarketDataCollector` requests historical data via REST (`GetPricesAsync`). **Crucial note:** IG enforces strict historical data allowance limits. The codebase catches these specific allowance exceptions to prevent infinite retry loops.
* **Cloud Snapshots**: The production worker can publish a validated SQLite snapshot to GCS every five minutes using the Google Cloud Storage .NET client. Local development can mirror that snapshot with Application Default Credentials, validate it, and transactionally import final market-data bars without replacing an active SQLite file.
* **Worker Health**: The worker writes `worker-status.json` locally and can publish `market-data/health/worker-status.json` to GCS. The payload includes process memory, GC state, stream queue depth, persisted-update counters, latest market-data freshness, and the most recent chart/evidence operation size and duration. A separate bounded one-second/five-second memory-forensics path captures cgroup evidence and post-exit state without retaining market payloads. See [worker memory diagnostics](docs/worker-memory-diagnostics.md) before changing memory limits or GC settings.
* **Mirror Mode**: When `MarketData:CloudSnapshot:Mirror:Enabled` is `true`, automatic IG historical REST backfill is disabled. Local workflows read the mirrored data from the normal `MarketDataService` path. Explicit REST backfill remains available through the `marketdata backfill` CLI command.
* **State Separation**: Cloud snapshots import only `instruments` and final `price_bars`. Local `market_data_health`, `market_data_coverage`, observability, workflow, and transient state remain local so the production database does not overwrite local operational state.
* **Execution Boundary Storage**: Broker mutation idempotency lives in a separate SQLite file, `Logs/Execution/execution-boundary.sqlite`, not in the market-data database. It stores durable operation reservations, stop/limit protection intent, deal references, submission attempts, broker outcomes, and reconciliation state for manual CLI and automated execution paths.
* **Aggregation**: `PriceBarAggregator` allows the base 5-minute canonical data to be rolled up dynamically into 10-minute or 1-hour candles for the AI charts.

### 3.1 Local Cloud Mirror Setup

Use the mirror when you want production-collected market data locally without consuming IG historical allowance.

1. Authenticate with Google Application Default Credentials that can read the configured bucket:

```powershell
gcloud auth application-default login
```

2. Add local configuration in `appsettings.local.json`:

```json
{
  "MarketData": {
    "StorePath": "Logs/MarketData/ig-market-data.sqlite",
    "BackfillEnabled": true,
    "CloudSnapshot": {
      "BucketName": "YOUR_GCS_BUCKET",
      "ObjectName": "market-data/ig-market-data.sqlite",
      "Mirror": {
        "Enabled": true,
        "Interval": "00:05:00",
        "SnapshotDirectory": "Logs/MarketData/cloud-mirror/snapshots",
        "StatePath": "Logs/MarketData/cloud-mirror/state.json",
        "LockPath": "Logs/MarketData/cloud-mirror/sync.lock",
        "RetainedSnapshotCount": 3,
        "StaleAfter": "00:15:00"
      }
    }
  }
}
```

3. Run a one-shot sync or inspect status:

```powershell
dotnet run --project src/Trading.Cli -- marketdata mirror sync
dotnet run --project src/Trading.Cli -- marketdata mirror status
```

To run continuously, start the worker or `automation run`; the mirror hosted service runs only when mirror mode is enabled.

Troubleshooting:

* **Stale mirror**: run `marketdata mirror status` and check `Remote Object Stale`, `Remote Latest Bar Stale`, `Remote Updated UTC`, `Remote Latest Bar UTC`, and `Diagnosis`. A status of `Unchanged` with stale remote metadata points at the publisher/worker, not the local mirror command.
* **Permission failures**: verify ADC can read the bucket/object and that the GCS object path matches `BucketName` plus `ObjectName`.
* **Schema errors**: the mirror rejects snapshots that do not contain the current `instruments` and `price_bars` schema. It preserves the last valid local data.
* **Corrupt downloads**: validation runs `PRAGMA quick_check`; corrupt snapshots are rejected before import.
* **Unexpected IG REST calls**: mirror mode disables automatic historical backfill, but direct manual commands such as `markets prices`, `markets chart`, and `marketdata backfill` intentionally call IG.

---

## 4. AI Workflows, State & Observability

The automated flow uses narrow services instead of one catch-all workflow facade.

### 4.1 The Pipeline

1. **Daily Briefing**: Runs once a day to assess the macro regime. It produces a Markdown research brief, which a second prompt extracts into a structured `DailyPlanDocument` JSON.
2. **Intraday Preparation**: `IntradayOpportunityPreparationService` loads the active plan and fresh price series, renders the current chart recipe, and writes a typed preparation document with evidence IDs, hashes, time windows, and prompt/schema provenance.
3. **AI Analysis**: `IntradayOpportunityAnalysisService` verifies the prompt contract, request hash, evidence manifest, and artifact hashes before `IntradayOpportunityReviewer` calls OpenAI.
4. **Deterministic Decision**: `IIntradayDecisionService` independently validates AI candidates, recalculates reward/risk, and records rejected, unsupported, duplicate, or approved execution intents. Shadow mode never writes to IG.
5. **Coordination**: `IntradayOpportunityDecisionCoordinator` reserves the execution boundary, writes the immutable decision audit, and optionally invokes the demo canary for an approved intent.



### 4.2 Volatile State Warning

**IMPORTANT:** The system state (like the Daily Plan) is held in memory via `InMemoryTradingDayStore`. If the worker restarts, the plan is lost from memory; the next full intraday scan lazily recreates today's plan before analysis continues. Preparation-only commands do not auto-create a plan.

### 4.3 Prompt Engineering & Debugging

If the LLM misbehaves, you edit the embedded Markdown files located at `Trading.AI.Prompts.*`.

* **Observability Dumps**: The system drops everything into the `Logs/Observability/<Date>` directory. You will find exact rendered prompts, versioned evidence manifests, hashed chart artifacts, raw OpenAI responses, and extracted JSONs.
* **CLI Evidence Root**: `automation run --root <PATH>` overrides the prompt/evidence root for that run. Use this to keep long-run evidence in a dedicated subfolder.
* **Decision Audits**: Each `DecisionAuditRecord` is a write-once decision-time artifact containing prompt provenance, evidence references, deterministic outcomes, and any selected execution-ready intent. Paper evaluations and demo executions are appended as separate sidecars that include the source audit SHA-256; they never rewrite the source audit.
* **Execution Boundary Evidence**: Decision audits also include the reserved execution-boundary state and deterministic deal reference when an automated intent is selected. Manual submissions render their operation id, ledger state, attempt count, and broker reference in CLI output.

---

## 5. The Verification & Testing Workflow

Testing is treated as a first-class citizen. Standard tests run via `dotnet test Trading.slnx`. However, verifying the live IG and OpenAI interactions requires the Verification Scripts.

### 5.1 Opt-in Integration Tests

Integration tests that touch the live IG API are heavily guarded so they don't run accidentally in CI or fail without credentials.
To run them, set your environment variables:

```powershell
$env:RUN_IG_INTEGRATION='true'
$env:IG__TestEpic='CC.D.VIX.UMA.IP'
dotnet test --filter Category=Integration

```

The phase-zero broker baseline is a separate broker-mutating check. It records
open/protect/close, atomic protection, and rejection scenarios without enabling
automated strategy execution:

```powershell
$env:RUN_IG_INTEGRATION='true'
$env:RUN_IG_BROKER_BASELINE='true'
dotnet test tests/Trading.IG.Tests/Trading.IG.Tests.csproj --filter Category=BrokerBaseline
```

### 5.2 Verification Scripts

To prove the system works end-to-end, there is a suite of PowerShell scripts under `.codex/skills/verify-existing-trader/scripts/`.
You must run these scripts sequentially to verify the IG read paths, live demo lifecycles, and the AI Daily Briefing. They write their outputs to `artifacts/verification/<run-id>/`.

```powershell
# Example verification run
pwsh -File .codex/skills/verify-existing-trader/scripts/verify-ig-demo-lifecycle.ps1
pwsh -File .codex/skills/verify-existing-trader/scripts/verify-broker-baseline.ps1
pwsh -File .codex/skills/verify-existing-trader/scripts/run-controlled-worker.ps1

```

### 5.3 Opt-in GCS Mirror Verification

The normal test suite uses fake GCS storage. To verify the real GCS publish/mirror path with Application Default Credentials, provide a disposable bucket/prefix and opt in:

```powershell
$env:RUN_MARKETDATA_GCS_E2E='true'
$env:MARKETDATA_GCS_E2E_BUCKET='YOUR_GCS_BUCKET'
$env:MARKETDATA_GCS_E2E_PREFIX='codex-e2e'
dotnet test tests\Trading.MarketData.Tests\Trading.MarketData.Tests.csproj --filter RealGcsMirrorWorkflow
```

This publishes a unique test object, mirrors it into a local SQLite database, verifies restart behavior, then uploads corrupt content to the same unique object and proves the existing local data remains intact.

---

## 6. Infrastructure, Deployment & Database Backups

The system deploys as a standalone Linux background service to a GCP `e2-micro` VM.

### 6.1 IaC with Pulumi

* The `Trading.Infrastructure` project uses Pulumi to provision the GCP VM, an attached standard persistent disk, and a Google Cloud Storage (GCS) bucket for SQLite backups.
* The GitHub Action (`deploy.yml`) builds a self-contained `linux-x64` .NET binary, runs `pulumi up`, and uses `gcloud compute scp` to copy the binary and shell scripts to the VM.

### 6.2 Host Setup & systemd

* The `install-vm.sh` script runs on the VM to set up an `ai-trader` system user, unpacks the binary to `/opt/ai-trader/app`, installs the local-only exit-evidence hook, and wires up the `ai-trader.service` systemd file to keep the worker running.
* The service is tuned for `e2-micro`: bounded stream queues, health telemetry, `MemoryHigh`/`MemoryMax`, bounded cgroup diagnostics, controlled fail-fast thresholds, and restart-rate limits. Slack webhook settings belong in `/etc/ai-trader/ai-trader.env`, not in tracked config.

### 6.3 SQLite Cloud Snapshots

Because SQLite is used for crucial market data, the worker publishes snapshots from inside the .NET process.

* `MarketDataSnapshotPublisher` uses SQLite's online backup API to create a consistent copy while the production database remains writable.
* `MarketDataSnapshotValidator` runs `PRAGMA quick_check`, verifies the expected schema, computes SHA-256, and records latest-bar metadata.
* `GcsMarketDataSnapshotObjectStore` uploads the validated snapshot with the official Google Cloud Storage .NET client and the VM service account.
* The worker service config enables publishing every five minutes to `market-data/ig-market-data.sqlite` in the provisioned backup bucket.
* The health reporter publishes JSON status separately from the SQLite snapshot, so remote diagnostics remain available even when SSH or journald are unhealthy.
* No runtime synchronization uses cron, PowerShell, mounted GCS storage, or `gcloud`.

---

## 7. Engineering Bar & Guidelines

When modifying this repository, adhere strictly to these rules:

* **Composition over inheritance:** Keep types small and focused.
* **Fail gracefully:** Translate raw transport errors into domain-level contexts.
* **Never commit credentials:** Tracked secret files will fail code reviews.
* **SOLID pragmatism:** Avoid "utility" classes that become dependency magnets, and avoid building speculative framework layers (YAGNI).
