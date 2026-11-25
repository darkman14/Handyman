---
id: US-0004
title: Seeder runs only where it is allowed
epic: E02
feature: F02.2
status: done
---

## Story
As the product owner, I want the development seeder to be impossible to run in Production, so that fake users with a known sign-in code can never appear in the live app.

## Acceptance criteria
- **AC1** Given the API starts in Production, or in any environment other than Development and Test (e.g. `Staging`), even with `Seed:Enabled` true, when startup runs, then no seeded user, profile or sample job is created, and the attempt is refused with a logged error (a test proves this).
- **AC2** Given the API starts in Development or Test with `Seed:Enabled` false or missing, when startup runs, then nothing is seeded.
- **AC3** Given the API starts in Development or Test with `Seed:Enabled` true, when startup runs, then migrations are applied first and the seed runs after them (a database with no migrations ends up migrated and seeded).
- **AC4** Given the OpenAPI document is generated at build time (`dotnet build backend`), when the build runs, then no seeding and no Supabase call happens.

## Business rules
- `DevSeeder` runs in Development or Test with `Seed:Enabled`, never in Production (workflow.md section 7, slice 2). Every other environment name is refused like Production.
- Development (and Test) startup applies migrations and seeds (workflow.md section 1, Local data).

## UI notes
- No UI.

## Out of scope
- What gets seeded (US-0003, US-0005, US-0006).
- Production deployment configuration (E11).

## Test notes
- Seeded users: none needed; assert on an empty `app.users` and no calls to the (faked) Supabase admin API.
- Edge cases: environment names in other casing (`production`, `PRODUCTION`); an unknown environment name such as `Staging` (must not seed, AC1); `Seed:Enabled` set via environment variable `Seed__Enabled=true` in Production.

## Open questions
- none

## Dependencies
- Depends on US-0011 (`app.users` and the first migration, checked in AC1 and AC3).
- Reference data (US-0018) is not seeder data: it is present in every environment, so AC1's "nothing seeded" means no seeded users, profiles or sample jobs.

## Decisions
- 2026-11-10: the seeder runs only in Development and Test; every other environment (Production, `Staging`, unknown names) is refused like Production (now in AC1).

## API contract
No API change.

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass: `US_0004_SeederRunsOnlyWhereAllowedTests.AC1_*`; QA: `Environment_name_variants_that_are_not_exactly_development_or_test_are_refused`, `Refused_seed_still_serves_health_logs_no_secrets_and_applies_no_migration` | n/a |
| AC2 | pass for `false`/missing (`AC2_*`, `Seed_enabled_false`); fail for empty value: BUG-0017 (`KNOWN_GAP_BUG_0017_*`) | n/a |
| AC3 | pass: `AC3_empty_database_is_migrated_first_and_then_seeded`; QA: `Environment_name_in_any_casing_of_development_or_test_seeds`, `Running_the_seeder_twice_*`, `Failing_seed_step_fails_startup_loudly` | n/a |
| AC4 | pass: real `dotnet build backend` with `Seed__Enabled=true` and a bogus DB/Supabase config succeeded in about 4 s, regenerated `docs/api/Handyman.Api.json` (no content diff); dev test `AC4_*` is only a source check | n/a |

## Dev notes
- Startup logic: `Data/Seed/DevSeeder.cs` (`DevSeedStartup.RunAsync`, called from `Program.cs`, skipped when the entry assembly is `GetDocument.Insider`). Allowed: environment name equals Development or Test (any casing) and `Seed:Enabled` true; then migrate, then run every registered `IDevSeedStep`. Other environments log an error and seed nothing (no migration either, unless `Database:MigrateOnStartup` is set).
- US-0003/0005/0006: add seed steps as scoped `IDevSeedStep`; use `ISupabaseAdminClient` (faked in tests).
- `ApiFactory` now forces `Seed:Enabled=false` because a developer's `appsettings.Local.json` enables it.
- Serilog now uses `preserveStaticLogger: true` (one logger per host) so tests can capture logs through a DI `ILogEventSink`.
