# Prompt Patterns

Use these patterns as starting points. Trim unused sections instead of filling every placeholder by default.

## Contract-style rewrite

```xml
<System_Context>Apply [domain rules or evaluation lens]. Prioritize [quality criteria].</System_Context>
<Task>[Describe the exact action to perform.]</Task>
<Context>[Provide only the necessary background, source text, or structured data.]</Context>
<Constraints>
- Format: [json | bullets | table | prose]
- Length: [limit]
- Tone: [tone]
- Exclusions: [hard boundaries]
- Fallback: [exact failure text or uncertainty rule]
</Constraints>
```

## Few-shot format lock

```xml
<System_Context>Classify support tickets using the provided label set.</System_Context>
<Task>Return one label and a one-sentence justification.</Task>
<Examples>
Input: "I was charged twice for my subscription."
Output: {"label":"billing","justification":"The issue concerns duplicate payment."}

Input: "The mobile app closes when I open settings."
Output: {"label":"bug","justification":"The request reports an application crash."}
</Examples>
<Input>[New ticket text]</Input>
<Constraints>Return valid JSON only.</Constraints>
```

## Decomposed analysis

```xml
<System_Context>Analyze the supplied business report using evidence from the source only.</System_Context>
<Task>First extract key metrics. Second identify material trends. Third produce an executive summary.</Task>
<Context>[Report text]</Context>
<Constraints>
- Verify each metric against the source before final output.
- If a requested metric is missing, state "Information not found".
- Format the final answer as JSON with keys: metrics, trends, summary.
</Constraints>
```

## Plan-and-solve

```xml
<System_Context>Use explicit stepwise reasoning for a multi-stage task.</System_Context>
<Task>First write a concise plan. Then execute it.</Task>
<Context>[Problem statement]</Context>
<Constraints>
- Keep the plan to 3-6 steps.
- Revise the plan if the provided data is incomplete.
- Mark missing information clearly instead of inventing details.
</Constraints>
```

## Prompt review checklist

- Does the prompt define domain constraints instead of a persona?
- Does the task ask for one primary outcome?
- Does the prompt specify the output shape precisely?
- Does it include examples only where they add reliability?
- Does it define what to do when information is missing or ambiguous?
- Could the prompt be versioned and regression-tested without guessing intent?
