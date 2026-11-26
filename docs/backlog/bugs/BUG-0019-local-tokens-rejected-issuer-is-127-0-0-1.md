---
id: BUG-0019
title: A real local Supabase token is rejected with 401 because its issuer is 127.0.0.1 while the API expects the configured Supabase URL
story: US-0003
criterion: AC4
severity: medium
status: fixed
found-by: tech lead
---

## Steps to reproduce
1. Local Supabase running with the phone provider enabled; API in Development with `appsettings.Local.json` written by `scripts/dev-env.*` (`Supabase:Url` = `http://localhost:54321`, or a Docker/LAN IP).
2. `./scripts/dev-token.sh +381600000201`, then `GET /api/me` with that token.

## Expected
`200` with the caller's `app.users` id (US-0003 AC4).

## Actual
`401`. The API log says `IDX10205: Issuer validation failed. Issuer: 'http://127.0.0.1:54321/auth/v1'. Did not match: ValidIssuer 'http://localhost:54321/auth/v1'`. Local Supabase Auth always signs tokens with issuer `http://127.0.0.1:54321/auth/v1`, whatever host the client used to reach it. `SupabaseJwt.cs` builds the expected issuer from `Supabase:Url`, which is the reachable address (localhost, a Docker host IP or a LAN IP).

## Evidence
Token header `alg=ES256` with a `kid` (so the JWKS path is used), claims `aud=authenticated`, `role=authenticated`. Only the issuer differs. Found while closing US-0003 AC4 after the local phone provider was enabled. Affects every local sign-in, so it also blocks qa-app and the app stories US-0007 to US-0010.

## Suggested fix
Add an optional `Supabase:Issuer` setting. When set, it is the expected issuer; when empty, keep `{Url}/auth/v1`. `scripts/dev-env.ps1` and `dev-env.sh` write `Issuer = http://127.0.0.1:54321/auth/v1` for the local stack. The hosted test and production projects keep the default. Cover with tests (issuer override accepted, default unchanged, wrong issuer still 401).

## Fix
- API: optional `Supabase:Issuer` (trimmed; empty means `{Url}/auth/v1` as before), in `SupabaseJwt.cs`. Tests: `BUG_0019_ConfigurableTokenIssuerTests`.
- Tooling: `scripts/dev-env.ps1` and `dev-env.sh` write `"Issuer": "http://127.0.0.1:<port>/auth/v1"`; `docs/runlocal.md` has a troubleshooting row. Hosted environments keep the default.
- Local phone sign-in also needed `[auth.sms.twilio]` enabled with fake values in `supabase/config.toml`.
- Verified on the real local stack on 2026-11-26: three seeded users sign in with `123456` and get `200` from `/api/me`.
- Existing machines: re-run `scripts/dev-env` (or set `Supabase__Issuer`) so `appsettings.Local.json` gets the issuer.
