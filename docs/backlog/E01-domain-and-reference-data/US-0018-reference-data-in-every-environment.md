---
id: US-0018
title: Reference data is in place in every environment
epic: E01
feature: F01.4
status: ready
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
| AC1 | | n/a |
| AC2 | | n/a |
| AC3 | | n/a |
| AC4 | | n/a |
| AC5 | | n/a |

## Dev notes
