---
id: US-0017
title: Failures come back as problem details without internals
epic: E01
feature: F01.3
status: ready
---

## Story
As a client or worker, I want the app to tell me clearly why something didn't work, without ever showing me server internals, so that I know whether to try again, pick something else, or give up.

## Acceptance criteria
- **AC1** Given a use case refuses an action because a business rule is broken (a domain error, e.g. hiring on a job that is no longer Open), when it is called over HTTP, then the response is `409` with an RFC 7807 problem-details body whose `code` is the rule's stable code.
- **AC2** Given two saves of the same job collide (the job changed since it was loaded), when the losing request completes, then the response is `409` with `code` `job_not_open`.
- **AC3** Given a use case can't find what it was asked for, when it is called, then the response is `404` with a problem-details body and a stable `code` of the form `<thing>_not_found` naming what was missing (e.g. `job_not_found`).
- **AC4** Given a request whose body or parameters fail validation (missing or malformed field), when it is sent, then the response is `400` with a problem-details body whose `code` is `validation_failed` and that names each invalid field, and the handler is not called.
- **AC5** Given any other unexpected failure, when it happens, then the response is `500` with a problem-details body that contains no exception message, stack trace, SQL or connection details, and the error is logged on the server.

## Business rules
- Errors use RFC 7807 problem details with a stable `code` (`profile_missing`, `rating_pending`, `job_not_open`, `already_bid`, `not_allowed`, `user_blocked`) (workflow.md section 4).
- One exception handler maps `DomainException` and `DbUpdateConcurrencyException` to `409`, `NotFoundException` to `404`, anything else to `500` without leaking details (workflow.md section 5, CQRS).
- Request validation stays at the endpoint (data annotations), so handlers receive valid input (workflow.md section 5, CQRS).
- Concurrent hires: the second gets `409` with `job_not_open` (workflow.md section 2, Business rules).
- Not-found codes follow `<thing>_not_found` (`job_not_found`, `bid_not_found`, `area_not_found`); validation failures use `validation_failed` with per-field errors (decision 2026-11-10).

## UI notes
- No UI. The app's generic error copy "Something went wrong. Try again." (US-0010) is what users see for `500`.

## Out of scope
- `401` and `403 user_blocked` (E02: US-0001, US-0002).
- Real endpoints that raise these errors (E04 onwards); this story proves the mapping with test-only endpoints.
- Translating error messages (Serbian localization later).

## Test notes
- Seeded users: none; use test-only endpoints in the test project that throw each kind of error.
- Edge cases: a `500` in Development must also not leak details in the response body (logs may have them); the `code` field is present on every problem-details body produced here; content type is `application/problem+json`; a malformed JSON body (not just a missing field) gives `400`, not `500`.

## Open questions
- none

## Decisions
- 2026-11-10: not-found codes are `<thing>_not_found` (`job_not_found`, `bid_not_found`, `area_not_found`), and validation failures use `validation_failed` with per-field errors (now in AC3 and AC4). The coordinator adds both patterns to workflow.md section 4 as a documentation clarification.

## Dependencies
- None; the mapping is proven with test-only endpoints.

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
