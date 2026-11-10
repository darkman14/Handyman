---
id: US-0003
title: Seeded users can sign in locally
epic: E02
feature: F02.2
status: draft
---

## Story
As a developer or QA engineer, I want the 10 seeded users to exist in local Supabase Auth and in `app.users` as soon as the API starts, so that I can sign in as any client, worker or admin with code `123456` and test real flows without setting anything up by hand.

## Acceptance criteria
- **AC1** Given an empty local database and local Supabase Auth with no users, when the API starts in Development or Test (shared test environment) with `Seed:Enabled` true, then all 10 users from workflow.md section 7 ("Seeded development data") exist in Supabase Auth with a confirmed phone, and each has an `app.users` row with the same id, the phone, the name and role from the table, and the home neighborhood listed under "Covers / lives in" for clients.
- **AC2** Given the API has started once, when it is restarted, then there is still exactly one auth user and one `app.users` row per seeded phone, and their ids are unchanged.
- **AC3** Given a Supabase Auth user already exists for a seeded phone (e.g. created by an earlier sign-in) but no `app.users` row does, when the API starts, then that auth user is reused (no second auth user, no error) and its `app.users` row is created with the auth user's id.
- **AC4** Given the local stack is running after seeding, when a tester signs in with any seeded number and code `123456` and calls `GET /api/me` with the resulting token, then the response is `200` and carries that user's `app.users` id.
- **AC5** Given the list of seeded users in code and the `[auth.sms.test_otp]` section of `supabase/config.toml`, when backend tests run, then a test fails if any phone is in one list but not the other.

## Business rules
- Seeded users are created through the Supabase Auth admin API with `phone_confirm: true`, reusing an existing auth user with the same phone (workflow.md section 7, slice 2).
- `app.users.id` equals the Supabase auth user id (workflow.md section 3).
- Every seeded number signs in locally with `123456` (workflow.md section 7, "Seeded development data").
- Credentials go to git-ignored `docs/local-credentials.md` (workflow.md section 1, Local data).
- The shared test environment runs with `ASPNETCORE_ENVIRONMENT=Test` and seeds on startup exactly as locally; its Supabase project has the seeded numbers as test phone numbers with code `123456` (workflow.md section 6, Step 5).
- `app.users` and Supabase Auth are reset together. The seeder never re-keys an existing `app.users` row to a new auth user id.
- Mila Pavlović's "referred by store `VRACAR01`" is a manual-testing reminder for E09; nothing is seeded for it.

## UI notes
- No new screens. Sign-in is checked with `./scripts/dev-token.sh` here, and through the app once US-0008 is done.

## Out of scope
- Worker trades, coverage and approval (US-0005).
- Sample jobs (US-0006).
- Environment gating (US-0004).
- Configuring test phone numbers in the shared test environment's Supabase dashboard (platform-dev, workflow.md section 6, Step 5).
- Re-keying `app.users` rows after only Supabase Auth was reset (not supported: reset both together).
- Any store referral for Mila Pavlović (E09).

## Test notes
- Seeded users: all 10 from workflow.md section 7, phones +381600000101–0104, +381600000201–0205, +381600000999.
- Edge cases: restart the API while local Supabase is down (startup should fail loudly or log a clear error; it must not create half a cast silently — record the behavior in Dev notes); delete one auth user in Supabase Studio and restart (it should come back); phone format differences (`+381 60 000 0101` vs `381600000101` in `config.toml`) must not cause a mismatch in AC5; tests must not call a real Supabase project (fake the admin API); only Supabase Auth wiped while `app.users` is kept (unsupported: the seeder must not create a second `app.users` row for the same phone or silently re-key; how it reports the mismatch is a dev choice recorded in Dev notes); after deploying to the shared test environment, sign in there as Ana Marković +381600000201 with `123456`.

## Open questions
- No open product questions.
- Kept `draft` because of an unmet dependency: the seeder writes `app.users` rows (and clients' home neighborhoods from the area tree), which come from E01 (entities, first migration, reference seed). E01 has no stories yet. Move to `ready` once the E01 stories for `app.users` and the area tree exist and are `ready`.

## Decisions
- 2026-11-10: the seeder also runs on the shared test environment (`Test`); AC1–AC4 apply there, with the test numbers configured in the `handyman-test` Supabase dashboard as in workflow.md section 6, Step 5.
- 2026-11-10: "reset both databases together" is the accepted rule; the seeder does not re-key `app.users` rows after only Supabase Auth was wiped.
- 2026-11-10: Mila's store referral is only a reminder for testing the E09 referral flow by hand; nothing is seeded for it and the data model is unchanged.

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | |
| AC5 | | n/a |

## Dev notes
