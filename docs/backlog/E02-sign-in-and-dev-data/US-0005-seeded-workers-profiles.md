---
id: US-0005
title: Seeded workers have trades, coverage and approval state
epic: E02
feature: F02.2
status: done
---

## Story
As a developer or QA engineer, I want the seeded workers to come with their trades, covered neighborhoods and approval state, so that matching, approval and "pending worker" flows can be tested with known workers from the first day.

## Acceptance criteria
- **AC1** Given the seeder has run, when the worker data is read, then Marko Jovanović has trades Electrical and Heating & AC; Jelena Petrović has Appliance repair; Nikola Đorđević has Appliance repair and Plumbing; Stefan Ilić has Painting and Carpentry (and no other trades).
- **AC2** Given the seeder has run, when worker coverage is read, then each worker covers every neighborhood of the municipalities listed for them in workflow.md section 7 (Marko: Novi Beograd, Zvezdara; Jelena: Vračar, Zvezdara; Nikola: Vračar, Novi Beograd; Stefan: Vračar), stored as neighborhood rows only, never as municipality or city rows.
- **AC3** Given the seeder has run, when worker profiles are read, then Marko, Jelena and Nikola have a worker profile with an approval time set, and Stefan has a worker profile with no approval time (pending).
- **AC4** Given the seeder has run more than once, when trades, coverage and profiles are read, then there are no duplicates, coverage is what the first seed wrote, and Stefan is still pending (a restart never changes existing worker data: it never approves him or overwrites an approval an admin made later).

## Business rules
- Worker coverage is stored at neighborhood level (workflow.md section 1, Location; section 7, slice 3 tests).
- A worker sees open jobs only after admin approval (workflow.md section 2, Business rules); Stefan exists to test that (workflow.md section 7, "Seeded development data").
- Trades and the area tree come from E01's reference seed; this story only links to them.

## UI notes
- No UI. Visible in the app from E03 (profile) and E05 (open jobs).

## Out of scope
- Seeding trades, areas and the store (E01).
- Showing or editing profiles (E03); approving workers in admin (E10).
- Bios: any short placeholder bio is fine.

## Test notes
- Seeded users: Marko Jovanović +381600000101, Jelena Petrović +381600000102, Nikola Đorđević +381600000103, Stefan Ilić +381600000104.
- Edge cases: a neighborhood added to Vračar in the reference seed after the first seed must not appear in seeded workers' coverage after a restart (AC4); clients and the admin must have no worker profile, trades or coverage.

## Open questions
- none

## Dependencies
- Depends on US-0011 (worker profile, trade and coverage tables), US-0018 (trades and area tree) and US-0003 (seeded users).

## Decisions
- 2026-11-10: seeded coverage is fixed at first seed; the seeder never changes existing worker data, so neighborhoods added to the area tree later are not added on restart (now in AC4).

## API contract
No API change.

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass: `AC1_workers_have_exactly_their_trades`, `US_0005_QaExploratoryTests.Exact_data_per_worker_and_coverage_count_matches_reference_data` | n/a |
| AC2 | pass: `AC2_coverage_is_every_neighborhood_of_the_listed_municipalities_and_only_neighborhoods`, `Exact_data_per_worker_and_coverage_count_matches_reference_data`, `Database_constraints_back_the_seed_duplicates_and_orphans_are_rejected` | n/a |
| AC3 | pass: `AC3_three_workers_approved_stefan_pending_and_clients_and_admin_have_nothing`, `Approved_at_is_set_now_not_in_the_future_and_stefan_is_null` | n/a |
| AC4 | pass: `AC4_restart_changes_nothing_and_never_approves_stefan`, `Three_restarts_change_nothing_at_all`, `Admin_approval_of_stefan_then_restart_keeps_the_approval_time`, `Neighborhood_added_to_Vracar_after_first_seed_is_not_added_on_restart`, `Blocked_worker_stays_blocked_and_untouched_after_restart` | n/a |

QA notes (qa-api, 2026-11-26): 21 US-0005 tests green. Also verified: workers step without the users step fails with "users seed step must run first" and writes nothing; a missing trade or municipality fails loudly and all-or-nothing (single SaveChanges); two hosts racing end in one complete seed (a loser, if any, fails with a unique violation, loud and retryable); `GET /api/trades` and `/api/areas` unchanged. Documented behaviour (pinned by test, by design): a worker whose profile exists but whose trades or coverage were removed is not repaired on restart. Real local stack run not done (needs the app DB connection from the local secrets file, which QA must not open).

## Dev notes
- Seeded by `SeedDevWorkersStep` (data in `DevWorkers.cs`), registered after `SeedDevUsersStep` in Program.cs (order = registration order; the step throws if a worker user is missing).
- A worker with an existing profile is skipped entirely, so approvals and coverage are fixed at first seed.
