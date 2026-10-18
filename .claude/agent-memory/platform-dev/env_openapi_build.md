---
name: env-openapi-build
description: OpenAPI build-time generation quirks (file name dots, Microsoft.OpenApi version pin, EF version alignment in tests)
metadata:
  type: project
---

- Microsoft.Extensions.ApiDescription.Server rejects dots in file names, so the csproj writes `v1.json` and a `PublishOpenApiContract` target renames it to `docs/api/Handyman.Api.json`.
- `Microsoft.OpenApi` is pinned to 2.12.2 in Handyman.Api.csproj (tool needs it; default 2.0.0 is vulnerable and breaks the generator; 3.x breaks source generators).
- Test project references EF Core + Relational 10.0.13 explicitly to avoid Testcontainers pulling 10.0.4.
- `python` on this Windows box is the Store stub; use sed/node instead.
