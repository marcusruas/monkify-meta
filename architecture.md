# Monkify — Architecture

> Scope: `monkify-backend` (.NET 8) and `monkify-front` (Vue 3). This describes the **current
> live state** of the code, not a future plan. Deep design lives in `specs/`.

## 1. High-level picture

```
                 ┌─────────────────────────────────────────────────────────────┐
                 │                         Players                              │
                 │   (browser: Vue 3 SPA + Solana wallet + SignalR client)     │
                 └───────────────┬───────────────────────────┬─────────────────┘
                          HTTPS (axios)               WebSocket (SignalR)
                                 │                           │
                 ┌───────────────▼───────────────┐          │
                 │        Monkify.Api             │◄─────────┘  (Hubs: /Hubs/Sessions, /Hubs/RecentBets)
                 │  Controllers → MediatR handlers│
                 └───────┬───────────────┬────────┘
                         │               │ publish events
                         │               ▼
                 ┌───────▼──────┐   ┌──────────────┐
                 │ PostgreSQL   │   │  RabbitMQ    │  (MassTransit)
                 │ (EF / Npgsql)│   └──────┬───────┘
                 └───────┬──────┘          │ consume
                         │          ┌──────▼──────────────────┐
                 ┌───────▼──────┐   │   Monkify.Consumers      │
                 │    Redis     │◄──┤  MassTransit consumers   │
                 │ cache + SignalR│ │  + Hangfire scheduler    │
                 │  backplane    │  │  + EF migrations/seeding │
                 └──────────────┘   └──────┬───────────────────┘
                                           │ pay out / refund
                                    ┌──────▼───────────────────┐
                                    │ Payment processors        │
                                    │ Solana / PayPal / Stripe… │
                                    └───────────────────────────┘
```

Two deployable .NET hosts share the same domain/infrastructure libraries:

- **`Monkify.Api`** — public HTTP API + SignalR hubs. Stateless. Does **not** apply migrations.
- **`Monkify.Consumers`** — background worker host. Runs MassTransit consumers, the Hangfire
  server, applies EF migrations at startup, seeds defaults, and opens initial sessions.

## 2. Backend solution layout (`monkify-backend/src`)

| Project | Responsibility |
| --- | --- |
| `Monkify.Api` | ASP.NET Core Web API. Controllers, SignalR hub mapping, Swagger, IP rate limiting, CORS. Thin: delegates to MediatR handlers. |
| `Monkify.Consumers` | Worker host (also a `WebApplication` for the Hangfire dashboard + health). MassTransit consumers, Hangfire server, startup migrations & seeding. |
| `Monkify.Domain` | Pure domain: entities, value objects/enums, and domain services (`MonkifyTyper`, `BetDomainService`). No EF/infra dependencies. |
| `Monkify.Infrastructure` | EF `DbContext`, MediatR request handlers, MassTransit consumers, SignalR hubs, services (Sessions, Cache, RabbitMQ, Solana, Payments), DI wiring. |
| `Monkify.Common` | Cross-cutting: `IMessaging`, extensions, `Result`/`ApiResult` types, error-message resources, `TableEntity` base. |
| `Monkify.Tests` | xUnit + Shouldly + Moq + Bogus. In-memory EF for handler/service/domain tests. |
| `Monkify.LoadTester` | Load-testing utilities. |

### Layering & request pattern

- HTTP request → **Controller** (e.g. `SessionsController`, `OperatorsController`) → constructs a
  **MediatR request** → **handler** that extends `BaseRequestHandler` → **services**
  (`SessionService`, payment processors) → **`MonkifyDbContext`**.
- Controllers extend `BaseController`; responses are wrapped by `ProcessRequest(...)` into a
  standard `ApiResult<T>` shape (`{ data, messages }`). Validation failures bubble up via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- Handlers live under `Monkify.Infrastructure/Handlers/<Area>/<UseCase>/` as a
  `Request` + `Handler` pair (e.g. `RegisterBet`, `GetActiveParameters`, `GetSessionHistory`,
  `GetActiveSessionForParameter`, `Operators/GetOperators`).

## 3. Data: PostgreSQL + EF Core (Npgsql)

- **Provider:** PostgreSQL via `UseNpgsql`. `NpgsqlDataSourceBuilder.EnableDynamicJson()` maps
  `jsonb` columns to strongly-typed POCOs (used by `BetPayment.Details`).
- **Single DbContext:** `Monkify.Infrastructure/Data/Context/MonkifyDbContext.cs`.
- **EF configuration is INLINE** in `OnModelCreating` — there are **no** `IEntityTypeConfiguration`
  classes. All entity mapping is in the `modelBuilder.Entity<T>(builder => { … })` blocks.
- Default query behavior is `NoTracking` (set in `OnConfiguring`). `SaveChangesAsync` stamps
  `UpdatedDate` on modified `TableEntity` rows and then clears the change tracker.
- **Migrations live in `Monkify.Infrastructure/Migrations/`** and are applied **only** by
  `Monkify.Consumers` at startup via `dbContext.Database.Migrate()` (`ApplyMigrations`). The API
  never migrates. `MonkifyDbContextModelSnapshot.cs` is auto-generated — do not hand-edit.

### Core entities (selected)

- **Sessions area** (`Monkify.Domain/Sessions/Entities`): `Session`, `SessionParameters`
  (a "game type"), `LotteryParameters`, `PresetChoice`, `SessionStatusLog`, `Operator`,
  `SessionParameterPaymentMethod`.
- **Bets area** (`Monkify.Domain/Bets/Entities`): `Bet`, `BetPayment` (+ `BetPaymentDetails`
  jsonb POCO), `TransactionLog` (the audit ledger), `BetStatusLog`.
- All entities extend `TableEntity` (`Id: Guid`, `CreatedDate`, `UpdatedDate`).

Relationships: `Operator 1—* SessionParameters 1—* Session 1—* Bet 1—* BetPayment`, and
`Bet 1—* TransactionLog`. `SessionParameters *—* PaymentMethod` via the
`SessionParameterPaymentMethod` join entity.

## 4. Messaging: RabbitMQ via MassTransit

- The **API** registers MassTransit only as a **publisher**. The **Consumers** host registers and
  binds the consumers (one receive endpoint per consumer).
- `IRabbitMQService` / `RabbitMQService` is a thin wrapper over `IPublishEndpoint.Publish`.

### Events and consumers (the session state machine)

| Event | Published by | Consumed by | Effect |
| --- | --- | --- | --- |
| `BetPlacedEvent` | `RegisterBetHandler` (non-lottery), Hangfire (lottery), `BetPlacedConsumer` flow | `BetPlacedConsumer` | After a minimum wait & min-player check, tries to start the session, then publishes `RunSessionEvent`. Refunds bets if a lottery lacks players. |
| `RunSessionEvent` | `BetPlacedConsumer` | `RunSessionConsumer` | Marks session `InProgress`, runs the typer, marks `Ended`, flags winners `NeedsRewarding`, creates the next session, publishes `RewardSessionEvent`. |
| `RewardSessionEvent` | `RunSessionConsumer` | `RewardSessionConsumer` | For each winner, resolves the payment processor for the bet's inbound method and calls `Credit(...)`; updates statuses. |
| `RefundInvalidBetEvent` | `RegisterBetHandler`, `BetPlacedConsumer` | `RefundInvalidBetConsumer` | Refunds an invalid/rejected bet via the appropriate processor. Carries `PaymentMethod` + external reference (not a raw signature). |
| `BetNotificationEvent` | `SessionService.InsertBet` | `BetNotificationConsumer` | Pushes the new bet to subscribers (recent-bets feed). |

> The "monkey typing" loop and all reward/refund money movement happen in the **Consumers** host,
> not the API.

## 5. Caching: Redis

- `ICacheService` with two impls: `RedisCacheService` (when a Redis connection string is present)
  and `MemoryCacheService` (fallback). `StackExchange.Redis` `IConnectionMultiplexer` is a singleton.
- `CacheHelper` builds cache keys/TTLs. The active-parameters cache is **operator-aware**:
  the key is namespaced per operator (house vs. each operator id), so one operator's game types
  never leak into another's.
- Redis is also the **SignalR backplane** (`AddStackExchangeRedis`) so hub messages fan out across
  instances.

## 6. Real-time: SignalR

- Hubs in `Monkify.Infrastructure/Hubs`: `ActiveSessionsHub` (mapped at `/Hubs/Sessions`) and
  `RecentBetsHub` (mapped at `/Hubs/RecentBets`), both registered in `Monkify.Api/Program.cs`.
- Clients join a per-session group (`JoinSession`). During a run, the typer streams:
  - `…/Terminal` — batched characters,
  - `…/DisplaySpeed` — current typing speed,
  - `…/Status` — session status transitions (incl. winners on `Ended`),
  - and `SessionReceived` / a recent-bets `…/Bets` channel.
- JSON uses camelCase. Both the API and Consumers configure SignalR (Consumers emits during runs).

## 7. Scheduling: Hangfire

- Configured in `Monkify.Consumers` with **PostgreSQL storage** (`UsePostgreSqlStorage`), with a
  dashboard at `/hangfire`.
- Used to **schedule lottery sessions**: `SessionService.ScheduleLottery` enqueues a delayed
  `BetPlacedEvent` publish for the session's start time.

## 8. Blockchain & payments

### Solana

- `Solnet` (`Solnet.Rpc`, `Solnet.Programs`, `Solnet.Wallet`) talks to a Solana cluster.
- `ISolanaService` / `SolanaService` handle SPL token transfers (SOL / USDC) and blockhash checks.
- The house owner/sender token account is the funded float for crypto payouts.

### Payment-method strategy (multiple payment methods)

- `IPaymentMethodProcessor` is the per-method strategy with `ValidateInboundPayment`, `Credit`,
  `Refund`. Resolved through **keyed DI** (.NET 8 `AddKeyedScoped<…>(PaymentMethod)`) via
  `IPaymentMethodProcessorFactory`.
- Implementations in `Monkify.Infrastructure/Services/Payments/Processors/`:
  `SolanaPaymentProcessor` (SOL), `UsdtPaymentProcessor` (USDT/USDC), `FreeCoinPaymentProcessor`,
  `PayPalPaymentProcessor`, `VenmoPaymentProcessor` (Braintree), `OnlineBankingPaymentProcessor`
  (Stripe ACH), `CardPaymentProcessor` (Stripe cards). Shared bases:
  `CryptoPaymentProcessorBase`, `StripePaymentProcessorBase`.
- External SDKs are wrapped behind mockable seams in `Services/Payments/Providers/`
  (`IPayPalClient`, `IBraintreeGateway`, `IStripeClientWrapper`).
- **Money is audited as USD.** Each payment is a `BetPayment` (Inbound stake / Outbound
  reward|refund) with method-specific JSONB `Details`; every movement also writes a
  `TransactionLog` row (`Inbound` / `Credit` / `Refund`). Reward/refund math counts only
  `TransactionType.Credit`.

## 9. The game mechanic (the "Typer")

- `MonkifyTyper` (`Monkify.Domain/Sessions/Services`) is the heart of a running session.
- It seeds a deterministic RNG from session + per-bet seeds, builds the set of bet choices and the
  pool of characters to type, then emits characters one at a time. When the rolling window of typed
  characters equals a bet's choice, that choice wins (`HasWinners`, `FirstChoiceTyped`).
- Typing speed/pacing is computed from the number of choices and character-pool size; speed ramps
  up as the run progresses. The `SessionService.RunSession` loop streams output over SignalR.

## 10. Multi-tenancy: Operators

- `Operator` rows represent casino/betting platforms. `SessionParameters.OperatorId` is a nullable
  FK (null = "house").
- Requests carry an optional **`operator-id` HTTP header**. Read GET handlers and bet creation
  scope data by it: header present → that operator's data (only if the contract is active/not
  expired); absent → house data (`OperatorId == null`).
- Expired/inactive operators are hidden behind generic "not found / unavailable" messaging
  (never "contract expired"). Branding is served by `GET /operators` (active operators only).
- See `specs/operator-feature.md` for full rules; `features/operators.md` for the distilled version.

## 11. Frontend (`monkify-front`)

- **Stack:** Vue 3 (Options/Composition via `<script setup>`), TypeScript, Vue CLI (webpack),
  Tailwind CSS + DaisyUI, Vuex + vue-router, `@microsoft/signalr`, Solana wallet adapter
  (`solana-wallets-vue`, `@solana/web3.js`, `@solana/spl-token`), axios, `vue3-toastify`.
- **Structure** (`src/`):
  - `hooks/` — composables: `useSession` (session state, websockets, bet flow), `useConnection`
    (Solana wallet/token transfer), `useOperator` (capture/persist `operator-id`, theming),
    `useCurrency` (display FX).
  - `services/` — `mainService.ts` (axios instance + endpoints), interceptors, handlers.
  - `types/` — shared TS types/enums (`ActiveSession`, `BodySessionBet`, `PaymentMethod`,
    `OperatorBranding`, …).
  - `pages/` — `SessionSelectPage`, `GamePage`, `HomePage`. `components/` — UI, game, header,
    background. `constants/`, `assets/`.
- **Routing:** `/` → `/game-select` (`SessionSelectPage`) → `/game` (`GamePage`).
- **API access:** `mainService` axios instance. A request interceptor sets `baseURL` from
  `VUE_APP_API_BASE_URL` and attaches the `operator-id` header from `sessionStorage` when present.
  Endpoints: `GET /sessions/active-types`, `GET /sessions/{id}`, `POST /sessions/{id}/bets`,
  `GET /operators`.
- **Real-time:** `useSession` opens two SignalR hubs (`Sessions`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`, joins the session group, and renders the streamed terminal output,
  speed, status, and incoming bets.
- **Bet flow:** `useSession.bet()` branches by `PaymentMethod`. SOL/USDT use the on-chain
  `sendToken` (the returned signature becomes `externalReference`); FreeCoin sends no reference;
  PayPal/Venmo/Stripe branches are stubbed pending live keys.

## 12. Local environment (docker-compose)

`monkify-backend/docker-compose.yml` brings up the full stack for development:

- `api` (port 6001 → 8080), `consumers` (port 6002 → 8080, hosts `/hangfire`),
- `database` (Postgres 16), `redis` (7), `rabbitmq` (3-management, UI on 15672).
- The API depends on the Consumers being healthy (so migrations/seeding run first).

> Connection strings and provider credentials are supplied via environment/config and **must not
> be read or edited** by the agent — see `constraints.md`.
