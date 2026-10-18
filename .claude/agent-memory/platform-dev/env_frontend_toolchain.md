---
name: env-frontend-toolchain
description: Expo SDK 58 / TS 6 / RNTL 14 quirks hit while scaffolding frontend (types, test-renderer, npmrc, BOM in env file)
metadata:
  type: project
---

- TypeScript 6 no longer auto-includes @types: tsconfig needs `"types": ["jest","node"]`.
- `@testing-library/react-native` 14 needs peer `test-renderer`; its render/fireEvent are async (await them). jest-expo needs `@react-native/jest-preset`.
- `openapi-typescript` peers on TS 5, so `frontend/.npmrc` has `legacy-peer-deps=true` (needed for `npm ci` too).
- create-expo-app refuses/pollutes non-empty dir (frontend/ holds .env.local): scaffold in the scratchpad, then copy package.json/app.json/tsconfig/assets only. Template puts routes in src/app; we use root `app/`.
- Windows PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM, which breaks the first var in .env.local; dev-env.ps1 now writes no-BOM.
- Auto-mode classifier denies `rm -rf` and moving .env.local; use plain `rm -f` on named files.
- Large multi-file bash heredocs can truncate silently; use the Write tool for source files.
