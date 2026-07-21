# Feature — MassTransit Postgres SQL transport (no broker)

> Status: **shipped.** Authoritative spec: `../specs/masstransit-postgres-transport.md`
> (merged via `feature/masstransit-postgres-transport` on 2026-07-16).

## What it does

The game state-machine events ride the **MassTransit Postgres SQL transport** instead of RabbitMQ, so
SpaceStroker runs with **no message broker** — one fewer always-on service to host, patch, and secure
(important for a crypto app) and one fewer fixed cost. This unblocks deploying to GCP, which has no
managed RabbitMQ. It is a **transport + provisioning** change only: event contracts, consumers, and the
game flow are unchanged. The change also renamed the game database `monkify → spacestroker`.

## Core rules

- **No broker.** MassTransit is configured with `UsingPostgres` (`MassTransit.SqlTransport.PostgreSQL`
  8.5.4). There is no RabbitMQ/AMQP service anywhere (local compose or deployed).
- **Separate messaging database.** The transport's queues/topics/subscriptions/messages are tables under
  schema **`transport`** in a **`spacestroker_messaging`** database — a distinct DB on the **same** Postgres
  instance as the game DB (`spacestroker`). Game tables never live in `spacestroker_messaging`.
- **Consumers provisions; API only publishes.** `SpaceStroker.Consumers` runs
  `AddPostgresMigrationHostedService()` at startup, which creates the `spacestroker_messaging` database and
  `transport` schema before the bus connects (mirroring how it owns EF migrations). `SpaceStroker.Api`
  registers `UsingPostgres()` **publisher-only** and never provisions the transport (it stays stateless).
- **One reused credential.** Both hosts bind `SqlTransportOptions` from `ConnectionStrings:Messaging`,
  reusing the game-DB user/host/password (only `Database=` differs). That user must have **`CREATE
  DATABASE`** on the instance so the migration service can create `spacestroker_messaging`.
- **Eight named endpoints kept.** The Consumers host still declares one explicitly-named
  `ReceiveEndpoint` per consumer (`BetPlacedConsumer`, `RunGameConsumer`, `RewardGameConsumer`,
  `RefundInvalidBetConsumer`, `BetNotificationConsumer`, `ChatMessagePersistedConsumer`,
  `GameParametersCreatedConsumer`, `GameTypeDeactivatedConsumer`) plus `ConfigureEndpoints`.
- **At-least-once, idempotent.** The SQL transport delivers at-least-once (competing consumers via
  `SKIP LOCKED` + `LISTEN/NOTIFY`). Reward/refund idempotency is ledger-based (`TransactionLog`): a
  redelivered `RewardGameEvent` for an already-rewarded game short-circuits (the winners are no longer
  `NeedsRewarding`) and credits the winner **exactly once** (verified by an integration test).
- **Hangfire is unchanged** and stays on the **game** DB (`spacestroker`); Redis (cache + SignalR backplane)
  is unchanged.
- **Publish abstraction renamed** transport-neutral: `IMessageBus` / `MessageBusPublisher`
  (was `IRabbitMQService` / `RabbitMQService`).
- **DB / infra rename:** the game database is `spacestroker` (was `monkify`); the local docker network is
  `spacestroker-network`.

## Key code touchpoints

Backend (`monkify-backend`, on `main`):
- `src/SpaceStroker.Infrastructure/Services/Messaging/` — `IMessageBus`, `MessageBusPublisher`,
  `MessagingRegistration.AddSpaceStrokerSqlTransportOptions` (binds `SqlTransportOptions` from
  `ConnectionStrings:Messaging` via `NpgsqlConnectionStringBuilder`; reuses the game-DB user as
  runtime + admin + role).
- `src/SpaceStroker.Api/Program.cs` — `AddSpaceStrokerSqlTransportOptions` + `AddMassTransit(x => x.UsingPostgres())`
  (publisher-only).
- `src/SpaceStroker.Consumers/Program.cs` — `AddSpaceStrokerSqlTransportOptions` +
  `AddPostgresMigrationHostedService()` + `AddMassTransit(... UsingPostgres(...))` with the 8 named endpoints.
- `src/SpaceStroker.{Api,Infrastructure,Consumers}/*.csproj` — `MassTransit.SqlTransport.PostgreSQL` 8.5.4
  (replaced `MassTransit.RabbitMQ`).
- `src/SpaceStroker.Common/spacestrokersettings.json` — `ConnectionStrings:SpaceStroker` (`Database=spacestroker`) +
  `ConnectionStrings:Messaging` (`Database=spacestroker_messaging`); no `RabbitMQ` entry.
- `docker-compose.yml` — no `rabbitmq` service; `ConnectionStrings__Messaging` on api + consumers;
  `POSTGRES_DB=spacestroker`; `spacestroker-network`. `scripts/setup_local_environment.bat` network ref updated.
- Consumers/handlers/hubs resolve `IMessageBus` (GameService, ActiveGamesHub, RunGameConsumer,
  BetPlacedConsumer, GameTypeDeactivatedConsumer, BetNotificationConsumer, RegisterBetHandler,
  CreateGameTypeHandler, DeactivateGameTypeHandler).

Tests:
- `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` — no `RabbitMqContainer`; derives
  `ConnectionStrings__Messaging` from the Postgres container (`Database=spacestroker_messaging`); the
  Consumers migration service provisions it. `PublishRewardGameAsync` helper added.
- `src/SpaceStroker.Tests.Integration/TransportIdempotencyFlowTests.cs` — redelivery idempotency test.
- `GameFlowTests` now exercises the happy path over the Postgres transport.

Frontend: **none** (no client/API/SignalR change).
