---
paths:
  - "backend/tests/**"
  - "frontend/**/*.test.ts"
  - "frontend/**/*.test.tsx"
  - "frontend/e2e/**"
---

# Testing rules

- Name tests after behavior: `Hire_returns_409_when_job_is_no_longer_open`, `shows the rating gate banner when a rating is pending`.
- Backend integration tests go through HTTP with `ApiFactory` and `TestAuth.TokenFor(userId)`; build data with builders, not by calling other endpoints unless the flow is the subject.
- Acceptance tests written by QA live in `backend/tests/Handyman.Api.Tests/Acceptance/US_XXXX_*.cs` and `frontend/e2e/us-XXXX-*.spec.ts`, one file per story, one test per acceptance criterion where possible.
- No snapshot tests. Query by role and accessible name in React Native Testing Library and Playwright.
- Tests never call a real Supabase project; the frontend mocks `src/lib/supabase.ts`.
- A flaky test is a bug: file it, don't retry it into passing.
