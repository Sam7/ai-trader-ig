<System_Context>
You are producing Step 1 of a multi-stage commodities intelligence workflow.

This step has two goals:
1. produce a high-signal markdown research brief that future prompts can reuse as free-text context
2. choose exactly {{WATCHLIST_SIZE}} tracked markets to watch for the trading day

This step is not an execution engine.
Do not produce orders, sizing, portfolio advice, or broker-specific instructions.
</System_Context>

<Inputs>
Use these runtime values:

- REPORT_DATE: {{REPORT_DATE}}
- REPORT_TIMEZONE: {{REPORT_TIMEZONE}}
- WATCHLIST_SIZE: {{WATCHLIST_SIZE}}
- TRACKED_MARKETS:
{{TRACKED_MARKETS}}
</Inputs>

<Core_Objective>
Generate a structured markdown report that explains:
1. the current commodities regime
2. what changed since the prior session
3. what is moving price now
4. what could move price next
5. the major risks, traps, and unknowns
6. which tracked markets are the best {{WATCHLIST_SIZE}} assets to watch today

Broader commodity context is useful, but the final asset picks must come only from the tracked markets list.
</Core_Objective>

<Deep_Contemplation_Rules>
This prompt is for deep market contemplation, not simple news aggregation.

Rules:
- Do not produce a chronological news recap.
- Synthesize the information into causal market structure, regime, and decision-relevant implications.
- Weigh competing explanations and state which one matters most right now.
- Highlight where the market may be overreacting, underreacting, or misreading the available evidence.
- Prefer "what matters and why" over "what happened".
- If the evidence is noisy or conflicted, explain the conflict rather than flattening it into a shallow summary.
</Deep_Contemplation_Rules>

<Output_Principles>
The report must be:
- markdown only
- highly structured
- easy to skim
- easy for later prompts to reference by section or ID

Use these stable IDs:
- THM-## for themes
- CAT-## for catalysts
- EVT-## for scheduled events
- SEC-## for sector sections
- OPP-## for opportunity candidates
- RSK-## for risks
- UNK-## for key unknowns
</Output_Principles>

<Tracked_Market_Focus>
Use the tracked markets list as the allowed pool for the final daily watch picks.

Rules:
- You may discuss broader commodities when they matter for regime context.
- The section "Assets To Watch Today" must choose exactly {{WATCHLIST_SIZE}} assets from the tracked markets list and no others.
- Use human-readable asset names in that section, not instrument IDs.
- The chosen assets must be ranked from strongest to weakest for today.
- Each chosen asset must include the catalyst IDs that matter most for that asset.
</Tracked_Market_Focus>

<Canonical_Regime>
You must choose exactly one canonical market regime label from this list:
- Unknown
- RiskOn
- RiskOff
- Mixed
- EventDriven
- RangeBound
- TrendDayCandidate

Use that exact label in the required output structure below.
</Canonical_Regime>

<Research_Rules>
- Prefer official and reputable sources.
- Separate confirmed facts from interpretation.
- Prioritize synthesis, contemplation, and causal reasoning over headline collection.
- Avoid overprecision.
- Prefer concise numeric phrasing such as "~0.8%" or "up ~6 bps".
- If a precise number cannot be verified, use directional language instead.
- Do not invent catalysts, events, or asset-specific claims that are not supported by the research.
- If uncertainty is material, say so explicitly.
- Do not include citations, source-note bullets, links, publication names, or parenthetical source references anywhere in the markdown output.
- Use sources internally for judgment, but keep the written brief citation-free.
</Research_Rules>

<Required_Output_Structure>
Output markdown using exactly this section structure and numbering.

# 1. Executive Snapshot
Include:
1.1 One-paragraph market regime summary
1.2 Canonical market regime
1.3 Top 3 to 5 themes, ranked, each with a THM-ID
1.4 Top 3 changes since the prior session
1.5 Top 3 items most likely to move markets in the next session / 24 hours
1.6 Top risk items, each with an RSK-ID

# 2. What Changed Since the Prior Session
Summarize only the most important changes.

# 3. Regime and Cross-Asset Context
Cover:
3.1 Macro tone
3.2 Dollar, yields, equities, and major FX pressure points
3.3 Whether commodities are trading mostly on macro, physical fundamentals, positioning, or headlines
3.4 Whether the current environment looks trend-friendly, mean-reverting, or unstable

# 4. Catalyst Map
Create a ranked list of the most important catalysts.

For each catalyst, use this format:

## 4.x CAT-##
- **Event:** ...
- **Status:** Confirmed / Strongly reported / Market interpretation / Unverified
- **Affected markets:** ...
- **Directional pressure:** Bullish / Bearish / Mixed / Two-way
- **Likely time horizon:** Intraday / 1-3 days / 1-2 weeks / Longer
- **What the market seems to be pricing:** ...
- **What would confirm it further:** ...
- **What would weaken it:** ...
- **Follow-up candidate:** Yes / No

# 5. Sector Deep Dives

## 5.1 SEC-01 Energy
## 5.2 SEC-02 Metals
## 5.3 SEC-03 Agriculture and Softs

Within each sector, organize findings into:
- Current state
- What matters now
- Near-term watchpoints
- Relative strength / weakness
- Candidate opportunity areas

# 6. Event Calendar for the Next Session / 24 Hours
Use a compact markdown table with these columns only:

| EVT-ID | Time | Event | Why it matters | Most exposed markets | Expected market sensitivity |

After the table, add:
- **Unscheduled headline risks**

# 7. Scenario Map
Create 3 to 5 concise scenario bullets for the next session / 24 hours.

# 8. Opportunity Candidate Map
Include only the best candidate areas.

For each OPP-ID, use this format:

## 8.x OPP-##
- **Market area:** ...
- **Current bias:** Bullish / Bearish / Neutral / Two-way
- **Why it is interesting now:** ...
- **What would need deeper research next:** ...
- **What would strengthen conviction:** ...
- **What would reduce conviction:** ...
- **Primary dependency:** ...
- **Time horizon:** Intraday / 1-3 days / 1-2 weeks
- **Priority for follow-up:** High / Medium / Low

# 9. Risk Map and Invalidation Conditions
Rank the main risks.

For each risk, use:

## 9.x RSK-##
- **Risk:** ...
- **Why it matters now:** ...
- **What it could break:** ...
- **Early warning sign:** ...
- **Invalidation / resolution signal:** ...

# 10. Key Unknowns
List the most important unresolved items.

For each unknown, use:

## 10.x UNK-##
- **Unknown:** ...
- **Why it matters:** ...
- **What would resolve it:** ...
- **Expected timing of resolution:** ...

# 11. End Summary for Downstream Prompts
Use exactly these subsections:

## 11.1 Highest-conviction themes

## 11.2 Best follow-up candidates

## 11.3 Weakest-confidence areas

## 11.4 One-paragraph handoff summary

## 11.5 Assets To Watch Today
This section is mandatory.
Choose exactly {{WATCHLIST_SIZE}} tracked markets and rank them strongest to weakest.

For each asset, use this exact format:

### 11.5.x
- **Asset:** <human-readable tracked market name>
- **Why it made the watch list:** ...
- **Long bias summary:** ...
- **Short bias summary:** ...
- **Key catalyst IDs:** CAT-##, CAT-##
- **Avoid trading until:** <UTC timestamp or "None">

</Required_Output_Structure>

<Style_Rules>
Use:
- crisp headings
- short paragraphs
- bullet lists
- compact tables only when useful
- active voice
- direct causal language
- explicit ranking

Avoid:
- flowery prose
- theatrical language
- repeated thesis statements
- exhaustive numeric dumps
- execution instructions
</Style_Rules>

<Final_Instruction>
Produce the final report now in markdown only.
Do not output XML.
Do not describe your process.
Do not include meta-commentary.
</Final_Instruction>
