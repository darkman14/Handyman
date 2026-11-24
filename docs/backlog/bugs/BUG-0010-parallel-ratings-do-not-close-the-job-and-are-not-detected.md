---
id: BUG-0010
title: Two parties rating at the same moment leave the job Done (never Closed); the xmin token cannot detect it because the job row is not written
story: US-0017
criterion: AC2
severity: medium
status: open
found-by: qa-api
---

## Steps to reproduce
1. Job is `Done` with an accepted bid and no ratings.
2. Two contexts (two requests) each load the job, the accepted bid and the (empty) ratings list.
3. Context A calls `job.AddRating(clientId, ...)`, context B calls `job.AddRating(workerId, ...)`; each adds its rating and saves.

## Expected
Either the second save fails with `DbUpdateConcurrencyException` (409), or the job ends `Closed` once both ratings exist (US-0014: the job closes when both rated).

## Actual
Neither call sees the other's rating, so neither sets `Status = Closed`. The first rating does not modify the job row, so there is nothing for the xmin token to compare: both saves succeed. Result: two ratings stored, job stays `Done` forever (rating gate / closing logic never fires). The same happens whenever a command only inserts child rows (ratings) and relies on the job's state: the token only protects rows that are updated.

Related, not a token issue: the same user submitting twice in parallel hits the unique index `(job_id, rater_id)` and raises `DbUpdateException` (not mapped to 409, so a 500 today).

## Evidence
- Passing KNOWN_GAP test: `US_0017_QaConcurrencyTokenTests.KNOWN_GAP_BUG_0010_parallel_ratings_by_both_parties_leave_the_job_Done_not_Closed` (flip its assertions when fixed).

## Suggested fix (optional)
In the rating handler, make the job row part of the write: mark it modified (`Entry(job).Property(x => x.Status).IsModified = true`) or lock it (`select ... for update`) so the second rater either conflicts (409) or re-reads and closes the job. Also map the unique violation on `(job_id, rater_id)` to `already_rated` 409.

## Disposition
Deferred to E07 (rating handler), decided by the coordinator 2026-11-24: the fix belongs in the handler (lock the job row with SELECT ... FOR UPDATE or mark it modified, and map the unique (job_id, rater_id) violation to 409 `already_rated`). Not a blocker for US-0017, whose AC2 is met for the rows it updates. Must become an acceptance criterion when E07 is planned.
