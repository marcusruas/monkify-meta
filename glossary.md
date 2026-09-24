# SpaceStroker — Glossary

> Domain vocabulary for agents. Source of truth: `monkify-backend/src/SpaceStroker.Domain`.
> See `architecture.md` for how terms fit together; `features/remove-operators.md` for why there is
> no operator/tenant vocabulary here any more.

- **Brand — "Space Stroker" (consumer) vs `SpaceStroker` (technical)** — as of the #74 frontend rebrand
  (shipped 2026-07-21, `specs/rebrand-space-stroker.md`), the **consumer-facing brand is Space
  Stroker**: the frontend wordmark/copy, the client-storage `space-stroker.*` keys, and the embed
  `source:'space-stroker'` (`'space-stroker-wallet'`). The **technical/backend
  name stays `SpaceStroker`** and is **still current** in this KB — C# namespaces (`SpaceStroker.*`), the
  domain source, DB, `spacestrokersettings.json`, `api.spacestroker.com`, and SignalR hub names are unchanged
  (the backend/infra rename is the separate follow-up card #75). So `SpaceStroker.*` /
  `spacestrokersettings.json` / backend-host references throughout these docs are accurate. **The
  `/space-stroker/*` route prefix is gone** — `home-dashboard` dropped it (the app is hosted at
  `app.spacestroker.com`, so the segment was redundant) and swept every path reference in `features/`,
  `conventions/` and `architecture.md`. Old URLs still resolve via a strip-prefix redirect shim.
  **`space-stroker.*` client-storage keys are unchanged** — they are not paths and were not touched.
- **Game** — one round of the game for a given game type. Has a status lifecycle
  (`WaitingBets → GameStarting → InProgress → Ended → RewardForWinners…`). Entity: `Game`.
- **GameParameters** — a "game type" / configuration (name, required amount, allowed characters,
  min players, choice length, lottery params). Flat — it belongs to no tenant, and every type settles
  in the one configured token, so the old `OperatorId`, `PaymentMethod` and `OperatorTokenId` columns
  are gone. Rows are seeded from `GeneralSettings:Operator:DefaultGames` (create-only by name) and
  managed in the admin portal. Entity: `GameParameters`.
- **Bet** — a player's wager on a `Game`: a `Choice` (the sequence they bet on), a token-native
  `Amount`, an optional `PlayerId` FK (NULL for anonymous free bets), a nullable indexed **`Signature`**
  (the on-chain payment signature — renamed from `ExternalTransactionReference` so the column matches
  the `paymentSignature` name the wire contract always used), a provably-fair **`Seed`** (a 64-char
  `HMAC-SHA256(clientSeed, Nonce)` hex — the raw client seed never reaches the server), and a per-bet
  **`Nonce`**. Entity: `Bet`. The on-chain payout/refund address is resolved from the linked
  `Player.ExternalPlayerId`, not stored on the bet row.
- **Player** — player identity, and there is exactly one source of it: the **connected wallet**.
  Fields: `ExternalPlayerId` (the wallet pubkey — uniquely indexed on its own), optional `DisplayName`,
  `LastSeenAt`, and the self-exclusion pair `SelfExcludedAt`/`SelfExcludedUntil`. No PII. Entity:
  `Player` (was `OperatorPlayer`, keyed `(OperatorId, ExternalPlayerId)`; the `Currency` column went
  with the `Standard` payment method). See `features/player-identity.md`.
- **Free bet** — dev/QA-only bet with `Amount = 0`, allowed only when backend `AllowFreeBets`, game
  `AllowFreeBet`, and (for UI) the frontend flag are all enabled. No debit/credit/refund and no ledger
  row; it also skips wallet-connect, so it carries no wallet and stays anonymous. Detected via
  `BetExtensions.IsFreeBet` (`Amount == 0`). See `features/free-bet-mode.md`.
- **AllowFreeBet** — bool on `GameParameters` (DB default `false`). Per game type; exposed as
  `allowFreeBet` on active-types API responses.
- **ComingSoon** — bool on `GameParameters` (DB default `false`), exposed as `comingSoon` on
  active-types responses. **Published but not playable:** the type stays in the catalogue (so the lobby
  and the manual can advertise it) while bets are refused with the vague `GameNotValidForBets`,
  roll-over and startup open no round, and Orbit will not bet it. ⚠️ **Distinct from `Active = false`**,
  which removes the type from the catalogue entirely. Flipped by config + a Consumers redeploy, never by
  an endpoint. See `features/games.md` §19.
- **AllowFreeBets** — bool on `GeneralSettings.Games` in `spacestrokersettings.json`. Global backend
  gate; must be `true` for the API to accept free bets.
- **Typer / SpaceStrokerTyper** — the domain service that "types" characters until a bet's choice appears;
  determines winners. Reads the game's committed **`ServerSeed`** and drives character selection from
  a **portable HMAC-SHA256 keystream** (rejection-sampled; no `System.Random`), with Stopwatch-scheduled
  wall-clock pacing and an HMAC-keystream fallback at the character cap.
  `SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs`. See `features/provably-fair.md` and
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
  (`space-stroker.clientSeed`), viewable/settable/rotatable in the header gear modal. Combined with a per-bet
  **`Nonce`** to derive `Bet.Seed = HMAC-SHA256(clientSeed, String(nonce))`. The raw client seed
  **never** leaves the browser; only the derived `Bet.Seed` reaches the server. Rotating/setting resets
  the nonce to 0. Frontend `useClientSeed`.
- **Bet nonce** — an incrementing per-bet counter (`Bet.Nonce`, `long`) under a client seed; makes each
  derived `Bet.Seed` unique and lets a bettor prove `Bet.Seed == HMAC(clientSeed, nonce)` without
  exposing the client seed.
- **Provably-fair verification** — reproducing a round's outcome from published inputs. Exposed by the
  reveal-gated, public `GET /games/{id}/verification`: pre-Ended returns `serverSeedHash` +
  ordered bet seeds/choices/nonces + `characterPool`/`queueLength`/`maxCharacters`; post-Ended adds
  `serverSeed` + `winningChoice` + `winnerDecidedByFallback` + `charactersTyped` (the raw `clientSeed`
  is never returned). Anyone replays the HMAC keystream to reproduce the winner. Players do it in the
  **`FAIR` side-panel tab** (instant commitment + own-bet-seed checks, opt-in Web Worker recompute,
  hidden when embedded, locked until the round ends) or in the standalone offline `public/verify.html`,
  which contacts nothing. There is still **no in-terminal recompute** — the game terminal only displays
  the commitment (during betting) and the revealed seed (after Ended). The endpoint never replays the
  stream and caches settled rounds. See `features/provably-fair.md` and `features/provably-fair-final.md`.
- **CharactersTyped** — how many characters a run actually produced, persisted on `Game` in the same
  `SaveChanges` as the winner. `winnerDecidedByFallback` is **derived** from it
  (`CharactersTyped >= MaxCharacters`) instead of replayed; `null` means **unknown**, never `false`. Also
  sizes the FAIR panel's opt-in recompute. See `features/provably-fair-final.md`.
- **Characters Away / near-miss** — a player-facing, display-only reveal: while a round runs, a
  bottom-docked terminal row per **own** bet choice shows how close the monkey has come to typing it
  (`> N AWAY!`, `away = L − k` where `k` = longest stream-suffix that is a prefix of the choice — a
  KMP tail-match). Rows climb, **reset and re-climb after a 2 s stall**, and **type out the full word**
  on a win. Computed entirely client-side from the existing `Terminal` stream (`utils/nearMiss.ts` +
  `hooks/useNearMiss.ts`, rendered in `GameTerminal.vue` beside the in-game `> POT:` line). No effect
  on outcomes, payouts, pacing, or the seed. See `features/characters-away.md`.
- **Operator** — **a configuration section, not an entity.** `GeneralSettings:Operator` in
  `spacestrokersettings.json` holds everything that used to hang off an `Operator` row: **`Token`**
  (cluster URLs, symbol, mint, sender, owner keypair, decimals, `CommissionPercentage`, cache TTLs),
  **`AdminToken`** (RS256 keypair PEM + TTL) and **`DefaultGames`** (the seeded game types). The
  deployment *is* the operator — onboarding another one means deploying again with a different
  `spacestrokersettings.json`. There is no `Operator` table, no `OperatorId`, no contract window and no
  tenant scoping anywhere. See `features/remove-operators.md`.
- **DefaultGames** — the game-type definitions under `GeneralSettings:Operator:DefaultGames`, seeded
  by `SpaceStroker.Consumers` at startup **create-only by name**: a type is inserted only when no row
  with that name exists, and an existing row is never rewritten (so a restart cannot resurrect an
  admin-deactivated type or mutate one under a live round). Three ship today — **Cosmic Sprint**
  (6-char race), **Hyper Roll** (player-vs-house, `IsHouseEdge`; replaced Beast Mode on 2026-09-18; three board modes AVERAGE / SUPER / HYPER since 2026-09-24 — see `features/hyper-roll.md` and `features/hyper-roll-modes.md`) and **Space Jackpot**
  (24h lottery). The exceptions to "never rewritten" are the **three single-field startup self-heals**:
  `AllowFreeBet = !IsProduction()`, `AllowOrbit` and `ComingSoon` from config. None of them touches
  `Active`, which is what preserves the resurrection guarantee.
- **Admin token** — an **RS256 JWT** with `scope = "admin"` and **no `sub`**, sent as
  **`X-Admin-Token`** on `/admin/*` REST calls. Signed with
  `GeneralSettings:Operator:AdminToken:PrivateKeyPem` and validated by `AdminTokenValidationMiddleware`
  against the matching `PublicKeyPem` — **no `iss` check, no contract check**. It is the **only**
  authenticated surface left; player endpoints are public. Entry:
  `/admin#admin_token=<jwt>`; `POST /dev/admin-token` mints one outside production. See
  `features/admin-page.md`.
- **IAdminContext** — scoped request identity set by the admin middleware. It no longer carries a
  tenant (there is none) — it exists so admin handlers keep a uniform request-identity shape.
- **The configured token** — there is exactly **one**, read from `GeneralSettings:Operator:Token`
  (`Symbol`, `MintAddress`, `SenderAccount`, `TokenOwnerPublicKey`, `TokenOwnerPrivateKey`, `Decimals`,
  `CommissionPercentage`, cluster URLs, cache TTLs). Every game settles in it, so every amount in the
  system is token-native and there is no per-game payment choice, no `PaymentMethod` enum, no
  registered-token entity and no settlement-strategy indirection — `ISolanaService` is called directly.
  Its owner private key is **not** encrypted at rest: it lives in config, and encrypting a config value
  with a key stored in the same config bought nothing once no user could type a private key into the
  admin portal (`ISecretProtector`/`AesSecretProtector` are deleted). Deployed environments inject it
  via Secret Manager; the committed file carries a placeholder.
- **The launch token** — the project's **pump.fun** coin (ticker `$STROKE`, a stand-in until launch).
  ⚠️ **NOT the configured token above, and never a stake** — rounds settle in SOL and this coin has no
  backend surface at all: no endpoint, entity, migration or settlement path. It exists only as copy on
  four frontend surfaces, gated by `VUE_APP_TOKEN_MINT` / `PUBLIC_TOKEN_MINT` (fail-closed, build-time).
  Its mint address and the **treasury wallet** (`VUE_APP_TREASURY_WALLET` / `PUBLIC_TREASURY_WALLET`, a
  public address holding ~50% of supply) are the only two values that come from env. See
  [`features/token-launch.md`](features/token-launch.md).
- **TransactionLog** — **THE ledger**: one row per money movement, and the only one
  (`CryptoTransactionLog` was merged in and dropped, as `RefundLog` had been before it). Columns:
  **nullable `BetId`**, `Type` (`Credit | Debit | Refund`), token-native `Amount`, `Signature` (this
  movement's own on-chain transaction), `RelatedSignature` (for a refund, the payment being reversed),
  `Wallet`, `MintAddress`, `SenderAccount`, `RawTokenAmount`, `Decimals`, `Reason`.
  **A placement is a `Debit`** — the stake leaving the player — and must never be read as a payout;
  `BetDomainService` never subtracted `Debit` rows, so reward and already-refunded math is unchanged
  (reward subtraction reads `Credit`, the already-refunded guard reads `Refund`). Any query filtering
  by bet id must tolerate the **null `BetId`** of a bet-less refund. See `features/remove-operators.md`.
- **TransactionLogType** — enum on `TransactionLog` (`Credit = 0`, `Debit = 1`, `Refund = 2`); `Credit` is
  the default so legacy reward rows keep their meaning.
- **NeedsRefunding / Refunded lifecycle** — `Bet.Status` values that are now a real, self-clearing
  refund lifecycle. `RefundInvalidBetConsumer` sets a bet to **`Refunded`** on any **successful**
  refund (all refund paths, incl. the inline lottery/deactivation/invalid-bet triggers — which used to
  leave it `Made`); a **failed** refund leaves the status so it is retried. A bet in **`NeedsRefunding`**
  is one flagged for refund but not yet paid back. See `features/refund-safeguards.md`.
- **Pending-refund sweep** — `PendingRefundSweepJob`, a Hangfire recurring job in `SpaceStroker.Consumers`
  (cron `0 */12 * * *`, id `pending-refund-sweep`) that loads every `NeedsRefunding` bet (any game
  state) and publishes a `RefundInvalidBetEvent` for each so the refund consumer pays it back and
  clears it to `Refunded`; failures retry on the next run. Fed by the admin
  **`POST /admin/bets/refund { paymentSignature }`** action (the only producer of `NeedsRefunding`),
  which marks a bet for refund by its indexed `Bet.Signature` (blocks Rewarded/Refunded/free;
  idempotent). See `features/refund-safeguards.md`.
- **PresetChoice** — predefined winnable choices for a game type.
- **Choice exclusivity / `ExclusiveChoice`** — in **free-text** game types (empty `PresetChoices`),
  each typed sequence is exclusive to the **first** player who bets it in a round; a different player
  taking the same normalized choice is rejected (`ErrorMessages.ChoiceAlreadyTaken`) and **auto-refunded**
  when the stake was already paid on-chain (reject-only for a free bet). Enforced by a cross-player check in
  `RegisterBetHandler.ValidateBetParameters` **and** a DB unique index `(GameId, ExclusiveChoice)` —
  `Bet.ExclusiveChoice` is the **trimmed** choice for free-text bets (casing not folded — uppercase is
  rejected as `InvalidCharacters`, so a valid choice is already lowercase), `NULL` for preset bets (NULLs
  distinct ⇒ preset games still share choices). Same-player repeats still hit the `DuplicateBet` path.
  Consequence: a free-text winning sequence maps to exactly one bet ⇒ **winner-takes-all** (no
  reward-math change). See `features/choice-exclusivity.md`.
- **Per-round bet limits** — two caps on how many bets one player may hold in a single round, applied
  to **paid, identified** bets only (free bets are exempt, which is what keeps solo free-bet QA
  working). In **preset** types: **one bet per player per round** whatever the choice
  (`ErrorMessages.OneBetPerPresetRound`). In **every** type: while a player is the round's **only**
  bettor, a second bet is rejected (`ErrorMessages.SecondBetNeedsAnotherPlayer`) until a bet from a
  different `PlayerId` exists — after which there is no further cap. Every persisted `Bet` row
  counts regardless of `Status`; the limits reset on rollover. Enforced in
  `RegisterBetHandler.ValidateBetParameters` only (no unique index, unlike choice exclusivity), with
  blocked bets refunded/rejected via the shared `RefundInvalidBet` path. See `specs/fix-bets-logic.md`.
- **LotteryParameters** — config for lottery-style games (`Frequency`, base `LotteryPot`); scheduled
  via Hangfire. The base is a per-round seed; the *running* un-won jackpot is carried on the game as
  **`CarriedExtra`**, not here. See `features/lottery-pot-stacking.md`.
- **CarriedExtra** — the running lottery jackpot stacked from prior **un-won** rounds, stored per
  game (`Game.CarriedExtra`, `decimal(18,9)`, default `0`) **on top of** the base `LotteryPot`.
  A **preset lottery** whose drawn choice had no bettors carries `prev.CarriedExtra + Σ losing stakes`
  into the successor; an **under-subscribed** round refunds stakes and carries the jackpot unchanged; a
  **paid win** (incl. a free-bet winner) resets the successor to `0`. Decided once at rollover by
  `LotteryPotCarry.NextCarriedExtra(Game)`. `0` for every non-lottery game. On admin deactivation
  it is **transferred** to an eligible active lottery (never destroyed).
  See `features/lottery-pot-stacking.md`.
- **Pot** — the amount winners split, shown as the `> POT:` line in the game terminal for **every**
  game type (free-type race, preset race, lottery). Client-side, display-only: races = `Σ bets`,
  lottery = `lotteryParameters.lotteryPot` base `+ CarriedExtra` (the stacked jackpot) `+ Σ bets`;
  seeded on load, accumulated live from the `/Bets` SignalR stream, reset/reseeded on rollover; free
  bets (`amount = 0`) add nothing. Rendered via `formatAmount`, which is token-native only (the
  configured token's symbol + decimals). Settlement math is separate —
  `BetDomainService.SetPotAmount` (`= Σ bets + LotteryPot + CarriedExtra`) remains the payout source of
  truth. See `features/game-pot-display.md` and `features/lottery-pot-stacking.md`.
- **Display name** — optional `Player.DisplayName` (1–20 chars, trimmed). Set via the customization
  modal (`PATCH /players/preferences`), which is **signature-gated**: the endpoint is public and a
  wallet address is not a secret, so the write carries an ed25519 signature over a purpose-scoped
  single-use nonce. Rendered on bet DTOs and recent-bets SignalR events for self and others; falls
  back to a truncated `externalPlayerId`. See `features/player-customization-modal.md`.
- **Wallet-proof nonce** — a single-use challenge for any write that must prove control of a wallet.
  `GET /players/nonce?wallet=…&purpose=…` issues it into Redis under
  `WalletNonce:{purpose}:{wallet}` (5 min), and the handler consumes it on use. It is
  **purpose-scoped** — today `preferences` and `self-exclusion` — so a challenge minted for one flow
  cannot be replayed against the other. *(There is no preferred display currency and no FX rate table:
  every game settles in the one configured token, so there is nothing to convert.)*
- **Game chat message** — a plain-text line in a **chat room** sent over
  `ActiveGamesHub.SendChatMessage(gameTypeId, gameId, displayName, text)`; attributed by client-resolved
  **display name**; carries a server `id` and `sentAt`; persisted to `GameChatMessage` (with `GameTypeId` and
  the sender's round `GameId`) for audit and as the history fallback. See `features/game-chat.md`.
- **Chat room** — the chat of one **game type** (SignalR group `chat:{gameTypeId}`), not of a round: it
  survives rollover and `Ended`, and on load shows the last 100 messages of the type within
  `Chat.RetentionDays` (`GET /games/{parametersId}/chat`). See `features/game-chat.md`.
- **Game-type stats** — all-time aggregated stats for one **game type** (`GameParameters`),
  served read-only by the public `GET /games/{parametersId}/stats`: biggest pot
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
  are tables under schema `transport` in the **`spacestroker_messaging`** database. At-least-once delivery,
  competing consumers via `SKIP LOCKED` + `LISTEN/NOTIFY`. The API is publisher-only; `SpaceStroker.Consumers`
  provisions the DB + schema at startup (`AddPostgresMigrationHostedService`). See
  `features/masstransit-postgres-transport.md`.
- **spacestroker_messaging** — the dedicated database (schema `transport`) holding the MassTransit SQL
  transport tables, on the **same** Postgres instance as the game DB (`spacestroker`). Its connection is
  `ConnectionStrings:Messaging`, reusing the game-DB user (which therefore needs `CREATE DATABASE`).
  Isolates queue churn from the game schema while keeping one instance/one bill. (The game DB itself was
  renamed `monkify → spacestroker`.)
- **IMessageBus / MessageBusPublisher** — the thin publish abstraction over MassTransit's
  `IPublishEndpoint.Publish` (`SpaceStroker.Infrastructure.Services.Messaging`). Renamed from the old
  transport-specific `IRabbitMQService` / `RabbitMQService` when the transport moved to Postgres.
- **Persistent / workload layers** — the two Terraform states in `monkify-meta/deploy/terraform/`. The
  **persistent** layer holds the Cloud DNS zone + domain + remote-state bucket (`prevent_destroy`); the
  **workload** layer holds everything a `teardown` may destroy (Cloud Run, Cloud SQL, Memorystore, LB,
  Cloud Armor, VPC, Artifact Registry, buckets). Teardown destroys only the workload, so the domain stays.
- **spacestroker-deploy / spacestroker-db** — the two local .NET 8 console tools under `deploy/tools/`.
  `spacestroker-deploy` runs the manual GCP deploy (`deploy [api|consumers|front|lp]` + `teardown`; syncs
  secrets → Secret Manager, builds/pushes, `terraform apply`). `spacestroker-db` is a read-write DB REPL over
  the Cloud SQL Auth Proxy that requires typing `YES` before any write.
- **CorsOriginResolver / GeneralSettings:Cors** — config-driven browser CORS allow-list read by both hosts
  (`GeneralSettings:Cors:AllowedOrigins`, default `https://app.spacestroker.com`, prod-overridable via env).
  Replaced the hardcoded origin.
- **`/rpc` proxy** — a frontend-facing `POST /rpc` JSON-RPC passthrough (`RpcController`,
  `SpaceStroker.Api`) that is the SPA's **primary and only** Solana transport wherever
  `VUE_APP_SOLANA_PROXY_URL` is set (every deployed environment); locally/on devnet, unset, the SPA
  uses the public endpoint directly. Forwards a strict **3-method** allowlist —
  `getLatestBlockhash`, `sendTransaction`, `getSignatureStatuses` — to the key-bearing
  `Token.ClusterUrl` and returns the response verbatim; gated by an env-independent Origin allowlist +
  method allowlist + per-IP rate limit, **not** launch-token gated. Distinct from the backend's own
  Solnet payout RPC clients (`ISolanaRpcClientProvider`). *It began (#87) as a 429-only failover behind
  a public primary, with 7 methods and a cooldown; `frontend-helius-gateway` (#99) made it primary and
  deleted the failover, because a production browser cannot use the public mainnet endpoint at all.*
  See `features/frontend-helius-gateway.md`.
- **`GET /solana/balance`** — the frontend-facing SPL balance read (`SolanaController`), so the browser
  never calls a Solana RPC for a player's balance. Redis-cached per `mint+wallet`; `/solana/balance/live`
  is the uncached variant (a separate **route**, because the rate limiter cannot budget a query-string
  value). Restricted to the **configured mint**; every rejection returns one vague message. A wallet with no associated token account reads `0`, not an error.
- **`confirmSignature` (confirmation poll)** — the frontend's bounded `getSignatureStatuses` poll that
  replaced `Connection.confirmTransaction`. A WebSocket subscription cannot be proxied and the public
  RPC's socket is unusable on mainnet, so desktop, the embedded popup and the mobile deeplink return
  all confirm this way. A **timeout is not a failed payment** — the transfer has most likely landed.
- **Self-exclusion** — a player-set, **per-wallet** block on betting, stored on the `Player` row as
  `SelfExcludedAt` (the flag) + `SelfExcludedUntil` (the expiry; **null with the flag set means
  permanent**). Blocks paid **and** free bets for that wallet, is **extend-only**, and **cannot be
  lifted early** — there is no support desk and no account recovery, so expiry is the only reversal
  offered. Setting it requires an **ed25519 signature** over a purpose-scoped single-use nonce, because
  wallet addresses are public and an unproven request would let anyone lock anyone out. It is now the
  **only** responsible-gaming control that runs: the opt-in per-operator RG pre-bet hook was deleted
  with operators (it resolved off an `Operator` row and no adapter was ever configured). See
  `features/legal-docs-self-exclusion.md`.
- **`ss_terms`** — the 18+/Terms acceptance cookie, scoped to `.spacestroker.com` so one acceptance
  covers `www` and `app`. Its value is a **version string**, not a boolean, so bumping it re-prompts
  everyone after a material change to the Terms. Sibling of `ss_consent`; the cookie banner waits for
  it, because consent to analytics must stay separable from accepting the Terms.
- **Quick Tour** — the pixel-art onboarding modal. Four of them: `overview` (the basics, on the first
  lobby visit) plus three **mode archetypes** matched by game **SHAPE** — `preset`, `race`, `lottery`.
  Shape, never `gameTypeId` or name: GUIDs change on every teardown+reseed and operators mint their own
  types, so neither can key content. A **preset lottery matches no archetype** and gets no auto-tour
  (deliberate no-op, not an error). Re-openable from the `How to play` ☰ row; absent when embedded.
  See `features/quick-tour-modals.md`.
- **Tour key** — a **versioned** tour identifier, `^[a-z]+@\d+$` (`overview@1`, `race@1`). The version
  is the *only* mechanism for re-showing a tour: bumping `TOUR_VERSIONS` in `constants/quickTours.ts`
  re-opens it once for everyone who dismissed the previous version. Everything ships at `@1`.
- **Seen-set (quick tours)** — the dismissed tour keys, held in **`localStorage` alone**
  (`space-stroker.tour.seen`). There is deliberately **no server column**: `PATCH /players/preferences`
  is signature-proved and display-name-specific, so persisting a dismissal would mean prompting the
  player to sign a Solana message to close an onboarding modal — and the overview opens on the lobby,
  where no wallet is connected. The accepted cost is that a tour can reappear on another browser; the
  rejected alternative was a new unsigned write keyed on a public wallet address. Every read/write is
  try/catch'd, so private mode degrades to "shows every visit" rather than throwing.
- **Bet-less refund** — a refund issued from `RefundInvalidBetEvent` alone, for a stake whose `Bet` row
  was never persisted (every inline rejection in `RegisterBetHandler` runs before `InsertBet`).
  Idempotency keys on `TransactionLog.RelatedSignature` — the payment being reversed — because the
  row's own `Signature` is the *refund's* transaction. The row carries a **null `BetId`**, which is
  precisely why the merged ledger had to make that column nullable. ⚠️ It has **no automated recovery** if its
  retries are exhausted: the 12h sweep and the admin refund endpoint both find work by bet.

- **Feedbacker Program** — the standing offer to pay players, in the project's token, for bugs
  (fixed prices), suggestions and things they make about the game. ⚠️ **An announcement, not a
  system:** it exists as one section on the landing page and one in the app's `/how-to-play` guide,
  both gated on the mint; **no code takes, judges or pays a submission** — that happens by hand in the
  community Telegram. Its budget is not a new pot, it **is** the 25% `Community & game rewards`
  treasury slice. ⚠️ It is also the **one surface exempted** from the "percentages only, never token
  counts" copy rule. See [`features/feedbacker.md`](features/feedbacker.md).

- **Season (Feedbacker)** — one budgeted window of that programme (`S1`…`S5`), each with a smaller
  token budget than the one before it. The current one is a single `currentSeason` index in the
  mirrored `FEEDBACKER` constants block, which drives the highlighted bar and the `YOU ARE HERE`
  chip. ⚠️ Not to be confused with a game round, a lottery draw window, or anything the backend knows
  about — nothing in `monkify-backend` models a season.

- **Orbit** — the custodial wallet plus the engine that plays from it: a house-held per-player
  balance, funded by on-chain deposits, withdrawable on demand, **auto-bet once per round**
  on the game type the player chose, and (since #194, 2026-09-24) spendable **by hand** on any paid game
  (see *manual Orbit wager*, `features/orbit-wallet.md`). All three cards are done (custody + engine + notifications), so
  shipped to `main` together as the `release/orbit` train on 2026-08-03; its player-facing surface was
  rebuilt by **#141** on 2026-08-06. See `features/orbit-custodial-wallet.md`,
  `features/orbit-auto-bet-engine.md`, `features/orbit-notifications.md` and
  `features/orbit-ui-overhaul.md`.
- **Orbit panel vs the `/orbit` page** — Orbit's two player-facing surfaces, with one job each. The
  **panel** is the compact `ArcadeModal` for money you move **mid-round**: turn Orbit on for the game
  you are already in, and add money — plus a status strip and the seam row. The **page** is everything
  else, as a four-step collapse-to-dashboard list (`Game → Money → Stop rules → Telegram`), and **no
  control on it opens a modal**. The panel is the short version of the page, never a second copy.
  ⚠️ The split's accepted cost: the **cash-out floor** is readable but not editable from inside a round.
- **`.arcade-frame--orbit`** — Orbit's own page material (void-deep fill, 2px violet-deep frame), the
  **third** panel beside grey and black. It exists because `/orbit` needed the black panel's depth but
  carries a stop-loss, and black is documented as *never a warning about money* — so Orbit got its own
  material rather than bending that rule. **Scope is the `/orbit` page only.**
- **Orbit word claim** — `OrbitChoiceClaim`, unique on `(GameParametersId, Choice)`. The row's
  existence **is** the claim. An Orbit player's optional fixed word becomes a **standing, cross-round**
  reservation on that free-text game type, blocking other Orbit players **and human bettors** in every
  round — including rounds Orbit does not bet in, because a conditional claim could be lost
  mid-losing-streak and make STOP dangerous. Released on change, clear or **disable**; re-validated on
  re-enable rather than silently restored. **Preset types are never claimed.** A blocked human sees the
  ordinary `ChoiceAlreadyTaken`, byte-identical to a same-round collision, so Orbit stays invisible.
  Distinct from the per-round reservation in `features/choice-exclusivity.md`, which frees up each round.
- **STOPPED vs paused (Orbit)** — two different states, deliberately named apart.
  **STOPPED** (`OrbitEnrollment.Stopped`) is the player's deliberate pause: it skips the planner and
  **nothing else** — no forced cash-out, so no cool-off is owed, the word claim survives, deposits,
  cash-outs and notifications keep working, and resuming is instant. **Paused** stays the name of the
  *automatic* out-of-funds state (`isPausedForFunds`, client-derived), so no shipped copy moved.
- **Orbit cash-out floor** — `OrbitEnrollment.CashOutFloor`: the balance falls **to or below** it,
  Orbit **stops betting**, and what is left goes home. The complement of the "cash out at" threshold,
  which only ever fires on a **win** and so can never catch a balance losing its way down. Its own
  `OrbitCashOutSource.Floor`, because a ledger that cannot tell "I won enough" from "I have lost
  enough" cannot explain to a player why their money came back.
  ⚠️ **It is two rules, not one** (2026-08-10,
  [`specs/orbit-stop-loss-trigger.md`](specs/orbit-stop-loss-trigger.md)). It is evaluated once the
  round **settles** — never at placement, since a stake leaves the balance immediately — **and** it
  gates the planner, so a player at or below their floor is not dispatched into another round.
  Settlement alone was not enough: the successor round opens and its stake is signed *before* the
  previous round settles, so the floor could only ever fire with the next bet already committed —
  paying the player one stake short and letting one more round run. Strictly **above** the floor is
  still playing, and that comparison must read the same in both places.
- **Orbit settled balance** — `IOrbitLedgerService.ResolveSettledBalance`: the ledger balance **plus**
  the stakes of Orbit bets whose round is still in `Game.ActiveGamesStatus`, i.e. what the player
  holds counting money still in play. Every Orbit stop rule **decides** on this; nothing **sizes**
  money with it — cash-outs are still computed from the raw ledger, because a riding stake is not
  available to send home.
- **Orbit notification channel** — a private per-player SignalR group on `ActiveGamesHub`
  (`orbit/{playerId}`), joined with the Orbit session token and named **server-side** from it. Keyed
  on the `PlayerId` and never on the wallet: a wallet address is public on every bet list, so a
  wallet-keyed group would let anyone watch a stranger's wins and learn they use Orbit at all. Its
  payload carries the amount and the game type but **never a balance**.
- **Manual Orbit wager** — a bet the player places by hand and pays from their Orbit balance
  (`POST /orbit/wagers`, session-only, no per-bet signature; `PlaceManualOrbitBetConsumer`). Its `Bet` is
  `FundedByOrbit = true`, `OrbitAutoPlaced = false`. Wins and refunds return to the Orbit balance; stop
  rules, the "Orbit won" toast and the start gate ignore it. The outcome is polled by `requestId`
  (numeric `Pending 1 / Placed 2 / Refused 3 / NotFound 4`). See `features/orbit-wallet.md`.
- **`OrbitAutoPlaced`** — `Bet` flag: the Orbit **engine** placed this bet. Every auto-bet reader (start
  gate, house-seed count, stop rules, win toast, $STROKE exclusion, `BetDto.ViaOrbit`) reads it, never
  `FundedByOrbit`, which now only says the money is in custody.
- **Orbit stake reconciler** — `OrbitStakeReconciliationJob` (Consumers, Hangfire
  `orbit-stake-reconciliation`, every minute). It decides Orbit placement claims left `Open` by an
  in-doubt transfer: never sent or failed/expired → ledger credit once; landed → on-chain return to
  custody once; a bet carries it → link. Only a finalized chain answer decides.
- **Orbit placement claim** — the `BetDebit` ledger row itself, made unique per `(pool, player,
  round)` by `OrbitTransaction.GameId` for auto bets, and per request by `PlacementRequestId` for manual
  wagers. Since #194 it has a `ClaimState` (`Open` / `Settled`) and carries the transfer's `Signature`
  and `LastValidBlockHeight`, stamped **before** the send; every later decision on it is a conditional
  update, and the bet that spends it links it inside its own insert transaction. Written in the same transaction as the atomic debit and
  **before** any transfer is signed, so a redelivered placement loses on `23505` having moved no
  money. Losing the claim is *not* a failure: someone else is placing that bet.
- **Orbit start gate** — `Game.ExpectedOrbitBets` + `Game.OrbitGateExpiresAt`, which hold a round
  from starting until the Orbit bets it is counting on have landed. Armed **only when Orbit is
  load-bearing** — if humans already meet `MinimumNumberOfPlayers`, nobody waits on a bot. A ceiling
  (45s), never a trap: a definitive placement failure decrements it and wakes the round immediately.
- **Orbit-funded bet** — a `Bet` with `FundedByOrbit`, staked by the house out of the player's Orbit
  balance, either by the engine (`OrbitAutoPlaced`) or as a manual Orbit wager. Indistinguishable from a human's to every other player, and it gets **no exemption** from
  the per-round bet limits or choice exclusivity. Its win and its refund both go back to the Orbit
  account, never the personal wallet.
- **Orbit pool** — the `(OrbitAccount, MintAddress)` pair a balance or ledger row belongs to. Rotating
  the configured Orbit keypair or mint starts a **new** pool at zero and leaves the old one intact and
  withdraw-only, so a balance is never silently re-pointed at different money.
- **Orbit session** — a stateless HMAC token (`X-Orbit-Session`, ~7 days) proving a caller owns a
  wallet. Authorizes **reads only**: every movement of money additionally needs a fresh,
  purpose-scoped ed25519 signature. Not revocable before expiry.
- **Reserve (a cash-out)** — writing the `Pending` ledger row and debiting the balance in one
  transaction at *request* time, so the money is immediately unavailable to anything else. The
  opposite of paying on confirmation, which would let a later operation spend money already promised.
- **Release train** — a `release/<name>` branch that several cards merge into, landing on `main` only
  once the whole set is coherent. Used for Orbit. A card on a train stays in **Implemented**, because
  its code is genuinely not on `main`.
- **Ghost wallet pool** — the set of wallets we control that hold the team's own token position, listed
  once in the git-ignored `tools/secrets.json` and shared by both console tools: `spacestroker-fund-wallets`
  fills them with SOL (`features/coinbase-wallet-funding.md`), `spacestroker-snipe-token` spends it
  (`features/ghost-wallet-snipe.md`). Each entry's **`alias`** is its identity and its execution order;
  the sniper is the only tool that ever binds a wallet's private key. Nothing about this pool touches
  the game, the ledger or any player-facing path.
- **Bonding curve (pump.fun)** — the launch venue our token trades on before it graduates: a
  constant-product market held in one on-chain account per mint, priced over **virtual** reserves, with
  the platform and creator fees taken on top of each trade. Its rates live in the program's `global`
  account (95 + 5 basis points on 2026-08-11) and are read at runtime, never hardcoded. A curve is not
  necessarily quoted in SOL — see `features/ghost-wallet-snipe.md`.
- **Graduation** — the moment a bonding curve fills and liquidity migrates to an AMM. The curve account
  is marked `complete` and its sellable reserves go to zero, so **buys against it stop working**.
  `spacestroker-snipe-token` refuses to start against a graduated token and stops cleanly if graduation
  happens mid-run; it deliberately has no AMM path.
