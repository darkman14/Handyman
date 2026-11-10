---
id: US-0012
title: The database refuses duplicates that break core rules
epic: E01
feature: F01.1
status: done
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
- Edge cases: two bids saved at the same moment from two connections (only one survives); a withdrawn bid followed by a new bid from the same worker (refused, AC1); referral code with any whitespace (spaces, tabs, newlines, non-breaking spaces); job numbers after a failed insert may skip a value (gaps are fine, duplicates are not).

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
| AC1 | pass: `Second_bid_by_the_same_worker_on_a_job_is_refused`, `A_withdrawn_bid_still_blocks_a_new_bid_from_the_same_worker`, `Two_simultaneous_bids_from_two_connections_leave_exactly_one`, `Ten_parallel_duplicates_leave_exactly_one_of_each` | n/a |
| AC2 | pass: `Second_rating_by_the_same_rater_on_a_job_is_refused_and_only_the_first_remains`, `The_other_party_can_rate_the_same_job` | n/a |
| AC3 | pass: `A_second_user_with_the_same_phone_is_refused`, `Phone_uniqueness_is_exact_string_match_and_case_of_other_columns_is_irrelevant` | n/a |
| AC4 | pass for letter case and plain spaces (`A_store_with_the_same_referral_code_in_any_case_or_spacing_is_refused`); tab, newline and non-breaking-space padding slip through: BUG-0001 (low), test `Referral_code_spelling_variants_are_refused` | n/a |
| AC5 | pass: `Job_numbers_are_assigned_automatically_unique_and_increasing`, `Job_number_cannot_be_supplied_and_a_failed_insert_never_causes_a_duplicate`, `Job_numbers_stay_unique_under_parallel_inserts_updates_are_refused_and_rollbacks_leave_gaps` | n/a |

QA (qa-api) extra checks: local Supabase already had UniqueConstraints applied; API restart applied nothing ("already up to date"); the five unique indexes and the `number` GENERATED ALWAYS identity are confirmed there (opt-in test `Local_supabase_has_the_unique_indexes_and_the_migration_recorded`, env QA_SUPABASE_DB_URL). Upgrade over Initial with clean rows works; with pre-existing duplicate bids the migration fails atomically (nothing half-applied), acceptable pre-release but a database holding real data would need a dedupe step first. Down migration verified.

## Dev notes

- QA note (2026-11-10): BUG-0001 (referral code padded with tab, newline or non-breaking space) was fixed in place in the unmerged UniqueConstraints migration; the index strips all whitespace including non-breaking space (see the migration) and lowercases. Referral lookups and writes (E09) must normalize the same way. A local database that already applied the earlier version of this migration keeps the old index until `supabase db reset`.
