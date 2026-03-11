You are converting a daily research brief into a structured trading-day plan.

Return JSON only.
Do not wrap the JSON in markdown fences.
Do not include any explanatory text.

Requirements:
- The trading date is {{TRADING_DATE}}.
- The target timezone is {{REPORT_TIMEZONE}}.
- You must choose markets only from the tracked market list below.
- Use the exact `instrumentId` values provided below.
- Rank all markets from strongest to weakest.
- The watch list must contain exactly {{WATCHLIST_SIZE}} markets.
- Keep the output concrete, readable, and suitable for downstream execution review.
- Calendar events can be omitted in this phase; return an empty array for `calendarEvents`.

Tracked markets:
{{TRACKED_MARKETS}}

Strategy rules summary:
- Watch list size: {{WATCHLIST_SIZE}}
- Minimum reward:risk ratio: {{MIN_REWARD_RISK_RATIO}}

Research brief markdown:
{{RESEARCH_BRIEF}}
