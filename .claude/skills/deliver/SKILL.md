---
name: deliver
description: Deliver one ready user story end to end with the AI team (contract, parallel backend and frontend, integration, parallel QA).
argument-hint: "<US-id, e.g. US-0012>"
disable-model-invocation: true
---

Deliver story $ARGUMENTS. You are the tech lead: you coordinate, run checks and commit; the subagents do the work. Keep their full outputs out of the conversation and report only summaries.

## 0. Preconditions
- Find the story file in `docs/backlog/`. Its status must be `ready`; otherwise stop and tell me why.
- `git status` must be clean. Switch to `develop`; if a remote exists, run `git pull --ff-only`. Create and switch to `feat/<id-lowercase>-<slug>` from `develop`.
- Set the story status to `in-progress` and update `docs/backlog/board.md`.

## 1. Contract
- If the story touches the API: run backend-dev in contract mode. Then run `dotnet build backend -v q` and `cd frontend && npm run api:types`.
- Commit: `chore(<id>): API contract`.

## 2. Build in parallel
- Start backend-dev (build mode) and frontend-dev at the same time, in one message with two subagent calls. Give each the story path and anything from the contract step it needs.
- They work in different folders and don't commit.

## 3. Integrate
- Run: `dotnet build backend -v q`, `dotnet test backend --logger "console;verbosity=minimal"`, `cd frontend && npm run api:types && npm run typecheck && npm test -- --silent && npm run build:web`.
- On failure, send the failure summary back to the owning developer subagent. At most 2 rounds; then stop and report to me.
- Commit: `feat(<id>): <story title>`.

## 4. QA in parallel
- Make sure Supabase is running (`npx supabase status`). Start the API (`dotnet run --project backend/src/Handyman.Api`) and the web app (`cd frontend && npm run web`) in the background if they aren't running.
- Set the story status to `in-qa`. Start qa-api and qa-app at the same time, in one message with two subagent calls.

## 5. Fix loop
- For each bug of severity medium or higher: send it to the owning developer, re-run step 3 checks, then re-run only the affected QA agent for the affected criteria. At most 2 loops; then stop and report.
- Low-severity bugs stay open in the backlog unless I say otherwise.

## 6. Wrap up
- Commit tests and backlog updates: `test(<id>): acceptance tests and QA results`.
- Set the story status to `done` only if the Definition of Done in `CLAUDE.md` is met; otherwise leave it `in-qa` and explain.
- Report: what was built, test counts, bugs (open and fixed), commits, and the commands to push and open the PR into `develop` (`git push -u origin <branch>` and `gh pr create --base develop --fill`). Never push yourself, never merge, and never touch `main`; I do that.
