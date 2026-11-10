---
id: US-0015
title: Commands save all or nothing and push only after saving
epic: E01
feature: F01.3
status: ready
---

## Story
As a worker, I want a notification like "You were hired" to reach me only when the hire was really saved, and never a half-saved job, so that I don't drive to a job that isn't mine.

## Acceptance criteria
- **AC1** Given a command whose handler saves several changes and succeeds, when it runs, then all changes are in the database afterwards and the caller gets the handler's result unchanged.
- **AC2** Given a command whose handler saves a change and then throws, when it runs, then none of its changes are in the database and the caller gets the same exception the handler threw.
- **AC3** Given a command that queues pushes and succeeds, when it runs, then each queued push is handed to the push queue exactly once, and only after the changes are committed.
- **AC4** Given a command that queues pushes and then fails (exception or rolled-back save), when it runs, then no push from it is ever handed to the push queue.
- **AC5** Given any command or query runs, when the logs are read, then there is an entry with the use case name, its duration and its outcome (success or failure); a query slower than 500 ms is logged as a warning.

## Business rules
- Command decorators, outermost first: logging → transaction → handler; query decorators: logging → handler (workflow.md section 5, CQRS).
- Pushes are queued with `PendingPushes` during the command and flushed to `PushQueue` after commit, so no push is sent for a rolled-back change (workflow.md section 5, CQRS; `.claude/rules/backend.md`).
- No MediatR or other mediator library; registration via `AddCqrs` by assembly scanning (workflow.md section 1, CQRS; section 5).

## UI notes
- No UI.

## Out of scope
- Actually sending pushes (Expo, Web Push), the `PushWorker` and notification rows (E08). Here the push queue only has to receive what is flushed.
- Mapping exceptions to HTTP responses (US-0017).
- Architecture rules about handlers (US-0016).

## Test notes
- Covered by `DecoratorTests` (workflow.md section 5, "Tests for the CQRS code itself"), using test-only commands and queries and a fake or in-memory push queue; database assertions against Testcontainers Postgres.
- Seeded users: none.
- Edge cases: a command that succeeds without queuing pushes (nothing handed over); a command that queues the same push twice (handed over twice, as queued); a cancelled request (`CancellationToken` fired mid-command) behaves like a failure; logs must not contain request bodies with phone numbers or addresses.

## Open questions
- none

## Decisions
- 2026-11-10: the human reviewed E01 and accepted all proposed answers; no product decision was open in this story.

## Dependencies
- None; DecoratorTests use test-only commands and queries.

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
