---
id: US-0010
title: App calls the API as the signed-in user
epic: E02
feature: F02.3
status: draft
---

## Story
As a signed-in client or worker, I want the app to talk to the Handyman API as me, so that everything I see and do afterwards is mine, and I get a clear message if my access was removed.

## Acceptance criteria
- **AC1** Given I am signed in, when the home screen loads, then the app calls `GET /api/me` with my access token and shows a "Signed in" confirmation; while loading it shows a loading state.
- **AC2** Given my access token expired while the app was open, when the app next calls the API, then it uses a refreshed token and the call succeeds without me signing in again.
- **AC3** Given the API answers `401` even after a token refresh, when that happens, then I am signed out and see the sign-in screen with "Your session has ended. Sign in again."
- **AC4** Given my account is blocked (API answers `403` with code `user_blocked`), when the app calls the API, then I see a full-screen message "Your account has been blocked. Contact support." with a "Sign out" button and no contact details, and no other screen is reachable.
- **AC5** Given the API can't be reached (network error or `5xx`), when the home screen loads, then I see "Something went wrong. Try again." with a "Try again" button that repeats the call.

## Business rules
- The app sends the Supabase access token to the API (workflow.md section 1, Auth).
- Blocked users get `403` with code `user_blocked` on every API call (workflow.md section 2, Business rules; section 4). Only that code shows the blocked screen.
- Server state only through TanStack Query hooks and the typed client generated from the OpenAPI contract (CLAUDE.md hard rules).

## UI notes
- Home placeholder: loading, success ("Signed in"), error with "Try again".
- Blocked screen: full screen, plain generic copy with no phone, email or link, single "Sign out" action.
- Session-ended message appears on the sign-in screen as an info banner.
- `src/ui` components only; strings in `src/i18n/en.ts`.

## Out of scope
- Profile content and the `profile_missing` onboarding redirect (E03).
- Offline caching.
- Support contact details on the blocked screen (generic text only for the MVP).

## Test notes
- Seeded users: Ana Marković +381600000201 (normal client); block Petar Stojanović +381600000204 by setting `blocked_at` in the local database for AC4, and clear it afterwards.
- Jest tests with a mocked API client and mocked Supabase: success, refresh then success, `401` after refresh, `403` blocked, network error.
- Web e2e (Playwright, qa-app): sign in as Ana with `123456` and see "Signed in".
- Edge cases: several API calls failing with `401` at once must sign out only once; `403` with a different code (e.g. `not_allowed`) must not show the blocked screen; API base URL on a phone on the LAN (not `localhost`).

## Open questions
- No open product questions.
- Kept `draft` because of an unmet dependency: AC1, AC3 and AC4 need the API side of US-0002 (`GET /api/me` resolving the caller, `403` `user_blocked`), and the AC4 check needs Petar seeded in `app.users` (US-0003). Both are `draft` waiting on E01, which has no stories yet. Move to `ready` once US-0002 is `ready`.

## Decisions
- 2026-11-10: the blocked-user code is `user_blocked` (decided in US-0002; now in AC4).
- 2026-11-10: the blocked screen shows generic text without support contact details for the MVP.

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | |
| AC2 | n/a | |
| AC3 | | |
| AC4 | | |
| AC5 | n/a | |

## Dev notes
