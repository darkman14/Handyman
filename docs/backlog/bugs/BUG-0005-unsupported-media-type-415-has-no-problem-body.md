---
id: BUG-0005
title: 415 (wrong content type) comes back with an empty body: no problem+json, no code
story: US-0017
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. POST a body endpoint (test-only /test-errors/validated) with `Content-Type: text/plain` and a body.

## Expected
415 with an `application/problem+json` body, `code` `request_rejected` (as the Dev notes and ApiExceptionHandler describe).

## Actual
415 with an empty body and no content type. ASP.NET answers 415 directly without throwing, so ApiExceptionHandler never sees it. The same holds for 404 (unknown route) and 405 (wrong method), seen on the running API; 401 is out of scope (US-0001).

## Evidence
- Failing test: none. Documented as a passing KNOWN_GAP test: `US_0017_QaFailuresAsProblemDetailsTests.KNOWN_GAP_BUG_0005_wrong_content_type_is_415_with_an_empty_body` (flip its assertion when the bug is fixed).

## Suggested fix (optional)
Add `app.UseStatusCodePages()` (with AddProblemDetails it writes problem+json for empty 4xx responses), or map 415 in a small middleware to `request_rejected`; and correct the Dev notes.
