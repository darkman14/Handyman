---
id: US-0016
title: Every use case has one handler and reads never write
epic: E01
feature: F01.3
status: in-qa
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
| AC1 | pass (code and test level): `ArchitectureTests.Every_command_and_query_in_the_api_has_exactly_one_handler`, `Two_handlers_for_one_command_are_reported`, `US_0016_MutationTests.AC1_*` (also two handlers via inheritance) | n/a |
| AC2 | pass with gap, see BUG-0003 (low): `ArchitectureTests.No_query_handler_depends_on_AppDbContext...`, `US_0016_MutationTests.AC2_*` | n/a |
| AC3 | pass: `HandlerRegistrationTests.Every_handler_resolves_from_a_scope_wrapped_in_the_decorators_in_the_right_order`, `Decorators_and_open_generics_are_never_registered_as_handlers...` | n/a |
| AC4 | pass for SaveChanges/SaveChangesAsync and no tracking (`ReadDbContextTests`, `US_0016_ReadsNeverWriteTests.AC4_*`); fail for ExecuteUpdate/ExecuteDelete/ExecuteSql, BUG-0002 (medium): `AC4_ExecuteUpdate_...`, `AC4_ExecuteDelete_...`, `AC4_ExecuteSql_...` | n/a |

## Dev notes
- QA: the build, OpenAPI file (EOL only), SchemaTests and `dotnet ef migrations add --context AppDbContext` are unaffected; the generated migration was empty (ReadDbContext adds no schema change) and was deleted.
- QA: mutation checks of the AC2 rule. Caught: constructor parameter, primary constructor, field, property, `Func<>`, `Lazy<>`, `IDbContextFactory<>`, base class taking the wrong context (both directions), class implementing both handler interfaces. Not caught: `IServiceProvider`, plain `DbContext`, transitive dependency through a helper service (BUG-0003). Rule for the E03+ handlers: do not inject `IServiceProvider` or helper services that take a context into handlers until the rule is hardened.
- QA: AC1 rules catch a missing handler, two handlers, an internal handler and two handlers through inheritance. Registration is automatic by assembly scan, so a handler cannot be left unregistered.
- QA: no navigation properties exist in the model, so `Include` is not applicable today; `Find`, projection, joins and `FromSql` leave the change tracker empty. With auto-detect off, reads still see rows committed by other contexts.
- QA: tests in `Acceptance/US_0016_OneHandlerReadsNeverWriteTests.cs`; the three `AC4_Execute*` tests fail until BUG-0002 is fixed; the three `*_NOT_caught_known_gap` tests pass now and must be flipped when BUG-0003 is fixed.
