---
name: project-backlog-naming
description: Backlog naming and numbering decisions for Handyman (epic folders, story ids, decided error codes, phone/session rules, UI copy, seeded-data mapping) to keep consistent across slices
metadata:
  type: project
---

- E02 folder is `E02-sign-in-and-dev-data` (slice 2, auth + dev seed). Stories US-0001..US-0010, features F02.1 (API auth), F02.2 (seed), F02.3 (app sign-in). E01 (slice 1, domain + migration + reference seed) had no stories as of 2026-11-10; stories needing E01 tables stay `draft` with a "Kept draft because of an unmet dependency" note under Open questions (README has no separate status for this).
- Decided by the human 2026-11-10 (all E02 proposals accepted): blocked user = 403 `user_blocked`; token with missing/non-GUID `sub` = 401; non-/me endpoints for a user with no profile = 404 `profile_missing` (build in E03); Serbian numbers only, `+381` prefilled, accept `06x`/`+3816x`, normalize to `+381...`; open sign-up; Supabase default session lifetime, no forced web timeout; blocked screen has no support contact; seeder runs only in Development and Test (shared test env included), all other envs refused; seeder never changes existing data (coverage fixed at first seed) and never re-keys `app.users` (reset Auth and DB together); Mila's store referral is a manual E09 reminder only; no Assigned sample job.
- Resolved questions are recorded in a story `## Decisions` section dated, with `## Open questions` set to `- none`.
- Copy used so far (keep consistent): "Send code", "Sign in", "Send a new code", "Change number", "Sign out", "Enter a valid phone number", "That code is wrong or has expired.", "Your session has ended. Sign in again.", "Your account has been blocked. Contact support.", "Something went wrong. Try again."
- Seeded-data mapping: Ana's dishwasher job = Appliance repair in Čubura with bids from Jelena and Nikola; Milan = Electrical in Bežanijska kosa, no bids (Marko is matched worker); Ivana = Done job with Jelena, no ratings. Sample jobs fill every address field so privacy tests cover all fields. Block Petar (+381600000204) in tests that need a blocked user.

**Why:** Keeps ids, codes and copy stable across slices so later stories and QA reuse the same terms.
**How to apply:** Check board.md for next id; reuse these codes/copy; when E01 stories become ready, revisit E02 drafts US-0002..US-0006 and US-0010.
