---
id: BUG-0003
title: AC2 rule does not see a handler that reaches the wrong context through IServiceProvider or plain DbContext
story: US-0016
criterion: AC2
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. Add a query handler whose constructor takes `IServiceProvider` and calls `sp.GetRequiredService<AppDbContext>()` (or a command handler that resolves `ReadDbContext` that way), or a handler taking plain `DbContext`.
2. Run `ArchitectureRules.ContextViolations` over it.

## Expected
Reported as a violation, or the rule documented as not covering it.

## Actual
No violation (0 reported). The rule inspects constructor parameters and instance fields (including base classes and generic arguments such as `Func<>`, `Lazy<>`, `IDbContextFactory<>`, which are caught) but not what is resolved at runtime.

## Evidence
- Documenting tests (pass today, flip to 1 when hardened): `US_0016_MutationTests.AC2_query_handler_with_IServiceProvider_is_NOT_caught_known_gap`, `AC2_command_handler_with_IServiceProvider_is_NOT_caught_known_gap`, `AC2_query_handler_taking_plain_DbContext_is_NOT_caught_known_gap`.
- Also not covered: a handler that depends on a helper service which itself takes the wrong context (transitive).

## Suggested fix (optional)
Fail any handler that takes `IServiceProvider` or plain `DbContext` (handlers do not need them), and make the rule follow constructor dependencies of services declared in the API assembly. Low severity: reviewers and the `rules/backend.md` convention catch it, and the first handlers (E01 and later) are simple.
