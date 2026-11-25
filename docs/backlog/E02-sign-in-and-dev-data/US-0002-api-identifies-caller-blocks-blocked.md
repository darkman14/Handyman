---
id: US-0002
title: API identifies the caller and refuses blocked users
epic: E02
feature: F02.1
status: in-progress
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
- none

## Dependencies
- Depends on US-0011 (`app.users` with `id` and `blocked_at`) and US-0001 (token validation). US-0017 supplies the problem-details format.
- The `403 user_blocked` also applies on the public reference-data endpoints (US-0019, US-0020) for a valid token of a blocked user.

## Decisions
- 2026-11-10: a blocked user gets `403` with the new code `user_blocked`, distinct from `not_allowed`, so the app can show the blocked-account message (now in workflow.md section 4).
- 2026-11-10: endpoints other than `GET /me` and `PUT /me` answer a signed-in user with no `app.users` row with `404` and code `profile_missing`, same as `GET /me`. Built with the E03 endpoints; this story only guarantees AC4.

## API contract
No new endpoints and no new request/response shapes. This story changes behaviour of existing `/api` calls.

**Blocked user (every `/api/*` call, including `GET /api/me`, `GET /api/trades`, `GET /api/areas`)**
- Condition: valid token whose `sub` matches an `app.users` row with `blocked_at` not null (any value, even in the future).
- Response: `403`, `application/problem+json`, `code: "user_blocked"` (distinct from `not_allowed`). No data is changed.
- Anonymous requests and invalid/expired tokens are not affected: they stay anonymous (public endpoints `200`, protected ones `401` as in US-0001). The block check only applies when a valid token was authenticated.
- `GET /health` is outside `/api` and never returns `user_blocked` (`200` for blocked users too).
- Takes effect on the next request after `blocked_at` is set; clearing `blocked_at` restores access on the next request.

**GET /api/me** (signed in)
| Case | Response |
| --- | --- |
| `app.users` row, not blocked | `200` `{ "id": "<caller's app.users id>" }` (the E03 story extends this to the full profile) |
| Blocked | `403` `user_blocked` |
| Valid token, no `app.users` row (first sign-in, or deleted user) | `404` problem-details `code: "profile_missing"` (not 403, not 500) |
| No/invalid token | `401` (unchanged) |

Other `/api` endpoints keep their current behaviour for a user with no row (the public reference data stays `200`). The `404 profile_missing` for future user-scoped endpoints comes with E03 (see Decisions).

OpenAPI: `GET /api/me` now declares `403` and `404` problem responses (the only contract-file change).

**Design question for build mode: where to enforce**
- Middleware placed after `UseAuthentication()` (and before `UseAuthorization()`/endpoints), applied only to paths under `/api`, so `/health` and `/openapi` are untouched.
- Runs on every request (no caching of the blocked state, or only a cache short enough that AC3 "immediate" holds; simplest is no cache). Only acts when `context.User.Identity.IsAuthenticated`; anonymous requests pass through, so public endpoints stay open.
- One cheap lookup per request: primary-key query on `app.users` by `sub`, selecting only `blocked_at` (e.g. `ReadDbContext`, `AnyAsync(u => u.Id == id && u.BlockedAt != null)`), no tracking, no entity load. PK index suffices.
- Writes the 403 through the existing problem-details writer (US-0017) with `code = user_blocked`, short-circuiting before the handler, so no state changes.
- `GET /me` needs to know if the row exists for `profile_missing`: either the same middleware lookup stores "exists/blocked" in `HttpContext.Items`, or the `/me` handler checks the row itself. Prefer one lookup per request shared via `Items`.
- Existing tests that call `/api/me` with `factory.ClientFor(userId)` need that user to exist in `app.users`; `US_0019_QaExploratoryTests.cs:40` already notes it must be updated for `user_blocked`.

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |
| AC5 | | n/a |

## Dev notes
- Contract mode: only `Program.cs` changed (`GET /me` now declares 403 and 404 problem responses); the regenerated `docs/api/Handyman.Api.json` diff is real content, not line endings.
- Build mode: `Auth/CallerStatusMiddleware.cs` (after `UseAuthentication()`, `/api` and authenticated callers only) does one PK lookup through `ReadDbContext` per request, no cache; blocked gives 403 `user_blocked` via `ApiExceptionHandler.ProblemFor/WriteAsync`. The result is stored in `HttpContext.Items[CallerStatus.ItemKey]`; `GET /api/me` reads it and answers 404 `profile_missing` when no row exists.
- Tests: `ApiFactory.SeedUserAsync / SignedInUserAsync / SetBlockedAtAsync / DeleteUserAsync` helpers; any test that expects 200 on `/api/me` must seed the user first.
