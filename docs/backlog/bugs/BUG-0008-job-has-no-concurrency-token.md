---
id: BUG-0008
title: Job has no concurrency token, so two real saves never collide and AC2 cannot happen with real data
story: US-0017
criterion: AC2
severity: medium
status: verified
found-by: qa-api
---

## Steps to reproduce
1. Inspect the EF model: `AppDbContext.Model.FindEntityType(typeof(Job))` has no property with `IsConcurrencyToken` (no xmin or row version).

## Expected
The loser of two concurrent saves of the same job gets `DbUpdateConcurrencyException`, mapped to `409 job_not_open`.

## Actual
Nothing in the model raises `DbUpdateConcurrencyException`. The mapping is correct (proven with a thrown exception), but two concurrent hires that both loaded an Open job would both save: last writer wins, the hire rule is only checked in memory. The rule "concurrent hires: the second gets 409 job_not_open" is not enforced by the code as it stands.

## Evidence
- Failing test: none. Documented as a passing KNOWN_GAP test: `US_0017_QaFailuresAsProblemDetailsTests.KNOWN_GAP_BUG_0008_job_has_no_concurrency_token` (flip its assertion when the bug is fixed).

## Suggested fix (optional)
Map Postgres `xmin` as a concurrency token on Job (and Bid), then add a two-context test that saves the same job twice. Can be done in E04 (hire), but AC2 should not count as fully verified until then.
