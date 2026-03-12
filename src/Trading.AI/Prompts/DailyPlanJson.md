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
1. Prefer the section titled `## 11.5 Assets To Watch Today` as the source of truth for the daily asset picks.
2. If that section is missing, infer the top {{WATCHLIST_SIZE}} watched assets from the report's executive summary, catalysts, sector analysis, opportunity map, and handoff summary.
3. Use only tracked markets from the tracked-markets list.
4. Map human-readable asset names from the brief onto the exact `instrumentId` values from the tracked-markets list.
5. Extract all catalysts from section 4, all opportunities from section 8, and all risks from section 9.
6. Extract all calendar events from section 6 and map their exposed markets onto tracked-market `instrumentId` values when possible.
</Task>

<Constraints>
- Return JSON only.
- Do not wrap the JSON in markdown fences.
- Do not include explanatory text.
- `marketRegime` must be exactly one of: `Unknown`, `RiskOn`, `RiskOff`, `Mixed`, `EventDriven`, `RangeBound`, `TrendDayCandidate`.
- Use the canonical market regime from section `1.2` if present.
- `rankedMarkets` must contain exactly {{WATCHLIST_SIZE}} items.
- Each item in `rankedMarkets` must use the exact `instrumentId` from the tracked-markets list.
- Each item in `rankedMarkets` must also include the human-readable market name as `instrumentName`.
- `expectedCatalysts` must contain only `CAT-##` identifiers that already appear in the research brief.
- `catalysts` must include every catalyst from section 4.
- `opportunities` must include every opportunity from section 8.
- `risks` must include every risk from section 9.
- `calendarEvents` must include every `EVT-##` row from section 6 that has a concrete scheduled time.
- Each `calendarEvents` item must include `id`, `title`, `scheduledAtUtc`, `impact`, and `affectedInstrumentIds`.
- `impact` must be exactly one of: `Low`, `Medium`, `High`.
- `affectedInstrumentIds` must use exact tracked-market `instrumentId` values only.
- If a calendar row mentions broad markets or untracked markets, include only the tracked markets that are clearly exposed.
- If a calendar row does not provide a concrete UTC timestamp, omit that row from `calendarEvents`.
- If no valid catalyst IDs are available for a scenario, return an empty array.
- If the brief says `Avoid trading until: None`, return `null` for `avoidTradingUntilUtc`.
- Do not invent markets, IDs, catalysts, opportunities, risks, or events that are not supported by the brief.
</Constraints>

<Field_Mapping>
- `macroSummary`: a compact summary of the regime and key drivers for the day
- `marketRegimeSummary`: a concise summary of why the chosen canonical regime fits
- `marketRegime`: the exact canonical regime label
- `rankedMarkets`: the {{WATCHLIST_SIZE}} watched assets in rank order
- `instrumentName`: the human-readable tracked market name that matches the `instrumentId`
- `rationale`: why that asset is on the watch list today
- `longScenario`: convert the long-bias summary into thesis, confirmation, invalidation, expectedCatalysts, and avoidTradingUntilUtc
- `shortScenario`: convert the short-bias summary into thesis, confirmation, invalidation, expectedCatalysts, and avoidTradingUntilUtc
- `catalysts`: every `CAT-##` item from section 4 with its key fields
- `opportunities`: every `OPP-##` item from section 8 with its key fields
- `risks`: every `RSK-##` item from section 9 with its key fields
- `calendarEvents`: every extractable `EVT-##` row from section 6, normalized to tracked-market instrument IDs
</Field_Mapping>

<Final_Instruction>
Return the final JSON now.
</Final_Instruction>
