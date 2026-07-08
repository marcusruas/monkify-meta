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
                                    │ SolanaService             │
                                    │ SPL token transfers       │
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

- HTTP request → **`LaunchTokenValidationMiddleware`** (validates `X-Launch-Token`, sets
  `ILaunchContext`; rejects admin-scoped tokens) and/or **`AdminTokenValidationMiddleware`**
  (validates `X-Admin-Token`, sets `IAdminContext`; guards `/admin` only) → **Controller**
  (`SessionsController`, `OperatorsController`, `PlayersController`, `AdminController`, dev-only
  `DevController`) → constructs a **MediatR request** → **handler** that extends
  `BaseRequestHandler` → **services** (`SessionService`, `IOperatorService`, payment processors) →
  **`MonkifyDbContext`**.
- Controllers extend `BaseController`; responses are wrapped by `ProcessRequest(...)` into a
  standard `ApiResult<T>` shape (`{ data, messages }`). Validation failures bubble up via
  `IMessaging.ReturnValidationFailureMessage(...)`.
- Handlers live under `Monkify.Infrastructure/Handlers/<Area>/<UseCase>/` as a
  `Request` + `Handler` pair (e.g. `RegisterBet`, `GetActiveParameters`, `GetSessionHistory`,
  `GetActiveSessionForParameter`, `Operators/GetOperatorById`).

## 3. Data: PostgreSQL + EF Core (Npgsql)

- **Provider:** PostgreSQL via `UseNpgsql`.
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
  (a "game type"; carries `PaymentMethod` + optional `OperatorTokenId`), `LotteryParameters`,
  `PresetChoice`, `SessionStatusLog`, `Operator`, **`OperatorToken`** (a per-operator registered SPL
  token; owner private key encrypted at rest).
- **Bets area** (`Monkify.Domain/Bets/Entities`): `Bet`, `TransactionLog` (unified money-movement
  ledger — `Credit`/`Debit`/`Refund`, now carrying `PaymentMethod` + `ExternalTransactionReference`),
  **`CryptoTransactionLog`** (on-chain specifics for `SolanaToken` movements), `BetStatusLog`.
  (`RefundLog` was removed; refund audit lives in `TransactionLog`.)
- All entities extend `TableEntity` (`Id: Guid`, `CreatedDate`, `UpdatedDate`).

Relationships: `Operator 1—* SessionParameters 1—* Session 1—* Bet 1—* TransactionLog`;
`Operator 1—* OperatorToken 1—* SessionParameters` (nullable — only `SolanaToken` types reference a token);
`Bet 1—* CryptoTransactionLog`; `Operator 1—* OperatorPlayer 1—* Bet` (nullable FK — anonymous free bets
have no `OperatorPlayer`). See `features/payment-methods.md`.

## 4. Messaging: RabbitMQ via MassTransit

- The **API** registers MassTransit only as a **publisher**. The **Consumers** host registers and
  binds the consumers (one receive endpoint per consumer).
- `IRabbitMQService` / `RabbitMQService` is a thin wrapper over `IPublishEndpoint.Publish`.

### Events and consumers (the session state machine)

| Event | Published by | Consumed by | Effect |
| --- | --- | --- | --- |
| `BetPlacedEvent` | `RegisterBetHandler` (non-lottery), Hangfire (lottery), `BetPlacedConsumer` flow | `BetPlacedConsumer` | After a minimum wait & min-player check, tries to start the session, then publishes `RunSessionEvent`. Refunds bets if a lottery lacks players. |
| `RunSessionEvent` | `BetPlacedConsumer` | `RunSessionConsumer` | Marks session `InProgress`, runs the typer, marks `Ended`, flags winners `NeedsRewarding`, creates the next session, publishes `RewardSessionEvent`. |
| `RewardSessionEvent` | `RunSessionConsumer` | `RewardSessionConsumer` | For each winner, calls `ISolanaService.TransferTokensForBet(...)`; updates statuses. |
| `RefundInvalidBetEvent` (carries `BetId`) | `RegisterBetHandler`, `BetPlacedConsumer`, `SessionTypeDeactivatedConsumer` | `RefundInvalidBetConsumer` | Refunds an invalid/rejected bet — crypto via `ISolanaService.RefundTokens`, regulated via the operator wallet adapter. Skipped for free bets (`Value == 0`). |
| `SessionParametersCreatedEvent` | `CreateSessionTypeHandler` | `SessionParametersCreatedConsumer` | Opens the first session for a newly created operator session type (idempotent). |
| `SessionTypeDeactivatedEvent` | `DeactivateSessionTypeHandler` | `SessionTypeDeactivatedConsumer` | Cancels `WaitingBets` sessions of a deactivated type (refunds paid bets, skips free). |
| `BetNotificationEvent` | `SessionService.InsertBet` | `BetNotificationConsumer` | Pushes the new bet to subscribers (recent-bets feed). |

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

- Hubs in `Monkify.Infrastructure/Hubs`: `ActiveSessionsHub` (mapped at `/Hubs/Sessions`) and
  `RecentBetsHub` (mapped at `/Hubs/RecentBets`), both registered in `Monkify.Api/Program.cs`.
- Clients join a per-session group (`JoinSession`). During a run, the typer streams:
  - `…/Terminal` — batched characters,
  - `…/DisplaySpeed` — current typing speed,
  - `…/Status` — session status transitions (incl. winners on `Ended`),
  - `{sessionId}/ChatMessage` — per-session chat (`SendChatMessage` on the same hub/group),
  - and `SessionReceived` / a recent-bets `…/Bets` channel.
- JSON uses camelCase. Both the API and Consumers configure SignalR (Consumers emits during runs).

## 7. Scheduling: Hangfire

- Configured in `Monkify.Consumers` with **PostgreSQL storage** (`UsePostgreSqlStorage`), with a
  dashboard at `/hangfire`.
- Used to **schedule lottery sessions**: `SessionService.ScheduleLottery` enqueues a delayed
  `BetPlacedEvent` publish for the session's start time.
- **Session chat purge:** daily recurring job `session-chat-purge` runs `ChatMessagePurgeJob`, deleting
  `SessionChatMessage` rows older than `GeneralSettings.Chat.RetentionDays` (default 7). See
  `features/session-chat.md`.

## 8. Blockchain & payments

- **Payment method per session type** (`SessionParameters.PaymentMethod`, `Standard | SolanaToken`) —
  **not** per operator (the old `Operator.PaymentModel` is removed). **Standard** settles off-chain in
  USD through the **operator's wallet API**; **SolanaToken** settles on-chain in a per-operator
  **registered `OperatorToken`** (its own mint/sender/owner-keypair/decimals; token-native units). A
  single operator can offer both. See `features/payment-methods.md`.
- **Unified dispatch:** `IPaymentMethodService` (methods `Debit`/`Credit`/`Refund` → `PaymentResult`)
  with `StandardPaymentMethodService` (→ `IOperatorWalletService`) and `SolanaTokenPaymentMethodService`
  (→ `ISolanaService` + a per-call `SolanaTokenDescriptor`); `IPaymentMethodResolver.Resolve(parameters)`
  keys on the session's method. `GetAvailableMethods(operatorId)` drives the admin form (Standard iff a
  wallet provider is configured; SolanaToken iff ≥1 active `OperatorToken`).
- `Solnet` (`Solnet.Rpc`, `Solnet.Programs`, `Solnet.Wallet`) talks to a Solana cluster.
- `ISolanaService` / `SolanaService` validate bet payment signatures, transfer SPL tokens for
  rewards, and refund invalid bets **for `SolanaToken` sessions**, taking a `SolanaTokenDescriptor` per
  call (owner account built per call from the session's token; global `ClusterUrl` only). They write no
  ledger rows — the payment strategy does.
- **Standard payouts** go through `IOperatorWalletService` (a per-operator adapter; only a mock adapter
  ships today), resolved by `IOperatorWalletResolver` from `GeneralSettings.OperatorWallets`. The stake
  **debit** runs synchronously in `Monkify.Api` at bet time; **credit** and **refund** run in
  `Monkify.Consumers`. SolanaToken bet-time validation runs in the API (public token data only, no
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
  reads the ledger by type for reward/refund idempotency. There is **no Monkify commission** — winners
  receive the full pot.

## 9. The game mechanic (the "Typer")

- `MonkifyTyper` (`Monkify.Domain/Sessions/Services`) is the heart of a running session.
- It seeds a deterministic RNG from session + per-bet seeds, builds the set of bet choices and the
  pool of characters to type, then emits characters one at a time. When the rolling window of typed
  characters equals a bet's choice, that choice wins (`HasWinners`, `FirstChoiceTyped`).
- Wall-clock pacing is time-budget driven (`SessionSettings` targets in `monkifysettings.json`):
  `MonkifyTyper` schedules ms-per-character from mode mean/p95 bounds and search-space horizon;
  Stopwatch delays keep duration hardware- and load-stable. Large configs may hit
  `MaxTyperCharacters` and resolve via a deterministic seed-based fallback winner. `DisplaySpeed`
  (1–3) ramps on elapsed-time fractions. The `SessionService.RunSession` loop streams output over
  SignalR. See `features/typer-pacing-scalability.md`.
- **Fallback terminal reveal:** on a fallback-decided winner (the word was never naturally typed),
  `RunSession` emits `MonkifyTyper.FirstChoiceTyped` as a final `…/Terminal` batch so the terminal
  ends with the winning sequence "typed" — matching a natural win. Display-only; the winner and seed
  are unchanged. This backs the client-side near-miss reveal (see `features/characters-away.md`).

## 10. Multi-tenancy: Operators

- `Operator` rows represent casino/betting platforms. `SessionParameters.OperatorId` is a **required**
  FK — there is no house / operator-less mode. Operators carry a `LaunchPublicKey` (payment is now a
  **per-session** concern — the old operator-level `PaymentModel` is removed; see §8 and
  `features/payment-methods.md`). An operator may register one or more `OperatorToken`s.
- Players enter at **`/monkify/game/sessions#launch_token=<jwt>`** (the URL no longer carries the
  operator id — it is read from the token's `iss` claim). Operator + player identity arrive in a
  casino-signed **launch token** (`X-Launch-Token`, RS256), validated by
  `LaunchTokenValidationMiddleware` in `Monkify.Api` on every operator-scoped REST call
  (`/sessions/*`, `/operators/*`, `/players/*`, `/fx/*`). The old `x-operator-id` header is **removed**; any
  token failure → generic **HTTP 401** (real reason logged server-side only). See
  `features/launch-token-validation.md`.
- Read handlers and bet creation scope data to the token's operator (only if `ContractIsActive`; the
  active-operator lookup is cached via `IOperatorService`). Cross-operator attempts are rejected with
  vague errors. Player identity is decided **per session `PaymentMethod`**: `Standard` = token `sub`;
  `SolanaToken` = the connected wallet in the body (+ signature).
- Expired, not-yet-started, or unknown operators are hidden behind generic "not found / unavailable"
  messaging (never "contract expired"). Branding is served by **`GET /operators/{id}`** only (no list;
  token-guarded; no longer carries a payment model).
- Mid-session contract expiry: the running session finishes; new bets and new sessions for that
  operator are blocked.
- SignalR hubs use session id only — no token validation on websockets.
- Non-prod: `Monkify.Consumers` seeds **one** dev operator (fixed RFC-4122 GUID
  `11111111-1111-4111-8111-111111111111`) pre-provisioned for **both** methods — a mock wallet map
  (Standard) + one encrypted `OperatorToken` seeded from `GeneralSettings.Token` (SolanaToken) — with
  all default session types Standard plus 3 SolanaToken defaults; dev-only `POST /dev/launch-token` and
  `POST /dev/admin-token` mint player and admin tokens (no `?paymentModel`; disabled in production).
- **Operator admin portal** (`/monkify/admin`): operators manage their own session types **and register
  Solana tokens** via **`X-Admin-Token`** (admin-scoped RS256 JWT, same keypair as launch tokens,
  **not** contract-gated). Session types + tokens: create/list/deactivate only — no in-place edit;
  `GET /admin/payment-methods` reports availability. See `features/operator-admin-page.md` and
  `features/payment-methods.md`.
- See `specs/operator-feature.md` + `specs/launch-token-validation.md` for full design;
  `features/operators.md` and `features/launch-token-validation.md` for the distilled versions.

## 11. Frontend (`monkify-front`)

- **Stack:** Vue 3 (Options/Composition via `<script setup>`), TypeScript, Vue CLI (webpack),
  Tailwind CSS + DaisyUI, Vuex + vue-router, `@microsoft/signalr`, Solana wallet adapter
  (`solana-wallets-vue`, `@solana/web3.js`, `@solana/spl-token`), axios, `vue3-toastify`.
- **Structure** (`src/`):
  - `hooks/` — composables: `useSession` (session state, websockets, bet flow), `useConnection`
    (Solana wallet/token transfer), `useOperator` (load branding from route param, theming, `/error`
    redirect).
  - `services/` — `mainService.ts` (axios instance + endpoints), interceptors, handlers.
  - `types/` — shared TS types/enums (`ActiveSession`, `BodySessionBet`, `OperatorBranding`, …).
  - `utils/` — `operatorRoute.ts` (GUID validation, path-based operator id for the interceptor).
  - `pages/` — `SessionSelectPage`, `GamePage`, `ErrorPage`. `components/` — UI, game, header,
    background. `constants/`, `assets/`.
- **Routing (param-free; operator from the token `iss`):** `/monkify/game/sessions` → session select;
  `/monkify/game/play` → live game; `/monkify/game` → dev launch landing (gated by `VUE_APP_DEV_LAUNCH`;
  production redirects to `/error`); missing/undecodable token or `/` → `/error`.
- **API access:** `mainService` axios instance. A request interceptor sets `baseURL` from
  `VUE_APP_API_BASE_URL` and attaches **`X-Launch-Token`** (captured from the launch URL fragment by
  `useLaunch`, persisted per-tab in `sessionStorage`); any `401` routes to `/error`. Endpoints:
  `GET /sessions/active-types`, `GET /sessions/{id}`, `POST /sessions/{id}/bets`, `GET /operators/{id}`,
  `GET /players/preferences`, `PATCH /players/preferences`, `GET /fx/rates`.
- **Player customization:** gear icon in `GameHeader.vue` opens `PlayerCustomizationModal.vue` (display
  name + preferred currency dropdown, both for **all** operators). `useDisplayCurrency` loads FX rates +
  prefs on app mount and converts USD amounts client-side; the preferred currency affects **`Standard`
  sessions only** — `SolanaToken` sessions always render token-native. The gear is gated on operator
  context derived from the launch token (survives an operatorId reset on a hard reload). See
  `features/player-customization-modal.md` + `features/payment-methods.md`.
- **Wallet UX is per-session:** the header wallet cluster (`WalletButtons` + `TokenBalance`) shows only
  when the current session's `paymentMethod === SolanaToken` (using the session's `tokenSymbol` /
  `tokenMintAddress`), never for `Standard` or on the sessions list — driven by the session, not an
  operator flag.
- **Real-time:** `useSession` opens two SignalR hubs (`Sessions`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`, joins the session group, and renders the streamed terminal output,
  speed, status, incoming bets, and **session chat** (`GameChatPanel` + `useSessionChat` on
  `{sessionId}/ChatMessage`). See `features/session-chat.md`.
- **Bet flow:** `useSession.bet()` branches on `selectedSession.paymentMethod`. **`Standard`** posts
  `{ amount, choice, seed }` with no wallet/signature (identity via launch token `sub`); **`SolanaToken`**
  sends an on-chain SPL token transfer to the session's token via `useConnection.sendToken`, then POSTs
  the bet with the returned signature + wallet. Free bets (amount 0) skip both.

## 12. Local environment (docker-compose)

`monkify-backend/docker-compose.yml` brings up the full stack for development:

- `api` (port 6001 → 8080), `consumers` (port 6002 → 8080, hosts `/hangfire`),
- `database` (Postgres 16), `redis` (7), `rabbitmq` (3-management, UI on 15672).
- The API depends on the Consumers being healthy (so migrations/seeding run first).

> Connection strings and provider credentials are supplied via environment/config and **must not
> be read or edited** by the agent — see `constraints.md`.
