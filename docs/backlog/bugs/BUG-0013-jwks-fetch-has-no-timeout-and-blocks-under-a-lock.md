---
id: BUG-0013
title: JWKS download has no short timeout and runs under a lock, so a hanging Supabase stalls every request that carries a bearer token (public endpoints included)
story: US-0019
criterion: AC5
severity: low
status: open
found-by: qa-api
---

## Steps to reproduce
1. Production config (no `Supabase:JwtSecret`, keys from JWKS). Make `<Supabase:Url>/auth/v1/.well-known/jwks.json` accept the connection but never answer (blackholed host, not a refused port).
2. Call `GET /api/trades` with any Bearer token that has a `kid` header (also an expired or garbage-looking JWT with a kid), from several clients at once.

## Expected
The public endpoint answers `200` quickly (AC5: bad or unverifiable token means anonymous), the protected endpoints answer `401`.

## Actual
Not run end to end (a blackhole needs a network fault; code reading only). A refused/dead port was verified: `GET /api/trades` is `200` and `/api/me` is `401`, no `500` (test `With_the_JWKS_unreachable_trades_still_answers_200_and_protected_endpoints_answer_401_not_500`).
For a hanging host: `JwksCache.GetKeys` does a synchronous `GetStringAsync(...).GetResult()` with a new `HttpClient` (default timeout 100 s) inside `lock (_lock)`. While `_set` is null (never fetched) or stale, every token validation retries the download under the lock, so requests queue behind a 100 s wait each and request threads are blocked. The public trade picker would hang for users holding a (lapsed) session token.

## Evidence
- Failing test: none (the refused-port case passes).

## Suggested fix (optional)
In `JwksCache`: shared `HttpClient` with a short timeout (2-3 s), keep serving the last good key set when a refresh fails, and negative-cache a failed fetch for ~30 s so requests do not retry each time.
