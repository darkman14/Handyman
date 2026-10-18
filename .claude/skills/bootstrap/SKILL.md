---
name: bootstrap
description: Set up the entire Handyman project from the starter kit with the platform-dev agent, one workflow step at a time, committing after each verified step.
argument-hint: "[step number to resume from, default 1]"
disable-model-invocation: true
---

Bootstrap the project, starting at step "$ARGUMENTS" of `docs/workflow.md` section 6 (if that is empty, start at Step 1). You are the coordinator: platform-dev does each step, you verify and commit.

## 0. Preflight (only when starting at step 1)
- Check tools and report versions: `git --version`, `dotnet --version` (10.x), `node --version`, `docker info` (respecting `DOCKER_HOST`), `npx supabase --version`.
- If `.env.dev.local` doesn't exist, ask me where Docker runs. If it isn't this machine, create `.env.dev.local` from `.env.dev.local.example` with my answer (for example `DOCKER_IP=172.24.146.72`) and tell me which user environment variables to set for Testcontainers (`docker info` must work before continuing).
- `git status` must be clean. Switch to `develop`, then create `chore/bootstrap` from it.

## 1–5. One step at a time
For each step from the start step through Step 5:
1. Run platform-dev with: "Do docs/workflow.md section 6, Step N. Verify its Done condition."
2. When it returns, run the step's Done check yourself (the command only; summarize the result).
3. If it passes, commit with the step's commit message. If it fails, send the failure back to platform-dev once; if it still fails, stop and report.
4. If platform-dev returned a human checklist, show it to me and wait for my "continue" before the next step.

Run each step in a fresh platform-dev invocation; don't carry the previous step's output beyond a 3-line summary.

## 6. Finish
- Run all checks: `dotnet build backend -v q`, `dotnet test backend --logger "console;verbosity=minimal"`, `cd frontend && npm run typecheck && npm test -- --silent && npm run build:web`.
- Report: steps done, commits, checks, and my remaining manual tasks (GitHub repo and branch protection, test environment accounts and secrets) as a checklist.
- Print the commands to push `chore/bootstrap` and open a PR into `develop`. Never push yourself.
