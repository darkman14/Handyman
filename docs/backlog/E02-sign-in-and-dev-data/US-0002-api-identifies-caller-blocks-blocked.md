---
id: US-0002
title: API identifies the caller and refuses blocked users
epic: E02
feature: F02.1
status: draft
---

## Story
As a client or worker, I want the platform to shut out users an admin has blocked, so that people who abused the service can't keep posting jobs or bidding.

## Acceptance criteria
- **AC1** Given a valid token whose `sub` matches an `app.users` row with no `blocked_at`, when the user calls an authenticated endpoint, then the request is handled as that user (e.g. `GET /api/me` returns that user's id, not the id of anyone else).
- **AC2** Given a valid token whose `sub` matches an `app.users` row with `blocked_at` set, when the user calls any endpoint under `/api` (including `GET /api/me`), then the response is `403` with a problem-details body whose `code` is `user_blocked`, and no data is changed.
- **AC3** Given a user who was not blocked when their token was issued, when an admin sets `blocked_at` and the user then calls the API with the same, still-valid token, then the response is `403` (blocking takes effect immediately, not at token expiry).
- **AC4** Given a valid token whose `sub` has no `app.users` row yet (first sign-in), when the user calls `GET /api/me`, then the response is not `403` and not `500` (profile creation itself is E03).
- **AC5** Given a blocked user, when they call `GET /health`, then it still returns `200`.

## Business rules
- Blocked users get `403` with code `user_blocked` on every API call (workflow.md section 2, Business rules; section 4).
- Roles live in `app.users`, not in the token (workflow.md section 1, Auth).
- Errors use RFC 7807 problem details with a stable `code` (workflow.md section 4).

## UI notes
- No UI. The app's blocked-user message is US-0010.

## Out of scope
- Blocking a user from the admin screen and banning them in Supabase Auth (E10).
- Hiding a blocked user's open jobs and active bids from others (comes with jobs and bids, E04/E05, and E10).
- Role-based authorization policies (client vs. worker vs. admin): added with the endpoints that need them.

## Test notes
- Seeded users: Petar Stojanović +381600000204 (client, no jobs) as the user to block in tests; Ana Marković +381600000201 as the unblocked control.
- Edge cases: `blocked_at` in the future (any non-null `blocked_at` counts as blocked, per AC2); block then unblock (clear `blocked_at`) → next call succeeds; blocked admin; token for a user deleted from `app.users`.

## Open questions
- No open product questions.
- Kept `draft` because of an unmet dependency: this story reads and filters on `app.users` (`id`, `blocked_at`), which comes from E01 (entities and first migration). E01 has no stories yet. Move to `ready` once the E01 story that delivers `app.users` exists and is `ready`.

## Decisions
- 2026-11-10: a blocked user gets `403` with the new code `user_blocked`, distinct from `not_allowed`, so the app can show the blocked-account message (now in workflow.md section 4).
- 2026-11-10: endpoints other than `GET /me` and `PUT /me` answer a signed-in user with no `app.users` row with `404` and code `profile_missing`, same as `GET /me`. Built with the E03 endpoints; this story only guarantees AC4.

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
