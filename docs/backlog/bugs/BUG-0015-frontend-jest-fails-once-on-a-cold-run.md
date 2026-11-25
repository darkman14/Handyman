---
id: BUG-0015
title: One frontend Jest test fails on the first (cold) run after a build, then passes on every rerun
story: none
criterion: none
severity: low
status: open
found-by: tech lead
---

## Steps to reproduce
1. Right after `npm run api:types && npm run typecheck`, run `cd frontend && npm test -- --silent` on a cold Jest cache.
2. Seen during US-0001 delivery (2026-11-25), and once earlier during E02 planning.

## Expected
All 8 tests pass on every run.

## Actual
`Tests: 1 failed, 7 passed, 8 total` on the first run (about 12 s). Seven reruns in a row passed, each about 2 s. The name of the failing test was not captured (the run used `--silent` and the output was filtered).

## Evidence
Suspect: a default 5 s test timeout hit while Jest transforms files on a cold cache. Not confirmed.

## Suggested fix (optional)
Reproduce with `npx jest --clearCache && npm test` and keep the full output. If it is a timeout, set a suitable `testTimeout` in the Jest config or reduce the work in the slow test. Do not add retries.
