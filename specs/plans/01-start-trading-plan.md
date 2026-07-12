# AI Trader IG: Incremental Roadmap to Automated Demo Trading

## 1. Purpose

This document defines the incremental path from the repository’s current state—AI-generated trade candidates and retrospective paper evaluation—to a system that can place, monitor, and evaluate trades automatically in an IG demo account.

The objective is not to design the final production trading platform upfront. The objective is to:

1. Reach genuine automated demo-account trading quickly.
2. Keep the first execution scope deliberately narrow.
3. Make every phase independently testable.
4. Require observable evidence before progressing.
5. Expand instruments, order types, sizing, recovery, and resilience only as they become necessary.
6. Avoid embedding speculative technical solutions where further investigation is required.

The first meaningful milestone is:

> The system can select one valid AI candidate, convert it into a deterministic trade decision, place one tightly constrained and protected trade in the IG demo account, confirm the broker state, and record enough evidence to prove exactly what happened.

---

# 2. Current State

The repository already has several important working components:

* IG demo authentication.
* Market discovery and market-detail retrieval.
* Historical and streamed market-data collection.
* GCS-backed SQLite snapshot mirroring.
* Daily AI briefing and trading-plan generation.
* Intraday chart preparation.
* AI-generated opportunity candidates.
* Structured opportunity validation.
* Decision-audit artifact generation.
* Historical paper evaluation against stored market data.
* Manual CLI operations for placing, listing, updating, and closing IG demo positions.
* Manual support for working orders.

The latest run demonstrated that the market-data, planning, AI-review, artifact, and evaluation paths can operate together. It produced 17 candidates across 10 decision-audit records, but the automated workflow stopped after validating and recording those candidates. It did not approve, size, submit, confirm, or manage any trades.

The repository documentation also confirms that manual IG trading operations already exist independently of the AI workflow.

The central architectural gap is therefore not basic broker connectivity. It is the missing execution bridge:

```text
AI candidate
    ↓
Deterministic decision
    ↓
Broker and account checks
    ↓
Execution-ready trade intent
    ↓
Order submission
    ↓
Broker confirmation
    ↓
Protected position
    ↓
Lifecycle management
    ↓
Recorded outcome
```

---

# 3. Delivery Principles

## 3.1 Build only the next useful capability

Each phase should introduce the smallest meaningful behaviour that unlocks real learning.

The first automated trade does not need to support:

* Every instrument.
* Every order type.
* Sophisticated dynamic position sizing.
* Multiple simultaneous trades.
* Full portfolio optimisation.
* Automatic stop adjustments.
* Live-account deployment.

Those capabilities should be added only after the narrow path has been demonstrated repeatedly.

## 3.2 Fail closed

When the system cannot confidently establish that a trade is safe and valid, it should not submit it.

Examples include:

* Stale market data.
* Unknown broker state.
* Existing exposure that cannot be reconciled.
* Missing market-dealing rules.
* Expired opportunity.
* Unconfirmed previous submission.
* Failure to establish protective risk controls.
* Configuration that does not clearly identify an approved demo environment.

## 3.3 Treat broker state as authoritative

Local state can support orchestration, auditing, and idempotency, but open positions and working orders must ultimately be reconciled against IG.

A process restart must not cause the application to assume that no positions exist.

## 3.4 Assume scheduled work can run more than once

The latest run contained repeated processing of some scheduled job occurrence identifiers. Whether caused by retries, scheduler persistence, or another mechanism, execution must assume at-least-once delivery.

No broker submission should rely on a scheduled job running exactly once.

## 3.5 Preserve all decision information

The transition from an AI candidate to a broker order must not silently discard:

* Entry method.
* Entry level.
* Stop level.
* Target level.
* Setup expiry.
* Current spread.
* Opportunity score.
* Source decision-audit record.
* The market and account context used to approve the trade.

## 3.6 Every phase must be demonstrably complete

Each phase must produce evidence that can be asserted through automated tests, integration tests, saved artifacts, broker queries, or controlled verification scripts.

A phase is not complete merely because the code compiles or a method was added.

---

# 4. Execution Modes

The application should eventually distinguish explicitly between the following modes.

## Disabled

The system may generate and evaluate opportunities but cannot produce broker writes.

## Shadow

The system makes the full decision it would have made, including whether it would trade, but does not submit anything to IG.

It records the proposed execution-ready intent for later evaluation.

## Demo

The system may submit orders only to an explicitly approved IG demo environment and account.

## Live

Out of scope for this roadmap.

Nothing in this plan should implicitly enable live trading. Live-account support should require a separate review, evidence threshold, operational plan, and explicit implementation decision.

The precise configuration model should be resolved during implementation, but environment safety must be explicit rather than inferred from an arbitrary base URL.

---

# 5. Fastest Safe Route to Automated Demo Trading

The critical path is:

1. Reconfirm the existing manual demo-order lifecycle.
2. Connect AI candidates to deterministic shadow decisions.
3. Introduce durable idempotent execution intents.
4. Support one protected market-order path.
5. Submit one minimum-size trade to one allowlisted demo instrument.
6. Confirm the position and its protection from IG.
7. Record the outcome and prevent duplicate execution.
8. Add lifecycle recovery and broader scenarios incrementally.

The first automated version should deliberately support:

```text
One demo account
One allowlisted instrument
Market entry only
Minimum permitted broker size
One simultaneous position
One new trade per trading day
One explicitly armed execution mode
Mandatory stop and target
No unresolved previous order
```

This constraint is a feature, not a limitation. It creates the shortest path to real execution evidence without prematurely solving the entire trading domain.

---

# 6. Phase 0: Establish the Verified Broker Baseline

**Status:** DONE

## Objective

Prove that the existing IG integration can reliably complete a manual demo lifecycle before modifying the AI automation path.

## Expected outcome

A controlled verification can:

1. Authenticate with the intended IG demo account.
2. Retrieve market details and dealing rules.
3. Place a minimum-size demo position.
4. Confirm the resulting broker position.
5. Update or protect the position where supported.
6. Close the position.
7. Confirm that no residual position or order remains.
8. Save evidence of each broker response.

This is an integration baseline, not an automated strategy test.

## Questions to resolve

* Which instrument is safest and most consistently available for the demo canary?
* What is its minimum valid deal size?
* What size increments, stop distances, and supported order types apply?
* Can protective stop and target values be submitted with the initial position request?
* If protection requires a second request, what failure behaviour is required?
* How quickly does IG confirmation normally become available?
* Which broker identifiers are stable enough for reconciliation?
* What happens when a request succeeds at IG but the client times out?

## Verification evidence

The verification should be repeatable through an opt-in integration test or controlled script.

The evidence should include:

* Authentication success.
* Market details used.
* Requested deal size.
* Broker deal reference.
* Confirmed deal ID.
* Position before protection.
* Position after protection.
* Close confirmation.
* Final empty exposure state.

## Minimum scenario matrix

Keep this parcel focused on broker semantics and evidence. Do not introduce the
automated execution engine, durable order state, candidate selection, or sizing
policy in this phase.

The baseline should record:

* `preflight-demo-safety`: demo endpoint, redacted account, authentication,
  selected canary EPIC, market status, bid/ask, dealing rules, minimum deal size,
  stop/limit distances, and current canary exposure.
* `market-open-protect-close`: open a minimum-size market position through the
  existing gateway, confirm broker deal reference and deal ID, amend stop/limit
  protection, confirm protection on the broker position, close the position, and
  confirm the created position is gone.
* `atomic-protected-open-close`: submit a minimum-size market position through
  the SDK with attached stop/limit fields, record whether IG accepts atomic
  protection, confirm broker-visible protection when accepted, then close.
* `invalid-size-rejection`: submit below the discovered minimum size, capture
  the broker rejection or validation error, and prove no new position remains.
* `invalid-protection-rejection`: submit stop/limit values that violate broker
  direction or distance rules, capture the broker reaction, and prove no new
  position remains.
* `confirmation-source-timing`: for each broker write, record submission time,
  confirmation result, position visibility, and any activity-history evidence.
  Treat position and confirmation state as primary; activity history can be
  eventually indexed and should not be the only truth source.
* `cleanup-verification`: before and after the run, list positions and working
  orders. Only close positions created by the baseline run; block rather than
  mutate if unrelated canary exposure already exists.

The implemented verification path is intentionally small: a guarded opt-in live
test category plus a PowerShell wrapper that writes sanitized evidence under
`artifacts/verification/<run-id>/broker-baseline/`.

## Gaps addressed

* Confirms that the existing write integration actually works.
* Establishes the broker semantics needed for later phases.
* Identifies whether protection can be atomic.
* Establishes valid minimum sizing for the canary.

## Gaps deliberately left open

* No AI-driven execution.
* No automated candidate selection.
* No durable execution workflow.
* No restart recovery.
* No dynamic sizing.
* No support for multiple instruments or order types.

---

# 7. Phase 1: Produce Execution-Ready Decisions in Shadow Mode

**Status:** DONE

## Objective

Connect the AI opportunity pipeline to a deterministic decision process without placing broker orders.

This phase should answer:

> Given this candidate and the current system context, would the application trade it, and exactly what trade would it intend to place?

## Expected outcome

For every AI candidate, the system records one clear result:

```text
Rejected
Approved for shadow execution
Already processed
Unsupported by current execution scope
```

An approved candidate produces a complete execution-ready trade intent containing at least:

* A stable decision identifier.
* Source decision-audit identifier.
* Trading date.
* Instrument.
* Direction.
* Entry method.
* Entry or expected execution level.
* Stop level.
* Target level.
* Setup expiry.
* Intended quantity policy.
* Approval time.
* Reasons for approval.
* The rules and context used.
* Any limitations associated with the decision.

No order is submitted in this phase.

## Minimum decision checks

The shadow decision path should determine whether:

* The candidate belongs to the active daily watchlist.
* The candidate has not expired.
* The quote and market data are sufficiently fresh.
* Direction, entry, stop, and target are internally consistent.
* Reward/risk is recalculated rather than trusted from the model.
* Spread is acceptable relative to the proposed risk.
* The current price has not moved beyond an acceptable execution range.
* The candidate meets the configured opportunity threshold.
* The instrument is supported by the current execution phase.
* The entry method is currently supported.
* Another equivalent candidate has already been handled.
* The configured trading date is interpreted consistently in the trading timezone.
* A relevant high-impact event should block execution.

The exact rule ownership and service boundaries should be decided by the implementation team. The important requirement is that these decisions are deterministic, explainable, and testable.

## Questions to resolve

* What is the canonical model between an AI candidate and a broker order?
* Which existing strategy models remain useful, and which duplicate the newer candidate architecture?
* How should equivalent candidates from adjacent scans be identified?
* What constitutes a materially changed candidate rather than a duplicate?
* Should multiple candidates from one scan be ranked, or should the first approved candidate be used?
* Which rule owns trading-date calculation?
* How should daily-plan bias influence, but not dictate, candidate approval?
* Which rejection reasons should be first-class domain outcomes?

## Verification evidence

Automated tests should prove:

* The same input always produces the same decision result.
* Reward/risk is independently recalculated.
* Expired candidates are rejected.
* Stale candidates are rejected.
* Invalid stop and target geometry is rejected.
* Unsupported order types are rejected explicitly.
* Duplicate candidates are detected.
* The Melbourne trading date is correct around UTC date boundaries.
* No broker write can occur in shadow mode.
* Every decision produces an auditable artifact.

A controlled shadow run should produce a report showing:

* Candidates considered.
* Candidates approved.
* Candidates rejected.
* Rejection reasons.
* Proposed trade intents.
* Duplicate suppression.
* Which candidate would have been selected for execution.

## Gaps addressed

* Missing AI-candidate-to-decision bridge.
* “Decision logic pending” in the current workflow.
* Loss of candidate information between AI and strategy layers.
* Inconsistent trading-date derivation.
* Lack of explicit candidate deduplication.
* Lack of deterministic rejection reasons.

## Gaps deliberately left open

* No broker submission.
* Broker balances and positions may still be observational rather than enforced.
* Quantity may use a deliberately simple canary policy.
* No restart-safe order lifecycle is required yet.
* No working-order execution.

---

# 8. Phase 2: Create a Durable and Idempotent Execution Boundary

**Status:** DONE

## Objective

Create the minimum durable state required to ensure that one approved decision can produce at most one broker submission.

This phase should be completed before scheduled AI automation is allowed to write to the broker.

## Expected outcome

Before any broker call, the application creates or reserves a durable execution record.

That record should make it possible to determine whether the decision is:

```text
Approved
Reserved
Submitting
Submitted
Confirmed
Rejected by broker
Failed before submission
Outcome uncertain
Closed
```

The exact state model should be investigated and kept as small as practical.

The core outcome is:

> Re-running the same scheduled job, candidate, or decision cannot create a duplicate broker trade.

## Required behaviour

The execution boundary should support:

* Stable decision identifiers.
* Atomic reservation of an execution decision.
* Durable broker deal references.
* Durable broker deal IDs when confirmed.
* Recording of each submission attempt.
* Explicit representation of uncertain outcomes.
* Retrieval of the existing execution state after restart.
* Reconciliation rather than resubmission when the outcome is uncertain.
* A clear link back to the original candidate and decision audit.

## Questions to resolve

* Should execution state use the existing SQLite infrastructure or a separate operational store?
* What is the correct transaction boundary around reservation and submission?
* How should a timeout after possible broker acceptance be represented?
* Can the broker’s deal reference safely be deterministic?
* What evidence can distinguish “not submitted” from “submitted but not confirmed”?
* Which operations are safe to retry automatically?
* How should manual broker actions be represented during reconciliation?

## Verification evidence

Tests must simulate:

* The same decision being processed twice.
* Two workers attempting to reserve the same decision concurrently.
* Scheduler redelivery.
* Process termination before submission.
* Process termination after broker acceptance but before local confirmation.
* Local persistence failure after submission.
* Confirmation endpoint failure.
* Restart with an unresolved execution.

Expected test outcome:

> Across all duplicate and retry scenarios, the fake or demo broker receives at most one order for the same decision.

## Gaps addressed

* Lack of durable execution idempotency.
* Insufficient order-reference journal.
* Duplicate scheduler delivery risk.
* Inability to represent uncertain submissions.
* Inability to resume execution reconciliation after restart.

## Gaps deliberately left open

* This phase does not require automatic broker execution to be enabled.
* Full trading-day state does not yet need to be durable.
* Active position management remains limited.
* Working orders remain unsupported.

---

# 9. Phase 3: Minimum Viable Automated Demo Canary

**Status:** DONE

## Objective

Place the first automatically selected and protected trade in the IG demo account under tightly constrained conditions.

This is the first phase in which the system is genuinely trading automatically.

## Execution scope

The first canary should support only:

* One explicitly allowlisted IG demo account.
* One allowlisted instrument.
* Market-entry candidates.
* The instrument’s minimum valid deal size.
* One open position at a time.
* One new trade per trading day.
* No existing working orders.
* Fresh market data.
* A valid unexpired candidate.
* Mandatory protective stop and target.
* Explicitly armed demo execution.
* A global kill switch.

Limit and stop-entry candidates should be rejected as unsupported in this phase.

The existing generic theoretical position-sizing method should not be relied on for the first canary. Using the broker’s minimum valid quantity is safer and allows contract-aware sizing to be developed separately.

## Expected outcome

The scheduled automation can:

1. Produce AI candidates.
2. Select one deterministic approved candidate.
3. Confirm the application is explicitly operating against the approved demo environment.
4. Query current broker positions and working orders.
5. Confirm no conflicting or unresolved exposure exists.
6. Reserve the execution intent.
7. Submit one market order.
8. Establish the intended stop and target.
9. Confirm the resulting broker position and protection.
10. Record the confirmed deal reference and deal ID.
11. Prevent any duplicate order from subsequent scans or retries.
12. Surface a clear failure if the intended protection cannot be established.

## Critical risk-control question

The implementation must determine the safest supported mechanism for establishing stop-loss and take-profit protection.

The current order abstraction does not retain those values in the market-order request. This gap must be resolved before automated demo execution is considered complete.

If protection cannot be created atomically with entry, the engineers must determine:

* How the unprotected interval is minimised.
* How protection failure is detected.
* Whether an immediate compensating close is required.
* How partial failure is represented.
* What evidence proves that the final broker position is protected.

The plan should not prescribe a particular IG endpoint design without validating the broker’s supported behaviour.

## Broker state requirements

The execution path must query actual broker state rather than relying on the current passive risk context.

At minimum, it must know:

* Whether the instrument already has an open position.
* Whether a relevant working order exists.
* Whether another order is unresolved.
* Whether the account is the expected demo account.
* Whether the market is currently tradeable.
* Whether the requested quantity and stops comply with dealing rules.

Full portfolio-risk calculation is not yet required.

## Verification evidence

Unit and fake-gateway tests should prove:

* Execution is impossible when mode is Disabled or Shadow.
* Execution is impossible against an unapproved host or account.
* Only allowlisted instruments can be submitted.
* Only market candidates can be submitted.
* Maximum daily trade count is enforced.
* Existing exposure prevents a new trade.
* An unresolved previous submission prevents a new trade.
* The minimum valid quantity is used.
* Missing or invalid protection prevents successful completion.
* The same decision cannot create a second trade.
* A stale quote or expired setup prevents submission.

An opt-in IG demo integration test should prove:

```text
approved candidate
→ durable reservation
→ minimum-size order
→ broker confirmation
→ verified stop and target
→ visible open position
→ no duplicate on repeated processing
→ controlled close
→ verified empty final state
```

## Gaps addressed

* No automated broker submission.
* Fake risk context for basic existing-exposure checks.
* Market-order model losing stop and target.
* Lack of environment safety boundary.
* Lack of confirmed protected execution.
* Lack of broker-aware minimum deal validation.
* Duplicate order risk.

## Gaps deliberately left open

* No Limit or StopEntry execution.
* No dynamic risk-based sizing.
* No multiple simultaneous positions.
* No multi-instrument execution.
* Limited restart recovery after a fully confirmed position.
* No automatic position adjustments.
* No portfolio-wide exposure management.

---

# 10. Phase 4: Complete the Market-Position Lifecycle

## Objective

Move from “the system can open one trade” to “the system can safely own and reconcile that trade until closure.”

## Expected outcome

Once a position is opened, the application can:

* Reconcile it from IG after restart.
* Verify that its stop and target remain attached.
* Identify whether it was closed by stop, target, manual action, or another broker event.
* Record the final execution outcome.
* Prevent a new trade while exposure remains active.
* Detect an unknown or unexpected broker position.
* Handle an unresolved submission without blindly placing another order.
* Close the position when a configured safety or lifecycle rule requires it.
* Relate the actual trade outcome to the originating decision audit.

The application should remain safe if its in-memory daily-plan state is lost.

## Questions to resolve

* Which broker endpoints or streams are most reliable for reconciliation?
* How frequently should confirmed positions be checked?
* What is the source of truth for stop and target state?
* How should external manual modifications be represented?
* What happens if the broker has a position unknown to the local execution store?
* Should the system adopt, block around, or automatically close unknown positions?
* How are partial closes represented?
* What constitutes terminal completion of an execution intent?
* Should there be an end-of-session flattening rule?
* Which alerts require human attention?

## Verification evidence

Tests should cover:

* Restart with an open confirmed position.
* Restart with an unresolved submitted order.
* Broker position closed by stop.
* Broker position closed by target.
* Position manually closed outside the application.
* Stop or target manually changed.
* Broker position exists but local record is missing.
* Local record exists but broker position does not.
* Protection disappears unexpectedly.
* Compensating close succeeds.
* Compensating close fails.
* No new trade is placed while state remains uncertain.

A controlled demo scenario should prove:

1. The system opens a trade.
2. The worker is stopped.
3. The worker restarts.
4. It rediscovers the position.
5. It does not open a duplicate.
6. It observes or performs closure.
7. It records the final state correctly.

## Gaps addressed

* Volatile in-memory execution state.
* No startup reconciliation.
* No complete confirmation workflow.
* No active-trade lifecycle.
* No execution-report application.
* No detection of external broker changes.
* No recovery from uncertain submissions.

## Gaps deliberately left open

* Working-order lifecycle.
* Advanced stop management.
* Partial-position strategies.
* Multi-position portfolio management.
* Dynamic sizing.

---

# 11. Phase 5: Support Limit and Stop-Entry Working Orders

## Objective

Support AI candidates whose entry method is not an immediate market order.

## Expected outcome

The system can convert supported Limit and StopEntry candidates into broker working orders and manage them until they are:

```text
Filled
Cancelled
Expired
Rejected
Replaced or amended
```

When filled, the working order must transition into a managed protected position without losing its relationship to the original trade intent.

## Required behaviours

* Preserve the candidate entry method.
* Preserve setup expiry.
* Translate setup expiry into appropriate broker order lifetime.
* Confirm the resulting working-order deal ID.
* Attach or establish stop and target protection.
* Cancel the order automatically when the opportunity expires.
* Prevent equivalent working orders from being submitted repeatedly.
* Detect when a working order fills.
* Reconcile a fill after restart.
* Avoid simultaneous duplicate working orders and positions for the same intent.

## Questions to resolve

* Which combinations of order type and time-in-force does IG support for the target instruments?
* Can protection be attached to the working order before fill?
* How is the working-order deal ID obtained reliably?
* How should expiry timestamps map to broker rules?
* What happens when expiry occurs while cancellation is uncertain?
* How should material market movement invalidate an unfilled order?
* Should AI candidates be allowed to update an existing working order?
* When is replacement safer than amendment?

## Verification evidence

Tests should prove:

* Limit and StopEntry semantics are not reversed.
* Expired candidates are never submitted.
* Working-order expiry is enforced.
* Duplicate scans do not produce duplicate orders.
* Cancellation is idempotent.
* Fill transitions to one active managed position.
* Restart recovery works before and after fill.
* A cancelled order cannot later be treated as active.
* Protection is confirmed after fill.

## Gaps addressed

* Current incomplete working-order lifecycle.
* Entry-method information being lost.
* Missing automated order expiry.
* Missing fill-to-position transition.
* Missing working-order confirmation and reconciliation.

## Gaps deliberately left open

* Advanced order replacement strategies.
* Partial fills if unsupported or not yet observed.
* Multiple layered entries.
* Portfolio-level order optimisation.

---

# 12. Phase 6: Broker-Aware Position Sizing and Portfolio Risk

## Objective

Replace the minimum-size canary policy with risk-based sizing grounded in actual broker contract semantics and current account exposure.

## Expected outcome

For each approved trade, the system calculates a broker-valid quantity that respects:

* Configured risk per trade.
* Current account equity or balance.
* Available funds or margin.
* Existing position risk.
* Existing working-order risk.
* Instrument contract or lot size.
* Value per point.
* Account and instrument currency.
* Minimum deal size.
* Quantity increments.
* Minimum and maximum stop distances.
* Instrument-specific margin constraints.
* Maximum aggregate exposure.

The quantity returned must be both financially meaningful and broker-valid.

## Questions to resolve

* Which IG account field should represent sizing equity?
* How should available margin constrain otherwise valid risk sizing?
* How is value per point derived for each instrument type?
* When is currency conversion required?
* Are spread betting and CFD quantity semantics materially different?
* How should existing stop levels contribute to portfolio risk?
* How should positions without stops be treated?
* How should correlated exposure be handled initially?
* Is a hard instrument-level notional cap also required?
* How should rounding affect actual risk?

## Verification evidence

Tests should cover representative instruments with different:

* Lot sizes.
* Units.
* Currencies.
* Minimum quantities.
* Quantity increments.
* Stop-distance rules.
* Account currencies.

Tests should prove that:

* Quantity is always broker-valid.
* Estimated monetary risk remains within tolerance after rounding.
* Unsupported contract semantics fail closed.
* Missing market metadata prevents execution.
* Existing portfolio risk reduces or eliminates available risk.
* Margin constraints can override theoretical sizing.
* The old simplistic `risk ÷ price distance` approach cannot bypass the new calculation.

## Gaps addressed

* Fictitious fixed account-equity context.
* Simplistic position sizing.
* Ignored dealing rules.
* Missing account and instrument currency semantics.
* No portfolio-wide exposure control.
* No working-order risk allocation.

## Gaps deliberately left open

* Sophisticated correlation modelling.
* Volatility-targeted portfolio construction.
* Cross-broker capital allocation.
* Kelly-style optimisation.
* Live-account capital policy.

---

# 13. Phase 7: Data Reliability and Decision-Quality Validation

## Objective

Ensure that execution decisions and retrospective evaluations are based on sufficiently complete and trustworthy data.

The latest evaluation reported:

* 17 candidates.
* 13 data-insufficient candidate outcomes.
* 30 assessment windows with data gaps.
* A displayed average of 0.9 R based on only the small subset with estimable outcomes.

This is interesting but not sufficient evidence of strategy quality.

## Expected outcome

The system can distinguish clearly between:

* No trade opportunity.
* Candidate rejected by strategy.
* Candidate not filled.
* Candidate outcome complete.
* Candidate outcome unavailable due to missing data.
* Market closure.
* Streaming outage.
* Mirror staleness.
* Broker non-tradeable state.
* Evaluation horizon not yet complete.

Operational data gaps should not be mistaken for strategy performance.

## Areas to investigate

* Why streaming subscriptions stopped and re-established repeatedly.
* Why mirrored data became stale during portions of the run.
* Whether the remote collector, local mirror, or both caused the gap.
* How long after candidate expiry an evaluation should be run.
* What data-completeness threshold is needed before performance metrics are reported.
* Whether broker status evidence should supplement missing price bars.
* Whether the 10-minute analysis series and 5-minute evaluation series remain aligned.
* Whether repeated adjacent candidates represent distinct opportunities or repeated expressions of the same setup.
* Whether the strong Buy bias is caused by market conditions, daily-plan bias, or prompt behaviour.

## Expected reporting

Evaluation should eventually report:

* Completed candidate count.
* Data-insufficient count.
* No-fill count.
* Target and stop outcomes.
* Expectancy.
* Average win and loss.
* Spread cost measured in R.
* Maximum favourable excursion.
* Maximum adverse excursion.
* Results by instrument.
* Results by direction.
* Results by entry method.
* Results by time of day.
* Results by daily-plan bias.
* Duplicate or overlapping opportunity rate.

Metrics should not present a headline average without making the evaluated sample size unmistakable.

## Verification evidence

Tests should prove:

* Evaluation waits for the required outcome horizon.
* Missing tail data remains insufficient rather than being guessed.
* Market closure is supported by explicit evidence.
* Same-candle ambiguity remains conservatively resolved.
* No-fill requires data through expiry.
* Directional assessment and candidate outcome completeness are reported separately.
* Summary metrics expose both total and evaluable sample counts.
* Data outages generate operational evidence.

## Gaps addressed

* High proportion of data-insufficient outcomes.
* Incomplete evaluation horizon.
* Potentially misleading headline performance metrics.
* Streaming interruption visibility.
* Mirror staleness visibility.
* Strong unexplained Buy bias.
* Repeated adjacent candidate analysis.

## Gaps deliberately left open

* Proof of profitability.
* Strategy optimisation.
* Automated model or prompt tuning.
* Live capital allocation.

---

# 14. Phase 8: Broader Resilience and Operational Controls

## Objective

Make the demo trader resilient enough to run unattended for extended periods while remaining observable and recoverable.

## Expected outcome

The application can operate for days or weeks in demo mode and provide a reliable answer to:

* What decisions were made?
* What orders were attempted?
* Which orders were accepted?
* What positions currently exist?
* Which positions are protected?
* Which operations remain uncertain?
* Why was a candidate rejected?
* Why did the system stop trading?
* What action is required from an operator?

## Capabilities to introduce as needed

* Health checks for market data, broker connectivity, execution reconciliation, and scheduler activity.
* Alerts for unknown positions.
* Alerts for unprotected positions.
* Alerts for unresolved submissions.
* Alerts for stale market data.
* An operator-visible kill switch.
* Safe pause and resume.
* Controlled account flattening.
* Retention and cleanup of observability artifacts.
* Execution and reconciliation dashboards.
* Rate-limit handling.
* Broker maintenance-window handling.
* Backoff and retry policies tailored to operation safety.
* Explicit degraded operating modes.
* Configuration validation at startup.
* Recovery verification after deployment.

## Questions to resolve

* Which conditions should stop only new entries versus stop the entire worker?
* Which failures require immediate flattening?
* Which failures require human intervention?
* Which alerts need paging rather than logging?
* How long may an order remain uncertain?
* What operational state is required before execution can resume?
* How are configuration changes audited?
* What should deployment validation prove before the new worker is considered healthy?

## Verification evidence

Fault-injection and long-running tests should include:

* Broker API outage.
* Streaming outage.
* GCS mirror outage.
* SQLite lock or disk failure.
* Process crash.
* Host restart.
* Duplicate scheduled jobs.
* Delayed confirmation.
* Rate limiting.
* Unknown external position.
* Missing protection.
* Failed compensating close.
* Configuration changed to an unapproved environment.

The system should produce a predictable safe state in each case.

## Gaps addressed

* Limited operational recovery.
* Limited alerting.
* Limited long-running execution evidence.
* Lack of explicit degraded modes.
* Deployment and startup reconciliation risk.
* Unclear operator intervention paths.

## Gaps deliberately left open

* Live-account enablement.
* Multi-region failover.
* High-availability active-active execution.
* Institutional-grade disaster recovery.
* Fully autonomous remediation of every failure class.

---

# 15. Phase 9: Expand Strategy and Execution Coverage

## Objective

Broaden trading capability only after the narrow demo workflow is reliable and measurable.

Possible later increments include:

* Additional allowlisted instruments.
* Multiple simultaneous positions.
* Multiple new trades per day.
* Instrument-specific decision rules.
* More entry methods.
* Partial closes.
* Stop tightening.
* Break-even transitions.
* Trailing stops.
* Session-specific rules.
* Event-specific trading restrictions.
* Portfolio concentration limits.
* Correlation controls.
* Multiple strategy variants.
* Comparative shadow experiments.

Each should be introduced as an independently testable capability rather than as a large “complete trading engine” project.

## Expected outcome

Every newly supported scenario has:

* A defined domain behaviour.
* A controlled configuration boundary.
* Deterministic tests.
* Broker integration tests where relevant.
* Clear observability.
* A rollback or disable path.
* Comparative evidence that the added complexity is useful.

---

# 16. Consolidated Gap Register

The following identified gaps must remain visible throughout implementation.

## G1. Missing candidate-to-decision bridge

The AI pipeline validates candidates but does not decide whether they should be ignored, queued, or executed.

**Primary phase:** Phase 1.

## G2. Parallel and partially disconnected strategy models

The existing risk-review architecture and current AI candidate architecture do not form one coherent execution flow.

**Primary phase:** Phase 1.

## G3. Candidate information can be lost

Entry method, expiry, stop, target, and other AI-candidate context are not preserved through the older execution abstractions.

**Primary phases:** Phases 1 and 5.

## G4. Fake risk context

The automated host currently assumes fixed equity and no positions or working orders.

**Primary phases:** Phases 3 and 6.

## G5. Simplistic position sizing

The current mathematical sizing does not account for broker contract semantics.

**Primary phase:** Phase 6.

## G6. Market orders do not preserve protection

The current market-order abstraction does not include the AI-proposed stop and target.

**Primary phase:** Phase 3.

## G7. Working-order lifecycle is incomplete

Working orders lack complete protection, confirmation, expiry, fill transition, and lifecycle reconciliation.

**Primary phase:** Phase 5.

## G8. No durable execution idempotency

A retried job or uncertain submission could result in duplicate trades.

**Primary phase:** Phase 2.

## G9. Current journal is insufficient

A recent deal-reference journal is not a durable execution outbox or reconciliation ledger.

**Primary phase:** Phase 2.

## G10. Execution state is volatile

In-memory state cannot safely represent open or unresolved broker activity across restart.

**Primary phase:** Phase 4.

## G11. Confirmation and reconciliation are incomplete

A single immediate order-status lookup is insufficient for asynchronous broker execution.

**Primary phases:** Phases 3 and 4.

## G12. Active trade management is disconnected

Existing active-trade and execution-report concepts are not part of the scheduled broker workflow.

**Primary phase:** Phase 4.

## G13. Demo and live boundaries are implicit

Automated writes require explicit approved-environment and approved-account enforcement.

**Primary phases:** Phases 0 and 3.

## G14. Trading-date interpretation is inconsistent

Some services derive dates from UTC rather than the configured trading timezone.

**Primary phase:** Phase 1.

## G15. Scheduler processing may be duplicated

The execution design must assume at-least-once job delivery.

**Primary phase:** Phase 2.

## G16. Market-data continuity is not yet reliable enough

The latest run contained stale data periods and repeated streaming reconnections.

**Primary phases:** Phases 3 and 7.

The Phase 3 freshness gate must remain fail-closed. The deeper operational cause can be addressed in Phase 7.

## G17. Current evaluation evidence is incomplete

Most candidate outcomes and all market assessments lacked sufficient data.

**Primary phase:** Phase 7.

## G18. Evaluation headline metrics can overstate confidence

Average R must be accompanied by the number of candidates that actually received an estimable outcome.

**Primary phase:** Phase 7.

## G19. Candidate direction is heavily biased toward Buy

The cause and persistence of the bias have not yet been established.

**Primary phase:** Phase 7.

## G20. Repeated adjacent opportunities may not be independent

Similar candidates across consecutive scans may represent one continuing setup rather than multiple distinct opportunities.

**Primary phases:** Phases 1 and 7.

## G21. Broker protection failure behaviour is undefined

If entry succeeds but protection fails, the required compensating and escalation behaviour must be explicit.

**Primary phases:** Phases 3 and 4.

## G22. Unknown external broker activity is not handled

The system does not yet define what to do with positions or orders created or modified outside the application.

**Primary phase:** Phase 4.

---

# 17. Testing Expectations for Every Phase

Every implementation phase should begin with a test and validation plan before production code is changed.

The plan should identify:

1. The behaviour being introduced.
2. The boundaries involved.
3. The deterministic unit tests.
4. The fake-boundary integration tests.
5. The opt-in external integration tests.
6. The failure and retry scenarios.
7. The artifacts that prove completion.
8. The existing behaviours that must not regress.
9. The configuration needed to keep the feature disabled by default.
10. The command or script that another engineer or coding agent can run to verify the result.

A coding agent should be able to answer:

```text
What exact command proves this phase works?
What output should it produce?
What broker or database state should exist afterward?
What negative tests prove it fails safely?
What persisted evidence can be inspected?
```

A phase should not be considered complete when only the happy path passes.

At minimum, each externally visible workflow should test:

* Success.
* Rejection.
* Duplicate invocation.
* Timeout.
* Restart.
* Invalid configuration.
* Stale input.
* Existing conflicting state.
* External dependency failure.
* Recovery or safe termination.

---

# 18. Recommended Immediate Work Sequence

## Immediate task 1

Run and document the existing manual IG demo lifecycle.

Do not change the AI pipeline yet.

## Immediate task 2

Introduce explicit execution modes, with Disabled as the default.

## Immediate task 3

Implement the deterministic shadow decision and execution-intent model.

Run it for several scheduled scans and inspect its decisions.

## Immediate task 4

Implement durable decision reservation and duplicate suppression.

Prove that repeated scheduler processing cannot produce repeated submissions.

## Immediate task 5

Research and implement the protected market-position path.

This includes resolving whether protection can be established atomically and what happens when it cannot.

## Immediate task 6

Enable the one-instrument, minimum-size, one-trade-per-day demo canary.

## Immediate task 7

Prove restart reconciliation and controlled closure.

## Immediate task 8

Allow the demo trader to run in the narrow mode long enough to collect meaningful execution and outcome data.

Only after this should working orders, dynamic sizing, multiple instruments, and broader resilience be prioritised.

---

# 19. First Automated Demo Milestone

The first major roadmap milestone is complete when the following statement is demonstrably true:

> From a scheduled AI scan, the system can select one valid market-entry candidate, produce a deterministic and durable execution intent, verify the approved IG demo environment and current broker exposure, place one minimum-size protected position, confirm the broker state, survive duplicate job delivery without placing a second order, close or observe closure of the position, and retain complete evidence linking the original AI decision to the final broker outcome.

This milestone deliberately does not require:

* Limit orders.
* Stop-entry orders.
* Dynamic position sizing.
* Multiple simultaneous trades.
* Multiple instruments.
* Advanced stop management.
* Proven strategy profitability.
* Live-account capability.

Those are later increments.

---

# 20. Long-Term Completion Direction

The roadmap is progressing correctly when each new phase improves one of these properties:

## Capability

The system can represent and execute more legitimate trading scenarios.

## Safety

The system prevents invalid, duplicate, unprotected, or unintended orders.

## Recoverability

The system can determine the truth after restart, timeout, or partial failure.

## Observability

Every decision and broker action can be reconstructed.

## Validity

Performance conclusions are based on complete and correctly interpreted data.

## Testability

A developer or coding agent can independently prove that the stated phase outcome has been achieved.

The priority order should remain:

```text
Correct narrow execution
before broad execution

Durable idempotency
before unattended execution

Broker reconciliation
before local assumptions

Complete evidence
before strategy optimisation

Demo reliability
before any consideration of live trading
```
