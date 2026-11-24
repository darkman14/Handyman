---
id: US-0015
title: Commands save all or nothing and push only after saving
epic: E01
feature: F01.3
status: done
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
| AC1 | pass (api): AC1_changes_on_several_entities_in_one_save_are_all_committed; DecoratorTests.Success_commits_every_change_and_returns_the_handler_result_unchanged | n/a |
| AC2 | pass (api): AC2_rollback_removes_changes_of_several_entities_and_several_saves; AC2_second_context_resolved_from_the_same_scope_...; AC2_original_exception_stack_trace_is_kept | n/a |
| AC3 | pass (api): AC3_pushes_arrive_in_queued_order_exactly_once_each; AC3_a_queue_that_throws_after_commit_...; DecoratorTests.Pushes_reach_the_queue_once_each_and_only_after_the_commit | n/a |
| AC4 | pass (api): AC4_a_commit_that_fails_in_the_database_drops_pushes_...; AC4_an_already_cancelled_token_...; Pushes_of_a_failed_command_do_not_leak_...; Parallel_scopes_do_not_share_pending_pushes | n/a |
| AC5 | pass (api): AC5_logs_hold_use_case_name_...; DecoratorTests.Query_slower_than_500_ms_logs_a_warning, Command_logs_* | n/a |

## Dev notes
- QA (qa-api, 2026-11-24): reviewed at code and test level; tests in `Acceptance/US_0015_CommandsAllOrNothingTests.cs` (14 added, 160 backend tests pass). No bugs filed. `dotnet build backend` leaves `docs/api/Handyman.Api.json` content unchanged (line endings only).
- Verified: real deferred-FK commit failure -> rollback, no push, DB error surfaces; push queue throwing after commit -> command still succeeds, later pushes still delivered, error logged with type only; PendingPushes does not leak between sequential commands in one scope or parallel scopes; no mediator package in csproj; logging outermost; a missing handler throws InvalidOperationException naming the type.
- Limit 1: only the scope's `AppDbContext` is in the transaction. A second context resolved from the scope is the same instance (rolled back); a `new AppDbContext(...)` made by a handler is NOT rolled back (test `AC2_limit_a_hand_made_second_context_is_NOT_rolled_back`). Worth an architecture rule in US-0016.
- Limit 2: `EnableRetryOnFailure` is not set today. If it is ever enabled, `BeginTransactionAsync` throws on every command (user-initiated transaction under a retrying execution strategy), i.e. all commands would fail (high impact, but loud, not silent). Require then: wrap the work in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` inside the decorator, and clear PendingPushes at the start of each attempt. No `ReadDbContext` exists yet; query side untested against it.
- Limit 3 (low): if cancellation fires during COMMIT itself the outcome is ambiguous: the DB may have committed while the caller gets OperationCanceledException and pushes are dropped. Accepted trade-off (push lost, never sent for unsaved data); worth knowing for E08.
- Limit 4 (low): two handlers for one command silently resolve to the last registered; a nested command run from a handler fails because the transaction is already open; `PushQueue` is in-memory and unbounded, pushes are lost on restart (E08 scope).
