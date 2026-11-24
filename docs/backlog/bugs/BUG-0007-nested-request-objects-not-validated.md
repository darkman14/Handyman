---
id: BUG-0007
title: Nested objects in a request body are not validated; invalid inner values reach the handler
story: US-0017
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. POST `{ "name": "ok", "inner": { "stars": 99 } }` to an endpoint whose record has a nested record with `[Range(1,5)] Stars`.

## Expected
400 `validation_failed` naming `inner.stars`; handler not called.

## Actual
200, handler called with invalid inner data. Known and documented in the Dev notes (keep request records flat), but AC4 says a request whose body fails validation, and handlers are told to trust their input. Collections of objects are not walked either.

## Evidence
- Failing test: none. Documented as a passing KNOWN_GAP test: `US_0017_QaFailuresAsProblemDetailsTests.KNOWN_GAP_BUG_0007_nested_objects_are_not_validated` (flip its assertion when the bug is fixed).

## Suggested fix (optional)
Walk properties recursively in ValidationFilter (keys like `inner.stars`, cycle guard), or add an architecture test that rejects non-flat request records until then.
