# Run Handyman locally

Everything runs on your machine: Supabase (Postgres, Auth, Storage, Studio) in Docker, the .NET API, and the Expo app in a browser, an emulator or your phone. No real SMS is sent; seeded users sign in with code `123456`.

## 1. Prerequisites

| Tool | Check | Notes |
| --- | --- | --- |
| Docker Desktop (or Docker Engine) | `docker info` | Must be running; Supabase and backend tests use it |
| .NET 10 SDK | `dotnet --version` | 10.x |
| Node.js LTS | `node --version` | Includes `npx` |
| Git | `git --version` | |
| Expo Go on your phone (optional) | — | Or an Android emulator / iOS simulator |

The Supabase CLI runs through `npx supabase`, so no global install is needed.

## 1a. If Docker runs on a VM

Copy `.env.dev.local.example` to `.env.dev.local` and set `DOCKER_IP` (for example `172.24.146.72`). Then either develop inside the VM (recommended; also set `LAN_IP` to the VM address) or point your desktop's Docker tools at the VM with the `DOCKER_HOST` and `TESTCONTAINERS_HOST_OVERRIDE` user environment variables. The full steps are in `workflow.md` section 6, "Docker on another machine". After that, every URL in this guide that says `localhost` for Supabase or Postgres uses the VM address instead; `docs/local-credentials.md` shows the right ones.

## 2. First-time setup

New repo? Run `/bootstrap` in Claude Code instead of setting things up by hand (`workflow.md` section 6).

Run from the repo root.

```bash
# 1. Start local Supabase (first run downloads images, a few minutes)
npx supabase start

# 2. Write local config files and docs/local-credentials.md
#    Set LAN_IP if you want to use the app on your phone (see section 5)
./scripts/dev-env.sh                  # macOS, Linux, Git Bash, WSL
# or
./scripts/dev-env.ps1                 # Windows PowerShell

# 3. Restore .NET tools (dotnet-ef)
cd backend
dotnet tool restore
cd ..

# 4. Install app dependencies
cd frontend
npm ci
cd ..
```

`dev-env` creates three git-ignored files:

- `backend/src/Handyman.Api/appsettings.Local.json`: connection string, Supabase URL and service-role key, seeding switch, local CORS origin
- `frontend/.env.local`: Supabase URL, publishable (anon) key, API URL
- `docs/local-credentials.md`: all local credentials and seeded users, for you to look up

## 3. Run

Use two terminals.

**Terminal 1: API**

```bash
dotnet run --project backend/src/Handyman.Api
```

On startup in Development, the API applies EF Core migrations to schema `app` and seeds reference data, 10 users (4 workers, 5 clients, 1 admin) and sample jobs. Seeding is idempotent: users are matched by phone number, and sample jobs are added only when there are no jobs.

Check it: <http://localhost:5080/health> returns `200`, and the OpenAPI document is at <http://localhost:5080/openapi/v1.json>.

**Terminal 2: app**

```bash
cd frontend
npm run web          # browser at http://localhost:8081
# or
npx expo start       # then press a (Android emulator), i (iOS simulator), or scan the QR code with Expo Go
```

## 4. Sign in with seeded users

Enter any seeded phone number and code `123456`. The full list with roles is in `docs/local-credentials.md`.

| To test | Sign in as |
| --- | --- |
| Comparing bids and hiring | Ana Marković, +381 60 000 0201 |
| Sending a bid | Marko Jovanović, +381 60 000 0101 (sees Milan's electrical job) |
| The rating gate | Ivana Nikolić, +381 60 000 0203, or Jelena Petrović, +381 60 000 0102 |
| Worker approval | Admin, +381 60 000 0999, at <http://localhost:5080/admin>; Stefan Ilić is pending |
| A new client with a store referral | Mila Pavlović, +381 60 000 0205 |

To test both sides at once, use a normal browser window for one user and a private window (or the phone) for the other.

## 5. Use the app on your phone

The easiest way is the shared test environment (`workflow.md` section 6, Step 5): open its URL in the phone's browser. To run your local build on a phone instead:

1. Connect the phone and the computer to the same Wi-Fi.
2. Find the computer's LAN IP (`ipconfig` on Windows, `ipconfig getifaddr en0` on macOS, `hostname -I` on Linux).
3. Rewrite the config with that IP and restart the app:

   ```bash
   LAN_IP=192.168.1.20 ./scripts/dev-env.sh      # PowerShell: $env:LAN_IP="192.168.1.20"; ./scripts/dev-env.ps1
   cd frontend && npx expo start --clear
   ```

4. Scan the QR code with Expo Go (Android) or the Camera app (iOS).
5. If the app can't reach the API, allow inbound connections on ports 5080 and 54321 in the computer's firewall.

## 6. Daily commands

| Task | Command |
| --- | --- |
| Start / stop Supabase | `npx supabase start` / `npx supabase stop` |
| Supabase status and keys | `npx supabase status` |
| Open Supabase Studio | <http://localhost:54323> |
| Run the API | `dotnet run --project backend/src/Handyman.Api` |
| Run the web app | `cd frontend && npm run web` |
| Run the mobile app | `cd frontend && npx expo start` |
| Backend tests | `dotnet test backend` |
| Frontend tests | `cd frontend && npm test` |
| Frontend type check | `cd frontend && npm run typecheck` |
| Regenerate the OpenAPI file | `dotnet build backend` (writes `docs/api/Handyman.Api.json`) |
| Regenerate API types | `cd frontend && npm run api:types` |
| Web e2e tests (API + web running) | `cd frontend && npm run e2e` |
| Access token for a seeded user | `./scripts/dev-token.sh +381600000201` (PowerShell: `./scripts/dev-token.ps1 +381600000201`) |
| Call the API as that user | `curl -H "Authorization: Bearer $(./scripts/dev-token.sh +381600000201)" http://localhost:5080/api/jobs/mine` |
| Add a migration | `dotnet ef migrations add <Name> --project backend/src/Handyman.Api` |
| Apply migrations without starting the API | `dotnet ef database update --project backend/src/Handyman.Api` |

## 7. Reset to a clean database

```bash
npx supabase db reset
dotnet run --project backend/src/Handyman.Api
```

`db reset` wipes the local database, including Supabase auth users. The next API start migrates and seeds everything again. Keys don't change, so `dev-env` doesn't need to run again.

## 8. Ports

| Port | Service |
| --- | --- |
| 54321 | Supabase API (auth, storage, realtime) |
| 54322 | Postgres |
| 54323 | Supabase Studio |
| 5080 | Handyman API (`/api`, `/health`, `/admin`, `/openapi`) |
| 8081 | Expo dev server and web app |

## 9. Troubleshooting

| Problem | Fix |
| --- | --- |
| `supabase start` fails | Make sure Docker is running. If a port is taken, stop the other service or run `npx supabase stop --all` and start again |
| API fails with "password authentication failed" or "connection refused" | Supabase isn't running, or `appsettings.Local.json` is missing. Run `npx supabase start`, then `scripts/dev-env` |
| Sign-in says the phone provider or SMS is not configured | Check `[auth.sms]` in `supabase/config.toml` (`enable_signup = true`), then `npx supabase stop` and `start`. If your CLI version still requires a provider, enable `[auth.sms.twilio]` with placeholder values; numbers in `[auth.sms.test_otp]` never reach the provider |
| Code `123456` is rejected | The number isn't in `[auth.sms.test_otp]`, or was typed with a different format. Seeded numbers start with `+381` |
| API returns `401` for every call | The app is pointed at a different Supabase instance than the API. Run `scripts/dev-env` again and restart both |
| Web app shows CORS errors | The API must run in Development and `Cors:Origins` must include `http://localhost:8081` |
| Phone can't reach the API | Use `LAN_IP` (section 5), same Wi-Fi, and open ports 5080 and 54321 in the firewall |
| Backend tests fail to start containers | Docker must be running; tests use their own Postgres container, not local Supabase |
| Stale data or broken seed | Reset (section 7) |

## 10. Working with Claude Code

Start `claude` in the repo root once Supabase, the API and the web app run (QA agents need them). The AI team, skills and rules are described in `workflow.md` section 9; token-saving setup (graphify) in section 10. Agents are denied access to `docs/local-credentials.md` and `appsettings.Local.json`; they get tokens through `scripts/dev-token`.

## 11. Credentials file

`docs/local-credentials.md` holds local-only credentials: the Postgres user and password, Supabase keys and the seeded users. It's generated, git-ignored, and safe to regenerate at any time with `scripts/dev-env`. Never copy production credentials into it; production secrets live only in the hosting environment's variables.
