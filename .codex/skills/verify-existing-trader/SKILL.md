---
name: verify-existing-trader
description: Verify the existing AI Trader IG implementation with durable evidence. Use when asked to run, resume, diagnose, or report the demo-only existing-system verification gates for this repository, including build/tests, redacted configuration, IG demo read paths, live demo lifecycle tests, OpenAI daily/intraday flows, scheduler runs, observability inspection, and final verification reports.
---

# Verify Existing Trader

## Overview

Use this skill to make the current implementation prove what works and what does not. Do not add trading features, persistence, automated decision logic, or architecture cleanup while using it.

Evidence lives under `artifacts/verification/<run-id>/`. Each run writes `verification.json`, `REPORT.md`, gate logs, redacted summaries, and artifact references. Never store secrets in evidence.

## Prerequisites

- Read `AGENTS.md` and preserve the working tree.
- Confirm `git status --short --branch` before changing anything.
- Use the IG demo endpoint only: `https://demo-api.ig.com/gateway/deal`.
- Keep real credentials in ignored local config, environment variables, or user secrets only.
- Do not print passwords, API keys, CST tokens, X-SECURITY-TOKEN values, OpenAI keys, or complete credential files.
- Do not run broker-mutating tests unless demo URL and demo account proof are captured.

## Quick Start

Run the safe baseline first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/inspect-repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-build-and-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-configuration.ps1
```

The first command creates a run ID and prints the run directory. Later commands use the latest run by default, or pass `-RunId <run-id>`.

## Complete Verification

Run gates in this order, continuing past non-fatal failures where later checks still have value:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/inspect-repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-build-and-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-configuration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-ig-read-path.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-ig-demo-lifecycle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-ai-daily-briefing.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-intraday-preparation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/run-controlled-worker.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/inspect-observability.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/summarize-verification.ps1
```

Run the demo lifecycle only after G00/G03 prove the demo endpoint and a redacted demo account. Run scheduler and freshness gates only during an active session for at least one configured market.

## Controlled Scheduler Verification

Use `run-controlled-worker.ps1` during an active market session. It calculates temporary cron overrides relative to current local time and writes worker output to the run directory. Do not edit committed schedule defaults for verification.

Expected successful sequence:

- scheduler registers daily briefing and intraday opportunity jobs;
- daily briefing job runs;
- daily research and structured plan complete;
- plan is available in the same worker process;
- intraday job runs;
- IG prices are retrieved and freshness is evaluated;
- charts and prepared request artifacts are written;
- OpenAI intraday review completes;
- structured assessments deserialize;
- validation reaches the current intentional outcome: decision logic pending.

If the sequence stops, identify the final confirmed stage, reproduce that stage manually, classify the failure, apply only the smallest justified correction, then rerun the affected gate.

## Rerun A Failed Gate

Use the same run directory unless intentionally starting a fresh run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/verify-ig-read-path.ps1 -RunId <run-id>
powershell -NoProfile -ExecutionPolicy Bypass -File .codex/skills/verify-existing-trader/scripts/summarize-verification.ps1 -RunId <run-id>
```

Each script is idempotent for its gate IDs: it replaces that gate entry in `verification.json` and updates `REPORT.md`.

## Diagnostic Decision Tree

When an intraday run produces no useful result, check in order:

1. Did the scheduled job run? Use scheduler registration, job ID, and job-start log.
2. Did a plan exist? Confirm daily job completion in the same process and remember the store is in memory.
3. Was the watchlist non-empty? Inspect the structured plan and tracked-market identities.
4. Did IG authenticate? Compare CLI and automation effective configuration before investigating prices.
5. Did each EPIC resolve? Classify expired or invalid EPICs as configuration.
6. Did prices return? Distinguish entitlement, authentication, invalid EPIC, transport, market availability, and empty response.
7. Were prices fresh enough? Check latest UTC timestamp, requested-at UTC, freshness threshold, and market-open status.
8. Did chart generation work? Confirm a real PNG, dimensions, and source price summary.
9. Was the intraday request prepared? Confirm prepared JSON, prompt text, chart attachments, and market context.
10. Did OpenAI complete? Inspect observability envelope status, usage, cost, response, and failure exception.
11. Did structured validation complete? Confirm assessment count, candidate count, watchlist checks, and decision-logic-pending outcome.

## Intentional Limitations

- No deterministic candidate decision is implemented.
- No strategy-generated order execution is expected.
- No durable trading-day state exists; a worker restart loses the in-memory plan until planning runs again.
- No streaming data, backtesting, profitability proof, dashboard, or notification workflow is part of this verification.
- A zero-candidate intraday review can pass if assessments deserialize, watchlist validation succeeds, and the terminal outcome is explicit.
