---
id: US-0014
title: A job closes only when both sides have rated
epic: E01
feature: F01.2
status: done
---

## Story
As a client or worker, I want my rating to count only once and the job to close only after both of us have rated, so that neither side can rate after seeing the other's rating, and the rating gate always has a clear end.

## Acceptance criteria
- **AC1** Given a Done job, when the client rates it with 1–5 stars and an optional comment, then the rating is recorded with the client as rater and the hired worker as ratee, and the job stays Done.
- **AC2** Given a Done job that the client has already rated, when the hired worker rates it, then the rating is recorded with the worker as rater and the client as ratee, the job becomes Closed and its close time is set in UTC; the same holds in the other order (worker first, then client).
- **AC3** Given a Done job, when someone who is neither the job's client nor its hired worker tries to rate it, then it is refused with a domain error and no rating is recorded.
- **AC4** Given a Done job the client has already rated, when the client tries to rate it again, then it is refused with a domain error and the first rating is unchanged.
- **AC5** Given a job, when a rating is attempted while it is Open, Assigned, Closed or Cancelled, or with stars below 1 or above 5, then it is refused with a domain error and nothing changes.

## Business rules
- Done: both sides must rate; Closed: ratings are revealed (workflow.md section 2, Job lifecycle).
- Both give 1–5 stars and an optional comment (workflow.md section 2, Step 7).
- Unique rating per `(job_id, rater_id)` (workflow.md section 3); backed by the database rule in US-0012.
- Rating is a `Job` method (`AddRating`) that throws `DomainException` on an invalid rating (workflow.md section 5, Backend conventions).
- Required test: "rating closes only when both exist" (workflow.md section 7, slice 1).

## UI notes
- No UI. The rating form and gate banner come in E07.

## Out of scope
- Hiding the other side's rating until both exist (API response, E07).
- Updating users' rating sum, count and average on close (E07).
- The rating gate on `POST /jobs` and `POST /jobs/{id}/bids` (`rating_pending`, E04, E05, E07).
- Comment length limits and content rules (E07).

## Test notes
- Covered by `JobTests` alongside US-0013.
- Seeded users: none; in test data use Ivana Nikolić as client and Jelena Petrović as hired worker (mirrors the seeded Done job, US-0006).
- Edge cases: a bidder who was not hired tries to rate (refused, AC3); stars 0, 6 and negative (refused); empty comment vs. no comment (both allowed); client rates, worker rates, then the client tries again on the Closed job (refused).

## Open questions
- none

## Decisions
- 2026-11-10: the human reviewed E01 and accepted all proposed answers; no product decision was open in this story.

## Dependencies
- Depends on US-0013 (Done jobs with a hired worker) and US-0011 (ratings table).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass (domain): US_0014_JobClosesWhenBothRatedTests.Both_orders_close_the_job_with_correct_ratees_and_utc_close_time; JobRatingTests.Client_rating_a_done_job_records_it_for_the_worker_and_keeps_the_job_done | n/a |
| AC2 | pass (domain, Postgres): US_0014_JobClosesWhenBothRatedTests.Both_orders_close_the_job_with_correct_ratees_and_utc_close_time (non-UTC input); JobRatingTests.Ratings_and_the_closed_job_survive_save_and_reload_on_postgres | n/a |
| AC3 | pass (domain): US_0014_JobClosesWhenBothRatedTests.Strangers_other_bidders_and_empty_guid_are_refused_and_the_callers_list_is_untouched; US_0014_JobClosesWhenBothRatedTests.A_refused_rating_stores_nothing_and_does_not_touch_the_job_row | n/a |
| AC4 | pass (domain, Postgres): US_0014_JobClosesWhenBothRatedTests.Same_rater_twice_is_refused_in_both_directions_and_first_rating_is_unchanged; US_0014_JobClosesWhenBothRatedTests.Concurrent_double_submit_by_the_same_rater_leaves_exactly_one_row | n/a |
| AC5 | pass (domain): US_0014_JobClosesWhenBothRatedTests.Invalid_stars_are_refused_for_both_parties; JobRatingTests.Rating_a_job_that_is_not_done_is_refused_and_changes_nothing | n/a |

## Dev notes
- QA (2026-11-19), no migration or model change in this branch. Known risk for E07 (not a blocker here, no bug filed): `AddRating` trusts the `jobRatings` list the handler passes. Two concurrent FIRST ratings by the client and the worker both read an empty list, both insert (different `(job_id, rater_id)`, so the unique index does not stop them), and the job stays Done with two ratings forever. Reproduced on Postgres: `Concurrent_first_ratings_by_both_sides_can_leave_the_job_stuck_in_done_RISK_for_E07`. Severity for E07: high if unmitigated (job never closes, rating gate blocks both users on new jobs/bids), probability low but real.
- Mitigation E07 must implement: in the rate handler, one transaction that first locks the job row (`SELECT ... FOR UPDATE` on app.jobs) before reading the ratings, so submits for one job serialize. Verified by `Locking_the_job_row_in_a_transaction_closes_the_job_even_with_simultaneous_submits`. Also catch `DbUpdateException` on the unique index (same rater double submit) and map it to 409 like a DomainException. Optional safety net: an E07 job that reconciles Done jobs having two ratings.
- Notes for E07: a comment longer than 1000 characters passes the domain (out of scope) but the column is varchar(1000), so the handler must validate the length or the save fails with a 500. `AddRating` with a null accepted bid throws NullReferenceException, not DomainException (handler must load the bid and 404/409 before calling).
