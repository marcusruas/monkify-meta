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
                 │        SpaceStroker.Api             │◄─────────┘  (Hub: /Hubs/Games)
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

- HTTP request → **`AdminTokenValidationMiddleware`** (validates `X-Admin-Token`, sets
  `IAdminContext`; guards `/admin` **only** — it is the single authenticated surface, and every player
  endpoint is public) → **Controller** (`GamesController`, `PlayersController`, `AdminController`,
  `SolanaController`, `RpcController`, `HomeController`, dev-only `DevController`) → constructs a
  **MediatR request** → **handler** that extends `BaseRequestHandler` → **services** (`GameService`,
  `ISolanaService`) → **`SpaceStrokerDbContext`**.
- Controllers extend `BaseController`; responses are wrapped by `ProcessRequest(...)` into a
  standard `ApiResult<T>` shape (`{ data, messages }`). Validation failures bubble up via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- Handlers live under `SpaceStroker.Infrastructure/Handlers/<Area>/<UseCase>/` as a
  `Request` + `Handler` pair (e.g. `RegisterBet`, `GetActiveParameters`, `GetGameHistory`,
  `GetActiveGameForParameter`, `GetGameTypeStats`). Read handlers
  may also be backed by dedicated services — e.g. `GET /games/{parametersId}/stats` aggregates
  per-game-type all-time stats on demand via `IGameStatsService` (Redis-cached, no new table).
  See `features/game-type-stats.md`. `GET /games/{parametersId}/history` (paginated completed-round
  history for one game type) works the same way via `IGameHistoryService` — the public page is
  Redis-cached 5 min per type+page+size and the caller's own-bet columns are joined live.
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
  `GameParameters` (a "game type" — flat, belonging to no tenant), `LotteryParameters`, `PresetChoice`,
  `GameStatusLog`, **`Player`** (wallet-keyed identity + self-exclusion).
- **Bets area** (`SpaceStroker.Domain/Bets/Entities`): `Bet` (its `Seed` is a 64-char
  `HMAC(clientSeed, Nonce)` hex, with a per-bet **`Nonce`**, and a nullable indexed `Signature`),
  **`TransactionLog`** — THE ledger, one row per money movement (`Credit`/`Debit`/`Refund`) carrying a
  **nullable `BetId`** plus the on-chain columns (`Signature`, `RelatedSignature`, `Wallet`,
  `MintAddress`, `SenderAccount`, `RawTokenAmount`, `Decimals`) — and `BetStatusLog`.
  (`RefundLog` and then `CryptoTransactionLog` were both folded into `TransactionLog`.)
- All entities extend `TableEntity` (`Id: Guid`, `CreatedDate`, `UpdatedDate`).

Relationships: `GameParameters 1—* Game 1—* Bet 1—* TransactionLog` (the FK is **nullable** — a refund
for a stake whose bet was never persisted is a real ledger row with no bet); `Player 1—* Bet` (nullable
FK — anonymous free bets have no `Player`). There is **no** `Operator`, `OperatorToken`,
`OperatorPlayer` or `CryptoTransactionLog` table. See `features/remove-operators.md`.

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
| `RefundInvalidBetEvent` (carries `BetId` **or**, for a stake rejected before the bet was persisted, the payment signature + wallet) | `RegisterBetHandler`, `BetPlacedConsumer`, `GameTypeDeactivatedConsumer`, `PendingRefundSweepJob` (12h sweep of `NeedsRefunding`) | `RefundInvalidBetConsumer` | Refunds an invalid/rejected/flagged bet via `ISolanaService.RefundTokens` and writes the `Refund` ledger row. Clears the bet to `Refunded` on success (any path); skipped for free bets (`Value == 0`). See `features/refund-safeguards.md`. |
| `GameParametersCreatedEvent` | `CreateGameTypeHandler` | `GameParametersCreatedConsumer` | Opens the first game for a newly created game type (idempotent). |
| `GameTypeDeactivatedEvent` | `DeactivateGameTypeHandler` | `GameTypeDeactivatedConsumer` | Cancels `WaitingBets` games of a deactivated type (refunds paid bets, skips free). |
| `BetNotificationEvent` | `GameService.InsertBet` | `BetNotificationConsumer` | Pushes the new bet to subscribers (recent-bets feed). |

> The "monkey typing" loop and all reward/refund money movement happen in the **Consumers** host,
> not the API.

## 5. Caching: Redis

- `ICacheService` with two impls: `RedisCacheService` (when a Redis connection string is present)
  and `MemoryCacheService` (fallback). `StackExchange.Redis` `IConnectionMultiplexer` is a singleton.
- `CacheHelper` builds cache keys/TTLs. There is one deployment, so the lookup keys are **global**:
  `ActiveParameters` (12h) and the global-stats key (~120s) carry no tenant suffix (they were
  `ActiveParameters:{operatorId}` / `Operator:{operatorId}`). Anything that mutates game types
  invalidates the former.
- **Wallet-proof nonces:** `WalletNonce:{purpose}:{wallet}`, 5-minute TTL, single use — the challenge
  behind the signature-gated `PATCH /players/preferences` and `POST /players/self-exclusion`. The
  purpose segment is what stops a nonce minted for one flow being replayed against the other.
- Redis is also the **SignalR backplane** (`AddStackExchangeRedis`) so hub messages fan out across
  instances.

## 6. Real-time: SignalR

- **One hub**: `ActiveGamesHub` in `SpaceStroker.Infrastructure/Hubs`, mapped at `/Hubs/Games` in
  `SpaceStroker.Api/Program.cs`. (`RecentBetsHub` / `/Hubs/RecentBets` was removed by
  `unified-websocket-connection` — it carried no logic of its own and only ferried `…/Bets`.)
- Clients join a per-game group (`JoinGame`). During a run, the typer streams:
  - `…/Terminal` — batched characters,
  - `…/DisplaySpeed` — current typing speed,
  - `…/Status` — game status transitions (incl. winners on `Ended`),
  - `{gameId}/ChatMessage` — per-game chat (`SendChatMessage` on the same hub/group),
  - `{gameId}/Bets` — each new bet, published by `BetNotificationConsumer`,
  - and the `GameReceived` broadcast (`Clients.All`) announcing the next round.
- **A player holds one connection per browser tab.** It is opened lazily on the first game load and
  kept across round rollovers, game-type switches and trips back to the lobby — a round change swaps
  the group (`LeaveGame`/`JoinGame`) instead of reconnecting. See
  `specs/unified-websocket-connection.md`.
- JSON uses camelCase. Both the API and Consumers configure SignalR (Consumers emits during runs).
- **The client hangs up when nobody is watching.** The server keeps no idle policy of its own
  (`KeepAliveInterval` / `ClientTimeoutInterval` are untouched); instead the SPA proactively closes
  the connection after an idle period and refuses to reconnect until the player clicks, so abandoned
  tabs stop pinning Cloud Run instances. See the frontend section below and
  `specs/idle-websocket-disconnect.md`.

## 7. Scheduling: Hangfire

- Configured in `SpaceStroker.Consumers` with **PostgreSQL storage** (`UsePostgreSqlStorage`) in the
  **game** DB (`spacestroker`, `ConnectionStrings:SpaceStroker` — not the messaging DB), with a dashboard at
  `/hangfire`.
- Used to **schedule lottery games**: `GameService.ScheduleLottery` enqueues a delayed
  `BetPlacedEvent` publish for the game's start time. This is a lottery's **only** draw trigger —
  `RegisterBetHandler` publishes `BetPlacedEvent` only for **non-lottery** games — so moving a round's
  `StartDate` without rescheduling the job does nothing, and vice versa.
- **Anchored draw times.** A lottery type may carry `LotteryParameters.DrawTimeUtc`, a fixed UTC time
  of day. When set, `GameService.CreateGame` puts `StartDate` on the absolute grid
  `{ epoch + DrawTimeUtc + k·Frequency }` (via `LotterySchedule.NextDrawTime`) instead of
  `UtcNow + Frequency`, so the draw stops drifting when a round runs long; null keeps the rolling
  behaviour. At startup `SpaceStroker.Consumers` realigns open **anchored** rounds that are off-grid —
  updating `StartDate` **and** rescheduling the job — which also re-arms a draw missed during
  downtime. See `features/lottery-draw-time.md`.
- **Game chat purge:** daily recurring job `game-chat-purge` runs `ChatMessagePurgeJob`, deleting
  `GameChatMessage` rows older than `GeneralSettings.Chat.RetentionDays` (default 7). See
  `features/game-chat.md`.
- **Pending-refund sweep:** 12-hourly recurring job `pending-refund-sweep` (cron `0 */12 * * *`) runs
  `PendingRefundSweepJob`, publishing a `RefundInvalidBetEvent` for every bet in `NeedsRefunding` so the
  refund consumer pays it back and clears it to `Refunded` (failures retry next run). See
  `features/refund-safeguards.md`.

## 8. Blockchain & payments

- **One token, one settlement path.** Every game settles on-chain in the single token configured
  under **`GeneralSettings:Operator:Token`** (symbol, mint, sender account, owner keypair, decimals,
  commission). There is no per-game payment choice: the `PaymentMethod` enum, the off-chain `Standard`
  method, `IPaymentMethodService`/`IPaymentMethodResolver`, `IOperatorWalletService` and the
  registered-token entity are all deleted, and `ISolanaService` is called directly. Every amount in the
  system is token-native. See `features/remove-operators.md`.
- `Solnet` (`Solnet.Rpc`, `Solnet.Programs`, `Solnet.Wallet`) talks to a Solana cluster.
- `ISolanaService` / `SolanaService` validate bet payment signatures, transfer SPL tokens for rewards,
  and refund invalid bets, reading the token straight from config.
- **RPC endpoints (one global primary + optional fallback).** The RPC clients are owned by
  `ISolanaRpcClientProvider` / `SolanaRpcClientProvider` (a DI singleton): a **primary** `IRpcClient`
  from `GeneralSettings.Operator.Token.ClusterUrl` and, when a **distinct**
  `GeneralSettings.Operator.Token.FallbackClusterUrl` is set, a **fallback** `IRpcClient`. `IRpcClient` still
  resolves (it aliases the primary). Every RPC call in `SolanaService` runs through one
  `ExecuteRpcAsync` helper: a per-operation Polly retry policy (exponential backoff + jitter, treating
  HTTP 429/5xx/transport faults as retryable — counts in `GeneralSettings.Polly`) against the primary,
  then **exactly one** unretried attempt against the fallback if the primary's retries are exhausted,
  logged at Error. All four ops (`getLatestBlockhash`, `getTokenAccountsByOwner`, `sendTransaction`,
  `getTransaction`) fail over; re-submitting the already-signed `sendTransaction` bytes is safe (the
  cluster de-duplicates the identical signature). Both endpoints failing preserves today's behaviour
  (an `Unavailable` result → `NeedsRefunding` → the 12h refund sweep). A blank or
  primary-equal `FallbackClusterUrl` disables failover. **`ClusterUrl` may carry the RPC provider's
  api-key in its query string, so it is a secret** — committed config keeps the keyless public devnet
  URL, deployed environments inject the real (e.g. Helius) URL via Secret Manager, and the provider
  **masks endpoints to scheme+host** before they reach any log. See `specs/solana-rpc-api-key.md`.
- **Frontend `/rpc` failover proxy (separate from the payout path above).** A thin stateless
  `POST /rpc` passthrough (`RpcController` on `SpaceStroker.Api`) that is the **frontend's primary and
  only Solana transport** in any deployed environment (`VUE_APP_SOLANA_PROXY_URL`). It forwards a
  strict **allowlist of 3 methods** — `getLatestBlockhash`, `sendTransaction`, `getSignatureStatuses` —
  to the same key-bearing `GeneralSettings.Operator.Token.ClusterUrl` (reused, no new secret) and
  returns the provider response verbatim. Gated by an **environment-independent Origin allowlist**
  (`OriginAllowList`, reusing `CorsOriginResolver`, plus localhost) + the **method allowlist** (403
  otherwise) + a per-IP `AspNetCoreRateLimit` rule `*:/rpc`. Key never returned/logged (`MaskRpcUrl`). No
  entity/migration/consumer. Unrelated to the backend's own payout RPC clients above.
  *(It began as a 429-only failover behind a public primary — `features/solana-connection-rpc.md`;
  `frontend-helius-gateway` made it primary because a production browser cannot use the public mainnet
  endpoint at all.)*
- **The browser never calls a public Solana RPC in a deployed environment, and never holds the
  provider key.** Wallet-info reads go to **`GET /solana/balance`** (+ uncached `/solana/balance/live`)
  on `SpaceStroker.Api` — Redis-cached per `mint+wallet`, restricted to the **configured mint**,
  served through `ISolanaService.GetTokenBalance` (local ATA derivation, one
  `getTokenAccountBalance`, inheriting the retry + single-fallback + masking above). Confirmation is a
  **`getSignatureStatuses` poll**, not a WebSocket — a socket cannot be proxied. Locally/on devnet,
  with no proxy configured, the SPA still uses the public endpoint directly so no key is needed and no
  provider credits are spent. See `features/frontend-helius-gateway.md`.
- **Host split:** bet-time signature validation runs in `SpaceStroker.Api`; Credit/Refund transfers run
  in `SpaceStroker.Consumers`. The token owner private key is read from config by both — it is **not**
  encrypted at rest, because encrypting a config value with a key stored in the same config bought
  nothing once no user could type a private key into the admin portal (`ISecretProtector` is deleted).
  Deployed environments inject it via Secret Manager; the committed file carries a placeholder.
- **Responsible gaming:** the only control that runs is first-party **per-wallet self-exclusion**,
  checked in `RegisterBetHandler` before settlement and blocking paid *and* free bets. The opt-in
  per-operator RG pre-bet hook (`IPreBetCheckService` + resolver + `GeneralSettings.OperatorRgChecks`)
  was deleted with operators — it resolved off an `Operator` row and no adapter was ever configured.
  See `features/legal-docs-self-exclusion.md`.
- **Money ledger — one table.** `TransactionLog` records **every** movement: the stake placement as a
  `Debit`, the payout as a `Credit`, a returned stake as a `Refund`. Each row carries the on-chain
  detail directly (`Signature`, `RelatedSignature`, `Wallet`, `MintAddress`, `SenderAccount`,
  `RawTokenAmount`, `Decimals`) and a **nullable `BetId`**, so a refund for a stake whose bet was never
  persisted is an ordinary row rather than a special case. `CryptoTransactionLog` was merged in and
  dropped; `ExternalTransactionReference` is now just `Signature`. `BetDomainService` reads the ledger
  by type for reward/refund idempotency and never subtracts `Debit` rows, so a placement is never read
  as a payout. A house **commission** applies to winnings above each winner's own stake
  (`GeneralSettings.Operator.Token.CommissionPercentage`, default 0.5%) — see
  `features/crypto-commission.md`.

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
  never invalidated). Players verify
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

## 10. Tenancy: the deployment is the operator

There is **no multi-tenancy**. Space Stroker was built as a B2B platform where an `Operator` row owned
every game type, held an RS256 launch keypair, carried a contract window and branding, and owned the
Solana token — but only one operator ever existed, so the whole apparatus was deleted. Onboarding
another operator means **deploying the app again with a different `spacestrokersettings.json`**.

- **`GeneralSettings:Operator` is the operator.** It holds `Token` (the one settlement token),
  `AdminToken` (the RS256 keypair for the admin portal) and `DefaultGames` (the seeded game types).
  Top-level `Token`, `LaunchTokens`, `OperatorWallets`, `OperatorRgChecks`, `SecretProtection` and `Fx`
  no longer exist.
- **Every player endpoint is public.** `X-Launch-Token`, `LaunchTokenValidationMiddleware`,
  `ILaunchContext`, `LaunchController` and the SPA's mint/boot path are gone. `/games/*` and
  `/players/*` take no auth header at all. A paid bet still requires a confirmed on-chain signature, so
  it costs real money to place; free bets remain non-production-only.
- **Identity is the wallet** that signed the transfer, upserted into a single `Players` table. A free
  bet carries none and stays anonymous. Writes that only a wallet's owner may make — the display name
  and self-exclusion — are gated on an **ed25519 signature** over a purpose-scoped single-use nonce,
  because a wallet address is public.
- **Game types are seeded from config, create-only by name** (`SpaceStroker.Consumers` at startup): a
  type is inserted only when no row with that name exists and an existing row is never rewritten, so a
  restart cannot resurrect an admin-deactivated type or mutate one under a live round. The exception is
  the free-bet self-heal (`AllowFreeBet = !IsProduction()`).
- **Admin portal** (`/space-stroker/admin`): the one authenticated surface. `X-Admin-Token` is an
  admin-scoped RS256 JWT validated against the configured public key with **no `iss` and no contract
  check**; `POST /dev/admin-token` mints one outside production. Game types: create/list/deactivate,
  no in-place edit. See `features/admin-page.md`.
- **No branding indirection.** `OperatorsController`, `GET /operators/{id}` and the SPA's
  `useOperator`/`applyOperatorTheme` are gone; `--color-secondary` keeps its static value from
  `colors.css`.
- SignalR hubs use game id only — they never validated a token anyway.
- See `specs/remove-operators.md` for the full design and `features/remove-operators.md` for the
  distilled version. `specs/operator-feature.md`, `specs/launch-token-validation.md`,
  `specs/payment-methods-refactor.md`, `specs/operator-wallet-api.md`, `specs/rg-pre-bet-hook.md`,
  `specs/main-production-operator.md` and `specs/main-only-player-entry.md` remain as the design
  archive of what was removed.

## 11. Frontend (`monkify-front`)

- **Stack:** Vue 3 (Options/Composition via `<script setup>`), TypeScript, Vue CLI (webpack),
  Tailwind CSS + DaisyUI, Vuex + vue-router, `@microsoft/signalr`, Solana wallet adapter
  (`solana-wallets-vue`, `@solana/web3.js`, `@solana/spl-token`), axios, `vue3-toastify`.
- **Structure** (`src/`):
  - `hooks/` — composables: `useGame` (game state, websockets, bet flow), `useConnection`
    (Solana wallet/token transfer), `usePlayerPreferences` (nonce → sign → PATCH).
  - `services/` — `mainService.ts` (axios instance + endpoints), interceptors, handlers.
  - `types/` — shared TS types/enums (`ActiveGame`, `BodyGameBet`, …).
  - `pages/` — `GameSelectPage`, `GamePage`, `ErrorPage`. `components/` — UI, game, header,
    background. `constants/`, `assets/`.
- **Routing (param-free, unauthenticated):** `/` redirects to `/space-stroker/game` → game
  select (`GameLobby`, the entry point — an `#admin_token=<jwt>` fragment here hands off to the admin
  portal);
  `/space-stroker/game/play` → live game; `/space-stroker/game/history` + `/space-stroker/game/stats` →
  the header-menu pages; `/space-stroker/admin` → admin portal; `/space-stroker/wallet-bridge` → the
  same-origin wallet popup; `/space-stroker/background` → the backdrop-only page; `/error`. Player
  preferences are loaded **only** for `GameLobby`/`Game`/`GameHistory`/`GameStats` (`App.vue`), and only
  once a wallet is connected.
- **Chromeless routes:** routes flagged `meta: { chromeless: true }` in `routes.ts` render without
  `GameHeader` — the wallet-bridge popup (whose header `RouterLink` would navigate the popup away) and
  `/space-stroker/background`, a chrome-free URL that shows **only** the animated backdrop (both
  `GameBackground` layers + `.section-vignette`, all global in `App.vue`) for embedding/screen-recording;
  its page component is intentionally empty. See `specs/background-page.md`.
- **API access:** `mainService` axios instance. A request interceptor sets `baseURL` from
  `VUE_APP_API_BASE_URL` and attaches **no auth header** — there is none. (The blanket `401 → /error`
  went with the launch token; terminal failures still reach `/error` by their own paths.) Endpoints:
  `GET /games/active-types`, `GET /games/{id}`, `POST /games/{id}/bets`, `GET /players/nonce`,
  `GET /players/preferences`, `PATCH /players/preferences`, `POST /players/self-exclusion`,
  `GET /games/stats` (global stats), `GET /games/{parametersId}/stats`,
  `GET /games/{parametersId}/history`, `GET /games/{id}/verification`.
- **Player customization:** a **menu button** in `GameHeader.vue` holds **Settings** (opens
  `PlayerCustomizationModal.vue` — display name only), **Stats** (the Stats page), **Fair** (the
  fairness modal) and **History** (the Games History page); Stats + Fair are hidden when embedded.
  Audio (mute/volume) and the wallet cluster stay directly on the header. Saving the display name runs
  the wallet-proof flow (fetch nonce → sign the canonical message → PATCH), so the modal shows a
  "Connect wallet to customize" gate with no wallet. **Amounts are token-native everywhere** — there is
  no preferred display currency and no FX conversion. See `features/player-customization-modal.md`
  and `features/games-history.md`.
- **Games history:** `GameHistoryPage.vue` at `/space-stroker/game/history` (route `GameHistory`,
  reached from the header menu) browses one game type's completed rounds via
  `GET /games/{parametersId}/history` — paginated, "mine only" toggle (keyed on the connected wallet),
  per-row Verify link-out to `public/verify.html`. See `features/games-history.md`.
- **Wallet UX:** the header wallet cluster (`WalletButtons` + `TokenBalance`) uses the configured
  token's `tokenSymbol` / `tokenMintAddress`, published on the game-type payload. Every game is paid in
  that one token, so there is no per-game payment branch.
- **Real-time:** `useGameHub` (`src/hooks/useGameHub.ts`) owns **one** SignalR connection per tab to
  the `Games` hub at `VUE_APP_WEBSOCKET_BASE_URL` — opened lazily on the first game load and kept
  across rollovers, game switches and lobby round-trips, with each round joining/leaving its group and
  binding/unbinding `{gameId}/…` handlers over the same socket (`GameReceived` is bound once for the
  tab). `useGame` consumes it and renders the streamed terminal output,
  speed, status, incoming bets, and **game chat** (`GameChatPanel` + `useGameChat` on
  `{gameId}/ChatMessage`). See `features/game-chat.md`. On `/game` the right-docked, collapsible cabinet
  (`GameSidePanel`) is now **chat-only** — the STATS and FAIR tabs moved to the header menu
  (`fair-stats-menu`). Per-game-type **stats** render on the dedicated **Stats page**
  (`GameStatsPage.vue`, `/space-stroker/game/stats`): a global section via `GET /games/stats` plus a
  per-type section via `GET /games/{parametersId}/stats`. See `features/fair-stats-menu.md` +
  `features/game-type-stats.md`.
- **Idle disconnect (cost control):** `useIdleDisconnect` (a module singleton armed when `useGameHub`
  opens the connection and stopped when it closes it — the watch follows the socket, not the page,
  since the connection outlives `GamePage`; `GamePage` still arms it on mount so arriving never shows
  a stale overlay) watches two independent thresholds — **30 min** with the tab
  continuously `document.hidden` (`VUE_APP_IDLE_DISCONNECT_HIDDEN_MS`) and **60 min** with zero user
  input whether visible or not (`VUE_APP_IDLE_DISCONNECT_INACTIVE_MS`); whichever fires first wins,
  and a visible + interacting tab is never disconnected. Elapsed time is derived from **timestamps**
  re-evaluated on a 30s tick plus `visibilitychange`/`focus`/`pageshow` — never from a `setTimeout`
  callback, because background and mobile tabs throttle or freeze timers. On firing, `useGame` calls
  `useGameHub.stopForIdle()`, which stops the tab's connection and the watch with it, blocks `bet()`
  (a stale `gameId` must never be POSTed to), and emits the embedded `idle-disconnected` event. The
  connection also carries an `onreconnecting` idle check so `.withAutomaticReconnect()` cannot revive
  a frozen tab's dropped socket; an explicit game load, by contrast, reconnects and re-arms the watch
  (there is no idle gate on the connect path, or a lobby click after an idle stop would land on a dead
  screen). Recovery is explicit — a
  `GameIdleOverlay` **RECONNECT** button calls `resumeFromIdle()`, which re-runs the *full* join path
  (`updateNewGame(true)`) because the round has rolled over; there is no silent auto-resume. An
  in-flight mobile wallet hop is exempt from the hidden timer. Only the game **play** page is
  affected — no other route holds a websocket. See `specs/idle-websocket-disconnect.md`.
- **Analytics (both public sites, frontend-only):** the app and the `monkify-lp` landing page report to
  **one GA4 property** via `gtag.js` — they share the registrable domain `spacestroker.com`, so the
  `_ga` cookie is shared and the LP→app hop stays a **single session** (a bet keeps the campaign that
  brought the visitor). The tag loads only when a measurement id was baked in at build time
  (`VUE_APP_GA_MEASUREMENT_ID` / `PUBLIC_GA_MEASUREMENT_ID`, both from the existing `frontBuild`
  block); **empty ⇒ no script, no cookie, no banner**, which is how local, docker and the development
  environment stay out of the production property. Consent Mode v2 defaults to `denied` behind an
  Accept/Reject banner whose answer rides an `ss_consent` cookie on `.spacestroker.com` so both hosts
  honour one choice. `useAnalytics` is the app's sole `gtag` owner (manual `page_view` per route;
  events for CTA clicks, wallet connect, `game_type_selected`, and `bet_placed` as a **count** with no
  amounts, wallet or player identity). Analytics is **off entirely when embedded** and on the
  `chromeless` routes. No backend involvement. See `features/analytics.md`.
- **Bet flow:** `useGame.bet()` sends an on-chain SPL token transfer via `useConnection.sendToken`,
  then POSTs the bet with the returned signature + wallet. Free bets (amount 0) skip both and post
  `{ amount: 0, choice, seed }` anonymously.

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
  Cloud CDN**; the front carries `Content-Security-Policy: frame-ancestors *` **and `X-Robots-Tag:
  noindex`** as CDN response headers (no nginx in this path). The lp bucket carries neither.
  Uploads set **`Cache-Control` per file class** — HTML `no-cache`, content-hashed assets
  `immutable` for a year, everything else an explicit 1h — and **retain** the previous builds' hashed
  assets (pruned after 24h) so a session running an older build can still fetch a lazily-loaded chunk.
  See `features/terraform-gcp-deploy.md` → "Static-site upload".
- **Crawler policy (`specs/robots-txt.md`):** the three public hosts answer deliberately and differently.
  **`app.spacestroker.com`** and **`api.spacestroker.com`** are `Disallow: /` — the SPA is client-rendered
  (a crawler only ever sees an empty shell) and the API serves JSON, while bot traffic on either spends the
  same per-IP Cloud Armor throttle budget as a real player. The app serves the file from `public/robots.txt` and is
  additionally de-indexed by the `X-Robots-Tag` header above; the API serves it from a `HomeController`
  action (`GET /robots.txt`, `text/plain`, no launch token) because the API has **no static-file middleware
  and no wwwroot**. **`spacestroker.com` / `www.`** stays fully indexable (`Allow: /` + sitemap) and is where
  all SEO signal is concentrated, but denies **AI training** crawlers (`GPTBot`, `ClaudeBot`,
  `Google-Extended`, `meta-externalagent`, `CCBot`, `Bytespider`) while leaving **AI search/answer** crawlers
  (`OAI-SearchBot`, `PerplexityBot`, `Claude-SearchBot`, …) and **all link-preview agents**
  (`facebookexternalhit`, `Twitterbot`, `TelegramBot`) allowed — the latter generate the campaign unfurls.
  The **development environment is deliberately not covered**: its hosts are public but serve prod's
  committed `robots.txt` verbatim.
- **Data tier (private only):** **Cloud SQL Postgres 16** (one instance, both `spacestroker` and
  `spacestroker_messaging`) and **Memorystore Redis 7**, no public IPs; Cloud Run reaches them through a
  **Serverless VPC Access connector**, egress via **Cloud NAT**.
- **Edge:** one external **HTTPS load balancer** → **Cloud Armor** (per-IP **throttle** + OWASP rules) →
  api / app / lp, with **Google-managed TLS** and **Cloud DNS** for `spacestroker.com`. The per-IP rule
  **throttles at 600/60 s and never bans** — denying only the excess, so a burst costs a few 429s instead
  of a blackout. It banned for 300 s until 2026-07-27, which locked real players out; see
  `features/terraform-gcp-deploy.md` → Cloud Armor for why the ban must not be restored. A **second,
  looser rule at priority 800** gives `/health` + `/Hubs/*` their own 3000/60 s bucket (a throttle, not
  an exemption). ⚠️ **An Armor denial is emitted before the app runs and carries no CORS headers**, so
  the browser blocks it and the SPA sees a *network error with no status* — never a readable 429. Any
  client-side handling of rate limiting must key off "no response", not off `status === 429`; see
  `conventions/frontend.md` → Transient vs terminal failures.
- **Secrets:** the deploy tool syncs `secrets.<env>.json` into **Secret Manager**; Cloud Run reads them via
  IAM at boot. Each host has a **least-privilege service account**. No secret value enters Terraform state.
- **Health probes:** Cloud Run probes `GET /health` on both hosts (API via `HomeController`, Consumers via
  `MapHealthChecks`). The API trusts forwarded headers (`UseForwardedHeaders`) so HTTPS-redirect works
  behind the LB. Browser CORS origins are config-driven (`GeneralSettings:Cors:AllowedOrigins`).
- **Two rate limiters, one hard and one soft.** Cloud Armor at the edge (above) is the **enforceable**
  one. The **in-app** `AspNetCoreRateLimit` limiter is deliberately **soft**: its counters are
  in-process `MemoryCache` singletons (`SpaceStroker.Api/Program.cs`), so with `api_min_instances: 1`
  … `max: 10` the effective limit scales with instance count. **It must never be used to justify
  loosening Cloud Armor.** Its job is narrow — protect the **paid Helius RPC quota** — so only three
  rules exist (`*:/rpc` 240/min, `*:/solana/*` 120/min, `*:/solana/balance/live` 20/min) and
  `EndpointWhitelist` exempts `*:/health` + `*:/Hubs/*`. **Everything else, including
  `POST /games/{id}/bets`, is intentionally unlimited app-side**: rejecting a paid `SolanaToken` bet
  would strand tokens that already left the wallet with no `Bet` row and no auto-refund.
  See `conventions/backend.md` → "Middleware ordering and client-IP keying" for the two traps that
  made this limiter a no-op in production until 2026-07-28.
- **Observability (production only):** a `monitoring` module creates one dashboard
  (`spacestroker-prod-overview`), two uptime checks, an email notification channel, a spend **budget**
  (50/90/100%) and **two Cloud Armor log-based metrics** — one on `enforcedSecurityPolicy`, one on
  **`previewSecurityPolicy`** so the preview WAF's would-be blocks are visible. `enable_dashboards` is
  **derived** by the deploy tool from `environment == "production"`, and the module block carries
  `count`, so a development plan holds **no `module.monitoring` address at all**. Error/warning panels
  read the built-in `logging.googleapis.com/log_entry_count` — which works only because the app emits
  **structured JSON in Production** (plain-text stdout lands at severity `DEFAULT`, where an `Error` is
  indistinguishable from a `Warning`). **Cost is not charted**: Cloud Monitoring has no cost data
  source, so the dashboard links to Billing → Reports and the budget does the telling. See
  `features/terraform-gcp-deploy.md` → Observability.
- **Teardown keeps the domain:** Terraform is split into a **persistent** layer (DNS zone + domain + state
  bucket, `prevent_destroy`) and a **workload** layer; `teardown` destroys only the workload.
