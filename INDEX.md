# SpaceStroker — Project Knowledge Base (INDEX)

> **Read this file first on every task.** It is the map of the SpaceStroker knowledge base.
> It tells you where things live, what each document is for, and the rules you must never break.

SpaceStroker is a real-time, multiplayer betting game where a simulated "monkey" types random
characters until a player's chosen word/sequence appears. Players bet on a sequence; when the
typer produces a matching sequence, the matching bets win and are paid out.

## How to use this knowledge base

1. **Always start here.** Then read `constraints.md` before writing or running anything.
2. Read `architecture.md` to understand the systems, repos, and data/message flow.
3. For domain vocabulary, read `glossary.md`.
4. For "how we write code here", read the relevant file in `conventions/`.
5. For a specific feature, read the relevant file in `features/`.
6. The authoritative, deep design docs are in `specs/`. The `features/` files are distilled
  summaries that point back into `specs/` and the live code.

## Implementing from a spec

When you are **implementing** a feature from a spec in `specs/` (not just reading for context):

1. Read the **target spec** end to end, plus its matching `features/` summary if one exists.
2. Read **every other doc in `features/`** — and skim related shipped specs — so you know what
   already exists in production (games, operators, payment flows, player identity, free-bet mode,
   etc.). The spec you are building is one piece of a shared system, not an isolated module.
3. While designing and coding, actively look for **cross-feature interactions**: shared entities
   and tables, request headers, payment and payout paths, game/bet lifecycle, operator scoping,
   frontend state and composables, migrations, SignalR contracts, and public API shape.
4. If you find a **conflict, ambiguity, or gap** between the spec you are implementing and
   **existing behavior documented elsewhere** — and the target spec does **not** already resolve
   it — **stop and ask the user**. Do not guess, silently pick one behavior, or paper over the
   mismatch with a local workaround.
5. When you ask, be concrete: name the existing feature, describe the conflicting behavior, quote
   or summarize what the spec says (or omits), and offer one or two sensible resolution options if
   you have them. Wait for an answer before proceeding on that point.

Treat unresolved cross-feature conflicts as **blockers**, not implementation details you can defer.

## Keeping the knowledge base current

Every time a feature gets merged into `main`, this repository's knowledge base **must be updated**
so it reflects the application's current business logic and behavior. That includes (as relevant)
`features/`, `conventions/`, `glossary.md`, `architecture.md`, and any other docs that describe
how the system works — not only `specs/`. Stale docs here mislead agents and humans alike; treat
knowledge-base updates as part of shipping the feature, not optional follow-up work.

## Repositories (in this workspace, `e:\SpaceStroker`)


| Folder             | What it is                                                               | Covered here       |
| ------------------ | ------------------------------------------------------------------------ | ------------------ |
| `monkify-backend`  | .NET 8 backend: API, background consumers, domain, infrastructure, tests | Yes (primary)      |
| `monkify-front`    | Vue 3 + TypeScript frontend (the game UI)                                | Yes (primary)      |
| `monkify-lp`       | Landing page (Astro 5 + Vue 3 + Tailwind v4)                             | LP relaunch: `features/lp-space-stroker-relaunch.md` |
| `monkify-meta`     | **This knowledge base**                                                  | —                  |


> Note: when the user says "the backend" they mean `monkify-backend`; "the front" / "the UI"
> means `monkify-front`. (The repo is `monkify-backend`, not "monkify-back".)

## Map of this knowledge base


| File                        | Purpose                                                                                  | Status               |
| --------------------------- | ---------------------------------------------------------------------------------------- | -------------------- |
| `INDEX.md`                  | This map. Read first.                                                                    | Drafted              |
| `architecture.md`           | Repos, services, MassTransit Postgres SQL transport, PostgreSQL, Redis, SignalR, Solana, EF, payment processors | Drafted              |
| `constraints.md`            | Non-negotiable rules: secrets, branches per repo, unit tests, migrations                 | Drafted              |
| `glossary.md`               | Game, Bet, Operator, Typer, GameParameters, etc.                                   | Drafted              |
| `conventions/backend.md`    | EF inline in DbContext, migrations in Consumers, MediatR handlers, MassTransit consumers | TODO (stub)          |
| `conventions/frontend.md`   | Vue 3, composables (`src/hooks`), types in `src/types`, axios service layer              | TODO (stub)          |
| `features/games.md`      | Game lifecycle, the typer, rewards                                                    | Drafted              |
| `features/operators.md`     | Multi-tenant operators; distilled from `specs/operator-feature.md`                       | Shipped              |
| `features/free-bet-mode.md` | Dev/QA free bets without Solana payment; distilled from `specs/free-bet-mode.md`         | Shipped              |
| `features/operator-player-identity.md` | Unified `OperatorPlayer` model; remove `Bet.Wallet`; distilled from `specs/operator-player-identity.md` | Shipped              |
| `features/typer-pacing-scalability.md` | Wall-clock typer pacing, fallback winner, duration bounds; distilled from `specs/typer-pacing-scalability.md` | Shipped              |
| `features/launch-token-validation.md` | Signed casino launch flow (`X-Launch-Token`); `Operator.PaymentModel`; replaces `x-operator-id` | Shipped              |
| `features/operator-wallet-api.md` | Regulated (Model A) operator wallet debit/credit/refund; unified `TransactionLog`; commission removed | Shipped              |
| `features/dev-qol-stuff.md` | Dev QOL: operator from token (not URL), dev launch buttons (`VUE_APP_DEV_LAUNCH`), `SpaceStroker.Tests.Integration` suite | Shipped              |
| `features/rg-pre-bet-hook.md` | Opt-in per-operator responsible-gaming pre-bet check (`IPreBetCheckService`); allow/deny/fail-closed before the wallet debit | Shipped              |
| `features/player-customization-modal.md` | Header-gear modal: display name (all operators) + preferred display currency (regulated only); `GET /fx/rates`, `GET /players/preferences`, client-side USD→fiat rendering; display-only | Shipped              |
| `features/game-chat.md`  | Per-game chat on `/game` via `ActiveGamesHub`; display-name identity, DB audit persistence + daily purge, client 200-char/1s limits, no history replay | Shipped (v2)         |
| `features/operator-admin-page.md` | Operator self-service admin portal at `/space-stroker/admin`: admin-scoped RS256 token (`X-Admin-Token`), create/list/deactivate game types (all 3 modes), deactivate-or-create (no edit), dev header icon → `POST /dev/admin-token` | Shipped              |
| `features/sales-technical-documents.md` | B2B SaaS sales doc set in `sales/` — per-model pitch + under-NDA integration overview (Model A/B), anti-clone playbook; EN + PT-BR | Shipped              |
| `features/game-pot-display.md` | Frontend-only: the `> POT:` line is shown for **all** game types (races + lottery), seeded from `Σ bets` (+ lottery base), live via `/Bets`, visible during/after the run; distilled from `specs/game-pot-display.md` | Shipped              |
| `features/characters-away.md` | Near-miss reveal: bottom-docked `> N AWAY!` row per own choice (KMP tail-prefix `away = L − k`) that climbs, loops every 2s, and types out the winner on a win; in-game `> POT:`; backend emits the fallback-decided winner as a final terminal batch | Shipped              |
| `features/audio-overhaul.md` | Frontend-only AI (Suno) arcade-chiptune audio: idle/starting/random-running background per game status, own-bet/button/error SFX, bettors-only victory/lose, menu loop on the games page, header mute+volume (`localStorage`); 9 mp3 assets (kept `error1.wav`). Proximity "coin" bleeps dropped. Also: `/`→`/space-stroker/game` redirect + game-connection teardown fix | Shipped              |
| `features/payment-methods.md` | **Per-game** payment: `GameParameters.PaymentMethod` (`Standard`=off-chain USD wallet / `SolanaToken`=on-chain per-operator registered token, key AES-GCM encrypted at rest); unified `IPaymentMethodService` strategy+resolver + availability gating; ledger split (`Bet`/`TransactionLog` `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`); admin token CRUD. **Removes `Operator.PaymentModel`** | Shipped              |
| `features/iframe-embedding.md` | Frontend-only: a **regulated (Model A)** casino embeds the game in an `<iframe>` — `useEmbed` detection, secret-free `{source:'space-stroker'}` postMessage lifecycle API, embedded 401→`token-expired`+"relaunching" (never `/error`), fills the frame, permissive `frame-ancestors *`, example page + `EMBEDDING.md`; distilled from `specs/iframe-embedding.md` | Shipped              |
| `features/iframe-crypto-games.md` | Frontend-only, **stacked on #3**: paid **`SolanaToken`** bets inside the iframe via a **same-origin wallet-bridge popup** (`/space-stroker/wallet-bridge`) that returns the on-chain signature+wallet (`source:'space-stroker-wallet'`); popup opened synchronously on click, token-native amounts, no new parent contract; distilled from `specs/iframe-crypto-games.md` | Shipped              |
| `features/game-type-stats.md` | Per-game all-time stats per game type via `GET /games/{parametersId}/stats` (biggest pot, biggest single payout, rounds played, total paid out, hottest choice for preset types) — **on-demand aggregate + Redis cache, no new entity/migration**; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type, operator-scoped/vague; rendered in a lazy **STATS** tab of the `/game` right-side panel (bundled with **CHAT** via `GameSidePanel`) | Shipped              |
| `features/provably-fair.md` | Commit-reveal `ServerSeed` (committed on the first bet, revealed after `Ended`), portable HMAC-SHA256 keystream RNG + labelled fallback, Stake-style client seed + `Bet.Nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /games/{id}/verification` + offline `public/verify.html`; terminal shows commitment/reveal only. **Verification surface superseded by `features/provably-fair-final.md`** | Shipped              |
| `features/provably-fair-final.md` | In-app verification: lazy **`FAIR`** tab in `GameSidePanel` (hidden when embedded, locked until the round ends) with two instant checks (commitment + own bet seed) and an **opt-in cancellable Web Worker** outcome replay, rollover retention, COPY VERIFICATION JSON, and a download link + instructions for the **kept** offline `public/verify.html`. Backend: `Game.CharactersTyped` persisted at run end → `winnerDecidedByFallback` **derived, never replayed**; payload **cached only once the round is final** (`GameVerification:{gameId}`, 12h), operator check **before** the cache read. Current round only | Shipped              |
| `features/games-history.md` | Standalone **Games History** page (`/space-stroker/game/history`, from a new header **menu button**: Settings + History + dev Admin; audio + wallet stay on the header). Read-only `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` → `PaginatedList<GameHistoryDto>` — **one game type at a time** (dropdown), **only `RewardForWinnersCompleted`** rounds, newest first, `pageSize`≤30, **"mine only"** toggle. Rows: ended date · type name · winning choice · pot (`Σ paid bets`+lottery base+carried) · **bet count** (free incl.) · winner count · short `serverSeedHash` · nullable **own-bet** block (choice/stake/won/payout) — no character stream. Money rules mirror **game-type-stats** (free bets excluded from pot/winners, single currency per type, `useDisplayCurrency`). Public page **Redis-cached 5 min** per operator+type+page+size; own columns joined **live** (cloned rows). "Mine" identity = `wallet` ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`; vague `ParameterNotFound`. **Verify = link-out only** to `/verification` + `public/verify.html`. **No entity/migration.** | Shipped              |
| `features/solana-connection-rpc.md` | **`monkify-backend` + `monkify-front`:** keep the game playable under RPC rate limits. Frontend `useConnection` keeps the **public RPC as primary**; a custom `fetch` detects **HTTP 429** and `withFailover` retries the same call on a new backend **`POST /rpc`** proxy, sticky for a cooldown (`VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS`), then drifts back — **429-only** (other errors surface as-is); `confirmTransaction` **always stays on the public connection**; unset `VUE_APP_SOLANA_PROXY_URL` ⇒ no-op passthrough. `sendToken` reworked: **single** idempotent-ATA + transfer (**one** approval), optional ComputeBudget **priority fee** (0 disables), integer/BigInt amount math, sign-once + `sendRawTransaction` (`preflightCommitment:'confirmed'` to match the blockhash). Backend `RpcController` = thin JSON-RPC passthrough to the reused key-bearing `Token.ClusterUrl`, gated by env-independent **Origin allowlist + 7-method allowlist + per-IP `*:/rpc` rate rule**, **not** launch-token gated; key never logged (`MaskRpcUrl`); **no entity/migration/consumer/secret**. | Shipped |
| `features/wallet-display-name.md` | SolanaToken player display-name/preferences key off the **connected wallet** everywhere: backend prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`); `InsertBet` carries `externalPlayerId` on live notifications; frontend reactive per-wallet load + "Connect wallet to customize" gate + currency hidden for SolanaToken | Shipped              |
| `features/lottery-pot-stacking.md` | Un-won lottery pots **stack** into the next round via `Game.CarriedExtra` (preset no-winner draw carries losing stakes; under-subscription carries the base unchanged; paid win resets). `pot = Σ bets + LotteryPot + CarriedExtra`; carry decided once at rollover by `LotteryPotCarry`. Admin **deactivation transfers** the un-won pot to an eligible active lottery (same operator/method/token) or blocks. Player `> POT:` reflects the jackpot; the dev 1-min preset lottery `6L Minute Menagerie` is **no longer seeded** (2026-07-23) | Shipped              |
| `features/main-production-operator.md` | Default **production** operator **"Main"** (fixed RFC-4122 id, registered Solana token from `GeneralSettings.Token`, 3 SolanaToken default types: `6X Main Sprint` race / `6X Main Derby` preset "horse race" / `6X Main Millions` 24h lottery, free bets off) seeded in **all** envs (**prod = Main only**; non-prod also kept DEV Cassino — **DEV Cassino removed in ALL envs by `main-only-player-entry`, 2026-07-24; entry is player-only, free bets ON outside prod**). Production-enabled `POST /launch-token/default` mints an **anonymous Main-only** launch token; SPA auto-mints on no-token (never embedded), silent re-mint on 401, real tokens win; prod `/space-stroker/game` → games. No schema/migration | Shipped              |
| `features/mobile-layout.md` | **Frontend-only:** responsive **mobile layout** (`< lg`/1024px) for the games lobby + game screen, portrait + landscape phones, standalone & embedded. Lobby: 1-col condensed cards. Game: a `GAME \| CHAT·STATS` segmented view (absolutely-stacked so a hidden view can't intercept taps) — full-width fluid terminal + compact bet panel (**no amount field**, lever = PLACE BET); full-width chat/stats with an unread dot; monkey hidden. Header inline; customization modal scrolls + seed row wraps. Mobile SolanaToken bets via an **"OPEN IN WALLET APP"** deep-link (Phantom/Solflare in-app browser) reusing `sendToken`. `useIsMobile`/`useChatUnread`/`GameMobileTabs`/`walletDeepLink`. No backend. | Shipped              |
| `features/terraform-gcp-deploy.md` | **Infra/DevOps:** production **GCP** deploy (Terraform + two local C# console tools in `monkify-meta/deploy/`, driven by an unversioned `secrets.<env>.json`). Cloud Run (API public autoscale / Consumers internal, CPU-always-on, before-API) + GCS/CDN front·lp + Cloud SQL 16 (`spacestroker`+`spacestroker_messaging`, private) + Memorystore + Artifact Registry + HTTPS LB·Cloud Armor + Secret Manager + Cloud DNS/managed-TLS; TF **persistent** (DNS/domain `prevent_destroy`) vs **workload** (teardown target, keeps domain). Backend Cloud-Run readiness (no game change): `/health` on Consumers (API already had one via `HomeController`), `UseForwardedHeaders`, config-driven CORS (`GeneralSettings:Cors`, default `app.spacestroker.com`). Real `apply` is an ops step. | Shipped |
| `features/lp-space-stroker-relaunch.md` | **Frontend-only (LP):** the `monkify-lp` landing page relaunched as **SpaceStroker**, **token-free/USDC** — SPACE·STROKER wordmark, USDC stake, **Tokenomics removed** → honest **"No token. Yet."** note (token maybe later, no ticker/CA), roadmap de-tokenized + future-token tease, "Buy" gone / "Play now" + community "Join the troop", trust points (provably-fair + instant on-chain payouts + auto-refund). App URL + socials are flagged `TODO(ship)` placeholders. Art/story/skin unchanged. | Shipped              |
| `features/lp-redesign.md` | **Frontend-only (LP):** selective **visual redesign** of the SpaceStroker LP (arcade-cabinet) — cherry-picks a Claude mock into the real Astro/Vue: **two-column hero** (coin + mascot chimp + comet) with a cosmetic local **LIVE typing pill** (no network/money claim); **3-card how-it-works + full-width FAIR PLAY bar**; a new image-forward **"SELECT YOUR GAME"** `#modes` section; restyled **EDSON.LOG** story (painted slides kept); **two-column "No token. Yet."** card; header **coin + Modes/Story/Roadmap/Troop + PLAY NOW**; footer **"BET ON THE MONKEY"** tagline; simplified no-emoji roadmap (perk box removed); **richer per-section space art** (no wins/pot ticker). Brand / token-free USDC / trust points / `TODO(ship)` placeholders preserved; no palette drift. | Shipped              |
| `specs/operator-feature.md` | Full implementation spec for Operators                                                   | Shipped              |
| `specs/free-bet-mode.md`    | Full implementation spec for dev-only Free Bet Mode                                      | Shipped              |
| `specs/operator-player-identity.md` | Operator-scoped unified player model (`OperatorPlayer`, remove `Bet.Wallet`)     | Shipped              |
| `specs/typer-pacing-scalability.md` | Typer wall-clock pacing — consistent duration across configs and servers           | Shipped              |
| `specs/launch-token-validation.md` | Signed casino launch flow — RS256 launch token, `Operator.PaymentModel`, prefs endpoint | Shipped              |
| `specs/operator-wallet-api.md` | Model A (Regulated) operator wallet debit/credit/refund adapters; unify `TransactionLog`, drop `RefundLog`, remove commission | Shipped              |
| `specs/dev-qol-stuff.md`    | Dev QOL: operatorId out of game URL (decode token `iss`), dev payment-model launch buttons, Testcontainers game integration tests | Shipped              |
| `specs/rg-pre-bet-hook.md`  | Responsible-gaming pre-bet check hook (`IPreBetCheckService`, opt-in per operator, fail-closed) wired into `RegisterBetHandler` before the wallet debit | Shipped              |
| `specs/sales-technical-documents.md` | Non-code: B2B SaaS sales docs in `sales/` — per-model (Model A/B) pitch + under-NDA integration overview (grounded in shipped code) under `model-a/`+`model-b/`, plus a single anti-clone playbook; EN + PT-BR; placeholders only, paid steps deferred | Shipped              |
| `specs/player-customization-modal.md` | Header-gear customization modal: edit display name (all operators) + preferred display currency (regulated only); FreeCurrencyAPI USD-base rates cached in Redis 6h, `GET /fx/rates`, new `GET /players/preferences`, PATCH allow-list, `DisplayName` on bet DTO/SignalR; display-only (settlement stays USD), no migration | Shipped              |
| `specs/game-chat.md`     | Per-game chat over `ActiveGamesHub` (`SendChatMessage` → `{gameId}/ChatMessage`); **v2:** nickname = player display name (client-resolved, crypto must connect to send), DB persistence (`GameChatMessage`) via event→consumer + daily purge (`Chat.RetentionDays`, default 7), client-only 200-char/1s limits, no history load (late joiners see nothing), read-only on end, reset on rollover; right-sidebar `GameChatPanel` | Shipped (v2)         |
| `specs/operator-admin-page.md` | Operator self-service admin portal (`/space-stroker/admin`): admin-scoped RS256 token (`X-Admin-Token`, reuses `LaunchPublicKey`; not contract-gated), create/list/deactivate game types (all 3 modes; deactivate-or-create, no edit), dev header icon → `POST /dev/admin-token`; `AllowFreeBet` hidden | Shipped              |
| `specs/game-pot-display.md` | Frontend-only: show the `> POT:` line for **all** game types (races + lottery), not just lottery; generalize `currentLotteryPot` → `currentPot` in `useGame.ts` (seed `Σ bets` + lottery base only for lottery, accumulate live via `/Bets`), move the POT `<p>` out of the lottery-only block in `GameTerminal.vue` + a second POT line in the running/ended view (AC #8); reuse `formatAmount`; no backend/DB/SignalR change | Shipped              |
| `specs/characters-away.md`  | Characters Away near-miss reveal. Spec body is the original 3/2/1 pinned-milestone design; **shipped** behaviour (see spec Post-ship notes + `features/characters-away.md`) is a bottom-docked closest-approach row per own choice (KMP `away = L − k`) that climbs + loops every 2s + types out the winner, plus in-game `> POT:` and a backend fallback terminal reveal | Shipped              |
| `specs/audio-overhaul.md`   | Audio overhaul with AI — **(1/3) manifest & Suno-generation spec**. Sound manifest + per-item Suno prompts + the frontend integration contract for cards 2 (#34) & 3 (#35). **Shipped reality differs:** the 3/2/1 proximity "coin" bleeps were dropped (Suno couldn't produce them) → **9 generated mp3s, not 12**, and no proximity audio. See `features/audio-overhaul.md`. Frontend-only; branch `feature/audio-overhaul` | Shipped              |
| `specs/audio-overhaul-generation.md` | Audio overhaul — **(2/3) asset generation spec**. Suno-MCP + ffmpeg generation pipeline with approve-each review. **Shipped reality:** generated **manually in the Suno UI** (MCP ran out of credits) and post-processed with ffmpeg; **9 assets** committed to `monkify-front/src/assets/audios` (kept `error1.wav`, dropped `success1.wav` + old `.wav`s); the 3 coin bleeps were cut. See `features/audio-overhaul.md` | Shipped              |
| `specs/audio-overhaul-wiring.md` | Audio overhaul — **(3/3) wiring spec**. `helpers.ts` master gain/mute, idle + one-shot Game Starting + random running (`GameAudio.vue`), own-bet/error/button, bettors-only victory/lose, header mute/volume, timing fixes; asset swap. **Shipped extras beyond spec:** proximity bleeps **omitted** (assets dropped); added a menu loop on the games page, a `/`→`/space-stroker/game` redirect, and a game-connection teardown fix. See `features/audio-overhaul.md` | Shipped              |
| `specs/payment-methods-refactor.md` | Removes operator `PaymentModel`; per-game **`PaymentMethod`** (`Standard`/`SolanaToken`); per-operator **registered Solana tokens** (encrypted key) via admin portal; unified `IPaymentMethod` strategy+resolver; ledger split — `Bet`/`TransactionLog` carry `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`. **Shipped** (encryption deviates: AES-GCM/config passphrase, not Data Protection) | Shipped |
| `specs/iframe-embedding.md` | Frontend-only: let a **regulated (Model A)** casino embed the game in an `<iframe>`. New `useEmbed` (`window.self!==window.top` \| `?embed=1`) + `utils/embed.ts` postMessage emitter (`{source:'space-stroker',type,payload}`, `targetOrigin:'*'`, secret-free, USD); embedded 401/expired/inactive-operator emits `token-expired` + light "relaunching" state instead of `/error` (branch in `mainService.ts` + `useOperator.ts`); lifecycle emits `ready`/`game-started`/`bet-placed`/`bet-settled`(bettors-only)/`game-ended`/`resize`(hint) from `useGame.ts`+`App.vue`; fills the frame (`main.css`); permissive `frame-ancestors *` in `nginx.conf`; static `public/embed-example.html` + `EMBEDDING.md`. **No backend.** Branch `feature/iframe-embedding` | Shipped              |
| `specs/iframe-crypto-games.md` | Frontend-only, **stacked on #3**: make paid **`SolanaToken` (on-chain)** bets work inside the #3 `<iframe>`. Extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move into a **same-origin popup** on the game host (route `/space-stroker/wallet-bridge`, `WalletBridgePage.vue`) that `postMessage`s the signature + wallet back (`source:'space-stroker-wallet'`, `utils/walletBridge.ts`); `useGame.bet()` acquires the signature in-frame **or** via popup (`useEmbeddedWallet.ts`), popup opened **synchronously** on click (blocked → visible affordance); relax the `GameConsole` `connected` gate when embedded; #3 lifecycle payloads made **token-native** (never USD). **No backend.** Branch `feature/iframe-crypto-games` | Shipped              |
| `specs/game-type-stats.md` | Per-game general stats per game type (all-time): `GET /games/{parametersId}/stats` → biggest pot, biggest single payout, rounds played, total paid out, hottest choice (most-picked, preset types only); **on-demand aggregate + Redis cache, no new entity/migration** (documented reversal of the maintained-counter idea); lazy-loaded `/game` stats panel; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type; token-gated + operator-scoped | Shipped |
| `specs/provably-fair.md` | Commit-reveal server seed (committed on the **first bet**, revealed after `Ended`), portable **HMAC-SHA256 keystream** RNG (rejection sampling, canonical bet order) replacing `System.Random`/`Int32`, Stake-style client seed + per-bet `nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /games/{id}/verification`, standalone `public/verify.html` verifier (in-terminal recompute dropped — terminal shows commitment/reveal only). Greenfield migration (drop `Game.Seed`; add `ServerSeed`/`ServerSeedHash`, `Bet.Nonce`). | Shipped |
| `specs/wallet-display-name.md` | **Full-stack:** make SolanaToken player display-name/preferences key off the **connected wallet** everywhere. Frontend: reactive per-wallet load (`App.vue`, wallet sent only in a SolanaToken context), customization-modal **gate** ("Connect wallet to customize" when no wallet AND (SolanaToken game OR no usable `sub`)), currency dropdown hidden in a SolanaToken context. **Backend (scope corrected):** prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`), and `GameService.InsertBet` captures the wallet address before nulling `OperatorPlayer`. Child of umbrella #46, escrow-independent | Shipped |
| `specs/lottery-pot-stacking.md` | **Full-stack:** un-won lottery pots **stack** into the next round via a running `Game.CarriedExtra` (preset no-winner draw carries `prev + Σ losing stakes`; under-subscription refunds stakes and carries the base unchanged; a paid win resets to the config base). `pot = Σ bets + LotteryPot + CarriedExtra`. Admin **deactivation transfers** the un-won pot to an eligible same-operator/same-method/same-token active lottery (blocks when none). Both Standard + SolanaToken; player `> POT:` reflects the jackpot; seeded 1-min preset lottery for testing. | Shipped |
| `specs/main-production-operator.md` | Default **production** operator **"Main"** (fixed RFC-4122 id, own registered Solana token + 3 SolanaToken default types: 6X race/preset "horse race"/24h lottery, free bets off) seeded in **all** envs (**prod = Main only**, non-prod also keeps DEV Cassino). New **production-enabled** `POST /launch-token/default` mints an **anonymous Main token** (empty `sub`, Main-only); frontend auto-mints on no-token, silent re-mint on 401, real tokens win. Seeding/config + thin mint endpoint + FE fallback — **no schema/migration; integration test waived**. | Shipped |
| `specs/mobile-layout.md` | **Frontend-only:** responsive **mobile layout** for the two main player pages — the games lobby (`/space-stroker/game/games`) and the game screen (`/space-stroker/game/play`) — for **portrait + landscape phones**, standalone and embedded. Mobile engages **below `lg` (1024px)** (reuses the `GameMonkey hidden lg:block` boundary, so landscape phones are covered). Lobby: 1-col condensed cards. Game: a `GAME | CHAT·STATS` segmented toggle (monkey hidden; terminal + compact bet panel — no amount field, lever = PLACE BET; chat/stats full-width). Customization modal made mobile-friendly (`verify.html` already responsive). Net-new **mobile crypto wallet-connect** ("open in wallet app" deep-link to the Phantom/Solflare in-app browser; Android MWA deferred). No backend change. | Shipped |
| `specs/rebrand-spacestroker.md` | **Rebrand refactor (all repos):** deep rename **`Monkify → SpaceStroker`** (brand proper noun only; monkey theme stays) and **`Session → Game`** (pure mechanical, whole `Game*` family) across code, DB (greenfield rename migration), SignalR (`ActiveGamesHub` `/Hubs/Games`), routes (`/games`, FE `/space-stroker/…`), config (`spacestrokersettings.json`, `GeneralSettings:Games`), and docs. Clean break (no aliases). Tooling (`monkify-*` skills, MonkAIfy board, clone URLs, repo-folder names) and the Solana token symbol (#62/#63) are out of scope. **(Historical: SpaceStroker was later renamed to Space Stroker — frontend #74, backend/infra #75.)** | Shipped               |
| `specs/lp-space-stroker-relaunch.md` | **Frontend-only (LP):** finish the LP launch pass #49 deferred, on the **arcade-overhaul** redesign — full **SpaceStroker** rebrand (wordmark SPACE·STROKER, kill every `$MONKIFY`), **token-free/USDC** messaging, **remove the Tokenomics section** → a short honest **"no token (yet)"** note (token maybe later, no ticker/CA), roadmap perks reframed future/earned (no holder-gating, no "$MONKIFY live"), hero secondary CTA → community "Join the troop", "Play now" → **flagged placeholder** app URL (real route `/space-stroker/game`), trust points kept (provably-fair + instant on-chain payouts + auto-refund); Edson/art/story/skin unchanged. Reuses branch `feature/rebranding-spacestroker` (reconciled overhaul); no backend/DB/test. | Shipped |
| `specs/lp-redesign.md` | **Frontend-only (LP):** selective visual redesign — adopt the winning pieces of a Claude design mock onto the live LP (`monkify-lp` off `master`), re-implemented in Astro/Vue inside the arcade skin: two-column hero + local LIVE pill, 3-card how-it-works + FAIR PLAY bar, new image-forward `#modes`, restyled EDSON.LOG story, two-column `#token`, header (coin + Modes/Story/Roadmap/Troop + PLAY NOW), footer tagline, simplified/perk-free roadmap, richer per-section art. Skips the fabricated wins/pot ticker + tabbed modes. Preserves brand / token-free / trust / placeholders; no backend/DB/test. | Shipped |
| `specs/masstransit-postgres-transport.md` | **Backend infra:** replace the MassTransit **RabbitMQ** transport with the **Postgres SQL transport** (8.5.4) — no event-contract/handler-logic change. Messaging store = a separate DB **`spacestroker_messaging`** on the same Postgres instance, provisioned by `SpaceStroker.Consumers`' MT migration hosted service at startup; API stays publisher-only. Reuse the game-DB user (needs CREATE DATABASE) via a new `ConnectionStrings:Messaging`; keep the 8 named endpoints; rename publisher `IRabbitMQService`→`IMessageBus`. Bonus: game DB `monkify → spacestroker` + `monkify-network → spacestroker-network`. Drops the local `rabbitmq` container. **Blocks #61.** | Shipped |
| `features/masstransit-postgres-transport.md` | Distilled: game events ride the **MassTransit Postgres SQL transport** (no broker); transport tables in `spacestroker_messaging` (schema `transport`) on the same instance; Consumers provisions at startup, API publisher-only; `IMessageBus`; game DB `monkify→spacestroker`. | Shipped |
| `specs/terraform-gcp-deploy.md` | **Infra/DevOps (all repos, meta-primary):** production **GCP** deploy as Terraform + two local **C# console apps** (deploy `[api\|consumers\|front\|lp]` + `teardown`; read-write DB REPL over both DBs via Cloud SQL Auth Proxy) — all in `monkify-meta/deploy/`. API + Consumers → **Cloud Run** (API autoscale min≥1; Consumers CPU-always-on, `var.consumer_count`=1, internal ingress — hosts `/hangfire` + owns migrations); front + lp → **GCS + Cloud CDN**; **Cloud SQL 16** (1 instance, DBs `spacestroker` + `spacestroker_messaging`) + **Memorystore Redis 7**, **private-IP only** (VPC + NAT + connector); **Artifact Registry**; external **HTTPS LB + Cloud Armor**; **Secret Manager** synced from a local unversioned `secrets.<env>.json`; Cloud DNS + managed TLS on new **spacestroker.com**; TF split **persistent** (DNS/domain, `prevent_destroy`) vs **workload** (teardown target, keeps domain). Backend: add `/health` to both hosts + forwarded-headers + config-driven CORS (no game change). Builds on **#64** (Postgres transport, shipped). | Shipped |
| `specs/no-db-logging.md` | **Backend infra cleanup:** stop persisting Serilog output to Postgres. Remove the Serilog **PostgreSQL sink** from `AddLogs` (Console-only), drop the `Serilog.Sinks.PostgreSQL` package + the `tableName` param + the connection-string read, update both call sites, and add a raw-SQL EF migration `DropLogTables` (`DROP TABLE IF EXISTS "SpaceStrokerApiLogs"/"SpaceStrokerConsumerLogs"`, no-op Down, empty model diff). Log levels/content unchanged; no frontend; no game-flow change ⇒ no integration test. | Shipped |
| `specs/wallet-balance-refresh.md` | **Frontend-only bug fix:** the header **SolanaToken** balance (`TokenBalance.vue`) now keeps itself current — a **~10 s periodic poll** (scoped to a connected SolanaToken game, paused when the tab is hidden, stopped on unmount/disconnect) **plus immediate refreshes**: own bet updates **instantly** via an optimistic stake decrement, the **win payout reads ~5 s after `Ended`**, and refund (`NotEnoughPlayersToStart` + `ErrorWhenProcessingRewards`) refreshes on settlement; + token-switch refetch + resilient last-known reads. No backend/Standard-balance change (Standard shows no balance by design). | Shipped |
| `specs/crypto-commission.md` | **Full-stack:** re-add a house **commission on winnings, crypto (`SolanaToken`) games only** — `0.5%` of **player-funded winnings above each winner's own stake**, with the house-funded lottery base (`LotteryPot + CarriedExtra`) excluded (≡ 0.5% of losing-bet stakes); a winner never nets below stake. Configurable global rate `GeneralSettings.Token.CommissionPercentage` (default 0.5, `0` disables). Realized by crediting winners less on-chain (remainder stays in the token account) — **no new transfer, no ledger row, no migration**. Reward math in `BetDomainService.CalculateRewardForBet`; rate exposed on `GameParametersDto`→`ActiveGame`; UI = **STATS side-panel badge only** (method-aware `0.5%`/`0% HOUSE CUT`). Standard/USD games unchanged. Shipped alongside a pre-existing multi-winner reward-division fix (frozen winner set + paid-winner divisor). | Shipped |
| `features/crypto-commission.md` | Distilled: crypto (`SolanaToken`) house **commission** on winnings — default 0.5% (`GeneralSettings.Token.CommissionPercentage`), taken from profit above each winner's own stake, house-funded lottery base excluded; realized by a smaller on-chain credit (no transfer/ledger/migration); STATS-badge-only UI; Standard unchanged. Includes the multi-winner **paid-winner divisor / frozen winner-set** reward fix. | Shipped |
| `specs/restore-own-bets-on-refresh.md` | **Frontend-only bug fix:** after a refresh/rejoin the player's **own-bet cues** (the `> N AWAY!` near-miss rows, the returning-bettor victory/lose sound, the embedded `bet-settled`) vanished because `ownBetChoices` (`useGame.ts`) is only written on the live PLACE BET click. Fix: on the load path re-derive ownership from the fetched `gameInstance.bets` (which already carry `externalPlayerId`) via a shared reactive `currentPlayerId`, replaying owned choices through the idempotent `recordOwnChoice`; reactive re-run for SolanaToken wallet reconnect; live `{gameId}/Bets` identity match. **No backend change.** Ended-view reload out of scope (GET excludes ended games; DTO lacks `winners`). | Shipped |
| `specs/rebrand-space-stroker.md` | **Frontend-only rebrand (Minor change):** rename the **consumer-facing brand** `SpaceStroker → Space Stroker` across `monkify-front` + `monkify-lp` only — a **clean break** (no aliases/redirects; safe because nothing is live): display copy/wordmark (split-colour `SPACE`·`STROKER`), `<title>`/meta, route prefix `/space-stroker/*`→`/space-stroker/*` (+ runtime wallet-bridge URL), postMessage contract `source:'space-stroker'`→`'space-stroker'`, `space-stroker.*` storage keys, `.space-stroker-toast*` CSS, package names, `EMBEDDING.md`/`embed-example.html`. Monkey/space lore (Edson, art, "BET ON THE MONKEY", story) **retained**; at #74 the `// Mirrors backend SpaceStroker.*` namespace comments were **kept accurate**. **Backend/DB/infra stayed SpaceStroker** (follow-up #75 renamed them to SpaceStroker). No backend/DB/test. Shipped alongside a Mission Roadmap rebuild on the LP. | Shipped |
| `specs/rebrand-space-stroker-backend.md` | **Backend + infra rename (Minor change), follow-up to #74:** finish the Space Stroker rebrand by renaming the **backend + infra** half `SpaceStroker → SpaceStroker` (machine) / `Space Stroker` (display) — C# solution/projects/namespaces (`SpaceStroker.* → SpaceStroker.*`, `src/` folders + `.sln` on disk), `SpaceStrokerDbContext`→`SpaceStrokerDbContext`, config (`spacestrokersettings.json`→`spacestrokersettings.json`, `ConnectionStrings:SpaceStroker`→`:SpaceStroker`, CORS `app.spacestroker.com`→`app.spacestroker.com`), DB rename via connection string (`spacestroker→spacestroker`, `spacestroker_messaging→spacestroker_messaging`; no new migration — zero SpaceStroker schema objects), docker-compose/scripts, the `monkify-meta/deploy` GCP/Terraform stack + deploy tools (`SpaceStroker.Deploy`/`SpaceStroker.DbConsole`→`SpaceStroker.*`) with the **domain `spacestroker.com → spacestroker.com`** (code-only; real apply is an ops step), a **full KB rename** (living + historical specs, guarding tooling + the two rebrand specs' from→to tokens), and the 6 `// Mirrors backend SpaceStroker.*` comments in `monkify-front` → `SpaceStroker.*`. Clean break, no behavior change; repo folders + `monkify-*` tooling stay. No new integration test / `features/` doc. | Ready |
| `specs/refund-safeguards.md` | **Full-stack:** make `NeedsRefunding` a real, self-clearing bet lifecycle + recover stuck money. (1) A **12h Hangfire sweep** (`PendingRefundSweepJob`) publishes `RefundInvalidBetEvent` for every `NeedsRefunding` bet. (2) `RefundInvalidBetConsumer` now sets `Bet.Status = Refunded` on any **successful** refund (all refunds, the 3 inline triggers included → they end `Refunded` not `Made`); failure leaves it `NeedsRefunding` to retry next sweep. (3) New operator-admin `POST /admin/bets/refund { paymentSignature }` marks a bet (found by indexed `ExternalTransactionReference`, operator-scoped) `NeedsRefunding` — blocks `Rewarded`/`Refunded` (double-pay) + free bets, no game-state restriction, idempotent; money moves via the sweep (single executor, ~≤12h). **No schema/migration.** New admin panel in `AdminPage`/`GameTypeManager`. | Shipped |
| `features/refund-safeguards.md` | Distilled: `NeedsRefunding`/`Refunded` become a real self-clearing bet lifecycle. A 12h Hangfire sweep (`PendingRefundSweepJob`, cron `0 */12 * * *`) publishes a `RefundInvalidBetEvent` for every `NeedsRefunding` bet; `RefundInvalidBetConsumer` sets `Refunded` on any successful refund (all paths incl. the 3 inline triggers) and leaves `NeedsRefunding` to retry on failure. Operator-admin `POST /admin/bets/refund { paymentSignature }` marks a bet (found by `ExternalTransactionReference`, operator-scoped) `NeedsRefunding` — blocks `Rewarded`/`Refunded`+free, any game state, idempotent; money moves via the sweep. No schema/migration. | Shipped |
| `specs/telegram-marketing-campaign.md` | **Content (Minor change), `monkify-meta`-only:** a **$0 organic Telegram launch campaign** for Space Stroker (T-0 = 2026-07-23) as a new `marketing/telegram-launch-campaign.md`. Sibling of the Twitter card #76, adapted to Telegram: **a single community group** (announcements + chat), `[PLAY_URL]` **in-body + inline PLAY NOW button + pin** (bot/admin-delivered, plain-link fallback), **6 required sections** (group description + pinned welcome / message copy / schedule / growth playbook / media direction / **group setup + moderation & anti-scam**), Telegram as the **retention hub with X (#76) funnelling in**. ≥3 teasers + ≥5-message launch sequence + ≥5 first-week; relative-day T-2…T+7; token-free/anti-scam guardrails; existing Edson/arcade assets only. No integration test / `features/` doc. | Shipped |
| `features/app-design-refinement.md` | **Frontend-only (app):** distilled — ported the LP arcade design system into `monkify-front`: unified OKLCH tokens (3 drifting blues → one cyan accent; `--color-blue-vivid` kept = cyan hex; operator `--color-secondary` preserved), `arcade.css` material classes (**plain CSS, not `@layer components`** — Vue CLI css-loader gotcha), 2 fonts (PS2P + VT323), `.btn-arcade` buttons, frosted always-visible header + `SPACE STROKER` wordmark, **neutral brushed-metal lobby cards** (amber hover, clip-path kept), drifting randomized nebula (`NebulaField.vue`), game screen inherits only. No backend/DB/test. | Shipped |
| `specs/app-design-refinement.md` | **Frontend-only (app):** port the `monkify-lp` arcade design system into the game app `monkify-front` to close the LP↔app quality gap — **chrome overhaul, in-play game screen NOT restructured** (inherits tokens/fonts/buttons only). Translate the LP's OKLCH `@theme` tokens into `colors.css`+`tailwind.config.js` (collapse the 3 drifting blues `#3668d4`/`#5b7fcc`/README + stray `#00b7ff` to one system), re-implement the LP material classes (`.arcade-panel`/`.btn-arcade`/`.chip`/`.crt-screen`/`.pixel-notch`/glows) in a v3 `@layer components`, collapse to **2 fonts** (Press Start 2P + VT323; drop Pixelify Sans **+ Silkscreen** + Courier New), `.btn-arcade` buttons (kill `bg-blue-500`/undefined `.title`), frosted **always-visible** header + coin glow, re-tokenized reactive background + LP richness, LP-quality lobby (keep clip-path cards), re-skinned shared surfaces. **Preserves** the operator-themable `--color-secondary` hook. No backend/DB/test. **Shipped** — see `features/app-design-refinement.md`. | Shipped |
| `specs/twitter-marketing-campaign.md` | **Marketing content (Minor change), `monkify-meta` only:** a **$0-budget organic Twitter/X launch campaign** for **@SpaceStroker** ahead of the **2026-07-23** crypto launch. Deliverable `marketing/twitter-launch-campaign.md` — account **bio** (143 chars), ready-to-paste **tweet copy** (4 pre-launch teasers, a 6-tweet launch-day thread, 6 first-week posts incl. an Edson lore thread), a **relative-day schedule** (T-2…T+7), an **organic growth playbook** (cold-start seeding, named Solana communities + spoofing warning, reply-first tactics, hashtags, post times, free KOL outreach + a precedent scan), and **per-tweet media direction** reusing existing Edson/arcade assets. English-only; **link-in-bio + first-reply** with a `[PLAY_URL]` placeholder; token-free USDC/Solana, provably-fair, "No token. Yet." + anti-scam, **no-financial-promise / no-fake-metrics** guardrails. Sibling of Telegram #77. No code; **no integration test / `features/` doc** (content-only). | Shipped |
| `features/lp-final-adjustments.md` | **Frontend-only (LP):** distilled — the Space Stroker LP's 3 mode cards now match the seeded **Main** types (**Cosmic Sprint** = 6-char alphanumeric race; **Beast Mode** = back 1 of 8 beasts; **Space Jackpot** = daily/24h lottery), each with its own animated GIF + first-frame poster (scene art `object-cover`, Space Jackpot `object-top`); real launch/social links (`app.spacestroker.com`, X, Telegram; **Discord removed**), LP `site` = `spacestroker.com`; `#token` "TBD coin" final (markers gone); roadmap reads as goals; no `/placeholder.png` fallback. No backend/DB/test. | Shipped |
| `specs/lp-final-adjustments.md` | **Frontend-only (LP), `monkify-lp` (`master`) + `monkify-meta`:** final pre-launch honesty/polish pass on the Space Stroker LP. Correct the 3 factually-wrong `GameModes.vue` mode texts vs the seeded **Main** types (Sprint = one 6-char **alphanumeric** race, not "6 letters · 6 numbers"; Derby = back 1 of **8** runners, not the invented "rabbit/monkey/jaguar/falcon"; Millions = **once-a-day/24h** lottery, not "every 4h"); rename to **Cosmic Sprint / Beast Mode / Space Jackpot**, wire new `cosmicsprint.gif` + `spacejackpot.gif` (+ posters, uniform square `object-cover` frames); **remove** the `#token` "TBD coin" markers (visual final); Header logo `href="#"`→`#hero`; **fill** PLAY_URL (`app.spacestroker.com`) + X/Telegram, **remove Discord**, `site`=`spacestroker.com`; audit roadmap HI-SCORE as goals + `/placeholder.png` unreachable. No backend/front/DB/test. | Shipped |
| `features/light-graphics-mode.md` | **Frontend-only (app):** distilled — automatic **light graphics mode** for CPU-rasterized browsers (Chrome with hardware acceleration off) + the permanent deletion of `NebulaField.vue`. Verdict per load, **no UI/persistence**: mobile (`< lg`) ⇒ always light → WebGL unmasked-renderer probe → ~2s rAF sample (<25 FPS) → inconclusive ⇒ normal; published as `html.light-graphics`. Light mode drops all blur/`backdrop-filter`, infinite animations, blend overlays and glows; background **keeps drifting** at 8 elements / 24 FPS / frozen scroll, torpedoes off. Mascot stays animated; `.section-vignette` kept. Normal mode unchanged. | Shipped |
| `specs/light-graphics-mode.md` | **Frontend-only (app), `monkify-front` only — LP explicitly out of scope:** fix the near-unplayable frame rate when Chrome's **hardware acceleration is off**. (A) **Delete `NebulaField.vue`** outright — 5 clouds of 40–82vw radial gradient carrying `blur(24–54px)` **and** `mix-blend-mode: screen`, restyled every rAF tick; confirmed live as the dominant cost. (B) A **light graphics mode** with **no UI whatsoever** — resolved by **mobile short-circuit** (`< lg`/1024px ⇒ always light) → **WebGL unmasked-renderer probe** (SwiftShader/llvmpipe/software ANGLE) → **~2s rAF FPS sample**; both inconclusive ⇒ normal. New `usePerfMode` composable + a `html.light-graphics` root class; strips all `backdrop-filter`/`blur()`, the infinite animations (`bar-scan`/`pulse`/`blink`/`crt-flicker`/`wobble`, LP `drift-*`/`coinFloat`/`marquee`/`sectionBgScroll`), `mix-blend-mode` overlays and multi-layer glows; background **keeps drifting, heavily reduced** (cap 8, ~15 FPS, no bg scroll, torpedoes off). Mascot stays animated; `.section-vignette` stays (confirmed free). Includes an rAF efficiency audit (per-frame style-object rebuilds, `right`→`transform` on the torpedo blast). **`monkify-lp` is out of scope** (its findings are parked in the spec's §9 for a future card). **No backend/DB/unit/integration test** (bug fix / QOL). | Ready |
| `specs/provably-fair-final.md` | **Full-stack:** finish provably fair — put verification **in the app** and make the endpoint cheap. New lazy **`FAIR`** tab in `GameSidePanel` (mobile: inside the `CHAT·STATS` segment; **hidden when embedded**), **locked until `Ended`**: two **instant** browser checks (commitment `SHA256(serverSeed)==hash` + own bet seed `HMAC(clientSeed, nonce)`) and an **opt-in** `RECOMPUTE OUTCOME` in a **cancellable Web Worker** with progress, plus **COPY VERIFICATION JSON** and **rollover retention** of the last ended round; **`public/verify.html` kept** (the spec said delete; 3-way duplication remains). Backend: persist **`Game.CharactersTyped` (`int?`)** at run end so `winnerDecidedByFallback` is **derived, never replayed** (the handler's full ≤5M-character replay is deleted), and **cache** the payload (`GameVerification:{gameId}`, 12h) — written only at a **final** status, with the operator check kept **before** the cache read. Reach = current round only; no round history, no tokenless verification. | Shipped |
| `specs/terraform-development-deploy.md` | **Infra/DevOps (Minor change), `monkify-meta` only:** make a **development** teardown wipe everything — after `teardown --env development` nothing Terraform created remains in GCP, only the **GoDaddy registration of `spacestroker.com`**. Because dev and prod share **one GCP project**, this first makes the stack **environment-scoped**: `name_prefix = spacestroker-<envCode>` derived from `secrets.<env>.json` → `environment` (`development→dev`; the full word breaks the SA 30-char and VPC-connector 25-char caps), per-env **state bucket** (`spacestroker-tfstate-<envCode>-<projectId>`), per-env **Cloud SQL + Memorystore**, per-env **Secret Manager ids / Artifact Registry repo / GCS buckets**, per-env `<env>.tfvars.json` (the `.auto.` name is dropped — it cross-contaminates envs) and `TF_DATA_DIR`, plus **fail-fast id-cap validation** (`validate` command). Teardown then destroys workload → persistent (zone + state bucket, via a **local state migration**; `prevent_destroy` can't be variable-driven ⇒ protected/ephemeral resource pairs on `protect_persistent`) → orphan-secret sweep → local generated-file cleanup, guarded by **hard-block on production** + **refuse the registrable apex zone**; GoDaddy is only warned about, never called. `deploy --env development` **auto re-bootstraps** the persistent layer. No backend/front change ⇒ **no unit/integration test**. | Shipped |
| `specs/solana-rpc-api-key.md` | **Backend infra (Minor change), `monkify-backend` + `monkify-meta` — no frontend:** move the backend's Solana RPC off the free public endpoint onto the purchased **Helius** endpoint and harden the money path. `GeneralSettings:Token:ClusterUrl` carries the whole keyed URL, so it **moves from the deploy tool's `appConfig` (plaintext Cloud Run env) to `secrets` (Secret Manager)** — which also needs the id added to the two hard-coded lists in `deploy/terraform/workload/main.tf`; the committed `spacestrokersettings.json` stays on the **keyless public devnet** URL so local dev/Docker/tests need no key and burn no credits. Adds retry policies to `sendTransaction` + `getTokenAccountsByOwner` (which have **none** today), replaces the 3×fixed-1s policies with exponential+jitter that is 429/5xx-aware, and adds a **per-call fallback** to the public RPC after retries are exhausted (new `FallbackClusterUrl`, new `ISolanaRpcClientProvider`, one attempt, Error-logged, all four ops; safe on `sendTransaction` because identical signed bytes de-duplicate on-chain). RPC URLs are **masked to host-only** wherever logged. Both endpoints failing ⇒ today's `Unavailable` → `NeedsRefunding` → 12h sweep. **No schema/migration/API change; no integration test** (the suite swaps in `FakeSolanaService`). | Shipped |
| `specs/games-history.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** a standalone **Games History** page (`/space-stroker/game/history`, reached from a new header **menu button** that also holds Settings; audio + wallet stay on the header). New read-only `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` → `PaginatedList<GameHistoryDto>` — **one game type at a time** (dropdown), **only `RewardForWinnersCompleted`** rounds, newest first, `pageSize`≤30, a **"mine only"** toggle. Rows: ended date · type name · winning choice · pot (`Σ paid bets` + lottery base + carried extra) · winner count · short `serverSeedHash` · nullable **own-bet** block (choice/stake/won/payout) — **no character stream**. Money rules mirror **game-type-stats** (free bets excluded, single currency per type, `useDisplayCurrency`); "mine" identity resolves like the prefs handlers (`wallet` ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`). **Public page cached 5 min in Redis** per operator+type+page+size; caller columns joined **live**. **Verify = link-out only** to the existing `/verification` payload + `public/verify.html` (no in-app replay). **No entity/migration.** New feature ⇒ mandatory happy-path integration test + handler/service unit tests. | Shipped |
| `features/main-only-player-entry.md` | Consolidate to a **single "Main" operator in every environment** (DEV Cassino seeding removed), make `/space-stroker/game` **always land a player in the lobby** (auto-mint Main; `DevLandingPage` picker + `VUE_APP_DEV_LAUNCH` retired), and make the **admin portal token-gated** (`#admin_token` captured at the game page → `/space-stroker/admin`; `DevAdminButton` + `POST /dev/admin-token` removed; `POST /dev/launch-token`→Main). Main default types free bets `!IsProduction()` with a startup **self-heal** (`ExecuteUpdate`, since the context is `NoTracking`). Fixes "same link → different game types". **Partially reverses `main-production-operator` #2.** No schema/migration. | Shipped |
| `specs/main-only-player-entry.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** remove the **DEV Cassino** operator so **only Main** is seeded in all envs; player-only entry at `/space-stroker/game` (retire the dev-launch picker + `VUE_APP_DEV_LAUNCH`); token-gated admin (capture `#admin_token` at the game page, remove `POST /dev/admin-token`, repoint `POST /dev/launch-token`→Main); Main default types `AllowFreeBet = !IsProduction()` with a startup self-heal (`ExecuteUpdate`; the `SpaceStrokerDbContext` is `NoTracking` by default so load-mutate-`SaveChanges` no-ops). Existing DEV Cassino rows purged by hand via `SpaceStroker.DbConsole` (no cleanup code). **No schema/migration.** New feature ⇒ happy-path integration test (`OperatorSeedingTests`) + `DevControllerTests` rewrite. | Shipped |
| `specs/choice-exclusivity.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** in **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is **exclusive** to the first player who bets it in a round; a different player who takes the same normalized sequence is rejected (and **auto-refunded** for a paid `SolanaToken` bet, since the on-chain debit precedes validation — free/Standard are reject-only). **Preset games unchanged** (shared choices). Enforced at two layers: an app-level cross-player check in `RegisterBetHandler.ValidateBetParameters` (routed through the existing `RefundInvalidBet` helper + new `ErrorMessages.ChoiceAlreadyTaken`) **and** a DB unique index on a new NULL-for-preset shadow column `Bet.ExclusiveChoice` (`(GameId, ExclusiveChoice)`; NULLs distinct → preset unaffected), with `GameService.InsertBet` surfacing the 23505 race-loser as `ChoiceAlreadyTaken`. Also fixes the raw-vs-normalized per-player duplicate query. Frontend pre-pay guard in `useGame.bet()` blocks a taken sequence before payment (best-effort; backend authoritative). Winner becomes singular ⇒ winner-takes-all (no reward-math change). New migration `AddBetExclusiveChoice`. New feature ⇒ mandatory happy-path integration test. | Shipped |
| `features/choice-exclusivity.md` | Distilled: in **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is reserved by the **first** bettor per round; a different player taking the same normalized choice is rejected (`ErrorMessages.ChoiceAlreadyTaken`) and **auto-refunded** for a paid `SolanaToken` bet (Standard rejected pre-debit; free log-only). Cross-player check in `RegisterBetHandler.ValidateBetParameters` **and** a DB unique index `(GameId, ExclusiveChoice)` (`Bet.ExclusiveChoice` = normalized choice for free-text, `NULL` for preset ⇒ NULLs distinct, preset games still share); `GameService.InsertBet` maps the 23505 race-loser to `ChoiceAlreadyTakenException`. Same-player repeats still hit `DuplicateBet`; frontend pre-pay guard in `useGame.bet()`; free-text winner becomes singular ⇒ **winner-takes-all** (no reward-math change). | Shipped |
| `specs/fix-pricing.md` | **Minor change (`monkify-backend` + `monkify-front` + `monkify-meta`):** launch pricing fixes for the seeded **Main** game types + the lottery POT display. (A) Per-type `RequiredAmount` in `DefaultGameParameters.GenerateMainDefaultParameters` — **Cosmic Sprint = 1**, **Beast Mode = 1**, **Space Jackpot = 2** (was all `2` via `Base`); Space Jackpot `LotteryPot` **stays 5**; broad dev + crypto catalogues stay `2`. **Seed constants only** — no reconciliation, no SQL patch, **no migration**; applied to existing dev/prod via the user's **teardown + reseed**. (B) Fix `POT: 0 USDC` on Space Jackpot: add base `LotteryPot` to `ActiveGameDto` and seed the frontend `> POT:` from the **game-instance DTO alone** (`isLottery ? lotteryPot + carriedExtra + Σ bets : Σ bets`), removing the fragile `selectedGame.lotteryParameters` cross-fetch dependency so a fresh 5-base lottery reads `POT: 5 USDC`. Display-only; settlement untouched. **No new integration test** (Minor change). **Shipped extras from QA:** the real `POT: 0` cause was a `currentPot = 0` reset in `useGame.updateNewGame` that ran *after* `getBets` (clobbering the seed on every load/rollover) — removed; plus a lottery **FREQUENCY** fix (`formatLotteryFrequency` now parses the TimeSpan day component, so 24h reads "24 HOURS" not "1 HOUR"). | Shipped |
| `specs/solana-connection-rpc.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** keep crypto play alive under mainnet 429s + fix the transfer path. New thin **`POST /rpc`** JSON-RPC passthrough in `SpaceStroker.Api` reusing the key-bearing `TokenSettings.ClusterUrl` (no new secret/provider), gated by **Origin allowlist (`CorsOriginResolver`) + per-IP `AspNetCoreRateLimit` rule + strict 7-method allowlist**; **NOT** launch-token gated (keeps the no-token wallet-bridge popup working). Frontend `useConnection.ts`: **public stays primary**, a **429-only** `withFailover` retries on the proxy and sticks for a **~60s cooldown** (`VUE_APP_SOLANA_PROXY_URL` unset ⇒ no-op); **`confirmTransaction` stays on public** (never proxy a socket). Bundled reliability fixes in `sendToken`: **single-tx idempotent-ATA transfer (1 approval, not 3)** via **sign-once + `sendRawTransaction`**, optional **ComputeBudget priority fee** (`0` disables), **integer/BigInt** amount math. New feature ⇒ mandatory happy-path integration test (`RpcProxyTests` against a fake RPC) + method/Origin unit tests. **No entity/migration.** Trello #87 ([7Fm2RhGB](https://trello.com/c/7Fm2RhGB)). | Shipped |
| `specs/near-miss-dock-bg.md` | **Bug fix, `monkify-front` + `monkify-meta` — no backend:** the bottom-docked near-miss (`> N AWAY!`) overlay in `GameTerminal.vue` is `position: sticky; bottom: 0` inside the scrolling terminal but `background: transparent`, so the auto-scrolling stream bleeds through behind the rows ("letters on top of letters"). Fix = give `.near-miss-dock` an **opaque near-black `background: #050505`** (matches the desktop cabinet screen sampled ~`#040404`; a scoped `.mobile-terminal` override keeps `#0b0d18` for the mobile CRT panel) plus a `box-shadow` covering the form's 24px bottom padding gap + left/right sub-pixel edges; keep the 1px green top separator, no gradient. **Frontend-only CSS; no entity/migration/test** (bug fix, not a new feature). Trello #90 ([h2CVRNby](https://trello.com/c/h2CVRNby)). | Shipped |
| `specs/standardize-scrollbars.md` | **Frontend-only (`monkify-front` + `monkify-lp` + `monkify-meta`), QOL/styling:** replace the raw default OS/browser scrollbars on most scrollable surfaces (reported case: the in-game chat panel) with **one minimal, on-brand scrollbar** applied app-wide as a fallback — thin **8px**, transparent track, subtle neutral thumb `rgba(255,255,255,0.18)`→`0.30` hover, **WebKit** (`*::-webkit-scrollbar*`) **+ Firefox** (`scrollbar-width/color`). Global rule in `monkify-front/src/main.css` and `monkify-lp/src/styles/global.css`; two preserved exceptions in the front — the game terminal keeps its green `.terminal-scroll` (Firefox parity + `border-radius:0` added) and `.side-tabstrip` stays hidden (both win by specificity). No JS/backend/env/token/migration. Not a new feature ⇒ no integration test. Trello #91 ([cGrBi584](https://trello.com/c/cGrBi584)). | Shipped |
| `features/standardize-scrollbars.md` | Distilled: **one global minimal scrollbar** (thin 8px, transparent track, neutral `rgba(255,255,255,0.18)`→`0.30` hover, WebKit + Firefox) applied app-wide as a `*` fallback in both `monkify-front` (`src/main.css`) and `monkify-lp` (`src/styles/global.css`); two preserved front exceptions — the green game terminal `.terminal-scroll` (Firefox parity + square thumb added; WebKit look unchanged) and the hidden `.side-tabstrip` (scoped, unchanged), both winning by specificity. Frontend/CSS-only; no JS/backend/env/token/migration. | Shipped |
| `specs/bet-choice-casing.md` | **Bug fix (Minor change), `monkify-backend` + `monkify-front` + `monkify-meta`:** uppercase bets (e.g. `ASDADA`) were silently lowercased by the backend (so they matched + paid) but the frontend sent/tracked/win-recognized the **raw** choice **case-sensitively** vs the lowercase typed stream → near-miss stuck at "N AWAY!" and no win reveal. Fix: frontend normalizes the choice **once** in `useGame.bet()` (`userWord.trim().toLowerCase()`, propagates to POST body + near-miss + win-recognition) and downcases the maska input on entry; backend stops silently lowering (`Bet.Choice` trim-only, drop `RegisterBetHandler` `.ToLower()`) and now **rejects** uppercase / internal-space / symbol as `InvalidCharacters` (preset games → `InvalidChoice`), keeping whitespace trim. No schema/migration; unit tests updated (uppercase → invalid; case/whitespace refund retargeted lowercase); FE has no test runner so near-miss casing verified by manual QA; not a new feature ⇒ no integration test. Trello #93 ([7yXgbqcQ](https://trello.com/c/7yXgbqcQ)). | Shipped |
| `specs/fair-stats-menu.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** pull **STATS** and **FAIR** out of the `/game` `GameSidePanel` tabs into the **header menu**, leaving the docked cabinet **chat-only** (chrome kept, tab bar dropped; mobile segmented `GAME \| CHAT·STATS` → **`GAME \| CHAT`**). **Stats** becomes a dedicated page (`/space-stroker/game/stats`): a **new GLOBAL section** + the existing per-type stats behind a game-type dropdown. Global = new read-only **`GET /games/stats`** → operator-scoped, **single dominant currency** (USDC on prod Main), on-demand + Redis-cached (120s), **no entity/migration** — mirrors `GameStatsService`; today's headline tiles aggregated across types **+ 6 new tiles** (total volume wagered, unique players, total bets placed, winners paid, biggest win multiplier, most-played type); free bets excluded from money, only `RewardForWinnersCompleted` rounds. **Fair** becomes a full-screen **modal + overlay** in `GameHeader` (like the settings modal): an always-on *how-to-verify* explainer + the existing current-round `GameFairPanel` checks (populate only on `/game/play` after the round ends; off-game → explainer + "watch a round to verify it"). Both **hidden when embedded**. New feature ⇒ mandatory happy-path integration test (`GlobalStatsFlowTests`, dedicated operator for determinism) + service/handler unit tests. Trello #89 ([w9jCXK0N](https://trello.com/c/w9jCXK0N)). | Shipped |
| `features/fair-stats-menu.md` | Distilled: **Stats** + **Fair** move from the `/game` side-panel tabs to the **header menu** (Settings · Stats · Fair · History, hidden when embedded); the docked cabinet is **chat-only** (chrome kept); mobile segmented `GAME \| CHAT`. **Stats page** (`/space-stroker/game/stats`) = a new operator-wide **GLOBAL** section (`GET /games/stats`, single dominant currency, on-demand + Redis 120s, **no entity/migration**; only `RewardForWinnersCompleted` rounds, free bets excluded from money but counted in participation; 10 tiles incl. total volume wagered / unique players / biggest win multiplier / most-played type; non-leaking → zeroed for unknown/expired operators) + the existing per-type stats behind a dropdown, with one page-level house-cut badge. **Fair modal** = always-on how-to-verify explainer + the reused current-round `GameFairPanel` (verifies on `/game/play` after the round ends; off-game → "watch a round to verify it", no fetch). | Shipped |
| `specs/background-page.md` | **Frontend-only (Minor change), `monkify-front`:** a chrome-free `/space-stroker/background` route that renders **only** the animated backdrop — a reusable visual asset to iframe/screen-record. The backdrop is already global (both `GameBackground` layers + `.section-vignette` live in `App.vue`), so the page is an **empty component** and the work is removing what sits in front of it: a declarative `meta: { chromeless: true }` route flag replaces `App.vue`'s hard-coded `route.name !== 'WalletBridge'` header check (`WalletBridge` gets the flag too). Registered in production; outside the operator-scoped `/game/*` tree, so boot loads no operator → no token, no API call, no `/error` dead-end. `TorpedoSpawner` stays but is inert (bet-driven). No backend/DB/test. | Ready |
| `skills/`                   | Agent Skills that power the SpaceStroker task pipeline (see below)                            | Drafted              |


## Skills (the task pipeline)

Reusable Agent Skills live in `skills/`. They are the source of truth, version-controlled here.
Because this repo's `skills/` folder is **not** an auto-discovery path, AGENTS.md installs them into
the Cursor- and Claude-discovered locations (`.cursor/skills/` and `.claude/skills/`) on first use —
see "Skills — ensure installed" in `AGENTS.md`.


| Skill                         | Invoke                  | Purpose                                                                                                                                                                                                                                      |
| ----------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `skills/monkify-refine-tasks` | `/monkify-refine-tasks` | Pick a card from the Trello **monkaify** board and refine it (SpaceStroker-wide, both apps) into a clear, unambiguous, spec-ready task; write it back and move it to **Refined**. Needs the `trello` MCP + Superpowers.                           |
| `skills/monkify-create-spec`  | `/monkify-create-spec`  | From a **Refined** card: ingest card + chat context, explore code, create `feature/<slug>` branches, write `specs/<slug>.md`, update INDEX, move card to **Specs created**. Needs Trello MCP.                                              |
| `skills/monkify-implement-feature` | `/monkify-implement-feature` | From a **Specs created** card: gather game + card + spec context, write an implementation plan (Superpowers **writing-plans**), implement it in the feature worktree(s) with unit + a mandatory happy-path integration test, **commit + push, deploy to the local Docker stack** (`setup_local_environment.bat`; detects a running stack and offers Replace/Combine/Skip, never auto-clobbers), have the user verify the running containers, then move card to **Implemented**. Needs Trello MCP + Superpowers + Docker. |
| `skills/monkify-ship-feature` | `/monkify-ship-feature` | Ship an **Implemented** feature: sync the knowledge base **on the feature branch** (from card + spec + branch code), then merge `feature/<slug>` → `main` in each affected repo (KB included), move card to **Shipped**, and delete the merged branches + worktrees. **Branch + spec read from card Implementation section.** Needs Trello MCP. |


## Feature currently in flight

- **Background-only page** — `specs/background-page.md`. Branch `feature/background-page` in
  `monkify-front` (worktree `.worktrees/background-page/monkify-front`). Frontend-only, Minor change.

- _Last shipped:_ **Bet choice casing** (Minor change / bug fix) — `feature/bet-choice-casing`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**.
  Fixed uppercase bets (e.g. `ASDADA`) stuck at "N AWAY!" with no win reveal: frontend normalizes the
  choice to lowercase at the single capture point (`useGame.bet()`) + downcases input on entry; backend
  stops silently lowercasing (`Bet.Choice` trim-only, drop handler `.ToLower()`) and now **rejects**
  uppercase / out-of-alphabet input as `InvalidCharacters`. No migration; Minor change → no dedicated
  `features/` doc (distilled into `features/games.md` + `features/choice-exclusivity.md` + `glossary.md`).
  Spec `specs/bet-choice-casing.md`. Trello #93 ([7yXgbqcQ](https://trello.com/c/7yXgbqcQ)).

- _Last shipped:_ **Standardize scrollbars** (QOL/styling) — `feature/standardize-scrollbars`
  (**`monkify-front`** + **`monkify-lp`** → their default branch + **`monkify-meta`** → `main`),
  **shipped 2026-07-24**. App-wide minimal scrollbar fallback (thin 8px neutral, WebKit + Firefox)
  replacing default browser scrollbars, with the green game terminal scroll + hidden side tab strip
  preserved as exceptions. Spec `specs/standardize-scrollbars.md` → `features/standardize-scrollbars.md`.
  Trello #91 ([cGrBi584](https://trello.com/c/cGrBi584)).
- _Last shipped:_ **Fair & Stats to the menu; chat-only side panel** — `feature/fair-stats-menu`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**.
  STATS + FAIR moved out of the `/game` side-panel tabs into the header menu (side panel now
  **chat-only**; mobile segmented → **GAME | CHAT**); **Stats** = a dedicated page
  (`/space-stroker/game/stats`) with a new global section (`GET /games/stats`, operator-scoped
  single-currency, on-demand + Redis, no entity/migration) + the per-type stats behind a dropdown;
  **Fair** = a full-screen **modal + overlay** (how-to-verify explainer + the current-round checks on
  `/game/play`); both hidden when embedded. Spec `specs/fair-stats-menu.md` →
  `features/fair-stats-menu.md`. Trello #89 ([w9jCXK0N](https://trello.com/c/w9jCXK0N)).

- _Last shipped:_ **Near-miss dock opaque background** (bug fix) — `feature/near-miss-dock-bg`
  (**`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**. The bottom-docked
  `> N AWAY!` overlay in `GameTerminal.vue` was `background: transparent` and let the scrolling stream
  bleed through; `.near-miss-dock` is now opaque **near-black `#050505`** (matches the cabinet screen;
  mobile keeps `#0b0d18` via a scoped override) with a `box-shadow` covering the form's 24px bottom
  padding + left/right edges. Frontend-only CSS; no backend/test. Spec `specs/near-miss-dock-bg.md`;
  distilled in `features/characters-away.md`. Trello #90 ([h2CVRNby](https://trello.com/c/h2CVRNby)).

- _Last shipped:_ **Improve Solana wallet connection & RPC calls** — `feature/solana-connection-rpc`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**.
  Backend **`POST /rpc`** 429-failover proxy (Origin + per-IP + method allowlist, not launch-token
  gated) reusing `Token.ClusterUrl` + frontend `useConnection` failover/cooldown and single-approval /
  priority-fee / integer-math transfer rework; confirmation stays on the public connection.
  Spec `specs/solana-connection-rpc.md` → `features/solana-connection-rpc.md`. Trello #87
  ([7Fm2RhGB](https://trello.com/c/7Fm2RhGB)).

- _Last shipped:_ **Fix pricing** (Minor change) — `feature/fix-pricing`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**.
  Per-type Main bet prices (Cosmic Sprint = 1, Beast Mode = 1, Space Jackpot = 2; lottery base pot 5)
  via seed constants (applied by teardown + reseed; no migration), and fix the lottery terminal:
  `> POT:` seeds from `ActiveGameDto.LotteryPot` on the instance and the stray `currentPot = 0` reset
  in `updateNewGame` (real `POT: 0` cause) is removed; `> FREQUENCY:` parses the TimeSpan day component
  (24h → "24 HOURS"). Trello [#88](https://trello.com/c/IJRNPqAs/88-fix-pricing);
  spec `specs/fix-pricing.md`.

- _Last shipped:_ **Choice exclusivity** — `feature/choice-exclusivity` (**`monkify-backend`** +
  **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-24**. **Full-stack feature.**
  In **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is reserved by
  the **first** bettor per round; a different player taking the same normalized choice is rejected
  (`ErrorMessages.ChoiceAlreadyTaken`) and **auto-refunded** for a paid `SolanaToken` bet (Standard
  rejected pre-debit; free log-only). Enforced by a cross-player check in
  `RegisterBetHandler.ValidateBetParameters` **and** a DB unique index `(GameId, ExclusiveChoice)`
  (`Bet.ExclusiveChoice` = normalized choice for free-text, `NULL` for preset ⇒ preset games still
  share); `GameService.InsertBet` maps the 23505 race-loser to `ChoiceAlreadyTakenException`.
  Same-player repeats still hit `DuplicateBet`; frontend pre-pay guard in `useGame.bet()`; the
  free-text winner is now singular ⇒ **winner-takes-all** (no reward-math change). New migration
  `AddBetExclusiveChoice`. Integration: `ChoiceExclusivityFlowTests`; unit: `RegisterBetHandlerTests`.

- _Last shipped:_ **Main-only operator + player-only entry + token-gated admin** —
  `feature/main-only-player-entry` (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** →
  `main`), **shipped 2026-07-24**. **Full-stack feature.** Consolidate to a **single "Main" operator in
  every environment** (all DEV Cassino seeding removed), make `/space-stroker/game` **always land a
  player in the lobby** (auto-mint Main; `DevLandingPage` picker + `VUE_APP_DEV_LAUNCH` flag retired),
  and make the operator **admin portal token-gated** — the admin token is captured at
  `/space-stroker/game#admin_token=<jwt>` and redirected to `/space-stroker/admin`; the dev
  `DevAdminButton` and **`POST /dev/admin-token`** are removed (only the operator's private key mints
  one), and **`POST /dev/launch-token`** is repointed to **Main**. Main's 3 default types get
  `AllowFreeBet = !IsProduction()` (free bets ON outside prod) via a startup **self-heal** using
  `ExecuteUpdate` (the `SpaceStrokerDbContext` is `NoTracking` by default, so a load-mutate-`SaveChanges`
  silently persists nothing). Fixes "two people open the same link, get different game types".
  **Partially reverses `main-production-operator` #2** (DEV Cassino no longer seeded in non-prod).
  Existing deployed **DEV Cassino** rows are purged by hand via `SpaceStroker.DbConsole` (no cleanup code
  ships). No schema/migration. Integration: `OperatorSeedingTests`; unit: `DevControllerTests` rewrite.

- _Last shipped:_ **Games history** — `feature/games-history`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-23**.
  **Full-stack feature.** A standalone **Games History** page at `/space-stroker/game/history`, reached
  from a new header **menu button** (Settings + History + dev Admin; audio + wallet cluster stay on the
  header). Read-only **`GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet`** →
  `PaginatedList<GameHistoryDto>`: **one game type at a time** (dropdown), **only
  `RewardForWinnersCompleted`** rounds, newest first, `pageSize` ≤ 30, plus a **"mine only"** toggle.
  Rows: ended date · type name · winning choice · pot (`Σ paid bets` + `LotteryPot` + `CarriedExtra`) ·
  **bet count** (free bets included) · winner count · first-12 of `ServerSeedHash` · nullable own-bet
  block (choice/stake/won/payout). No typed character stream. Money rules mirror **game-type-stats**
  (free bets excluded from pot/winners, single currency per type, `useDisplayCurrency`). "Mine"
  identity = `wallet` (SolanaToken) ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`.
  Public page **Redis-cached 5 min** per `(operator, parametersId, pageNumber, pageSize)`; own columns
  joined **live** (cloned rows, never poisoning the shared cache). **Verify = link-out only** to the
  existing `/verification` payload → copy/download JSON → `public/verify.html` (no in-app replay).
  Cross-operator/unknown/expired/deactivated → vague `ParameterNotFound`. **No entity/migration.**
  Verified: unit **425 passed** (12 new) + integration `GamesHistoryFlowTests` **2/2** (happy path +
  mineOnly). Spec `specs/games-history.md` → `features/games-history.md`; Trello #84
  ([mg3naq8d](https://trello.com/c/mg3naq8d)).

- _Last shipped:_ **Call Solana RPC With API Key** — `feature/solana-rpc-api-key`
  (**`monkify-backend`** + **`monkify-meta`** → `main`; **no `monkify-front`**), **shipped 2026-07-23**.
  **Level: Minor change** (backend infra/resilience ⇒ **no integration test, no dedicated `features/`
  doc**; distilled into `architecture.md` → Solana section). Points the backend's Solana RPC at a
  configurable provider (e.g. **Helius**) and hardens the money path. The single `IRpcClient` singleton
  becomes an **`ISolanaRpcClientProvider`** owning a **primary** (`GeneralSettings.Token.ClusterUrl`)
  and an optional **fallback** (new `FallbackClusterUrl`; disabled when blank or equal to the primary);
  `IRpcClient` still resolves as an alias of the primary. `SolanaService` routes all four RPC calls
  (`getLatestBlockhash`, `getTokenAccountsByOwner`, `sendTransaction`, `getTransaction`) through one
  `ExecuteRpcAsync` helper: a per-op Polly policy (exponential backoff + jitter, 429/5xx/transport-aware,
  counts in `GeneralSettings.Polly`) against the primary — **`sendTransaction` and `getTokenAccountsByOwner`
  had no retry at all before** — then **exactly one** Error-logged attempt against the fallback on
  exhaustion (safe on `sendTransaction`: identical signed bytes de-duplicate on-chain). Both failing ⇒
  today's `OperatorWalletError.Unavailable` → `NeedsRefunding` → 12h sweep. **`ClusterUrl` carries the
  provider api-key, so it is a secret**: committed `spacestrokersettings.json` keeps the keyless public
  devnet URL (local dev/Docker/tests need no key, spend no credits), the deploy stack moves the key from
  `appConfig` to **Secret Manager** (`deploy/terraform/workload/main.tf` + `secrets.example.json` +
  `README.md`), and the provider **masks endpoints to scheme+host** in every log. No schema/migration/API
  change; frontend untouched. **413 unit tests green** (new `SolanaRpcClientProviderTests` + retry/fallback
  cases in `SolanaServiceTests`); integration suite 40 green and unchanged. See
  `specs/solana-rpc-api-key.md` + `architecture.md`; Trello #62 ([h0bO1Adv](https://trello.com/c/h0bO1Adv)).

- _Last shipped:_ **Terraform development deploy** — `feature/terraform-development-deploy`
  (**`monkify-meta` only**; no backend/front/lp change), **shipped 2026-07-23**.
  **Level: Minor change** (deploy tooling ⇒ **no integration test, no dedicated `features/` doc** —
  `features/terraform-gcp-deploy.md` was extended instead). Two halves, the first a prerequisite for
  the second. **(A) Environment scoping:** development and production share **one GCP project**, and
  the stack had no per-env naming at all (`name_prefix` defaulted to `spacestroker`, the state bucket
  was keyed off the project id, the state prefix was the literal `workload`) — so the two would have
  collided on every resource name **and** on the same state object. Now a new
  `deploy/tools/SpaceStroker.Deploy/EnvNaming.cs` derives an **env code** from `secrets.<env>.json` →
  `environment` (`development→dev`, `production→prod`, `staging→stg`, else the first 4 alphanumerics)
  and owns **every** derived id: `name_prefix = spacestroker-<envCode>` across Cloud Run, service
  accounts, VPC/connector/NAT, Cloud SQL + Memorystore (**one instance set per environment**),
  Artifact Registry, **Secret Manager ids**, the site buckets, LB/Armor/cert/IP and the DNS zone, plus
  a **per-env state bucket** `spacestroker-tfstate-<envCode>-<projectId>`, a per-env
  `TF_DATA_DIR=.terraform-<envCode>` with `init -reconfigure`, and a per-env `<environment>.tfvars.json`
  (**deliberately not `*.auto.tfvars.json`** — Terraform auto-loads every such file, so two
  environments' vars would both load). The short code is forced by GCP caps:
  `spacestroker-development-consumers` = 34 > the service-account limit of 30 and
  `spacestroker-development-vpcconn` = 32 > the VPC-connector limit of 25 (`spacestroker-dev-vpcconn`
  = 24, one char of headroom) — so the tool **validates every derived id's length and charset before
  invoking Terraform**, exposed as `spacestroker-deploy validate`. **(B) Total development teardown:**
  `teardown` dispatches on the environment — **production keeps** today's workload-only behaviour
  (domain + state bucket preserved), **any other environment destroys everything Terraform created**:
  workload → persistent (the state bucket holds its own state, so it is **migrated to a local backend
  first**, then zone + bucket are destroyed together) → a sweep of leftover `spacestroker-<envCode>-*`
  **Secret Manager** secrets → removal of the generated local files (`<env>.tfvars.json`,
  `.terraform-<envCode>/`, local state, and the legacy `production.auto.tfvars.json`; **never**
  `secrets.<env>.json` or `*.tfvars`). Afterwards only the **GoDaddy registration of
  `spacestroker.com`** remains. Two guardrails: production is **hard-blocked** from the total path, and
  a zone whose `dnsDomain` is the **registrable apex** (`spacestroker.com.`) is refused — only
  sub-zones like `dev.spacestroker.com.` may be destroyed. `deploy` **auto re-bootstraps** a missing
  persistent layer (local state → apply → migrate) and prints the new nameservers, so a total teardown
  is round-trippable without raw Terraform; the tool never calls a registrar API (a dead NS delegation
  is only warned about). Terraform-side: `protect_persistent` is realized as **mutually exclusive
  protected/ephemeral resource pairs** with coalescing outputs, because `lifecycle.prevent_destroy`
  only accepts a literal; `ProcessRunner` gained `Capture()` + per-invocation env vars; Cloud SQL gained
  an optional `db_instance_suffix` (GCP reserves a deleted instance name ~**7 days**). Verified with
  `dotnet build`, `terraform validate`/`fmt`, `validate` (19 ids PASS), a forced cap failure, and dry
  runs of both teardown paths + the apex guard — **the real GCP round-trip (spec §13) is still an
  operator step**, and the pre-rename dev environment must be torn down under the old names before
  deploying fresh. Meta `21d7d88`. See `features/terraform-gcp-deploy.md`; spec
  `specs/terraform-development-deploy.md`; Trello #85 ([fHU4Dpis](https://trello.com/c/fHU4Dpis)).

- _Last shipped:_ **Provably fair final** — `feature/provably-fair-final`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`**), **shipped 2026-07-23**.
  **Level: Feature.** Closes the loop on provably fair:
  verification moves **into the app** and the endpoint becomes cheap. **Frontend:** a new lazy
  **`FAIR`** tab in `GameSidePanel` next to `CHAT`·`STATS` (mobile: inside the `CHAT·STATS` segment;
  **hidden when embedded**), **locked until the round `Ended`**, then two **instant** in-browser checks
  — the commitment (`SHA256(UTF8(serverSeed)) == serverSeedHash`) and the player's own bet seed
  (`HMAC-SHA256(clientSeed, nonce)` matched against `orderedBetSeeds`; spectators/rotated seeds get an
  **informational** row, never a failure) — plus an **opt-in** `RECOMPUTE OUTCOME` that replays the
  stream in a **cancellable Web Worker** (first worker in the repo) with progress sized by the new
  `charactersTyped`, a **COPY VERIFICATION JSON** button, and **rollover retention** so a slow
  recompute survives the next round starting. **`public/verify.html` was KEPT, not deleted** (the spec
  said delete): it is the offline "don't trust us" path — the FAIR tab links it with a download +
  instructions, and it gained a character-count cross-check and a last-1,000-character download, so the
  **3-way byte-identical duplication remains**. The game terminal is unchanged.
  **Backend:** persist **`Game.CharactersTyped` (`int?`)** in `UpdateGameStatus` (same `SaveChanges` as
  `WinningChoice`; `RunGameConsumer` unchanged) so `winnerDecidedByFallback` is **derived**
  (`CharactersTyped >= MaxCharacters`, `null` for legacy rows) and the handler's full ≤**5,000,000**-character
  replay is **deleted**; cache the payload via `ICacheService` (`GameVerification:{gameId}`, 12h) —
  written only once the round is **final** (`RewardForWinnersCompleted`/`ErrorWhenProcessingRewards`,
  because the DTO carries `Status`), with the **operator check kept before the cache read** (the key is
  per-game). One nullable column + migration `GameCharactersTyped`; no config. Reach is the **current
  round only** — no round history, no tokenless/public verification. **395 unit + 40 integration tests
  green.** Bundled (out of the card's scope): the **dev operator now seeds the production catalogue** —
  `GenerateMainDefaultParameters` (Cosmic Sprint / Beast Mode / Space Jackpot) against the dev token
  with `AllowFreeBet` left on, replacing the old 13 Standard + 3 crypto dev types, which survive as
  **test fixtures only** (so `6L Minute Menagerie`, `6X Crypto Clash` etc. are no longer seeded). See
  `features/provably-fair-final.md` + `features/games.md`; spec `specs/provably-fair-final.md`;
  Trello #82 ([RcJrKixz](https://trello.com/c/RcJrKixz)).

- _Last shipped:_ **Light graphics mode (software-rendering performance)** —
  `feature/light-graphics-mode` (**`monkify-front`** → `main` + **`monkify-meta`** → `main`; **no
  backend, no LP**), **shipped 2026-07-22**. **App-only frontend bug fix /
  QOL.** With Chrome's hardware acceleration off (software rasterization) the game is close to
  unplayable. Two changes: **(A)** `NebulaField.vue` is **deleted permanently** — 5 clouds, each a
  40–82vw radial gradient carrying **both** `filter: blur(24–54px)` and `mix-blend-mode: screen`,
  re-styled on every rAF tick; commenting it out live produced a large, immediately visible
  improvement, confirming it as the dominant cost. **(B)** An automatic **light graphics mode** with
  **no user-facing control at all** (no toggle, no settings row, no `localStorage`, no URL param):
  resolved by a **mobile short-circuit** (`< lg`/1024px via the existing `useIsMobile` ⇒ always light,
  reactive to rotation/resize) → a **WebGL unmasked-renderer probe** (SwiftShader / llvmpipe /
  software ANGLE ⇒ light) → a **~2s rAF FPS sample** fallback (<25 FPS ⇒ light); both inconclusive ⇒
  **normal** (never punish good hardware). A new `hooks/usePerfMode.ts` singleton drives a
  `html.light-graphics` root class that CSS keys off. Light mode drops every
  `backdrop-filter`/`filter: blur()` (header, lobby panels, game-type cards, toasts), the infinite
  animations (`bar-scan`, `pulse`, `blink`, `crt-flicker`, `wobble`), the `mix-blend-mode` overlays
  and the multi-layer
  `box-shadow`/`text-shadow` glows — while the space background **keeps drifting, heavily reduced**
  (cap 8 elements, ~15 FPS, frozen `background-position`, `TorpedoSpawner` off). The animated mascot
  **stays animated**; `.section-vignette` **stays** (audited — one static
  gradient, no blur/blend/animation, and now the only thing framing the screen edges). Also in scope:
  an rAF **efficiency audit** (per-frame inline-style rebuilds + reactive-array churn in
  `GameBackground`, hoisting the rarity pool, and `right`→`transform: translateX()` on the torpedo
  `blast` keyframe, which helps both modes). **The landing page (`monkify-lp`) is explicitly out of
  scope** — its perf findings are parked in the spec's §9 for a future card. Target ~30 FPS with no
  input lag; normal mode otherwise unchanged (its 30 FPS background cap is pre-existing).
  **Shipped deviation:** the light-mode frame target is **24 FPS**, not the drafted ~15 — raised in
  live QA since the element cap + frozen scroll already carry the savings.
  Frontend-only ⇒ **no unit test, no integration test** (`constraints.md` §3 — bug fix / QOL);
  verified with `yarn lint` + `yarn build` clean + live browser QA with acceleration actually
  disabled. Front `52fc9f1`. See `features/light-graphics-mode.md`; spec
  `specs/light-graphics-mode.md`; Trello #83 ([wsiDoPfK](https://trello.com/c/wsiDoPfK)).
- _Last shipped:_ **LP final adjustments** — `feature/lp-final-adjustments`
  (**`monkify-lp`** → `master` + **`monkify-meta`** → `main`; **no backend/front**), **shipped
  2026-07-22**. **Frontend-only, LP-only pre-launch polish.** The Space Stroker LP's 3 mode cards now
  match the seeded **Main** production types with factually-correct copy and final art: **Cosmic
  Sprint** (6-char alphanumeric race, tag `6 CHARS · A–Z + 0–9`), **Beast Mode** (back **1 of 8**
  beasts — no invented animal list), **Space Jackpot** (**daily/24h** lottery, tag `DAILY DRAW · 24H`).
  The 3 old factual errors ("6 letters · 6 numbers", "rabbit/monkey/jaguar/falcon", "every 4h") are
  gone. New animated GIFs `cosmicsprint.gif` + `spacejackpot.gif` with auto-generated first-frame
  posters (Beast keeps `beastmode.gif`); uniform square art frames — scene art `object-cover` (Space
  Jackpot `object-top` so the head isn't cropped), transparent Beast character contained/bottom-anchored.
  Launch/social links filled (no more `TODO(ship)`): `PLAY_URL = app.spacestroker.com/space-stroker/game`,
  X `x.com/SpaceStroker`, Telegram `t.me/spacestroker`, **Discord removed** (socials grid → 2-up), Astro
  `site = spacestroker.com`; Header logo `href="#"`→`#hero`; `#token` dimmed-coin "TBD" markers removed
  (visual kept final); roadmap HI-SCORE/phases confirmed goal-framed (no change); `/placeholder.png`
  fallback confirmed unreachable (no change). Frontend-only ⇒ **no unit/integration test** (LP has no
  suite); verified `npm run build` clean + browser QA (0 console errors, all art 200) + brand sweep
  clean. LP `ecd675b`. See `features/lp-final-adjustments.md`; spec `specs/lp-final-adjustments.md`;
  Trello #78 ([3dPBofpy](https://trello.com/c/3dPBofpy)).
- _Last shipped:_ **Twitter/X launch marketing campaign** — `feature/twitter-marketing-campaign`
  (**`monkify-meta` only**; no backend/front), **shipped 2026-07-21**. **Minor change (marketing content).** A
  **$0-budget, fully organic** Twitter/X launch campaign for **@SpaceStroker** to announce the **2026-07-23**
  crypto launch to Solana players. Deliverable = **`monkify-meta/marketing/twitter-launch-campaign.md`** with
  account **bio** (143 chars), ready-to-paste **tweet copy** — **4** pre-launch teasers (a gm-native, degen-voiced
  opener), a **6-tweet launch-day thread**, and **6** first-week posts incl. a 6-tweet Edson lore thread — a
  **relative-day posting schedule** (T-2…T+7, survives a launch slip), a **$0 organic growth playbook**
  (cold-start seeding, real Solana hubs with an impersonator/spoofing warning, a **reply-first** strategy since
  replies outweigh likes in X's 2026 feed, 1–2 hashtags, best post times, free KOL outreach, and a precedent scan
  of comparable Solana launches — Moonbet/transparency, referral loops, memecoin community launches), a
  **media-direction index** reusing existing Edson/arcade assets, and **guardrails + a pre-post self-audit**.
  English-only; **link-in-bio + first-reply** with a flagged **`[PLAY_URL]`** (app domain undecided); grounded in
  the live LP — token-free USDC/Solana, provably-fair, "No token. Yet." + anti-scam — under **no-financial-promise
  / no-fabricated-metrics** guardrails. Content-only ⇒ **no code, no integration test, no dedicated `features/`
  doc** (Minor change), nothing to deploy; verified via a content self-audit (bio ≤160, every tweet ≤280,
  banned-term scan clean, all referenced assets exist). Spec `specs/twitter-marketing-campaign.md`; Trello #76
  ([gApr5z7v](https://trello.com/c/gApr5z7v)). Telegram is the sibling card #77.
- _Last shipped:_ **Telegram launch marketing campaign** — `feature/telegram-marketing-campaign`
  (**`monkify-meta` only**; no backend/front), **shipped 2026-07-21**. **Minor change (marketing content).**
  A ready-to-run, **$0 organic Telegram launch campaign** for the **2026-07-23** crypto launch, delivered as a
  new `monkify-meta/marketing/telegram-launch-campaign.md`. Sibling of the Twitter campaign (#76), reusing its
  verified product truth + guardrails, adapted to Telegram: **a single open community group** (announcements +
  chat in one), the app link `[PLAY_URL]` **in-body + inline PLAY NOW button + pinned** (bot/admin-delivered,
  plain-link fallback), **six required sections** (group description + pinned welcome, message copy, posting
  schedule, organic growth playbook, per-message media direction, and a Telegram-specific **group setup +
  moderation & anti-scam** section — Rose + join captcha, ready-to-pin "admins never DM first" / "no token/CA"),
  and **Telegram as the retention hub with Twitter/X (#76) funnelling in**. ≥3 pre-launch teasers + a ≥5-message
  launch-day sequence + ≥5 first-week messages; relative-day (T-2…T+7) schedule; token-free/anti-scam guardrails;
  existing Edson/arcade assets only. Content self-audit green (all 8 ACs); **no unit/integration test, no
  `features/<slug>.md` doc** (Minor change). Deliverable `marketing/telegram-launch-campaign.md`; spec
  `specs/telegram-marketing-campaign.md`; Trello #77 ([LTrjTgdy](https://trello.com/c/LTrjTgdy)).
- _Last shipped:_ **APP (front) design refinement** — `feature/app-design-refinement`
  (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-21**. **Frontend-only, app-only.**
  Ported the LP's arcade design system into the game app to close the LP↔app visual gap — a chrome
  overhaul (tokens/fonts/buttons/header/background/lobby/shared UI) with the **in-play game screen NOT
  restructured** (it inherits the token/font/button restyle only). Unified OKLCH tokens in
  `colors.css` (the 3 drifting blues `#3668d4`/`#5b7fcc`/`rgba(91,127,204)` + stray `#00b7ff` collapsed
  to **one cyan accent**; `--color-blue-vivid` kept as a hex alias = the cyan `#47deee` so existing
  Tailwind opacity markup still builds; operator `--color-secondary` preserved), new
  `src/assets/styles/arcade.css` material layer (`.arcade-panel`/`.btn-arcade`/`.chip`/`.crt-screen`/
  glows/`.title`) — **plain CSS, deliberately not `@layer components`** because Vue CLI's css-loader
  builds each `@import`'d file standalone (a bare `@layer components` there has no matching
  `@tailwind components` and fails). 2-font system (Press Start 2P + VT323; dropped Pixelify Sans +
  Silkscreen + Courier New), `.btn-arcade` buttons (killed `bg-blue-500` + the undefined `.title`), a
  frosted **always-visible** header with the `SPACE STROKER` wordmark + coin glow, **neutral
  brushed-metal lobby cards** (amber on hover, clip-path kept — the interim bright-cyan frame was
  dropped in QA), and a **drifting, randomized nebula** (`NebulaField.vue`) synced to the reactive
  background (opacity 0.05–0.18, freezes under reduced motion); re-skinned toast/loaders/customization
  modal (→ `.arcade-panel`)/wallet, plus a `GameMonkey` placement fix (stopped reserving the chat
  panel's full column). **In-app QA decisions** (flexibility note): LP dot-grid pruned (moirés vs the
  starfield); "GAME SELECTION" header keeps a cyan glow; admin/dev-only pages keep Courier New.
  Frontend-only ⇒ **no unit/integration test, no game-flow change** (DoD = `yarn lint` + `yarn build` +
  live browser QA, 0 console errors). A matching `SPACE STROKER` wordmark tweak shipped **separately**
  to `monkify-lp` `master` (`ba7c203`). Front `1a426af`. See `features/app-design-refinement.md`; spec
  `specs/app-design-refinement.md`; Trello #79 ([R2x70aWY](https://trello.com/c/R2x70aWY)).
- _Last shipped:_ **Backend + infra renamed to "Space Stroker"** (follow-up to #74) —
  `feature/rebrand-space-stroker-backend` (`monkify-backend` + `monkify-front` + `monkify-meta` →
  `main`), **shipped 2026-07-21**. **Minor change (rename refactor).** Completed the Space Stroker
  rebrand across the **backend + infra** so the whole product (code + DB + infra + docs) now reads
  **SpaceStroker** (PascalCase machine token; two-word "Space Stroker" display). Backend:
  solution/projects/namespaces `SpaceStroker.*` (+ on-disk `src/` folders + `SpaceStroker.sln`),
  `SpaceStrokerDbContext`, config `spacestrokersettings.json` / `ConnectionStrings:SpaceStroker` / CORS
  `app.spacestroker.com`, DB `spacestroker` + `spacestroker_messaging` (renamed via connection string +
  docker-compose — **no new migration**: zero brand-named schema objects, so the fresh `spacestroker`
  DB is built by the existing migrations), and docker-compose/scripts. Infra: the `monkify-meta/deploy`
  GCP/Terraform stack + deploy tools (`SpaceStroker.Deploy`/`SpaceStroker.DbConsole`) + domain
  **`spacestroker.com`** (`api.`/`app.`/`www.`/apex) — code-only; real `terraform apply`/DNS cutover is
  a downstream ops step (confirm the domain is secured first). Frontend: the 6 `// Mirrors backend
  SpaceStroker.*` namespace comments + `.env.example` host hints (comments/config only). KB: full brand
  scrub across living + historical docs (the mascot "space chimp" character retained; only the brand
  name removed). Clean break, zero game-flow change; repo folders + `monkify-*` tooling/board stay. **No
  new integration test, no new `features/` doc** (Minor change). **Verified:** `dotnet build` clean,
  386 unit tests green, local Docker stack healthy end-to-end. Spec
  `specs/rebrand-space-stroker-backend.md`; Trello #75 ([VgNMpTzu](https://trello.com/c/VgNMpTzu)).
- _Last shipped:_ **Frontend rebrand — SpaceStroker → "Space Stroker"** —
  `feature/rebrand-space-stroker` (`monkify-front` → `main` + `monkify-lp` → `master` + `monkify-meta`
  → `main`; **no backend**), **shipped 2026-07-21**. **Frontend-only, Minor change.** Renamed the
  **consumer-facing brand** `SpaceStroker → Space Stroker` across both frontends as a **clean break** (no
  aliases/redirects — safe because nothing is live: LP `PLAY_URL` is a placeholder host, domain
  undecided, no integrated casino, no persisted player seed). Scope: display copy + split-colour
  wordmark (`SPACE`·`STROKER`), `<title>`/meta, **route prefix** `/space-stroker/*`→`/space-stroker/*`
  (incl. the runtime-built wallet-bridge URL), the **postMessage embed contract**
  `source:'space-stroker'`→`'space-stroker'` (+ `'space-stroker-wallet'`), the `space-stroker.*`
  **client-storage keys**, `.space-stroker-toast*` CSS, `package.json` names
  (`space-stroker`/`space-stroker-lp`), and operator docs (`EMBEDDING.md`, `embed-example.html`) + a
  full editorial pass (fixed the LP `PRODUCT.md` `$MONKIFY` leftovers and stale front `CLAUDE.md`).
  **Monkey/space lore retained 100%** (Edson, all art/GIFs, `EDSON.LOG` story, `BET ON THE MONKEY`,
  "infinite monkey"). At #74 the `// Mirrors backend SpaceStroker.*` namespace comments were **deliberately kept**
  — **backend / DB / infra / `spacestroker.com` stayed SpaceStroker**, an intentional first-step drift since resolved
  by follow-up card **#75** ([VgNMpTzu](https://trello.com/c/VgNMpTzu)). **Bundled in the same ship
  (beyond the rename):** a **Mission Roadmap rebuild** on the LP (`SpaceRoadmap.vue`) — **5
  space-themed nodes** (Earth → Moon → Gas Giant → Comet → Galaxy; the previously-dropped 4th comet
  node restored), a **HI-SCORE north-star banner** (10k players / $1M bet / 250k rounds), a live
  flashing **"WE ARE HERE"** beacon on the current node, rewritten per-node copy, and **future-phase
  copy that teases a token launch + airdrop** — the site itself stays **token-free now** (the
  `#token` "No token. Yet." + anti-scam card is unchanged; the roadmap phases are the forward plan,
  we're at phase ①). Frontend-only ⇒ **no integration test, no new `features/` doc**; verified —
  `yarn lint` + `yarn build` (front) and `npm run build` (LP) all green, brand sweep clean (only the
  intentional backend-namespace refs remain). LP `08c195a`, front `1f267b4`. Spec
  `specs/rebrand-space-stroker.md`; Trello #74 ([qbcZgHuX](https://trello.com/c/qbcZgHuX)).
- _Last shipped:_ **Refund safeguards** — `feature/refund-safeguards`
  (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped 2026-07-20**. **Full-stack Feature.** Makes
  `BetStatus.NeedsRefunding`/`Refunded` (defined but never set today) a real, self-clearing lifecycle
  and lets operators recover stuck money. Three parts: **(1)** a new **12-hourly Hangfire recurring
  job** `PendingRefundSweepJob` (`SpaceStroker.Infrastructure/Jobs`, cron `0 */12 * * *`, registered in
  `SpaceStroker.Consumers/Program.cs`) that reads every `NeedsRefunding` bet and publishes one
  `RefundInvalidBetEvent` each; **(2)** `RefundInvalidBetConsumer` now sets `Bet.Status = Refunded` on
  any **successful** `Refund()` (applies to **all** refunds — the three existing inline triggers
  included, which previously left the bet `Made`), and on failure leaves the status untouched so a
  swept bet is retried next cycle (indefinitely); **(3)** a new operator-admin endpoint
  `POST /admin/bets/refund { paymentSignature }` (`AdminController` + `Handlers/Admin/RefundBet`) that
  finds the bet by the indexed `ExternalTransactionReference` (operator-scoped via
  `IAdminContext.OperatorId`), **blocks** `Rewarded`/`Refunded` (double-pay safety) + free bets, allows
  **any** game state, is **idempotent**, and only **marks** `NeedsRefunding` — the money moves via the
  sweep (single executor, up to ~12h). Frontend: a new **"Refund a bet"** panel
  (`BetRefundManager.vue` + `adminService.refundBet` + a `GameTypeManager` tab). **No schema/migration**
  (enum values + the `ExternalTransactionReference` index already exist). **386 unit + 39 integration
  tests green** (+`PendingRefundSweepJobTests`, +`RefundBetHandlerTests`, +`RefundInvalidBetConsumer`
  flip/failure cases, +`RefundSafeguardsTests`: sweep happy-path / admin-mark → sweep → refund /
  guards). See `features/refund-safeguards.md`; spec `specs/refund-safeguards.md`; Trello #71
  ([MpfBtpE4](https://trello.com/c/MpfBtpE4)).
- _Last shipped:_ **Landing-page visual redesign (arcade-cabinet)** — `feature/lp-redesign`
  (`monkify-lp` → `master`; **no backend / game-app change**), **shipped 2026-07-19**. **Frontend-only**
  selective visual revamp of the SpaceStroker LP: cherry-picked the winning pieces of a Claude design mock
  onto the live page, re-implemented in Astro/Vue inside the existing arcade `@theme` skin (no palette
  drift). Section order hero → `#about` → **`#modes`** (new "SELECT YOUR GAME", image-forward) →
  `#story` → `#token` → `#roadmap` → `#community`. **Two-column hero** (copy + CTAs + a cosmetic local
  **LIVE typing pill** — no network/money claim | glowing coin + mascot chimp + drifting comet);
  **how-it-works** = 3 horizontal `01/02/03` step cards + a **full-width FAIR PLAY bar** (provably-fair
  + instant on-chain payouts + auto-refund); **`#token`** = two-column "No token. Yet." card (anti-scam
  warning + Join CTA); **header** = coin + SPACE·STROKER + **Modes/Story/Roadmap/Troop** + **PLAY NOW**;
  **footer** promotes **"BET ON THE MONKEY"** to a tagline; **roadmap** keeps the star-map/titles but
  simplifies items to no-emoji lines and **drops the per-phase perk box**; **story** keeps the EDSON.LOG
  terminal + painted slides (restyled). **Richer per-section space art** (planets/black hole/satellites/
  asteroids/comets) behind every section; **no** wins/pot ticker anywhere. Brand (SPACE·STROKER, no
  `$MONKIFY`), token-free/USDC, the three trust points, and the `PLAY_URL` + social `#` `// TODO(ship)`
  placeholders are **preserved**. LP code merged to `master` as `5a5003d Arcade-cabinet LP redesign`;
  **no backend ⇒ no unit/integration test** (DoD = `npm run build` + browser QA). A **post-ship polish**
  (2026-07-19, `master` `6cc713a`, beyond this card) added a per-section nebula colour journey + depth
  parallax to the backdrops. See `features/lp-redesign.md`; spec `specs/lp-redesign.md`; Trello #65
  ([hp9GJjuQ](https://trello.com/c/hp9GJjuQ)).
- _Last shipped:_ **Restore your own bets after a refresh** —
  `feature/restore-own-bets-on-refresh` (`monkify-front` + `monkify-meta`; **no backend**), **shipped
  2026-07-17**. **Frontend-only bug fix** for card #70: after a player bets and then
  refreshes/rejoins a round, their **own-bet UI cues** disappeared (most visibly the `> N AWAY!`
  near-miss rows), so they rendered like a spectator who never played. Root cause: the near-miss/audio
  cues all gate on `ownBetChoices` (`useGame.ts`), which was only written by the **live PLACE BET
  click** and reset to `[]` on every load — `getBets` re-fetches the bets (each already carrying
  `externalPlayerId`) but never matched them to the current player. Fix (no backend): on the load
  path (`updateNewGame`, **after** the reset) re-derive ownership by matching `gameInstance.bets`
  against a shared reactive **`currentPlayerId`** (SolanaToken → connected wallet; else launch-token
  `externalPlayerId`/`sub`) and replay owned choices through the idempotent `recordOwnChoice` (which
  re-seeds `ownBetChoices` + `nearMiss.trackChoice`); a **per-consumer watch** re-runs it when the wallet
  reconnects (crypto), and the live `{gameId}/Bets` handler also matches by identity. Restores near-miss
  rows + returning-bettor victory/lose sound + embedded `bet-settled`. **Ended-view reload is out of
  scope** — `GET /games/{gameTypeId}` never returns an `Ended` game (`ActiveGamesStatus`) and
  `ActiveGameDto` lacks `winners`, so a just-ended reward screen can't be reconstructed frontend-only
  (kept a Minor change). Frontend-only ⇒ **no integration test, no new `features/` doc** (Minor-change
  exemptions); `features/characters-away.md` corrected. **`yarn lint` + `yarn build` green.** See
  `features/characters-away.md`; spec `specs/restore-own-bets-on-refresh.md`; Trello #70
  ([IkVOurKA](https://trello.com/c/IkVOurKA)).
- _Last shipped:_ **Add commission (crypto games)** —
  `feature/crypto-commission` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-17**. Re-adds a house **commission on winnings, `SolanaToken` games only**: `0.5%` of each
  winner's **player-funded profit above their own stake**, with the house-funded lottery base
  (`LotteryParameters.LotteryPot + Game.CarriedExtra`) **excluded** — equivalently 0.5% of the
  losing-bet stakes; a winner never nets below their stake. **Configurable global rate**
  `GeneralSettings.Token.CommissionPercentage` (default `0.5`, `0` disables) — restores the old,
  removed `TokenSettings.CommisionPercentage` location (correctly spelled). Applied per-winner in
  `BetDomainService.CalculateRewardForBet` (SolanaToken branch; `SetPotAmount` unchanged) and realized
  by **crediting winners less on-chain** — the retained remainder stays in the operator's token
  account, so **no new transfer, no ledger row, no migration**. The rate is surfaced to the SPA on
  `GameParametersDto` → `ActiveGame` (`commissionPercentage`); **UI = STATS side-panel badge only**
  (`GameStatsPanel.vue` made method-aware: `0.5% HOUSE CUT` crypto / `0% HOUSE CUT` Standard) — no
  terminal line, no lobby card. Standard/USD settlement, refunds, and provably-fair verification are
  untouched. **Also fixed a pre-existing multi-winner reward bug** the commission surfaced —
  `BetDomainService.Winners` was a deferred query, so the winner count drifted down as winners were
  paid (a 3-token/2-winner round paid the 2nd winner the whole pot); the winner set is now frozen and
  the pot divided among **paid** winners only (`PayableWinnerCount`). **376 unit + 36 integration
  tests green** (+`PaidCryptoRace_AppliesCommission_CreditsPotMinusCut` +`…WinnersRewardedSequentially`
  regression). See `features/crypto-commission.md`; spec `specs/crypto-commission.md`; Trello #66
  ([qq0F0ZNJ](https://trello.com/c/qq0F0ZNJ)).
- _Previously shipped:_ **Live wallet balance refresh on the game page** —
  `feature/wallet-balance-refresh` (`monkify-front` + `monkify-meta`; **no backend**), **shipped
  2026-07-17**. **Frontend-only bug fix** for the SolanaToken header balance (`TokenBalance.vue`) not
  updating after a bet or payout — it was read single-shot with no retry and fired before the RPC node
  propagated the change, so it went stale until the next bet/round. The balance now keeps itself
  current: a **~10 s periodic poll** scoped to a connected SolanaToken game via the component lifecycle
  (paused when the tab is hidden with an immediate read on re-show; stopped on unmount / leaving the
  crypto game / disconnect), **plus immediate refreshes** — the player's **own bet updates instantly**
  via an **optimistic stake decrement** (`useGame.submitBet` passes `body.amount`; the poll reconciles
  once the debit propagates), the **win payout reads ~5 s after `Ended`** (`WINNER_SETTLE_DELAY_MS`, so
  the reward transfer has time to settle), and **refund / reward-error** refresh on settlement
  (`NotEnoughPlayersToStart` — previously missed — + `ErrorWhenProcessingRewards`). Plus a token-switch
  refetch (`watch(props.tokenAddress)`) and resilient last-known reads (no flash to `0`/blank, spinner
  only until first load). **Standard games are unchanged** (they show no balance — SpaceStroker holds no
  player float; a Standard/USD balance is out of scope). All in `TokenBalance.vue` + a one-line
  `useGame.ts` touchpoint; no backend/DTO/SignalR/data change. Frontend-only bug fix ⇒ **no integration
  test / no dedicated `features/` doc** (AGENTS.md rule 8 + ship feature-doc exemption); `monkify-front`
  has no unit-test harness → verified via `yarn lint`/`yarn build` + manual QA on the local stack
  (instant-on-bet + ~5 s-after-winner confirmed by the user). See `specs/wallet-balance-refresh.md`;
  Trello #67 ([m4rcHArM](https://trello.com/c/m4rcHArM)).
- _Previously shipped:_ **Make log not stored in DB** —
  `feature/no-db-logging` (`monkify-backend` + `monkify-meta`; **no frontend**), **shipped
  2026-07-17**. Backend-only infra/config cleanup: the app's Serilog output is **no longer persisted
  to Postgres**. The **PostgreSQL sink** is removed from `AddLogs` so logging is **Console-only**
  (stdout → Cloud Run/Cloud Logging in prod, terminal locally); the `Serilog.Sinks.PostgreSQL`
  package reference, the now-unused `tableName` parameter, and the `ConnectionStrings:SpaceStroker` read
  inside `AddLogs` are all gone, and both call sites (`SpaceStroker.Api`/`SpaceStroker.Consumers`) now call
  `builder.AddLogs()`. A raw-SQL EF migration `DropLogTables` drops the two sink-created tables
  (`SpaceStrokerApiLogs`, `SpaceStrokerConsumerLogs`) with `DROP TABLE IF EXISTS` (no-op `Down`; **empty
  model diff** — the tables were never in the EF model, so `SpaceStrokerDbContextModelSnapshot` is
  unchanged), applied by `SpaceStroker.Consumers` at startup. Log **levels/content unchanged**
  (Information non-prod / Warning prod); the integration harness (swaps `Log.Logger` post-build) is
  unaffected. QOL/infra change (no game-flow change) ⇒ **no new integration test, no dedicated
  `features/` doc**; **366 unit + 35 integration tests green** (no unit tests changed). Verified on
  the local Docker stack (`DropLogTables` in `__EFMigrationsHistory`, both log tables absent, logs to
  stdout). Spec `specs/no-db-logging.md`; Trello #68 ([ZlNLBc5C](https://trello.com/c/ZlNLBc5C)).
- _Previously shipped:_ **Create Terraform to deploy the app (GCP)** —
  `feature/terraform-gcp-deploy` (`monkify-meta` **primary** + `monkify-backend` + `monkify-front`; `monkify-lp`
  built by the tool, no repo change), **shipped 2026-07-17**. Production **GCP** deploy as
  **Infrastructure-as-Code + two local C# console apps**, all in `monkify-meta/deploy/`: a **Terraform** project
  split into a **persistent layer** (Cloud DNS zone + domain + remote-state bucket, `prevent_destroy`) and a
  **workload layer** (everything `teardown` may destroy); a **deploy** console app (`deploy [api|consumers|front|lp]`
  + `teardown`) that syncs a local unversioned `secrets.<env>.json` → **Secret Manager**, builds/pushes, and
  `terraform apply`s; and a read-write **DB REPL** over both DBs via the Cloud SQL Auth Proxy. Topology: **API**
  (Cloud Run autoscale, min≥1) + **Consumers** (Cloud Run CPU-always-on, `var.consumer_count`=1, **internal
  ingress** — hosts `/hangfire` + owns EF migrations/seeding, deployed **before** the API) + **front/lp** (GCS +
  Cloud CDN) behind an external **HTTPS LB + Cloud Armor** WAF; **Cloud SQL 16** (one instance, DBs `spacestroker` +
  `spacestroker_messaging`) + **Memorystore Redis 7** on a **private-only** VPC (no public IPs, Cloud NAT egress,
  Serverless VPC connector); **Artifact Registry** images; Cloud DNS + Google-managed TLS on the new
  **spacestroker.com** (hosts as TF variables). **Teardown keeps the domain** (workload-only destroy, typed
  confirmation, overrides Cloud SQL deletion-protection). Backend changes are Cloud-Run-readiness only (no game
  behaviour): **forwarded-headers** before HTTPS-redirect, config-driven **CORS** origin (was hardcoded
  `app.monkify.net`), and **`/health` on `SpaceStroker.Consumers`** — the **API already had `/health` via
  `HomeController`** (correction; only Consumers lacked one). Front: production build baked with
  spacestroker hosts + **Solana `mainnet-beta`** + dev flags off; CSP `frame-ancestors *` moves from `nginx.conf` to an
  **LB response-header policy**. **Builds on #64** (Postgres SQL transport, DB rename `monkify→spacestroker`
  + `spacestroker_messaging`, `ConnectionStrings:Messaging`, `CREATE DATABASE` grant) — **shipped to `main`**; the
  feature branches were synced with `main` before implementation.
  Built in two passes: **pass 1** = backend Cloud-Run-readiness + front `.env.example` (deployed +
  `curl /health`-verified locally, 366 unit tests green +3 `CorsOriginResolverTests`); **pass 2** = the full
  `deploy/` tree (Terraform persistent + workload/9 modules + `SpaceStroker.Deploy`/`SpaceStroker.DbConsole` + README +
  `secrets.example.json`) — **static-validated** (`terraform fmt`+`validate` both layers, `dotnet build` both
  apps). The real `terraform apply` is an **ops step** (needs a registered domain + real Solana/Fx secrets).
  **No new integration test** (infra tooling, not a game feature; 366 unit + 35 integration green on the branch).
  See `features/terraform-gcp-deploy.md`; spec `specs/terraform-gcp-deploy.md`; Trello #61
  ([QrjmQttY](https://trello.com/c/QrjmQttY)).
- _Last shipped:_ **LP launch relaunch — SpaceStroker rebrand + token-free (USDC)** —
  `feature/rebranding-spacestroker` (`monkify-lp` → `master` + `monkify-meta` → `main`; **no
  backend/front-app change**), **shipped 2026-07-16**. **Frontend-only** copy/structure/brand pass on
  the **arcade-overhaul LP redesign**, finishing the LP work the rebrand (#49) deliberately **deferred**
  (`specs/rebrand-spacestroker.md` §9.3) — landed as **"one coherent landing"**: the overhaul + roadmap
  overhaul + this launch pass in a single merge (LP `master` fast-forwarded `c6e1c36` → `f4f9da3`). Full
  user-visible **SpaceStroker** rebrand (hero/footer wordmark **SPACE·STROKER**, coin `alt` de-tokenized),
  **token-free/USDC** launch messaging (stake = USDC, no `$MONKIFY`), **Tokenomics section removed** → a
  short honest **"No token. Yet."** note (a token may come later; **no ticker/CA**; anti-scam line),
  **roadmap** perks reframed future/earned (no holder-gating, phase 1 no "$MONKIFY live") + a light
  "token later / early players first" tease, hero secondary CTA → community **"Join the troop"**
  (`#community`), header "Buy" removed + "Tokenomics" nav → **"Token"** (desktop + mobile). **"Play now"
  → a flagged placeholder app URL** (`https://PLACEHOLDER-app-url/space-stroker/game`; real route confirmed,
  **domain still TBD**); social links stay `#` — both carry `// TODO(ship)` and **must be filled before
  the public launch**. Trust points kept + completed (provably-fair + instant on-chain Solana payouts +
  auto-refund); Edson, all art, the 6-slide story, and the retro/arcade skin **unchanged**. **No backend
  ⇒ no integration/unit test** (DoD = `npm run build` + browser QA; 11 acceptance criteria). A **full
  visual redesign** (keep sprites + story, reimagine layout/motion) is a planned follow-up card off
  `master`. See `features/lp-space-stroker-relaunch.md`; spec `specs/lp-space-stroker-relaunch.md`; Trello #51
  ([Y0PpEDii](https://trello.com/c/Y0PpEDii)).
- _Last shipped:_ **Replace RabbitMQ with MassTransit Postgres SQL transport** —
  `feature/masstransit-postgres-transport` (`monkify-backend` + `monkify-meta`; **no frontend**),
  **shipped 2026-07-16**. Swaps the MassTransit transport from **RabbitMQ** (`UsingRabbitMq`) to the
  **Postgres SQL transport** (`UsingPostgres`, `MassTransit.SqlTransport.PostgreSQL` 8.5.4) with **no
  change to event contracts or consumer handler logic** — so the app can deploy to GCP with no broker
  VM. Messaging tables live in a **separate DB `spacestroker_messaging`** (schema `transport`) on the
  **same** Postgres instance as the game DB, created at `SpaceStroker.Consumers` startup by MassTransit's
  **Postgres migration hosted service** (`AddPostgresMigrationHostedService`; API stays publisher-only,
  no provisioning). Reuses the **game-DB app user** (needs CREATE DATABASE ⇒ #61 grant) via an explicit
  new secret **`ConnectionStrings:Messaging`** (bound to `SqlTransportOptions` via
  `NpgsqlConnectionStringBuilder`); keeps the **8 explicitly-named receive endpoints**; renames the
  publish abstraction `IRabbitMQService`/`RabbitMQService` → **`IMessageBus`/`MessageBusPublisher`**
  (`Services/Messaging`). Also folds in a config-only game-DB rename **`monkify → spacestroker`** (+
  `monkify-network → spacestroker-network`) and drops the local `rabbitmq` container. **363 unit + 35
  integration tests green** (+`TransportIdempotencyFlowTests` redelivery idempotency; harness now runs
  the game flow over the Postgres transport). **Unblocks #61** (terraform GCP deploy). See
  `features/masstransit-postgres-transport.md`; spec `specs/masstransit-postgres-transport.md`; Trello
  #64 ([nPCH4e5R](https://trello.com/c/nPCH4e5R)).
- _Last shipped:_ **Rebranding (new name): Monkify → SpaceStroker, Session → Game** —
  `feature/rebrand-spacestroker` (`monkify-backend` + `monkify-front` + `monkify-meta` +
  `monkify-lp`), **shipped 2026-07-16**. A **deep rename refactor** (not a
  behaviour change): the brand proper noun **`Monkify → SpaceStroker`** (only the token — the monkey
  character/theme and the common noun "monkey" stay) and the domain term **`Session → Game`** (pure
  mechanical across the whole `Game*` family — a `Game` is one round, `GameParameters` its
  definition) across code identifiers, DB schema (greenfield EF rename migration — `Games`,
  `GameParameters`, `GameBets`, …; FK `GameId`), SignalR (`ActiveGamesHub` at `/Hubs/Games`), API
  routes (`/games/*`) + frontend routes (`/space-stroker/*`), config (`spacestrokersettings.json`,
  `GeneralSettings:Games`, `ConnectionStrings:SpaceStroker`), storage/postMessage keys (`space-stroker.*`,
  `source:'space-stroker'`), and all living + spec docs. **Clean break** (no aliases/redirects/dual
  channels). **Out of scope:** the Solana token symbol/`$MONKIFY` ticker + `app.monkify.net` domain
  (→ cards #62/#63 + ops), all tooling (`monkify-*` skills, MonkAIfy board, `AGENTS.md` clone URLs,
  repo-folder names). Rebrand/refactor ⇒ **no new integration test** (existing suites renamed + kept
  green — 363 unit + 34 integration). Spec `specs/rebrand-spacestroker.md`; Trello #49 ([UBbiaz0d](https://trello.com/c/UBbiaz0d)).
- _Previously shipped:_ **Main production casino (default operator + auto-mint)** —
  `feature/main-production-operator` (`monkify-backend` + `monkify-front` + `monkify-meta`),
  **shipped 2026-07-15**. Seeds a default **production** operator **"Main"** (fixed RFC-4122 id,
  own registered Solana token from `GeneralSettings.Token`, three **SolanaToken** default types —
  6-char alphanumeric race / preset "horse race" over 8 runners / 24h lottery, **free bets off**) in
  **all** environments: **production seeds only Main**, non-prod also keeps **DEV Cassino** (+ dev
  landing) unchanged. Adds a **production-enabled** `POST /launch-token/default` that mints an
  **anonymous** (`sub` empty) launch token for **Main only** (never any other operator), signed with
  Main's own launch key. Frontend `useLaunch.ensureLaunchToken()` auto-mints Main when there is no
  fragment/stored token **and not embedded** (real tokens win; marker flags auto-minted tokens); a
  **401 of an auto-minted token** triggers a silent **re-mint + retry** instead of `/error`
  (real-operator 401 unchanged), and the prod token-less entry `/space-stroker/game` redirects to the game
  page instead of `/error`. **No schema/migration** (data seeding). **Integration test waived** for
  this card (seeding/config + thin mint endpoint + FE fallback; existing SolanaToken integration tests
  cover bet→run→reward). **363 unit + 34 integration tests green** (+3 `LaunchControllerTests`, +4
  `DefaultGameParametersTests`). See `features/main-production-operator.md`; spec
  `specs/main-production-operator.md`; Trello #60 ([bdkTZJOL](https://trello.com/c/bdkTZJOL)).
- _Last shipped:_ **Mobile layout for the game screen** — `feature/mobile-layout`
  (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-15**. **Frontend-only**
  responsive layout for the two main player pages (games lobby + game screen), portrait + landscape
  phones, standalone and embedded. Mobile engages **below `lg` (1024px)** (reuses the existing
  `GameMonkey hidden lg:block` boundary, covering landscape phones); a shared `useIsMobile()` composable
  drives the branches. **Lobby:** the already-1-col grid, with the card description clamped to one line.
  **Game:** `GamePage` swaps the desktop three-sibling row for a **`GAME | CHAT·STATS` segmented view**
  (`GameMobileTabs`) — the two views are **absolutely stacked so a hidden view can't intercept taps**,
  and both stay mounted (chat keeps its live subscription). `GAME` = a full-width fluid terminal (JS
  height-driven width skipped) + a **compact bet panel** (native keyboard / preset keypad, lever =
  `PLACE BET`, **no amount field**); `CHAT·STATS` = the chat + stats bodies full-width (no absolute
  cabinet) with an **unread dot** (`useChatUnread`); the monkey stays hidden. Header stays inline; the
  customization modal scrolls + its seed row wraps (`public/verify.html` already responsive). **Net-new
  mobile crypto wallet-connect:** an **"OPEN IN WALLET APP"** deep-link (Phantom/Solflare in-app browser,
  `utils/walletDeepLink.ts`) for SolanaToken bets with no injected wallet, reusing the existing
  `sendToken` flow (Android MWA deferred); the bet POST is unchanged so no backend work. Design mockups
  (Claude Design) were approved before build. Corrected two card items during implementation: no amount
  stepper; the header "shield" is the dev-admin button, not a provably-fair verifier. **Front build +
  lint green; frontend-only ⇒ no backend integration test required.** Card #5
  ([TOTAUpmq](https://trello.com/c/TOTAUpmq)); `features/mobile-layout.md`; spec `specs/mobile-layout.md`.
- _Last shipped:_ **Side panel cabinet restyle + maximize** — `feature/side-panel-cabinet`
  (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-15**. **Frontend-only,
  presentation-only** UX cleanup
  of the `/game` right-docked CHAT/STATS side panel (the `GameSidePanel` bundle). The panel is
  re-housed as a **diegetic arcade-cabinet module** — metal bezel (`terminal.border #92a1b9`) + corner
  rivets + a recessed near-black CRT screen with faint scanlines — matching the word-input housing
  instead of the old translucent floating web box, and moved back on-system: orange (`#ffa214`) active
  tab + labels + Send on blue-grey chrome, with terminal green kept **only** inside the live chat log
  (VT323 CRT text, green sender names, **own** messages orange). The floating green "Hide" pill is
  gone. The panel now has **three states**, driven by header controls (a square **maximize** icon +
  a collapse caret): **collapsed** → a vertical metal **dock rail** (CHAT/STATS) that expands straight
  to a tab; **docked** (default) → the narrow right-rail cabinet (same `h-50%` upper-band height as
  maximized, so it also clears the monkey); **maximized** → a wide landscape CRT (`w-[40vw]`/max-720,
  `h-50%`) pinned to the **top-right play area**, where the maximize icon swaps to a **restore** icon. **Chat timestamps** (`HH:MM`) are hidden in the docked view and
  **restored (with a larger log font) only when maximized**, where there's width for them. Chat also
  gains a **connection meta line** ("CONNECTED AS …" / "CONNECT WALLET TO CHAT" / "CHAT CLOSED") beside
  the char counter and warmer in-world idle copy; STATS promotes "0% house cut" to a top trust badge
  and renders a label/value hierarchy (Silkscreen labels over VT323 values, biggest-pot in orange).
  Also nudges the **monkey** to the lower-centre of the visible play area (`GameMonkey.vue`: drop the
  `absolute bottom-20`, bottom-align + reserve the panel width on the right). Touches only
  `GameSidePanel.vue`, `GameChatPanel.vue`, `GameStatsPanel.vue`, `GameMonkey.vue`; both tab bodies
  still stay mounted (`v-show`, incl. while collapsed) so chat keeps its live subscription, and STATS
  still lazy-fetches on first activation. No backend / DB / SignalR / data-shape change, so no
  integration test required (frontend-only presentation tweak). See `features/game-type-stats.md` +
  `features/game-chat.md`.
- _Last shipped:_ **Lottery pot stacking** —
  `feature/lottery-pot-stacking` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-14**. Makes lottery
  pots roll over: a new `decimal Game.CarriedExtra` (default 0) holds a running jackpot above the
  config base, so `pot = Σ bets + LotteryParameters.LotteryPot + CarriedExtra`. A **preset lottery that
  draws a choice nobody bet on** (`NumberOfWinners == 0`) carries `prev.CarriedExtra + Σ losing stakes`
  (stakes NOT refunded); an **under-subscribed** round refunds stakes and carries the jackpot unchanged;
  any **paid win** (incl. a free-bet winner) resets the successor to `0`. Carry decided once at
  rollover via a `LotteryPotCarry.NextCarriedExtra(Game)` helper (used by `RunGameConsumer`,
  `BetPlacedConsumer`, and startup backfill). Admin **deactivation** of a lottery holding un-won money
  requires a `TargetLotteryId` and **transfers** `CarriedExtra` into an eligible active lottery (same
  operator, method, and `OperatorTokenId` for SolanaToken); **blocks** when none exists. Frontend `> POT:`
  seeds from `LotteryPot + CarriedExtra`; admin deactivate gains an eligible-target picker. Seeded
  dev-only **1-min preset lottery** ("6L Minute Menagerie"). **356 unit + 34 integration tests green**
  (+`LotteryPotCarryTests`, +2 pot-with-carry, +1 CreateGame carry, +6 deactivation-transfer, +2
  Testcontainers flow tests: preset no-winner stack + deactivation transfer). See
  `features/lottery-pot-stacking.md`; spec `specs/lottery-pot-stacking.md`. Trello #53
  ([1Mhgvlq6](https://trello.com/c/1Mhgvlq6)).
- _Last shipped:_ **Display name per connected wallet for SolanaToken games** —
  `feature/wallet-display-name` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-14**. Full-stack fix so SolanaToken display-name/preferences key off the **connected wallet**
  everywhere. **Backend (scope corrected from the original "frontend-only" plan):** the preference
  handlers now **prefer the wallet when provided** (`request.Wallet ?? sub`) — the previous `sub`-first
  resolution ignored the wallet for the **dev operator and any operator offering both Standard +
  SolanaToken games**, whose launch token always carries a non-empty `sub`, so the display name was
  saved to the `sub` `OperatorPlayer` while crypto bets (keyed by the wallet) rendered `ANON`; and
  `GameService.InsertBet` now captures `GetWalletAddress()` **before** nulling `OperatorPlayer` so
  live `BetNotificationEvent`s carry `externalPlayerId` (own live crypto bets render `YOU`).
  **Frontend:** `App.vue` reloads preferences reactively on wallet/game-method change (sending the
  wallet **only** in a SolanaToken context, so a Standard player's stray wallet still writes to their
  `sub`); the customization modal shows a **"Connect wallet to customize"** gate when no wallet is
  connected AND (the game is `SolanaToken` OR there is no usable `sub`), and hides the
  preferred-currency dropdown for SolanaToken. Display-only; no entity/EF/migration/DTO/SignalR change.
  **342 unit + 32 integration tests green** (+2 precedence unit tests, +1 happy-path integration test
  `CryptoPreferences_TokenAlsoHasSub_WalletTakesPrecedenceOverSub`). See `features/wallet-display-name.md`;
  spec `specs/wallet-display-name.md`. Trello #58 ([sohAuZN0](https://trello.com/c/sohAuZN0)).
- _Last shipped:_ **Iframe embedding + embedded crypto (SolanaToken) play (2 stacked cards, one branch)** —
  `feature/iframe-crypto-games` (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-14**.
  Two frontend-only features merged to `main` in one stacked merge (the branch contained both #3's
  `feature/iframe-crypto-games` (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-14**.
  Two frontend-only features merged to `main` in one stacked merge (the branch contained both #3's
  `4e53e02` and #41's `9784354`; `feature/iframe-embedding` is a contained ancestor). **#3 "Make the game
  Iframe-able"** ([RGvJZNTW](https://trello.com/c/RGvJZNTW)): a **regulated (Model A)** casino embeds the
  game in an `<iframe>` — `useEmbed` detection (`window.self !== window.top` \| `?embed=1`), a secret-free
  `{source:'space-stroker',type,payload}` postMessage lifecycle API (`ready`/`game-started`/`bet-placed`/
  `bet-settled` bettors-only/`game-ended`/`resize` hint), and the **core fix**: an embedded 401 /
  expired / invalid token / inactive operator emits `token-expired` + a light "relaunching" state instead
  of dead-ending on `/error` (branch in `mainService.ts` + `useOperator.ts`); app fills the frame
  (`main.css`), permissive `frame-ancestors *` in the committed `nginx.conf` (prod host must match), static
  `public/embed-example.html` + `EMBEDDING.md`. **#41 "Embedded crypto (SolanaToken) games"**
  ([PVUH9zJZ](https://trello.com/c/PVUH9zJZ)): paid **`SolanaToken`** (on-chain) bets inside that iframe —
  extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move to a
  **same-origin popup** on the game host (route `/space-stroker/wallet-bridge` → `WalletBridgePage.vue`) that
  `postMessage`s the **signature + wallet** back over an internal same-origin channel
  (`source:'space-stroker-wallet'`, `utils/walletBridge.ts`); `useGame.bet()` acquires the signature in-frame
  (`sendToken`) **or** via the popup (`useEmbeddedWallet.ts`), opened **synchronously** on click (blocked →
  visible affordance); the `GameConsole` in-frame `connected` gate is relaxed when embedded; free (amount 0)
  SolanaToken games embed with no wallet/popup; #3's lifecycle payloads become **token-native**
  (`tokenSymbol`, never USD). Standalone play unchanged. See `features/iframe-embedding.md` +
  `features/iframe-crypto-games.md`; specs `specs/iframe-embedding.md`, `specs/iframe-crypto-games.md`.
- _Last shipped:_ **Per-game general stats (all-time, per game type)** —
  `feature/game-type-stats` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-10**. Casino-style per-game-type stats via `GET /games/{parametersId}/stats` (biggest
  pot, biggest single payout, rounds played, total paid out, hottest-choice for preset types) rendered
  in a lazy **STATS** tab of the `/game` right-side panel (bundled with **CHAT** via `GameSidePanel`).
  **On-demand aggregate + Redis cache (~120s), no new entity/migration** — a documented reversal of the
  card's maintained-counter idea, justified because the panel fetches only on open (no lobby fan-out).
  Only `RewardForWinnersCompleted` rounds count; money from `TransactionLog` Credit rows; free bets
  excluded (free-only rounds still count as played); single currency per type (Standard USD + preferred
  display currency, SolanaToken token-native); token-gated + operator-scoped (vague on
  cross-operator/expired). **325 unit + 29 integration tests green.** Surfaced a pre-existing
  multi-winner reward double-credit race under redelivery (`RewardGameConsumer`) — tracked
  separately. See `features/game-type-stats.md`; spec `specs/game-type-stats.md`; Trello #43
  ([oxZkifXh](https://trello.com/c/oxZkifXh)).
- _Last shipped:_ **Provably fair (commit-reveal + portable RNG + player verifier)** —
  `feature/provably-fair` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped 2026-07-10**.
  Replaces the post-lock `Int32` + `System.Random` seed with a **commit-reveal `ServerSeed` committed on
  the first bet** (atomic first-writer-wins; `ServerSeedHash` published during betting, raw seed revealed
  after `Ended`) and a **portable HMAC-SHA256 counter keystream** (rejection sampling, canonical bet
  order `CreatedDate,Id`, labelled fallback) reproducible in any language. Client seed is **Stake-style**
  (persistent, settable/rotatable, localStorage) with a per-bet **`nonce`** — `Bet.Seed =
  HMAC-SHA256(clientSeed, nonce)`, raw client seed never leaving the browser. Reveal-gated
  `GET /games/{id}/verification` + `ServerSeedHash` on `ActiveGameDto`; verification via that
  endpoint + standalone offline `public/verify.html` (**the in-terminal "Verify this round" recompute
  was dropped** — the terminal shows commitment/reveal only). Greenfield migration (drop `Game.Seed`;
  add `ServerSeed`/`ServerSeedHash`, `Bet.Nonce`; widen `Bet.Seed` to 64). **329 unit + 2 provably-fair
  integration tests green.** Bundled a `GameTypeCard` price fix (games-list price now mirrors the
  in-game `BET:` label). Out-of-repo `04 Fairness & game rules.pdf` regenerated to the new scheme. See
  `features/provably-fair.md`; spec `specs/provably-fair.md`. Trello #42
  ([XWAeIgIQ](https://trello.com/c/XWAeIgIQ)).
- _Last shipped:_ **Refactor payment models** — `feature/payment-methods-refactor`
  (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped 2026-07-08**. Removes the
  operator-level `Operator.PaymentModel` (`Regulated`/`Crypto`) and moves payment to a per-game
  **`GameParameters.PaymentMethod`** (`Standard` = off-chain operator wallet, USD; `SolanaToken` =
  on-chain via a per-operator **registered `OperatorToken`**, token-native amounts). A unified
  `IPaymentMethodService` strategy + resolver dispatches Debit/Credit/Refund by game method
  (availability = wallet-adapter for Standard, ≥1 active token for SolanaToken); admin token
  register/list/deactivate (private key **encrypted at rest** — shipped as **AES-256-GCM keyed from a
  `GeneralSettings.SecretProtection` passphrase**, a deviation from the spec's Data-Protection plan); a
  ledger split (`Bet` & `TransactionLog` drop the Solana signature and carry `ExternalTransactionReference`
  + `PaymentMethod`; new `CryptoTransactionLog` holds on-chain detail); identity per method; display
  currency only for Standard; one dev operator (fixed RFC-4122 GUID
  `11111111-1111-4111-8111-111111111111`) pre-provisioned for both methods, with **all** default game
  types Standard plus **3** SolanaToken defaults. Dev/greenfield migration. **314 unit + 28 integration
  tests green.** See `features/payment-methods.md`; spec `specs/payment-methods-refactor.md`. Trello #38
  ([FH5TMT0T](https://trello.com/c/FH5TMT0T)).
- _Last shipped:_ **Audio overhaul with AI (3-card feature, one shared branch)** —
  `feature/audio-overhaul` (`monkify-front` + `monkify-meta`; **no backend**), 2026-07-07. A cohesive
  AI-generated (Suno) **warm arcade-chiptune** sound identity, all frontend and display-only: idle loop
  on `WaitingBets`, a **one-shot** Game Starting on `GameStarting` (once per game, running variant
  preloaded here), a **random** of three running loops on `InProgress`; own-bet/button/error SFX;
  **bettors-only** victory/lose on `Ended` (once); a **menu loop on the games page** (admin stays
  silent); and a header **mute+volume** control (`localStorage` `space-stroker.audio`, master gain in
  `helpers.ts`, live re-apply). **9 generated mp3s** (kept `error1.wav`, dropped `success1.wav` + the 4
  old `.wav`s). Shipped as three cards on one branch: **(1/3)** manifest [#17 `9qiDtDAe`], **(2/3)**
  generate [#34 `OuXhyqyU`] (done **manually in the Suno UI** + ffmpeg), **(3/3)** wire [#35 `RvVdpO25`].
  **The 3/2/1 proximity "coin" bleeps were dropped** (Suno couldn't produce them) — near-miss visual
  unchanged, no proximity audio. **Extras beyond spec:** `/`→`/space-stroker/game` redirect, and a
  **game-connection teardown fix** (`disconnectAllPreviousConnections` enabled) that stops a stale
  game's SignalR events from corrupting the shared game state on switch/rollover. See
  `features/audio-overhaul.md`.
- _Previously shipped:_ **Characters Away (near-miss reveal)** — `feature/characters-away`
  (`monkify-front` + `monkify-backend` + `monkify-meta`), 2026-07-07. While a round runs, a
  bottom-docked terminal row per **own** bet choice shows how close the monkey is to typing it
  (`> N AWAY!`, KMP tail-prefix `away = L − k`): it climbs, **resets to the start and re-climbs after
  a 2s stall**, and on a win **types out the full word**; a `> POT:` line shows the in-game pot. New
  `src/utils/nearMiss.ts` + `src/hooks/useNearMiss.ts`, wired into `useGame.ts` (`ownBetChoices`,
  `feed`/`finish`/`reset`; `lastOwnBetChoice` untouched) + `GameTerminal.vue` dock. Backend:
  `GameService.RunGame` emits the **fallback-decided** winner as a final `<gameId>/Terminal`
  batch so the terminal ends with the word "typed" (unit-tested; display-only, outcomes unchanged).
  Trello #30 ([vlarPOaw](https://trello.com/c/vlarPOaw)). See `features/characters-away.md`.
- _In progress:_ **Play page UX fixes** — `feature/play-page-ux-fixes` (`monkify-front` +
  `monkify-meta`), started 2026-07-01; three independent frontend-only changes: (1) hide the wallet
  cluster (Connect Wallet button + USDC balance) in `GameHeader.vue` for **regulated** operators,
  (2) show `> BET: {requiredAmount} {betCurrency}` for **every** game type in `GameTerminal.vue`
  (`betCurrency`: crypto `USDC` / regulated `credits`), and (3) fix `GameMonkey.vue` so the
  received-bet animation caps at 2 and always settles to a resting frame instead of looping. No
  backend change; real-fiat currency for regulated operators is a deferred backend follow-up.
- _Last shipped:_ **Show game pot value** — `feature/game-pot-display` (`monkify-front` +
  `monkify-meta`), 2026-07-07; frontend-only. The `> POT:` line is now shown for **every** game
  type (free-type race, preset race, lottery), not just lottery: `useGame.ts` renames
  `currentLotteryPot` → `currentPot`, seeds it as `Σ bets` (+ `lotteryPot` base only for lottery), and
  accumulates live by removing the `isLottery` gate on the `/Bets` stream; `GameTerminal.vue` renders
  a universal POT line in the pre-run view (lottery block keeps only `> FREQUENCY:`) plus a second POT
  line in the running/ended view so it stays visible during and after the run (AC #8). Reuses
  `formatAmount`; free bets contribute 0; resets/reseeds on rollover; no backend/DB/SignalR change.
  Trello #32 ([0FJquICm](https://trello.com/c/0FJquICm)). See `features/game-pot-display.md`.
- Previously shipped: **Operator page (operator admin portal)** — `feature/operator-admin-page`,
  2026-07-06; operator self-service admin at `/space-stroker/admin` with admin-scoped RS256 token
  (`X-Admin-Token`, not contract-gated), create/list/deactivate game types (all 3 modes;
  deactivate-or-create, no edit), first-game-on-create event, deactivation refunds waiting
  games. Trello #31 ([JZcMz3ma](https://trello.com/c/JZcMz3ma)). See
  `features/operator-admin-page.md`.
- Previously shipped: **Add chat (game chat v2)** — `feature/game-chat`, 2026-07-03; per-game
  chat on `/game` via `ActiveGamesHub` (`SendChatMessage`, display-name identity, audit DB +
  daily purge, client 200-char/1s limits, no history replay). Trello #4 (`7UnjEOom`). See
  `features/game-chat.md`.
- Previously shipped: **Player Customization Modal** — `feature/player-customization-modal`, 2026-07-02;
  header-gear modal for **display name** (all operators) and **preferred display currency** (regulated
  only); `GET /fx/rates` + 6h Redis cache, `GET /players/preferences`, PATCH allow-list + name
  validation, `DisplayName` on bet DTO/SignalR; client-side display-only conversion (settlement stays
  USD). Trello #25 (`ZyJViMKI`). See `features/player-customization-modal.md`.
- Previously shipped: **Sales technical documents (B2B SaaS)** — `feature/sales-technical-documents`,
  2026-07-01; per-model Markdown sales docs under `sales/` (`model-a/` + `model-b/` pitch and
  under-NDA integration overview, single anti-clone playbook, README + template); EN + PT-BR;
  placeholders only, paid steps DEFERRED, PDF export deferred. Non-code; backend/front read-only
  grounding. Trello #27 (`NFNDRlBk`). See `features/sales-technical-documents.md`. Sibling legal
  batch still in flight on `feature/legal-pitch-documents-batch-1`.
- Previously shipped: **RG pre-bet hook** — `feature/rg-pre-bet-hook`, 2026-07-01; an opt-in
  per-operator responsible-gaming pre-bet check (`IPreBetCheckService` + resolver + shared mock)
  called in `RegisterBetHandler` after local validation and **before** the wallet debit; allow /
  deny(reason, specific message) / **fail-closed** unavailable; applies to both payment models; no
  provider configured ⇒ skipped; log-only, no migration; frontend unchanged. Integration doc
  descoped to a separate story. Trello #20. See `features/rg-pre-bet-hook.md`.)
- Previously shipped: **DEV QOL Stuff** — `feature/dev-qol-stuff`, 2026-06-30; operator resolved
  from the launch token's `iss` instead of the URL (routes param-free; prod launch lands on
  `/space-stroker/game/games`), dev mint returns `{ token }` only + non-expiring, silent auto-mint
  replaced by dev launch buttons gated on `VUE_APP_DEV_LAUNCH`, and a new `SpaceStroker.Tests.Integration`
  Testcontainers suite. Trello #28. See `features/dev-qol-stuff.md`.
  Backlog follow-ups: SpaceStroker commission per bet → https://trello.com/c/BXgxJcwP #26.

## The 60-second mental model

- **Backend** (`monkify-backend`) is a layered .NET 8 solution split into a web **API**
(`SpaceStroker.Api`) and a background worker host (`SpaceStroker.Consumers`). They share `SpaceStroker.Domain`,
`SpaceStroker.Infrastructure`, and `SpaceStroker.Common`.
- A **bet** is placed via `POST /games/{id}/bets`. The handler validates the on-chain Solana
payment signature, persists the bet, and publishes a `BetPlacedEvent`.
- **Games** progress through a state machine driven by **RabbitMQ events** (via MassTransit)
consumed in `SpaceStroker.Consumers`: `BetPlaced → RunGame → RewardGame`, with
`RefundInvalidBet` for failures.
- The **typer** (`SpaceStrokerTyper`) runs the game: it emits random characters over **SignalR** until
a bet's choice appears; winners are then rewarded via **Solana** SPL token transfers.
- **PostgreSQL** (EF Core / Npgsql) is the store. **Redis** caches active parameters, FX rate tables
  (6h TTL), and backs SignalR. **Hangfire** (on PostgreSQL) schedules lottery games.
- **Frontend** (`monkify-front`) is Vue 3 + TypeScript: composables in `src/hooks`, an axios
service layer in `src/services`, and SignalR clients to stream the live typer + bets.
