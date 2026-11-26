---
id: BUG-0026
title: Web e2e against the Metro dev server is slow and flaky because the gzipped dev bundle takes about 25 s
story: US-0009
criterion: n/a
severity: low
status: open
found-by: tech-lead
---

## Steps to reproduce
1. Start the web app with `npx expo start --web --port 8081` (Node v25, Windows).
2. Time the entry bundle: `curl` without `Accept-Encoding` takes about 0.7 s (6.2 MB); with `Accept-Encoding: gzip`, which every browser sends, it takes about 25 s (1.1 MB).
3. Run `npx playwright test e2e/us-0008 --retries=0`, or the first run of `us-0007`/`us-0008`.

## Expected
A page load or reload in the e2e suite takes a few seconds, and the suite passes without retries.

## Actual
Every page load takes about 25 s. `page.reload()` in the US-0008 tests then exceeds the 30 s test timeout. Without retries, 3 of 44 US-0008 tests fail (`AC2 typing again clears the error message`, `reload on /home keeps the session`, `reload on /code keeps the number and the screen`). With retries, 5 pass as flaky. The US-0009 spec needed raised timeouts for the same reason, and its full run takes about 13 minutes.

## Evidence
- The slowness is in the Metro server, not in app code: the bundle is served fast when not compressed.
- Both runs are on the feature branch, with `AuthGate` and the other US-0009 changes.

## Suggested fix (optional)
Point Playwright at a static `expo export --platform web` build served locally (fast and closer to production), or find out why Metro's gzip is slow on this setup (Node v25; the repo's CI uses lts).
