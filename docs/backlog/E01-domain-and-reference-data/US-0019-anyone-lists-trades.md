---
id: US-0019
title: Anyone can list the trades
epic: E01
feature: F01.4
status: done
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
`GET /api/trades` (operationId `ListTrades`), feature `Features/Trades/ListTrades.cs`.

- **Auth:** anonymous (`AllowAnonymous`). No token, an expired, malformed or badly signed token, or an `Authorization` header without `Bearer` is treated as anonymous (AC5). A valid token of a blocked user gets `403 user_blocked` once US-0002 is built.
- **Request:** no parameters, no body.
- **Response `200`:** JSON array, always the six trades in this fixed order: Appliance repair, Electrical, Plumbing, Heating & AC, Painting, Carpentry.
  ```json
  [ { "id": 1, "name": "Appliance repair" }, ... ]
  ```
  `id`: integer (the stored trade id, stable across calls); `name`: string, English, `&` kept as is.
- **Status codes:** `200` (all cases above), `403 user_blocked` (blocked user's valid token, from US-0002). No `401`, no `400`, no `404`; the list is never empty.
- **Error body:** the standard problem-details shape with a machine `code` (only `user_blocked` applies).

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass (Anonymous_gets_the_six_trades_in_the_fixed_order; live curl) | n/a |
| AC2 | pass (Anonymous_gets_the_six_trades_in_the_fixed_order; Names_keep_the_ampersand_in_the_raw_json; live curl) | n/a |
| AC3 | pass (A_signed_in_user_gets_the_same_list) | n/a |
| AC4 | pass (Ids_match_the_stored_ids_and_are_stable_across_calls; live curl ids 1045-1050) | n/a |
| AC5 | pass (expired, garbage, bad signature, no Bearer, unreachable JWKS tests; live curl) | n/a |

## Dev notes
- Contract mode: handler is a stub that throws `NotImplementedException`; the real `ReadDbContext` query (ordered, `Select` into `TradeResponse`) comes in build mode. Order must be explicit (the table has no order column; decide in build: order by id if seeded in that order, else add a sort column).
- Build mode must verify AC5: the JWT handler must not challenge on an invalid token for an `AllowAnonymous` endpoint (the `/api` group requires authorization by default).

QA notes (qa-api):
- Live HTTP against the API in Development with local Supabase (JWKS): no token, expired, bad signature, garbage, no Bearer, empty Bearer, 8 KB token, duplicate Authorization headers, trailing slash, query noise, any Accept: all `200`, body identical (196 bytes, `Heating & AC` raw, only `id` and `name`, ids 1045-1050 on the polluted local DB, fixed order), `application/json; charset=utf-8`. Header of 40 KB or more: Kestrel `431` before the app (platform limit). POST/PUT/DELETE/PATCH/HEAD: `405` with `Allow: GET` (HEAD not allowed; harmless). CORS preflight `204`. About 215 ms on localhost, no gzip (196-byte body).
- No `Cache-Control`/`ETag`. Recommended (optional, not in the story, no bug): `Cache-Control: public, max-age=300`, since reference data changes only by migration.
- AC3 with a real Supabase token was not possible: local Supabase answers `phone_provider_disabled` on the OTP call, so `dev-token.sh` fails. AC3 verified with `TestAuth.TokenFor`.
- A blocked user's valid token returns `200` today; `403 user_blocked` is US-0002 (not built), not a bug. Test `A_blocked_users_valid_token_gets_200_today_because_US_0002_is_not_built` must flip when US-0002 lands.
- Unreachable JWKS (dead port): `GET /api/trades` `200`, `/api/me` `401`, no `500`. A hanging JWKS host is a code-reading risk: BUG-0013 (low).
- A seventh custom trade is not listed (`A_seventh_custom_trade_is_not_listed`); matches the contract ("always the six"). Later stories cannot offer a custom trade in the picker; fine while adding trades is out of scope.
- Architecture tests (US-0016) pass with the real handler; full suite 342 passed. OpenAPI regenerates unchanged; `frontend/src/api/schema.d.ts` contains `/api/trades`.
- Tests: `Acceptance/US_0019_ListTradesTests.cs` (dev), `Acceptance/US_0019_QaExploratoryTests.cs` (QA).
