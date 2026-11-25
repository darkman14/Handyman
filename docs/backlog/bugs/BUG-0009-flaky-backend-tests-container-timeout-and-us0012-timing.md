---
id: BUG-0009
title: Backend test suite occasionally fails once on a Testcontainers start timeout or a timing-sensitive US_0012 test
story: US-0017
criterion: none (test reliability)
severity: medium
status: fixed
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

## Update 2026-11-24 (US-0018)
Possible root cause found by backend-dev: before US-0018 every ApiFactory-based test class ran against the local Supabase database (localhost:54322, about 1130 leftover trades), not its own Testcontainers container, because `Program.cs` loads appsettings.Local.json after the host settings so the factory's `UseSetting` lost. `ApiFactory` now sets ConnectionStrings:Default and Supabase:Url through ConfigureAppConfiguration. Shared state between parallel test classes is a likely source of the timing-sensitive failure. Keep this bug open until the suite has run clean many times in a row.

## Occurrence 2026-11-24 (US-0018, coordinator)
In a full `dotnet test backend --no-build` run (second of two back-to-back runs, 323 tests) the three `HealthAndAuthTests` failed at about 1:27 into the run; the first run passed (323 of 323) and the same three tests passed when rerun alone (3 of 3). The failure message was not captured (output was cut). Pattern so far: one-off failures in tests that start a Testcontainers Postgres or use ApiFactory, only in full parallel runs. Next time capture the full failure text with `--logger "console;verbosity=normal"` or a trx logger.

## Severity raised to medium 2026-11-24
Four full-suite runs in one day had failures (once 10 tests, once 3, once 2): about one in three full runs. It makes CI unreliable. Fix as its own `fix/*` branch (cap test parallelism for container tests or share one container per collection), not inside a feature story.

## Fix
Root cause: 25 test fixtures (ApiFactory per class, SchemaFixture, QaFixture, DecoratorFixture and three per-class `PostgreSqlContainer`s) each started their own `postgres:17-alpine` container, and xUnit runs those classes in parallel. A full run therefore started about 25 containers at once through Docker in WSL; under that load `pg_ctl` timed out ("server did not start in time", `container ... is not running`). One failed fixture start fails every test of its class in 1 ms, which explains all earlier reports (US_0012, HealthAndAuthTests, ReadOnlySessionTests, US_0017). No wall-clock assertion was involved; the "timing-sensitive" US_0012 test was the same fixture start failure.

Change (tests only, no product code): new `backend/tests/Handyman.Api.Tests/Infrastructure/TestDatabase.cs` with `SharedPostgres` (one container per test run, started lazily once, `max_connections=1000`, `fsync=off`) and `TestDatabase` (a fresh empty database per fixture, dropped on dispose). The six fixtures that built a `PostgreSqlContainer` now use `TestDatabase` instead; test bodies are unchanged. Parallelism is unchanged, isolation per class is kept (own database).

Numbers (342 tests, this machine): before, 2 of 4 full runs failed (31 and 2+ tests, all container start errors), 137-156 s per run. After, 9 of 9 full runs passed, 39-43 s per run.
