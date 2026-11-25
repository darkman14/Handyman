---
id: BUG-0016
title: A validly signed token with sub 00000000-0000-0000-0000-000000000000 is accepted as a signed-in user
story: US-0001
criterion: AC4
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. Sign a token with the project secret: `sub = 00000000-0000-0000-0000-000000000000`, `role = authenticated`, valid `iss`, `aud`, `exp`.
2. `GET /api/me` with `Authorization: Bearer <token>`.

## Expected
`401`. The nil GUID is not a real `app.users.id`; AC4 says `sub` must be a GUID that identifies a user, and the intent is "no usable identity".

## Actual
`200 {"id":"00000000-0000-0000-0000-000000000000"}`.

## Evidence
Test `US_0001_QaExploratoryTests.KNOWN_GAP_BUG_0016_all_zero_guid_sub_is_accepted_but_should_be_401` pins the current behavior (passes today). When fixed, flip its assertion to `Unauthorized`.

Impact is low: forging the token needs the signing key, and Supabase never issues the nil GUID. It is defense in depth, and it matters if any later code treats `Guid.Empty` as "no owner".

## Suggested fix (optional)
In `SupabaseJwt.RejectUnusableIdentity` require `Guid.TryParse(sub, out var id) && id != Guid.Empty`; apply the same check in `CurrentUser.Id()`.
