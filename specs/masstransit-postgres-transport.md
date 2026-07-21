# SpaceStroker — "Replace RabbitMQ with MassTransit Postgres SQL transport" — Implementation Spec

> **Status: Shipped** — merged via `feature/masstransit-postgres-transport` on 2026-07-16
> (`monkify-backend` + `monkify-meta`). Distilled summary: `../features/masstransit-postgres-transport.md`.
> 363 unit + 35 integration tests green. This spec is retained as the deep design archive.
>
> **Status: Ready for implementation** — refined via Trello card
> [Replace RabbitMQ with MassTransit Postgres SQL transport](https://trello.com/c/nPCH4e5R) (2026-07-16).
> Repos affected: `monkify-backend` and this doc in `monkify-meta`. **No `monkify-front` change.**
>
> **Purpose:** Move the game state-machine's MassTransit message transport off **RabbitMQ** onto
> Postgres via the **MassTransit SQL transport** (8.3+), so the app can deploy to GCP (which has no
> managed RabbitMQ) with **no broker VM** — one fewer always-on attack surface and one fewer fixed
> cost for a crypto app. Event contracts and consumer handler logic are unchanged; this is a
> transport-registration + provisioning swap. The card also folds in a config-only rename of the game
> database **`monkify → spacestroker`** (plus cosmetic docker infra names) so all storage lines up with
> the SpaceStroker brand before #61's terraform hardcodes names.

---

## 1. Summary

Today MassTransit runs over RabbitMQ: the **API** registers a publish-only bus (`UsingRabbitMq`), and
**Consumers** registers 8 consumers with explicitly-named receive endpoints. We swap both to the
**Postgres SQL transport** (`UsingPostgres`), keep the 8 named endpoints, and let **Consumers** create
the transport's dedicated database + schema at startup. The publish abstraction is renamed
transport-neutral. Local docker-compose drops the `rabbitmq` service and points the transport at the
existing Postgres. The integration harness drops the RabbitMQ Testcontainer and runs the full game
flow over Postgres.

| | Before | After |
| --- | --- | --- |
| Transport pkg | `MassTransit.RabbitMQ` | `MassTransit.SqlTransport.PostgreSQL` |
| Bus registration | `UsingRabbitMq(cfg.Host(conn))` | `UsingPostgres` (reads `SqlTransportOptions` from DI) |
| Broker | RabbitMQ container / VM | none — Postgres `spacestroker_messaging` DB |
| Messaging store | AMQP queues | tables in DB `spacestroker_messaging` (schema `transport`), **same Postgres instance** as the game DB |
| Schema owner | RabbitMQ auto-declares | MassTransit **Postgres migration hosted service** in `SpaceStroker.Consumers` at startup |
| Game DB name | `monkify` | `spacestroker` |
| New secret | — | `ConnectionStrings:Messaging` |
| Delivery | at-least-once (AMQP) | at-least-once (SQL: LISTEN/NOTIFY + polling, SKIP-LOCKED competing consumers) |

**No change** to: event contracts, consumer handler logic, the game lifecycle, Hangfire (stays on the
**game** DB), Redis, or the SignalR backplane.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/masstransit-postgres-transport/<repo>`; the shared
  checkout stays on `main`. Branch `feature/masstransit-postgres-transport` in each affected repo (the
  merge/PR ref), pushed to `origin`.
- **Unit tests:** update/add in `SpaceStroker.Tests`; run via `scripts/run_tests.bat` (never the full
  `dotnet test` — `ProductionSessions` is a slow Monte Carlo suite); **report changed tests** at the
  end of implementation.
- **Integration test mandatory (message-flow change):** the Testcontainers `SpaceStroker.Tests.Integration`
  suite must exercise the game happy path **over the Postgres transport**; run via
  `scripts/run_integration_tests.bat`. See §11.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`. **This
  card adds no EF entity/migration** — the transport schema is MassTransit-owned, not in the DbContext.
- **Migrations owned by `SpaceStroker.Consumers`;** the **API stays stateless** — it registers the bus as
  **publisher-only** and does **not** provision the transport schema.
- **Secrets/config:** only mock/placeholder values in committed files (e.g. `spacestrokersettings.json`).
- **Out-of-scope domain rename:** `app.monkify.net` / token symbol stay untouched (owned by #62/#63 +
  ops) — this card renames only the **database identifier** and cosmetic docker infra.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Transport:** MassTransit **8.5.4** Postgres SQL transport. Replace `MassTransit.RabbitMQ` with **`MassTransit.SqlTransport.PostgreSQL`** (8.5.4) in `SpaceStroker.Api`, `SpaceStroker.Infrastructure`, `SpaceStroker.Consumers`. `SpaceStroker.Common` keeps core `MassTransit` only. |
| 2 | **Messaging store:** a **separate database `spacestroker_messaging`** on the **same** Postgres/Cloud SQL instance as the game DB. Transport objects live under schema **`transport`**. |
| 3 | **Provisioning:** `SpaceStroker.Consumers` runs MassTransit's **Postgres migration hosted service** at startup to create the messaging DB + schema. The **API never provisions** (publisher-only). |
| 4 | **Credentials:** **reuse the game-DB app user** (same host/user/password; only `Database=` differs). That user therefore needs **CREATE DATABASE** on the instance so MT can create `spacestroker_messaging` (a #61 grant requirement). |
| 5 | **Config shape:** add an **explicit `ConnectionStrings:Messaging`** connection string; translate it into `SqlTransportOptions` via `NpgsqlConnectionStringBuilder`. Independently rotatable; becomes a new secret for #61. |
| 6 | **Endpoints:** **keep all 8 explicitly-named `ReceiveEndpoint`s** + `ConfigureEndpoints` in Consumers (smallest behavioral diff). Clean cutover — no in-flight RabbitMQ messages to preserve. |
| 7 | **Publisher rename:** `IRabbitMQService`/`RabbitMQService` → **`IMessageBus`/`MessageBusPublisher`**; folder + namespace `Services/RabbitMQ` → `Services/Messaging`; fix the "published to RabbitMQ" log text. Mechanical across all call sites; still wraps `IPublishEndpoint.Publish`. |
| 8 | **DB rename `monkify → spacestroker`:** config-only (EF `Database.Migrate()` creates the fresh `spacestroker` DB at Consumers startup — greenfield dev). Also rename cosmetic infra (`monkify-network` → `spacestroker-network` + the `setup_local_environment.bat` network reference). |
| 9 | **Delivery semantics:** keep MassTransit defaults (LISTEN/NOTIFY + polling fallback; competing consumers via SKIP-LOCKED; `_error`/`_skipped` handling). Transport latency is negligible for gameplay (the flow already waits `MinimumWaitPeriodForBets` before `RunGame`). |
| 10 | **Idempotency:** re-confirm reward/refund idempotency (ledger-based in `TransactionLog`) holds under at-least-once redelivery. Known related risk documented in §6.5. |

---

## 4. Data model

**No EF entity or migration change.** The MassTransit SQL transport manages its own tables (queues,
topics, subscriptions, messages) under schema `transport` in the `spacestroker_messaging` database,
created by the MassTransit migration hosted service — **not** by `SpaceStrokerDbContext` and **not** under
`SpaceStroker.Infrastructure/Migrations/`.

The game database `spacestroker` (renamed from `monkify`) keeps its existing EF schema unchanged; the
rename is a connection-string identifier change only. `Database.Migrate()` at Consumers startup creates
the fresh `spacestroker` database and applies existing migrations (greenfield dev — no data preservation).

---

## 5. Configuration

### 5.1 `spacestrokersettings.json` (`SpaceStroker.Common`, committed — mock values only)

```jsonc
"ConnectionStrings": {
  "SpaceStroker":  "Host=localhost;Port=5432;Database=spacestroker;Username=postgres;Password=postgres",
  "Messaging": "Host=localhost;Port=5432;Database=spacestroker_messaging;Username=postgres;Password=postgres"
  // "RabbitMQ" line REMOVED
}
```

- `SpaceStroker`: `Database=monkify` → `Database=spacestroker`.
- `Messaging`: new; same host/user/password as `SpaceStroker`, only `Database=` differs.
- Remove the `RabbitMQ` (`amqp://…`) entry.

> Both hosts read `ConnectionStrings:Messaging` (via env override in containers/prod). The API needs it
> to publish; Consumers needs it to provision + consume.

### 5.2 `SqlTransportOptions` binding (shared)

Both hosts translate the `Messaging` connection string into `SqlTransportOptions`. Add a small shared
helper in `SpaceStroker.Infrastructure` (e.g. `MessagingRegistration.AddSpaceStrokerSqlTransportOptions`) so
the parse is not duplicated:

```csharp
public static IServiceCollection AddSpaceStrokerSqlTransportOptions(
    this IServiceCollection services, IConfiguration configuration)
{
    var b = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Messaging"));
    services.AddOptions<SqlTransportOptions>().Configure(o =>
    {
        o.Host     = b.Host;
        o.Port     = b.Port;
        o.Database = b.Database;      // spacestroker_messaging
        o.Schema   = "transport";
        o.Username = b.Username;
        o.Password = b.Password;
        // Reuse the game-DB user for admin (create DB/schema) too — it has CREATE DATABASE.
        // Confirm exact admin-credential knobs for MassTransit 8.5.4 (AdminUsername/AdminPassword or Role).
    });
    return services;
}
```

> **Implementation note (verify against MassTransit 8.5.4):** the SQL transport distinguishes an admin
> connection (create database/schema/role) from the runtime connection. With the single reused user we
> set both to the game-DB user. If 8.5.4 requires a distinct `Role`, create/own it with that same user.
> This is the one knob to confirm against the MassTransit docs; the **decision** (one reused credential,
> no dedicated role) is fixed.

---

## 6. Backend behaviour

### 6.1 API host — `SpaceStroker.Api/Program.cs` (publisher-only)

Replace (lines ~75–81):

```csharp
builder.Services.AddSpaceStrokerSqlTransportOptions(builder.Configuration);
builder.Services.AddMassTransit(x =>
{
    x.UsingPostgres(); // reads SqlTransportOptions from DI
});
```

- **No** consumers, **no** migration hosted service (API stays stateless — Decision 3).
- `IPublishEndpoint` is still provided by `AddMassTransit`, so `MessageBusPublisher` keeps working.

### 6.2 Consumers host — `SpaceStroker.Consumers/Program.cs`

Replace the `AddMassTransit` block (lines ~52–109):

```csharp
builder.Services.AddSpaceStrokerSqlTransportOptions(builder.Configuration);
builder.Services.AddPostgresMigrationHostedService(); // creates spacestroker_messaging DB + transport schema at startup

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<BetPlacedConsumer>();
    x.AddConsumer<RunGameConsumer>();
    x.AddConsumer<RewardGameConsumer>();
    x.AddConsumer<RefundInvalidBetConsumer>();
    x.AddConsumer<BetNotificationConsumer>();
    x.AddConsumer<ChatMessagePersistedConsumer>();
    x.AddConsumer<GameParametersCreatedConsumer>();
    x.AddConsumer<GameTypeDeactivatedConsumer>();

    x.UsingPostgres((context, cfg) =>
    {
        cfg.ReceiveEndpoint(nameof(BetPlacedConsumer),            e => e.ConfigureConsumer<BetPlacedConsumer>(context));
        cfg.ReceiveEndpoint(nameof(RunGameConsumer),             e => e.ConfigureConsumer<RunGameConsumer>(context));
        cfg.ReceiveEndpoint(nameof(RewardGameConsumer),          e => e.ConfigureConsumer<RewardGameConsumer>(context));
        cfg.ReceiveEndpoint(nameof(RefundInvalidBetConsumer),    e => e.ConfigureConsumer<RefundInvalidBetConsumer>(context));
        cfg.ReceiveEndpoint(nameof(BetNotificationConsumer),     e => e.ConfigureConsumer<BetNotificationConsumer>(context));
        cfg.ReceiveEndpoint(nameof(ChatMessagePersistedConsumer),e => e.ConfigureConsumer<ChatMessagePersistedConsumer>(context));
        cfg.ReceiveEndpoint(nameof(GameParametersCreatedConsumer),e => e.ConfigureConsumer<GameParametersCreatedConsumer>(context));
        cfg.ReceiveEndpoint(nameof(GameTypeDeactivatedConsumer), e => e.ConfigureConsumer<GameTypeDeactivatedConsumer>(context));
        cfg.ConfigureEndpoints(context);
    });
});
```

- Keep the 8 named endpoints (Decision 6).
- `AddPostgresMigrationHostedService` runs before the bus hosted service, so the schema exists before
  consumers/publishers connect. The existing `ApplyMigrations(app)` (EF `Database.Migrate()`, line ~164)
  is unchanged and creates the separate `spacestroker` game DB — the two provisioning steps target different
  databases, so their ordering is independent.

### 6.3 Publish abstraction rename (`SpaceStroker.Infrastructure`)

- Move `Services/RabbitMQ/` → `Services/Messaging/`; namespace `SpaceStroker.Infrastructure.Services.RabbitMQ`
  → `SpaceStroker.Infrastructure.Services.Messaging`.
- `IRabbitMQService` → `IMessageBus` (method `PublishEventAsync<T>` unchanged).
- `RabbitMQService` → `MessageBusPublisher` (still injects `IPublishEndpoint`; update the log line to
  "Event of type {eventType} published via MassTransit." — drop "RabbitMQ").
- `DependencyInjection.cs`: `services.AddScoped<IRabbitMQService, RabbitMQService>()` →
  `AddScoped<IMessageBus, MessageBusPublisher>()`; update the `using` (line ~30).
- Update all injection sites (constructor params/fields may keep local names but should be renamed for
  clarity; at minimum the **type** changes). Call sites (all resolve `IMessageBus`):
  - `Services/Games/GameService.cs` (ctor param + field)
  - `Hubs/ActiveGamesHub.cs` (ctor param + field)
  - `Consumers/RunGame/RunGameConsumer.cs`, `Consumers/BetPlaced/BetPlacedConsumer.cs`,
    `Consumers/GameTypeDeactivated/GameTypeDeactivatedConsumer.cs`,
    `Consumers/BetNotification/BetNotificationConsumer.cs` (scoped `GetRequiredService`)
  - `Handlers/Games/RegisterBet/RegisterBetHandler.cs`,
    `Handlers/Admin/CreateGameType/CreateGameTypeHandler.cs`,
    `Handlers/Admin/DeactivateGameType/DeactivateGameTypeHandler.cs`

### 6.4 Event flow (unchanged)

`BetPlacedEvent → BetPlacedConsumer → RunGameEvent → RunGameConsumer → RewardGameEvent →
RewardGameConsumer`, plus `RefundInvalidBetEvent`, `GameParametersCreatedEvent`,
`GameTypeDeactivatedEvent`, `BetNotificationEvent`, `ChatMessagePersistedEvent`. Only the wire changes.

### 6.5 Idempotency under at-least-once redelivery (verify)

The SQL transport is at-least-once (SKIP-LOCKED competing consumers; a crash between work and ack
redelivers). Reward/refund idempotency is ledger-based (`TransactionLog` `Credit`/`Refund` rows read by
`BetDomainService`) and should already cover redelivery. **Risk to watch:** the INDEX notes a
pre-existing *multi-winner reward double-credit race under redelivery* in `RewardGameConsumer` (surfaced
by game-type-stats, tracked separately). Switching to the SQL transport does **not** change that logic,
but the redelivery integration test (§11) may exercise it. **Scope:** if the test exposes that race,
**flag it and coordinate** — fixing it is the separately-tracked card's job, not this transport swap.
This card's bar is: single-consumer redelivery of `RewardGameEvent`/`RefundInvalidBetEvent` credits
exactly once.

---

## 7. Frontend behaviour

**None.** No API shape, SignalR contract, route, env var, or UI change. `monkify-front` is untouched.

---

## 8. Local environment & scripts

### 8.1 `docker-compose.yml`

- **Remove** the `rabbitmq` service (lines ~80–96) and both `rabbitmq: condition: service_healthy`
  `depends_on` entries (api + consumers).
- **Remove** both `ConnectionStrings__RabbitMQ` env lines; **add** to api **and** consumers:
  `ConnectionStrings__Messaging=Host=database;Port=5432;Database=spacestroker_messaging;Username=postgres;Password=postgres`.
- **Rename game DB:** api/consumers `ConnectionStrings__SpaceStroker` `Database=spacestroker`; `database` service
  `POSTGRES_DB=spacestroker`; healthcheck `pg_isready -U postgres -d spacestroker`.
- **Rename network** `monkify-network` → `spacestroker-network` (the `networks:` block + all 4 service
  references).

> The single Postgres `database` container hosts both `spacestroker` (POSTGRES_DB, created by the image) and
> `spacestroker_messaging` (created by the MT migration hosted service at Consumers startup — the `postgres`
> superuser has createdb). The api still `depends_on: consumers healthy`, so the messaging schema exists
> before the api publishes.

### 8.2 `scripts/setup_local_environment.bat`

- Line ~33: `--network monkify-backend_monkify-network` → `--network monkify-backend_spacestroker-network`
  (compose derives `<project>_<network>`; project stays `monkify-backend`, network becomes
  `spacestroker-network`). Update the comment lines ~5–6 and ~30 (drop "rabbitmq" from the container list).

### 8.3 `scripts/run_integration_tests.bat`

- Comment line ~7 mentions "PostgreSQL + RabbitMQ + Redis" → "PostgreSQL + Redis" (cosmetic).

---

## 9. Out of scope

- Event contracts, consumer handler logic, and the game flow itself (unchanged).
- Hangfire storage (stays on the **game** DB), Redis, and the SignalR backplane.
- The GCP terraform / Cloud SQL provisioning + `max_connections` tuning — owned by **#61** (this card
  produces the new secret, the createdb requirement, and a sizing note; see §14).
- `app.monkify.net` domain / CORS origins / operator `MainPageUrl` / Solana token symbol (owned by
  #62/#63 + ops).
- Fixing the separately-tracked multi-winner reward double-credit race (§6.5) — flag only.
- `monkify-front`.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.Api/SpaceStroker.Api.csproj` | `MassTransit.RabbitMQ` → `MassTransit.SqlTransport.PostgreSQL` (8.5.4) |
| 2 | `src/SpaceStroker.Infrastructure/SpaceStroker.Infrastructure.csproj` | same package swap |
| 3 | `src/SpaceStroker.Consumers/SpaceStroker.Consumers.csproj` | same package swap |
| 4 | `src/SpaceStroker.Api/Program.cs` | `UsingRabbitMq` → `UsingPostgres` (publisher-only); add `AddSpaceStrokerSqlTransportOptions` |
| 5 | `src/SpaceStroker.Consumers/Program.cs` | `UsingRabbitMq` → `UsingPostgres`; keep 8 named endpoints; add `AddSpaceStrokerSqlTransportOptions` + `AddPostgresMigrationHostedService` |
| 6 | `src/SpaceStroker.Infrastructure/Services/RabbitMQ/IRabbitMQService.cs` | → `Services/Messaging/IMessageBus.cs` (rename type + namespace) |
| 7 | `src/SpaceStroker.Infrastructure/Services/RabbitMQ/RabbitMQService.cs` | → `Services/Messaging/MessageBusPublisher.cs` (rename type + namespace; fix log text) |
| 8 | `src/SpaceStroker.Infrastructure/Messaging` (new) | `AddSpaceStrokerSqlTransportOptions` helper (`NpgsqlConnectionStringBuilder` → `SqlTransportOptions`) |
| 9 | `src/SpaceStroker.Infrastructure/DependencyInjection.cs` | registration + `using` → `IMessageBus`/`MessageBusPublisher` |
| 10 | `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` | `IRabbitMQService` → `IMessageBus` (ctor + field) |
| 11 | `src/SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs` | `IRabbitMQService` → `IMessageBus` |
| 12 | `src/SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs` | resolve `IMessageBus` |
| 13 | `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` | resolve `IMessageBus` |
| 14 | `src/SpaceStroker.Infrastructure/Consumers/GameTypeDeactivated/GameTypeDeactivatedConsumer.cs` | resolve `IMessageBus` |
| 15 | `src/SpaceStroker.Infrastructure/Consumers/BetNotification/BetNotificationConsumer.cs` | update `using` (namespace move) |
| 16 | `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | `IRabbitMQService` → `IMessageBus` |
| 17 | `src/SpaceStroker.Infrastructure/Handlers/Admin/CreateGameType/CreateGameTypeHandler.cs` | `IRabbitMQService` → `IMessageBus` |
| 18 | `src/SpaceStroker.Infrastructure/Handlers/Admin/DeactivateGameType/DeactivateGameTypeHandler.cs` | `IRabbitMQService` → `IMessageBus` |
| 19 | `src/SpaceStroker.Common/spacestrokersettings.json` | `Database=spacestroker`; add `Messaging` conn; remove `RabbitMQ` conn |
| 20 | `docker-compose.yml` | drop `rabbitmq` svc + depends_on + `ConnectionStrings__RabbitMQ`; add `ConnectionStrings__Messaging`; rename game DB → `spacestroker`; `monkify-network` → `spacestroker-network` |
| 21 | `scripts/setup_local_environment.bat` | network ref `monkify-backend_spacestroker-network`; comment cleanup |
| 22 | `scripts/run_integration_tests.bat` | comment cleanup (drop RabbitMQ) |
| 23 | `src/SpaceStroker.Tests.Integration/SpaceStroker.Tests.Integration.csproj` | remove `Testcontainers.RabbitMq` |
| 24 | `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` | drop `RabbitMqContainer` + `using`; set `ConnectionStrings__Messaging` from the PG container (swap `Database=spacestroker_messaging`); `PublishRunGameAsync` resolves `IMessageBus`; fix XML doc |
| 25 | Unit test files (§11) | swap `Mock<IRabbitMQService>` → `Mock<IMessageBus>` + `using` |

### 10.2 Frontend (`monkify-front`)

None.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/masstransit-postgres-transport.md` | this spec |
| 2 | `INDEX.md` | add spec row (Ready); update "Feature currently in flight" |
| 3 | `architecture.md` | **at ship time** (`/monkify-ship-feature`): §4 messaging (RabbitMQ→Postgres SQL transport), diagram, DB names `spacestroker`/`spacestroker_messaging`, §12 docker-compose (no rabbitmq). Not edited now. |

---

## 11. Tests

### 11.1 Unit tests — project `SpaceStroker.Tests` (run `scripts/run_tests.bat`)

Mechanical: every `Mock<IRabbitMQService>` becomes `Mock<IMessageBus>`; update `using`s. No behavior
change — these assert publish calls, which are transport-agnostic.

| Test file | Change | Expected |
| --- | --- | --- |
| `UnitTests/Shared/GameServiceTestFactory.cs` | `Mock<IRabbitMQService>` → `Mock<IMessageBus>` | builds |
| `UnitTests/Services/GameServiceTests.cs` | same | green |
| `UnitTests/Services/GameServiceParametersTests.cs` | same | green |
| `UnitTests/Hubs/ActiveGamesHubTests.cs` | same | green |
| `UnitTests/Handlers/RegisterBetHandlerTests.cs` | same | green |
| `UnitTests/Handlers/AdminGameTypeHandlerTests.cs` | same | green |
| `UnitTests/Background/ProcessGameHandlerTests.cs` | same (incl. `GetService(typeof(IMessageBus))`) | green |
| `UnitTests/Background/GameTypeDeactivatedConsumerTests.cs` | same | green |
| `UnitTests/Background/BetPlacedHandlerTests.cs` | same | green |
| `UnitTests/Background/CreateGameTests.cs` | same (2 local mocks) | green |

### 11.2 Integration tests — project `SpaceStroker.Tests.Integration` (run `scripts/run_integration_tests.bat`)

**Harness swap** (`Harness/IntegrationTestFixture.cs`):

- Remove `using Testcontainers.RabbitMq;`, the `_rabbitMq` field, its `StartAsync`/`DisposeAsync`, and
  the `ConnectionStrings__RabbitMQ` env var.
- Set the messaging connection from the Postgres container:
  ```csharp
  var msg = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "spacestroker_messaging" };
  Environment.SetEnvironmentVariable("ConnectionStrings__Messaging", msg.ToString());
  ```
  The Consumers host's MT migration hosted service creates `spacestroker_messaging` inside the same
  container (the container's superuser has createdb).
- `PublishRunGameAsync` (line ~349): resolve `IMessageBus` instead of `IRabbitMQService`.
- Update the class XML doc ("Postgres + Redis Testcontainers").

**Mandatory happy-path-over-Postgres:** the existing `GameFlowTests` (bet → run → reward end-to-end)
now runs entirely over the Postgres transport via the swapped harness — this **is** the required happy
path assertion (Decision + constraints). No new test needed for the happy path; confirm it's green.

**Complex-flow (redelivery idempotency) test:** add a test (e.g. `TransportIdempotencyFlowTests`) that
drives a game to `Ended`/rewarded, then **re-publishes `RewardGameEvent`** for the same game (simulating
at-least-once redelivery) via a new `PublishRewardGameAsync(gameId)` helper, and asserts the winner's
`TransactionLog` `Credit` count is **exactly 1** (no double credit). Keep it single-consumer/single-winner
to assert this card's idempotency bar; do **not** chase the separately-tracked multi-winner race (§6.5) —
if it surfaces, flag it.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given the backend solution, when it builds, then no project references `MassTransit.RabbitMQ` and
   Api/Infrastructure/Consumers reference `MassTransit.SqlTransport.PostgreSQL` (8.5.4).
2. Given the local docker-compose stack, when it starts fresh, then there is no `rabbitmq` service and
   no `ConnectionStrings__RabbitMQ` anywhere, and both hosts start healthy.
3. Given `SpaceStroker.Consumers` starts against a Postgres instance with no messaging DB, when it boots,
   then MassTransit's Postgres migration hosted service creates the `spacestroker_messaging` database and
   transport schema, and the consumers come up healthy.
4. Given the messaging transport is configured, when the bus connects, then it uses
   `ConnectionStrings:Messaging` (`Database=spacestroker_messaging`, same host/user as the game DB) and the
   messaging tables live in `spacestroker_messaging`, not in the game database.
5. Given a player places a bet, when the game runs, then the full `BetPlaced → RunGame → RewardGame`
   flow completes over the Postgres transport (no RabbitMQ) with all 8 named consumer endpoints active,
   and a winner is paid out.
6. Given a reward or refund message is redelivered (at-least-once), when the consumer reprocesses it,
   then the ledger-based idempotency (`TransactionLog`) prevents a double credit/refund and the winner
   is paid exactly once.
7. Given the publish abstraction is renamed, when the code compiles, then `IRabbitMQService`/
   `RabbitMQService` and the `Services/RabbitMQ` namespace no longer exist (replaced by `IMessageBus`/
   `MessageBusPublisher` under `Services/Messaging`) and no log/comment text still says "RabbitMQ".
8. Given the `monkify → spacestroker` rename, when the stack runs, then the game database is named
   `spacestroker` (spacestrokersettings.json + docker-compose `Database=`/`POSTGRES_DB=`/`pg_isready`), the
   docker network is `spacestroker-network`, and no `monkify`-named DB/infra remains.
9. Given the `SpaceStroker.Tests.Integration` suite, when it runs, then it uses no `RabbitMqContainer`,
   provisions `spacestroker_messaging` inside the Postgres container, and a happy-path test drives a full
   game over the Postgres transport and asserts it works end to end.
10. Given the affected unit tests, when the suite runs via `scripts/run_tests.bat`, then all tests pass
    and any RabbitMQ-referencing tests are updated to the Postgres transport / `IMessageBus`, with the
    changed/added tests reported at the end.

---

## 13. Manual QA

1. From `monkify-backend`, run `scripts/setup_local_environment.bat` (fresh `docker compose down -v`
   first). Confirm **no** `rabbitmq` container starts and all of api/consumers/database/redis come up
   healthy.
2. `docker exec` into the `database` container: `psql -U postgres -l` shows **both** `spacestroker` and
   `spacestroker_messaging`; `\dt transport.*` in `spacestroker_messaging` shows MassTransit tables; the game
   schema tables are **not** in `spacestroker_messaging`.
3. Open the SPA (`localhost:6003`), place a bet on a dev game, confirm the round runs (SignalR terminal
   streams) and a winner is paid — proving the full event flow over Postgres.
4. Confirm the Hangfire dashboard (`localhost:6002/hangfire`) still lists `game-chat-purge` and lottery
   schedules (Hangfire still on the `spacestroker` game DB).

---

## 14. Handoff to #61 (terraform — [QrjmQttY](https://trello.com/c/QrjmQttY))

- **New secret:** `ConnectionStrings:Messaging` (same instance/user as the game DB,
  `Database=spacestroker_messaging`) → Secret Manager.
- **Grant:** the game-DB user needs **CREATE DATABASE** on the Cloud SQL instance (MT creates
  `spacestroker_messaging` at startup), **or** #61 pre-creates the empty DB and the user only needs schema
  rights.
- **Sizing:** size Cloud SQL `max_connections` for the EF pool + Hangfire + the transport's LISTEN/poll
  connections on one instance; note autovacuum on the messaging DB (high queue churn).
- **Naming:** the game DB is now **`spacestroker`** (not `monkify`); terraform/Cloud SQL must reference
  `spacestroker` + `spacestroker_messaging`.

---

## 15. Trello / pipeline

- **Source card:** [#64 nPCH4e5R](https://trello.com/c/nPCH4e5R) — list **Specs created**;
  **Implementation** section on the card carries branch + spec + worktree.
- **Blocks #61** "Create terraform to deploy the app" — do this first or in lockstep.
- **Next:** `/monkify-implement-feature Replace RabbitMQ with MassTransit Postgres SQL transport`
  (build it), then `/monkify-ship-feature …` (sync KB — incl. `architecture.md` §4 — and merge to `main`).

---

## 16. Revision log

| Date | Change |
| --- | --- |
| 2026-07-16 | Initial spec from refined card #64 (same-session refinement + code exploration). |
