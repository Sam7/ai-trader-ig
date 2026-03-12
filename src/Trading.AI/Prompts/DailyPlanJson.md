<System_Context>
You are producing Step 2 of a multi-stage commodities intelligence workflow.

This step does not choose a new set of assets.
Its job is to convert the research brief into strict JSON for the daily trading plan.
</System_Context>

<Inputs>
- TRADING_DATE: {{TRADING_DATE}}
- REPORT_TIMEZONE: {{REPORT_TIMEZONE}}
- WATCHLIST_SIZE: {{WATCHLIST_SIZE}}
- TRACKED_MARKETS:
{{TRACKED_MARKETS}}

- RESEARCH_BRIEF:
{{RESEARCH_BRIEF}}
</Inputs>

<Task>
Convert the research brief into the exact JSON structure required by the daily trading plan.

Use this extraction policy:
1. Prefer the section titled `## 12.5 Assets To Watch Today` as the source of truth for the daily asset picks.
2. If that section is missing, infer the top {{WATCHLIST_SIZE}} watched assets from the report's executive summary, catalysts, sector analysis, opportunity map, and handoff summary.
3. Use only tracked markets from the tracked-markets list.
4. Map human-readable asset names from the brief onto the exact `instrumentId` values from the tracked-markets list.
</Task>

<Constraints>
- Return JSON only.
- Do not wrap the JSON in markdown fences.
- Do not include explanatory text.
- `marketRegime` must be exactly one of: `Unknown`, `RiskOn`, `RiskOff`, `Mixed`, `EventDriven`, `RangeBound`, `TrendDayCandidate`.
- Use the canonical market regime from section `1.2` if present.
- `rankedMarkets` must contain exactly {{WATCHLIST_SIZE}} items.
- `watchList` must contain the same {{WATCHLIST_SIZE}} items in the same order as `rankedMarkets`.
- Each item in `rankedMarkets` and `watchList` must use the exact `instrumentId` from the tracked-markets list.
- `expectedCatalysts` must contain only `CAT-##` identifiers that already appear in the research brief.
- If no valid catalyst IDs are available for a scenario, return an empty array.
- If the brief says `Avoid trading until: None`, return `null` for `avoidTradingUntilUtc`.
- `calendarEvents` must be an empty array for this version.
- Do not invent markets, IDs, catalysts, or events that are not supported by the brief.
</Constraints>

<Field_Mapping>
- `macroSummary`: a compact summary of the regime and key drivers for the day
- `marketRegimeSummary`: a concise summary of why the chosen canonical regime fits
- `marketRegime`: the exact canonical regime label
- `rankedMarkets`: the {{WATCHLIST_SIZE}} watched assets in rank order
- `watchList`: the same markets in the same order as `rankedMarkets`
- `rationale`: why that asset is on the watch list today
- `longScenario`: convert the long-bias summary into thesis, confirmation, invalidation, expectedCatalysts, and avoidTradingUntilUtc
- `shortScenario`: convert the short-bias summary into thesis, confirmation, invalidation, expectedCatalysts, and avoidTradingUntilUtc
</Field_Mapping>

<Final_Instruction>
Return the final JSON now.
</Final_Instruction>
