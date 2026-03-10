# Review Checklist

Use this checklist proportionally. It is a decision aid, not a rule engine.

## Correctness

- Does the change do what the tests claim it does?
- Are edge cases and error paths covered where the behavior matters?
- Is there any silent change in semantics, defaults, or status mapping?
- Are time, currency, quantity, and direction handled consistently?

## Architecture

- Did any IG-specific concern leak outside `Ig.Trading.Sdk` or `Trading.IG`?
- Did the adapter stay thin, or is it accumulating transport and DTO concerns?
- Did the CLI remain wiring-only?
- Did a new abstraction earn its place, or is it framework-shaped speculation?

## API and DX

- Are names intention-revealing?
- Do public APIs feel small and obvious to consume?
- Are enums or value objects used where they remove ambiguity?
- Are failures readable and broker-neutral at the abstraction boundary?

## Tests

- Are tests behavior-focused and readable?
- Is setup proportional to what is being tested?
- Is the test using mocks only at real boundaries?
- Are integration tests clearly opt-in?

## Maintainability

- Is logic duplicated in a way that will drift?
- Did a file or method become too long without a good reason?
- Is branching logic centralized where it belongs?
- Are comments rare and useful rather than compensating for unclear code?

## Security

- Are secrets, tokens, passwords, or sensitive payloads exposed anywhere?
- If the file is local config, is it ignored by git? Ignored local config is acceptable. Tracked or unignored secret-bearing files are not.
- Are logs sanitized?
- Are error messages useful without leaking broker-sensitive details?

## Refactoring restraint

- Do not recommend a refactor unless it clearly improves correctness, clarity, maintainability, or boundaries.
- Ignore cosmetic issues unless they are obscuring a real problem.
