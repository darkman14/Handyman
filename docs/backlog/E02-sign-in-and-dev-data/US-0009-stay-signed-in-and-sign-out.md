---
id: US-0009
title: Stay signed in and sign out
epic: E02
feature: F02.3
status: done
---

## Story
As a busy electrician, I want the app to keep me signed in between uses and only show its screens when I'm signed in, so that I can check jobs quickly without a new SMS code every time, and sign out when I hand my phone to someone.

## Acceptance criteria
- **AC1** Given I signed in, when I fully close and reopen the app (iOS, Android) or reload the page (web), then I land on the home screen without entering a code again.
- **AC2** Given I am signed out, when I open any screen that requires sign-in (including a direct URL on web), then I am sent to the sign-in screen, and after signing in I land on the screen I asked for.
- **AC3** Given I am signed in, when I open the sign-in or code screen (e.g. by URL on web), then I am sent to the home screen.
- **AC4** Given I am signed in, when I tap "Sign out" and confirm, then my session is removed from the device, I see the sign-in screen, and reopening the app or reloading the page keeps me signed out.
- **AC5** Given my stored session can no longer be refreshed (e.g. revoked in Supabase), when I open the app, then I see the sign-in screen instead of an error or a blank screen.

## Business rules
- Session storage is platform-specific and lives in `src/lib/*.native.ts` / `*.web.ts` (workflow.md section 1, Frontend; CLAUDE.md hard rules).
- The app uses Supabase only for sign-in (workflow.md section 2, Business rules).
- Session lifetime on every platform, including shared browsers, follows Supabase defaults (refresh token, no forced timeout) for the MVP.

## UI notes
- While the stored session is being restored at startup, show a full-screen loading state (no flash of the sign-in screen).
- "Sign out" lives on the placeholder home screen for now; confirmation dialog: "Sign out?" with "Sign out" and "Cancel".
- `src/ui` components only; strings in `src/i18n/en.ts`.

## Out of scope
- Signing out other devices; "remember me" options; a forced session timeout on web.
- Handling `401`/`403` from the API (US-0010).
- Where "Sign out" ends up in the final navigation (E03 profile screen).

## Test notes
- Seeded users: Marko Jovanović +381600000101 (worker), Ana Marković +381600000201 (client).
- Jest tests for the auth guard with the mocked Supabase client: signed out, signed in, restoring, refresh failure.
- Edge cases: two browser tabs, sign out in one then act in the other; access token expired while the app was closed (should refresh silently); airplane mode at startup with a valid stored session; web private window.

## Open questions
- none

## Decisions
- 2026-11-10: web sessions, including on shared browsers, use Supabase defaults (refresh token, no forced timeout) for the MVP; users sign out explicitly (AC4).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | n/a | pass: `AC1 reload keeps the session and lands on home`, `AC1 a new tab...`, `AC1 no sign-in screen flashes while the session is restored` |
| AC2 | n/a | pass: `AC2 signed out: direct URL to /home goes to sign-in, then lands on /home after sign-in`, 7 open-redirect tests (`//evil.com`, `https://evil.com`, `javascript:`, backslash variants, `/code`, `/`). Only /home is protected today, so the redirect param is not set for it. See BUG-0025 (low) |
| AC3 | n/a | pass: `AC3 signed in: opening / or /code goes to home`, `AC3 ... back ... never shows the sign-in screen` |
| AC4 | n/a | pass: `AC4 sign out: cancel keeps the session, confirm removes it and reload stays signed out`, `AC4 ... Escape cancels and focus is inside the dialog`, `AC4 two tabs`, `AC4 ... phone width`, `AC4 ... back button` |
| AC5 | n/a | pass: `AC5 corrupted stored session...`, `AC5 revoked refresh token...`, `AC5 refresh endpoint returns 400 invalid_grant (mocked)...` |

## Dev notes
