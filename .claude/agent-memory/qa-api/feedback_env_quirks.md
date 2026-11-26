---
name: env-quirks-and-migration-weak-spots
description: Tooling quirks in this repo's QA environment and recurring weak spots found in data migrations
metadata:
  type: feedback
---

- `docker exec` prints nothing in the Bash tool here (and PowerShell is disabled). To inspect the local Supabase DB read-only, use a temporary xunit probe with Npgsql on localhost:54322 (`default_transaction_read_only=on`), write output to the scratchpad, delete the probe afterwards.
- Do not patch C# files with Python `str.index` slices on repeated snippets (it deleted a block once); rewrite the file with Write or use unique anchors. Python heredocs turn `\n`/` ` into real characters.
- Migration weak spot: SQL scalar subqueries on tables without unique constraints (areas by name) fail with 21000 when duplicates exist; Down scripts matching by id range without name/children checks. Test duplicates, foreign rows on reference ids and child rows.
- Local dev DB gets junk from test runs (before ApiFactory fix); reference ids there are not 1-6/1-20.
- Local Supabase has the phone provider disabled (OTP returns `phone_provider_disabled`), so `dev-token.sh` fails; use `TestAuth.TokenFor` in ApiFactory for signed-in checks. No python in Bash here; use sed/printf for doc edits. API on 5080 runs as Handyman.Api.exe (taskkill to stop).
- Weak spot: `JwksCache` (Auth/SupabaseJwt.cs) fetches synchronously under a lock with the default 100 s HttpClient timeout (BUG-0013).
- API for real-stack checks: `dotnet run --project backend/src/Handyman.Api --no-build --urls http://localhost:5080` with Seed__Enabled=true, ASPNETCORE_ENVIRONMENT=Development (default launch port is 5088). Dev logs show EF SQL and outgoing HTTP, so seeding is verifiable by counting `INSERT INTO app.users` and `Sending HTTP request POST` lines. Raw `app.users` inserts in tests need rating_sum/rating_count. Lambda param named `_` shadows the `_ = x` discard.
