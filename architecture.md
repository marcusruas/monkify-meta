# SpaceStroker — Architecture

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
                 │        SpaceStroker.Api             │◄─────────┘  (Hubs: /Hubs/Games, /Hubs/RecentBets)
                 │  Controllers → MediatR handlers│
                 └───────┬────────────────────────┬────────┘
                         │                        │ publish events
                         │                        │ (MassTransit SQL transport)
                 ┌───────▼────────────────────────▼────────┐
                 │ PostgreSQL (EF / Npgsql), one instance   │
                 │  • spacestroker            (game DB)          │
                 │  • spacestroker_messaging  (MassTransit SQL   │
                 │     transport: queues/topics/messages)    │
                 └───────┬────────────────────────┬─────────┘
                         │                        │ consume (SKIP-LOCKED + LISTEN/NOTIFY)
                 ┌───────▼──────┐          ┌───────▼──────────────────┐
                 │    Redis     │◄─────────┤   SpaceStroker.Consumers      │
                 │ cache+SignalR│          │  MassTransit consumers   │
                 │  backplane   │          │  + Hangfire scheduler    │
                 └──────────────┘          │  + EF + transport         │
                                           │    migrations/seeding     │
                                           └──────┬───────────────────┘
                                                  │ pay out / refund
                                           ┌──────▼───────────────────┐
                                           │ SolanaService             │
                                           │ SPL token transfers       │
                                           └───────────────────────────┘
```

> **No message broker.** Events ride the **MassTransit Postgres SQL transport** — there is no
> RabbitMQ/AMQP broker. See §4.

Two deployable .NET hosts share the same domain/infrastructure libraries:

- **`SpaceStroker.Api`** — public HTTP API + SignalR hubs. Stateless. Does **not** apply migrations.
- **`SpaceStroker.Consumers`** — background worker host. Runs MassTransit consumers, the Hangfire
  server, applies EF migrations at startup, seeds defaults, and opens initial games.

## 2. Backend solution layout (`monkify-backend/src`)

| Project | Responsibility |
| --- | --- |
| `SpaceStroker.Api` | ASP.NET Core Web API. Controllers, SignalR hub mapping, Swagger, IP rate limiting, CORS. Thin: delegates to MediatR handlers. |
| `SpaceStroker.Consumers` | Worker host (also a `WebApplication` for the Hangfire dashboard + health). MassTransit consumers, Hangfire server, startup migrations & seeding. |
| `SpaceStroker.Domain` | Pure domain: entities, value objects/enums, and domain services (`SpaceStrokerTyper`, `BetDomainService`). No EF/infra dependencies. |
| `SpaceStroker.Infrastructure` | EF `DbContext`, MediatR request handlers, MassTransit consumers, SignalR hubs, services (Games, Cache, Messaging, Solana, Payments), DI wiring. |
| `SpaceStroker.Common` | Cross-cutting: `IMessaging`, extensions, `Result`/`ApiResult` types, error-message resources, `TableEntity` base. |
| `SpaceStroker.Tests` | xUnit + Shouldly + Moq + Bogus. In-memory EF for handler/service/domain tests. |
| `SpaceStroker.LoadTester` | Load-testing utilities. |

### Layering & request pattern

- HTTP request → **`LaunchTokenValidationMiddleware`** (validates `X-Launch-Token`, sets
  `ILaunchContext`; rejects admin-scoped tokens) and/or **`AdminTokenValidationMiddleware`**
  (validates `X-Admin-Token`, sets `IAdminContext`; guards `/admin` only) → **Controller**
  (`GamesController`, `OperatorsController`, `PlayersController`, `AdminController`, dev-only
  `DevController`) → constructs a **MediatR request** → **handler** that extends
  `BaseRequestHandler` → **services** (`GameService`, `IOperatorService`, payment processors) →
  **`SpaceStrokerDbContext`**.
- Controllers extend `BaseController`; responses are wrapped by `ProcessRequest(...)` into a
  standard `ApiResult<T>` shape (`{ data, messages }`). Validation failures bubble up via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- Handlers live under `SpaceStroker.Infrastructure/Handlers/<Area>/<UseCase>/` as a
  `Request` + `Handler` pair (e.g. `RegisterBet`, `GetActiveParameters`, `GetGameHistory`,
  `GetActiveGameForParameter`, `GetGameTypeStats`, `Operators/GetOperatorById`). Read handlers
  may also be backed by dedicated services — e.g. `GET /games/{parametersId}/stats` aggregates
  per-game-type all-time stats on demand via `IGameStatsService` (Redis-cached, no new table).
  See `features/game-type-stats.md`. `GET /games/{parametersId}/history` (paginated completed-round
  history for one game type) works the same way via `IGameHistoryService` — the public page is
  Redis-cached 5 min per operator+type+page+size and the caller's own-bet columns are joined live.
  See `features/games-history.md`.

## 3. Data: PostgreSQL + EF Core (Npgsql)

- **Provider:** PostgreSQL via `UseNpgsql`.
- **Two databases, one instance:** the game/EF store is **`spacestroker`** (`ConnectionStrings:SpaceStroker`);
  the MassTransit message transport lives in a **separate database `spacestroker_messaging`**
  (`ConnectionStrings:Messaging`) on the **same** Postgres instance, reusing the same user (see §4).
  Hangfire storage stays in the **game** DB. (The game DB was renamed from `monkify` → `spacestroker`.)
- **Single DbContext:** `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (game DB only — the
  transport schema is MassTransit-owned, not in the DbContext).
- **EF configuration is INLINE** in `OnModelCreating` — there are **no** `IEntityTypeConfiguration`
  classes. All entity mapping is in the `modelBuilder.Entity<T>(builder => { … })` blocks.
- Default query behavior is `NoTracking` (set in `OnConfiguring`). `SaveChangesAsync` stamps
  `UpdatedDate` on modified `TableEntity` rows and then clears the change tracker.
- **Migrations live in `SpaceStroker.Infrastructure/Migrations/`** and are applied **only** by
  `SpaceStroker.Consumers` at startup via `dbContext.Database.Migrate()` (`ApplyMigrations`). The API
  never migrates. `SpaceStrokerDbContextModelSnapshot.cs` is auto-generated — do not hand-edit.

### Core entities (selected)

- **Games area** (`SpaceStroker.Domain/Games/Entities`): `Game` (carries the provably-fair
  **`ServerSeed`** + **`ServerSeedHash`** committed on the first bet; the old `int? Seed` is removed),
  `GameParameters` (a "game type"; carries `PaymentMethod` + optional `OperatorTokenId`),
  `LotteryParameters`, `PresetChoice`, `GameStatusLog`, `Operator`, **`OperatorToken`** (a
  per-operator registered SPL token; owner private key encrypted at rest).
- **Bets area** (`SpaceStroker.Domain/Bets/Entities`): `Bet` (its `Seed` is now a 64-char
  `HMAC(clientSeed, Nonce)` hex, with a per-bet **`Nonce`**), `TransactionLog` (unified money-movement
  ledger — `Credit`/`Debit`/`Refund`, now carrying `PaymentMethod` + `ExternalTransactionReference`),
  **`CryptoTransactionLog`** (on-chain specifics for `SolanaToken` movements), `BetStatusLog`.
  (`RefundLog` was removed; refund audit lives in `TransactionLog`.)
- All entities extend `TableEntity` (`Id: Guid`, `CreatedDate`, `UpdatedDate`).

Relationships: `Operator 1—* GameParameters 1—* Game 1—* Bet 1—* TransactionLog`;
`Operator 1—* OperatorToken 1—* GameParameters` (nullable — only `SolanaToken` types reference a token);
`Bet 1—* CryptoTransactionLog`; `Operator 1—* OperatorPlayer 1—* Bet` (nullable FK — anonymous free bets
have no `OperatorPlayer`). See `features/payment-methods.md`.

## 4. Messaging: MassTransit Postgres SQL transport

- **No broker.** Events flow over the **MassTransit Postgres SQL transport**
  (`MassTransit.SqlTransport.PostgreSQL`, `UsingPostgres`) — there is **no** RabbitMQ/AMQP broker to
  run. Queues, topics, subscriptions, and messages are tables under schema **`transport`** in the
  **`spacestroker_messaging`** database (a separate DB on the same Postgres instance as the game DB; see
  §3). Delivery is at-least-once with competing consumers via `SKIP LOCKED` + `LISTEN/NOTIFY`.
- The **API** registers MassTransit only as a **publisher** (`UsingPostgres()`, no consumers, no
  provisioning — it stays stateless). The **Consumers** host registers and binds the consumers (one
  named receive endpoint per consumer) and **provisions the transport**: `AddPostgresMigrationHostedService()`
  creates the `spacestroker_messaging` database + `transport` schema at startup (mirroring how it owns EF
  migrations), before the bus connects.
- Both hosts bind `SqlTransportOptions` from `ConnectionStrings:Messaging` (helper
  `MessagingRegistration.AddSpaceStrokerSqlTransportOptions`, parsed via `NpgsqlConnectionStringBuilder`),
  **reusing the game-DB user** — so that user needs `CREATE DATABASE` on the instance.
- The publish abstraction is `IMessageBus` / `MessageBusPublisher`
  (`SpaceStroker.Infrastructure.Services.Messaging`), a thin wrapper over `IPublishEndpoint.Publish`.
- **Event contracts and consumer handler logic are unchanged** by the transport swap — only the wire
  changed. Reward/refund idempotency (ledger-based in `TransactionLog`) holds under at-least-once
  redelivery. See `features/masstransit-postgres-transport.md`.

### Events and consumers (the game state machine)

| Event | Published by | Consumed by | Effect |
| --- | --- | --- | --- |
| `BetPlacedEvent` | `RegisterBetHandler` (non-lottery), Hangfire (lottery), `BetPlacedConsumer` flow | `BetPlacedConsumer` | After a minimum wait & min-player check, tries to start the game, then publishes `RunGameEvent`. Refunds bets if a lottery lacks players. |
| `RunGameEvent` | `BetPlacedConsumer` | `RunGameConsumer` | Marks game `InProgress`, runs the typer, marks `Ended`, flags winners `NeedsRewarding`, creates the next game, publishes `RewardGameEvent`. |
| `RewardGameEvent` | `RunGameConsumer` | `RewardGameConsumer` | For each winner, calls `ISolanaService.TransferTokensForBet(...)`; updates statuses. |
| `RefundInvalidBetEvent` (carries `BetId`) | `RegisterBetHandler`, `BetPlacedConsumer`, `GameTypeDeactivatedConsumer`, `PendingRefundSweepJob` (12h sweep of `NeedsRefunding`) | `RefundInvalidBetConsumer` | Refunds an invalid/rejected/flagged bet — crypto via `ISolanaService.RefundTokens`, regulated via the operator wallet adapter. Clears the bet to `Refunded` on success (any path); skipped for free bets (`Value == 0`). See `features/refund-safeguards.md`. |
| `GameParametersCreatedEvent` | `CreateGameTypeHandler` | `GameParametersCreatedConsumer` | Opens the first game for a newly created operator game type (idempotent). |
| `GameTypeDeactivatedEvent` | `DeactivateGameTypeHandler` | `GameTypeDeactivatedConsumer` | Cancels `WaitingBets` games of a deactivated type (refunds paid bets, skips free). |
| `BetNotificationEvent` | `GameService.InsertBet` | `BetNotificationConsumer` | Pushes the new bet to subscribers (recent-bets feed). |

> The "monkey typing" loop and all reward/refund money movement happen in the **Consumers** host,
> not the API.

## 5. Caching: Redis

- `ICacheService` with two impls: `RedisCacheService` (when a Redis connection string is present)
  and `MemoryCacheService` (fallback). `StackExchange.Redis` `IConnectionMultiplexer` is a singleton.
- `CacheHelper` builds cache keys/TTLs. The active-parameters cache is **operator-aware**:
  keys are `ActiveParameters:{operatorId}` and `Operator:{operatorId}` so one operator's game types
  and branding never leak into another's.
- **FX rates (display-only):** the full USD→fiat rate table is cached under `Fx:Rates:USD` with a
  **6-hour TTL** (`GetFxRatesHandler`); populated by `IExchangeRateProvider` (FreeCurrencyAPI or mock).
  See `features/player-customization-modal.md`.
- Redis is also the **SignalR backplane** (`AddStackExchangeRedis`) so hub messages fan out across
  instances.

## 6. Real-time: SignalR

- Hubs in `SpaceStroker.Infrastructure/Hubs`: `ActiveGamesHub` (mapped at `/Hubs/Games`) and
  `RecentBetsHub` (mapped at `/Hubs/RecentBets`), both registered in `SpaceStroker.Api/Program.cs`.
- Clients join a per-game group (`JoinGame`). During a run, the typer streams:
  - `…/Terminal` — batched characters,
  - `…/DisplaySpeed` — current typing speed,
  - `…/Status` — game status transitions (incl. winners on `Ended`),
  - `{gameId}/ChatMessage` — per-game chat (`SendChatMessage` on the same hub/group),
  - and `GameReceived` / a recent-bets `…/Bets` channel.
- JSON uses camelCase. Both the API and Consumers configure SignalR (Consumers emits during runs).

## 7. Scheduling: Hangfire

- Configured in `SpaceStroker.Consumers` with **PostgreSQL storage** (`UsePostgreSqlStorage`) in the
  **game** DB (`spacestroker`, `ConnectionStrings:SpaceStroker` — not the messaging DB), with a dashboard at
  `/hangfire`.
- Used to **schedule lottery games**: `GameService.ScheduleLottery` enqueues a delayed
  `BetPlacedEvent` publish for the game's start time.
- **Game chat purge:** daily recurring job `game-chat-purge` runs `ChatMessagePurgeJob`, deleting
  `GameChatMessage` rows older than `GeneralSettings.Chat.RetentionDays` (default 7). See
  `features/game-chat.md`.
- **Pending-refund sweep:** 12-hourly recurring job `pending-refund-sweep` (cron `0 */12 * * *`) runs
  `PendingRefundSweepJob`, publishing a `RefundInvalidBetEvent` for every bet in `NeedsRefunding` so the
  refund consumer pays it back and clears it to `Refunded` (failures retry next run). See
  `features/refund-safeguards.md`.

## 8. Blockchain & payments

- **Payment method per game type** (`GameParameters.PaymentMethod`, `Standard | SolanaToken`) —
  **not** per operator (the old `Operator.PaymentModel` is removed). **Standard** settles off-chain in
  USD through the **operator's wallet API**; **SolanaToken** settles on-chain in a per-operator
  **registered `OperatorToken`** (its own mint/sender/owner-keypair/decimals; token-native units). A
  single operator can offer both. See `features/payment-methods.md`.
- **Unified dispatch:** `IPaymentMethodService` (methods `Debit`/`Credit`/`Refund` → `PaymentResult`)
  with `StandardPaymentMethodService` (→ `IOperatorWalletService`) and `SolanaTokenPaymentMethodService`
  (→ `ISolanaService` + a per-call `SolanaTokenDescriptor`); `IPaymentMethodResolver.Resolve(parameters)`
  keys on the game's method. `GetAvailableMethods(operatorId)` drives the admin form (Standard iff a
  wallet provider is configured; SolanaToken iff ≥1 active `OperatorToken`).
- `Solnet` (`Solnet.Rpc`, `Solnet.Programs`, `Solnet.Wallet`) talks to a Solana cluster.
- `ISolanaService` / `SolanaService` validate bet payment signatures, transfer SPL tokens for
  rewards, and refund invalid bets **for `SolanaToken` games**, taking a `SolanaTokenDescriptor` per
  call (owner account built per call from the game's token). They write no ledger rows — the payment
  strategy does.
- **RPC endpoints (one global primary + optional fallback).** The RPC clients are owned by
  `ISolanaRpcClientProvider` / `SolanaRpcClientProvider` (a DI singleton), **not** built per operator:
  a **primary** `IRpcClient` from `GeneralSettings.Token.ClusterUrl` and, when a **distinct**
  `GeneralSettings.Token.FallbackClusterUrl` is set, a **fallback** `IRpcClient`. `IRpcClient` still
  resolves (it aliases the primary). Every RPC call in `SolanaService` runs through one
  `ExecuteRpcAsync` helper: a per-operation Polly retry policy (exponential backoff + jitter, treating
  HTTP 429/5xx/transport faults as retryable — counts in `GeneralSettings.Polly`) against the primary,
  then **exactly one** unretried attempt against the fallback if the primary's retries are exhausted,
  logged at Error. All four ops (`getLatestBlockhash`, `getTokenAccountsByOwner`, `sendTransaction`,
  `getTransaction`) fail over; re-submitting the already-signed `sendTransaction` bytes is safe (the
  cluster de-duplicates the identical signature). Both endpoints failing preserves today's behaviour
  (`OperatorWalletError.Unavailable` → `NeedsRefunding` → the 12h refund sweep). A blank or
  primary-equal `FallbackClusterUrl` disables failover. **`ClusterUrl` may carry the RPC provider's
  api-key in its query string, so it is a secret** — committed config keeps the keyless public devnet
  URL, deployed environments inject the real (e.g. Helius) URL via Secret Manager, and the provider
  **masks endpoints to scheme+host** before they reach any log. See `specs/solana-rpc-api-key.md`.
- **Frontend `/rpc` failover proxy (separate from the payout path above).** A thin stateless
  `POST /rpc` passthrough (`RpcController` on `SpaceStroker.Api`) that the **frontend** fails over to
  when the public RPC returns **HTTP 429**. It forwards a strict **allowlist of 7 read/send methods**
  to the same key-bearing `GeneralSettings.Token.ClusterUrl` (reused, no new secret) and returns the
  provider response verbatim. Gated by an **environment-independent Origin allowlist** (reuses
  `CorsOriginResolver`, plus localhost) + the **method allowlist** (403 otherwise) + a per-IP
  `AspNetCoreRateLimit` rule `*:/rpc`; **not** launch-token gated (so the wallet-bridge popup, which
  carries no token, works). Key never returned/logged (`MaskRpcUrl`). No entity/migration/consumer.
  This is unrelated to the backend's own payout RPC clients above. See `features/solana-connection-rpc.md`.
- **Standard payouts** go through `IOperatorWalletService` (a per-operator adapter; only a mock adapter
  ships today), resolved by `IOperatorWalletResolver` from `GeneralSettings.OperatorWallets`. The stake
  **debit** runs synchronously in `SpaceStroker.Api` at bet time; **credit** and **refund** run in
  `SpaceStroker.Consumers`. SolanaToken bet-time validation runs in the API (public token data only, no
  decrypt); its Credit/Refund transfers run in Consumers (decrypting the token key via `ISecretProtector`).
- **Secret protection:** `OperatorToken.TokenOwnerPrivateKey` is stored **AES-256-GCM** encrypted, the
  key derived (SHA-256) from the `GeneralSettings.SecretProtection.Key` passphrase shared by both hosts
  (`ISecretProtector` / `AesSecretProtector`). The private key is never returned by any DTO or logged.
- **Responsible-gaming pre-bet check:** an **opt-in**, per-operator `IPreBetCheckService` (resolved by
  `IPreBetCheckResolver` from `GeneralSettings.OperatorRgChecks`) runs in `RegisterBetHandler` after
  local validation and **before** settlement, letting the operator allow/deny a bet (self-exclusion,
  limits, KYC…). Unconfigured operators are skipped; denials return a specific message and a
  timeout/error is fail-closed. Applies to **both** payment methods; log-only, no migration. See
  `features/rg-pre-bet-hook.md`.
- **Money ledger:** `TransactionLog` records every movement (`Credit`/`Debit`/`Refund`) per bet, each
  carrying a `PaymentMethod` and an `ExternalTransactionReference` (the operator wallet ref for Standard,
  the on-chain signature for SolanaToken; the `Signature` column was dropped). A `SolanaToken` movement
  also writes a `CryptoTransactionLog` (wallet/signature/mint/raw amount/decimals). `BetDomainService`
  reads the ledger by type for reward/refund idempotency. There is **no SpaceStroker commission** — winners
  receive the full pot.

## 9. The game mechanic (the "Typer")

- `SpaceStrokerTyper` (`SpaceStroker.Domain/Games/Services`) is the heart of a running game.
- It reads the game's **committed `ServerSeed`** (generated + hash-committed on the first bet) and
  drives characters from a **portable HMAC-SHA256 counter keystream** over `ServerSeed ‖ gameId ‖
  ordered bet seeds ‖ counter` (rejection-sampled into the character pool — no `System.Random`), builds
  the set of bet choices and the character pool, then emits characters one at a time. When the rolling
  window equals a bet's choice, that choice wins (`HasWinners`, `FirstChoiceTyped`). The scheme is
  **provably fair**: the commitment is published before betting closes and the raw seed revealed after
  the round via `GET /games/{id}/verification`. That endpoint is deliberately cheap — it never replays
  the character stream (`winnerDecidedByFallback` is derived from the persisted `Game.CharactersTyped`)
  and caches a round's payload once rewards are settled (`GameVerification:{gameId}`, 12h, immutable so
  never invalidated), with the operator-ownership check kept **before** the cache read. Players verify
  in the app via the `FAIR` side-panel tab (Web Worker replay) or offline via `public/verify.html`. See
  `features/provably-fair.md` and `features/provably-fair-final.md`.
- Wall-clock pacing is time-budget driven (`GameSettings` targets in `spacestrokersettings.json`):
  `SpaceStrokerTyper` schedules ms-per-character from mode mean/p95 bounds and search-space horizon;
  Stopwatch delays keep duration hardware- and load-stable. Large configs may hit
  `MaxTyperCharacters` and resolve via a deterministic HMAC-keystream fallback winner. `DisplaySpeed`
  (1–3) ramps on elapsed-time fractions. The `GameService.RunGame` loop streams output over
  SignalR. See `features/typer-pacing-scalability.md`.
- **Fallback terminal reveal:** on a fallback-decided winner (the word was never naturally typed),
  `RunGame` emits `SpaceStrokerTyper.FirstChoiceTyped` as a final `…/Terminal` batch so the terminal
  ends with the winning sequence "typed" — matching a natural win. Display-only; the winner and seed
  are unchanged. This backs the client-side near-miss reveal (see `features/characters-away.md`).

## 10. Multi-tenancy: Operators

- `Operator` rows represent casino/betting platforms. `GameParameters.OperatorId` is a **required**
  FK — there is no house / operator-less mode. Operators carry a `LaunchPublicKey` (payment is now a
  **per-game** concern — the old operator-level `PaymentModel` is removed; see §8 and
  `features/payment-methods.md`). An operator may register one or more `OperatorToken`s.
- Players enter at **`/space-stroker/game/games#launch_token=<jwt>`** (the URL no longer carries the
  operator id — it is read from the token's `iss` claim). Operator + player identity arrive in a
  casino-signed **launch token** (`X-Launch-Token`, RS256), validated by
  `LaunchTokenValidationMiddleware` in `SpaceStroker.Api` on every operator-scoped REST call
  (`/games/*`, `/operators/*`, `/players/*`, `/fx/*`). The old `x-operator-id` header is **removed**; any
  token failure → generic **HTTP 401** (real reason logged server-side only). See
  `features/launch-token-validation.md`.
- Read handlers and bet creation scope data to the token's operator (only if `ContractIsActive`; the
  active-operator lookup is cached via `IOperatorService`). Cross-operator attempts are rejected with
  vague errors. Player identity is decided **per game `PaymentMethod`**: `Standard` = token `sub`;
  `SolanaToken` = the connected wallet in the body (+ signature).
- Expired, not-yet-started, or unknown operators are hidden behind generic "not found / unavailable"
  messaging (never "contract expired"). Branding is served by **`GET /operators/{id}`** only (no list;
  token-guarded; no longer carries a payment model).
- Mid-game contract expiry: the running game finishes; new bets and new games for that
  operator are blocked.
- SignalR hubs use game id only — no token validation on websockets.
- **Default production operator (Main):** `SpaceStroker.Consumers` seeds an operator named **`Main`**
  (fixed RFC-4122 GUID `22222222-2222-4222-8222-222222222222`, contract active, no end date) in **all**
  environments — in **production it is the only** seeded operator. Main is pre-provisioned with one
  encrypted `OperatorToken` from `GeneralSettings.Token` and **three SolanaToken** default game types
  (a 6-char alphanumeric race, a preset "horse race", a 24h lottery; free bets off). A
  **production-enabled** `POST /launch-token/default` (`LaunchController`, outside the middleware's
  protected prefixes, signed with Main's own launch key) mints an **anonymous** (empty `sub`) launch
  token **only** for Main; the SPA auto-mints one when a standalone visitor arrives with no launch
  token (never when embedded), so the public game works with no casino redirect, and a 401 on an
  auto-minted token silently re-mints. See `features/main-production-operator.md`.
- Non-prod **also** seeds **one** dev operator (fixed RFC-4122 GUID
  `11111111-1111-4111-8111-111111111111`) pre-provisioned for **both** methods — a mock wallet map
  (Standard) + one encrypted `OperatorToken` seeded from `GeneralSettings.Token` (SolanaToken) — with
  all default game types Standard plus 3 SolanaToken defaults; dev-only `POST /dev/launch-token` and
  `POST /dev/admin-token` mint player and admin tokens (no `?paymentModel`; disabled in production).
- **Operator admin portal** (`/space-stroker/admin`): operators manage their own game types **and register
  Solana tokens** via **`X-Admin-Token`** (admin-scoped RS256 JWT, same keypair as launch tokens,
  **not** contract-gated). Game types + tokens: create/list/deactivate only — no in-place edit;
  `GET /admin/payment-methods` reports availability. See `features/operator-admin-page.md` and
  `features/payment-methods.md`.
- See `specs/operator-feature.md` + `specs/launch-token-validation.md` for full design;
  `features/operators.md` and `features/launch-token-validation.md` for the distilled versions.

## 11. Frontend (`monkify-front`)

- **Stack:** Vue 3 (Options/Composition via `<script setup>`), TypeScript, Vue CLI (webpack),
  Tailwind CSS + DaisyUI, Vuex + vue-router, `@microsoft/signalr`, Solana wallet adapter
  (`solana-wallets-vue`, `@solana/web3.js`, `@solana/spl-token`), axios, `vue3-toastify`.
- **Structure** (`src/`):
  - `hooks/` — composables: `useGame` (game state, websockets, bet flow), `useConnection`
    (Solana wallet/token transfer), `useOperator` (load branding from route param, theming, `/error`
    redirect).
  - `services/` — `mainService.ts` (axios instance + endpoints), interceptors, handlers.
  - `types/` — shared TS types/enums (`ActiveGame`, `BodyGameBet`, `OperatorBranding`, …).
  - `utils/` — `operatorRoute.ts` (GUID validation, path-based operator id for the interceptor).
  - `pages/` — `GameSelectPage`, `GamePage`, `ErrorPage`. `components/` — UI, game, header,
    background. `constants/`, `assets/`.
- **Routing (param-free; operator from the token `iss`):** `/` redirects to `/space-stroker/game` → game
  select (`GameLobby`, the token-less entry point — its boot auto-mints a Main token, and an
  `#admin_token=<jwt>` fragment here hands off to the admin portal instead);
  `/space-stroker/game/play` → live game; `/space-stroker/game/history` + `/space-stroker/game/stats` →
  the header-menu pages; `/space-stroker/admin` → operator portal; `/space-stroker/wallet-bridge` → the
  same-origin wallet popup; `/space-stroker/background` → the backdrop-only page; `/error`. Operator
  context (token + display prefs) is booted **only** for `GameLobby`/`Game`/`GameHistory`/`GameStats`
  (`App.vue`) — the other routes carry none, so they cannot dead-end on `/error`.
- **Chromeless routes:** routes flagged `meta: { chromeless: true }` in `routes.ts` render without
  `GameHeader` — the wallet-bridge popup (whose header `RouterLink` would navigate the popup away) and
  `/space-stroker/background`, a chrome-free URL that shows **only** the animated backdrop (both
  `GameBackground` layers + `.section-vignette`, all global in `App.vue`) for embedding/screen-recording;
  its page component is intentionally empty. See `specs/background-page.md`.
- **API access:** `mainService` axios instance. A request interceptor sets `baseURL` from
  `VUE_APP_API_BASE_URL` and attaches **`X-Launch-Token`** (captured from the launch URL fragment by
  `useLaunch`, persisted per-tab in `sessionStorage`); any `401` routes to `/error`. Endpoints:
  `GET /games/active-types`, `GET /games/{id}`, `POST /games/{id}/bets`, `GET /operators/{id}`,
  `GET /players/preferences`, `PATCH /players/preferences`, `GET /fx/rates`,
  `GET /games/stats` (operator-wide global stats), `GET /games/{parametersId}/stats`,
  `GET /games/{parametersId}/history`, `GET /games/{id}/verification`.
- **Player customization:** a **menu button** in `GameHeader.vue` (gated on operator context derived
  from the launch token, surviving an operatorId reset on a hard reload) holds **Settings** (opens
  `PlayerCustomizationModal.vue` — display name + preferred currency dropdown, both for **all**
  operators), **Stats** (the Stats page), **Fair** (the fairness modal), **History** (the Games History
  page), and the dev-only **Admin** shortcut (Stats + Fair are hidden when embedded); audio
  (mute/volume) and the wallet cluster stay directly on the header. `useDisplayCurrency` loads FX rates
  + prefs on app mount and converts USD amounts client-side; the preferred currency affects **`Standard`
  games only** — `SolanaToken` games always render token-native. See
  `features/player-customization-modal.md`, `features/games-history.md`, + `features/payment-methods.md`.
- **Games history:** `GameHistoryPage.vue` at `/space-stroker/game/history` (route `GameHistory`,
  reached from the header menu) browses one game type's completed rounds via
  `GET /games/{parametersId}/history` — paginated, "mine only" toggle, per-row Verify link-out to
  `public/verify.html`. `App.vue` boots operator context (token + display prefs) for this route like
  the lobby/game. See `features/games-history.md`.
- **Wallet UX is per-game:** the header wallet cluster (`WalletButtons` + `TokenBalance`) shows only
  when the current game's `paymentMethod === SolanaToken` (using the game's `tokenSymbol` /
  `tokenMintAddress`), never for `Standard` or on the games list — driven by the game, not an
  operator flag.
- **Real-time:** `useGame` opens two SignalR hubs (`Games`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`, joins the game group, and renders the streamed terminal output,
  speed, status, incoming bets, and **game chat** (`GameChatPanel` + `useGameChat` on
  `{gameId}/ChatMessage`). See `features/game-chat.md`. On `/game` the right-docked, collapsible cabinet
  (`GameSidePanel`) is now **chat-only** — the STATS and FAIR tabs moved to the header menu
  (`fair-stats-menu`). Per-game-type **stats** render on the dedicated **Stats page**
  (`GameStatsPage.vue`, `/space-stroker/game/stats`): a global section via `GET /games/stats` plus a
  per-type section via `GET /games/{parametersId}/stats`. See `features/fair-stats-menu.md` +
  `features/game-type-stats.md`.
- **Bet flow:** `useGame.bet()` branches on `selectedGame.paymentMethod`. **`Standard`** posts
  `{ amount, choice, seed }` with no wallet/signature (identity via launch token `sub`); **`SolanaToken`**
  sends an on-chain SPL token transfer to the game's token via `useConnection.sendToken`, then POSTs
  the bet with the returned signature + wallet. Free bets (amount 0) skip both.

## 12. Local environment (docker-compose)

`monkify-backend/docker-compose.yml` brings up the full stack for development:

- `api` (port 6001 → 8080), `consumers` (port 6002 → 8080, hosts `/hangfire`),
- `database` (Postgres 16 — hosts both `spacestroker` and the MassTransit `spacestroker_messaging` DB), `redis` (7).
- **No `rabbitmq` service** — the MassTransit Postgres SQL transport uses the `database` container.
- All services share the `spacestroker-network` docker network.
- The API depends on the Consumers being healthy (so migrations/seeding — incl. the transport schema —
  run first).

> Connection strings and provider credentials are supplied via environment/config and **must not
> be read or edited** by the agent — see `constraints.md`.

## 13. Production deployment (GCP)

The production deployment is Infrastructure-as-Code + two local console tools under
`monkify-meta/deploy/` (spec `specs/terraform-gcp-deploy.md`, feature `features/terraform-gcp-deploy.md`).
Deploys are **manual** (no CI); everything is driven by an unversioned local `secrets.<env>.json`.

- **Compute:** `SpaceStroker.Api` and `SpaceStroker.Consumers` run as **Cloud Run** services (from images in
  **Artifact Registry**). The API autoscales (`min ≥ 1`) and is public behind the load balancer; Consumers
  is **internal-ingress only** (it hosts `/hangfire` and owns EF migrations/seeding), **CPU always-on**,
  pinned to `var.consumer_count` (default 1), and is released **before** the API.
- **Static sites:** `monkify-front` and `monkify-lp` are built and served from **GCS website buckets +
  Cloud CDN**; the front reproduces its `Content-Security-Policy: frame-ancestors *` as a CDN response
  header (no nginx in this path).
- **Data tier (private only):** **Cloud SQL Postgres 16** (one instance, both `spacestroker` and
  `spacestroker_messaging`) and **Memorystore Redis 7**, no public IPs; Cloud Run reaches them through a
  **Serverless VPC Access connector**, egress via **Cloud NAT**.
- **Edge:** one external **HTTPS load balancer** → **Cloud Armor** (per-IP rate limiting + OWASP rules) →
  api / app / lp, with **Google-managed TLS** and **Cloud DNS** for `spacestroker.com`.
- **Secrets:** the deploy tool syncs `secrets.<env>.json` into **Secret Manager**; Cloud Run reads them via
  IAM at boot. Each host has a **least-privilege service account**. No secret value enters Terraform state.
- **Health probes:** Cloud Run probes `GET /health` on both hosts (API via `HomeController`, Consumers via
  `MapHealthChecks`). The API trusts forwarded headers (`UseForwardedHeaders`) so HTTPS-redirect works
  behind the LB. Browser CORS origins are config-driven (`GeneralSettings:Cors:AllowedOrigins`).
- **Teardown keeps the domain:** Terraform is split into a **persistent** layer (DNS zone + domain + state
  bucket, `prevent_destroy`) and a **workload** layer; `teardown` destroys only the workload.
