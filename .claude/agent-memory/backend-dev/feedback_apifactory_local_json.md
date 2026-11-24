---
name: apifactory-local-json-gotcha
description: Test-env host loads appsettings.Local.json last, so UseSetting loses; ApiFactory must override via ConfigureAppConfiguration
metadata:
  type: feedback
---

Program.cs adds appsettings.Local.json (Development and Test) after host settings, so `UseSetting("ConnectionStrings:Default")` loses and ApiFactory tests silently wrote into the local Supabase DB (localhost:54322). Fixed in US-0018 with `ConfigureAppConfiguration(AddInMemoryCollection)` in ApiFactory.

**Why:** reference-data counts and fresh-DB assumptions are only valid on the Testcontainers DB.
**How to apply:** override config in tests via ConfigureAppConfiguration, never only UseSetting. Never grep/read appsettings.Local.json (secret; the permission classifier blocks it). Test files here use CRLF; node/Edit string replaces with `\n` silently miss.
