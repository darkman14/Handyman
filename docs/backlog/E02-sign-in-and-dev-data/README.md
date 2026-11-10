---
id: E02
title: Phone sign-in and seeded development users
slice: 2
status: draft
---

## Goal
Anyone on the team can open the app on a phone or in a browser, sign in with a seeded phone number and the local code `123456`, and reach the API as that user. The API trusts only valid Supabase tokens, knows who is calling, and shuts out blocked users. Every developer and QA agent starts from the same known cast of clients, workers, an admin and sample jobs (workflow.md section 7, "Seeded development data"), so later slices can be demoed and tested against realistic data from day one.

## Success measure
- A fresh clone, after the local stack starts, lets every one of the 10 seeded numbers sign in on web, iOS and Android with code `123456` and get a `200` from an authenticated API call, without any manual database or Supabase work.
- API tests prove `401` for missing, expired, wrong-issuer, wrong-audience and badly signed tokens, and `403` for blocked users.
- The seeder never runs in Production (proved by a test).
- Restarting the API repeatedly does not duplicate users, profiles or sample jobs.

## Features
- F02.1 API accepts only valid signed-in users
- F02.2 Seeded development users and sample jobs
- F02.3 Phone sign-in in the app

## Out of scope
- Profile creation and onboarding, `GET /me` profile shape and `profile_missing` (E03).
- Real SMS provider and production Supabase Auth settings (E11; provider choice is open in workflow.md section 11).
- Blocking users from the admin screen and banning them in Supabase Auth (E10). This epic only enforces `blocked_at` once it is set.
- Store referral handling (E09).
- Domain entities, the first migration and reference data seeding (trades, area tree, store) belong to E01 and are a prerequisite.
