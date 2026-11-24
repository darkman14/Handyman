---
id: BUG-0012
title: ReferenceData Down deletes a foreign neighborhood on a reference id, and fails when a foreign area is a child of one
story: US-0018
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
A. Migrate to `AddXminConcurrencyTokens`; insert an unrelated area `Foreign hood` (kind Neighborhood) with id 7; apply `ReferenceData` (it adds `Englezovac` under the next free id); roll back to `AddXminConcurrencyTokens`.
B. Migrate fully; insert an area `Block A` (Neighborhood) with `parent_id = 6` (Čubura); roll back to `AddXminConcurrencyTokens`.

## Expected
Down removes only the rows this migration added and is never blocked by other data: foreign rows stay, Down succeeds.

## Actual
A. `DELETE FROM app.areas a WHERE a.id BETWEEN 5 AND 20 AND a.kind = 'Neighborhood' AND NOT EXISTS (...)` matches by id range and kind, not name, so the foreign `Foreign hood` row is deleted (data loss), while the real `Englezovac` (moved to another id) stays behind.
B. The neighborhood delete does not check `areas.parent_id` references, so Postgres raises 23503 (`fk_areas_areas_parent_id`) and the whole Down fails (transaction rolled back, nothing lost).
Same gap for the clash-moved rows: reference rows that got ids outside 1-6 / 1-20 are never removed by Down.

## Evidence
- Pinned as passing KNOWN_GAP tests: `US_0018_QaReferenceDataMigrationTests.KNOWN_GAP_BUG_0012_down_deletes_a_foreign_neighborhood_that_sits_on_a_reference_id` and `KNOWN_GAP_BUG_0012_down_fails_when_a_foreign_area_is_a_child_of_a_reference_neighborhood`.
- Trades are fine (the Down matches the name too, and the test `Down_never_deletes_foreign_trades_that_sit_on_the_reference_ids` passes).

## Suggested fix (optional)
Match neighborhoods by `(id, name, kind)` against the same VALUES list as Up (join on the parent name), and add `AND NOT EXISTS (SELECT 1 FROM app.areas c WHERE c.parent_id = a.id)` to the neighborhood delete. Impact is low: Down is rarely run, and only on databases that had id clashes.
