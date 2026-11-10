---
id: US-0001
title: API rejects calls without a valid sign-in token
epic: E02
feature: F02.1
status: ready
---

## Story
As a client or worker, I want the API to act only on requests that carry my valid Supabase session, so that nobody else can post, bid or read data in my name.

## Acceptance criteria
- **AC1** Given a request to any endpoint under `/api` with no `Authorization` header, when it is sent, then the response is `401`.
- **AC2** Given a token that is otherwise valid but expired, when it is sent to an `/api` endpoint, then the response is `401`.
- **AC3** Given a token whose issuer is not this project's Supabase Auth (`<Supabase URL>/auth/v1`), when it is sent, then the response is `401`.
- **AC4** Given a token whose audience is not `authenticated`, whose signature does not verify, or whose `sub` is missing or not a GUID, when it is sent, then the response is `401`.
- **AC5** Given a valid, unexpired token for a seeded user, when it is sent to an authenticated endpoint (today: `GET /api/me`), then the response is not `401`; and given no token at all, `GET /health` still returns `200`.

## Business rules
- The app sends the Supabase access token; the API validates it and maps `sub` to `app.users.id` (workflow.md section 1, Auth).
- The API has no sign-in endpoints (workflow.md section 4).
- A missing or invalid token, or one with no valid `sub`, returns `401` (workflow.md section 4).
- Tests never call a real Supabase project; tokens are signed with a test key (workflow.md section 1, Tests).

## UI notes
- No UI. The app's reaction to `401` is US-0010.

## Out of scope
- Mapping the caller to `app.users` and blocking (US-0002).
- Profile shape of `GET /me` (E03).
- A problem-details body on `401`: status code is enough for this story.

## Test notes
- Seeded users: any, e.g. Ana Marković +381600000201 (client); get a real local token with `./scripts/dev-token.sh +381600000201` for the manual check.
- Edge cases: token with a missing, empty or non-GUID `sub` (all `401`, AC4); `Bearer` prefix missing or misspelled; token that expires a few seconds in the future (should pass) vs. a few seconds in the past (clock skew tolerance is a dev choice, but must be documented in Dev notes); a Supabase `anon` key sent as the bearer token (must be `401`); `GET /health` with a garbage token.

## Open questions
- none

## Decisions
- 2026-11-10: a token with a valid signature but a missing or malformed `sub` is treated as invalid and gets `401` (now in AC4 and workflow.md section 4).

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
