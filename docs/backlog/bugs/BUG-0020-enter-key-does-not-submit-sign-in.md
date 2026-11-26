---
id: BUG-0020
title: Pressing Enter in the phone field does not submit the form
story: US-0007
criterion: AC2
severity: low
status: fixed
found-by: qa-app
---

## Steps to reproduce
1. Open http://localhost:8081/.
2. Type 060 000 0201 in Phone number.
3. Press Enter.

## Expected
The form submits like Send code (standard keyboard behavior on web; also the phone keyboard "go" key).

## Actual
Nothing happens, no request is sent. Only Tab then Enter on the button works.

## Evidence
Was pinned by a KNOWN_GAP test in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; now inverted.

## Suggested fix (optional)
Pass onSubmitEditing={onSubmit} (and returnKeyType="go") on the TextField in SignInScreen; TextField may need to forward it.

## Fix
`SignInScreen` passes `onSubmitEditing` and `returnKeyType="go"` to the phone field. Verified in the browser on 2026-11-26: e2e test `AC2 pressing Enter in the phone field submits the form (BUG-0020 fixed)` passes (one OTP request, code screen shown). The code screen behaves the same (US-0008 test `pressing Enter in the code field submits the code`). The phone "go" key on iOS and Android is not tested.
