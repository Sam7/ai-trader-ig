---
name: llm-prompt-writer
description: Write or refine prompts for large language model workflows. Use when Codex needs to create prompt text for generation, extraction, classification, evaluation, summarization, agent workflows, or system prompts, especially when the user wants modern prompt-engineering practices built around explicit constraints, examples, decomposition, uncertainty handling, and versionable prompt structure.
---

# LLM Prompt Writer

Write prompts as durable instructions, not as ad hoc prose. Prefer explicit contracts, testable constraints, and clear fallback behavior over persona-based prompting.

## Workflow

1. Identify the real task.
2. Choose the minimum prompt shape that can succeed.
3. Write the prompt in contract style.
4. Add examples only where they improve reliability.
5. Add decomposition and uncertainty rules for non-trivial tasks.
6. Tighten output constraints until the prompt is testable.

## Build the prompt contract

Use clearly separated sections. Prefer XML-style tags or equally explicit headings.

- Set `System_Context` or `Domain` to define the operating environment, evaluation lens, and non-negotiable rules.
- Set `Task` with direct action verbs and one primary objective.
- Set `Context` or `Data` with only the information the model needs.
- Set `Constraints` to define format, length, tone, exclusions, and edge-case handling.

Do not default to persona or role prompting. Replace "Act as ..." with domain and constraint language such as "Analyze this using strict financial reporting rules" or "Apply technical writing principles focused on brevity and active voice."

## Add reliability controls

- Prefer positive instructions. State the desired behavior directly.
- Use strict exclusion language only when a hard boundary matters.
- Add fallback behavior for missing or uncertain information.
- Require verification against the provided source before final output when extraction accuracy matters.
- Specify exact response text when the model must fail gracefully.

For example, use instructions such as:

- `If the source does not contain the answer, reply exactly with "Information not found".`
- `Verify every extracted metric against the source text before producing the final answer.`

## Use examples deliberately

Add one to three input/output examples when the target format, tone, or logic path is fragile.

- Prefer examples that match the real task closely.
- Include one edge case when omissions or malformed input are likely.
- Keep examples concrete and short enough to remain readable.

Load `.codex/skills/llm-prompt-writer/references/prompt-patterns.md` when you need reusable templates or example layouts.

## Decompose complex work

Break multi-step tasks into explicit stages instead of asking for a final answer immediately.

- Extract facts before analysis.
- Analyze before formatting.
- Ask for a plan first when the task needs multi-step reasoning or tool use.
- Keep the visible instructions concise even when the internal process is staged.

Use ordered steps such as:

1. Extract the relevant facts.
2. Check them for completeness or contradiction.
3. Produce the final output in the requested format.

## Treat prompts like code

- Start with a simple zero-shot prompt.
- Add constraints only where baseline behavior fails.
- Add examples only where constraints alone are insufficient.
- Keep prompt text in source control when it matters to a workflow.
- Test prompt changes against known inputs and expected outputs before calling the prompt stable.

## Expected behavior

- Produce prompts that are easy to inspect, edit, and version.
- Prefer compact instructions over verbose explanation.
- Make uncertainty behavior explicit.
- Avoid hidden assumptions about tone, expertise, or intent.
- Return the finished prompt ready to use, and include a short note on which parts are most important to tune if the user wants iteration.
