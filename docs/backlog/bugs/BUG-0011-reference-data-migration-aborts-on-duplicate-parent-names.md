---
id: BUG-0011
title: ReferenceData migration aborts (and blocks API startup) when areas already has two rows that match a parent name
story: US-0018
criterion: AC5
severity: medium
status: open
found-by: qa-api
---

## Steps to reproduce
1. On a throwaway Postgres, migrate to `20261124164959_AddXminConcurrencyTokens`.
2. Insert a second municipality named `Vračar` under another city (`INSERT INTO app.areas (name, kind, parent_id) ...`), or two cities named `Beograd` (there is no unique constraint on areas).
3. Apply `20261124180000_ReferenceData` (`Database.Migrate()`, which Development and Test run at startup).

## Expected
The migration succeeds (the story's data is added or adopted, foreign rows are left alone), as it does for every other collision the migration handles (id clashes, same names in the right place, colliding referral codes).

## Actual
Postgres error 21000 "more than one row returned by a subquery used as an expression" at the `parent := CASE r.kind ...` assignment. The whole migration rolls back (no trade, area or store added), `Migrate()` throws, and the API does not start in Development, or in any environment with `Database:MigrateOnStartup`. The parent lookups (`SELECT id FROM app.areas WHERE kind = 'Municipality' AND name = r.parent_name`, and the store's area lookup with the join on `m.name = 'Vračar'`) are not restricted to the rows this migration owns (the Beograd tree) and do not guarantee a single row.

## Evidence
- Failing test (now pinned as passing KNOWN_GAP tests): `US_0018_QaReferenceDataMigrationTests.KNOWN_GAP_BUG_0011_a_foreign_municipality_named_Vracar_under_another_city_aborts_the_migration` and `KNOWN_GAP_BUG_0011_two_existing_cities_named_Beograd_abort_the_migration`.
- A foreign `Čubura` under another parent does not trigger it (the store lookup there is correct).

## Suggested fix (optional)
Resolve the parent as the row in the tree being built (city: `kind='City' AND name='Beograd' ORDER BY id LIMIT 1`; municipality: join on that city id; neighborhood: join on the municipality id), i.e. add `ORDER BY id LIMIT 1` and constrain the municipality lookup by `parent_id = city_id`; same for the store's area lookup.
