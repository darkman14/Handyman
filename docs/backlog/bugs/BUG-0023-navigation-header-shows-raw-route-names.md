---
id: BUG-0023
title: Stack header shows raw route names "index" and "code"
story: US-0007
criterion: AC1
severity: low
status: open
found-by: qa-app
---

## Steps to reproduce
1. Open http://localhost:8081/ and read the top of the page.
2. Send a code; read the top of the code screen.

## Expected
No header, or a proper title (Sign in / Enter your code).

## Actual
A header with the lowercase route names "index" and "code" is visible above the card on both screens (also read by screen readers as extra text, and on code screen it duplicates the real title).

## Evidence
Pinned by the passing test `KNOWN_GAP BUG-0023 navigation headers show raw route names` in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; when fixed, invert it.

## Suggested fix (optional)
In app/_layout.tsx set headerShown:false for these screens (the card already has a title) or set title options from i18n.
