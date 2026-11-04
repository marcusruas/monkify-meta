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
  **`GET /games/{gameTypeId}/holder-eligibility?wallet=`** (`GetHolderEligibility`, #196) is the Space
  Jackpot pre-check: it reads the wallet's $STROKE balance against the type's live holder round and
  returns `{ wallet, symbol, minimum, balance | null, eligible, entriesPaused }` — cached 15 s per
  round + wallet (a null balance is never cached), rate-limited 30/min
  (`get:/games/*/holder-eligibility`). It places nothing; `RegisterBetHandler` re-verifies everything.
  See `features/space-jackpot-refactor.md`.

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
  `GameStatusLog`, **`Player`** (wallet-keyed identity + self-exclusion; cosmetic `ProfileTheme` and, since
  Profile V2, `AvatarId` = the equipped Edson icon key, `null` = default). A **holder round** (Space
  Jackpot, #196) carries `Game.HolderMinimum` (N, frozen), `Game.HouseFundedPot` (frozen, house-funded)
  and `Game.HolderEntriesPaused`; its type is flagged by `LotteryParameters.HolderOnly`.
- **Bets area** (`SpaceStroker.Domain/Bets/Entities`): `Bet` (its `Seed` is a 64-char
  `HMAC(clientSeed, Nonce)` hex, with a per-bet **`Nonce`**, and a nullable indexed `Signature`),
  **`TransactionLog`** — THE ledger, one row per money movement (`Credit`/`Debit`/`Refund`) carrying a
  **nullable `BetId`** plus the on-chain columns (`Signature`, `RelatedSignature`, `Wallet`,
  `MintAddress`, `SenderAccount`, `RawTokenAmount`, `Decimals`) — and `BetStatusLog`.
  (`RefundLog` and then `CryptoTransactionLog` were both folded into `TransactionLog`.)
  `Bet.EntryKind` (`Paid = 1` / `Free = 2` / `Holder = 3`) says what an amount-0 bet is: a dev free bet
  or a Space Jackpot **holder ticket**. One holder ticket per wallet per round is enforced by the unique
  partial index `IX_GameBets_GameId_PlayerId_Holder`. `BetStatus.Voided` is a holder ticket struck out
  by the draw recheck.
- **Chat area** (`SpaceStroker.Domain/Chat/Entities`): `DirectMessage`, `DirectMessageReadState`,
  `ChatBlock`, `ChatMute`, `Donation`, and **`Notification`** (Profile V2, table `Notifications`: `Wallet`,
  `Type` (`NotificationTypes`: `donation` / `trophyUnlocked` / `refundIssued` / `jackpotWon`), `DedupKey`,
  JSON `Payload`, `ReadAt`; unique `(Wallet, Type, DedupKey)` + index `(Wallet, CreatedDate)`; migration
  `ProfileV2AvatarsAndNotifications`). See `features/chat.md`, `features/profile-v2.md`.
- **`PlayerIconPurchase`** (#212, `SpaceStroker.Domain/Players/Entities`, table `PlayerIconPurchases`):
  one row per $STROKE burn made to unlock a burn icon, keyed by wallet (not `Player.Id`): `Wallet`, `IconKey`,
  unique `BurnSignature`, `Amount`, `PriceAtPurchase`, `Memo`, `Status` (`Unlocked` 1 / `Unmatched` 2, int),
  `UnmatchedReason`; index `(Wallet, Status)`; migration `AddPlayerIconPurchases`. See
  `features/token-integration.md`.
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
| `BetPlacedEvent` | `RegisterBetHandler` (non-lottery), Hangfire (lottery), `BetPlacedConsumer` flow | `BetPlacedConsumer` | After a minimum wait & min-player check, tries to start the game, then publishes `RunGameEvent`. Refunds bets if a lottery lacks players. For a **holder round** it first runs `HolderDrawRecheck` (batched Token-2022 balance reads; tickets below N are `Voided`, unreadable ones kept) and an under-subscribed holder round refunds nothing. |
| `RunGameEvent` | `BetPlacedConsumer` | `RunGameConsumer` | Marks game `InProgress`, runs the typer, marks `Ended`, flags winners `NeedsRewarding`, creates the next game, publishes `RewardGameEvent`. |
| `RewardGameEvent` | `RunGameConsumer` | `RewardGameConsumer` | For each winner, calls `ISolanaService.TransferTokensForBet(...)`; updates statuses. Once the round is `RewardForWinnersCompleted` (both the no-winners and the winners exit, never on `ErrorWhenProcessingRewards`) it publishes `RoundSettledEvent` **last**; a publish failure is logged, never thrown. |
| `RoundSettledEvent(GameId)` (Profile V2) | `RewardGameConsumer` | `RoundSettledConsumer` — **its own receive endpoint**, off the payout path | Creates `trophyUnlocked` notifications for trophies whose **unlock round** is this round, and `jackpotWon` for each credited winner of a jackpot-mode round (`NotificationService`, deduped by the unique index). A failing player is logged and skipped. See `features/profile-v2.md`. |
| `RefundInvalidBetEvent` (carries `BetId` **or**, for a stake rejected before the bet was persisted, the payment signature + wallet) | `RegisterBetHandler`, `BetPlacedConsumer`, `GameTypeDeactivatedConsumer`, `PendingRefundSweepJob` (12h sweep of `NeedsRefunding`) | `RefundInvalidBetConsumer` | Refunds an invalid/rejected/flagged bet via `ISolanaService.RefundTokens` and writes the `Refund` ledger row. Clears the bet to `Refunded` on success (any path); skipped for free bets (`Value == 0`). After a successful refund of a persisted bet it writes a `refundIssued` notification (paid bets with `Amount > 0` only) and re-validates the player's equipped trophy icon (`PlayerAvatarGuard`); neither can fail the refund. See `features/refund-safeguards.md`. |
| `GameParametersCreatedEvent` | `CreateGameTypeHandler` | `GameParametersCreatedConsumer` | Opens the first game for a newly created game type (idempotent). |
| `GameTypeDeactivatedEvent` | `DeactivateGameTypeHandler` | `GameTypeDeactivatedConsumer` | Cancels `WaitingBets` games of a deactivated type (refunds paid bets, skips free). |
| `BetNotificationEvent` | `GameService.InsertBet` | `BetNotificationConsumer` | Pushes the new bet to subscribers (recent-bets feed). |
| `GameOpenedEvent` (ids only) | `GameService.CreateGame` (all four creation paths), `UpdateOrbitSettingsHandler` (non-solo types only) | `OrbitPlacementPlannerConsumer` **and** `HouseSeedBetConsumer` — **separate receive endpoints**, so a slow house placement never delays Orbit planning for the same round | Orbit decides who auto-bets (§8c). The house seeder places **one ordinary paid bet** into the round, **production only** (§8e, `features/house-seed-bet.md`). |
| `OrbitSoloRoundDueEvent(PlayerId)` | `UpdateOrbitSettingsHandler` (enable / save on a solo type), `OrbitDepositCreditedConsumer` (solo enrolment), `OrbitSoloAutoBetService` via Hangfire (`IOrbitSoloScheduler`, after the gap), `OrbitSoloAutoBetSweepJob` (every minute), Consumers startup (`KickOrbitSoloLoops`) | `OrbitSoloRoundConsumer` — **its own receive endpoint** | One idempotent, lease-guarded tick of Orbit's **solo loop** (Hyper Roll / VS HOUSE duel): open the player's round, stake it, or wait (§8c, `features/auto-bet-duel-hyper-roll.md`). |

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
  behind the signature-gated `PATCH /players/preferences` and `POST /players/self-exclusion` (and,
  since #213, `POST /players/partner-code` under its own `partner-code` purpose). The
  purpose segment is what stops a nonce minted for one flow being replayed against the other.
- **Player profile reads (#186):** `PlayerProfile:{wallet}` (60 s; only wallets with a `Player` row; name,
  theme, icon and self-exclusion read live on every request; dropped on an icon save and when a round
  unlocks a trophy) and `PlayerLevel:{wallet}` (60 s, `{ Level, Theme, AvatarId }`, dropped on a theme or
  icon save; the staff-icon check runs after the cache). `TrophiesComplete:{wallet}` (1 h) lets
  `RoundSettledConsumer` skip players who already hold all 10 trophies.
- **Staff icon lists** (Profile V2) are plain config, not secrets: `GeneralSettings.PlayerIcons.{Elder,Phantom,Beanie}.Wallets`
  (committed empty; env `GeneralSettings__PlayerIcons__Elder__Wallets__0`), read live by `PlayerIconRules`.
- Redis is also the **SignalR backplane** (`AddStackExchangeRedis`) so hub messages fan out across
  instances.

## 6. Real-time: SignalR

- **One hub**: `ActiveGamesHub` in `SpaceStroker.Infrastructure/Hubs`, mapped at `/Hubs/Games` in
  `SpaceStroker.Api/Program.cs`. (`RecentBetsHub` / `/Hubs/RecentBets` was removed by
  `unified-websocket-connection` — it carried no logic of its own and only ferried `…/Bets`.)
- Clients join a per-game group (`JoinGame`). During a run, the typer streams:
  - `…/Terminal` — batched characters,
  - `…/DisplaySpeed` — current typing speed,
  - `…/Status` — game status transitions (incl. winners on `Ended`, and — since 2026-08-24 — a
    `result` on `RewardForWinnersCompleted` too, carrying `netRewardPerWinner`: the net a single **paid**
    winner received, captured from the payout loop by `RewardGameConsumer` and null when nothing was
    paid. Built in `SpaceStroker.Consumers`; the API gains nothing),
  - `{gameId}/Bets` — each new bet, published by `BetNotificationConsumer`,
  - and the `GameReceived` broadcast (`Clients.All`) announcing the next round.
- **Chat is ONE app-wide room plus per-wallet DM groups, not tied to any round or game type** (since
  2026-10-22, `specs/chat-revamp.md`): clients `JoinGlobalChat()` (anonymous) the group `chat:global`
  and receive `ChatMessage` / `ChatMessageDeleted` / `ChatOnlineCount`. Sending needs a wallet-signed
  **chat session** (`POST /chat/session`, token prefixed `chat:`): `AuthenticateChat(token)` joins the
  server-derived group `dm:{wallet}` (`DirectMessage`, `DirectMessagesRead`, `DonationReceived`,
  `DonationsRead`, and since Profile V2 `NotificationReceived` / `NotificationsRead` — pushed from either
  host through the Redis backplane); `SendGlobalMessage` / `SendDirectMessage` resolve the sender server-side via
  `IChatService`, which rate-limits per wallet (Redis, 1/s + 20/min), appends global messages to
  `ChatHistory:global` and publishes `ChatMessagePersistedEvent`; DMs are written synchronously before
  delivery. `GET /chat` serves the last 100 (Redis, DB fallback); DM/donation/notification/block routes
  live on `ChatController` (`X-Chat-Session`; `GET /chat/notifications`, `POST /chat/notifications/read`). `ChatPresenceBroadcaster` (API host only) pushes the approximate
  online count every 30s from the Redis sorted set `ChatOnline`. See `features/chat.md`.
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
- **Holder rounds are planned at creation (#196).** For a `LotteryParameters.HolderOnly` type,
  `GameService.CreateGame` asks **`IHolderJackpotRoundPlanner`** for the round's N and pot before the
  insert: N from the Jupiter price (below, §8), the pot from the treasury's headroom **under the shared
  house-edge advisory lock** (`HouseEdgeCoverageService.AcquireLockAsync` + `EvaluateHeadroomAsync`),
  with the round insert joining the lock's transaction. `CarriedExtra` is forced to 0, and creation is
  **skipped** when the type already has a live round (checked under the lock). ⚠️ The planner is an
  **optional** `GameService` constructor dependency registered in **Consumers only** — the API host
  builds `GameService` without it, and a holder round opened there opens **PAUSED**. Every planning
  failure also opens the round PAUSED rather than throwing. See `features/space-jackpot-refactor.md`.
- **Chat purge:** daily recurring job `game-chat-purge` runs `ChatMessagePurgeJob`, deleting
  `GameChatMessage` rows older than `GeneralSettings.Chat.RetentionDays` (class default 7; committed **2**),
  `DirectMessages` and `DirectMessageReadStates` older than `Chat.DirectMessageRetentionDays` (30), and
  expired `ChatMutes`. Global rows are also the fallback source of the global history, and the Redis
  history list expires on the same retention. Donations are not purged. See `features/chat.md`.
- **Pending-refund sweep:** 12-hourly recurring job `pending-refund-sweep` (cron `0 */12 * * *`) runs
  `PendingRefundSweepJob`, publishing a `RefundInvalidBetEvent` for every bet in `NeedsRefunding` so the
  refund consumer pays it back and clears it to `Refunded` (failures retry next run). See
  `features/refund-safeguards.md`.
- **Orbit stake reconciliation:** every-minute recurring job `orbit-stake-reconciliation` runs
  `OrbitStakeReconciliationJob` (`[DisableConcurrentExecution]`, no automatic retry, 45 s budget),
  deciding Orbit placement claims left open by an in-doubt transfer against the chain. See §8c and
  `features/orbit-wallet.md`.
- **Orbit solo auto-bet sweep (#200):** every-minute recurring job `orbit-solo-autobet-sweep` runs
  `OrbitSoloAutoBetSweepJob` — reads only, publishes `OrbitSoloRoundDueEvent` for every Enabled solo
  enrolment that is due (nothing in flight and the gap elapsed, or its round gone / finished), and logs at
  Error (rate-limited hourly) a loop held by an errored payout or a round stuck unsettled > 10 min. Never
  shortens the gap. See §8c and `features/auto-bet-duel-hyper-roll.md`.
- **House-edge board cleanup:** recurring job `house-edge-board-cleanup` (every
  `GeneralSettings.Games.HouseEdgeBoardCleanupIntervalMinutes`, default 5) runs `HouseEdgeBoardCleanupJob`,
  deleting unbet Hyper Roll solo boards past `BoardExpiresAt` (30 min idle). House-edge rounds have no
  automatic rollover, so the next board is opened by the player (`POST /games/{gameTypeId}/boards`). See
  `features/hyper-roll.md`. Since #184 the same job also sweeps **Monkey Duel** shells
  (`IsHouseEdge || DuelKind != null`) — only unowned, unbet rounds; a posted duel room never expires.
- **Key Mines inactivity sweep:** recurring job `key-mines-inactivity-sweep` (every
  `GeneralSettings.Games.KeyMinesSweepIntervalMinutes`, default 1) runs `KeyMinesInactivitySweepJob`: a KEYS
  round idle for `KeyMinesInactivityMinutes` (5) auto-cashes at its step (≥ 1 press) or is refunded (0 presses),
  and rounds stuck `Ended` are re-driven to `RewardGameConsumer`. Key Mines rounds are excluded from
  `CloseOngoingGames`, so they survive a Consumers restart. See `features/key-mines.md`.
- **Monkey Duel house side:** no consumer and no house bet since `specs/duel-treasury-house.md` — the
  creator's bet draws the house word into `Game.HouseWord` inside `RegisterBetHandler` (duel lock →
  shared treasury lock), and the treasury pays a winner. The duel word pool self-heals at startup via
  `DuelWordPoolReconciler` (before `OpenNewGames`). See `features/monkey-duel.md`.

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
- **$STROKE holder balances are Token-2022 reads (#196).** $STROKE is a **Token-2022** mint
  (`GeneralSettings:Operator:Token:HolderToken` → `HolderTokenSettings`; public, committed), and Solnet's
  helpers hardcode the classic program — a classic ATA derivation reads a real balance as **0**. So the
  holder lottery reads through `ISolanaService.GetToken2022Balance` (one `getTokenAccountBalance` on the
  `Token2022Program`-derived ATA) and `GetToken2022Balances` (`getMultipleAccounts`, chunks of 100),
  both behind **`IHolderBalanceReader`** (registered in **both** hosts: the API reads at entry and for
  the eligibility endpoint, Consumers rechecks at the draw). A missing account is 0; a failed read is
  `null`. ⚠️ Never read $STROKE through `GetTokenBalance`. The token is **never transferred** by the
  backend any more — the winner bonus that did (`JackpotRewardService`) is deleted.
- **Jupiter price — the one outbound price call (#196).** `IStrokePriceClient` /
  `JupiterPriceClient` does `GET https://lite-api.jup.ag/price/v3?ids=<mint>` (keyless; named
  `HttpClient`, `PriceTimeoutSeconds`) and returns the USD price or `null` — it never throws. It is
  registered in **Consumers only** and called once per holder round opened (about once a day) to size
  the holder minimum N. A failure keeps the previous N and logs at Error.
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
  (`GeneralSettings.Operator.Token.CommissionPercentage`, **1%** since 2026-08-04; was 0.5%) — see
  `features/crypto-commission.md`. The committed value is the production rate and is read at payout
  time, so changing it takes a redeploy of **both** hosts and no in-flight paid round.

## 8b. Custody: the Orbit wallet

> Shipped on `main` 2026-08-03 with the Orbit release train — see `features/orbit-custodial-wallet.md`.

The first place the platform holds player money rather than passing it straight through.

- A **second house keypair** (`GeneralSettings:Operator:Token:Orbit`) holds every player's deposited
  SOL in one on-chain account, kept **separate from the bet/payout keypair** so "what we owe
  players" is checkable against "what the account holds", and so an Orbit-funded transfer is never a
  same-account no-op.
- Balances live in `OrbitBalance` and are authoritative **only** as a cache: the truth is
  `SUM(OrbitTransaction.Amount)` for the same **pool** `(OrbitAccount, MintAddress)` and player, over
  all rows regardless of status. Money is never corrected by editing a row — the opposite row is
  appended.
- The **API only reserves**; every transfer happens in `SpaceStroker.Consumers` (`constraints.md` §8).
  A cash-out writes a `Pending` row and takes an atomic conditional debit in one DB transaction, then
  publishes; the consumer claims the row with `SET Executing WHERE Status = Pending`, so at-least-once
  bus delivery cannot pay twice.
- Two new Hangfire jobs join the schedule (§7): an **hourly** cash-out sweep that re-drives stuck
  `Pending` rows and never touches `Executing` ones, and a **6-hourly** reconciliation that compares
  each player's ledger to their column and each pool's total to its on-chain balance, logging only.
- Eleven **public** `/orbit/*` endpoints (five here, the three Telegram routes #123 added,
  `GET /orbit/history` from #150, and `POST /orbit/wagers` + `GET /orbit/wagers/{requestId}` from #194),
  authorized by a stateless HMAC session for reads and a fresh purpose-scoped wallet signature for
  cash-outs. ⚠️ **Manual bets (`POST /orbit/wagers`) are the one money-moving call authorized by the
  session alone** (#194 decision 2): the stake can only become a bet whose win returns to the same balance.
- ⚠️ **The ledger's self-healing read takes one `REPEATABLE READ` snapshot** for both the ledger sum
  and the materialized column, and its correction is guarded on the column still holding the value it
  read. Two independent `READ COMMITTED` reads could straddle a commit and heal the column to a
  mid-flight total — see `features/orbit-feedback-adjustments.md`.
- **`OrbitChoiceClaim`** (unique `(GameParametersId, Choice)`) reserves an Orbit player's fixed word
  across rounds. ⚠️ It is read from `RegisterBetHandler` on **every free-text bet** — the money path —
  so it must stay a cheap indexed read.

## 8c. The Orbit auto-bet engine

> Shipped on `main` 2026-08-03 with the Orbit release train — see `features/orbit-auto-bet-engine.md`.

What spends the custody above: the house bets on an enrolled player's behalf, once per round.

- **A two-stage bus fan-out, never an inline call.** Round creation must not block on N on-chain
  transfers, and the decision would otherwise be duplicated at each of the four creation paths.
  Stage 1 (`OrbitPlacementPlannerConsumer`) decides **once** who bets and arms the gate; stage 2 is
  **one `PlaceOrbitBetEvent` per player**, so MassTransit places concurrently and retries each
  independently — one player's RPC 429 never re-drives the players who already succeeded.
- **Three things trigger stage 1**: a round opening (`GameOpenedEvent`), a human bet that makes an
  unviable round viable (`BetPlacedConsumer`), and a **deposit being credited**
  (`OrbitDepositCreditedConsumer`), so money arriving mid-round joins the round that is already open.
  All three call the same `IOrbitBetPlacementService`, so they cannot disagree.
- ⚠️ **`GameOpenedEvent` is ids only, and is not `GameCreatedEvent`.** The latter is the SignalR
  payload and carries the whole `GameParameters` entity, whose navigation properties cycle —
  MassTransit's serializer throws on it, and it only *appears* to work for a type with a shallow
  graph. Ids also mean a consumer can never act on a stale snapshot.
- **A start gate on `Game`** (`ExpectedOrbitBets`, `OrbitGateExpiresAt`) holds the round until the
  expected Orbit bets land — armed **only when Orbit is load-bearing**, so a paying human never waits
  on a bot. Two columns rather than a Redis key, whose flush would silently disarm the gate and start
  a round without bets players were already debited for.
- **The `BetDebit` ledger row is the placement claim.** `OrbitTransaction.GameId` plus a unique index
  across the pool makes it so; the claim and the atomic debit are the **same write, one transaction,
  before the transfer is signed**, so a redelivery hits `23505` having moved nothing.
- The **`ServerSeed` now commits at round creation**, not on the first bet — creation-time placement
  does not satisfy "first bet", and one round must not be committed at two different lifecycle points
  depending on who happened to be enrolled.
- Wins and refunds on an Orbit-funded bet are routed to the **Orbit account**, never the player's
  personal wallet, each with its own ledger row. All of it runs in `SpaceStroker.Consumers`.

### The solo loop (#200, 2026-10-06)

A **second** engine for **solo types** (`IsHouseEdge || IsDuel`: Hyper Roll, Monkey Duel VS HOUSE), which
have no shared round for the planner above to fill. The planner, `IsOrbitEligible`,
`PlaceOrbitBetConsumer` and `BetPlacedConsumer` all **refuse solo rounds**. See
`features/auto-bet-duel-hyper-roll.md`.

- **Trigger → tick:** `OrbitSoloRoundDueEvent(playerId)` → `OrbitSoloRoundConsumer` (own endpoint) →
  `OrbitSoloAutoBetService.RunOnce`. Eligibility → in-flight check (`OrbitSoloRoundState`: every
  unsettled status blocks; `ErrorWhenProcessingRewards` blocks until an operator resolves it) → gap →
  **lease** (`OrbitEnrollment.SoloLeaseUntil`, a token, taken **before** any round exists) → funds (settled
  **and** spendable cover the stake; stop-loss floor) → house pre-check (`HouseEdgeEnabled` + coverage at
  the worst multiplier, no money yet) → `GameService.OpenSoloBoard` / `OpenDuelRoom(House)` → record
  `SoloRoundId` + push `OrbitAutoRound` → the **unchanged** `OrbitStakePlacer`.
- **Enrolment writes go only through `IOrbitSoloLoopStore`** (conditional `ExecuteUpdate`; the context is
  NoTracking). The loop never writes a ledger row.
- **Next tick:** `RewardGameConsumer` (both paths, after `EvaluateOrbitFloors`) and
  `RefundInvalidBetConsumer` call `OnAutoRoundSettled` → `NextSoloRoundAt = now + SoloRoundGapSeconds` and a
  Hangfire-scheduled due event (`HangfireOrbitSoloScheduler`, the `ScheduleOrbitGateExpiry` shape). The
  minute sweep and the Consumers startup kick recover lost schedules. ⚠️ Hangfire's Postgres storage polls
  scheduled jobs ~every 15 s, so the real round-to-round time is ~10–30 s, not the configured 5 s (accepted
  at ship).
- **House refuses** (maintenance switch, coverage, no board): the enrolment stays Enabled,
  `WaitingForHouseSince` is set, nothing moves, and the loop retries after the gap.

### Manual Orbit wagers (#194, 2026-09-24)

A second spender of the custody balance: the player, by hand, on any paid type. See
`features/orbit-wallet.md`.

- **API:** `POST /orbit/wagers` (session-only, rate-limited 30/min) runs cheap pre-checks, stores a
  `Pending` result in Redis (`OrbitWager:{playerId}:{requestId}`, 15 min) and publishes
  `PlaceManualOrbitBetEvent`. It never signs or debits. `GET /orbit/wagers/{requestId}` (120/min) reads the
  result, with the ledger authoritative for `Placed`.
- **Consumers:** `PlaceManualOrbitBetConsumer`, on **its own receive endpoint** so a burst of manual bets
  never delays the auto engine. Both it and `PlaceOrbitBetConsumer` call the shared `OrbitStakePlacer`:
  claim + debit (one transaction) → stamp signature + `LastValidBlockHeight` on the claim → house-signed
  custody → treasury transfer (`TransferStakeWithMemo`, tri-state `NotSent` / `Confirmed` / `Unknown`) →
  `RegisterBetHandler` with `FundedByOrbit` and the claim id, which links the claim inside the bet insert.
- **`OrbitStakeReconciliationJob`** (Hangfire `orbit-stake-reconciliation`, every minute, no concurrent
  runs, 45 s budget) decides claims left open by an unknown transfer outcome against the chain (primary
  RPC, finalized only): ledger credit once, or an on-chain return to custody once.

## 8d. Telling the player: the Orbit notification channel and Telegram

> Shipped on `main` 2026-08-03 with the Orbit release train — see `features/orbit-notifications.md`.

- **A private per-player group on the existing `ActiveGamesHub`.** `JoinOrbitNotifications(token)`
  validates the shipped Orbit session and derives the group name **server-side** from the token's
  `PlayerId` (`OrbitNotificationChannel.GroupFor`) — it is never accepted from the caller, because a
  wallet address is public on every bet list and a wallet-keyed group would leak both the win amounts
  and the fact that the player uses Orbit at all. No new socket: it reuses the one tab-scoped
  connection `unified-websocket-connection` established, and dies with the idle disconnect.
- **`OrbitBetSettlementService` publishes `OrbitWinNotificationEvent`** (ids and primitives only)
  after the credit commits and after the threshold evaluation, so the win toast and any resulting
  cash-out message arrive in the order the money moved. `OrbitNotificationConsumer` pushes it to the
  group over the **Redis backplane**, so a message sent from `SpaceStroker.Consumers` reaches a
  browser attached to the **API** — the same shape as `BetNotificationConsumer`. The payload carries
  **no balance**: the threshold cash-out fires the instant the credit lands, so the client re-reads.
- **`OrbitAutoRound`** (#200, `GameSettings.OrbitAutoRoundEndpoint`) rides the same group: the solo loop
  pushes `{ gameTypeId, roundId, waitingForHouse }` (camelCase JSON string) from Consumers through
  `IHubContext<ActiveGamesHub>` when it opens an auto round (before placing, so the game screen can attach)
  and when it enters "waiting for the house". Never sent to Telegram.
- **`OrbitRunFinished`** (#208, `GameSettings.OrbitRunFinishedEndpoint`) rides the same group the same way:
  `OrbitRunService` pushes `{ gameTypeId, gameTypeName, betsPlaced }` from Consumers when a run reaches its
  bet limit and the enrolment is paused. Never sent to Telegram (a pause moves no money).
- **Telegram (`ITelegramNotifier`) is an outbound-only integration with no poller and no webhook.**
  Linking is a `t.me/<bot>?start=<code>` deep link plus a VERIFY press making **one** `getUpdates`
  call; the link code and the read offset live in **Redis**, and the offset advances only on a match.
  Sends happen exclusively in `SpaceStroker.Consumers`, on completed cash-outs, and can never fail a
  money path. It is **inert without a bot token** (a `MOCK_`-prefixed token counts as unconfigured),
  which is how local, dev and the test suites run.
- ⚠️ **`SpaceStroker.Consumers`' startup work moved to `ApplicationStarted`.** It used to run before
  `app.Run()`, while the MassTransit transport database and its `transport` schema are provisioned by
  a hosted service that only starts **at** `app.Run()` — so on a cold boot the first rounds opened
  with their `GameOpenedEvent` publish failing, and Orbit never planned them. Silent on every warm
  boot; it only bites a fresh volume or a brand-new environment's first start.

## 8e. The house seed bet

> Shipped on `main` 2026-08-07 — see `features/house-seed-bet.md`. Replaces `SpaceStroker.Seeder`,
> the manual 6-hourly console app, which is deleted.

Every newly created round opens with **one paid bet already on it**, from a dedicated house wallet, on
all three game types — **production only**.

- **It is an ORDINARY bet, and that is the architecture.** No marker, no column, no migration, no
  payment-validation bypass, no payout redirect, no branch anywhere. It works because the house wallet
  is **both the transfer's sender and the bet's own `Wallet`**, so `ValidateBetPayment` — which matches
  the sender on `bet.GetWalletAddress()` and the recipient on `token.TokenOwnerPublicKey` — passes
  untouched. Contrast §8c: Orbit's *custody account* signs for a **different** player's wallet, which
  is the entire reason `Bet.FundedByOrbit` exists. ⚠️ **That flag must never be set here** — it would
  discard a validation that genuinely passes and route a win into player custody.
- **The gate is in the consumer; every decision is in the service.** `HouseSeedBetConsumer` does the
  hard `IsProduction()` check and nothing else, so `IHouseSeedBetService` stays drivable by the
  Testcontainers harness, which pins every host to `Development` across a shared fixture. There is
  **no config enable-flag** — turning seeding off is a code change and a deploy.
- **One consumer, its own endpoint, one bet.** It shares `GameOpenedEvent` with the Orbit planner but
  never its endpoint, because it sleeps out a randomised placement delay. The delay is clamped strictly
  inside `MinimumWaitPeriodForBets`, so it costs no start latency, and the round is re-checked for
  `GameAcceptingBets` **before** signing. Idempotent on "a bet from the house player already exists on
  this round", so a redelivered event cannot spend a second stake.
- **It counts as a distinct player**, via `BetPlacedConsumer`'s existing distinct-`PlayerId` count — so
  a **single real player is now enough to start a round**. Nothing in the start logic changed.
- **The choice is derived, not picked** — `OrbitChoiceGenerator.DeriveChoice` over the committed
  `ServerSeed`, so nothing is chosen after the commitment. Free-text candidates must clear both an
  existing bet and an `OrbitChoiceClaim`.
- ⚠️ **Two hard refusals, both fail-closed:** a house wallet equal to the treasury (a same-ATA transfer
  is an on-chain no-op — the shipped production incident) or to the Orbit account (player custody
  money) disables seeding with a `Fatal` log **without aborting the host**; and a game type with
  `MinimumNumberOfPlayers < 2` is never seeded, because the typer keystream mixes in every bet's seed
  and the house must not be able to run a round where it controls every input.

## 8f. Streamer wallets: the play-money ledger (#201, 2026-10-07)

- **A second, off-chain ledger** beside `TransactionLog` and `OrbitTransaction`: `StreamerWallet`
  (materialised `Balance`) + append-only `StreamerTransaction` (`Seed`/`TopUp`/`BetDebit`/`WinCredit`/
  `Refund`), written only by `IStreamerLedger` and the startup `StreamerWalletReconciler` (Consumers
  `ApplicationStarted`, after `CreateDefaultReferralPartners`; config `GeneralSettings:Operator:Streamers`).
  Idempotency is in filtered unique indexes, not in code. Nothing in it is backed by lamports.
- **Flow:** `POST /orbit/wagers` from an active streamer runs **synchronously in the API** — no event, no
  claim, no transfer — through `RegisterBetRequest { StreamerPlay }`, which debits the play ledger inside
  the bet insert's transaction (`Bet.EntryKind = Streamer`). `RewardGameConsumer` / `RefundInvalidBetConsumer`
  credit wins / refunds to the same ledger and never read the treasury for a streamer-only round.
- **Cross-cutting:** every money read filters `EntryKind != Streamer` (coverage, leaderboards, stats money,
  referral earnings); a streamer wallet is refused on every real-money path (`RegisterBetHandler` guard,
  Orbit deposit / cash-out / settings / Telegram) and excluded from both Orbit auto-bet engines. The only
  public routes that read streamer status (`/availability`, open duel) honour it **only** behind a matching
  `X-Orbit-Session`. See `features/streamer-wallets.md`.

## 9. The game mechanic (the "Typer")

- `SpaceStrokerTyper` (`SpaceStroker.Domain/Games/Services`) is the heart of a running game.
- It reads the game's **committed `ServerSeed`** (generated + hash-committed at round creation; it
  was committed on the first bet until the Orbit auto-bet engine, which places before any bet exists,
  moved it earlier — `EnsureServerSeedCommitted` remains an idempotent backstop) and
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
  restart cannot resurrect an admin-deactivated type or mutate one under a live round. The exceptions
  are three **single-field** self-heals — `AllowFreeBet = !IsProduction()`, `AllowOrbit`, and
  `ComingSoon` — none of which touches `Active`. ⚠️ **`ComingSoon` is how a type is taken out of play in
  production** (config + a Consumers redeploy, which also publishes the existing
  `GameTypeDeactivatedEvent` to cancel and refund its open round): there is no admin endpoint, because
  no admin token can be minted in production. See `features/games.md` §19.
  ⚠️ **Holder lotteries have their own, wider pair (#196):** `HolderLotteryReconciler` brings an
  existing default with `HolderOnly: true` onto `HolderOnly`, `LotteryPot 0`, `RequiredAmount 0`,
  `AllowFreeBet false` and the config `Description` (the `AllowFreeBet` self-heal skips those types),
  and `HolderJackpotTransition` — right after default-parameter seeding, before
  `CorrectScheduledLotteryDraws` / `ReplanOpenRounds` / `OpenNewGames` — refunds the paid bets of the
  type's live **legacy** round and closes it `GameEndedAbruptely`, so `OpenNewGames` opens the first
  holder round. Both are idempotent and never throw out of startup. Both hosts also run
  `HolderJackpotSettings.Validate()` at startup and log at Error when it fails.
- **Admin portal** (`/admin`): the one authenticated surface. `X-Admin-Token` is an
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
- **Routing (param-free, unauthenticated, prefix-free):** `/` → the **starting page** (`Home` —
  hero, the game picker, the player's own stats, and entries to every other surface); `/game` → the
  plain lobby (`GameLobby`, still the documented iframe target and `?type=` deep-link fallback — an
  `#admin_token=<jwt>` fragment here hands off to the admin portal); `/game/play` → live game;
  `/game/history` + `/game/stats` → the header-menu pages; `/admin` → admin portal;
  `/wallet-bridge` → the same-origin wallet popup; `/background` → the backdrop-only page;
  `/soon` → the placeholder for unbuilt entries; `/error`. Two catch-alls, ranked by vue-router
  **specificity** rather than declaration order: a strip-prefix shim `/space-stroker/*` → the stripped
  path (query and hash preserved), and a generic catch-all → `/error`. **Embedded `/` redirects to
  `/game`**, so the dashboard never mounts in a frame. Player preferences are loaded **only** for
  `Home`/`GameLobby`/`Game`/`GameHistory`/`GameStats` (`App.vue`), and only once a wallet is connected.
  See `features/home-dashboard.md`.
- **Chromeless routes:** routes flagged `meta: { chromeless: true }` in `routes.ts` render without
  `GameHeader` — the wallet-bridge popup (whose header `RouterLink` would navigate the popup away) and
  `/background`, a chrome-free URL that shows **only** the animated backdrop (both
  `GameBackground` layers + `.section-vignette`, all global in `App.vue`) for embedding/screen-recording;
  its page component is intentionally empty. See `specs/background-page.md`.
- **API access:** `mainService` axios instance. A request interceptor sets `baseURL` from
  `VUE_APP_API_BASE_URL` and attaches **no auth header** — there is none. (The blanket `401 → /error`
  went with the launch token; terminal failures still reach `/error` by their own paths.) Endpoints:
  `GET /games/active-types`, `GET /games/{id}`, `POST /games/{id}/bets`, `GET /players/nonce`,
  `GET /players/preferences`, `PATCH /players/preferences`, `POST /players/self-exclusion`,
  `POST /players/partner-code` (#213 — one-time signed partner link, see
  `features/partner-code-on-profile.md`), `GET /players/stats`, `GET /players/profile` and the batched `GET /players/levels` (#186 — see
  `features/player-profile.md`), `POST /players/icon-purchases` (anonymous; the on-chain burn is the proof),
  `GET /players/stroke-balance` and `GET /players/icon-prices` (#212 — see `features/token-integration.md`),
  `GET /games/stats` (global stats), `GET /games/{parametersId}/stats`,
  `GET /games/{parametersId}/history`, `GET /games/{id}/verification`, and for Hyper Roll
  `POST /games/{gameTypeId}/boards`, `GET /games/rounds/{roundId}` (also serves **House** duel rounds
  since #200; also the grid + hits of a settled Word Lines round — the reroll route was removed by #209),
  `POST /games/{gameTypeId}/availability` (see `features/hyper-roll.md`), for Key Mines
  `POST /games/rounds/{roundId}/press` and `POST /games/rounds/{roundId}/cashout` (header `X-Round-Token`, the
  per-board secret returned once by `boards`; see `features/key-mines.md`), and for Monkey Duel
  `POST /games/{gameTypeId}/duels`, `GET /games/{gameTypeId}/duels`,
  `POST /games/rounds/{roundId}/duel-cancel` (wallet proof), plus the `ActiveGamesHub`
  `JoinDuelBoard`/`LeaveDuelBoard` channel `{gameTypeId}/DuelBoard` (see `features/monkey-duel.md`).
- **Player customization:** the header menu (opened by the **profile card** that replaced the ☰ glyph
  in #186) is headed by the player's identity block — a link to their own `/player/:wallet` — and holds
  **Settings** (opens `PlayerCustomizationModal.vue` — display name + color profile), **Stats** (the Stats
  page), **Fair** (the fairness modal), **History** (the Games History page) and the other destinations;
  Stats + Fair are hidden when embedded. Audio (mute/volume) and the wallet cluster stay directly on the
  header, which shows **one** balance (the Orbit pill while Orbit holds money, else the wallet pill).
  Saving the name or theme runs the wallet-proof flow (fetch nonce → sign the canonical message → PATCH),
  so the modal shows a "Connect wallet to customize" gate with no wallet.
- **Player profiles (#186):** `/player/:wallet` (`PlayerProfilePage.vue`) is public for any wallet and
  reads `GET /players/profile` + `GET /players/stats`; every live player name is a `PlayerTag` that opens
  the one mini `PlayerCard` mounted in `App.vue`; level badges and color profiles for all visible tags come
  from one batched `GET /players/levels` (≤ 50 wallets, 60 s cache), which since Profile V2 also carries each
  wallet's Edson icon (`avatars`), drawn by the shared `PlayerAvatar`; the icon picker is mounted once in
  `App.vue`. Donations are a wallet-signed SOL transfer the backend never moves (it only verifies and records
  it for the 🔔 feed). See `features/player-profile.md`, `features/profile-v2.md`. **Amounts are token-native everywhere** — there is
  no preferred display currency and no FX conversion. See `features/player-customization-modal.md`
  and `features/games-history.md`.
- **Games history:** `GameHistoryPage.vue` at `/game/history` (route `GameHistory`,
  reached from the header menu) browses one game type's completed rounds via
  `GET /games/{parametersId}/history` — paginated, "mine only" toggle (keyed on the connected wallet),
  per-row Verify link-out to `public/verify.html`. See `features/games-history.md`.
- **Wallet UX:** the header wallet cluster uses the configured token's `tokenSymbol` /
  `tokenMintAddress`, published on the game-type payload. Every game is paid in that one token, so
  there is no per-game payment branch. Since #151 the cluster is **two balance-carrying pills** —
  amber wallet (`WalletButtons`) and violet Orbit (`OrbitLauncher`) — and the on-chain balance is
  owned by the **`useTokenBalance` module singleton** (one refcounted poll, the app's only
  `getTokenBalance` call site; `TokenBalance.vue` is deleted). Attributed changes animate through the
  shared `MoneyValue` readout; see `features/money-changing-animations.md`.
- **Real-time:** `useGameHub` (`src/hooks/useGameHub.ts`) owns **one** SignalR connection per tab to
  the `Games` hub at `VUE_APP_WEBSOCKET_BASE_URL` — opened lazily on the first game load and kept
  across rollovers, game switches and lobby round-trips, with each round joining/leaving its group and
  binding/unbinding `{gameId}/…` handlers over the same socket (`GameReceived` is bound once for the
  tab). `useGame` consumes it and renders the streamed terminal output,
  speed, status and incoming bets. **Chat is not part of the game screen** any more: the app-wide
  `ChatDrawer` (mounted once in `App.vue`, started from `GameHeader`) shares the same socket through
  `useGameHub.joinGlobalChat` / `authenticateChat` — see `features/chat.md`. The old right-docked
  `GameSidePanel` / `GameChatPanel` were removed with it; the STATS and FAIR tabs had already moved to the header menu
  (`fair-stats-menu`). Per-game-type **stats** render on the dedicated **Stats page**
  (`GameStatsPage.vue`, `/game/stats`): a global section via `GET /games/stats` plus a
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
