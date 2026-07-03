# AI Trader IG: Target Architecture & Boundary Guardrails

This document defines "what good looks like" for the AI Trader IG solution. Its purpose is to protect the codebase from entropy, architectural drift, and quick-and-dirty fixes that compromise long-term maintainability.

Any automated tool, AI assistant, or developer modifying this repository **must strictly adhere to these boundaries** unless there is an explicit, documented consensus to change the architecture.

---

## 1. Core Architectural Intent

The mission of this codebase is to build a small, clean, test-first trading solution. It prioritizes developer experience (DX), readable code, small APIs, fast feedback, and low cognitive overhead.

**Guiding Principles:**

* **Composability over Inheritance:** Build small, focused types.
* **Immutable Models:** Prefer immutability to prevent unexpected state mutations across layers.
* **Explicit Boundaries:** Prefer explicit, crisp boundaries over flexible-but-vague abstractions.
* **Broker-Neutrality:** The core business logic must never know it is talking to IG Group.

---

## 2. Project Boundaries & "What Good Looks Like"

The solution is intentionally split into distinct layers. This separation ensures that the broker-neutral model stays readable and stable.

| Project | Purpose & Responsibility | Strict "Never Do This" Guardrail |
| --- | --- | --- |
| **`Trading.Abstractions`** | Defines the domain language (models, enums, `ITradingGateway`). | **Never** include transport, HTTP, or broker-specific terminology (e.g., IG "Epics") in the contracts. |
| **`Trading.Strategy`** | Owns the business workflow: daily planning, intraday assessments, risk gating, and execution intent. | **Never** tie this layer to a specific LLM implementation or a specific broker adapter. |
| **`Trading.AI`** | Manages LLM prompts, json extraction, and observability artifacts. | **Never** let raw LLM responses leak into the strategy layer without strict JSON validation/mapping. |
| **`Trading.MarketData`** | Handles SQLite persistence, gap-finding, and historical backfills for price data. | **Never** allow strategy logic to bypass this layer to fetch raw prices directly. |
| **`Ig.Trading.Sdk`** | A Refit-based SDK for IG REST and streaming. Handles auth, session tokens, and raw DTOs. | **Never** couple this to the rest of the solution. It must remain extractable as a standalone OSS library. |
| **`Trading.IG`** | The Gateway Adapter. Maps abstraction requests into IG SDK calls and translates errors. | **Never** build a "second SDK" here. It must stay thin and focused solely on mapping and orchestration. |
| **`Trading.Charting`** | Renders broker-neutral `PriceSeries` into PNG images using ScottPlot. | **Never** bleed ScottPlot-specific drawing logic into the CLI, Strategy, or AI layers. |
| **`Trading.Cli` / `Trading.Worker**` | Outermost shells. Load configuration, wire Dependency Injection, and trigger flows. | **Never** write business logic or branching trading rules in the CLI or Worker. |

---

## 3. CLI Intent & Command Separation

The `Trading.Cli` project is the outermost shell of the application. Its sole intent is to wire configuration, load dependency injection, and expose manual commands.

**It must remain "thin" and entirely devoid of business logic.** If a command requires a complex sequence of steps, that sequence belongs in a service class (e.g., inside `Trading.Strategy`), which the CLI simply invokes.

### Maintaining Separation: Operational vs. Diagnostic Commands

During development, we frequently build CLI endpoints to validate code, isolate bugs, or collect data. To prevent the CLI from becoming a confusing junkyard, we strictly separate **Operational** commands from **Diagnostic/Verification** commands through command branching and routing.

* **Operational Commands:** Intended for the end-user or live system interaction. These should be top-level or cleanly grouped.
* *Examples:* `trades buy`, `positions list`, `orders status`.


* **Diagnostic & Verification Commands:** Intended for developers to evaluate system health, test AI prompts, or backfill databases. These must be segregated into specific branches so they don't clutter the primary trading UX.
* *Examples:* `automation brief research` (tests prompt generation), `automation audit evaluate` (runs historical paper-trading audits), `marketdata collect` (tests SQLite/Lightstreamer ingestion).



**How to protect this boundary:**

1. Use Spectre.Console's `AddBranch` feature to nest developer tools (e.g., keeping all AI tests under the `automation` and `brief` branches).
2. If a developer introduces a new command just to test an endpoint, force them to place it in a dedicated debugging branch (or use the external verification PowerShell scripts instead).

---

## 4. Strategy Intent & Workflow

The `Trading.Strategy` project is a **broker-neutral workflow library**. Its intent is to orchestrate *when* decisions are made, leaving the *how* to the AI, Market Data, and Adapter layers. It models the rules of engagement: daily briefings, shortlists, trigger handling, risk gating, and execution intent.

### The Current Strategy Architecture

The system operates on an event-driven, scheduled paradigm separated into two distinct rhythms:

**1. The Daily Setup (Strategic Intent)**
Before intraday trading begins, the system assesses the macro environment.

* **Action:** The `DailyBriefingPlanService` runs.
* **Mechanism:** The AI digests inflation, yields, and geopolitical news to determine the `MarketRegime` (e.g., *EventDriven*, *RiskOn*, *Mixed*).
* **Output:** It generates a `TradingDayPlan` containing a focused watchlist (exactly 3 ranked markets) and identified calendar events.

**2. The Intraday Scan (Tactical Execution)**
Throughout the day, the system looks for actionable setups on the watched markets.

* **Attention Filter:** The `MarketAttentionService` constantly evaluates incoming ticks. It mechanically drops noise and only escalates to a full AI review if specific criteria are met (e.g., a volatility expansion beyond the `VolatilityExpansionThreshold`, or a scheduled event release).
* **AI Review:** If escalated, the `IntradayOpportunityScanService` retrieves the recent `PriceSeries`, renders a PNG chart, and asks the AI to review the opportunity against the Daily Plan context.
* **Output:** The AI returns a JSON array of `CandidateOpportunities`, suggesting a Direction, Entry Method (Market/Limit/Stop), Stop Loss, and Take Profit.

**3. Mechanical Risk Gating (The Final Guard)**
The AI proposes trades, but **the system makes the final mechanical decision**.

* **Action:** The `OpportunityReviewer` intercepts the AI's candidate.
* **Mechanism:** It validates the math (e.g., ensuring a Buy order has a Stop below the Entry and a Target above the Entry). It evaluates strict rules: Does the setup meet the `MinimumRewardRiskRatio`? Is the `MaxSpread` exceeded? Is a high-impact calendar event too close?.
* **Sizing:** If approved, the `PositionSizer` uses the account equity and the `RiskPerTradeFraction` to calculate the exact trade quantity.

---

## 5. System Control Flow

To understand the architecture, you must understand how data flows through the boundaries without violating them.

### Flow A: The External Call (e.g., Placing a Trade)

1. **Invocation:** The `Trading.Cli` or `Trading.Worker` requests an action via the `ITradingGateway` interface using domain models.
2. **Adapter Mapping:** `Trading.IG` intercepts the call, validates it, and maps the broker-neutral request into an IG-specific DTO.
3. **Transport:** `Ig.Trading.Sdk` executes the HTTP call, handling `CST` and `X-SECURITY-TOKEN` headers automatically.
4. **Response & Translation:** The SDK returns a raw IG response or throws an `IgApiException`. `Trading.IG` maps the successful response back to a broker-neutral result, or translates the API exception into a `TradingGatewayException`.

---

## 6. State Management Rules

* **Trading Day State is Volatile:** The daily plan and current execution state (pending trades, active trades) are stored in memory via `InMemoryTradingDayStore`. If the worker restarts, the next full intraday scan lazily recreates today's plan before analysis continues. Do not attempt to persist this to a database without an architectural review.
* **Market Data is Durable:** Price bars, gap tracking, and stream accumulation are strictly persisted to `ig-market-data.sqlite` using Write-Ahead Logging (WAL).
* **Decision Audits are Immutable:** Every LLM prompt, context, chart, and extracted JSON must be saved to disk under the `Logs/Observability` folder to ensure the AI's reasoning is 100% auditable and reproducible.

---

## 7. Architectural Red Flags (Anti-Patterns)

If you see any of the following happening in a Pull Request or during an AI code-generation step, **reject it immediately**:

> **Red Flag 1: "Leaky DTOs"**
> Passing an `Ig.Trading.Sdk.Models` object (like `MarketDetailsResponse`) out of the `Trading.IG` project and into `Trading.Strategy` or `Trading.Cli`.

> **Red Flag 2: "The Fat Adapter"**
> Adding HTTP serialization, JSON parsing, or API key management directly inside `Trading.IG`. Those concerns belong strictly in `Ig.Trading.Sdk`.

> **Red Flag 3: "Naked Broker Errors"**
> Catching a generic `Exception` or `IgApiException` in the CLI and printing it to the user. All broker errors must be caught in `Trading.IG` and wrapped in a `TradingGatewayException` with a standardized `TradingErrorCode`.

> **Red Flag 4: "Secret Commits"**
> Hardcoding any IG credentials, OpenAI keys, or environment-specific connection strings directly into C# files or tracked `appsettings.json` files.

> **Red Flag 5: "Speculative Abstractions"**
> Building massive, complex, generic interfaces for features that do not exist yet (YAGNI). Abstractions should be explicit and crisp, driven by immediate testing needs.
