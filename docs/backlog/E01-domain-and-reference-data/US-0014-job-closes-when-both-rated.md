---
id: US-0014
title: A job closes only when both sides have rated
epic: E01
feature: F01.2
status: ready
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
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |
| AC5 | | n/a |

## Dev notes
