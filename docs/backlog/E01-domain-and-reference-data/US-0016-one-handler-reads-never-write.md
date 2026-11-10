---
id: US-0016
title: Every use case has one handler and reads never write
epic: E01
feature: F01.3
status: ready
---

## Story
As a developer on the team, I want the build to catch a use case with no handler, two handlers, or a read that writes, so that every endpoint we add later behaves predictably and "just looking" at data can never change it.

## Acceptance criteria
- **AC1** Given the API assembly, when `ArchitectureTests` run, then every command and every query type has exactly one handler; adding a command or query without a handler, or with two, makes a test fail.
- **AC2** Given the API assembly, when `ArchitectureTests` run, then no query handler depends on the command database context and no command handler depends on the read database context; a test fails otherwise.
- **AC3** Given the API started by the test host (`ApiFactory`), when each handler is resolved from its container, then it resolves and is wrapped by the decorators from US-0015; the decorators themselves are never registered as handlers.
- **AC4** Given a query handler, when it tries to save anything through the read database context, then it fails with an error and nothing is written; data it reads is not tracked for changes.

## Business rules
- Commands use `AppDbContext` and domain methods; queries use `ReadDbContext` (CLAUDE.md hard rules; workflow.md section 5).
- `ReadDbContext` uses the same model and connection, no tracking, and throws `InvalidOperationException` on `SaveChanges` and `SaveChangesAsync` (workflow.md section 5, CQRS).
- `ArchitectureTests` use plain reflection, no extra package (workflow.md section 5, "Tests for the CQRS code itself").

## UI notes
- No UI.

## Out of scope
- The decorators' behavior (US-0015) and error mapping (US-0017).
- The first real queries (`GET /api/trades`, `GET /api/areas`: US-0019, US-0020), which these tests then cover automatically.

## Test notes
- Seeded users: none.
- Edge cases: a handler implementing two handler interfaces; an internal (non-public) handler class is still found; the tests must pass both with zero real handlers and after US-0019/US-0020 add the first ones; the read context's synchronous and asynchronous save both fail.

## Open questions
- none

## Decisions
- 2026-11-10: the human reviewed E01 and accepted all proposed answers; no product decision was open in this story.

## Dependencies
- Depends on US-0015 (AC3 checks handlers are wrapped by its decorators).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |

## Dev notes
