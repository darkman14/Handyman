---
paths:
  - "frontend/**"
---

# Frontend rules

- Routes in `app/` stay thin: layout + feature components. Logic lives in `src/features/<area>/` hooks.
- Server state only through TanStack Query hooks that call the typed client in `src/api/client.ts`. No `fetch` in screens.
- Types come from `src/api/schema.d.ts` (generated with `npm run api:types`). Never write API types by hand.
- UI only from `src/ui` and `theme.ts` tokens: no hex colors, no magic spacing numbers in screens.
- Forms: `react-hook-form` + `zod`. Every screen has loading, empty and error states.
- User-facing strings live in `src/i18n/en.ts`. Sentence case, plain verbs ("Send bid", "Hire Jelena").
- Accessibility: `accessibilityLabel` on every touchable, 44×44 minimum target, font scaling respected.
- Platform differences only in `*.native.ts` / `*.web.ts` under `src/lib`.
