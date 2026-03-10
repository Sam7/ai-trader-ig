---
name: git-commit-guard
description: Stage the intended repository changes and create a clear, well-scoped Git commit with an appropriate message. Use when Codex is asked to commit work, prepare a commit, choose a commit message, or ensure only the right files are included in a Git commit.
---

# Git Commit Guard

Prepare a deliberate Git commit. Verify scope first, then stage the intended changes, then write a commit message that accurately describes what changed and why.

## Workflow

1. Inspect the current repository state before staging anything.
2. Separate intended changes from unrelated work.
3. Stage only the files that belong in the commit.
4. Review the staged diff before committing.
5. Write a precise commit subject and, when needed, a short body.
6. Create the commit only after the staged diff and message agree.

## Use these commands first

- Run `pwsh -File .codex/skills/git-commit-guard/scripts/inspect-commit-scope.ps1`.
- Run `git diff --staged --stat`.
- Run `git diff --staged -- <path>` on the highest-risk staged files.

## Commit message rules

- Use imperative mood.
- Keep the subject specific and scoped to the staged changes.
- Prefer a short subject line that explains the outcome, not the mechanic.
- Add a body only when it improves future understanding.
- Do not claim behavior that is not in the staged diff.
- Do not hide multiple unrelated changes behind one vague commit.

## Commit message heuristics

Load `.codex/skills/git-commit-guard/references/commit-message-guide.md` and use it to shape the final message.

## Expected behavior

- Prefer one coherent commit over one huge mixed commit.
- If unrelated changes are present, leave them unstaged unless the user explicitly wants them included.
- If the staged diff is too broad to summarize honestly in one message, stop and split the commit.
- If the repository is already dirty in unrelated areas, do not clean it up unless requested.
