---
id: BUG-0021
title: Send code loading state is not exposed to assistive tech (no aria-busy)
story: US-0007
criterion: AC2
severity: low
status: open
found-by: qa-app
---

## Steps to reproduce
1. Slow the OTP request (route delay).
2. Tap Send code and inspect the button.

## Expected
While sending, the button exposes busy state (aria-busy=true) next to the visible spinner.

## Actual
Button is only disabled (aria-disabled); Button sets accessibilityState.busy but react-native-web renders no aria-busy. A screen reader user gets no hint that the request is in progress.

## Evidence
Pinned by the passing test `KNOWN_GAP BUG-0021 loading state is not exposed as aria-busy` in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; when fixed, invert it.

## Suggested fix (optional)
In src/ui/Button.tsx add a web-compatible aria-busy prop (or a live region/label such as "Sending"). Note the field is readonly, not disabled, while sending (acceptable).
