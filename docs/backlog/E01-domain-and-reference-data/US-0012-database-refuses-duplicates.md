---
id: US-0012
title: The database refuses duplicates that break core rules
epic: E01
feature: F01.1
status: ready
---

## Story
As a client comparing bids, I want each worker to appear at most once per job and each rating to count once, so that the bid list and the ratings I rely on can't be inflated by duplicates.

## Acceptance criteria
- **AC1** Given a worker already has a bid (in any status, `withdrawn` included) on a job, when a second bid for the same job and worker is saved, then the save fails and the job still has exactly one bid from that worker.
- **AC2** Given a person has already rated a job, when a second rating by the same rater on the same job is saved, then the save fails and only the first rating exists.
- **AC3** Given a user with phone `+381600000201` exists, when another user with the same phone is saved, then the save fails.
- **AC4** Given a store with referral code `VRACAR01` exists, when another store with `VRACAR01` (in any letter case, e.g. `vracar01`) is saved, then the save fails.
- **AC5** Given several jobs are created, when their numbers are read, then every job got a plain integer number automatically on creation, no two jobs share a number, and a later job has a higher number than an earlier one (gaps allowed).

## Business rules
- One bid per worker per job: unique `(job_id, worker_id)` (workflow.md section 2, Business rules). A withdrawn bid still counts, so a worker can't bid again after withdrawing (decision 2026-11-10).
- Ratings unique on `(job_id, rater_id)` (workflow.md section 3).
- Phones are stored normalized to `+381...` (E02 decision, 2026-11-10), so uniqueness applies to the normalized form.
- Referral codes identify a store (workflow.md section 3; section 2, Step 1); they are unique regardless of letter case, and E09 looks them up case-insensitively (decision 2026-11-10).
- Job number: a plain increasing integer, unique across all jobs, assigned on creation; gaps allowed; shown as "#<number>" in the app (display built in E04) (decision 2026-11-10).

## UI notes
- No UI.

## Out of scope
- Friendly API errors for these cases (`already_bid` comes with `POST /jobs/{id}/bids` in E05).
- How a job number is displayed (E04; decided format "#<number>").
- Looking up a referral code from a flyer link (E09).
- Rules that need business logic rather than uniqueness, e.g. "coverage and jobs point at neighborhoods only" (enforced where the data is written, E03/E04).

## Test notes
- Seeded users: none required; build rows with test builders. Ana Marković's phone `+381600000201` is used in AC3 for familiarity.
- Edge cases: two bids saved at the same moment from two connections (only one survives); a withdrawn bid followed by a new bid from the same worker (refused, AC1); referral code with surrounding spaces; job numbers after a failed insert may skip a value (gaps are fine, duplicates are not).

## Open questions
- none

## Decisions
- 2026-11-10: a worker who withdrew a bid can't bid again on the same job; one bid per worker per job, withdrawn included (keeps the rule simple and stops withdraw-and-undercut games).
- 2026-11-10: the job number is a plain increasing integer, unique across all jobs, assigned on creation, gaps allowed; the app shows it as "#<number>" (E04).
- 2026-11-10: referral codes are unique regardless of letter case (AC4), and E09 looks them up case-insensitively, since people type them from flyers.

## Dependencies
- Depends on US-0011 (the tables these rules apply to).

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
