---
id: BUG-0002
title: ReadDbContext can still write through ExecuteUpdate, ExecuteDelete and Database.ExecuteSql
story: US-0016
criterion: AC4
severity: medium
status: open
found-by: qa-api
---

## Steps to reproduce
1. Resolve `ReadDbContext` from a scope (as a query handler would).
2. Run `read.Trades.Where(t => t.Name == x).ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "y"))`, or `...ExecuteDeleteAsync()`, or `read.Database.ExecuteSqlAsync($"delete from app.trades where name = {x}")`.
3. Read the row back with `AppDbContext`.

## Expected
Any write through the read context fails with an error and nothing is written ("reads never write"; the story's stated purpose is that "just looking" can never change data).

## Actual
All three execute immediately on the connection and change the data. Only `SaveChanges*` is overridden, and these APIs do not go through it. The app database role can write to `app` tables, so the database does not stop it either.

## Evidence
- Failing tests (in `Acceptance/US_0016_OneHandlerReadsNeverWriteTests.cs`): `AC4_ExecuteUpdate_through_the_read_context_must_not_write`, `AC4_ExecuteDelete_through_the_read_context_must_not_write`, `AC4_ExecuteSql_through_the_read_context_must_not_write`.
- Same applies to `ExecuteSqlRaw`, `ExecuteSqlInterpolated` and their async versions (same code path as `ExecuteSql`).
- The literal AC4 text (SaveChanges) passes; this is a gap in the intent.

## Suggested fix (optional)
Defense in depth, pick one or combine:
1. Database level (strongest, covers raw SQL): make the read context's connection read-only, for example `Options Target Session Attributes`/`default_transaction_read_only=on` in the connection string used for `ReadDbContext` (Npgsql: `Options='-c default_transaction_read_only=on'`), or a separate Postgres role with SELECT only.
2. EF level: add an `IDbCommandInterceptor` on `ReadDbContext` that throws `InvalidOperationException` when the command text is not a SELECT/WITH read, and for non-query commands (`NonQueryExecuting`/`NonQueryExecutingAsync`). Queries stay allowed, `ExecuteUpdate`/`ExecuteDelete`/`ExecuteSql` fail.
3. Architecture test: ban `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql*` in query handlers (reflection over IL is awkward; a source text scan like the hand-made context rule is enough).
Option 1 or 2 plus the three tests above turning green is the real fix.
