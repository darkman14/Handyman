---
id: BUG-0014
title: GET /api/areas accepts non-canonical integers (+1, 0001, -0) as ids, and a padded id is a 400 while an all-blank value is the top level
story: US-0020
criterion: AC5
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. `GET /api/areas?parentId=%2B1` (that is `+1`), `?parentId=0001`, `?parentId=-0`.
2. `GET /api/areas?parentId=%201` (space then 1) and `?parentId=%20%20` (two spaces).

## Expected
The contract says "send the integer id as text" and "not an integer is a 400". Accepting only the plain digits form (optionally a minus sign) keeps one spelling per id. Whitespace is treated one way: either trimmed everywhere or rejected everywhere.

## Actual
- `+1` and `0001` return the same list as `1`; `-0` returns `[]` (as id 0). `ListAreasQuery.From` parses with `NumberStyles.AllowLeadingSign`.
- `%201` and `1%20` return `400 validation_failed`, but `%20%20` returns the top level (`string.IsNullOrWhiteSpace`).
- Harmless to data (no leak, no 500), but a client or cache keyed on the raw query string sees several spellings of one request.

## Evidence
- Passing test pinning today's behavior: `US_0020_QaExploratoryTests.KNOWN_GAP_BUG_0014_non_canonical_ids_are_accepted_as_the_same_id`. When fixed, flip its assertions.
- Also seen, already tracked as BUG-0005: `HEAD /api/areas` and POST/PUT/PATCH/DELETE return `405` with `Allow: GET` but an empty body (no problem+json).

## Suggested fix (optional)
Use `NumberStyles.None`/`Integer` without leading `+`, require `^-?\d+$` (or `^\d+$`), and decide on trimming: trim, then treat empty as top level, or reject any whitespace other than the fully empty value.
