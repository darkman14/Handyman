---
id: E01
title: Business data model, use-case plumbing and reference data
slice: 1
status: draft
---

## Goal
The API gets a real home for every business fact the marketplace needs: users, worker profiles, trades, the area tree, stores, jobs, photos, bids, ratings, notifications and devices, all in schema `app` and locked down with row-level security so nothing leaks through Supabase's Data API. The job lifecycle (Open → Assigned → Done → Closed, or Cancelled) and the "both must rate" rule live in the domain from day one, so later slices cannot break them by accident. Every use case is built on the same small command/query plumbing, and every environment starts with the same reference data: the six trades, the Belgrade area tree (city → municipality → neighborhood) and the partner store `VRACAR01`. Anyone, including a client who has not signed in yet, can list trades and browse areas.

This unblocks E02 (seeded users, worker coverage, sample jobs, blocked-user check) and E03 (profile, trade and area pickers).

## Success measure
- On an empty Postgres (Testcontainers and local Supabase), one migration creates all 13 tables in `app`; a test proves every table has RLS enabled and no policies.
- `JobTests` cover every allowed and every forbidden transition, and prove a job closes only when both ratings exist.
- `DecoratorTests` and `ArchitectureTests` pass as described in workflow.md section 5 (CQRS).
- After migrating any environment, `GET /api/trades` returns the six trades and `GET /api/areas` walks Beograd → municipality → neighborhoods, without signing in and without the dev seeder.
- E02 stories US-0002 to US-0006 can move to `ready` because everything they wait for has a `ready` E01 story.

## Features
- F01.1 Business tables and first migration
- F01.2 Job lifecycle rules
- F01.3 Use-case plumbing (commands, queries, errors)
- F01.4 Reference data: trades, areas, partner store

## Out of scope
- Sign-in, the caller-to-`app.users` mapping, blocked-user check and the dev seeder (E02).
- `GET /me` profile shape, `profile_missing`, `PUT /me`, worker trades and coverage editing (E03). `GET /trades` and `GET /areas` moved into E01 (US-0019 decision, 2026-11-10).
- Any app screen. This slice has no frontend work (workflow.md section 7, slice 1).
- Endpoints for jobs, bids, ratings, notifications and devices (E04 to E08); only their tables and the `Job` domain rules are built here.
- Rating averages on users (E07), push sending and `PushQueue` workers (E08), Realtime (E12).
- The real pilot city and store: Belgrade and `VRACAR01` are placeholders (workflow.md section 11).
