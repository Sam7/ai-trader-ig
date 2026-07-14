# AI Trader IG: Priority Roadmap for Better Trading Decisions

## 1. Purpose

This document distils the highest-value ideas from the repository's day-trading research into a small, ordered implementation roadmap.

It is deliberately narrower than the source material. It does not attempt to recreate an institutional trading desk, add every professional indicator, or redesign the existing strategy. It identifies only the improvements most likely to make the current AI-driven intraday workflow safer, more deterministic, easier to evaluate, and more useful before broader strategy expansion.

This roadmap complements the existing [start-trading plan](01-start-trading-plan.md). The start-trading plan answers:

> How do we reach narrow, safe, automated IG demo execution?

This document answers:

> What small set of trading-context improvements should make those decisions more trustworthy and more measurable?

The first meaningful milestone is:

> Every intraday candidate is evaluated using a deterministic, auditable snapshot of data quality, volatility, session, event, price-range, and execution-cost context; ineligible conditions are rejected before execution; and later performance can be segmented by the exact conditions under which the decision was made.

The source research is:

* [Day Trading Masterclass Guide](../../research/day-trading/Day%20Trading%20Masterclass%20Guide.md)
* [Elite Commodity Day Trader Routine](../../research/day-trading/Elite%20Commodity%20Day%20Trader%20Routine.md)
* [Veteran Commodity Day Trading Insights](../../research/day-trading/Veteran%20Commodity%20Day%20Trading%20Insights.md)

---

# 2. Core Conclusion

The most useful lesson from the research is not a particular indicator or entry pattern.

It is this division of responsibility:

```text
Deterministic code establishes the facts
    ↓
The AI interprets an opportunity inside those facts
    ↓
Deterministic policy decides whether the opportunity is eligible
    ↓
The evaluator measures what actually happened
```

The AI should not be required to estimate from a chart alone:

* Whether the data is complete.
* Whether the latest bar is stale.
* Whether volatility is unusually high or low.
* How much of the normal daily range is already consumed.
* Whether the market is in an active or illiquid session.
* Whether the system is before, inside, or immediately after a high-impact event.
* Whether spread and expected slippage consume too much of the trade's proposed risk.
* Whether a candidate is a genuinely new setup or a repeated version of the same thesis.

Those facts should be calculated once, persisted, reused by the AI review, reused by deterministic decision policy, and reused by paper evaluation.

---

# 3. Current State

The repository already has a strong base:

* Broker-neutral bid and ask OHLC bars.
* Five-minute streamed candle collection.
* Historical repair and data-gap handling.
* Daily AI briefing and a focused watchlist.
* Intraday chart preparation and AI candidate generation.
* Deterministic shadow-decision checks.
* Quote freshness checks.
* Independent reward/risk recalculation.
* Spread-relative-to-risk checks.
* A pre-event high-impact block.
* Immutable decision-audit artifacts.
* Bid/ask-aware retrospective outcome evaluation.
* Durable execution reservations and duplicate submission protection.

The central decision-quality gaps are narrower:

```text
Stored market data
    ↓
No single canonical quality/context snapshot
    ↓
AI infers too much from the chart
    ↓
Decision policy sees only part of the market state
    ↓
Evaluation cannot cleanly explain which conditions helped or hurt
```

There are also several specific concerns:

1. The IG streaming field `CONS_TICK_COUNT` is currently mapped into a generic `PriceBar.Volume` property, even though it is a consolidated tick count rather than guaranteed exchange-traded volume.
2. Data freshness is checked, but there is not yet one first-class quality result combining expected bars, present bars, missing runs, latest final-bar age, and usability.
3. Volatility and range context are not represented as one reusable domain snapshot.
4. Session state is not yet an explicit strategy input.
5. High-impact events are blocked before release, but release shock and post-event stabilisation are not modelled as separate states.
6. Exact decision identifiers may not be sufficient to recognise slightly changing versions of the same continuing setup.
7. Evaluation can model bid and ask outcomes, but it does not yet fully separate forecast quality from entry cost, slippage, and net realised edge.

---

# 4. Delivery Principles

## 4.1 Reliable data before richer strategy

Do not add sophisticated indicators to unreliable or semantically ambiguous data.

A simple ATR calculated from complete, correctly interpreted bars is more useful than a complex volume-profile feature calculated from a field that is not genuine traded volume.

## 4.2 Calculate facts once

A volatility, session, event, quality, or cost metric should have one canonical definition.

Do not calculate one version for the prompt, a different version in shadow decision policy, and a third version in retrospective evaluation.

## 4.3 Facts remain deterministic; interpretation may be probabilistic

Deterministic code should calculate values such as:

* ATR.
* Daily range consumed.
* Current spread.
* Latest final-bar age.
* Missing-bar counts.
* Session state.
* Minutes before or after an event.
* Exact and setup-level identities.

The AI may interpret whether those facts support a trade, but it should not invent them.

## 4.4 Reject uncertainty rather than silently approximating it

Fail closed when:

* Required bars are missing.
* The latest final bar is stale.
* the trading session cannot be resolved confidently.
* Broker status conflicts with local session assumptions.
* Event timing is ambiguous.
* A required cost input is unavailable.
* Volume semantics do not support the requested calculation.

## 4.5 Prefer context over more entry patterns

The immediate goal is not to add VWAP fades, opening-range breakouts, order-flow imbalance, or other new strategies.

The goal is to determine whether the current candidate occurred in conditions where it was sensible, executable, and measurable.

## 4.6 Preserve raw inputs and derived context

Every derived value should remain traceable to:

* Instrument.
* Resolution.
* Price window.
* Session definition.
* Trading timezone.
* Event timestamp.
* Quote timestamp.
* Configuration values.
* Calculation version where later changes could affect reproducibility.

## 4.7 Do not let this roadmap delay the narrow demo canary unnecessarily

These improvements should be introduced in small parcels around the existing execution roadmap.

The minimum demo canary does not need every later analytical feature. It does need fail-closed data freshness, unambiguous market-data semantics, and enough context to explain why a trade was allowed.

---

# 5. Explicit Non-Goals

The following ideas from the research should not be implemented as part of this roadmap:

* Level 2 order-book analysis.
* Footprint charts.
* Iceberg-order detection.
* Direct Market Access workflows.
* Smart-order routing.
* Exchange-style Volume Profile using IG tick-count data.
* VWAP presented as true traded-volume fair value without compatible volume data.
* Options-implied volatility.
* Weather nowcasting.
* LME warehouse and warrant modelling.
* Calendar-spread or crack-spread execution.
* Wholesale-client leverage optimisation.
* Aggressive ATR-based position sizing before broker-aware sizing exists.
* Automatic breakeven or trailing-stop rules without comparative evidence.

These may become future research items. They are not prerequisites for trustworthy early demo trading.

---

# 6. Priority Order

The implementation order should remain:

```text
P0  Trustworthy market-data semantics and quality
P1  Deterministic market-context snapshot
P2  Explicit session and event eligibility
P3  Setup-level identity and repeated-thesis suppression
P4  Realistic execution-cost and fill evaluation
P5  Decision-quality reporting and evidence thresholds
P6  Structured commodity fundamentals in the daily plan
```

P0 through P2 are the immediate priorities.

P3 and P4 should follow as the shadow and demo workflows gather repeated candidates and actual broker evidence.

P5 is required before drawing strategy conclusions.

P6 should begin only after the preceding evidence path is stable.

---

# 7. Priority 0: Trustworthy Market-Data Semantics and Quality

## Objective

Make it impossible for later strategy code to mistake ambiguous, stale, or incomplete data for reliable market evidence.

## Expected outcome

Every price series used for AI review, deterministic approval, or evaluation has an explicit quality assessment.

The system can answer:

```text
What data was expected?
What data was present?
What data was missing?
How old is the latest final bar?
Was the market reported open, closed, or unknown?
Is this series usable for this specific purpose?
What does the stored volume-like field actually mean?
```

## 7.1 Correct the meaning of volume

The current streaming candle maps IG's `CONS_TICK_COUNT` into `PriceBar.Volume`.

This should not remain an unqualified generic volume field.

Choose the smallest design that makes the semantics explicit. Reasonable options include:

```csharp
public sealed record PriceBar(
    DateTimeOffset TimestampUtc,
    decimal BidOpen,
    decimal BidHigh,
    decimal BidLow,
    decimal BidClose,
    decimal AskOpen,
    decimal AskHigh,
    decimal AskLow,
    decimal AskClose,
    long? ConsolidatedTickCount);
```

or:

```csharp
public enum VolumeKind
{
    Unavailable,
    TickCount,
    LastTradedVolume,
    IncrementalTradedVolume,
    ExchangeVolume,
}

public sealed record MarketActivity(
    decimal? Value,
    VolumeKind Kind);
```

The exact shape should be selected based on current consumers and migration cost.

The required behaviour is:

* Existing IG `CONS_TICK_COUNT` data is labelled as tick count.
* No code may silently treat tick count as exchange-traded volume.
* Any future VWAP or Volume Profile calculation must declare the volume semantics it requires.
* Unsupported calculations fail explicitly rather than producing misleading output.

## 7.2 Introduce one first-class market-data quality result

A minimum quality model should capture:

```text
MarketDataQuality
├── Instrument
├── Resolution
├── WindowStartUtc
├── WindowEndUtc
├── ExpectedFinalBarCount
├── PresentFinalBarCount
├── MissingFinalBarCount
├── MaximumConsecutiveMissingBars
├── MissingBarRatio
├── LatestFinalBarUtc
├── LatestFinalBarAge
├── BrokerMarketStatus
├── QualityGrade
└── IsUsable
```

The first implementation does not need a sophisticated scoring model. A small enum is sufficient:

```csharp
public enum MarketDataQualityGrade
{
    Unusable,
    Degraded,
    Complete,
}
```

The important requirement is that `IsUsable` is resolved for a named purpose rather than assumed globally.

For example:

```text
Usable for chart display
Usable for market assessment
Usable for candidate replay
Usable for execution approval
```

Candidate replay should remain stricter than high-level assessment because missing interior bars can change whether stop or target was reached first.

## Questions to resolve

* Which component owns expected-bar calculation?
* How is broker-closed evidence distinguished from missing price data?
* Which gaps can be tolerated for context calculation?
* Which gaps must make candidate replay unestimable?
* Should quality thresholds be global or resolution-specific?
* How should daylight-saving and exceptional market sessions affect expected bars?
* How should forming candles be excluded from final-bar completeness?

## Verification evidence

Tests should prove:

* `CONS_TICK_COUNT` cannot be consumed as exchange volume.
* The expected number of five-minute bars is correct for a known open interval.
* Broker-closed periods are not reported as unexplained missing bars when authoritative closure evidence exists.
* Unexplained missing bars remain missing rather than being forward-filled.
* The latest final-bar age is calculated from the correct timestamp.
* Forming candles do not satisfy final-bar completeness.
* A stale series is rejected for execution approval.
* Candidate replay remains unestimable when an unsafe gap occurs before the outcome is known.
* A decided outcome before a later gap remains estimable where existing evaluation policy allows it.

A controlled report should show quality results per instrument and resolution for at least one complete and one intentionally damaged window.

## Gaps addressed

* Ambiguous volume semantics.
* Overconfident use of incomplete data.
* Inconsistent quality rules across prompt preparation, decision policy, and evaluation.
* Difficulty explaining why an outcome was marked data-insufficient.

## Gaps deliberately left open

* No new trading signal.
* No Volume Profile.
* No VWAP strategy.
* No exchange feed.
* No attempt to infer missing prices.

---

# 8. Priority 1: Deterministic Market-Context Snapshot

## Objective

Provide the AI reviewer and deterministic decision policy with the same compact, reproducible description of the current market environment.

## Expected outcome

Each reviewed instrument receives a `MarketContextSnapshot` calculated from the approved price window and current executable quote.

The first version should contain only high-value fields:

```text
MarketContextSnapshot
├── Instrument
├── Resolution
├── CalculatedAtUtc
├── LatestFinalBarUtc
├── Atr14
├── AtrPercentile
├── Adr14
├── CurrentSessionRange
├── DailyRangeConsumedPercent
├── DistanceFromSessionHighInAtr
├── DistanceFromSessionLowInAtr
├── CurrentSpread
├── SpreadToAtrRatio
├── DataQuality
└── CalculationVersion
```

Not every field must be delivered in one change.

The minimum useful parcel is:

```text
Atr14
Adr14
DailyRangeConsumedPercent
CurrentSpread
SpreadToAtrRatio
LatestFinalBarAge
DataQualityGrade
```

## 8.1 ATR is context before it is a stop rule

ATR should initially be used to answer:

* Is current movement large or small relative to recent movement?
* Is the current spread unusually expensive relative to normal bar movement?
* Is the candidate stop tiny compared with current noise?
* Is the market already in an unusually volatile regime?

Do not initially use ATR to override the AI's stop or determine position size automatically.

Those decisions require separate evidence and, for position sizing, broker contract semantics.

## 8.2 ADR and daily-range consumption are exhaustion context

The system should distinguish between:

* A market that has moved only a small part of its normal daily range.
* A market that has already consumed most of its normal daily range.
* A market whose current range is abnormally large.

This should be context, not a universal rejection rule.

For example, high daily-range consumption may weaken a late breakout candidate but may be entirely consistent with an event-driven trend day.

## 8.3 Use executable prices where cost matters

Volatility measures may use a documented mid, bid, ask, or representative-price convention.

Execution checks must continue using the correct executable side:

* Buy entry against ask.
* Sell entry against bid.
* Long stop/target replay against the side that would actually close the position.
* Short stop/target replay against the corresponding executable side.

The chosen conventions should be explicit and covered by tests.

## 8.4 Reuse the same snapshot everywhere

The snapshot should be:

1. Included in the intraday AI review request.
2. Persisted in the decision audit.
3. Passed to deterministic shadow or demo eligibility policy.
4. Available to retrospective evaluation and reporting.

Do not recalculate the historical context later from a different or expanded data window unless the evaluator explicitly labels the result as reconstructed rather than original decision-time context.

## Questions to resolve

* Which project owns broker-neutral market-context calculation?
* What price convention should ATR and ADR use?
* What constitutes a trading day for instruments with nearly continuous sessions?
* Which timezone defines the daily range?
* How much history is required before the snapshot is valid?
* How is percentile context calculated without introducing unnecessary persistence complexity?
* Which fields belong in the first implementation and which remain deferred?

## Verification evidence

Tests should prove:

* ATR is correct for deterministic example bars.
* ATR handles price gaps correctly.
* ATR does not include forming bars.
* ADR uses the intended session boundaries.
* Daily-range consumption is correct around the trading-day boundary.
* Spread-to-ATR is rejected when ATR is unavailable or zero.
* The same inputs always produce the same snapshot.
* The AI payload, audit record, decision policy, and evaluator reference the same snapshot values.
* Changing chart rendering does not change the deterministic context.

A controlled run should save the market-context snapshot beside the chart and candidate response for each reviewed market.

## Gaps addressed

* Excessive reliance on visual LLM estimation.
* Lack of reusable volatility context.
* Lack of range-exhaustion context.
* Inability to segment later results by decision-time volatility.

## Gaps deliberately left open

* No automatic ATR stop placement.
* No ATR position sizing.
* No new entry strategy.
* No claim that a particular ATR or ADR threshold is profitable.

---

# 9. Priority 2: Explicit Session and Event Eligibility

## Objective

Stop treating every open-market minute as strategically equivalent.

## Expected outcome

Every intraday review has an explicit session state and event state, and policy can decide whether that state is blocked, observe-only, or eligible.

Keep descriptive state separate from trading policy.

For example:

```csharp
public enum TradingSessionState
{
    Closed,
    Maintenance,
    Illiquid,
    OpeningTransition,
    Active,
    ClosingTransition,
    Unknown,
}

public enum TradingEligibility
{
    Blocked,
    ObserveOnly,
    Eligible,
}
```

## 9.1 Session state

Session resolution should combine:

1. Broker-reported market status where available.
2. Instrument-specific configured session definitions.
3. IANA timezone rules rather than fixed UTC assumptions.
4. Explicit exceptional-session or holiday handling where needed.

The initial policy should remain simple:

* `Closed`, `Maintenance`, and `Unknown` are blocked.
* `Illiquid` is blocked or observe-only.
* `OpeningTransition` and `ClosingTransition` are configurable.
* `Active` is eligible subject to all other gates.

Do not encode claims such as "the first hour is always best" as universal truth. Record the state and let later evidence determine whether a session deserves different policy.

## 9.2 Event lifecycle

Replace a single pre-event block with an explicit lifecycle:

```text
Normal
→ PreEventBlocked
→ ReleaseShockBlocked
→ PostEventObservation
→ PostEventEligible
→ Normal
```

A minimum event model should capture:

```text
EventRiskState
├── EventId
├── ScheduledAtUtc
├── Impact
├── AffectedInstruments
├── MinutesUntilEvent
├── MinutesSinceEvent
├── LifecycleState
└── Eligibility
```

The timing values should be configuration, not hard-coded universal rules.

A conservative first policy might distinguish:

* Before a high-impact event: block new entries.
* At and immediately after release: block entries based on stale pre-release context.
* During a short observation period: continue collecting bars and spread evidence, but do not trade.
* After observation: permit a newly prepared post-event review if data and spread have stabilised.

The important change is not the exact number of minutes. It is that pre-release, release shock, and post-release trading are no longer treated as the same state.

## 9.3 Post-event review must use post-event evidence

A candidate generated before a release should not become executable afterward merely because its expiry has not elapsed.

The system should require a newly prepared context and quote after the event when the release may have invalidated the previous chart structure.

## Questions to resolve

* Which broker market-status values map to each session state?
* Which instruments require custom session definitions?
* How should the system handle broker-open status during strategically illiquid hours?
* Which events affect multiple correlated instruments?
* What constitutes spread and volatility stabilisation after an event?
* Should post-event eligibility require a minimum number of final bars?
* How are unscheduled breaking events represented?

## Verification evidence

Tests should prove:

* Session boundaries work across daylight-saving transitions.
* Broker-closed status blocks execution even when local configuration says active.
* Unknown session state fails closed.
* A candidate is blocked before a configured high-impact event.
* A pre-event candidate cannot be submitted after the release without fresh review.
* Release-shock and post-event-observation states are distinct.
* Post-event eligibility begins only after the configured conditions are met.
* The exact session and event state are saved in the decision audit.

A controlled shadow run should report candidates grouped by session and event lifecycle state, including those rejected before an LLM call and those rejected after review.

## Gaps addressed

* Overly simple event blocking.
* No distinction between liquid and illiquid open-market periods.
* Risk of executing stale pre-event analysis after a release.
* Lack of later evidence about which sessions generated useful candidates.

## Gaps deliberately left open

* No universal opening-range strategy.
* No assumption that post-event continuation or reversal is superior.
* No automated news-surprise interpretation beyond the existing daily and intraday AI responsibilities.

---

# 10. Priority 3: Setup-Level Identity and Repeated-Thesis Suppression

## Objective

Distinguish a genuinely new opportunity from a slightly changed rendering of the same continuing setup.

## Expected outcome

Each candidate has two identities:

```text
Exact decision identity
    = the immutable candidate as actually reviewed

Setup identity
    = the broader trading thesis or opportunity cluster
```

The exact identity preserves auditability.

The setup identity supports suppression, grouping, and evaluation.

## Suggested setup fingerprint inputs

A setup fingerprint may include:

```text
Trading date
+ instrument
+ direction
+ daily-plan scenario or catalyst
+ entry method
+ configured time bucket
+ quantised entry zone
```

The implementation should avoid overfitting the fingerprint to one current prompt shape.

The objective is to recognise cases such as:

```text
10:00  Buy at 72.10, stop 71.70, target 72.95
10:15  Buy at 72.14, stop 71.74, target 73.00
10:30  Buy at 72.08, stop 71.68, target 72.92
```

These may be three exact decisions but one continuing thesis.

## Required behaviour

The system should be able to report:

* New setup.
* Refresh of an existing setup.
* Materially changed setup.
* Opposite-direction replacement.
* Setup already executed.
* Setup expired.

For the first demo milestone, one-new-trade-per-trading-day should remain an explicit execution-policy rule rather than an accidental consequence of scan-level selection.

## Questions to resolve

* Which changes make a setup materially new?
* How should entry zones be quantised across instruments with different point sizes?
* How long does a setup remain active?
* Does a major event automatically end the previous setup identity?
* How should direction reversal be represented?
* Should repeated refreshes update one setup record or append immutable child decisions?

## Verification evidence

Tests should prove:

* Identical candidates map to the same exact and setup identities.
* Small price changes can preserve setup identity while exact identity changes.
* A materially different catalyst or direction produces a new setup.
* An event boundary can invalidate a pre-event setup where policy requires it.
* Repeated scheduled scans do not create repeated executable intents for one setup.
* One-new-trade-per-day is enforced across restart.
* Reports can count exact candidates separately from independent setups.

## Gaps addressed

* Inflated candidate counts from adjacent scans.
* Duplicate opportunities that differ only by minor model output changes.
* Unclear denominators in performance reporting.
* Accidental repeated execution of one continuing thesis.

## Gaps deliberately left open

* No portfolio-level correlation grouping.
* No semantic embedding system for setup identity.
* No machine-learned clustering.

---

# 11. Priority 4: Realistic Execution-Cost and Fill Evaluation

## Objective

Separate forecast quality from the economic quality of the executable trade.

## Expected outcome

Evaluation records both the theoretical setup and a conservative approximation of executable results.

A minimum execution-cost record should include:

```text
DecisionMid
DecisionBid
DecisionAsk
ExpectedEntry
ExecutableEntry
SubmissionQuoteTimeUtc
EntrySlippagePoints
SpreadCostR
SlippageCostR
CommissionCostR
GuaranteedStopPremiumR
GrossOutcomeR
NetOutcomeR
```

Only fields supported by the instrument and broker path need to be populated.

Unknown values should remain unknown rather than defaulting to zero.

## 11.1 Preserve decision-time and submission-time prices

For demo execution, save:

* The quote used to approve the candidate.
* The latest quote immediately before submission.
* The confirmed broker fill where available.
* Submission and confirmation timestamps.

This makes it possible to distinguish:

* Model delay.
* Queue or scheduling delay.
* Price movement before submission.
* Broker slippage.
* Spread expansion.

## 11.2 Report gross and net R

A candidate may have been directionally correct but economically poor after cost.

Reports should show both:

```text
Gross R before execution costs
Net R after spread, slippage, commission, and supported premiums
```

Do not compare strategies using gross R where meaningful costs are known.

## 11.3 Use conservative simulation assumptions

Where a broker fill is unavailable, the evaluator should use an explicit configured assumption rather than an ideal midpoint fill.

The assumption should be visible in the artifact and versioned when changed.

## Questions to resolve

* Which execution costs apply to each current IG instrument?
* How should market-order slippage be simulated before enough demo fill evidence exists?
* How are guaranteed-stop premiums represented?
* How should rejected or partially confirmed submissions appear in performance reporting?
* Which costs are known at decision time and which are known only afterward?
* How should overnight funding remain excluded from strict intraday trades but represented if a position crosses rollover?

## Verification evidence

Tests should prove:

* Buy and sell entries use the correct executable quote side.
* Gross and net R differ by the exact configured costs.
* Unknown commission is not silently treated as zero.
* Slippage assumptions are included in persisted evidence.
* A favourable price move between decision and submission is not assumed unless the fill confirms it.
* Same-bar stop and target ambiguity remains conservative.
* Broker-confirmed demo fills override simulation assumptions for actual executed trades.

A report should compare theoretical midpoint, executable spread-aware, and net-cost results for the same candidate set.

## Gaps addressed

* Overstated paper performance.
* Inability to distinguish poor forecasting from poor execution.
* Missing evidence about decision-to-submission movement.
* Inconsistent cost assumptions across instruments.

## Gaps deliberately left open

* No sophisticated market-impact model.
* No order-book queue simulation.
* No smart-order-routing model.
* No high-frequency execution optimisation.

---

# 12. Priority 5: Decision-Quality Reporting and Evidence Thresholds

## Objective

Make later optimisation depend on complete, segmented evidence rather than a single headline average.

## Expected outcome

The audit evaluator can report results by:

* Instrument.
* Direction.
* Independent setup rather than only exact candidate.
* Trading session.
* Event lifecycle state.
* Volatility regime.
* Daily-range-consumed bucket.
* Spread-to-risk bucket.
* Spread-to-ATR bucket.
* Data-quality grade.
* Gross R.
* Net R.
* Estimable versus data-insufficient outcome.

Every performance metric should display its denominator.

For example:

```text
Average net R: +0.34
Estimable setups: 11
Total independent setups: 37
Coverage: 29.7%
```

The average should never be displayed without the coverage that made it possible.

## Required reporting distinctions

The system should distinguish:

```text
Candidate count
Independent setup count
Approved intent count
Submitted order count
Confirmed fill count
Estimable outcome count
Completed lifecycle count
```

These are not interchangeable denominators.

## Evidence threshold before strategy changes

Do not introduce a new trading rule solely because a small retrospective sample appears favourable.

Before promoting an observed relationship into policy, require:

1. A predefined hypothesis.
2. A meaningful independent-setup sample.
3. Sufficient outcome coverage.
4. Cost-aware results.
5. Results that are not driven by one instrument or one day.
6. A comparison against the current baseline.
7. A shadow period before demo execution policy changes.

The exact statistical threshold can be decided later. The workflow requirement should exist now.

## Questions to resolve

* What is the minimum independent-setup count before displaying comparative statistics?
* What coverage is required before a report is considered decision-useful?
* How should correlated setups across instruments be disclosed?
* Which fields should be treated as exploratory rather than policy-ready?
* How are calculation-version changes handled in longitudinal reports?

## Verification evidence

Tests should prove:

* Every metric includes the correct denominator.
* Exact candidates and independent setups are counted separately.
* Data-insufficient outcomes remain visible.
* Net and gross results are not mixed.
* Grouped reports reproduce the totals of the underlying records.
* A report with insufficient coverage is clearly marked inconclusive.
* A calculation-version change is visible rather than silently rewriting history.

## Gaps addressed

* Headline metrics that overstate confidence.
* Repeated candidates being treated as independent evidence.
* Lack of visibility into session, event, volatility, and cost effects.
* Premature optimisation from incomplete samples.

## Gaps deliberately left open

* No automated strategy optimiser.
* No reinforcement-learning loop.
* No automatic threshold tuning from the same evaluation sample.

---

# 13. Priority 6: Structured Commodity Fundamentals in the Daily Plan

## Objective

Add commodity-specific context only where it can be represented as structured, timestamped evidence and evaluated later.

This is a later priority.

It should not block the market-context, event-state, execution, or evaluation work above.

## Expected outcome

The daily briefing may eventually receive a structured context such as:

```text
FundamentalMarketContext
├── DataAsOfUtc
├── ScheduledReport
├── InventoryActual
├── InventoryExpected
├── InventorySurprise
├── PreviousRevision
├── CurveState
├── CurveChange
├── PositioningPercentile
├── PositioningWeeklyChange
└── MajorSupplyRisk
```

For agricultural markets, an additional model may include:

```text
AgriculturalContext
├── StocksToUse
├── StocksToUseChange
├── YieldRevision
├── ProductionRevision
├── ExportRevision
├── OldCropNewCropSpread
└── WeatherRiskState
```

## Required boundaries

These inputs should initially influence:

* Daily market ranking.
* Market regime.
* Important scenarios.
* Scheduled-event awareness.
* Whether an instrument deserves attention.

They should not initially create direct execution signals.

The progression should be:

```text
Collect
→ Timestamp
→ Persist raw and normalised values
→ Include in daily plan
→ Save with decision evidence
→ Evaluate association with outcomes
→ Consider policy only after evidence
```

## Reaction to news is a later evaluable feature

One useful research insight is that price reaction may be more informative than the headline alone.

A future structured field might compare:

```text
Expected directional implication
versus
Observed post-release price and spread response
```

This should be evaluated as a feature, not encoded immediately as a universal rule.

## Gaps deliberately left open

* No automated futures-curve trading.
* No physical-market execution.
* No direct COT-based entry rule.
* No weather-driven automated trade.
* No LLM-only extraction where a stable structured source is available.

---

# 14. Consolidated Gap Register

## G1. Volume-like data has ambiguous semantics

`CONS_TICK_COUNT` is represented by a generic `Volume` property and could be misused as traded volume.

**Primary priority:** P0.

## G2. Market-data quality is distributed rather than canonical

Freshness, missing bars, broker closure, and purpose-specific usability are not represented by one reusable result.

**Primary priority:** P0.

## G3. The AI must infer important context visually

Volatility, range consumption, and spread relative to normal movement are not supplied as one deterministic snapshot.

**Primary priority:** P1.

## G4. Session state is not explicit

The system does not yet distinguish active, transitional, illiquid, maintenance, and unknown states as first-class decision inputs.

**Primary priority:** P2.

## G5. Event handling is primarily pre-release

The current block does not fully represent release shock, post-event observation, and fresh post-event eligibility.

**Primary priority:** P2.

## G6. Exact candidate identity may overcount one setup

Small price changes across adjacent scans can make one continuing thesis appear to be several independent opportunities.

**Primary priority:** P3.

## G7. One-trade-per-day must be explicit policy

Scan-level selection and exact duplicate suppression are not substitutes for a durable daily execution cap.

**Primary priority:** P3 and the start-trading plan's execution phases.

## G8. Forecast and execution quality are not fully separated

Spread-aware replay exists, but decision-to-submission movement, slippage, broker fill, and net cost require clearer evidence.

**Primary priority:** P4.

## G9. Headline performance can hide weak coverage

Metrics need independent-setup denominators, data coverage, and condition-level segmentation.

**Primary priority:** P5.

## G10. Commodity fundamentals are not structured decision evidence

Useful supply, inventory, curve, and positioning context remains mostly research material rather than timestamped evaluable input.

**Primary priority:** P6.

---

# 15. Testing Expectations for Every Priority

Each implementation parcel should begin with a test and validation plan.

The plan should identify:

1. The exact behaviour being added.
2. The canonical inputs and outputs.
3. The source of timestamps and timezones.
4. The deterministic unit tests.
5. The persistence and serialization tests.
6. The fake-boundary integration tests.
7. The opt-in broker or external-data tests where relevant.
8. The failure and ambiguity cases.
9. The artifact that proves completion.
10. The command another engineer or coding agent can run to verify the result.

A coding agent should be able to answer:

```text
Which exact facts are being calculated?
Which source data produced them?
What makes the result usable or unusable?
Where is the result persisted?
Which later components consume the same result?
What negative test proves the system fails closed?
```

At minimum, every context or eligibility feature should test:

* Complete data.
* Missing data.
* Stale data.
* Forming versus final bars.
* Timezone boundary.
* Daylight-saving transition.
* Closed market.
* Unknown broker status.
* Duplicate invocation.
* Configuration change.
* Serialization round trip.
* Historical reconstruction versus original decision-time evidence.

---

# 16. Recommended Immediate Work Sequence

## Immediate task 1

Correct or explicitly qualify the `PriceBar.Volume` semantics.

Do not build VWAP or Volume Profile first.

## Immediate task 2

Introduce a canonical `MarketDataQuality` result and use it in intraday preparation and audit evaluation.

Keep candidate replay stricter than general market assessment.

## Immediate task 3

Implement the minimum `MarketContextSnapshot`:

```text
ATR
ADR
Daily range consumed
Spread-to-ATR
Latest final-bar age
Data-quality grade
```

## Immediate task 4

Include the exact snapshot in:

* The intraday AI request.
* The decision audit.
* The deterministic shadow decision.
* The evaluator input.

## Immediate task 5

Add explicit session state and a conservative eligibility policy.

Use broker status and timezone-aware instrument sessions. Fail closed on unknown state.

## Immediate task 6

Expand the existing high-impact event check into pre-event, release-shock, observation, and post-event-eligible states.

Require fresh post-event context before a pre-event candidate can become executable.

## Immediate task 7

Add setup-level identity and durable one-new-trade-per-day enforcement.

Preserve immutable exact decisions beneath the broader setup.

## Immediate task 8

Add decision-time, submission-time, and broker-fill evidence, then report gross and net R.

## Immediate task 9

Add segmented reporting only after the preceding fields are present and stable.

Do not tune strategy rules from incomplete or repeated-candidate samples.

## Immediate task 10

Only after P0 through P5 are producing dependable evidence, begin one narrowly scoped structured commodity-data input for the daily plan.

---

# 17. First Decision-Quality Milestone

This roadmap's first major milestone is complete when the following statement is demonstrably true:

> For every intraday market review, the system can prove that the source bars are semantically understood, sufficiently complete, and fresh; calculate one deterministic market-context snapshot; resolve the current session and event lifecycle state; reject ineligible conditions before execution; preserve exact and setup-level identity; and retain enough evidence to evaluate gross and net outcomes without treating repeated candidates or data-insufficient results as independent proof.

This milestone deliberately does not require:

* A new entry strategy.
* VWAP or Volume Profile.
* Level 2 data.
* Dynamic ATR sizing.
* Multiple simultaneous positions.
* Automated stop movement.
* Commodity-fundamental execution signals.
* Proven profitability.
* Live-account capability.

---

# 18. Long-Term Direction

The roadmap is progressing correctly when each increment improves one of these properties:

## Data integrity

The system knows what each field means and refuses unsupported interpretations.

## Context quality

The AI and deterministic policy receive the same measurable market facts.

## Eligibility

The system knows when not to trade, not merely what it might trade.

## Independence

Repeated scans of one thesis do not masquerade as independent evidence.

## Economic realism

Performance includes the cost and timing of actual execution.

## Explainability

Every decision can be reconstructed from original decision-time evidence.

## Validity

Strategy changes are based on complete, segmented, cost-aware evidence.

The priority order should remain:

```text
Trustworthy data
before richer indicators

Deterministic context
before AI interpretation

Eligibility gates
before broader execution

Independent setups
before performance claims

Net outcomes
before optimisation

Stable evidence
before commodity complexity
```
