---
name: project-backlog-naming
description: Backlog naming and numbering decisions for Handyman (epic folders, story ids, proposed error codes, UI copy) to keep consistent across slices
metadata:
  type: project
---

- E02 folder is `E02-sign-in-and-dev-data` (slice 2, auth + dev seed). Stories US-0001..US-0010, features F02.1 (API auth), F02.2 (seed), F02.3 (app sign-in). Planned 2026-11-10 before E01 existed, so E01 (slice 1, domain + migration + reference seed) still has no stories yet, and E02 depends on it.
- Proposed, not yet approved: blocked-user 403 code `user_blocked`; non-/me endpoints for a user with no profile return 404 `profile_missing`; Serbian phone numbers only, `+381` prefilled.
- Copy used so far (keep consistent): "Send code", "Sign in", "Send a new code", "Change number", "Sign out", "That code is wrong or has expired.", "Your session has ended. Sign in again.", "Your account has been blocked. Contact support."
- Seeded-data mapping: Ana's dishwasher job = Appliance repair in Čubura with bids from Jelena and Nikola; Milan = Electrical in Bežanijska kosa, no bids (Marko is matched worker); Ivana = Done job with Jelena, no ratings. Sample jobs fill every address field so privacy tests cover all fields. Block Petar (+381600000204) in tests that need a blocked user.

**Why:** Keeps ids, codes and copy stable across slices so later stories and QA reuse the same terms.
**How to apply:** Check board.md for next id; reuse these codes/copy; if the human resolves a proposal, update this file.
