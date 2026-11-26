---
id: BUG-0020
title: Pressing Enter in the phone field does not submit the form
story: US-0007
criterion: AC2
severity: low
status: open
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
Pinned by the passing test `KNOWN_GAP BUG-0020 pressing Enter in the field does not submit` in `frontend/e2e/us-0007-request-sign-in-code.spec.ts`; when fixed, invert it.

## Suggested fix (optional)
Pass onSubmitEditing={onSubmit} (and returnKeyType="go") on the TextField in SignInScreen; TextField may need to forward it.
