---
id: BUG-0024
title: /code opened without a phone shows an empty number and no way forward except Use a different number
story: US-0007
criterion: AC2
severity: low
status: fixed
found-by: qa-app
---

## Steps to reproduce
1. Open http://localhost:8081/code directly (bookmark, or typed).
2. Read the screen.

## Expected
Redirect to sign-in (no number means no code was requested). Also the phone param is shown unvalidated (any text, e.g. <b>x</b>, is rendered as plain text, escaped, so no XSS).

## Actual
Screen says We sent a code to with an empty number. Misleading; US-0008 will build on this route. Observation: the phone number is in the URL (/code?phone=%2B381...), it stays in browser history and reload keeps it.

## Evidence
Was pinned by a KNOWN_GAP test in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; now inverted.

## Suggested fix (optional)
In CodeScreen redirect to / when phone is missing or does not pass normalizeSerbianPhone; consider passing the number via state instead of the URL.

## Fix
`CodeScreen` redirects to `/` when the `phone` param is missing or fails `normalizeSerbianPhone`. Verified in the browser on 2026-11-26: `AC2 /code without a phone sends the user back to sign-in (BUG-0024 fixed)` (US-0007 spec) and `/code with an invalid phone ... redirects to sign-in` for `abc`, a UK number and an HTML-looking value (US-0008 spec) pass. The number still travels in the URL query on `/code` (observation, not part of this bug).
