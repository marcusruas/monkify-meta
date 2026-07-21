# ChimpOut — Glossary

> Domain vocabulary for agents. Source of truth: `monkify-backend/src/ChimpOut.Domain`.
> See `architecture.md` for how terms fit together; `features/operators.md` for operator rules.

- **Brand — "Space Stroker" (consumer) vs `ChimpOut` (technical)** — as of the #74 frontend rebrand
  (shipped 2026-07-21, `specs/rebrand-space-stroker.md`), the **consumer-facing brand is Space
  Stroker**: the frontend wordmark/copy, the routes `/space-stroker/*`, the client-storage `space-stroker.*`
  keys, and the embed `source:'space-stroker'` (`'space-stroker-wallet'`). The **technical/backend
  name stays `ChimpOut`** and is **still current** in this KB — C# namespaces (`ChimpOut.*`), the
  domain source, DB, `chimpoutsettings.json`, `api.chimpout.com`, and SignalR hub names are unchanged
  (the backend/infra rename is the separate follow-up card #75). So `ChimpOut.*` /
  `chimpoutsettings.json` / backend-host references throughout these docs are accurate; older
  **`/chimpout/*` route and `chimpout.*` storage-key** references describe paths now served under
  `/space-stroker/*` and `space-stroker.*` (a full frontend-path sweep of `features/` is deferred).
- **Game** — one round of the game for a given game type. Has a status lifecycle
  (`WaitingBets → GameStarting → InProgress → Ended → RewardForWinners…`). Entity: `Game`.
- **GameParameters** — a "game type" / configuration (name, required amount, allowed characters,
  min players, choice length, lottery params, **required** `OperatorId`, a **`PaymentMethod`**, and a
  nullable **`OperatorTokenId`** set only for `SolanaToken`). Entity: `GameParameters`.
- **Bet** — a player's wager on a `Game`: a `Choice` (the sequence they bet on), an `Amount`
  (Standard = USD; SolanaToken = token-native), optional `OperatorPlayerId` FK (NULL for anonymous free
  bets), a **`PaymentMethod`** (copied from the game type), a nullable **`ExternalTransactionReference`**
  (SolanaToken → the on-chain signature; Standard → the wallet debit ref), a provably-fair **`Seed`**
  (a 64-char `HMAC-SHA256(clientSeed, Nonce)` hex — the raw client seed never reaches the server), and a
  per-bet **`Nonce`**. Entity: `Bet`. The old `PaymentSignature` column is **removed**. On-chain payout/refund address is resolved from linked
  `OperatorPlayer.ExternalPlayerId`, not stored on the bet row.
- **OperatorPlayer** — operator-scoped player identity. Fields include `ExternalPlayerId` (Solana
  wallet pubkey or casino player id), optional `DisplayName`, `Currency` (defaults to USD),
  `LastSeenAt`. Unique on `(OperatorId, ExternalPlayerId)`. No PII. (`IsCrypto` was removed; crypto-ness
  is now the game type's `PaymentMethod`, not an operator property.) Entity: `OperatorPlayer`. See
  `features/operator-player-identity.md`.
- **Free bet** — dev/QA-only bet with `Amount = 0`, allowed only when backend `AllowFreeBets`, game
  `AllowFreeBet`, and (for UI) the frontend flag are all enabled. No debit/credit/refund under **either**
  payment method; a free `SolanaToken` game also skips wallet-connect. Detected via
  `BetExtensions.IsFreeBet` (now `Amount == 0`; the old empty-`PaymentSignature` check is gone). See
  `features/free-bet-mode.md`.
- **AllowFreeBet** — bool on `GameParameters` (DB default `false`). Per game type; exposed as
  `allowFreeBet` on active-types API responses.
- **AllowFreeBets** — bool on `GeneralSettings.Games` in `chimpoutsettings.json`. Global backend
  gate; must be `true` for the API to accept free bets.
- **Typer / ChimpOutTyper** — the domain service that "types" characters until a bet's choice appears;
  determines winners. Reads the game's committed **`ServerSeed`** and drives character selection from
  a **portable HMAC-SHA256 keystream** (rejection-sampled; no `System.Random`), with Stopwatch-scheduled
  wall-clock pacing and an HMAC-keystream fallback at the character cap.
  `ChimpOut.Domain/Games/Services/ChimpOutTyper.cs`. See `features/provably-fair.md` and
  `features/typer-pacing-scalability.md`.
- **DisplaySpeed** — SignalR event `{ typingSpeed }` (1–3) ramping during a game run. Driven by
  elapsed-time fractions of the typer's target mean duration; the frontend maps tiers to refresh
  cadence in `GameTyper.vue`.
- **Fallback winner** — when the typer reaches `MaxTyperCharacters` without a natural sliding-window
  match, the winner is drawn from a dedicated `HMAC-SHA256(ServerSeed, prefix + "|fallback")` label over
  ordinally-sorted choices. Same `ServerSeed` + bets → same winner in any language
  (`WinnerDecidedByFallback`). Because the winning sequence was never
  naturally typed, `GameService.RunGame` then emits it as a **final `<gameId>/Terminal`
  batch** so the terminal ends with the word "typed" (display-only; see `features/characters-away.md`).
- **MaxTyperCharacters** — config cap (default 5,000,000) on characters typed before fallback
  winner selection; also bounds the pacing horizon for huge search spaces.
- **ServerSeed** — the per-game server entropy for provably fairness: 32 CSPRNG bytes stored as a
  64-char lowercase-hex string on `Game.ServerSeed`, generated + committed **atomically on the first
  bet** (first-writer-wins; never regenerated) and **revealed only after the round Ends** (via the
  verification endpoint). Keys the HMAC-SHA256 keystream that drives the typer. Replaces the removed
  `int? Game.Seed`. See `features/provably-fair.md`.
- **ServerSeedHash** — `SHA256(UTF8(ServerSeed))` (lowercase hex) on `Game.ServerSeedHash`, the
  **commitment** published as soon as the seed exists (during betting, on `ActiveGameDto` and the
  verification endpoint). After the reveal, `SHA256(ServerSeed) == ServerSeedHash` proves the seed was
  not grinded.
- **Client seed** — the player's own provably-fair entropy: a persistent value in browser localStorage
  (`chimpout.clientSeed`), viewable/settable/rotatable in the header gear modal. Combined with a per-bet
  **`Nonce`** to derive `Bet.Seed = HMAC-SHA256(clientSeed, String(nonce))`. The raw client seed
  **never** leaves the browser; only the derived `Bet.Seed` reaches the server. Rotating/setting resets
  the nonce to 0. Frontend `useClientSeed`.
- **Bet nonce** — an incrementing per-bet counter (`Bet.Nonce`, `long`) under a client seed; makes each
  derived `Bet.Seed` unique and lets a bettor prove `Bet.Seed == HMAC(clientSeed, nonce)` without
  exposing the client seed.
- **Provably-fair verification** — reproducing a round's outcome from published inputs. Exposed by the
  reveal-gated `GET /games/{id}/verification` (operator-scoped): pre-Ended returns `serverSeedHash` +
  ordered bet seeds/choices/nonces + `characterPool`/`queueLength`/`maxCharacters`; post-Ended adds
  `serverSeed` + `winningChoice` + `winnerDecidedByFallback` (the raw `clientSeed` is never returned).
  Anyone replays the HMAC keystream to reproduce the winner; a standalone offline `public/verify.html`
  does it without contacting ChimpOut. There is **no in-terminal recompute** — the game terminal only
  displays the commitment (during betting) and the revealed seed (after Ended). See
  `features/provably-fair.md`.
- **Characters Away / near-miss** — a player-facing, display-only reveal: while a round runs, a
  bottom-docked terminal row per **own** bet choice shows how close the monkey has come to typing it
  (`> N AWAY!`, `away = L − k` where `k` = longest stream-suffix that is a prefix of the choice — a
  KMP tail-match). Rows climb, **reset and re-climb after a 2 s stall**, and **type out the full word**
  on a win. Computed entirely client-side from the existing `Terminal` stream (`utils/nearMiss.ts` +
  `hooks/useNearMiss.ts`, rendered in `GameTerminal.vue` beside the in-game `> POT:` line). No effect
  on outcomes, payouts, pacing, or the seed. See `features/characters-away.md`.
- **Operator** — a casino/betting platform tenant (e.g. BetMGM). Every `GameParameters` row
  belongs to exactly one operator (`OperatorId` required — no house mode). Scoped on REST via the
  signed **launch token** (`X-Launch-Token`; the operator id is the token `iss`); players enter at
  `/chimpout/game/games` (the operator is read from the token, not the URL). Fields:
  `ContractStartDate`, optional `ContractEndDate` (null = never
  expires), **`LaunchPublicKey`** (+ optional `LaunchKeyId`), and a collection of registered
  **`OperatorToken`s**. The old operator-level `PaymentModel` is **removed** — payment is a per-game
  concern (see **PaymentMethod**). Entity: `Operator`.
- **Main (default production operator)** — the operator named `Main` (fixed RFC-4122 GUID
  `22222222-2222-4222-8222-222222222222`, active, no end date) seeded by `ChimpOut.Consumers` in
  **every** environment and the **only** seeded operator in production. Pre-provisioned with a
  registered `OperatorToken` (from `GeneralSettings.Token`) and three **SolanaToken** default game
  types (`6X Main Sprint` race, `6X Main Derby` preset "horse race", `6X Main Millions` 24h lottery;
  free bets off). A standalone visitor with no launch token is issued an **anonymous** Main token so the
  public game works without a casino redirect. See `features/main-production-operator.md`.
- **`POST /launch-token/default`** — a **production-enabled** endpoint (`LaunchController`, **not** under
  `/dev`, not token-guarded) that mints an **anonymous** (empty `sub`) RS256 launch token **only** for
  the **Main** operator, signed with Main's own launch key (`MainSigningPrivateKeyPem`; TTL
  `MainTokenTtlSeconds`, default 3600s). The SPA calls it when no fragment/stored token exists (never
  when embedded); a 401 on the resulting token triggers a **silent re-mint**. Real operators' launch
  tokens are unaffected. See `features/main-production-operator.md`.
- **Launch token** — a casino-signed **RS256 JWT** that proves operator + player identity; required
  (`X-Launch-Token` header) on every operator-scoped REST call and validated by middleware in
  `ChimpOut.Api` against the operator's `LaunchPublicKey`. Claims: `iss`=operatorId, `sub`=player id,
  `exp`/`iat`/`jti`. Failures return a vague 401. See `features/launch-token-validation.md`.
- **Admin token** — an operator-signed **RS256 JWT** with `scope = "admin"`, `iss = operatorId`,
  **no `sub`**, sent as **`X-Admin-Token`** on `/admin/*` REST calls. Validated by
  `AdminTokenValidationMiddleware` against the same `LaunchPublicKey`; **not** contract-gated. Admin
  and launch tokens are not interchangeable. Entry:
  `/chimpout/admin#admin_token=<jwt>`. See `features/operator-admin-page.md`.
- **IAdminContext** — scoped request identity set by admin middleware; carries `OperatorId` only
  (no player). Used by admin handlers; kept separate from `ILaunchContext`.
- **PaymentMethod** — enum on **`GameParameters`** (`Standard = 0`, `SolanaToken = 1`) that decides
  how a game type settles. **Standard** = off-chain via the operator's wallet adapter, always USD,
  player = launch-token `sub`, no wallet. **SolanaToken** = on-chain in a per-operator registered
  `OperatorToken`, token-native units, player = connected wallet + signature. Replaces the removed
  operator-level `Operator.PaymentModel`. Dispatched by `IPaymentMethodResolver`. See
  `features/payment-methods.md`.
- **OperatorToken** — a per-operator registered SPL token backing `SolanaToken` games. Fields:
  `Label` (display symbol), `MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`,
  `TokenOwnerPrivateKey` (**encrypted at rest**, never returned/logged), `Decimals`, `Active`. Managed
  in the admin portal (register / list / deactivate — no edit). Entity: `OperatorToken`.
- **SolanaTokenDescriptor** — an immutable record (`MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`,
  `TokenOwnerPrivateKey`, `Decimals`) passed to `ISolanaService` per call so it settles against the
  game's token (private key already decrypted by the caller — Consumers only). Replaces per-bet reads
  of the global `GeneralSettings.Token`.
- **IPaymentMethodService / resolver** — the unified settlement strategy (`Debit`/`Credit`/`Refund` →
  `PaymentResult`) with `StandardPaymentMethodService` (→ `IOperatorWalletService`) and
  `SolanaTokenPaymentMethodService` (→ `ISolanaService` + a `SolanaTokenDescriptor`).
  `IPaymentMethodResolver.Resolve(parameters)` keys on the game's `PaymentMethod`;
  `GetAvailableMethods(operatorId)` returns Standard iff a wallet provider is configured and SolanaToken
  iff ≥1 active `OperatorToken`. See `features/payment-methods.md`.
- **CryptoTransactionLog** — on-chain detail for a `SolanaToken` movement, always tied to a `Bet`:
  `Signature`, `Wallet`, `MintAddress`, `SenderAccount`, token-native `Amount`, `RawTokenAmount`,
  `Decimals`, `Reason`. `TransactionLogId` is **null** for the bet-**placement** record ("what the user
  paid") and **set** for a Credit/Refund movement. Entity: `CryptoTransactionLog`.
- **ISecretProtector** — encryption-at-rest for `OperatorToken.TokenOwnerPrivateKey`. Shipped impl
  `AesSecretProtector` = **AES-256-GCM**, key derived (SHA-256) from the
  `GeneralSettings.SecretProtection.Key` passphrase shared by both hosts; base64
  `[nonce][tag][ciphertext]`. The API `Protect`s on register; Consumers `Unprotect`s only to build a
  `SolanaTokenDescriptor` for a transfer. *(Deviates from the spec's Data-Protection plan.)*
- **ContractIsActive** — domain helper on `Operator`: true when `ContractStartDate <= UtcNow` and
  contract has not expired. Inactive operators return empty data or redirect to `/error` (vague). The
  active-operator lookup is cached via `IOperatorService.GetActiveOperatorById`.
- **TransactionLog** — the **unified money-movement ledger** per bet. Each row has a `TransactionLogType`
  (`Credit | Debit | Refund`), `Amount`, a **`PaymentMethod`**, a nullable `ExternalTransactionReference`
  (the operator wallet ref for `Standard`; the on-chain signature for `SolanaToken`), and nullable
  `Reason`. The old `Signature` column is **removed** (on-chain specifics now live in
  `CryptoTransactionLog`). Used for reward/refund idempotency: reward subtraction reads `Credit` rows,
  the already-refunded guard reads `Refund` rows. (The separate `RefundLog` table was folded in here.)
  See `features/payment-methods.md`.
- **TransactionLogType** — enum on `TransactionLog` (`Credit = 0`, `Debit = 1`, `Refund = 2`); `Credit` is
  the default so legacy reward rows keep their meaning.
- **Operator wallet API** — the off-chain settlement path for **`Standard`** game types: the
  operator's own wallet that ChimpOut instructs to **debit** stakes, **credit** winnings, and **refund**
  cancelled bets in USD (ChimpOut holds no float). Integrated via a per-operator adapter
  (`IOperatorWalletService`) selected by a named provider in config (`IOperatorWalletResolver`), reached
  through `StandardPaymentMethodService`. `SolanaToken` games settle on-chain instead. See
  `features/operator-wallet-api.md` + `features/payment-methods.md`.
- **NeedsRefunding / Refunded lifecycle** — `Bet.Status` values that are now a real, self-clearing
  refund lifecycle. `RefundInvalidBetConsumer` sets a bet to **`Refunded`** on any **successful**
  refund (all refund paths, incl. the inline lottery/deactivation/invalid-bet triggers — which used to
  leave it `Made`); a **failed** refund leaves the status so it is retried. A bet in **`NeedsRefunding`**
  is one flagged for refund but not yet paid back. See `features/refund-safeguards.md`.
- **Pending-refund sweep** — `PendingRefundSweepJob`, a Hangfire recurring job in `ChimpOut.Consumers`
  (cron `0 */12 * * *`, id `pending-refund-sweep`) that loads every `NeedsRefunding` bet (all
  operators, any game state) and publishes a `RefundInvalidBetEvent` for each so the refund consumer
  pays it back and clears it to `Refunded`; failures retry on the next run. Fed by the operator-admin
  **`POST /admin/bets/refund { paymentSignature }`** action (the only producer of `NeedsRefunding`),
  which marks a bet for refund by its `ExternalTransactionReference` (operator-scoped; blocks
  Rewarded/Refunded/free; idempotent). See `features/refund-safeguards.md`.
- **Pre-bet check / RG hook** — an **opt-in**, per-operator responsible-gaming check ChimpOut calls in
  `RegisterBetHandler` after local validation and **before** the wallet debit, so the operator can
  **allow or deny** a bet (self-exclusion, limits, cooling-off, KYC, blocked). Per-operator adapter
  (`IPreBetCheckService`) selected by a named provider in `GeneralSettings.OperatorRgChecks`
  (`IPreBetCheckResolver`, which returns **null → skip** when unconfigured — the default). Applies to
  **both** payment methods (it runs before settlement, whichever strategy applies). A denial returns a
  **specific** player message (`PreBetCheckDenyReason`: `SelfExcluded | LimitReached | CoolingOff |
  KycRequired | Blocked | Other`); a timeout/error is **fail-closed** (rejected, no settlement). Skipped
  for anonymous free bets. Log-only, no DB row. See `features/rg-pre-bet-hook.md`.
- **PresetChoice** — predefined winnable choices for a game type.
- **LotteryParameters** — config for lottery-style games (`Frequency`, base `LotteryPot`); scheduled
  via Hangfire. The base is a per-round seed; the *running* un-won jackpot is carried on the game as
  **`CarriedExtra`**, not here. See `features/lottery-pot-stacking.md`.
- **CarriedExtra** — the running lottery jackpot stacked from prior **un-won** rounds, stored per
  game (`Game.CarriedExtra`, `decimal(18,9)`, default `0`) **on top of** the base `LotteryPot`.
  A **preset lottery** whose drawn choice had no bettors carries `prev.CarriedExtra + Σ losing stakes`
  into the successor; an **under-subscribed** round refunds stakes and carries the jackpot unchanged; a
  **paid win** (incl. a free-bet winner) resets the successor to `0`. Decided once at rollover by
  `LotteryPotCarry.NextCarriedExtra(Game)`. `0` for every non-lottery game. On admin deactivation
  it is **transferred** to an eligible same-operator/same-method/same-token lottery (never destroyed).
  See `features/lottery-pot-stacking.md`.
- **Pot** — the amount winners split, shown as the `> POT:` line in the game terminal for **every**
  game type (free-type race, preset race, lottery). Client-side, display-only: races = `Σ bets`,
  lottery = `lotteryParameters.lotteryPot` base `+ CarriedExtra` (the stacked jackpot) `+ Σ bets`;
  seeded on load, accumulated live from the `/Bets` SignalR stream, reset/reseeded on rollover; free
  bets (`amount = 0`) add nothing. Rendered via `formatAmount` (SolanaToken → the token
  symbol/decimals; Standard → USD or preferred display currency). Settlement math is separate —
  `BetDomainService.SetPotAmount` (`= Σ bets + LotteryPot + CarriedExtra`) remains the payout source of
  truth. See `features/game-pot-display.md` and `features/lottery-pot-stacking.md`.
- **Display name** — optional `OperatorPlayer.DisplayName` (1–20 chars, trimmed). Set via the
  customization modal (`PATCH /players/preferences`); rendered on bet DTOs and recent-bets SignalR
  events for self and others; falls back to truncated `externalPlayerId`. See
  `features/player-customization-modal.md`.
- **Preferred display currency** — `OperatorPlayer.Currency` (default `USD`). Offered to **all**
  operators (chosen from the backend definitive fiat set, 33 codes), but affects **rendering of
  `Standard` games only** — `SolanaToken` games always render token-native, regardless of the
  choice. Rendering-only — backend always stores and transmits USD; client multiplies by cached
  USD→currency rates from `GET /fx/rates`. See `features/player-customization-modal.md`.
- **FX rates / display conversion** — token-gated `GET /fx/rates` returns the definitive currency
  list with USD-based rates (Redis cache, 6h TTL; `IExchangeRateProvider` + FreeCurrencyAPI or mock).
  Frontend `useDisplayCurrency.formatUsd` applies conversion for **`Standard`** games only;
  settlement and operator-wallet amounts stay USD.
- **Game chat message** — a plain-text line broadcast within one game instance over
  `ActiveGamesHub.SendChatMessage`; attributed by client-resolved **display name**; persisted to
  `GameChatMessage` for audit/retention only (not replayed). See `features/game-chat.md`.
- **Game-type stats** — all-time aggregated stats for one **game type** (`GameParameters`),
  served read-only by token-gated, operator-scoped `GET /games/{parametersId}/stats`: biggest pot
  ever split, biggest single payout, rounds played, total paid out, and (preset types only) the
  most-picked **hottest choice**. Computed **on demand** from the type's completed
  (`RewardForWinnersCompleted`) rounds — pot reconstructed as `Σ Bet.Amount` (+ lottery base), money
  from `TransactionLog` `Credit` rows — and cached in Redis (~120s); **no stats entity or migration**.
  Free bets are excluded from money/hottest-choice (a free-only completed round still counts as a
  round). Amounts are single-currency per type. Rendered in the lazy **STATS** tab of the `/game`
  side panel. Entity/DTO: `GameTypeStats` / `GameTypeStatsDto`. See
  `features/game-type-stats.md`.
- **MassTransit Postgres SQL transport** — the message transport carrying the game state-machine events
  (`BetPlaced → RunGame → RewardGame`, refunds, notifications, chat). Uses `MassTransit.SqlTransport.PostgreSQL`
  (`UsingPostgres`) instead of a RabbitMQ/AMQP broker — **there is no broker**. Queues/topics/messages
  are tables under schema `transport` in the **`chimpout_messaging`** database. At-least-once delivery,
  competing consumers via `SKIP LOCKED` + `LISTEN/NOTIFY`. The API is publisher-only; `ChimpOut.Consumers`
  provisions the DB + schema at startup (`AddPostgresMigrationHostedService`). See
  `features/masstransit-postgres-transport.md`.
- **chimpout_messaging** — the dedicated database (schema `transport`) holding the MassTransit SQL
  transport tables, on the **same** Postgres instance as the game DB (`chimpout`). Its connection is
  `ConnectionStrings:Messaging`, reusing the game-DB user (which therefore needs `CREATE DATABASE`).
  Isolates queue churn from the game schema while keeping one instance/one bill. (The game DB itself was
  renamed `monkify → chimpout`.)
- **IMessageBus / MessageBusPublisher** — the thin publish abstraction over MassTransit's
  `IPublishEndpoint.Publish` (`ChimpOut.Infrastructure.Services.Messaging`). Renamed from the old
  transport-specific `IRabbitMQService` / `RabbitMQService` when the transport moved to Postgres.
- **Persistent / workload layers** — the two Terraform states in `monkify-meta/deploy/terraform/`. The
  **persistent** layer holds the Cloud DNS zone + domain + remote-state bucket (`prevent_destroy`); the
  **workload** layer holds everything a `teardown` may destroy (Cloud Run, Cloud SQL, Memorystore, LB,
  Cloud Armor, VPC, Artifact Registry, buckets). Teardown destroys only the workload, so the domain stays.
- **chimpout-deploy / chimpout-db** — the two local .NET 8 console tools under `deploy/tools/`.
  `chimpout-deploy` runs the manual GCP deploy (`deploy [api|consumers|front|lp]` + `teardown`; syncs
  secrets → Secret Manager, builds/pushes, `terraform apply`). `chimpout-db` is a read-write DB REPL over
  the Cloud SQL Auth Proxy that requires typing `YES` before any write.
- **CorsOriginResolver / GeneralSettings:Cors** — config-driven browser CORS allow-list read by both hosts
  (`GeneralSettings:Cors:AllowedOrigins`, default `https://app.chimpout.com`, prod-overridable via env).
  Replaced the hardcoded origin.
