---
id: US-0001
title: API rejects calls without a valid sign-in token
epic: E02
feature: F02.1
status: done
---

## Story
As a client or worker, I want the API to act only on requests that carry my valid Supabase session, so that nobody else can post, bid or read data in my name.

## Acceptance criteria
- **AC1** Given a request to any endpoint under `/api` except the public reference-data endpoints (`GET /api/trades`, `GET /api/areas`) with no `Authorization` header, when it is sent, then the response is `401`.
- **AC2** Given a token that is otherwise valid but expired, when it is sent to an authenticated `/api` endpoint, then the response is `401`.
- **AC3** Given a token whose issuer is not this project's Supabase Auth (`<Supabase URL>/auth/v1`), when it is sent to an authenticated endpoint, then the response is `401`.
- **AC4** Given a token whose audience is not `authenticated`, whose signature does not verify, or whose `sub` is missing or not a GUID, when it is sent to an authenticated endpoint, then the response is `401`.
- **AC5** Given a valid, unexpired token for a seeded user, when it is sent to an authenticated endpoint (today: `GET /api/me`), then the response is not `401`; and given no token at all, `GET /health` still returns `200`.

## Business rules
- The app sends the Supabase access token; the API validates it and maps `sub` to `app.users.id` (workflow.md section 1, Auth).
- The API has no sign-in endpoints (workflow.md section 4).
- A missing or invalid token, or one with no valid `sub`, returns `401` (workflow.md section 4).
- Public reference-data endpoints (`GET /api/trades`, `GET /api/areas`) are open to anyone; an expired or garbage token on them is treated as anonymous, not `401` (E01 US-0019 decision 2026-11-10).
- Tests never call a real Supabase project; tokens are signed with a test key (workflow.md section 1, Tests).

## UI notes
- No UI. The app's reaction to `401` is US-0010.

## Out of scope
- Mapping the caller to `app.users` and blocking (US-0002).
- Profile shape of `GET /me` (E03).
- A problem-details body on `401`: status code is enough for this story.
- The public reference-data endpoints themselves (E01 US-0019, US-0020).

## Test notes
- Seeded users: any, e.g. Ana Marković +381600000201 (client); get a real local token with `./scripts/dev-token.sh +381600000201` for the manual check.
- Edge cases: token with a missing, empty or non-GUID `sub` (all `401`, AC4); `Bearer` prefix missing or misspelled; token that expires a few seconds in the future (should pass) vs. a few seconds in the past (clock skew tolerance is a dev choice, but must be documented in Dev notes); a Supabase `anon` key sent as the bearer token (must be `401`); `GET /health` with a garbage token; once US-0019/US-0020 exist, `GET /api/trades` and `GET /api/areas` with no token or a garbage token return `200`, not `401`.

## Open questions
- none

## Decisions
- 2026-11-10: a token with a valid signature but a missing or malformed `sub` is treated as invalid and gets `401` (now in AC4 and workflow.md section 4).
- 2026-11-10: the public reference-data endpoints are excluded from the `401` rule; an expired or garbage token on a public endpoint is treated as anonymous (E01 US-0019 decision; now in AC1 to AC4).

## API contract
No new endpoints and no request/response changes. The OpenAPI file does not list `401` for any endpoint (the project documents only success shapes; `401`/`403` come from the auth middleware), so `docs/api/Handyman.Api.json` is unchanged.

**Rule for every endpoint under `/api`** (the group is `RequireAuthorization()`): header `Authorization: Bearer <Supabase access token>`.
- `401`, empty body (no problem-details, see Out of scope), when: header missing; scheme not `Bearer`; token malformed; signature invalid; issuer is not `<Supabase URL>/auth/v1`; audience is not `authenticated`; token expired; `sub` missing, empty or not a GUID.
- Anything else valid reaches the handler; business errors use the normal codes (400/403/404/409).

**Public (no `401`, bad or expired token treated as anonymous):** `GET /api/trades`, `GET /api/areas` (`AllowAnonymous`).

**`GET /health`**: outside `/api`, no auth, `200 {"status":"ok"}` with any or no `Authorization` header.

**`GET /api/me`** (authenticated): `200` with `{ "id": "<guid>" }` where `id` is the token `sub` (= `app.users.id`). Profile shape comes in E03. `401` as above.

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | pass (`Ac1_api_returns_401_without_authorization_header`, `Every_non_public_endpoint_in_the_openapi_file_returns_401_without_or_with_bad_token`, query-string/cookie/apikey token tests) | n/a |
| AC2 | pass (`Ac2_expired_token_returns_401`, `Token_expired_longer_ago_than_the_clock_skew_returns_401`, nbf/exp variants in `US_0001_QaExploratoryTests`) | n/a |
| AC3 | pass (`Ac3_token_from_another_issuer_returns_401`, `Issuer_variants_return_401`) | n/a |
| AC4 | pass with known issue BUG-0016, low (nil GUID `sub` accepted; pinned by `KNOWN_GAP_BUG_0016_all_zero_guid_sub_is_accepted_but_should_be_401`). Other cases pass: `Ac4_*`, alg none / confusion, role, sub, audience variants | n/a |
| AC5 | pass (`Ac5_valid_token_is_not_401_and_returns_the_sub`, `Ac5_health_returns_200_without_token`, `Public_paths_return_200_with_every_kind_of_bad_token`) | n/a |

## Dev notes
- Validation lives in `backend/src/Handyman.Api/Auth/SupabaseJwt.cs`.
- **Clock skew: 30 seconds** (`SupabaseJwt.ClockSkew`, default was 5 minutes). A token that expired up to 30 s ago still passes; older gives `401`.
- **Algorithms:** HS256 only when `Supabase:JwtSecret` is set; ES256 and RS256 only when keys come from the JWKS. Anything else (HS512, `none`, HS* against a JWKS key) gives `401`.
- **Identity check** (`OnTokenValidated`): after the signature is valid, the token is refused with `401` unless `sub` is a GUID and `role` is `authenticated`. This rejects the Supabase `anon` key (role `anon`, no `sub`).
- `ApiExceptionHandler` maps any stray `UnauthorizedAccessException` (e.g. from `CurrentUser.Id()`) to `401` problem details, never `500`.
- Tests (`US_0001_ApiRejectsInvalidTokensTests`) run the production validation: `ApiFactory` sets `Supabase:JwtSecret` to the test key instead of replacing the validation parameters.
- Known, not fixed here: JWKS download is synchronous with no timeout (BUG-0013).
