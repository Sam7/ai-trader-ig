---
name: code-review-guard
description: Review uncommitted repository changes for correctness, maintainability, architecture, security, and developer-experience issues. Use after a meaningful chunk of coding work, especially after adding or changing tests, public APIs, cross-project boundaries, or infrastructure code.
---

# Code Review Guard

Review the current uncommitted changes with a code-review mindset. Focus on issues that matter, not cosmetic churn.

## Workflow

1. Determine the review scope from git.
2. Inspect only changed files unless a nearby dependency is necessary to understand risk.
3. Prioritize findings in this order:
   - correctness and behavioral regressions
   - security and secrets handling
   - broken architectural boundaries
   - developer experience and API clarity
   - test quality and maintainability
4. Fix clear, local issues immediately when the implementation is obviously wrong and the fix is low-risk.
5. Report broader issues instead of triggering speculative refactors.

## Use these commands first

- Run `pwsh -File .codex/skills/code-review-guard/scripts/list-uncommitted.ps1` to see the current review scope.
- Run `git diff --stat` for a quick shape check.
- Run `git diff -- <path>` on the highest-risk files.
- For secret-file findings, verify whether the file is ignored with `git check-ignore -v <path>` before raising a high-severity issue.
- If needed, run targeted tests for the changed area before finalizing review conclusions.

## Review checklist

Load `.codex/skills/code-review-guard/references/review-checklist.md` and use it as a heuristic checklist. Do not mechanically enforce every item. Use judgment.

## Expected behavior

- Prefer actionable findings with evidence.
- Do not rewrite working code only to satisfy style preferences.
- Treat long files, dense tests, and repeated logic as signals to investigate, not proof of a problem.
- Prefer identifying missing tests or bad abstractions over commenting on formatting.
- Treat ignored local config containing real credentials as acceptable local setup, but still call out any tracked or unignored secret-bearing file.
- When there are no findings, say that explicitly and mention any residual risk or test gap.
