---
id: US-0018
title: Reference data is in place in every environment
epic: E01
feature: F01.4
status: done
---

## Story
As a client posting a repair, I want the list of trades and the neighborhoods of my city to already be there, so that I can describe what's broken and where without typing it myself, and workers who cover my neighborhood get my job.

## Acceptance criteria
- **AC1** Given a freshly migrated database in any environment (Development, Test or Production), when the trades are read, then there are exactly six: Appliance repair, Electrical, Plumbing, Heating & AC, Painting, Carpentry.
- **AC2** Given a freshly migrated database, when the areas are read, then there is one city, Beograd, with the municipalities Vračar, Novi Beograd and Zvezdara as its children, and each municipality's neighborhoods as listed in Business rules; every neighborhood's parent is a municipality and every municipality's parent is the city.
- **AC3** Given a freshly migrated database, when the stores are read, then there is one store, Home Center Vračar, with referral code `VRACAR01`, located in the neighborhood Čubura.
- **AC4** Given the reference data is in place, when the API restarts or migrations are applied again, then no trade, area or store is duplicated and their ids are unchanged.
- **AC5** Given an environment where the dev seeder is disabled or refused (`Seed:Enabled` false or missing, or Production), when the database is migrated, then all reference data from AC1–AC3 is present anyway (it never depends on the dev seeder).

## Business rules
- Areas: city → municipality → neighborhood; seeded by hand for the pilot city; placeholder Belgrade areas until the pilot city is chosen (workflow.md section 1, Location; section 7, "Seeded development data"; section 11).
- Trades in the MVP: appliances, electrical, plumbing are the focus; painting and carpentry allowed (workflow.md section 1, MVP scope). The six names are the ones used by the seeded workers (workflow.md section 7).
- Seeded store: Home Center Vračar, referral code `VRACAR01` (workflow.md section 7).
- Placeholder neighborhoods (decision 2026-11-10):
  - Vračar: Crveni krst, Čubura, Englezovac, Kalenić, Krunski venac, Neimar
  - Novi Beograd: Bežanijska kosa, Fontana, Ledine, Paviljoni, Tošin bunar
  - Zvezdara: Cvetkova pijaca, Đeram, Konjarnik, Mali Mokri Lug, Mirijevo
- These include every neighborhood the seeded cast lives in (Čubura, Neimar, Krunski venac, Bežanijska kosa, Đeram), which US-0003 and US-0006 need.
- Trades, areas and the placeholder store `VRACAR01` go to every environment, Production included; all placeholders are reviewed before the pilot launch (decision 2026-11-10).
- The store's neighborhood is Čubura (Vračar) (decision 2026-11-10).

## UI notes
- No UI. Trades and areas are shown through US-0019 and US-0020, and in pickers in E03 and E04.

## Out of scope
- Seeded users, worker coverage and sample jobs (E02: US-0003, US-0005, US-0006).
- Store referral behavior (E09) and admin editing of reference data (not planned for the MVP).
- The real pilot city and store (workflow.md section 11); replacing the placeholders is a later story.
- Serbian translations of trade names.

## Test notes
- Seeded users: none.
- Edge cases: names keep their diacritics (Čubura, Đeram, Tošin bunar) end to end; "Novi Beograd" (municipality) and "Beograd" (city) must not be confused; no neighborhood has children; no area is its own ancestor; the store's area is a neighborhood, not the municipality; restart three times and count rows (AC4); check on the shared test environment after deploy.

## Open questions
- none

## Decisions
- 2026-11-10: placeholder areas are the three municipalities the seeded cast uses (Vračar, Novi Beograd, Zvezdara) with the 16 neighborhoods listed in Business rules, not all of Belgrade; replaced when the pilot city and neighborhood level are decided (workflow.md section 11).
- 2026-11-10: reference data goes to every environment, Production included: trades and areas because the app can't work without them, and the placeholder store `VRACAR01` until the real pilot store is chosen (Production isn't live before E11). Review all placeholders before the pilot launch.
- 2026-11-10: the store is in Čubura (Vračar); nothing depends on it before E09.

## Dependencies
- Depends on US-0011 (trades, areas and stores tables) and US-0012 (unique referral code).

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass (api): US_0018_ReferenceDataTests.There_are_exactly_the_six_trades_in_the_story_order_with_stable_ids; US_0018_QaReferenceDataMigrationTests.Fresh_database_has_exactly_the_expected_rows_byte_for_byte | n/a |
| AC2 | pass (api): US_0018_ReferenceDataTests.Areas_are_one_city_three_municipalities_and_sixteen_neighborhoods, Every_neighborhood_has_a_municipality_parent..., No_area_is_its_own_ancestor; US_0018_QaReferenceDataMigrationTests.Fresh_database_has_exactly_the_expected_rows_byte_for_byte (names byte for byte, NFC, ids, parents) | n/a |
| AC3 | pass (api): US_0018_ReferenceDataTests.There_is_one_store_Home_Center_Vracar_with_code_VRACAR01_in_the_neighborhood_Cubura; US_0018_QaReferenceDataMigrationTests.The_migrated_store_code_cannot_be_registered_again_in_any_case_or_spacing | n/a |
| AC4 | pass (api) on a fresh database: US_0018_ReferenceDataTests.Migrating_again_and_restarting_three_times_changes_nothing; US_0018_QaReferenceDataMigrationTests.Migrate_is_idempotent_and_ids_do_not_change, Down_with_referenced_rows_keeps_them_and_does_not_fail_and_Up_again_adopts_them. Known gap in Down only: BUG-0012 (low) | n/a |
| AC5 | pass (api): US_0018_ReferenceDataTests.Reference_data_is_there_when_the_dev_seeder_is_off_missing_or_refused, A_fresh_database_migrated_by_ef_alone_has_all_reference_data. BUG-0011 (duplicate parent names) fixed and verified: the migration no longer aborts and the tree is complete in every collision scenario | n/a |

## Dev notes

### QA (qa-api, 2026-11-24)
- Verified on throwaway Postgres 17 containers: fresh content byte for byte (hex of UTF-8, precomposed forms, ids, parents, store in Čubura); sequences give 7 / 21 / 2 next; schema, RLS (on, no policies) and constraints/indexes/columns identical to before the migration; `has-pending-model-changes` clean; OpenAPI file unchanged.
- Collisions handled correctly: unrelated rows on ids 1-6 / 1-20 / store 1 (reference rows take later ids, foreign rows untouched, next inserts work); same-name rows in the right place adopted with FKs (users, worker rows, jobs) intact; store code existing as `vracar01`, padded, with inner space, NBSP or tab is respected (no second store); a foreign `Čubura` elsewhere does not mislead the store area.
- BUG-0011 (medium) fixed and verified 2026-11-24 (parents resolved through settled ids, lowest matching id adopted; see the bug file for the scenarios). Open: BUG-0012 (low): Down deletes a foreign neighborhood on a reference id and fails when a foreign area is a child of one; pinned by passing KNOWN_GAP tests.
- Behavior to know: names are matched exactly (a trade `plumbing` does not count as `Plumbing`); after an id clash the reference rows keep working but have other ids than 1-6 / 1-20, and Down will not remove them.
- Local Supabase DB (read-only checks): migration is already applied. Reference ids differ from the story: trades 1045-1050 (ids 1-6 are junk trades named `read-...`), areas 103-122 (ids 1-102 and 123-138 are junk neighborhoods named `A<hex>`), store id 1 VRACAR01 in Čubura (area 108, parent Vračar 104). No duplicates, tree valid, RLS on. 1010 trades and 138 areas, 244 users, 118 jobs. A full test run after the ApiFactory fix left these counts unchanged, so tests no longer write there. Recommendation: `supabase db reset` once, so the local DB matches a fresh environment (ids 1-6 / 1-20); not required for correctness.
- Test review: the ApiFactory `ConfigureAppConfiguration` change is right (in-memory source wins over appsettings.Local.json). US_0012 AC4 theory VRACAR01 -> CODEONE1 was needed because the migrated DB now owns VRACAR01; case/space coverage is kept by CODEONE1, ZEMUN01, NBG01 and extended by `The_migrated_store_code_cannot_be_registered_again_in_any_case_or_spacing` (6 spellings incl. NBSP/tab on the real VRACAR01 row). No coverage lost.
- Not testable here: Production-like start with `Database:MigrateOnStartup` unset does not migrate by design (the host only migrates in Development or when the flag is true); the AC5 Production cases set the flag. The shared test environment check after deploy remains a manual step.
