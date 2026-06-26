# Backend conventions (`monkify-backend`, .NET 8)

> Status: **stub** — to be fully drafted. Captures "how we write backend code here".

## Patterns observed (fill in / verify)

- **EF mapping is INLINE** in `MonkifyDbContext.OnModelCreating` — no `IEntityTypeConfiguration`
  classes. Add new entity config as a `modelBuilder.Entity<T>(builder => { … })` block in the
  existing style.
- **Migrations** live in `Monkify.Infrastructure/Migrations/` and are applied **only** at
  `Monkify.Consumers` startup via `Database.Migrate()`. Generate with `dotnet ef migrations add`;
  never hand-edit the snapshot.
- **CQRS-ish via MediatR:** each use case is a `Request` + `Handler` pair under
  `Monkify.Infrastructure/Handlers/<Area>/<UseCase>/`. Handlers extend `BaseRequestHandler`.
- **Controllers are thin:** extend `BaseController`, build a MediatR request, return
  `ProcessRequest(...)` (wraps `ApiResult<T>` = `{ data, messages }`). Validation failures via
  `IMessaging.ReturnValidationFailureMessage(...)`. Operator-scoped session endpoints bind
  `[FromHeader(Name = "x-operator-id")]` and reject missing values with HTTP 400 before MediatR.
- **Async messaging via MassTransit/RabbitMQ:** consumers in `Monkify.Infrastructure/Consumers/…`
  (one receive endpoint each), registered in `Monkify.Consumers/Program.cs`. Publish via
  `IRabbitMQService`.
- **Caching via `ICacheService`** (Redis or in-memory). Keys built by `CacheHelper`; active-params
  cache is operator-aware.
- **Domain stays pure:** entities/value-objects/domain-services in `Monkify.Domain` have no
  EF/infra dependencies. All entities extend `TableEntity`.
- **Solana payments** via `ISolanaService` / `SolanaService` — validate signatures, transfer
  rewards, refund invalid bets.
- **Tests:** `Monkify.Tests` (xUnit + Shouldly + Moq + Bogus, in-memory EF). Update + add + report
  changed tests on every behavior change (see `constraints.md`).
- **Application settings** live in **`Monkify.Common/monkifysettings.json`** — the single source
  for backend configuration. Both `Monkify.Api` and `Monkify.Consumers` load it via
  `AddMonkifySettings()` (environment variables can override keys at runtime). Do **not** add
  `appsettings.json` / `appsettings.Development.json` overlays for app settings; put new keys in
  `monkifysettings.json` under the appropriate `GeneralSettings` section.

<!-- TODO: confirm naming conventions, file/folder rules, error-message/resource usage, logging
     (Serilog) patterns, and add concrete examples. -->
