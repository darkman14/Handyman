---
id: US-0013
title: A job moves only along the allowed lifecycle
epic: E01
feature: F01.2
status: done
---

## Story
As a client, I want a job to follow one clear path from posted to hired to finished, so that I never end up with two hired workers, a cancelled job that still gets work done, or a finished job that suddenly reopens.

## Acceptance criteria
- **AC1** Given a job is created, when it is read, then its status is Open, its creation time is set in UTC, and it has no accepted bid, finish time or close time.
- **AC2** Given an Open job with active bids from Jelena and Nikola, when the client hires Jelena's bid, then the job is Assigned, its accepted bid is Jelena's, Jelena's bid is `accepted`, Nikola's bid is `rejected`, and any `withdrawn` bid stays `withdrawn`.
- **AC3** Given a job, when a hire is attempted on a job that is not Open, or with a bid that belongs to another job, or with a bid that is `withdrawn` or `rejected`, then it is refused with a domain error and the job and its bids are unchanged.
- **AC4** Given an Assigned job, when it is finished, then its status is Done and its finish time is set in UTC; given a job in any other status (Open, Done, Closed, Cancelled), when finish is attempted, then it is refused with a domain error and nothing changes.
- **AC5** Given an Open or Assigned job, when it is cancelled, then its status is Cancelled, every bid keeps the status it had (active, accepted, rejected or withdrawn) and no cancellation time is recorded; given a Done, Closed or Cancelled job, when cancel is attempted, then it is refused with a domain error and nothing changes.

## Business rules
- Lifecycle: Open → Assigned (client hires a bid) → Done (either side finishes) → Closed (both rated); Open or Assigned → Cancelled (workflow.md section 2, Job lifecycle).
- State transitions only through `Job` methods (`Hire`, `Finish`, `Cancel`); invalid transitions throw `DomainException`, which the API maps to `409` (workflow.md section 5, Backend conventions; `.claude/rules/backend.md`).
- When a bid is hired, other bidders are "not selected" (workflow.md section 2, Assigned), recorded as bid status `rejected` (workflow.md section 3).
- All times UTC (workflow.md section 1, Database).
- Cancelling a job leaves its bids' statuses unchanged; bidders learn of it from the job's Cancelled status ("My bids" shows job status, workflow.md section 4). `accepted`/`rejected` only ever mean the hire decision (decision 2026-11-10).
- No `cancelled_at` for the MVP (decision 2026-11-10).

## UI notes
- No UI. Hire, finish and cancel buttons come in E06 and E07.

## Out of scope
- Who is allowed to call hire, finish or cancel (client vs. hired worker), and the HTTP endpoints (E06, E07).
- Two hires racing (concurrency token, `409 job_not_open`) (E06).
- Ratings and closing (US-0014).
- Notifications to bidders on hire or cancel (E08).
- Withdrawing a bid (E05).
- A cancellation time or a "cancelled" bid status.

## Test notes
- Covered by `JobTests` (workflow.md section 7, slice 1): one test per allowed transition and one per forbidden transition, named after the behavior.
- Seeded users: none; use the seeded cast's names in test data for readability (Ana as client, Jelena and Nikola as bidders).
- Edge cases: hire on a job with no bids at all; hire the same bid twice; cancel an Assigned job (allowed; the accepted bid stays `accepted`, the others stay `rejected`) and then try to finish it (refused); finish twice; a job with only withdrawn bids can't be hired; transition matrix: every status × every action.

## Open questions
- none

## Decisions
- 2026-11-10: when a job is cancelled, its bids keep their status; the job's Cancelled status is what bidders see ("My bids" shows job status). `accepted`/`rejected` mean the hire decision only (now in AC5).
- 2026-11-10: no `cancelled_at` for the MVP; workflow.md section 3 has no such field and nothing in the MVP shows it (now in AC5).

## Dependencies
- Depends on US-0011 (jobs and bids tables and statuses).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass (domain): JobTests.New_job_is_open_...; US_0013 Persisted_new_job_reloads_open_with_utc_creation_time_and_no_accepted_bid_finish_or_close (Postgres) | n/a |
| AC2 | pass (domain): Hiring_a_bid_assigns_...; US_0013 Hire_then_finish_survive_save_and_reload_on_postgres, Hire_tolerates_duplicate_entries_in_the_bid_list, Hire_leaves_already_rejected_and_withdrawn_bids_as_they_were | n/a |
| AC3 | pass (domain): US_0013 Hire_is_refused_unchanged_for_every_non_open_status, Hire_is_refused_for_a_bid_that_is_not_active_..., Hire_is_refused_for_a_bid_of_another_job, Hire_with_a_foreign_bid_in_the_list_changes_nothing_..., Refused_hire_followed_by_save_changes_writes_nothing | n/a |
| AC4 | pass (domain): US_0013 Finish_is_refused_in_every_status_but_assigned_..., Finish_stores_utc_for_non_utc_input_at_the_same_instant, Hire_then_finish_survive_save_and_reload_on_postgres | n/a |
| AC5 | pass (domain): US_0013 Cancel_is_refused_for_done_closed_and_cancelled_..., Cancelled_assigned_job_keeps_bid_statuses_..., Cancelled_open_job_leaves_active_bids_active, Cancel_after_hire_persists_cancelled_and_leaves_bid_rows_unchanged | n/a |

## Dev notes
