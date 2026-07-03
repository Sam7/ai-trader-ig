# AI Trader IG: The Complete Developer Guide

Welcome to **AI Trader IG**. This guide is designed to take you from a fresh clone of the repository to completely understanding how to run, debug, and deploy the system. The project is a .NET 10 algorithmic trading solution that evaluates markets, generates AI-driven trading plans, ingests live IG streaming data, and executes trades.

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
* **`Trading.Strategy`**: The workflow orchestrator. Models the daily briefing, intraday opportunity reviews, and risk gating (e.g., position sizing based on risk rules).
* **`Trading.AI`**: The LLM interaction layer. Manages prompts via `PromptRegistry` and enforces strict JSON schema output via `Microsoft.Extensions.AI`.
* **`Trading.MarketData`**: Manages price data ingestion, historical backfilling, SQLite persistence, and GCS-backed market-data snapshots.
* **`Trading.Charting`**: Generates broker-neutral price chart images (PNG) using `ScottPlot`.
* **`Trading.Worker`**: The background service running `TickerQ` to execute daily and intraday cron jobs.
* **`Trading.Infrastructure`**: Pulumi IaC definitions for GCP deployment.

See the full architecture in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## 3. The Market Data & SQLite Pipeline

Market data is the lifeblood of the charting and AI analysis workflows. It is handled through a hybrid stream-and-fill approach.

* **SQLite Storage**: Price data is stored in `ig-market-data.sqlite` with Write-Ahead Logging (WAL) enabled. The schema tracks `price_bars`, connection health (`market_data_health`), and historical backfill status (`market_data_coverage`).
* **Live Streaming**: The system uses `IgMarketDataStreamClient` to connect to IG's Lightstreamer endpoint, subscribing to items like `CHART:{epic}:5MINUTE`. Ticks are accumulated into forming and finalized candles by `IgChartCandleUpdateAccumulator` before being written to SQLite.
* **Historical Backfill**: If the system detects gaps (e.g., after a restart), the `MarketDataCollector` requests historical data via REST (`GetPricesAsync`). **Crucial note:** IG enforces strict historical data allowance limits. The codebase catches these specific allowance exceptions to prevent infinite retry loops.
* **Cloud Snapshots**: The production worker can publish a validated SQLite snapshot to GCS every five minutes using the Google Cloud Storage .NET client. Local development can mirror that snapshot with Application Default Credentials, validate it, and transactionally import final market-data bars without replacing an active SQLite file.
* **Mirror Mode**: When `MarketData:CloudSnapshot:Mirror:Enabled` is `true`, automatic IG historical REST backfill is disabled. Local workflows read the mirrored data from the normal `MarketDataService` path. Explicit REST backfill remains available through the `marketdata backfill` CLI command.
* **State Separation**: Cloud snapshots import only `instruments` and final `price_bars`. Local `market_data_health`, `market_data_coverage`, observability, workflow, and transient state remain local so the production database does not overwrite local operational state.
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

* **Stale mirror**: run `marketdata mirror status` and check `Last Success UTC`, `Latest Bar UTC`, and `Message`.
* **Permission failures**: verify ADC can read the bucket/object and that the GCS object path matches `BucketName` plus `ObjectName`.
* **Schema errors**: the mirror rejects snapshots that do not contain the current `instruments` and `price_bars` schema. It preserves the last valid local data.
* **Corrupt downloads**: validation runs `PRAGMA quick_check`; corrupt snapshots are rejected before import.
* **Unexpected IG REST calls**: mirror mode disables automatic historical backfill, but direct manual commands such as `markets prices`, `markets chart`, and `marketdata backfill` intentionally call IG.

---

## 4. AI Workflows, State & Observability

The trading strategy is executed in two main automated phases, orchestrated by `TradingDayWorkflow`.

### 4.1 The Pipeline

1. **Daily Briefing**: Runs once a day to assess the macro regime. It produces a Markdown research brief, which a second prompt extracts into a structured `DailyPlanDocument` JSON.
2. **Intraday Scan**: Runs on a cron schedule (e.g., every 15 minutes).
* **Attention Filter**: Quickly filters out noise (e.g., spread too wide, or no price volatility) without calling OpenAI.
* **AI Review**: Uses `IntradayOpportunityReviewer` to analyze ScottPlot PNG charts and the Daily Plan to find entry, stop, and target prices.
* **Guards**: Validates the math (e.g., Reward/Risk > minimum threshold) and calculates exact position sizes via the `PositionSizer`.



### 4.2 Volatile State Warning

**IMPORTANT:** The system state (like the Daily Plan) is held in memory via `InMemoryTradingDayStore`. If the worker restarts, the plan is lost from memory; the next full intraday scan lazily recreates today's plan before analysis continues. Preparation-only commands do not auto-create a plan.

### 4.3 Prompt Engineering & Debugging

If the LLM misbehaves, you edit the embedded Markdown files located at `Trading.AI.Prompts.*`.

* **Observability Dumps**: The system drops everything into the `Logs/Observability/<Date>` directory. You will find the exact rendered text prompts, the PNG charts sent to the vision model, raw OpenAI responses, and extracted JSONs.
* **CLI Evidence Root**: `automation run --root <PATH>` overrides the prompt/evidence root for that run. Use this to keep long-run evidence in a dedicated subfolder.
* **Decision Audits**: AI trade setups are saved as `DecisionAuditRecord`s. The `DecisionAuditEvaluationService` tests these records against actual market data (paper trading) to calculate true R-multiples and outcome statuses (Target Hit, Stopped Out).

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

* The `install-vm.sh` script runs on the VM to set up an `ai-trader` system user, unpacks the binary to `/opt/ai-trader/app`, and wires up the `ai-trader.service` systemd file to keep the worker running.

### 6.3 SQLite Cloud Snapshots

Because SQLite is used for crucial market data, the worker publishes snapshots from inside the .NET process.

* `MarketDataSnapshotPublisher` uses SQLite's online backup API to create a consistent copy while the production database remains writable.
* `MarketDataSnapshotValidator` runs `PRAGMA quick_check`, verifies the expected schema, computes SHA-256, and records latest-bar metadata.
* `GcsMarketDataSnapshotObjectStore` uploads the validated snapshot with the official Google Cloud Storage .NET client and the VM service account.
* The worker service config enables publishing every five minutes to `market-data/ig-market-data.sqlite` in the provisioned backup bucket.
* No runtime synchronization uses cron, PowerShell, mounted GCS storage, or `gcloud`.

---

## 7. Engineering Bar & Guidelines

When modifying this repository, adhere strictly to these rules:

* **Composition over inheritance:** Keep types small and focused.
* **Fail gracefully:** Translate raw transport errors into domain-level contexts.
* **Never commit credentials:** Tracked secret files will fail code reviews.
* **SOLID pragmatism:** Avoid "utility" classes that become dependency magnets, and avoid building speculative framework layers (YAGNI).
