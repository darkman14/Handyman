---
name: env-docker-wsl
description: Docker runs in WSL (tcp://localhost:2375); Windows-side `npx supabase start` fails on volume mounts, use WSL binary
metadata:
  type: project
---

Docker engine is in WSL Ubuntu, reached from Windows via DOCKER_HOST=tcp://localhost:2375. `npx supabase start` from Windows fails ("invalid volume specification D:\...\snippets") because the engine can't mount Windows paths.

**Why:** the CLI bind-mounts the project dir using the Windows path.
**How to apply:** start/stop Supabase via the Linux CLI in WSL: `wsl -e bash -lc "cd /mnt/d/Code/Handyman && ~/sb/supabase start"` (binary installed at ~/sb/supabase in WSL; WSL has no Linux node). `npx supabase status` and `scripts/dev-env.ps1` work fine from Windows afterwards.
