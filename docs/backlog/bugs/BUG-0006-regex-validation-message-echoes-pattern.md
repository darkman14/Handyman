---
id: BUG-0006
title: [RegularExpression] default message echoes the regex pattern to the client
story: US-0017
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. POST to an endpoint whose record has `[RegularExpression("^[a-z]+$")] string? Slug` with `slug = "BAD"`.

## Expected
A field-level message that names the field and the problem, without internals.

## Actual
`errors.slug[0]` is `The field Slug must match the regular expression '^[a-z]+$'.` Default data annotation messages expose server-side patterns. Harmless today, but real endpoints will use patterns (phone, referral code).

## Evidence
- Failing test: none. Documented as a passing KNOWN_GAP test: `US_0017_QaFailuresAsProblemDetailsTests.KNOWN_GAP_BUG_0006_regex_validation_message_echoes_the_pattern` (flip its assertion when the bug is fixed).

## Suggested fix (optional)
Require an explicit `ErrorMessage` on annotations in request records (architecture test), or have ValidationFilter replace messages that contain a quote with a generic one.
