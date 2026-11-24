---
id: BUG-0009
title: Backend test suite occasionally fails once on a Testcontainers start timeout or a timing-sensitive US_0012 test
story: US-0017
criterion: none (test reliability)
severity: low
status: open
found-by: coordinator
---

## Steps to reproduce
Run `dotnet test backend --logger "console;verbosity=minimal"` repeatedly. Observed 3 times while building US-0014 and US-0017: one failure per run, never the same run twice, and a rerun passes (275 of 275 twice in a row afterwards).

## Expected
The suite is deterministic (testing rules: a flaky test is a bug, not retried into passing).

## Actual
- A Postgres Testcontainers start timeout ("server did not start in time") under parallel load (seen once in US-0014 QA, once in US-0017 fix).
- One failure in a US_0012 test that looks timing-sensitive (reported as taking about 1 ms; the test name was not captured).

## Reachability
Test reliability only, no product impact.

## Evidence
Seen in subagent runs on 2026-11-19 and 2026-11-24; not reproduced in two clean full runs afterwards. The next occurrence should capture the failing test name and output.

## Suggested fix (optional)
Share one container per test class collection or cap test parallelism for container-based tests; find any wall-clock assertions in US_0012 tests and replace them with deterministic checks.
