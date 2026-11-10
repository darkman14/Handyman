---
id: US-0007
title: Request a sign-in code by phone
epic: E02
feature: F02.3
status: ready
---

## Story
As a client with a broken dishwasher (or a worker checking for jobs), I want to sign in with just my phone number, so that I don't have to create or remember a password.

## Acceptance criteria
- **AC1** Given I am signed out and open the app (web, iOS or Android), when it loads, then I see the sign-in screen with a phone number field and a "Send code" button.
- **AC2** Given I enter a valid Serbian phone number, either in local form (`06x...`) or international form (`+3816x...`), when I tap "Send code", then the button shows a loading state, a code is requested from Supabase Auth for that number normalized to `+381...` (no spaces or dashes), and I am taken to the code screen showing that number. This works for a number that has never signed in before (new sign-up) as well as for a seeded number.
- **AC3** Given I enter an empty, invalid or non-Serbian phone number, when I tap "Send code", then no code is requested and the field shows "Enter a valid phone number".
- **AC4** Given Supabase Auth refuses the request (rate limit, network error, or any other error), when I tap "Send code", then I stay on the sign-in screen, see a readable error message (rate limit: "Too many attempts. Try again in a minute."; other: "Couldn't send the code. Try again."), and can try again.

## Business rules
- Sign-in is Supabase Auth phone sign-in with an SMS code; local development uses test OTPs (workflow.md section 1, Auth).
- SMS code limits are set in Supabase Auth, not in the app (workflow.md section 2, Business rules).
- The app uses Supabase only for sign-in (workflow.md section 2, Business rules).
- Pilot accepts Serbian numbers only; the app normalizes them to `+381...` before calling Supabase.
- Anyone can sign up with a new number (Supabase `enable_signup = true`, workflow.md section 6); onboarding follows in E03.

## UI notes
- Sign-in screen: title "Sign in", helper text "We'll text you a 6-digit code.", phone field with a phone keypad and `+381` prefilled, button "Send code".
- States: idle, invalid input (inline field error), sending (button loading, field disabled), error (message above the button), success (navigate to code screen).
- Built only from `src/ui` components and `theme.ts` tokens; strings in `src/i18n/en.ts`; `accessibilityLabel` on the field and button; works with large font sizes.
- Layout is responsive on web (centered card on wide screens).

## Out of scope
- Entering and verifying the code (US-0008).
- Choosing a country other than Serbia (pilot is Serbia only).
- Onboarding a newly signed-up number (E03).
- Email or password sign-in, social sign-in.

## Test notes
- Seeded users: Ana Marković +381600000201 (client), Marko Jovanović +381600000101 (worker); locally no real SMS is sent.
- Screen tests run with the mocked Supabase client (`src/lib/__mocks__/supabase.ts`), covering success, invalid input and both error messages.
- Edge cases: number typed with spaces or dashes (`060 000 0201`, `+381 60 000 0201`), both must be sent as `+381600000201`; leading `0` local format typed after the prefilled `+381` (e.g. `+381 060...`); a non-Serbian number such as `+44...` (rejected, AC3); an unknown Serbian number (code requested, AC2); double tap on "Send code" must request only once; very long input; pasting a number.

## Open questions
- none

## Decisions
- 2026-11-10: Serbian numbers only for the pilot, `+381` prefilled, accepting both `06x...` and `+3816x...` and normalizing to `+381...` before sending (now in AC2, AC3 and UI notes).
- 2026-11-10: anyone can sign up with a new number (Supabase `enable_signup = true`); onboarding follows in E03 (now in AC2).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | n/a | |
| AC2 | n/a | |
| AC3 | n/a | |
| AC4 | n/a | |

## Dev notes
