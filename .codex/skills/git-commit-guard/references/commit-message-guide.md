# Commit Message Guide

Use this as a practical guide, not a rigid format rule.

## Subject line

- Use imperative mood: `Add`, `Fix`, `Refactor`, `Document`, `Test`, `Remove`.
- Keep it narrow and truthful.
- Prefer describing the user-visible or developer-visible outcome.
- Avoid filler like `update stuff`, `changes`, or `misc fixes`.

## Good examples

- `Add working-order lifecycle smoke test`
- `Fix IG close flow for delete-with-body fallback`
- `Document local appsettings workflow`
- `Refactor IG order status resolution`

## Bad examples

- `Updates`
- `Fix bug`
- `More work`
- `WIP`

## Body guidance

Add a body when one of these is true:

- the change has multiple tightly related parts
- the reason matters more than the mechanics
- cleanup, migration, or test strategy needs a brief explanation

Keep the body short. Prefer 1-3 lines that explain why.

## Scope check before commit

- Does the subject match every staged file?
- Are unrelated files unstaged?
- Would another engineer understand the commit from the message and diff together?
