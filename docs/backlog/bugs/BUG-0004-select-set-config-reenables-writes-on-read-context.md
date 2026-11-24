---
id: BUG-0004
title: ReadDbContext writes again after SELECT set_config('default_transaction_read_only','off') on an open connection
story: US-0016
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. `var read = scope.GetRequiredService<ReadDbContext>(); await read.Database.OpenConnectionAsync();` (keeps one connection, so the open-time read-only setting is not re-applied).
2. `await read.Database.SqlQueryRaw<string>("select set_config('default_transaction_read_only','off',false) as \"Value\"").ToListAsync();` (a SELECT, passes the command guard).
3. `await read.Trades.FromSqlRaw("with d as (delete from app.trades where name = {0} returning *) select * from d", name).ToListAsync();` (starts with WITH, passes the guard).
4. Read the row back with `AppDbContext`.

## Expected
The row is still there; the read context cannot write.

## Actual
The row is deleted. The guard only checks the first keyword, and the session setting can be flipped back by a plain SELECT. Without step 1 (connection closed between commands) the setting is re-applied on every open and the attempt fails; raw `GetDbConnection()` commands can also run `SET ... READ WRITE`.

## Reachability
Only deliberately malicious or very careless code (raw SQL that calls set_config, then a data-modifying CTE). Normal handlers (projections, FromSql SELECT/WITH, Find, counts) cannot hit it. Hence low.

## Evidence
- Failing test: `US_0016_ReadOnlyHardeningTests.Select_set_config_then_cte_delete_on_one_open_connection_must_not_write`.
- Checked and NOT bypasses: ExecuteUpdate/ExecuteDelete/ExecuteSql*, WITH ... DELETE ... RETURNING, leading comment/BOM/whitespace, multiple statements, volatile function that deletes, FOR UPDATE, DDL, COPY, SET via FromSql, BEGIN READ WRITE, explicit transaction, pooled connection reuse.

## Suggested fix (optional)
Also reject, in the command guard, command text containing `set_config`, `SET `/`RESET`/`BEGIN`/`COMMIT` keywords (fail closed), or use a Postgres role with SELECT-only grants for ReadDbContext (strongest; cannot be flipped by the session).
