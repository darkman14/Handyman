---
id: US-0020
title: Anyone can browse areas level by level
epic: E01
feature: F01.4
status: ready
---

## Story
As a client posting a request, I want to find my neighborhood by first picking my city and then my municipality, so that I can say where the job is quickly and workers who cover that neighborhood are the ones who see it.

## Acceptance criteria
- **AC1** Given the reference data from US-0018, when anyone calls `GET /api/areas` without `parentId` (or with an empty `parentId=`) and without a token, then the response is `200` with the top level only: the city Beograd, with its id, name and kind `city`.
- **AC2** Given Beograd's id, when `GET /api/areas?parentId=<Beograd id>` is called, then the response is `200` with Novi Beograd, Vračar and Zvezdara, each of kind `municipality`, and nothing else.
- **AC3** Given Vračar's id, when `GET /api/areas?parentId=<Vračar id>` is called, then the response is `200` with exactly Vračar's six neighborhoods, each of kind `neighborhood`, sorted by name in Serbian Latin alphabetical order (Crveni krst, Čubura, Englezovac, Kalenić, Krunski venac, Neimar).
- **AC4** Given a neighborhood's id, or an id that matches no area, when it is used as `parentId`, then the response is `200` with an empty list.
- **AC5** Given a `parentId` that is not a valid id (e.g. `abc`), when it is sent, then the response is `400` with a problem-details body whose `code` is `validation_failed` and names `parentId` (US-0017).

## Business rules
- `GET /areas?parentId=` is open to anyone; areas load level by level (workflow.md section 4).
- Areas in three levels, city → municipality → neighborhood (workflow.md section 1, Location; section 3).
- Blocked users get `403 user_blocked` on every API call (workflow.md section 2); applies with a blocked user's token once US-0002 is built.
- Public endpoint: an expired or invalid token is treated as anonymous and the call still returns `200` (US-0019 decision 2026-11-10); a valid token of a blocked user gets `403 user_blocked` once US-0002 is built.
- Unknown or leaf `parentId` returns `200` with an empty list, no new error code (decision 2026-11-10).
- Areas within a level are sorted by name in Serbian Latin alphabetical order; trades use a fixed order instead (decision 2026-11-10).

## UI notes
- No UI in this story. The area picker (city → municipality → neighborhoods; picking a municipality selects all its neighborhoods) is E03; the neighborhood picker on the new request form is E04.

## Out of scope
- Search by name, returning the whole tree in one call, or showing parents of a neighborhood.
- Showing areas in the app (E03, E04).
- Adding or editing areas.

## Test notes
- Seeded users: none needed; Ana Marković +381600000201 for a signed-in call once E02 is done; Petar Stojanović +381600000204 blocked for the `403` check after US-0002.
- Edge cases: sort order puts Č after C and Đ after D (Zvezdara: Cvetkova pijaca, Đeram, Konjarnik, Mali Mokri Lug, Mirijevo); diacritics survive JSON and URL round trips; `parentId=` empty (top level, AC1); an expired or garbage token (`200`, same as anonymous); `parentId` passed twice; a municipality id from one call works in the next call without change.

## Open questions
- none

## Decisions
- 2026-11-10: an unknown `parentId` returns `200` with an empty list (AC4); the picker shows nothing either way and no new error code is needed.
- 2026-11-10: an empty `parentId=` is the same as no `parentId` (top level, AC1).
- 2026-11-10: areas are sorted by name in Serbian Latin alphabetical order (AC3); trades use a fixed order (US-0019).
- 2026-11-10: `GET /areas` is built in E01, moved from slice 3 (US-0019 decision).

## Dependencies
- Depends on US-0018 (the area tree) and US-0017 (`400 validation_failed` for AC5).

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
