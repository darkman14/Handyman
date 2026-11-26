---
id: BUG-0018
title: Two API hosts starting at once can each seed the sample jobs (check-then-insert is not serialised)
story: US-0006
criterion: AC5
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. Seeded users and workers exist, no jobs exist.
2. Start several hosts (or run `SeedDevSampleJobsStep` in several scopes) at the same time with `Seed:Enabled=true` in Development/Test.
3. Count `app.jobs`.

## Expected
"Sample jobs only when no jobs exist": exactly 3 jobs and 3 bids in total (the story seeds 3 bids: two on Ana's job, one accepted on Ivana's), however many hosts start.

## Actual
`SeedDevSampleJobsStep` does `db.Jobs.AnyAsync()` and later inserts, with no lock, advisory lock or unique guard between the two. Every host that passes the check before another commits inserts a full set. Reproduced with 4 concurrent scopes: 12 jobs in the database (sets are always whole and job numbers stay distinct, so no half data, only duplicates).

## Evidence
`US_0006_QaExploratoryTests.KNOWN_GAP_two_hosts_seeding_at_once_may_duplicate_the_sample_set_but_never_partially` pins the tolerant behavior (passes today; when fixed, tighten it to `Assert.Equal(3, jobs)`).
Impact is low: dev/test only, requires truly simultaneous starts (e.g. scaled-out test deployment).

## Suggested fix (optional)
Wrap check and insert in a transaction holding `pg_advisory_xact_lock(<constant>)`, or take that lock in `DevSeeder` for all steps.
