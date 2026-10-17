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
