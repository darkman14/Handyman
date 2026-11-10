---
id: US-0005
title: Seeded workers have trades, coverage and approval state
epic: E02
feature: F02.2
status: ready
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
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |

## Dev notes
