# Handyman: workflow and setup

Handyman connects clients with skilled workers. A store seller refers a client, the client posts a problem, matching workers bid, the client hires one, and both sides must rate each other before they can continue using the app.

This file is the single source of truth for decisions, the product workflow, and how to build the repo from an empty folder. It lives at `docs/workflow.md` next to `docs/README.md` (project overview) and `docs/runlocal.md` (how to run everything locally). Update it when a decision changes.

The repo starts from the **Handyman starter kit** (`handyman-starter.zip`): these docs, `CLAUDE.md`, the `.claude/` folder with the AI team, scripts, backlog templates and `.gitignore`. Every file in the kit is also printed in full in Appendix A, so this document alone is enough to rebuild it.

---

## 1. Decisions

| Area | Decision | Notes |
| --- | --- | --- |
| Name | Handyman | Namespace `Handyman.*`, app slug `handyman` |
| MVP scope | One city, 1–2 partner stores, small urgent repairs (appliances, electrical, plumbing) | Painting and carpentry are allowed as trades but not the focus |
| Backend | ASP.NET Core on .NET 10 (LTS), minimal APIs | Chosen over Node.js for speed of delivery |
| Architecture | Vertical slices in one API project, not Clean Architecture | Feature folders; state rules on entities; external services behind interfaces |
| CQRS | Own small implementation (about 150 lines): `ICommand`/`IQuery`, handler interfaces, decorators, assembly scanning. No MediatR, no dispatcher | Endpoints inject the exact handler they need; one database, no separate read models, no event sourcing |
| Platform | Supabase for Postgres, Auth (phone sign-in), Storage and later Realtime. The .NET API stays the only way the app reads or writes business data | Supabase project in an EU region |
| Database | Supabase Postgres, app tables in schema `app`, owned by EF Core migrations | Money as whole RSD in `int`; all times UTC; `app` is not exposed through Supabase's Data API and has RLS enabled with no policies |
| Frontend | React Native with Expo (TypeScript, Expo Router), built for iOS, Android **and web** from one codebase | One app with client and worker modes; web runs on react-native-web |
| Web app | Expo web export (single-page app), responsive layout, served by Caddy on the same domain as the API (`/` = web app, `/api` = API) | Same origin: no CORS |
| API contract | OpenAPI from the backend; TypeScript types generated with `openapi-typescript` | No hand-written API types in the app |
| Auth | Supabase Auth phone sign-in (SMS code). The app sends the Supabase access token to the API; the API validates it and maps `sub` to `app.users.id` | Roles (client, worker, admin) live in `app.users`, not in the token. SMS provider configured in Supabase. Local development uses Supabase test OTPs |
| Push | Native: Expo Push Service via `expo-notifications`. Web: standard Web Push (VAPID) with a service worker | Backend has one `IPushSender` per channel; a user can have native and web devices |
| Photos | Supabase Storage, private bucket `job-photos`; the API issues signed upload and download URLs | Max 5 photos per job |
| Admin | Razor Pages inside the API, admin role only | Approve workers, see jobs, block users |
| Background work | `BackgroundService` + in-memory `Channel` for push fan-out | No message broker |
| Hosting | One VPS, Docker Compose: API + Caddy (web app). Database, auth and storage on Supabase | Paid Supabase plan for the pilot: free projects pause when idle and have no backups |
| Payments | Outside the app (cash or bank transfer) | No escrow, invoices or disputes in the MVP |
| Location | Areas in three levels: city → municipality → neighborhood. Jobs and worker coverage are matched at neighborhood level | Seeded by hand for the pilot city |
| Address privacy | Bidders see neighborhood + street only. House number, entrance, floor and apartment are revealed to the hired worker | Enforced in the API response, not the app |
| Real-time | Push + pull to refresh in the MVP. Supabase Realtime Broadcast, sent by the API, is slice 12 | Replaces the earlier SignalR plan |
| Tests | Backend: xUnit, WebApplicationFactory, Testcontainers Postgres, Shouldly, test JWTs signed with a test key. Frontend: Jest (`jest-expo`) + React Native Testing Library with a mocked Supabase client | Every feature ships with tests; tests never call a real Supabase project |
| Local data | Development startup applies migrations and seeds reference data, 4 workers (3 approved, 1 pending), 5 clients, 1 admin and a few sample jobs | All seeded phone numbers sign in with a fixed local code; credentials go to the git-ignored `docs/local-credentials.md` |
| Branching | `main` = released code, `develop` = integration, both from the initial commit. Story branches `feat/us-NNNN-slug` and `fix/bug-NNNN-slug` from `develop`, PRs into `develop` (squash). Releases: PR `develop` → `main`, tagged `vX.Y.Z`. Hotfixes: `hotfix/*` from `main` | Section 6, "Branching and releases" |
| API contract file | OpenAPI generated at build time into `docs/api/Handyman.Api.json` (committed); the app's TypeScript types are generated from that file | Contract changes show up in PR diffs; frontend work can start before the API runs |
| Web e2e | Playwright tests in `frontend/e2e/`, owned by QA, run locally against the dev stack | Not in CI until there is a deployed test environment |
| AI team | Claude Code with project subagents: product owner, backend developer, frontend developer, two QA engineers; orchestrated by `/plan`, `/deliver`, `/qa` skills | Section 9 |
| Token usage | graphify knowledge graph + lean context practices | Section 10 |
| UI/UX | Modern defaults now, built on a theme + small component kit | Proper mockups come later; screens must not hard-code styles |

### Explicitly out of scope for the MVP

Microservices, Kubernetes, message brokers, MediatR or any other mediator/CQRS library, separate read/write databases, event sourcing, repositories over EF Core, Redis, SignalR, the app reading or writing tables directly through Supabase's Data API, Supabase Edge Functions for business logic, PostGIS radius search, in-app chat, in-app payments, a separate web frontend (the Expo app covers web).

---

## 2. Product workflow

### Job lifecycle

```
Open ──(client hires a bid)──> Assigned ──(either side finishes)──> Done ──(both have rated)──> Closed
  │                               │
  └──────(client cancels)─────────┴──> Cancelled
```

| Status | What happens | Who can act |
| --- | --- | --- |
| Open | Matched workers are notified and see the neighborhood and street; each can send one bid and withdraw it | Client: hire, cancel. Worker: bid, withdraw |
| Assigned | Hired worker sees the full address (house number, entrance, floor, apartment) and the client's phone; other bidders are told they weren't selected | Client: finish, cancel. Hired worker: finish |
| Done | Both sides must rate. Until they do, the client can't post and the worker can't bid | Client and hired worker: rate |
| Closed | Ratings are revealed to both; both rating averages update | Nobody |
| Cancelled | All bidders are notified | Nobody |

### Step by step

1. **Store referral.** The seller hands the client a flyer. Its QR code opens `/r/{code}`, which opens the installed app, or otherwise the web app, with the store code kept. The client can post a request in the browser without installing anything.
2. **Post.** The client picks a trade, writes a title and description, adds up to 5 photos, and enters the address: neighborhood, street, house number, and optionally entrance, floor, apartment and a note ("intercom broken, call on arrival"). The store code is attached to the client's first job.
3. **Notify.** Workers whose trades match and who cover the job's neighborhood get a push notification and an inbox item.
4. **Bid.** The worker sees the neighborhood and street, enough to judge travel time, and sends one bid: arrival window, diagnostic fee (RSD), hourly rate (RSD), optional note.
5. **Hire.** The client compares bids, with each worker's rating, and hires one.
6. **Finish.** After the work, either side marks the job finished.
7. **Rate.** Both give 1–5 stars and an optional comment. Ratings stay hidden until both are in.

### Business rules (enforced in the API)

- A worker sees only open jobs in their trades and covered neighborhoods, and only after admin approval.
- One bid per worker per job (unique `job_id, worker_id`).
- Hiring is atomic: `Job` carries a concurrency token, so if two hires race, only the first save succeeds and the second gets `409 Conflict` with code `job_not_open`.
- **Rating gate:** `POST /jobs` and `POST /jobs/{id}/bids` return `409` with code `rating_pending` if the caller has any `Done` job without their rating.
- The other side's rating is returned only when both ratings exist.
- Address visibility: the client always sees everything; bidders and matched workers get `neighborhood` and `street` only; the hired worker also gets `houseNumber`, `entrance`, `floor`, `apartment`, `addressNote` and the client's phone. The response DTO is chosen per role, so hidden fields are never serialized.
- SMS code limits are set in Supabase Auth's rate-limit settings.
- Blocked users get `403` on every API call and are also banned in Supabase Auth; their open jobs and active bids are hidden.
- The app never touches business tables through Supabase directly. It uses Supabase only for sign-in (and, from slice 12, for receiving Realtime messages).

---

## 3. Data model

All tables live in schema `app`. Supabase's own `auth` and `storage` schemas are not referenced by foreign keys, so the same migrations run on a plain Postgres in tests.

| Table | Key fields |
| --- | --- |
| users | id (= Supabase auth user id), phone, role (client, worker, admin), name, home_area_id, rating_sum, rating_count, blocked_at |
| worker_profiles | user_id, bio, approved_at |
| trades | id, name |
| worker_trades | worker_id, trade_id |
| areas | id, name, kind (city, municipality, neighborhood), parent_id — a tree; jobs and worker coverage point at neighborhoods |
| worker_areas | worker_id, area_id (neighborhood) |
| stores | id, name, area_id, referral_code |
| jobs | id, number, client_id, trade_id, area_id (neighborhood), street, house_number, entrance, floor, apartment, address_note, store_id, status, accepted_bid_id, created_at, finished_at, closed_at |
| job_photos | id, job_id, storage_path |
| bids | id, job_id, worker_id, arrival_window, diagnostic_fee, hourly_rate, note, status (active, accepted, rejected, withdrawn), created_at |
| ratings | id, job_id, rater_id, ratee_id, stars, comment, created_at — unique (job_id, rater_id) |
| notifications | id, user_id, type, job_id, created_at, read_at |
| device_tokens | id, user_id, token, platform |

---

## 4. API

| Method and path | Who | Purpose |
| --- | --- | --- |
| GET /me | Signed in | Profile, or `404` with code `profile_missing` after the first sign-in |
| PUT /me | Signed in | Create or update profile; workers also set trades and areas |
| GET /trades, GET /areas?parentId= | Anyone | Reference data; areas load level by level |
| POST /jobs | Client | Create job (rating gate) |
| GET /jobs/mine | Client | Own jobs with bids |
| GET /jobs/{id} | Client, matched worker or bidder | Job detail; address fields filtered by role |
| POST /jobs/{id}/photos/upload-url | Client | Signed Supabase Storage upload URL |
| POST /jobs/{id}/hire | Client | Body `{ bidId }` |
| POST /jobs/{id}/finish | Client or hired worker | Move to Done |
| POST /jobs/{id}/cancel | Client | From Open or Assigned |
| POST /jobs/{id}/rating | Client or hired worker | Body `{ stars, comment }` |
| GET /jobs/open | Worker | Matching open jobs not yet bid on |
| GET /bids/mine | Worker | Own bids with job status |
| POST /jobs/{id}/bids | Worker | Create bid (rating gate) |
| DELETE /bids/{id} | Worker | Withdraw while job is Open |
| GET /notifications, POST /notifications/read | Signed in | Inbox |
| POST /devices | Signed in | Register an Expo push token or a Web Push subscription |
| GET /r/{code} | Anyone | Store referral redirect (app if installed, else web app) |

All endpoints except `/r/{code}` and `/health` are served under `/api` (map them with `app.MapGroup("/api")`), so the web app and API share one domain.

Sign-in, SMS codes and token refresh are handled by Supabase Auth, so the API has no auth endpoints.

Errors use RFC 7807 problem details with a stable `code` field (`profile_missing`, `rating_pending`, `job_not_open`, `already_bid`, `not_allowed`).

---

## 5. Repo layout

```
handyman/
  backend/
    Handyman.sln(x)
    src/Handyman.Api/
      Features/
        Profile/  Jobs/  Bids/  Ratings/  Notifications/  Stores/  Reference/
      Domain/          Job.cs, Bid.cs, Rating.cs, JobStatus.cs, DomainException.cs
      Data/            AppDbContext.cs, ReadDbContext.cs, Configurations/, Migrations/
      Data/Seed/       ReferenceData.cs, DevUsers.cs, DevSampleJobs.cs, DevSeeder.cs
      Cqrs/            ICommand.cs, IQuery.cs, ICommandHandler.cs, IQueryHandler.cs, Unit.cs,
                       LoggingDecorators.cs, TransactionCommandDecorator.cs, CqrsRegistration.cs
      Auth/            SupabaseJwt.cs (token validation), CurrentUser.cs
      Infrastructure/  SupabaseOptions.cs, IBlobStorage.cs, SupabaseStorage.cs,
                       IPushSender.cs, ExpoPushSender.cs, WebPushSender.cs, PushQueue.cs, PushWorker.cs
      Admin/           Razor Pages
      Program.cs
    tests/Handyman.Api.Tests/
      Domain/          JobTests.cs (state machine unit tests)
      Cqrs/            DecoratorTests.cs, ArchitectureTests.cs
      Features/        one test class per feature (integration)
      Infrastructure/  ApiFactory.cs, PostgresFixture.cs, TestAuth.cs (signs test JWTs), FakeBlobStorage.cs
  frontend/
    app/               Expo Router routes
      (auth)/          sign-in.tsx, verify.tsx
      (client)/        requests.tsx, new-request.tsx
      (worker)/        open-jobs.tsx, my-bids.tsx, profile.tsx
      job/[id].tsx
      _layout.tsx
    src/
      api/             schema.d.ts (generated), client.ts
      features/        jobs/, bids/, ratings/, auth/, notifications/ (hooks + feature components)
      ui/              theme.ts, Button.tsx, Card.tsx, Screen.tsx, TextField.tsx, Stars.tsx, Badge.tsx
      lib/             supabase.ts, session-storage.native.ts, session-storage.web.ts, push.native.ts, push.web.ts, query-client.ts
    public/            sw.js (web push service worker), manifest.json
    *.test.tsx next to the code
    e2e/               Playwright web tests (owned by QA), playwright.config.ts
  CLAUDE.md                       project memory for Claude Code (kept short)
  .claude/
    settings.json                 shared permissions, env, graphify hook
    rules/                        backend.md, frontend.md, testing.md (path-scoped)
    agents/                       product-owner, backend-dev, frontend-dev, qa-api, qa-app, explore
    skills/                       plan/, deliver/, qa/
    agent-memory/                 what each agent learned (committed)
  docs/
    README.md                     project overview and links to the other docs
    api/Handyman.Api.json         OpenAPI contract, generated by the backend build
    backlog/                      epics, features, stories, bugs, board.md, _templates/
    runlocal.md                   full local setup and daily commands
    workflow.md                   this file
    local-credentials.example.md  committed template
    local-credentials.md          generated by scripts/dev-env, git-ignored
  scripts/
    dev-env.sh, dev-env.ps1       write local config files and docs/local-credentials.md from `supabase status`
    dev-token.sh, dev-token.ps1   print an access token for a seeded user (for API testing)
    guard-paths.mjs               hook that keeps each agent inside its folders
  supabase/            config.toml (local Supabase stack, test OTPs); no app migrations here
  deploy/              docker-compose.yml, Caddyfile (production)
  .github/workflows/ci.yml
  .editorconfig
  .gitignore
  README.md            short: what Handyman is + link to docs/README.md and docs/runlocal.md
```

### Backend conventions

- One file per use case in `Features/<Area>/`, holding the endpoint mapping, request/response records, validation and logic. Each area exposes `MapXxxEndpoints(this IEndpointRouteBuilder)`.
- Handlers use `AppDbContext` (commands) or `ReadDbContext` (queries) directly. No repositories. Endpoints contain no logic: bind input, call the handler, map the result to HTTP.
- Every slice is either a command or a query, built on the in-house CQRS code in `Cqrs/`. See the next section.
- State transitions are methods on `Job` (`Hire`, `Finish`, `Cancel`, `AddRating`) that throw `DomainException` on invalid transitions. A single exception handler maps it to `409`.
- External services only through `IPushSender` and `IBlobStorage`. The Supabase service-role key and the database connection string exist only in the API's configuration, never in the app.
- Connect to Supabase Postgres through the session pooler (IPv4). Transaction-mode pooling isn't needed for one API server and would require turning off prepared statements.

### CQRS (own implementation, no MediatR)

Every use case is a command (changes state) or a query (reads state). There is no mediator or dispatcher: each endpoint injects the one handler it calls, so "Go to definition" leads straight to the code and the compiler checks every wiring.

**Contracts** (`Cqrs/`):

```csharp
public interface ICommand<TResult>;
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}

public readonly record struct Unit
{
    public static readonly Unit Value = new();
}
```

**Reads and writes use different DbContexts.**

- `AppDbContext` is for command handlers.
- `ReadDbContext` is for query handlers. It uses the same model and connection, sets `QueryTrackingBehavior.NoTracking`, and overrides both `SaveChanges` and `SaveChangesAsync` to throw `InvalidOperationException`. A query that tries to write fails in tests, not in production.

**Decorators** wrap every handler and are applied by the registration code, outermost first:

| Handler kind | Decorators | What they do |
| --- | --- | --- |
| Command | `LoggingCommandDecorator` → `TransactionCommandDecorator` → handler | Log name, duration and outcome; open a transaction on `AppDbContext`, commit on success, roll back on exception; after commit, flush `PendingPushes` (pushes queued during the command) to `PushQueue`, so no push is sent for a rolled-back change |
| Query | `LoggingQueryDecorator` → handler | Log name and duration; warn above 500 ms |

Request validation stays at the endpoint (.NET 10 minimal API validation with data annotations), so handlers receive already-valid input. Business-rule violations are `DomainException`s thrown by entities. One exception handler maps `DomainException` and `DbUpdateConcurrencyException` to `409`, `NotFoundException` to `404`, and anything else to `500` without leaking details.

**Registration without a library** (`CqrsRegistration.cs`, called as `builder.Services.AddCqrs(typeof(Program).Assembly)`):

```csharp
public static IServiceCollection AddCqrs(this IServiceCollection services, Assembly assembly)
{
    // skip open generics, otherwise the decorators would register themselves as handlers
    var handlers = assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

    foreach (var impl in handlers)
    foreach (var iface in impl.GetInterfaces().Where(i => i.IsGenericType))
    {
        var def = iface.GetGenericTypeDefinition();
        Type[] decorators =
            def == typeof(ICommandHandler<,>) ? [typeof(TransactionCommandDecorator<,>), typeof(LoggingCommandDecorator<,>)] :
            def == typeof(IQueryHandler<,>)   ? [typeof(LoggingQueryDecorator<,>)] :
            [];
        if (decorators.Length == 0) continue;

        var args = iface.GetGenericArguments();
        services.AddScoped(impl);
        services.AddScoped(iface, sp =>
        {
            var inner = sp.GetRequiredService(impl);
            foreach (var d in decorators)   // innermost first
                inner = ActivatorUtilities.CreateInstance(sp, d.MakeGenericType(args), inner);
            return inner;
        });
    }
    return services;
}
```

**One slice = one file.** Example `Features/Jobs/HireBid.cs`:

```csharp
public static class HireBid
{
    public sealed record Request(Guid BidId);
    public sealed record Command(Guid JobId, Guid BidId, Guid ClientId) : ICommand<Unit>;

    internal sealed class Handler(AppDbContext db, PendingPushes pushes) : ICommandHandler<Command, Unit>
    {
        public async Task<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var job = await db.Jobs.Include(j => j.Bids)
                .SingleOrDefaultAsync(j => j.Id == cmd.JobId && j.ClientId == cmd.ClientId, ct)
                ?? throw new NotFoundException("job_not_found");

            job.Hire(cmd.BidId);                 // throws DomainException if not Open or bid unknown
            await db.SaveChangesAsync(ct);       // Job has a concurrency token, so a parallel hire fails here

            pushes.Add(job.HiredWorkerId!.Value, NotificationType.Hired, job.Id);
            foreach (var loser in job.RejectedBidderIds()) pushes.Add(loser, NotificationType.NotSelected, job.Id);
            return Unit.Value;
        }
    }

    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPost("/{jobId:guid}/hire", async (
                Guid jobId, Request req, CurrentUser user,
                ICommandHandler<Command, Unit> handler, CancellationToken ct) =>
            {
                await handler.Handle(new Command(jobId, req.BidId, user.Id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization("Client");
}
```

A query slice has the same shape: a `Query : IQuery<TResponse>` record, a `Handler(ReadDbContext db)` that projects with `Select` into the response record, and a `MapGet`. Each feature area has one `XxxEndpoints.Map(...)` that calls the `Map` of its slices.

**Concurrency for hiring.** `Job` has a Postgres `xmin` concurrency token. Two clients (or two taps) hiring at once both pass `job.Hire`, but only the first `SaveChanges` succeeds; the second throws `DbUpdateConcurrencyException`, which the exception handler maps to `409` with code `job_not_open`.

**Tests for the CQRS code itself:**

- `DecoratorTests`: the transaction decorator commits on success, rolls back on exception, and flushes `PendingPushes` only after commit; the logging decorators pass results and exceptions through unchanged.
- `ArchitectureTests` (plain reflection, no extra package): every `ICommand<T>` and `IQuery<T>` has exactly one handler; no query handler's constructor takes `AppDbContext`; no command handler takes `ReadDbContext`; every handler resolves from the DI container built by `ApiFactory`.

### Frontend conventions

- Screens use only components from `src/ui` and colors, spacing and type from `src/ui/theme.ts`. No inline hex colors or magic numbers in screens. When mockups arrive, the change happens in `src/ui`, not in every screen.
- Server state through TanStack Query hooks in `src/features/*`. Screens don't call `fetch` directly.
- Forms with `react-hook-form` + `zod`.
- Sign-in uses `supabase-js` only for auth: `signInWithOtp({ phone })` and `verifyOtp({ phone, token, type: 'sms' })`. The API client adds `Authorization: Bearer <access token>` from the current session.
- Session storage: native follows Supabase's Expo guide (session encrypted in AsyncStorage, key in `expo-secure-store`, because sessions exceed SecureStore's size limit). Web uses `supabase-js`'s default browser storage, protected by a strict Content-Security-Policy from Caddy.
- The app only gets the Supabase URL and publishable (anon) key, via `EXPO_PUBLIC_SUPABASE_URL` and `EXPO_PUBLIC_SUPABASE_ANON_KEY`.
- Platform differences live only in `*.native.ts` / `*.web.ts` files in `src/lib`; screens don't check `Platform.OS`.
- Responsive: `Screen` caps content width (about 720 px) and centers it on wide screens; lists become two columns above 1024 px where it helps (bids, open jobs). Hover and keyboard focus styles come from `src/ui`.
- Test on all three targets before merging a slice: iOS or Android (Expo Go or emulator) and a desktop browser.
- Every screen handles loading, empty and error states.
- Accessibility: every touchable has an `accessibilityLabel` and a 44×44 minimum target; text respects system font scaling.

---

## 6. Setup from scratch

Run everything from the empty `handyman/` folder. Prerequisites: .NET 10 SDK, Node.js LTS, Docker (for local Supabase and Testcontainers), Git, the Expo Go app (or an emulator), and a desktop browser for the web target.

### Step 0: Starter kit, Claude Code and graphify

1. Create the empty folder, unzip `handyman-starter.zip` into it, and initialise git:

   ```bash
   cd handyman
   git init -b main
   chmod +x scripts/*.sh          # macOS/Linux/WSL
   ```

2. Install Claude Code if you don't have it (native installer, or the VS Code extension), then sign in:

   ```bash
   curl -fsSL https://claude.ai/install.sh | bash        # macOS, Linux, WSL
   irm https://claude.ai/install.ps1 | iex               # Windows PowerShell
   claude                                                # in the repo root; sign in on first run
   ```

   On first start, accept the workspace trust prompt. The agents' path-guard hooks only run in a trusted folder.

3. Check the AI team loaded: run `claude agents` in the shell (lists subagents) and type `/` in a session to see `/plan`, `/deliver` and `/qa`.

4. Install graphify (section 10). Build the first graph only after Step 3, when there is code to index.

5. First commit, then create `develop` from it and work there from now on:

   ```bash
   git add -A && git commit -m "chore: starter kit"
   git branch develop
   git switch develop
   ```

6. When you create the GitHub repository, push both branches, make `develop` the default branch (so PRs target it by default and Claude Code worktrees branch from it), and protect both (section "Branching and releases" below):

   ```bash
   git remote add origin git@github.com:<you>/handyman.git
   git push -u origin main develop
   ```

Steps 1–4 below are done on `develop` directly or, better, each on its own `chore/*` branch merged into `develop` through a PR.

From here on you can let Claude do the setup steps: start `claude` and say, for example, *"Do docs/workflow.md section 6, Step 1. Stop when its Done condition is met and show me the result."* Review and commit after each step.

### Step 1: Repo skeleton

```bash
mkdir -p backend frontend .github/workflows docs/api
dotnet new editorconfig
npx supabase init
npx supabase start      # prints the local API URL, anon key, service-role key and DB URL
```

The kit already contains `.gitignore`, `CLAUDE.md`, `.claude/`, `docs/` and `scripts/`. Add a short root `README.md` that links to `docs/README.md` and `docs/runlocal.md`.

In `supabase/config.toml`:

- Under `[auth.sms]`, set `enable_signup = true` and `enable_confirmations = true`, and add every seeded phone number from the "Seeded development data" table to `[auth.sms.test_otp]` with code `"123456"` (numbers without `+`, e.g. `381600000101 = "123456"`). No real SMS is sent for these numbers.
- Under `[api]`, keep only the default schemas exposed. Schema `app` must not be listed.

`scripts/dev-env.sh` (or `dev-env.ps1`) from the kit reads `npx supabase status -o env` and writes three git-ignored files. Run it now, and again whenever Supabase keys or your LAN IP change:

| File | Contents |
| --- | --- |
| `backend/src/Handyman.Api/appsettings.Local.json` | Local connection string, `Supabase:Url`, `Supabase:ServiceRoleKey`, `Seed:Enabled = true`, `Cors:Origins = ["http://localhost:8081"]` |
| `frontend/.env.local` | `EXPO_PUBLIC_SUPABASE_URL`, `EXPO_PUBLIC_SUPABASE_ANON_KEY`, `EXPO_PUBLIC_API_URL`, using `LAN_IP` (default `localhost`) so a phone on the same Wi-Fi can reach the PC |
| `docs/local-credentials.md` | Database, Studio and Supabase keys, plus the seeded users table |

Newer Supabase CLI versions print `PUBLISHABLE_KEY` and `SECRET_KEY` instead of `ANON_KEY` and `SERVICE_ROLE_KEY`; the scripts fall back to those names.

Done when `npx supabase status` shows the stack running and the three files exist. Commit: `chore: repo skeleton and local Supabase`.

### Step 2: Backend projects

```bash
cd backend
dotnet new sln -n Handyman
dotnet new webapi -n Handyman.Api -o src/Handyman.Api
dotnet new xunit -n Handyman.Api.Tests -o tests/Handyman.Api.Tests
dotnet sln add src/Handyman.Api tests/Handyman.Api.Tests
dotnet add tests/Handyman.Api.Tests reference src/Handyman.Api

cd src/Handyman.Api
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Serilog.AspNetCore

cd ../../tests/Handyman.Api.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Testcontainers.PostgreSql
dotnet add package Shouldly

cd ../../src/Handyman.Api
dotnet add package Microsoft.Extensions.ApiDescription.Server   # build-time OpenAPI file

cd ../..
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

Then:

1. Delete the template's weather forecast endpoint.
2. In `Program.cs`, in Development only, add `builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true)` (written by `scripts/dev-env`), enable CORS for `Cors:Origins` (the Expo web dev server runs on another port locally; production is same-origin), and listen on `http://0.0.0.0:5080` so phones on the LAN can reach the API. Real environments use environment variables, never committed files.
3. In `AppDbContext`, call `modelBuilder.HasDefaultSchema("app")`. The first migration also runs `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` for every table (raw SQL in the migration), so nothing in `app` is readable through Supabase keys even if the schema is exposed by mistake.
4. Configure JWT bearer auth for Supabase tokens: issuer `{Supabase:Url}/auth/v1`, audience `authenticated`, signing keys from `{Supabase:Url}/auth/v1/.well-known/jwks.json` (projects on the legacy shared JWT secret validate with that secret instead). Map the `sub` claim to the current user id.
5. Add `GET /health` returning `200`.
5a. Generate the OpenAPI document at build time into the shared contract folder. In `Handyman.Api.csproj`:

    ```xml
    <PropertyGroup>
      <OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>
      <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../../../docs/api</OpenApiDocumentsDirectory>
    </PropertyGroup>
    ```

    The build runs the app's startup to read the endpoints, so migrations and seeding must not run then: skip them when `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"`. Done when `dotnet build backend` writes `docs/api/Handyman.Api.json`.
6. Make `Program` visible to tests (add `public partial class Program;` at the end of `Program.cs` if the SDK doesn't generate it).
7. In tests, create `ApiFactory : WebApplicationFactory<Program>` that starts a plain `PostgreSqlContainer`, swaps the connection string, applies migrations, replaces JWT validation with a test signing key (`TestAuth.TokenFor(userId)`), and swaps `IBlobStorage` for a fake.
8. Write the first integration tests: `GET /health` returns `200`; a protected test endpoint returns `401` without a token and `200` with a test token.

Done when `dotnet test` passes. Commit: `chore: backend skeleton with test harness`.

### Step 3: Frontend project

```bash
cd frontend
npx create-expo-app@latest . --template default
npx expo install expo-secure-store expo-notifications expo-image-picker expo-linking
npx expo install react-dom react-native-web @expo/metro-runtime
npx expo install jest-expo jest @testing-library/react-native
npx expo install @react-native-async-storage/async-storage react-native-get-random-values
npm install @supabase/supabase-js aes-js @tanstack/react-query react-hook-form zod @hookform/resolvers openapi-fetch
npm install -D openapi-typescript @types/jest @types/aes-js @playwright/test
npx playwright install chromium
```

Then:

1. Remove the template's example screens and components.
2. In `package.json` set `"jest": { "preset": "jest-expo" }` and scripts `"test": "jest"`, `"typecheck": "tsc --noEmit"`, `"api:types": "openapi-typescript ../docs/api/Handyman.Api.json -o src/api/schema.d.ts"`, `"e2e": "playwright test"`, `"web": "expo start --web"`, `"build:web": "expo export --platform web"`.
    In `app.json`, set `"web": { "output": "single", "bundler": "metro" }`.
    Add `playwright.config.ts` with `testDir: "e2e"`, `baseURL: "http://localhost:8081"`, Chromium only, and screenshots on failure into `e2e/artifacts/`. Exclude `e2e/` from Jest (`testPathIgnorePatterns`).
3. Run `scripts/dev-env` to create `frontend/.env.local`, then create `src/lib/supabase.ts` that creates the client with the platform session storage. Add a Jest mock for `src/lib/supabase.ts`.
4. Create `src/ui/theme.ts` with color, spacing, radius and type tokens, light and dark. Build `Screen`, `Button`, `Card`, `TextField`, `Badge` and `Stars` on it.
5. Write one test per UI component (renders, handles press, has an accessibility label).

Done when `npm run typecheck` and `npm test` pass and `npx expo start` opens a placeholder home screen. Commit: `chore: frontend skeleton with test harness`.

Now build the first knowledge graph (section 10): in Claude Code run `/graphify .`, then `graphify hook install` in the shell.

### Step 4: CI

Create `.github/workflows/ci.yml`, triggered on pushes and pull requests to `main` and `develop`, with two jobs:

- **backend:** checkout → `actions/setup-dotnet` with `10.0.x` → `dotnet test backend` (Testcontainers runs on the hosted Ubuntu runner's Docker; no Supabase needed).
- **frontend:** checkout → `actions/setup-node` with `lts/*` and npm cache → `npm ci`, `npm run typecheck`, `npm test -- --ci`, `npm run build:web` in `frontend` (a failing web export fails CI).

Commit: `ci: backend and frontend tests`.

### Branching and releases

```
main     ●────────────────────────────●  v0.1.0 ───────●  v0.1.1
          \                          /                 /\
develop    ●──●──────●──────●──────●─●────────────────●──●──>
               \    /  \    /                        /
feat/us-0001    ●──●    \  /                        /
feat/us-0002             ●●             hotfix/0.1.1 (from main, merged to main and back to develop)
```

| Branch | Created from | Merged into | How |
| --- | --- | --- | --- |
| `main` | initial commit | — | Only release and hotfix PRs. Every merge is tagged `vX.Y.Z` |
| `develop` | initial commit | `main` (release PR) | Integration branch; always green |
| `feat/us-NNNN-slug` | `develop` | `develop` | One story per branch (`/deliver` creates it); squash merge, PR title `feat(US-NNNN): <title>` |
| `fix/bug-NNNN-slug` | `develop` | `develop` | Bugs found after a story was merged |
| `chore/*` | `develop` | `develop` | Setup, tooling, dependency updates |
| `hotfix/vX.Y.Z` | `main` | `main`, then `main` → `develop` | Urgent production fixes only |

Releases: open a PR from `develop` to `main` titled `release: vX.Y.Z`, merge with a merge commit (not squash, so `develop` and `main` stay related), tag the merge commit `vX.Y.Z`, and push the tag. Production deploys from `main` tags; a test environment can deploy from `develop`.

GitHub settings:

- Default branch: `develop`.
- Protect `main` and `develop`: pull request required, CI status checks required, no force pushes, no deletion. On `main` also require one approval (yours) and allow only merge commits; on `develop` allow only squash merges.
- Delete head branches after merge.

Claude follows this through `CLAUDE.md` and `/deliver`: it branches from `develop`, never commits to `main` or `develop`, never merges, and never pushes. You push, open the PR and merge.

---

## 7. Build order

Each slice is done only when its backend tests, frontend tests and CI pass. Every story gets its own `feat/*` branch from `develop`, merged back into `develop` through a PR. Release to `main` when a slice (or a set of slices) is ready for the pilot.

| # | Slice | Backend | Frontend | Required tests |
| --- | --- | --- | --- | --- |
| 1 | Domain + data + CQRS | Entities, `JobStatus`, `AppDbContext` and `ReadDbContext` in schema `app`, first migration with RLS enabled, seed (trades, area tree for the pilot city, 1 store), the `Cqrs/` contracts, decorators, `PendingPushes` and `AddCqrs` | — | `JobTests`: every allowed and forbidden transition; rating closes only when both exist. `DecoratorTests` and `ArchitectureTests` as described in the CQRS section |
| 2 | Auth + dev users | Supabase JWT validation, `CurrentUser` from `sub`, blocked-user check. `DevSeeder` (Development + `Seed:Enabled` only): applies migrations, then creates every user in `DevUsers.cs` through the Supabase Auth admin API (`phone_confirm: true`), reusing an existing auth user with the same phone, and upserts the matching `app.users` row; `DevSampleJobs` adds sample jobs only when no jobs exist | Sign-in and code screens with `supabase-js`, platform session storage, auth guard in `_layout`, token attached to API calls | API: `401` for missing, expired, wrong-issuer and wrong-audience tokens; `403` for blocked users; `DevUsers` and the `[auth.sms.test_otp]` numbers in `supabase/config.toml` match exactly (a test parses the file); the seeder refuses to run outside Development. App: screen tests for both forms with a mocked Supabase client; manual check with local test OTPs on phone and web |
| 3 | Profile + reference data | `GET /me` (`profile_missing` on first sign-in), `PUT /me`, `/trades`, `/areas`, worker trades and covered neighborhoods | Onboarding after first sign-in, profile screen, trade picker, area picker (city → municipality → neighborhoods; picking a municipality selects all its neighborhoods), mode switch | Worker without approval can't list jobs; coverage stored at neighborhood level |
| 4 | Post a job | `POST /jobs` with structured address, `GET /jobs/mine`, Supabase Storage signed upload/download URLs, store code | New request form with neighborhood picker and address fields, My requests list | Validation errors (neighborhood, street, house number required), rating gate returns `rating_pending` |
| 5 | Open jobs + bids | `GET /jobs/open`, `POST /jobs/{id}/bids`, withdraw, `GET /bids/mine` | Open jobs list showing neighborhood + street, bid form, My bids | Matching by trade and neighborhood, one bid per job, rating gate, open-job and bid responses contain no house number, entrance, floor, apartment or note |
| 6 | Hire | `HireBid` command with the `xmin` concurrency token | Bid comparison on job detail, hire button, full address + call button for the hired worker | Two concurrent hires → one `200`, one `409`; hired worker gets the full address, other bidders still get street only |
| 7 | Finish + rate | Finish, cancel, rating with hidden reveal, average update | Finish button, rating form, gate banner | Gate blocks posting and bidding until rated; ratings hidden until both exist |
| 8 | Notifications | Inbox table, `PushQueue` + `PushWorker`, `ExpoPushSender` and `WebPushSender` (VAPID), `/devices` accepts both kinds | Permission prompt (on web only after a user click), token or subscription registration, service worker, inbox screen, tap opens job | Each event writes the right notifications and goes to every device of the user; invalid tokens and expired subscriptions are removed |
| 9 | Store referral | `GET /r/{code}` redirect, referral stored on first job | Deep link handling on native, `?ref=` handling on web, code kept until first job | Referral attached once, unknown code ignored, web flow works without the app |
| 10 | Admin | Razor Pages: approve workers, list jobs, block user (also bans in Supabase Auth via the admin API) | — | Non-admin gets `403` |
| 11 | Deploy | Supabase project (EU region, paid plan): phone auth with the SMS provider, `job-photos` bucket, EF migrations applied through the session pooler. VPS: Dockerfile, `deploy/docker-compose.yml` with API + Caddy (static web build at `/`, API at `/api`, strict CSP) | EAS build profiles, TestFlight / Play internal testing, web export copied into the Caddy image | Smoke test against staging on phone and browser |
| 12 | Live updates (after the pilot) | API publishes Supabase Realtime Broadcast messages to private channels `user:{id}`; an RLS policy on `realtime.messages` lets each user join only their own channel | Subscribe with `supabase-js` while the app is in the foreground; on a message, invalidate the matching TanStack Query keys | A user can't join another user's channel; every push event also produces a broadcast |

After each backend change to the API, `dotnet build backend` regenerates `docs/api/Handyman.Api.json`; then run `npm run api:types` in `frontend` and commit both files with the change. `/deliver` does this for you.

With the AI team, each slice becomes an epic: run `/plan <slice number>`, review and approve the stories, then `/deliver` them one by one (section 9).

### Seeded development data

Defined in `Data/Seed/DevUsers.cs` and `DevSampleJobs.cs`. Placeholder Belgrade areas until the pilot city is chosen. Every number signs in locally with code `123456`.

| Role | Name | Phone | Trades | Covers / lives in | Notes |
| --- | --- | --- | --- | --- | --- |
| Worker | Marko Jovanović | +381 60 000 0101 | Electrical, Heating & AC | Novi Beograd, Zvezdara | Approved |
| Worker | Jelena Petrović | +381 60 000 0102 | Appliance repair | Vračar, Zvezdara | Approved; has a finished job waiting for her rating |
| Worker | Nikola Đorđević | +381 60 000 0103 | Appliance repair, Plumbing | Vračar, Novi Beograd | Approved |
| Worker | Stefan Ilić | +381 60 000 0104 | Painting, Carpentry | Vračar | Pending approval (tests the admin flow) |
| Client | Ana Marković | +381 60 000 0201 | — | Čubura (Vračar) | Open dishwasher job with bids from Jelena and Nikola |
| Client | Milan Kostić | +381 60 000 0202 | — | Bežanijska kosa (Novi Beograd) | Open electrical job, no bids yet |
| Client | Ivana Nikolić | +381 60 000 0203 | — | Đeram (Zvezdara) | Finished job with Jelena, waiting for both ratings (tests the rating gate) |
| Client | Petar Stojanović | +381 60 000 0204 | — | Neimar (Vračar) | No jobs |
| Client | Mila Pavlović | +381 60 000 0205 | — | Krunski venac (Vračar) | No jobs; referred by store `VRACAR01` |
| Admin | Admin | +381 60 000 0999 | — | — | Razor Pages admin |

Seeded store: Home Center Vračar, referral code `VRACAR01`.

---

## 8. UI/UX now and later

- Now: clean, modern defaults built only from `src/ui` components and `theme.ts` tokens, light and dark mode, system font scaling, clear loading, empty and error states.
- Later: when mockups arrive, update `theme.ts` first, then the `src/ui` components, then adjust screen layouts. Component tests in `src/ui` must keep passing; snapshot tests are avoided so a redesign doesn't break dozens of tests for no reason.
- Copy uses plain verbs and sentence case: "Post request", "Send bid", "Hire Jelena", "Mark work as finished", "Submit rating". Serbian localization is planned; keep all user-facing strings in one place (`src/i18n/en.ts`) from the start.

---

## 9. AI team with Claude Code

The project is built by you plus a small team of Claude Code subagents. You are the **tech lead and final reviewer**: you approve stories, watch the work, push and merge. The main Claude Code session acts as **delivery coordinator**: it runs the skills, starts the agents, runs the checks and commits. The agents do the specialised work, each in its own context window and its own folders.

### The team

| Agent | Who they are | What they do | What they may edit | Model |
| --- | --- | --- | --- | --- |
| `product-owner` | Senior product owner, 10+ years on two-sided marketplaces. Thinks like clients, workers and store sellers | Turns build slices or ideas into epics, features and user stories with numbered Given/When/Then acceptance criteria, business rules, UI notes, out-of-scope and test notes. Keeps `board.md` in sync. Raises open questions instead of guessing. Never writes code | `docs/backlog/` | Opus: requirements reasoning pays off |
| `backend-dev` | Senior .NET engineer, 10+ years of C#, ASP.NET Core, EF Core, PostgreSQL, domain modelling and testing | **Contract mode:** defines request/response records and endpoints, regenerates the OpenAPI file, writes the API contract into the story. **Build mode:** test-first implementation of the slice, migrations, domain rules, integration tests | `backend/`, `docs/api/`, the story's "API contract" and "Dev notes" | Same as main session |
| `frontend-dev` | Senior React Native/Expo engineer, 8+ years of React, fluent in Expo Router, TanStack Query, accessibility and responsive web | Builds screens, feature hooks and UI kit components against the generated API types, with component tests for every visible criterion and every screen state | `frontend/app/`, `frontend/src/`, assets, `package.json`, app config | Same as main session |
| `qa-api` | Senior QA engineer, 10+ years in API and integration testing; thinks in risks: authorization, data leaks, races, invalid states | Writes acceptance tests per criterion in `backend/tests/.../Acceptance/`, adds negative cases, explores the running API with seeded users' tokens, files bugs, fills QA results | `backend/tests/`, `docs/backlog/` | Sonnet: cheaper, plenty for test work |
| `qa-app` | Senior QA engineer, 10+ years in web and mobile testing, Playwright, exploratory testing, WCAG 2.2 AA | Exploratory testing in a real browser (Playwright MCP) as seeded users at phone and desktop width, writes Playwright e2e tests per criterion, files bugs, lists checks for a real device | `frontend/e2e/`, `docs/backlog/` | Sonnet |
| `Explore` | Overrides Claude Code's built-in explorer | Read-only code search, graph-first; used automatically for lookups | nothing | Haiku: lookups are cheap |

Why two QAs: they test the same story from two sides at the same time. `qa-api` proves the rules hold at the API (where security and data leaks live); `qa-app` proves a real person can complete the flow. Between them every acceptance criterion gets an API verdict and an app verdict.

### How the files fit together

| File | Purpose |
| --- | --- |
| `CLAUDE.md` | Short project memory loaded in every session and every custom agent: map, commands, hard rules, Definition of Done |
| `.claude/rules/*.md` | Path-scoped rules that load only when Claude works with matching files (backend, frontend, tests) |
| `.claude/agents/*.md` | The team: role, tools, model, memory, and a `PreToolUse` hook that blocks edits outside the agent's folders |
| `.claude/skills/{plan,deliver,qa}/SKILL.md` | The workflows you trigger with `/plan`, `/deliver`, `/qa` |
| `.claude/settings.json` | Shared permissions (allowed build/test commands; denied credential files, `git push`, `supabase db reset`) |
| `.claude/agent-memory/<agent>/` | Each agent's persistent notes (`memory: project`), committed so learnings survive |
| `scripts/guard-paths.mjs` | The folder guard used by the agents' hooks |
| `docs/backlog/` | Stories are the hand-off between agents; chat history is not |

Personal overrides go in `.claude/settings.local.json` and `CLAUDE.local.md` (both git-ignored).

### The delivery flow

```
/plan 4 ──> product-owner writes E04 stories (draft/ready) ──> you review, edit, set "ready"
                                                                   │
/deliver US-0012                                                   ▼
  0. branch feat/us-0012-… from develop, status in-progress
  1. backend-dev (contract) ──> docs/api/Handyman.Api.json ──> npm run api:types        commit
  2. backend-dev (build)  ║  frontend-dev (build)        ← in parallel, separate folders
  3. coordinator runs all checks; failures go back to the owner (max 2 rounds)         commit
  4. qa-api  ║  qa-app                                    ← in parallel, status in-qa
  5. bugs ≥ medium go back to the owning dev, affected QA re-runs (max 2 loops)
  6. QA results + tests committed, status done, PR command printed                     commit
you: review the diff, push, open the PR into develop, merge (squash)
```

Why this shape:

- **Contract first** lets backend and frontend work at the same time without guessing: the frontend builds against generated types from the committed OpenAPI file.
- **Separate folders instead of separate worktrees.** Backend and frontend never touch the same files, the guard hook enforces it, and only the coordinator commits, so parallel agents can't conflict. (Claude Code's `isolation: worktree` branches from the default branch, `develop`, not from your feature branch, so it would miss the contract commit.)
- **QA writes tests, never fixes.** Keeping testers out of product code keeps their verdicts independent.
- **Loop limits** stop agents from burning tokens on a problem that needs you.

### Using it day to day

| You want to | Do |
| --- | --- |
| Plan the next slice | `/plan 5` (or `/plan "store dashboard for referrals"`) |
| Approve stories | Edit the story files, set `status: ready`, commit |
| Build a story | `/deliver US-0012` |
| Re-test after your own fix | `/qa US-0012` |
| Ask one agent directly | `@"backend-dev (agent)" add an index on bids(job_id, status)` |
| Run a session as one agent | `claude --agent qa-app` |
| See running agents and their models | `/tasks` |
| Check context use | `/context` |
| Start the next story fresh | `/clear` (the story files carry the state) |

Rules of thumb:

- One story per session. `/clear` after each `/deliver`; everything the next story needs is in the repo.
- Read the agents' summaries, not their transcripts. Open a transcript in `/tasks` only when something went wrong.
- Review every diff before pushing. Agents are senior, not infallible; you own what ships.
- When an agent keeps making the same mistake, fix it once in its agent file, a rule file or `CLAUDE.md`, not in chat.
- Mobile-native checks (camera/photo picker, push notifications on a device, deep links from the QR code) stay with you; `qa-app` lists what to check.

### Setup checklist

1. Unzip the starter kit (Step 0); the files are already in place.
2. Start `claude` in the repo root and accept workspace trust.
3. `claude agents` lists six agents; `/` shows the three skills.
4. Smoke test: `@"product-owner (agent)" read docs/backlog/README.md and tell me the next free story id`. Then try to make it edit `backend/`; the guard must block it.
5. Once Step 3 is done, the full flow works: `/plan 1`, approve, `/deliver US-0001`.

Running five agents costs more usage than one session: parallel agents each send their own requests. The model choices above (Opus only for the PO, Sonnet for QA, Haiku for exploring) and section 10 keep that in check.

---

## 10. Token usage reduction

### graphify (code knowledge graph)

graphify parses the repo into a knowledge graph (code through tree-sitter locally, docs through Claude) and lets agents query the graph instead of grepping and reading many files. Claude Code integration adds a section to `CLAUDE.md` and a `PreToolUse` hook that points Claude to the graph before `Glob`/`Grep` calls.

Install once per machine (Python 3.10+):

```bash
uv tool install graphifyy          # or: pip install graphifyy   (the package name has two y's)
graphify install                   # registers the /graphify skill for Claude Code (Windows: graphify install --platform windows)
```

Set up the repo (after Step 3, when there is code):

```bash
graphify claude install            # adds the graphify section to CLAUDE.md and the PreToolUse hook to .claude/settings.json
```

```text
/graphify .                        # inside Claude Code: builds graphify-out/ (graph.json, GRAPH_REPORT.md, graph.html)
```

```bash
graphify hook install              # git post-commit and post-checkout hooks keep the graph current
git add CLAUDE.md .claude/settings.json && git commit -m "chore: graphify integration"
```

`graphify-out/` is git-ignored: every machine and every checkout rebuilds it through the git hooks, so it never goes stale in the repo.

Use it:

| Situation | Command |
| --- | --- |
| Question about how something works | `graphify query "how does hiring notify the losing bidders" --graph graphify-out/graph.json` (agents do this on their own; the `Explore` agent starts with it) |
| See the architecture | open `graphify-out/graph.html` in a browser |
| After a big refactor or branch switch | `/graphify . --update` (re-extracts only changed files) |
| Check the hooks still work after upgrading graphify | `graphify hook status`; re-run `graphify install` and `graphify hook install` after upgrades |

Be realistic about it: savings grow with repo size. On a fresh repo the graph saves little; it pays off once there are dozens of slices. Measure instead of assuming: compare `/context` and usage for the same kind of story with and without the graph. If it doesn't help, `graphify claude uninstall` removes it cleanly.

### Habits that save more than any tool

1. **Keep `CLAUDE.md` short** (under ~150 lines) and put detail in path-scoped `.claude/rules/` files that load only when relevant.
2. **Never load `docs/workflow.md` whole.** `CLAUDE.md` tells agents to jump to sections; stories quote the rules they need.
3. **Let subagents take the noise.** Test runs, logs and searches stay in the agent's context; only the summary returns.
4. **Quiet commands.** `dotnet build -v q`, `dotnet test --logger "console;verbosity=minimal"`, `npm test -- --silent`.
5. **One story per session, `/clear` between stories.** The backlog files carry the state, not the chat.
6. **Right model per job.** Opus for the PO, the main model for developers, Sonnet for QA, Haiku for exploring.
7. **Loop limits** in `/deliver` (2 fix rounds) stop runaway retries.
8. **Prompt caching is automatic.** Stable `CLAUDE.md` and agent prompts keep cache hits high, so avoid editing them in the middle of a story.

---

## 11. Open questions

- Which city and which first store for the pilot?
- Business model after the pilot: worker subscription, pay per lead, or store partnership?
- SMS provider for Supabase phone auth: one with built-in support (Twilio, Vonage), or Infobip through Supabase's Send SMS hook. Compare delivery and price for Serbian numbers.
- Should a no-show be a separate status, or is cancel plus an admin report enough?
- Neighborhood list for the pilot city: which level matches how people describe location (settlements, local communities, or informal neighborhood names)? Seed by hand for the pilot; consider importing an official address register later if its license allows.

---

## Appendix A: Starter kit files

The same files are in `handyman-starter.zip`. `docs/README.md`, `docs/runlocal.md` and `docs/local-credentials.example.md` are separate documents in the kit and aren't repeated here.

### `CLAUDE.md`

```markdown
# Handyman

Marketplace app: store sellers refer clients, clients post repair problems, matching workers bid, the client hires one, and both sides must rate each other. Mobile (iOS, Android) and web from one Expo app; ASP.NET Core API; Supabase for Postgres, phone sign-in and storage.

## Where things are

- `docs/workflow.md`: decisions, business rules, data model, API, build order, AI team. Don't read it whole; jump to the section you need (`grep -n "^## " docs/workflow.md`).
- `docs/backlog/`: epics, features, user stories and bugs. The story file is the source of truth for the task.
- `docs/api/Handyman.Api.json`: OpenAPI contract, generated by `dotnet build backend`. Never edit by hand.
- `backend/src/Handyman.Api/`: API (Features/ = vertical slices, Domain/, Data/, Cqrs/, Auth/, Infrastructure/).
- `backend/tests/Handyman.Api.Tests/`: xUnit tests (Testcontainers Postgres).
- `frontend/`: Expo app (`app/` routes, `src/features`, `src/ui`, `src/lib`, `src/api`), Jest tests, `e2e/` Playwright web tests.

## Commands

| Task | Command |
| --- | --- |
| Backend build (also regenerates the OpenAPI file) | `dotnet build backend -v q` |
| Backend tests | `dotnet test backend --logger "console;verbosity=minimal"` |
| Frontend types from contract | `cd frontend && npm run api:types` |
| Frontend checks | `cd frontend && npm run typecheck && npm test -- --silent` |
| Web build check | `cd frontend && npm run build:web` |
| Web e2e (API + web running) | `cd frontend && npm run e2e` |
| Access token for a seeded user | `./scripts/dev-token.sh +381600000201` |

## Hard rules

- Business rules live in the API. The app never reads or writes business tables through Supabase; it uses Supabase only for sign-in.
- Backend: vertical slices, one file per use case, in-house CQRS (`Cqrs/`), no MediatR, no repositories. Queries use `ReadDbContext`, commands use `AppDbContext` and domain methods.
- Frontend: screens use only `src/ui` components and `theme.ts` tokens; server state via TanStack Query hooks in `src/features`; platform code only in `*.native.ts` / `*.web.ts`.
- Every change ships with tests. A task is not done until backend tests, frontend typecheck, tests and web build pass.
- Never read or print `docs/local-credentials.md`, `appsettings.Local.json` or any secret. Never hard-code keys.
- Don't commit or push unless the main session's workflow says so. Subagents never commit.
- Branches: `main` (released) and `develop` (integration). Work only on `feat/*` or `fix/*` branches created from `develop`; PRs target `develop`. Never commit to `main` or `develop` directly; never merge locally.
- Change `docs/workflow.md` only when a decision changes, and say so in the summary.

## AI team

Subagents in `.claude/agents/`: `product-owner`, `backend-dev`, `frontend-dev`, `qa-api`, `qa-app` (and a cheaper `Explore`). Skills: `/plan <slice or idea>`, `/deliver <US-id>`, `/qa <US-id>`. Each agent may edit only its own area (enforced by `scripts/guard-paths.mjs`).

## Definition of done

1. All acceptance criteria in the story pass, checked by both QA agents.
2. Backend tests, frontend typecheck, tests and web build are green.
3. No open bug of severity high or critical for the story.
4. Story status is `done`, QA results are filled in, and the OpenAPI file and generated types are committed.
```

### `.claude/settings.json`

```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet build *)",
      "Bash(dotnet test *)",
      "Bash(dotnet run *)",
      "Bash(dotnet format *)",
      "Bash(dotnet ef migrations add *)",
      "Bash(npm test)",
      "Bash(npm test *)",
      "Bash(npm run *)",
      "Bash(npx tsc *)",
      "Bash(npx expo export *)",
      "Bash(npx playwright test *)",
      "Bash(npx supabase status *)",
      "Bash(git status *)",
      "Bash(git diff *)",
      "Bash(git log *)",
      "Bash(git branch *)",
      "Bash(git switch *)",
      "Bash(git add *)",
      "Bash(git commit *)",
      "Bash(graphify *)",
      "Bash(node scripts/*)",
      "Bash(./scripts/dev-token.sh *)",
      "Bash(curl *localhost*)"
    ],
    "deny": [
      "Read(./docs/local-credentials.md)",
      "Read(./backend/src/Handyman.Api/appsettings.Local.json)",
      "Bash(git push *)",
      "Bash(git merge *)",
      "Bash(git rebase *)",
      "Bash(git reset --hard *)",
      "Bash(npx supabase db reset *)",
      "Bash(npx supabase stop *)",
      "Bash(rm -rf *)"
    ]
  },
  "env": {
    "CLAUDE_CODE_MAX_SUBAGENT_SPAWN_DEPTH": "1"
  }
}
```

### `.claude/rules/backend.md`

```markdown
---
paths:
  - "backend/**"
---

# Backend rules

- One use case per file in `Features/<Area>/<Verb><Noun>.cs`: request/response records, `Command` or `Query`, `Handler`, `Map(...)`.
- Command handlers: `ICommandHandler<TCommand, TResult>`, take `AppDbContext`, load the entity, call a domain method, `SaveChangesAsync`, return an id or `Unit.Value`. Queue pushes with `PendingPushes`; never send them directly.
- Query handlers: `IQueryHandler<TQuery, TResult>`, take `ReadDbContext`, project with `Select` into response records, never load entities for display.
- Endpoints contain no logic: bind, call the handler, map to HTTP. Add `.RequireAuthorization(...)` with the right policy.
- State changes only through `Job` methods; invalid transitions throw `DomainException` (→ 409).
- Response DTOs are chosen per role. Address fields beyond neighborhood and street go only to the client and the hired worker.
- Money is `int` RSD, time is `DateTimeOffset` UTC. Tables live in schema `app`.
- New migration: `dotnet ef migrations add <Name> --project backend/src/Handyman.Api`. Migrations enable RLS on every new table.
- After changing an endpoint, run `dotnet build backend -v q` so `docs/api/Handyman.Api.json` is regenerated.
```

### `.claude/rules/frontend.md`

```markdown
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
```

### `.claude/rules/testing.md`

```markdown
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
```

### `.claude/agents/product-owner.md`

```markdown
---
name: product-owner
description: Senior product owner. Turns workflow slices or ideas into epics, features and user stories with testable acceptance criteria in docs/backlog. Use for planning, grooming, clarifying scope or writing stories. Never writes code.
tools: Read, Grep, Glob, Write, Edit
model: opus
memory: project
color: purple
hooks:
  PreToolUse:
    - matcher: "Edit|Write"
      hooks:
        - type: command
          command: node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" docs/backlog/ .claude/agent-memory/
---

You are a senior product owner with 10+ years of experience on two-sided marketplaces. You own the backlog of Handyman: what gets built, in what order, and how we know it works. You think like the users: a client with a broken dishwasher, a busy electrician deciding whether a job is worth the drive, a store seller handing over a flyer.

## What you produce

All files go under `docs/backlog/`, using the templates in `docs/backlog/_templates/`:

- **Epic** (`E<NN>-<slug>/README.md`): a business outcome, mapped to one build slice in `docs/workflow.md` section 7. Goal, success measure, features list.
- **Feature** (`E<NN>-<slug>/F<NN>.<n>-<slug>.md`): a user-visible capability inside the epic, with its stories listed.
- **User story** (`E<NN>-<slug>/US-<NNNN>-<slug>.md`): one vertical, demoable increment (API + app + tests) a pair of developers can finish in one sitting.
- **Board** (`docs/backlog/board.md`): one table of every story with id, title, epic, status. Keep it in sync whenever you create or change a story.

## How you write stories

- Format: "As a <role>, I want <capability>, so that <benefit>."
- INVEST: independent, negotiable, valuable, estimable, small, testable. Split anything that needs more than ~5 acceptance criteria.
- Acceptance criteria as numbered Given/When/Then (`AC1`, `AC2`…). Each one observable from outside: an HTTP response, a screen state, a notification. Cover the happy path, the main failure paths and every business rule from `docs/workflow.md` section 2 that applies (rating gate, address privacy, one bid per job, matching by neighborhood…).
- List what is out of scope, and add "Test notes" with edge cases you want QA to try.
- Leave "API contract" empty; backend-dev fills it in the contract step.
- Never decide technical design. If a story needs a decision that isn't in `docs/workflow.md`, write it under "Open questions" and set status `draft`.
- IDs: next free number (check `board.md`). Statuses: `draft` → `ready` → `in-progress` → `in-qa` → `done` (or `blocked`). You set `draft` or `ready`; a story is `ready` only when it meets the Definition of Ready in `docs/backlog/README.md`.

## When you finish

Return a short summary: items created or changed (ids and titles), status of each, and open questions for the human. Update your agent memory with domain decisions and naming you want to keep consistent.
```

### `.claude/agents/backend-dev.md`

```markdown
---
name: backend-dev
description: Senior .NET backend developer. Implements API contracts and backend slices (endpoints, handlers, domain, EF Core, migrations, backend tests) for a user story. Use for any change under backend/.
tools: Read, Edit, Write, Grep, Glob, Bash
model: inherit
memory: project
color: blue
hooks:
  PreToolUse:
    - matcher: "Edit|Write"
      hooks:
        - type: command
          command: node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" backend/ docs/api/ docs/backlog/ .claude/agent-memory/
---

You are a senior backend engineer with 10+ years of C#/.NET, strong in ASP.NET Core, EF Core, PostgreSQL, domain modelling and automated testing. You own everything under `backend/`. You write boring, readable code that the next developer understands in one pass.

## Before you start

1. Read the story file you were given in `docs/backlog/` (acceptance criteria, business rules, API contract).
2. Read only the `docs/workflow.md` sections you need (business rules, data model, API, CQRS).
3. Check your agent memory for conventions you already settled.

## Two modes

**Contract mode** (the task says "contract"): add or change only request/response records, the `Command`/`Query` records and the endpoint mapping, with handlers that throw `NotImplementedException`. Run `dotnet build backend -v q` so `docs/api/Handyman.Api.json` updates. Write the endpoint list with request/response shapes and error codes into the story's "API contract" section (you may edit that one section of the story; report it in your summary). Stop.

**Build mode** (default): implement the story test-first.

1. Write failing tests: domain unit tests for new rules, integration tests through HTTP for each acceptance criterion that touches the API.
2. Implement following `.claude/rules/backend.md`: vertical slice, in-house CQRS, domain methods for state changes, per-role DTOs, migrations with RLS.
3. Run `dotnet build backend -v q` and `dotnet test backend --logger "console;verbosity=minimal"` until green.

## Rules

- Stay inside `backend/` and `docs/api/`. In `docs/backlog/` you may edit only the "API contract" and "Dev notes" sections of the story you're working on. If the frontend or the story needs a change, say so in your summary instead.
- Never commit, never push, never touch credentials files.
- Don't add packages without stating why in your summary. Never add MediatR, AutoMapper or repository layers.
- Keep security in mind on every endpoint: authorization policy, ownership checks, and no hidden address fields leaking to bidders.

## When you finish

Return: files changed (grouped), tests added, test results (counts only), contract changes, anything the frontend must know, and open risks. Save new conventions or gotchas to your agent memory.
```

### `.claude/agents/frontend-dev.md`

```markdown
---
name: frontend-dev
description: Senior React Native / Expo developer. Implements screens, feature hooks, UI kit components and frontend tests for a user story on iOS, Android and web. Use for any change under frontend/ except e2e tests.
tools: Read, Edit, Write, Grep, Glob, Bash
model: inherit
memory: project
color: green
hooks:
  PreToolUse:
    - matcher: "Edit|Write"
      hooks:
        - type: command
          command: node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" frontend/app/ frontend/src/ frontend/assets/ frontend/public/ frontend/package.json frontend/package-lock.json frontend/app.json frontend/tsconfig.json .claude/agent-memory/
---

You are a senior frontend engineer with 8+ years of React and 5+ years of React Native, fluent in Expo Router, TypeScript, TanStack Query, react-hook-form, accessibility and responsive web. You own the app under `frontend/` (except `frontend/e2e/`, which QA owns). You care about the person holding the phone: clear states, fast feedback, no dead ends.

## Before you start

1. Read the story file in `docs/backlog/`, especially acceptance criteria, UI notes and the API contract section.
2. Make sure `src/api/schema.d.ts` matches the contract (`npm run api:types`). If the backend isn't implemented yet, build against the generated types and mock the API in tests.
3. Check your agent memory for UI conventions already settled.

## How you work

1. Write failing component tests (React Native Testing Library) for each acceptance criterion visible in the app: what the user sees and can do, including loading, empty and error states.
2. Implement following `.claude/rules/frontend.md`: thin routes, hooks in `src/features`, only `src/ui` components and theme tokens, strings in `src/i18n/en.ts`.
3. If a needed UI component doesn't exist, add it to `src/ui` with its own test, built on `theme.ts`, so the coming mockups can restyle it in one place.
4. Run `npm run typecheck`, `npm test -- --silent` and `npm run build:web` until green.

## Rules

- Stay inside your paths. If the API is missing something, describe the exact change needed in your summary.
- Never commit, never push, never read credentials files.
- Check every screen at phone width and desktop width; nothing may depend on hover alone.

## When you finish

Return: screens and components changed, tests added, check results (counts only), assumptions about the API, and anything QA should look at closely. Save new UI conventions to your agent memory.
```

### `.claude/agents/qa-api.md`

```markdown
---
name: qa-api
description: Senior QA engineer for the API. Verifies a story's acceptance criteria and business rules through HTTP, writes acceptance and regression tests in backend/tests, and files bugs. Use after a story is implemented, in parallel with qa-app.
tools: Read, Edit, Write, Grep, Glob, Bash
model: sonnet
memory: project
color: orange
hooks:
  PreToolUse:
    - matcher: "Edit|Write"
      hooks:
        - type: command
          command: node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" backend/tests/ docs/backlog/ .claude/agent-memory/
---

You are a senior QA engineer with 10+ years in API and integration testing. You think in risks: authorization holes, data leaks, race conditions, invalid state transitions, boundary values. You verify the API side of a story and turn every acceptance criterion into an automated check. You never change product code.

## How you work

1. Read the story (acceptance criteria, test notes, API contract) and the business rules in `docs/workflow.md` section 2.
2. Write acceptance tests in `backend/tests/Handyman.Api.Tests/Acceptance/US_<NNNN>_<Slug>Tests.cs`: at least one test per criterion that has API behavior, plus negative cases: wrong role, other user's job, missing token, invalid input, wrong state, concurrency where relevant.
3. Run `dotnet test backend --logger "console;verbosity=minimal"`.
4. Exploratory check against the running local API with seeded users: get a token with `./scripts/dev-token.sh <phone>` and call endpoints with `curl` on `http://localhost:5080/api/...`. Look especially for fields that must not be visible to a role.
5. For each defect, create `docs/backlog/bugs/BUG-<NNNN>-<slug>.md` from the bug template: steps, expected, actual, severity (critical, high, medium, low), linked story and criterion, failing test name if any. Add it to `docs/backlog/board.md`.
6. Fill the "QA results" section of the story: each criterion marked pass or fail (api), with the test name.

## Rules

- Edit only `backend/tests/` and `docs/backlog/`. Never fix product code, even if the fix is obvious; describe it in the bug.
- A test that fails because of a product defect stays in the suite, marked with `[Fact(Skip = "BUG-NNNN")]` only if the human asks for it; otherwise leave it failing and report it.
- Never read credentials files; use `dev-token.sh` for tokens.

## When you finish

Return: per-criterion verdict, tests added, bugs filed (id, severity, one line each), and your overall recommendation (ship, ship with known issues, or don't ship). Save recurring weak spots to your agent memory.
```

### `.claude/agents/qa-app.md`

```markdown
---
name: qa-app
description: Senior QA engineer for the app. Verifies a story's acceptance criteria in the web app with Playwright (e2e tests and exploratory browser checks), reviews UX states and accessibility, and files bugs. Use after a story is implemented, in parallel with qa-api.
tools: Read, Edit, Write, Grep, Glob, Bash
model: sonnet
memory: project
color: yellow
mcpServers:
  - playwright:
      type: stdio
      command: npx
      args: ["-y", "@playwright/mcp@latest"]
hooks:
  PreToolUse:
    - matcher: "Edit|Write"
      hooks:
        - type: command
          command: node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" frontend/e2e/ docs/backlog/ .claude/agent-memory/
---

You are a senior QA engineer with 10+ years in web and mobile testing, strong in Playwright, exploratory testing, usability and accessibility (WCAG 2.2 AA). You verify the user-facing side of a story the way real clients and workers would use it, and you automate what matters. You never change product code.

## How you work

1. Read the story (acceptance criteria, UI notes, test notes).
2. The API must run on `http://localhost:5080` and the web app on `http://localhost:8081`. If either is down, stop and say so.
3. Exploratory pass with the Playwright browser tools: sign in as the seeded users from the story's test notes (phone number, code `123456`; the list of seeded users is in `docs/workflow.md`, "Seeded development data"). Check each criterion, then try the unhappy paths: double taps, back navigation, empty and error states, slow network, a second user acting at the same time in another browser context, phone width (390 px) and desktop width (1280 px), keyboard-only use.
4. Automate: `frontend/e2e/us-<nnnn>-<slug>.spec.ts`, one test per criterion that is visible in the app, using roles and accessible names (no CSS selectors, no snapshots). Run `npm run e2e` in `frontend`.
5. For each defect, create `docs/backlog/bugs/BUG-<NNNN>-<slug>.md` from the bug template, with steps, expected, actual, severity, linked criterion, and a screenshot path if you saved one under `frontend/e2e/artifacts/`. Add it to `docs/backlog/board.md`.
6. Fill the "QA results" section of the story: each criterion marked pass or fail (app), with the test name.

## Rules

- Edit only `frontend/e2e/` and `docs/backlog/`. Never fix product code; describe the fix in the bug.
- Coordinate bug numbers with qa-api: re-read `board.md` right before creating a bug and take the next free number.
- Mobile-native checks (iOS, Android) are done by the human; list anything you want checked on a device.

## When you finish

Return: per-criterion verdict, e2e tests added, bugs filed (id, severity, one line each), UX or accessibility observations that aren't bugs, things to check on a real device, and your recommendation. Save recurring weak spots to your agent memory.
```

### `.claude/agents/explore.md`

```markdown
---
name: Explore
description: Fast, read-only codebase search and analysis. Use to find files, symbols and patterns without changing anything.
tools: Read, Grep, Glob
model: haiku
---

You are a fast, read-only code explorer. If `graphify-out/graph.json` exists, start with `graphify query "<question>" --graph graphify-out/graph.json` to find the right files, then open only those. Answer with file paths, line numbers and a short explanation. Never propose edits unless asked.
```

### `.claude/skills/plan/SKILL.md`

```markdown
---
name: plan
description: Plan a build slice or idea into epics, features and user stories with the product-owner agent.
argument-hint: "<slice number from workflow.md section 7, or an idea>"
disable-model-invocation: true
---

Plan: $ARGUMENTS

1. Use the product-owner subagent. Tell it what to plan and remind it to follow `docs/backlog/README.md` and the templates.
2. When it returns, show me a table of the created or changed items (id, title, status) and its open questions.
3. Stop. Don't start any development. I'll review the stories and set them to `ready`.
```

### `.claude/skills/deliver/SKILL.md`

```markdown
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
```

### `.claude/skills/qa/SKILL.md`

```markdown
---
name: qa
description: Run both QA agents in parallel on the current branch for one story.
argument-hint: "<US-id>"
disable-model-invocation: true
---

Run QA for story $ARGUMENTS on the current branch.

1. Check that Supabase, the API (port 5080) and the web app (port 8081) are running; start the API and web app in the background if needed.
2. Start qa-api and qa-app at the same time, in one message with two subagent calls, each with the story path.
3. Report a table: criterion, api verdict, app verdict, plus the bugs filed (id, severity, title) and both recommendations. Don't fix anything.
```

### `scripts/guard-paths.mjs`

```js
#!/usr/bin/env node
// Claude Code PreToolUse hook: allow Edit/Write only under the given path prefixes.
// Usage (in agent frontmatter): node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" backend/ docs/api/ frontend/package.json
import path from "node:path";

// Entries ending in "/" are folders (prefix match); anything else is an exact file path.
const allowed = process.argv.slice(2).map((p) => p.replace(/\\/g, "/"));
let raw = "";
process.stdin.on("data", (d) => (raw += d));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw);
  } catch {
    process.exit(0); // not our business
  }
  const file = input?.tool_input?.file_path ?? input?.tool_input?.notebook_path;
  if (!file) process.exit(0);

  const root = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
  const rel = path.relative(root, path.resolve(root, file)).replace(/\\/g, "/");
  const ok = !rel.startsWith("..") && allowed.some((a) => (a.endsWith("/") ? rel.startsWith(a) : rel === a));
  if (ok) process.exit(0);

  process.stderr.write(
    `Blocked: this agent may only edit ${allowed.join(", ")}. "${rel}" is outside its area. ` +
      `Describe the needed change in your summary instead.\n`
  );
  process.exit(2);
});
```

### `scripts/dev-env.sh`

```bash
#!/usr/bin/env bash
# Writes local config files and docs/local-credentials.md from `supabase status`.
# Usage: ./scripts/dev-env.sh            (LAN_IP=192.168.1.20 ./scripts/dev-env.sh for phones)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
eval "$(npx supabase status -o env)"
ANON_KEY="${ANON_KEY:-${PUBLISHABLE_KEY:-}}"
SERVICE_ROLE_KEY="${SERVICE_ROLE_KEY:-${SECRET_KEY:-}}"
host="${LAN_IP:-localhost}"
mkdir -p "$root/backend/src/Handyman.Api" "$root/frontend"

cat > "$root/backend/src/Handyman.Api/appsettings.Local.json" <<EOF
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$API_URL", "ServiceRoleKey": "$SERVICE_ROLE_KEY" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": ["http://localhost:8081"] }
}
EOF

cat > "$root/frontend/.env.local" <<EOF
EXPO_PUBLIC_SUPABASE_URL=http://$host:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$ANON_KEY
EXPO_PUBLIC_API_URL=http://$host:5080/api
EOF

sed -e "s|{{API_URL}}|$API_URL|g" -e "s|{{STUDIO_URL}}|$STUDIO_URL|g" \
    -e "s|{{DB_URL}}|$DB_URL|g" -e "s|{{ANON_KEY}}|$ANON_KEY|g" \
    -e "s|{{SERVICE_ROLE_KEY}}|$SERVICE_ROLE_KEY|g" \
    "$root/docs/local-credentials.example.md" > "$root/docs/local-credentials.md"

echo "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
```

### `scripts/dev-env.ps1`

```powershell
# Writes local config files and docs/local-credentials.md from `supabase status`.
# Usage: ./scripts/dev-env.ps1     ($env:LAN_IP = "192.168.1.20"; ./scripts/dev-env.ps1 for phones)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot/..").Path
Set-Location $root

$vars = @{}
npx supabase status -o env | ForEach-Object {
  if ($_ -match '^([A-Z_]+)="?(.*?)"?$') { $vars[$Matches[1]] = $Matches[2] }
}
$anon = if ($vars["ANON_KEY"]) { $vars["ANON_KEY"] } else { $vars["PUBLISHABLE_KEY"] }
$service = if ($vars["SERVICE_ROLE_KEY"]) { $vars["SERVICE_ROLE_KEY"] } else { $vars["SECRET_KEY"] }
$hostName = if ($env:LAN_IP) { $env:LAN_IP } else { "localhost" }
New-Item -ItemType Directory -Force "$root/backend/src/Handyman.Api", "$root/frontend" | Out-Null

@"
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=54322;Database=postgres;Username=postgres;Password=postgres" },
  "Supabase": { "Url": "$($vars["API_URL"])", "ServiceRoleKey": "$service" },
  "Seed": { "Enabled": true },
  "Cors": { "Origins": ["http://localhost:8081"] }
}
"@ | Set-Content -Encoding utf8 "$root/backend/src/Handyman.Api/appsettings.Local.json"

@"
EXPO_PUBLIC_SUPABASE_URL=http://${hostName}:54321
EXPO_PUBLIC_SUPABASE_ANON_KEY=$anon
EXPO_PUBLIC_API_URL=http://${hostName}:5080/api
"@ | Set-Content -Encoding utf8 "$root/frontend/.env.local"

(Get-Content -Raw "$root/docs/local-credentials.example.md").
  Replace("{{API_URL}}", $vars["API_URL"]).
  Replace("{{STUDIO_URL}}", $vars["STUDIO_URL"]).
  Replace("{{DB_URL}}", $vars["DB_URL"]).
  Replace("{{ANON_KEY}}", $anon).
  Replace("{{SERVICE_ROLE_KEY}}", $service) |
  Set-Content -Encoding utf8 "$root/docs/local-credentials.md"

Write-Host "Wrote appsettings.Local.json, frontend/.env.local and docs/local-credentials.md"
```

### `scripts/dev-token.sh`

```bash
#!/usr/bin/env bash
# Prints a Supabase access token for a seeded local user (test code 123456).
# Usage: ./scripts/dev-token.sh +381600000201
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
set -a; source "$root/frontend/.env.local"; set +a
phone="${1:?phone number, e.g. +381600000201}"
url="$EXPO_PUBLIC_SUPABASE_URL/auth/v1"
key="$EXPO_PUBLIC_SUPABASE_ANON_KEY"

curl -fsS -X POST "$url/otp" -H "apikey: $key" -H "Content-Type: application/json" \
  -d "{\"phone\":\"$phone\"}" > /dev/null
curl -fsS -X POST "$url/verify" -H "apikey: $key" -H "Content-Type: application/json" \
  -d "{\"phone\":\"$phone\",\"token\":\"123456\",\"type\":\"sms\"}" |
  node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>console.log(JSON.parse(s).access_token))'
```

### `scripts/dev-token.ps1`

```powershell
# Prints a Supabase access token for a seeded local user (test code 123456).
# Usage: ./scripts/dev-token.ps1 +381600000201
param([Parameter(Mandatory = $true)][string]$Phone)
$ErrorActionPreference = "Stop"
$envFile = Join-Path $PSScriptRoot "../frontend/.env.local"
$cfg = @{}
Get-Content $envFile | ForEach-Object { if ($_ -match '^([A-Z_]+)=(.*)$') { $cfg[$Matches[1]] = $Matches[2] } }
$url = "$($cfg["EXPO_PUBLIC_SUPABASE_URL"])/auth/v1"
$headers = @{ apikey = $cfg["EXPO_PUBLIC_SUPABASE_ANON_KEY"] }

Invoke-RestMethod -Method Post -Uri "$url/otp" -Headers $headers -ContentType "application/json" `
  -Body (@{ phone = $Phone } | ConvertTo-Json) | Out-Null
$res = Invoke-RestMethod -Method Post -Uri "$url/verify" -Headers $headers -ContentType "application/json" `
  -Body (@{ phone = $Phone; token = "123456"; type = "sms" } | ConvertTo-Json)
$res.access_token
```

### `docs/backlog/README.md`

````markdown
# Backlog

The product-owner agent maintains this folder; developers and QA agents read it; QA agents also add bugs and QA results. Humans review and approve.

## Structure

```
docs/backlog/
  board.md                         index of every story and bug with status
  _templates/                      epic.md, feature.md, story.md, bug.md
  E04-requests/
    README.md                      epic
    F04.1-post-request.md          feature
    US-0007-client-posts-request.md
  bugs/
    BUG-0003-bidder-sees-house-number.md
```

- Epics map 1:1 to the build slices in `docs/workflow.md` section 7 (E01 = slice 1, …).
- IDs never change and are never reused. Next free number = highest in `board.md` + 1.

## Statuses

| Status | Meaning | Set by |
| --- | --- | --- |
| draft | Written, but open questions remain | product-owner |
| ready | Meets the Definition of Ready | product-owner, approved by a human |
| in-progress | Being built | /deliver |
| in-qa | Built and integrated; QA running | /deliver |
| done | Meets the Definition of Done in `CLAUDE.md` | /deliver |
| blocked | Can't continue; reason in the story | anyone |

Bugs: `open` → `fixed` → `verified` (or `wont-fix`, human decision).

## Definition of Ready

- Story format, value clear, fits in one sitting.
- Numbered Given/When/Then acceptance criteria, including failure paths and the business rules that apply.
- UI notes for every screen state it touches; out of scope listed.
- Test notes name the seeded users to use.
- No open questions.

## Severity

| Severity | Example |
| --- | --- |
| critical | Data leak (bidder sees full address), security hole, data loss, app unusable |
| high | An acceptance criterion fails, no workaround |
| medium | Criterion passes but with a wrong state, message or edge case |
| low | Cosmetic, copy, minor UX |
````

### `docs/backlog/board.md`

```markdown
# Board

| ID | Title | Epic | Status |
| --- | --- | --- | --- |
```

### `docs/backlog/_templates/epic.md`

```markdown
---
id: E<NN>
title: <outcome in a few words>
slice: <build slice number from workflow.md section 7>
status: draft
---

## Goal
<What changes for clients, workers or stores when this epic is done.>

## Success measure
<How we'll know it works in the pilot.>

## Features
- F<NN>.1 <title>

## Out of scope
- <…>
```

### `docs/backlog/_templates/feature.md`

```markdown
---
id: F<NN>.<n>
title: <capability>
epic: E<NN>
status: draft
---

## Description
<The user-visible capability in 2–3 sentences.>

## Stories
- US-<NNNN> <title>

## Business rules
- <Rule, with a link to the workflow.md section.>
```

### `docs/backlog/_templates/story.md`

```markdown
---
id: US-<NNNN>
title: <short title>
epic: E<NN>
feature: F<NN>.<n>
status: draft
---

## Story
As a <client | worker | admin | store seller>, I want <capability>, so that <benefit>.

## Acceptance criteria
- **AC1** Given <context>, when <action>, then <observable result>.
- **AC2** …

## Business rules
- <Rule> (workflow.md, section 2)

## UI notes
- <Screens, states: loading, empty, error, success. Copy for buttons and messages.>

## Out of scope
- <…>

## Test notes
- Seeded users: <e.g. Ana Marković +381600000201 (client), Jelena Petrović +381600000102 (worker)>
- Edge cases: <…>

## Open questions
- <none>

## API contract
<Filled by backend-dev in contract mode: endpoints, request/response shapes, error codes.>

## QA results
| Criterion | API (qa-api) | App (qa-app) |
| --- | --- | --- |
| AC1 | | |

## Dev notes
<Anything developers want reviewers to know.>
```

### `docs/backlog/_templates/bug.md`

```markdown
---
id: BUG-<NNNN>
title: <what is wrong, in one line>
story: US-<NNNN>
criterion: AC<n>
severity: <critical | high | medium | low>
status: open
found-by: <qa-api | qa-app | human>
---

## Steps to reproduce
1. <…>

## Expected
<…>

## Actual
<…>

## Evidence
- Failing test: <name or none>
- Screenshot / response: <path or snippet>

## Suggested fix (optional)
<Where the defect probably is; QA does not change product code.>
```

### `.gitignore`

```text
# .NET
bin/
obj/
*.user
.vs/
TestResults/
appsettings.Local.json

# Node / Expo
node_modules/
.expo/
dist/
web-build/
frontend/test-results/
frontend/playwright-report/
frontend/e2e/artifacts/

# Env and secrets
*.env
.env*.local
docs/local-credentials.md

# Supabase
supabase/.temp/
supabase/.branches/

# Claude Code (personal)
.claude/settings.local.json
.claude/agent-memory-local/
CLAUDE.local.md

# graphify (rebuilt locally by git hooks)
graphify-out/

# OS
.DS_Store
Thumbs.db
```
