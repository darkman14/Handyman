---
id: US-0008
title: Sign in with the SMS code
epic: E02
feature: F02.3
status: done
---

## Story
As a client or worker who just received an SMS code, I want to type it in and be signed in, so that I can start using the app in seconds.

## Acceptance criteria
- **AC1** Given I am on the code screen for a seeded number, when I enter `123456` and tap "Sign in", then I am signed in and taken to the app's home screen.
- **AC2** Given I am on the code screen, when I enter a wrong or expired code and tap "Sign in", then I stay on the code screen, see "That code is wrong or has expired.", the field is cleared, and I can try again.
- **AC3** Given I am on the code screen, when I enter fewer than 6 digits, then "Sign in" is disabled (no request is sent).
- **AC4** Given I am on the code screen, when I tap "Send a new code", then a new code is requested for the same number, the link is disabled for the next 60 seconds with a visible countdown, and a Supabase error is shown as in US-0007 AC4.
- **AC5** Given I am on the code screen, when I tap "Change number", then I am back on the sign-in screen with my number still filled in.

## Business rules
- Every seeded number signs in locally with code `123456` (workflow.md section 7, "Seeded development data").
- SMS code limits are set in Supabase Auth (workflow.md section 2, Business rules); the 60-second resend wait is a UX guard only.

## UI notes
- Code screen: title "Enter the code", text "Sent to +381 60 000 0201", 6-digit numeric field (one-time-code autofill on iOS/Android and `autocomplete="one-time-code"` on web), button "Sign in", links "Send a new code" and "Change number".
- States: idle, incomplete code (button disabled), verifying (button loading), wrong code (inline error), resend cooldown ("Send a new code in 0:42"), success (navigate home).
- Home screen for this slice can be a placeholder that greets the signed-in user by phone; the real home comes with E03.
- `src/ui` components and `theme.ts` tokens only; strings in `src/i18n/en.ts`; accessibility labels on field, button and links.

## Out of scope
- Keeping the session across restarts and signing out (US-0009).
- Calling the API with the token (US-0010).
- Onboarding after the first sign-in (E03).

## Test notes
- Seeded users: Ana Marković +381600000201 (client), Jelena Petrović +381600000102 (worker), Admin +381600000999.
- Screen tests with the mocked Supabase client: success, wrong code, incomplete code, resend with cooldown, change number.
- Manual check on a phone and in a browser against local Supabase with code `123456`.
- Edge cases: pasting the code with spaces; tapping "Sign in" twice; going back with the hardware back button on Android; app put in background during verification.

## Open questions
- none

## API contract
No API change (Supabase Auth only).

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | n/a | pass: `AC1 real local Supabase signs Ana (client) / Jelena (worker) / Admin in with 123456 and shows home`, `AC1 full flow from sign-in screen with real Supabase reaches home` |
| AC2 | n/a | pass: `AC2 a real wrong code 000000 ...`, `AC2 mocked otp_expired 403 / invalid 400 / 422 ...`, `AC2 retry after a mocked failure succeeds ...`, `AC2 typing again clears the error message`, verify rate limit / server / network error tests |
| AC3 | n/a | pass: `AC3 fewer than 6 digits keeps Sign in disabled and sends no request`, `AC3 letters and symbols are ignored`, `AC3 pasted "123 456" is accepted as 123456`, `AC3 more than 6 digits are capped at 6` |
| AC4 | n/a | pass: `AC4 Send a new code requests an OTP ... visible 60 s countdown` (page.clock), `AC4 clicking the disabled resend link ...`, `AC4 resend rate limit ...`, `AC4 resend server error ...`, `AC4 a network failure on resend ...`, `AC4 the countdown does not block signing in` |
| AC5 | n/a | pass: `AC5 Change number returns to sign-in with the number prefilled`, `AC5 after Change number a different number can be used end to end` |

Also covered: double click sends one verify, Enter submits, /code without or with invalid phone and /home without session redirect to sign-in, reload on /home and /code, 360 px and 1920 px, 200% text size, no console errors, no token or code in the URL. No new bugs. Observations: the number is in the `/code?phone=` URL; resend and Sign in buttons lose keyboard focus when they become disabled; loading state aria-busy on the buttons is still BUG-0021; raw route names in headers still BUG-0023.

## Dev notes
