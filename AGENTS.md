# AI Trader IG Agent Guide

## Mission

Build a small, clean, test-first trading solution with an isolated IG SDK and a broker-neutral adapter. Optimize for developer experience first: readable code, small APIs, fast feedback, and low cognitive overhead.

## Architectural intent

- Keep `Trading.Abstractions` broker-neutral and stable.
- Keep `Ig.Trading.Sdk` isolated, packable, and useful as a standalone open-source SDK.
- Keep `Trading.IG` thin. It adapts broker-neutral models to the SDK. It should not become a second SDK.
- Keep `Trading.Cli` intentionally small. It wires configuration and exercises flows manually. No business logic.
- Treat test projects as first-class code. Tests are documentation of behavior, not a dumping ground for incidental details.

## Engineering bar

- Target the latest stable .NET and C# supported by the solution. Prefer current language features when they improve clarity, not novelty.
- Prefer composability over inheritance.
- Prefer immutable models and small focused types.
- Prefer explicit, crisp boundaries over flexible-but-vague abstractions.
- Prefer a small number of dependencies. Add one only when it clearly improves correctness, maintainability, or developer experience.
- Preserve a pleasant read path. A new developer should understand the main flow quickly.

## TDD workflow

- Work red, green, refactor in small steps.
- Start with the next smallest useful behavior.
- Write behavior-oriented tests with one clear failure reason.
- Use fakes at true boundaries. Do not over-mock internal implementation details.
- Keep fast tests fast. Keep live IG integration tests opt-in and clearly separated.
- Refactor immediately after green when the design wants it, but do not refactor speculatively.

## Separation of concerns

- Keep IG-specific DTOs, headers, auth tokens, endpoint quirks, and Refit contracts inside `Ig.Trading.Sdk`.
- Keep `Trading.IG` focused on mapping, orchestration, and error translation.
- Keep the CLI dependent on `ITradingGateway`, not IG internals.
- Keep configuration and secrets handling outside domain logic.
- Avoid “utility” classes that silently become dependency magnets.

## Developer experience rules

- Make APIs read like prose.
- Keep method signatures small and intention-revealing.
- Use enums and value objects where they reduce ambiguity.
- Use names that match the mental model of a consuming developer, not raw broker terminology.
- Prefer helpful failures with domain-level context over raw transport errors.
- Keep logs useful and structured. Never log secrets, tokens, or passwords.

## Design guardrails

- Apply SOLID pragmatically, not ceremonially.
- Use DRY to remove harmful duplication, not all repetition.
- Use YAGNI aggressively. Do not build speculative framework layers.
- Watch for code smells:
  - very long files
  - giant methods
  - tests with too many assertions or too much setup
  - mapping logic spread across multiple layers
  - public APIs that expose transport concerns
  - duplicated branching rules
- Treat these as investigation triggers, not automatic refactor mandates.

## Security and operational hygiene

- Never commit credentials or secrets.
- Prefer env vars or user secrets for runtime secrets.
- Ignored local config files with real credentials are acceptable for local development. Tracked or unignored secret-bearing files are not.
- Sanitize logs and debug output.
- Handle network and broker failures explicitly.
- Translate broker errors into clear application-level outcomes.

## Review workflow

- After any meaningful chunk of implementation, invoke the local `$code-review-guard` skill before concluding the work.
- Scope the review to uncommitted changes.
- Fix clear, local issues immediately when the fix is low-risk.
- If the review finds broader architectural tension, stop and explain the tradeoff instead of silently refactoring half the codebase.
- Do not run a heavy review pass after trivial edits that do not materially change behavior.

## Local skill

- Local skill path: `.codex/skills/code-review-guard/SKILL.md`
- Use it for proportional review of uncommitted changes, especially after adding new flows, new endpoints, or cross-project design changes.
