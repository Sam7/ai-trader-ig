Here is a comprehensive, standalone reference guide for the **AI Trader IG Command-Line Interface (CLI)**. You can save this directly as a markdown file (e.g., `cli-reference.md`).

It covers the complete syntax, available branches, options, and practical examples for every command built into the `Trading.Cli` project.

---

# AI Trader IG: CLI Reference Guide

The `Trading.Cli` project provides a `Spectre.Console`-based command-line interface. It is designed for manual execution, local verification, and triggering isolated parts of the automation pipeline without booting the background worker.

**General Execution Syntax:**

```powershell
dotnet run --project src/Trading.Cli -- <command> [options]

```

---

## 1. Authentication

Validates your IG credentials and returns the active session details (Broker, Account ID, and Auth Time).

* **Command:** `auth`
* **Example:**
```powershell
dotnet run --project src/Trading.Cli -- auth

```



---

## 2. Market Discovery, Prices & Charting

Commands to explore IG markets, retrieve historical prices, and generate ScottPlot charts.

### 2.1 Search Markets

Searches discoverable markets by text query.

* **Command:** `markets search`
* **Options:**
* `-q, --query <TEXT>` *(Required)*: The search term (e.g., VIX).
* `--max <COUNT>`: Maximum results to return (Default: 20).


* **Example:** `... markets search --query VIX --max 10`

### 2.2 Browse Markets

Navigates the IG market hierarchy nodes.

* **Command:** `markets browse`
* **Options:**
* `--node-id <ID>`: The specific node to browse. If omitted, browses the root node.


* **Example:** `... markets browse`

### 2.3 Market Details

Shows metadata and dealing rules (e.g., minimum stop distances, lot sizes) for a specific EPIC.

* **Command:** `markets details`
* **Options:**
* `-i, --instrument <EPIC>` *(Required)*: The exact IG instrument ID without whitespace.


* **Example:** `... markets details --instrument CC.D.VIX.UMA.IP`

### 2.4 Show Prices

Fetches recent historical prices.

* **Command:** `markets prices`
* **Options:**
* `-i, --instrument <EPIC>` *(Required)*.
* `--resolution <VALUE>`: e.g., `minute`, `5minute`, `hour`, `day`. Required if using `--max` or a date range.
* `--max <COUNT>`: Number of points to retrieve.
* `--from <ISO-8601>` & `--to <ISO-8601>`: Specific date range. Cannot be combined with `--max`.


* **Example:** `... markets prices --instrument CC.D.VIX.UMA.IP --resolution hour --max 10`

### 2.5 Render Market Chart

Fetches prices, renders an OHLC/Candlestick chart via ScottPlot, and saves it as a PNG.

* **Command:** `markets chart`
* **Options:**
* `-i, --instrument <EPIC>` *(Required)*.
* `--resolution <VALUE>` *(Required)*.
* `--output <PATH>` *(Required)*: File path to save the PNG.
* `--max <COUNT>` or `--from` / `--to` *(Required)*.
* `--style <STYLE>`: `candlestick` (default) or `ohlc`.
* `--gaps <MODE>`: `compress` (default) or `preserve`.
* `--sma <WINDOWS>`: Comma-separated list of SMA periods (e.g., `20,50`).
* `--bollinger <COUNT>`: Bollinger band period.
* `--width <PIXELS>` / `--height <PIXELS>`: Image dimensions (Default: 1200x800).


* **Example:** `... markets chart --instrument CC.D.VIX.UMA.IP --resolution hour --max 50 --output artifacts\vix-chart.png --style candlestick --sma 20,50 --bollinger 20`

---

## 3. Trading & Positions

Manage live trades, stops, limits, and working orders.

### 3.1 Place Market Trades

Places an immediate OTC market order.

* **Command:** `trades buy` or `trades sell`
* **Options:**
* `-i, --instrument <EPIC>` *(Required)*.
* `-s, --size <SIZE>` *(Required)*: Order size.


* **Example:** `... trades buy --instrument IX.D.SPTRD.DAILY.IP --size 1`

### 3.2 List & Manage Positions

* **Command:** `positions list` (Lists open positions)
* **Command:** `positions close` (Closes an open position)
* `--deal-id <ID>` *(Required)*.
* `-s, --size <SIZE>`: Optional partial close size.


* **Command:** `positions update` (Amends stops and limits)
* `--deal-id <ID>` *(Required)*.
* `--stop-level <LEVEL>`, `--limit-level <LEVEL>`.
* `--trailing-stop-distance <DISTANCE>`, `--trailing-stop-increment <INCREMENT>`.


* **Example:** `... positions update --deal-id DIAAAAAAA --stop-level 1 --limit-level 100`

### 3.3 Manage Working Orders

Entry orders (Limit/Stop) placed away from the current market price.

* **Commands:** `working list`, `working create`, `working update`, `working cancel`.
* **Options for `create`:**
* `-i, --instrument <EPIC>` *(Required)*.
* `-d, --direction <buy|sell>` *(Required)*.
* `-t, --type <limit|stop>` *(Required)*.
* `-s, --size <SIZE>`, `-l, --level <LEVEL>` *(Required)*.
* `--time-in-force <gtc|gtd>`: Good-till-cancelled or Good-till-date (Default: gtc).
* `--good-till-date <ISO-8601>`.



---

## 4. Market Data Collection

Commands to interact with IG Lightstreamer, local SQLite storage, and the GCS-backed market-data mirror.

### 4.1 Collect Stream

Runs the streaming collector for specific markets, optionally for a bounded duration.

* **Command:** `marketdata collect`
* **Options:**
* `--instruments <EPICS>` *(Required)*: Comma-separated list of EPICs to stream.
* `--duration <TIMESPAN>`: Run limit (e.g., `60:00:00`). Max 7 days. If omitted, runs indefinitely until cancelled.


* **Example:** `... marketdata collect --instruments CS.D.BITCOIN.CFD.IP --duration 60:00:00`

### 4.2 Mirror From GCS

Downloads the configured GCS SQLite snapshot only when the remote object changes, validates it, and imports final market-data bars into the local SQLite database without replacing the active file.

* **Command:** `marketdata mirror sync`
* **Configuration:** Uses `MarketData:CloudSnapshot:BucketName`, `ObjectName`, and `Mirror` settings.
* **Authentication:** Uses Google Application Default Credentials.

* **Example:** `... marketdata mirror sync`

### 4.3 Mirror Status

Shows the last sync attempt, last successful sync, latest mirrored bar, local immutable snapshot path, remote generation/SHA, and stale status.

* **Command:** `marketdata mirror status`
* **Exit code:** Returns `2` if mirror mode is enabled but not configured or stale.

* **Example:** `... marketdata mirror status`

### 4.4 Explicit Historical Backfill

Intentionally calls IG historical REST and persists returned bars. This is the manual override when mirror mode is enabled; automatic historical fallback remains disabled in mirror mode.

* **Command:** `marketdata backfill`
* **Options:**
* `-i, --instrument <EPIC>` *(Required)*.
* `--resolution <VALUE>` *(Required)*: e.g., `5minute`, `10minute`, `hour`.
* `--from <ISO-8601>` / `--to <ISO-8601>` *(Required)*.

* **Example:** `... marketdata backfill --instrument CS.D.BITCOIN.CFD.IP --resolution 5minute --from 2026-06-29T00:00:00Z --to 2026-06-29T01:00:00Z`

**Mirror-mode note:** `markets prices` and `markets chart` are direct manual IG commands. Mirror mode protects automated market-data fallback paths, not explicit manual IG requests.

---

## 5. Automation & AI Workflows

Commands to trigger LLM evaluation steps, audit pipelines, or start the background worker natively in the foreground.

### 5.1 Run Worker

Starts the background automation schedule (TickerQ cron jobs).

* **Command:** `automation run`
* **Options:**
* `--duration <TIMESPAN>`: Optional bounded run duration, max 7 days.
* `--instruments <EPICS>`: Optional comma-separated EPIC filter for automation analysis only. Market-data mirror sync still syncs all cloud data.
* `--root <PATH>`: Optional prompt/evidence root for artifacts produced by this run.
* **Example:** `... automation run --duration 08:00:00 --root Logs\Observability\2026-07-03-long-run --instruments CC.D.CL.UMA.IP,CS.D.CFAGOLD.CFA.IP`

`automation run` is the long-lived scheduler. If an intraday scan fires and today's in-memory daily plan is missing, the scan lazily creates the daily plan first, then continues. This covers process restarts and missed daily-plan schedules without making market-data sync responsible for AI workflow state.

### 5.2 Daily Briefing & Planning

* **Command:** `automation brief research` (Generates the daily research Markdown via LLM).
* `--date <YYYY-MM-DD>`: Optional target date.


* **Command:** `automation brief plan` (Generates and saves the structured `TradingDayPlan` JSON via LLM).
* **Command:** `automation brief convert` (Converts a pre-existing Markdown file into a JSON plan).
* `--input <PATH>` *(Required)*: Path to the markdown file.


* **Example:** `... automation brief convert --date 2026-03-12 --input Logs\Observability\2026-03-12\002044798-daily-brief-research.md`

### 5.3 Intraday Opportunities

* **Command:** `automation intraday scan` (Runs a full 15-minute scan cycle once).
* `--date <YYYY-MM-DD>`, `--at <UTC-ISO>`.
* A full scan lazily creates the daily plan if it is missing for the target date.
* The scan writes a decision audit containing phase-one shadow decisions. With `Automation:Execution:Mode` set to `Disabled`, candidates are analyzed and audited but cannot be approved. With `Shadow`, allowlisted market-entry candidates can produce execution-ready intents, but no IG order is submitted.


* **Command:** `automation intraday prepare` (Prepares charts and JSON payloads without calling OpenAI).
* Preparation does not auto-create a daily plan. It only prepares when a plan already exists.
* **Command:** `automation intraday submit` (Submits a prepared JSON payload to OpenAI).
* `--input <PATH>` *(Required)*: Path to the `*intraday-opportunity-prepare.json` file.



### 5.4 Evaluate Decision Audits (Paper Trading)

Evaluates past AI candidate opportunities against local SQLite market data to calculate hypothetical R-Multiples and hit/stop rates.

* **Command:** `automation audit evaluate`
* **Options:**
* `--root <PATH>`: The observability root folder (Default: `Logs/Observability`).
* `--date <YYYY-MM-DD>`: Specific date to evaluate.
* `--resolution <RESOLUTION>`: Defaults to `5minute`.
* `--strict-data`: Require every expected final bar in each outcome window. This reproduces the older conservative behavior.
* `--max-assessment-missing-bars <COUNT>`: Allows a small number of interior missing bars for market-assessment scoring only. Candidate trade replay is not forward-filled.
* `--max-assessment-consecutive-missing-bars <COUNT>` and `--max-assessment-missing-ratio <RATIO>`: Additional assessment-only tolerance guards.

Audit gap handling separates price defects from broker session evidence. Missing bars are not assumed to mean a closed market; they remain insufficient unless the stored evidence includes broker closed-market status for that window, or the trade outcome was already decided before the first unsafe gap.

* **Example:** `... automation audit evaluate --root Logs\Observability --date 2026-03-12`

---

## 6. Order History & Status

Inspect raw IG deal references and transaction history.

* **Command:** `orders list` (Lists recent activity)
* `--from <ISO-8601>`, `--to <ISO-8601>`, `--max <COUNT>`.


* **Command:** `orders status` (Check exact status of a specific deal reference)
* `--deal-reference <REFERENCE>` *(Required)*.


* **Example:** `... orders status --deal-reference spike-...`
