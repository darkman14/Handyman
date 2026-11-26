---
id: BUG-0022
title: Phone validation message is not announced to screen readers and not tied to the field
story: US-0007
criterion: AC3
severity: medium
status: fixed
found-by: qa-app
---

## Steps to reproduce
1. Enter abc and press Send code.
2. Inspect the message and the input.

## Expected
Message has role alert (like the Supabase error), input has aria-invalid and aria-describedby pointing at it.

## Actual
Plain text under the field: no alert role, no aria-invalid, no aria-describedby. Screen reader users are not told why nothing happened. The Supabase error uses role alert, so the two errors are inconsistent.

## Evidence
Pinned by the passing test `KNOWN_GAP BUG-0022 validation message is not announced` in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; when fixed, invert it.

## Re-verification attempt (2026-11-26)
Fix is in src/ui/TextField.tsx (commit e9b242e), but the running Metro dev server at localhost:8081 still serves the old TextField bundle (no role alert, no aria-invalid, no nativeID; checked in the served entry.bundle). Not verifiable until Metro is restarted with a cleared cache (`npx expo start --web -c`). The e2e test `AC3 validation message is announced as an alert and tied to the invalid field (BUG-0022)` already asserts the fixed behavior and currently fails against the stale bundle. Status stays open until it passes.

## Suggested fix (optional)
In src/ui/TextField.tsx render the error with role alert and set accessibilityInvalid / aria-describedby on the input.

## Fix
`TextField` now gives the error text `role="alert"` and an id, and the input `aria-invalid="true"` and `aria-describedby` pointing to that id (commit e9b242e). Verified in the browser on 2026-11-26 after restarting Metro with a cleared cache (the first re-check hit a stale bundle): e2e test `AC3 validation message is announced as an alert and tied to the invalid field (BUG-0022)` passes, 31 of 31 in the spec. Screen-reader output on iOS and Android is not tested.
