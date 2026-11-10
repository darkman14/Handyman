---
id: US-0011
title: Business tables exist in a locked-down app schema
epic: E01
feature: F01.1
status: ready
---

## Story
As a client or worker, I want my phone number, address and job history stored only where the Handyman API can reach them, so that nobody can read them by going around the app.

## Acceptance criteria
- **AC1** Given an empty Postgres database (Testcontainers or local Supabase), when the API's migrations are applied, then schema `app` contains exactly the 13 tables from workflow.md section 3 (`users`, `worker_profiles`, `trades`, `worker_trades`, `areas`, `worker_areas`, `stores`, `jobs`, `job_photos`, `bids`, `ratings`, `notifications`, `device_tokens`) with the key fields listed there, plus the migrations history table; `jobs` also has a `title` and a `description`, `bids` stores the arrival window as a start and an end time in UTC, and `users` holds exactly one role per user.
- **AC2** Given the migrated database, when a test inspects every table in schema `app`, then each one has row-level security enabled and no policies; a test fails if a table is added later without RLS.
- **AC3** Given the migrated local Supabase database, when the Supabase Data API is called with the `anon` key or a signed-in user's token for any `app` table, then no row is returned or written.
- **AC4** Given the migration files, when they are applied to a plain Postgres with no Supabase `auth` or `storage` schema, then they succeed (no foreign key or reference into those schemas).
- **AC5** Given the migrations folder, when it is listed, then the empty `Initial` migration is gone and one first migration creates the whole schema; applying migrations again on an already migrated database (API restart) changes nothing.

## Business rules
- All tables in schema `app`; Supabase's `auth` and `storage` are not referenced by foreign keys (workflow.md section 3).
- `app` is not exposed through Supabase's Data API and has RLS enabled with no policies (workflow.md section 1, Database).
- Migrations enable RLS on every new table (`.claude/rules/backend.md`).
- Money as whole RSD in `int`; all times UTC (workflow.md section 1, Database).
- `app.users.id` equals the Supabase auth user id; roles (client, worker, admin) live in `app.users` (workflow.md section 1, Auth; section 3).
- `app.users.blocked_at` marks a blocked user (workflow.md section 2, Business rules); US-0002 reads it.
- Job statuses: Open, Assigned, Done, Closed, Cancelled; bid statuses: active, accepted, rejected, withdrawn; area kinds: city, municipality, neighborhood (workflow.md sections 2 and 3).
- A job has a title and a description written by the client (workflow.md section 2, Step 2; added to the `jobs` row, decision 2026-11-10).
- A bid's arrival window is a start and an end time in UTC, end after start; the app shows it in local time (decision 2026-11-10).
- One role per user (client, worker or admin); whether a worker may also post jobs is decided in E03 and needs no schema change here (decision 2026-11-10).
- No cancellation time on jobs for the MVP (US-0013 decision 2026-11-10).

## UI notes
- No UI.

## Out of scope
- Uniqueness rules (US-0012) and job state rules (US-0013, US-0014).
- Reference data rows (US-0018).
- Seeded users, worker profiles and sample jobs (E02: US-0003, US-0005, US-0006).
- Any endpoint other than the ones that exist today.
- Concurrency token behavior for hiring (E06), though the `jobs` table may already carry what it needs.
- Validating the arrival window and job title/description on input (E04, E05).
- A client/worker mode switch or several roles per user (E03).
- Resetting the shared test database itself (platform-dev does it once when this story deploys, see Decisions).

## Test notes
- Seeded users: none; assertions run on the schema.
- AC3 is a manual check against the local Supabase stack (e.g. a REST call to `/rest/v1/users` with `Accept-Profile: app` and the anon key, and again with `./scripts/dev-token.sh +381600000201`'s token once E02 is done); automated tests prove AC2 on plain Postgres.
- Edge cases: the migration is applied by API startup in Development and by `Database__MigrateOnStartup=true` on the shared test server (Program.cs today) — check both start cleanly; schema `app` must not appear in the Data API's exposed schemas in `supabase/config.toml`; money columns hold integers only; time columns store UTC; the shared test environment already has the empty `Initial` recorded in its migrations history: after platform-dev's one-time reset of that database (and its Supabase Auth), check the deployed API starts and all 13 tables exist there.

## Open questions
- none

## Decisions
- 2026-11-10: `jobs` gets `title` and `description` in this migration (workflow.md section 2, Step 2 already asks for them; section 3 is updated by the coordinator as a documentation fix). US-0006 uses them.
- 2026-11-10: `bids` arrival window is a start and an end time in UTC (end after start), shown in local time by the app, so clients can compare bids by arrival and tests stay exact. Section 3 is clarified by the coordinator.
- 2026-11-10: the empty `Initial` migration is replaced in place. The shared test environment holds no real data, so platform-dev resets its `app` schema and migrations history once when this story deploys, together with its Supabase Auth users (E02 rule "reset both together").
- 2026-11-10: one role per user, as in workflow.md section 3; whether a worker may also post jobs is an E03 question and needs no schema change here.

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |
| AC5 | | n/a |

## Dev notes
