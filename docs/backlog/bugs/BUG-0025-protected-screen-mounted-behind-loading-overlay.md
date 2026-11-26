---
id: BUG-0025
title: Protected home screen is rendered in the DOM behind the loading overlay while signed out or restoring
story: US-0009
criterion: AC2
severity: low
status: open
found-by: qa-app
---

## Steps to reproduce
1. Signed out in a browser, install a MutationObserver that watches `document.body.innerText` for "Welcome".
2. Open `http://localhost:8081/home` directly.
3. Wait for the sign-in screen.

## Expected
A signed-out user never gets protected screen content rendered (the guard should not mount the protected route until the session is known).

## Actual
`AuthGate` keeps `<Stack />` mounted and covers it with `LoadingScreen overlay`, so the home screen ("Welcome", "Sign out" button) is present in the DOM and accessibility tree while the session is restoring or the redirect is pending. Sighted users see only the overlay. Assistive tech and keyboard Tab can still reach the hidden "Sign out" button (the overlay is a sibling, not a modal or `inert`). Today the placeholder home shows no personal data, but real protected screens will fire their API calls before the redirect.

## Evidence
- Passing KNOWN_GAP test: `KNOWN_GAP BUG-0025 protected screen is mounted behind the overlay while signed out` in `frontend/e2e/us-0009-stay-signed-in-and-sign-out.spec.ts`

## Suggested fix (optional)
Render `children` only when the route is allowed (signed in, or public path); otherwise render only the loading screen. If the navigator must stay mounted for router.replace to work, mark the covered content `inert`/`aria-hidden` and `importantForAccessibility="no-hide-descendants"` while blocked.
