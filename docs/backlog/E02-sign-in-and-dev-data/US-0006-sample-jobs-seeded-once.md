---
id: US-0006
title: Sample jobs are seeded once
epic: E02
feature: F02.2
status: ready
---

## Story
As a developer or QA engineer, I want a few sample jobs in typical states seeded on first start, so that bid comparison, the "no bids yet" state, address privacy and the rating gate can be demoed and tested without creating data by hand.

## Acceptance criteria
- **AC1** Given a database with no jobs, when the seeder runs, then Ana Marković has one `Open` Appliance repair job ("dishwasher") in Čubura, with a title and a description, and exactly two `active` bids: one from Jelena Petrović and one from Nikola Đorđević, each with an arrival window (start and end in UTC, end after start), a diagnostic fee and an hourly rate in whole RSD.
- **AC2** Given a database with no jobs, when the seeder runs, then Milan Kostić has one `Open` Electrical job in Bežanijska kosa with no bids.
- **AC3** Given a database with no jobs, when the seeder runs, then Ivana Nikolić has one `Done` Appliance repair job in Đeram, hired to Jelena Petrović (her bid `accepted`, `finished_at` set), with no ratings from either side.
- **AC4** Given the seeder has run, when any sample job is read, then it has neighborhood, street and house number, and also entrance, floor, apartment and an address note filled, so address privacy can be tested on every field.
- **AC5** Given at least one job already exists (seeded or created by a tester), when the API restarts, then no sample job or bid is added, changed or removed.

## Business rules
- `DevSampleJobs` adds sample jobs only when no jobs exist (workflow.md section 7, slice 2).
- Job states follow the lifecycle Open → Assigned → Done → Closed (workflow.md section 2, Job lifecycle); a `Done` job has a hired worker.
- One bid per worker per job (workflow.md section 2, Business rules).
- Jobs point at neighborhoods; bidders cover the job's neighborhood and trade (Jelena and Nikola cover Vračar with Appliance repair; Jelena covers Zvezdara) (workflow.md section 7, "Seeded development data").
- Money is whole RSD (workflow.md section 1, Database).
- The rating gate (workflow.md section 2) means Ivana and Jelena are blocked from posting and bidding once E07 is built; Jelena's seeded bid on Ana's job predates the gate and stays.

## UI notes
- No new screens. Jobs show up in the app from E04 (My requests) and E05 (Open jobs).

## Out of scope
- Photos on sample jobs.
- Seeded ratings, notifications or store referrals on jobs.
- Jobs for Petar Stojanović and Mila Pavlović (they have none by design).
- A sample job in the `Assigned` state: testers hire a bidder on Ana's job to reach it.

## Test notes
- Seeded users: Ana Marković +381600000201, Milan Kostić +381600000202, Ivana Nikolić +381600000203 (clients); Jelena Petrović +381600000102, Nikola Đorđević +381600000103 (workers); Marko Jovanović +381600000101 is the matched worker for Milan's job.
- Edge cases: delete all jobs and restart → the three sample jobs come back; delete only one sample job and restart → nothing comes back (AC5); jobs have distinct, increasing job numbers; arrival windows end after they start; created/finished times are in UTC and `finished_at` is after `created_at`.

## Open questions
- none

## Dependencies
- Depends on US-0011 (jobs and bids tables, job title and description, arrival window as a UTC start/end range), US-0012 (job numbers, one bid per worker), US-0013 (Done job with an accepted bid), US-0018 (trades and area tree), US-0003 (clients) and US-0005 (workers).

## Decisions
- 2026-11-10: no `Assigned` sample job for now; the seeded data table in workflow.md stays as is, and "hired worker sees full address" is tested by hiring on Ana's job first.

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
