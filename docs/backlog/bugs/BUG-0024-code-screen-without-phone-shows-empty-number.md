---
id: BUG-0024
title: /code opened without a phone shows an empty number and no way forward except Use a different number
story: US-0007
criterion: AC2
severity: low
status: open
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
Pinned by the passing test `KNOWN_GAP BUG-0024 /code without a phone shows an empty number` in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; when fixed, invert it.

## Suggested fix (optional)
In CodeScreen redirect to / when phone is missing or does not pass normalizeSerbianPhone; consider passing the number via state instead of the URL.
