---
id: US-0017
title: Failures come back as problem details without internals
epic: E01
feature: F01.3
status: done
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
| AC1 | pass (`Domain_error_returns_409_with_its_stable_code`, `Failure_inside_a_command_rolls_back_sends_no_push_and_maps_correctly` domain) | n/a |
| AC2 | pass; BUG-0008 verified fixed (xmin token). Gap BUG-0010 medium: parallel ratings do not write the job row, so no conflict (`Concurrency_conflict_returns_409_job_not_open_without_internals`, `Loser_of_a_real_race_gets_409_job_not_open_over_http` hire/finish/cancel, `Second_save_of_*_throws_DbUpdateConcurrencyException`, `Hire_loses_against_a_concurrent_bid_withdrawal_...`, `Saving_unchanged_entities_...never_conflicts`, `Save_after_reload_and_two_different_jobs_...`, `KNOWN_GAP_BUG_0010_...`) | n/a |
| AC3 | pass (`Not_found_returns_404_with_thing_not_found_code`, `Failure_inside_a_command_...` notfound) | n/a |
| AC4 | pass; gaps BUG-0005, BUG-0006, BUG-0007, all low (`Missing_and_invalid_fields_return_400_...`, `Body_variants_give_the_expected_status_and_never_500`, `Validation_errors_are_camel_cased_...`, `Bad_route_and_query_values_are_400_validation_failed`) | n/a |
| AC5 | pass (`Unexpected_failure_returns_500_without_internals_in_any_environment`, `Server_error_leaks_nothing_in_development_and_production_like_environments`, `Unexpected_failure_is_logged_at_error_level_by_the_handler`) | n/a |

## Dev notes

- Error codes chosen by backend-dev (2026-11-24), also in the header of `Infrastructure/ApiExceptionHandler.cs`:
  - 409 domain: `job_not_open` (hire on a non-Open job; any concurrency conflict), `bid_job_mismatch`, `bid_not_active`, `job_not_assigned`, `job_not_done`, `bid_not_accepted`, `not_allowed`, `invalid_stars`, `rating_job_mismatch`, `already_rated`, `job_not_cancellable`, and the fallback `business_rule_violated`.
  - 404: `<thing>_not_found` from `NotFoundException(thing)`.
  - 400: `validation_failed` with `errors` per field (also malformed JSON).
  - 500: `server_error`, fixed text "Something went wrong. Try again.", logged on the server.
  - Other rejected requests (for example 415) keep their status with `request_rejected`.
- Validation is a reflection-based endpoint filter on the `/api` group (the built-in `AddValidation()` didn't cover test-assembly types and its 400 has no `code`). It does not walk nested objects: keep request records flat.
- The problem-details `type` is a placeholder (`https://httpstatuses.io/<status>`).

- QA (qa-api, 2026-11-24): 265 backend tests green. New file `Acceptance/US_0017_QaFailuresAsProblemDetailsTests.cs`: real decorator chain (rollback, no push, correct mapping for domain, concurrency, not-found, 500), Development and Production environments, inner exceptions, body variants, client abort, auth unchanged. Known gaps are kept as passing `KNOWN_GAP_BUG_000x` tests that must be flipped when fixed.
- Real pipeline (Development, local Supabase): /health 200; no or bad token 401 (empty body, WWW-Authenticate); unknown route 404 and wrong method 405 have empty bodies (BUG-0005); CORS preflight 204 for an allowed origin. Seeded sign-in is not available yet (dev-token.sh fails until US-0003), so authenticated calls were checked through ApiFactory only.
- Double logging: not measured at runtime. By design the framework's ExceptionHandlerMiddleware logs the exception and ApiExceptionHandler logs it again, so two Error entries per 500; acceptable. The handler's own entry is exactly one, and a client abort produces no Error entry from it.
- A DomainException wrapped inside another exception type is a 500 by design (exact type mapping): handlers must not wrap it.
- OpenAPI file content unchanged by the build (only line endings).

- QA re-verification of BUG-0008 (qa-api, 2026-11-24, commit 7d6957a): confirmed. Real two-context races on Postgres give `DbUpdateConcurrencyException` for Hire, Finish, Cancel, a bid change racing a hire (job row untouched, bid xmin catches it), and HTTP 409 `job_not_open` problem+json without xmin/SQL; the winner's state is stored. No false conflicts: unchanged save, repeated saves in one context (token refreshed), hire with job + several bids in one SaveChanges, reload then save, two different jobs in parallel. New file `Acceptance/US_0017_QaConcurrencyTokenTests.cs`.
- Migration: `dotnet ef migrations script` for the new migration is only the history insert. A throwaway Postgres 17 taken to `UniqueConstraints`, then to latest: columns, constraints, indexes, RLS flags and policies of the whole `app` schema are byte-identical before and after; no `xmin`/`version` user column on `jobs`/`bids`; `has-pending-model-changes` is clean (Npgsql does not try to add the system column). The local Supabase DB already had the migration; the API in Development starts, reports "No migrations were applied", `/health` 200.
- Gotcha for future handlers: `FromSql("select * from app.jobs ...")` on `Jobs`/`Bids` throws because `xmin` is not part of `*`; write `select *, xmin from ...` (test `Raw_sql_select_star_on_jobs_works_only_when_xmin_is_selected_too`). Likelihood is low: the rules already say queries use `Select` projections and commands load entities with LINQ; no `FromSql` exists in the code today. A note here is enough; an architecture test is not warranted.
- Rule for command handlers: the xmin token only protects rows that are UPDATED. A command that only inserts child rows (ratings, bids) and decides from the job's state does not conflict with a parallel one. See BUG-0010 (both parties rating at once leave the job Done). ReadDbContext reads of jobs/bids (entities, projections, joins) work.
