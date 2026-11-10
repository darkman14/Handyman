---
id: US-0019
title: Anyone can list the trades
epic: E01
feature: F01.4
status: ready
---

## Story
As a client who just scanned a store flyer, I want to see which kinds of repair Handyman covers before I sign in, so that I know straight away whether it can help with my broken dishwasher.

## Acceptance criteria
- **AC1** Given the reference data from US-0018, when anyone calls `GET /api/trades` without a token, then the response is `200` with the six trades, each with an id and a name.
- **AC2** Given the six trades, when they are listed, then they come in this fixed order: Appliance repair, Electrical, Plumbing, Heating & AC, Painting, Carpentry (urgent repairs first).
- **AC3** Given a signed-in, unblocked user, when they call `GET /api/trades` with their token, then the response is the same `200` list.
- **AC4** Given the API, when `GET /api/trades` is called twice, then the ids are the same both times and match the ids stored for those trades (so worker trades and jobs can refer to them).
- **AC5** Given an expired, malformed or garbage token in the `Authorization` header, when `GET /api/trades` is called, then the caller is treated as anonymous and the response is the same `200` list (not `401`).

## Business rules
- `GET /trades` is open to anyone; reference data (workflow.md section 4).
- All endpoints except `/r/{code}` and `/health` are served under `/api` (workflow.md section 4).
- MVP focus is appliances, electrical and plumbing (workflow.md section 1, MVP scope), hence their place at the top.
- Public reference-data endpoints (`GET /api/trades`, `GET /api/areas`) treat an expired or invalid token as anonymous, so pickers keep working if a session lapses mid-form (decision 2026-11-10); US-0001 AC1 excludes them from the `401` rule.
- Blocked users get `403 user_blocked` on every API call (workflow.md section 2): a valid token of a blocked user on this endpoint still gets `403` once US-0002 is built (see Test notes).
- `GET /trades` and `GET /areas` are built in E01 (slice 1), not slice 3; `GET /me` profile shape and `PUT /me` stay in E03 (decision 2026-11-10).

## UI notes
- No UI in this story. The trade picker uses it in E03 (worker profile) and E04 (new request).

## Out of scope
- Showing trades in the app (E03, E04).
- Trade icons, descriptions or Serbian names.
- Adding or editing trades.
- The blocked-user `403` itself (US-0002); this story only must not stand in its way.
- `GET /me` and `PUT /me` (E02 US-0002 minimal `GET /me`; profile in E03).

## Test notes
- Seeded users: none needed for AC1, AC2 and AC4; Ana Marković +381600000201 (`./scripts/dev-token.sh +381600000201`) for AC3 once E02 is done; in tests use `TestAuth.TokenFor(userId)`.
- Edge cases: an expired token, a garbage token and a token with a bad signature on this public endpoint (all `200`, AC5); `Authorization` header with no `Bearer` prefix (`200`); a blocked user's valid token (Petar Stojanović +381600000204) returns `403 user_blocked` after US-0002; response is identical in Development and Test; names keep `&` in "Heating & AC".

## Open questions
- none

## Decisions
- 2026-11-10: `GET /trades` and `GET /areas` move from slice 3 into E01 (this story and US-0020); `GET /me` (profile shape, `profile_missing`) and `PUT /me` stay in E03; E02's US-0002 keeps the minimal `GET /me` that returns the caller's id. The coordinator updates the slice 1 and slice 3 rows of workflow.md section 7.
- 2026-11-10: a public endpoint treats an expired or invalid token as anonymous and returns `200` (now AC5); a valid token of a blocked user still gets `403 user_blocked` (US-0002). US-0001 AC1 now reads "except the public reference-data endpoints".

## Dependencies
- Depends on US-0018 (the six trades) and US-0016 (first real query handler is covered by the architecture tests).

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
