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
   already exists in production (games, the settlement path, player identity, free-bet mode, etc.).
   The spec you are building is one piece of a shared system, not an isolated module.
3. While designing and coding, actively look for **cross-feature interactions**: shared entities
   and tables, request headers, payment and payout paths, game/bet lifecycle, the single ledger,
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
| `glossary.md`               | Game, Bet, Player, Typer, GameParameters, the ledger, etc.                         | Drafted              |
| `conventions/backend.md`    | EF inline in DbContext, migrations in Consumers, MediatR handlers, MassTransit consumers | TODO (stub)          |
| `conventions/frontend.md`   | Vue 3, composables (`src/hooks`), types in `src/types`, axios service layer              | TODO (stub)          |
| `features/games.md`      | Game lifecycle, the typer, rewards                                                    | Drafted              |
| `features/free-bet-mode.md` | Dev/QA free bets without Solana payment; distilled from `specs/free-bet-mode.md`         | Shipped              |
| `features/player-identity.md` | Wallet-keyed `Player` model (was `OperatorPlayer`); no `Bet.Wallet`; distilled from `specs/operator-player-identity.md` | Shipped              |
| `features/remove-operators.md` | **The deployment is the operator.** Deletes the whole multi-tenant apparatus — `Operator`/`OperatorToken`/`OperatorPlayer`, the `X-Launch-Token` surface, the off-chain `Standard` payment method + `PaymentMethod` enum, the per-operator RG pre-bet hook, `ISecretProtector`, operator branding and the FX/display-currency stack. Everything token/game/admin now reads from **`GeneralSettings:Operator`** (`Token` + `AdminToken` + `DefaultGames`, seeded create-only by name); player endpoints are **public** and identity is the **connected wallet** (`Player`), with the display-name write ed25519-signature-gated on a purpose-scoped nonce. **One ledger:** `CryptoTransactionLog` merged into `TransactionLog` (nullable `BetId`, `RelatedSignature`; `ExternalTransactionReference` → `Signature`). Greenfield migration. 492 unit + 77 integration green | Shipped              |
| `features/typer-pacing-scalability.md` | Wall-clock typer pacing, fallback winner, duration bounds; distilled from `specs/typer-pacing-scalability.md` | Shipped              |
| `features/dev-qol-stuff.md` | Dev QOL: operator from token (not URL), dev launch buttons (`VUE_APP_DEV_LAUNCH`), `SpaceStroker.Tests.Integration` suite | Shipped              |
| `features/player-customization-modal.md` | Header-gear modal: display name (all operators) + preferred display currency (regulated only); `GET /fx/rates`, `GET /players/preferences`, client-side USD→fiat rendering; display-only | Shipped              |
| `features/game-chat.md`  | Per-game chat on `/game` via `ActiveGamesHub`; display-name identity, DB audit persistence + daily purge, client 200-char/1s limits, no history replay | Shipped (v2)         |
| `features/admin-page.md` | Admin portal at `/admin`: admin-scoped RS256 token (`X-Admin-Token`) validated against the **configured** keypair (no `iss`, no contract check), create/list/deactivate game types (all 3 modes), deactivate-or-create (no edit), `POST /dev/admin-token` outside production | Shipped              |
| `features/sales-technical-documents.md` | B2B SaaS sales doc set in `sales/` — per-model pitch + under-NDA integration overview (Model A/B), anti-clone playbook; EN + PT-BR | Shipped              |
| `features/game-pot-display.md` | Frontend-only: the `> POT:` line is shown for **all** game types (races + lottery), seeded from `Σ bets` (+ lottery base), live via `/Bets`, visible during/after the run; distilled from `specs/game-pot-display.md` | Shipped              |
| `features/characters-away.md` | Near-miss reveal: bottom-docked `> N AWAY!` row per own choice (KMP tail-prefix `away = L − k`) that climbs, loops every 2s, and types out the winner on a win; in-game `> POT:`; backend emits the fallback-decided winner as a final terminal batch | Shipped              |
| `features/audio-overhaul.md` | Frontend-only AI (Suno) arcade-chiptune audio: idle/starting/random-running background per game status, own-bet/button/error SFX, bettors-only victory/lose, menu loop on the games page, header mute+volume (`localStorage`); 9 mp3 assets (kept `error1.wav`). Proximity "coin" bleeps dropped. Also: `/`→`/game` redirect + game-connection teardown fix | Shipped              |
| `features/iframe-embedding.md` | Frontend-only: a **regulated (Model A)** casino embeds the game in an `<iframe>` — `useEmbed` detection, secret-free `{source:'space-stroker'}` postMessage lifecycle API, embedded 401→`token-expired`+"relaunching" (never `/error`), fills the frame, permissive `frame-ancestors *`, example page + `EMBEDDING.md`; distilled from `specs/iframe-embedding.md` | Shipped              |
| `features/iframe-crypto-games.md` | Frontend-only, **stacked on #3**: paid **`SolanaToken`** bets inside the iframe via a **same-origin wallet-bridge popup** (`/wallet-bridge`) that returns the on-chain signature+wallet (`source:'space-stroker-wallet'`); popup opened synchronously on click, token-native amounts, no new parent contract; distilled from `specs/iframe-crypto-games.md` | Shipped              |
| `features/game-type-stats.md` | Per-game all-time stats per game type via `GET /games/{parametersId}/stats` (biggest pot, biggest single payout, rounds played, total paid out, hottest choice for preset types) — **on-demand aggregate + Redis cache, no new entity/migration**; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type, operator-scoped/vague; rendered in a lazy **STATS** tab of the `/game` right-side panel (bundled with **CHAT** via `GameSidePanel`) | Shipped              |
| `features/provably-fair.md` | Commit-reveal `ServerSeed` (committed on the first bet, revealed after `Ended`), portable HMAC-SHA256 keystream RNG + labelled fallback, Stake-style client seed + `Bet.Nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /games/{id}/verification` + offline `public/verify.html`; terminal shows the `> SERVER SEED:` reveal only (the `> SERVER SEED HASH:` commitment line was removed from the terminal 2026-07-25 — it lives in the **FAIR** tab + `verify.html`). **Verification surface superseded by `features/provably-fair-final.md`** | Shipped              |
| `features/provably-fair-final.md` | In-app verification: lazy **`FAIR`** tab in `GameSidePanel` (hidden when embedded, locked until the round ends) with two instant checks (commitment + own bet seed) and an **opt-in cancellable Web Worker** outcome replay, rollover retention, COPY VERIFICATION JSON, and a download link + instructions for the **kept** offline `public/verify.html`. Backend: `Game.CharactersTyped` persisted at run end → `winnerDecidedByFallback` **derived, never replayed**; payload **cached only once the round is final** (`GameVerification:{gameId}`, 12h), operator check **before** the cache read. Current round only | Shipped              |
| `features/games-history.md` | Standalone **Games History** page (`/game/history`, from a new header **menu button**: Settings + History + dev Admin; audio + wallet stay on the header). Read-only `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` → `PaginatedList<GameHistoryDto>` — **one game type at a time** (dropdown), **only `RewardForWinnersCompleted`** rounds, newest first, `pageSize`≤30, **"mine only"** toggle. Rows: ended date · type name · winning choice · pot (`Σ paid bets`+lottery base+carried) · **bet count** (free incl.) · winner count · short `serverSeedHash` · nullable **own-bet** block (choice/stake/won/payout) — no character stream. Money rules mirror **game-type-stats** (free bets excluded from pot/winners, single currency per type, `useDisplayCurrency`). Public page **Redis-cached 5 min** per operator+type+page+size; own columns joined **live** (cloned rows). "Mine" identity = `wallet` ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`; vague `ParameterNotFound`. **Verify = link-out only** to `/verification` + `public/verify.html`. **No entity/migration.** | Shipped              |
| `features/solana-connection-rpc.md` | **`monkify-backend` + `monkify-front`:** keep the game playable under RPC rate limits. Frontend `useConnection` keeps the **public RPC as primary**; a custom `fetch` detects **HTTP 429** and `withFailover` retries the same call on a new backend **`POST /rpc`** proxy, sticky for a cooldown (`VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS`), then drifts back — **429-only** (other errors surface as-is); `confirmTransaction` **always stays on the public connection**; unset `VUE_APP_SOLANA_PROXY_URL` ⇒ no-op passthrough. `sendToken` reworked: **single** idempotent-ATA + transfer (**one** approval), optional ComputeBudget **priority fee** (0 disables), integer/BigInt amount math, sign-once + `sendRawTransaction` (`preflightCommitment:'confirmed'` to match the blockhash). Backend `RpcController` = thin JSON-RPC passthrough to the reused key-bearing `Token.ClusterUrl`, gated by env-independent **Origin allowlist + 7-method allowlist + per-IP `*:/rpc` rate rule**, **not** launch-token gated; key never logged (`MaskRpcUrl`); **no entity/migration/consumer/secret**. | Shipped |
| `features/wallet-display-name.md` | SolanaToken player display-name/preferences key off the **connected wallet** everywhere: backend prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`); `InsertBet` carries `externalPlayerId` on live notifications; frontend reactive per-wallet load + "Connect wallet to customize" gate + currency hidden for SolanaToken | Shipped              |
| `features/lottery-pot-stacking.md` | Un-won lottery pots **stack** into the next round via `Game.CarriedExtra` (preset no-winner draw carries losing stakes; under-subscription carries the base unchanged; paid win resets). `pot = Σ bets + LotteryPot + CarriedExtra`; carry decided once at rollover by `LotteryPotCarry`. Admin **deactivation transfers** the un-won pot to an eligible active lottery (same operator/method/token) or blocks. Player `> POT:` reflects the jackpot; the dev 1-min preset lottery `6L Minute Menagerie` is **no longer seeded** (2026-07-23) | Shipped              |
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
| `specs/operator-admin-page.md` | Operator self-service admin portal (`/admin`): admin-scoped RS256 token (`X-Admin-Token`, reuses `LaunchPublicKey`; not contract-gated), create/list/deactivate game types (all 3 modes; deactivate-or-create, no edit), dev header icon → `POST /dev/admin-token`; `AllowFreeBet` hidden | Shipped              |
| `specs/game-pot-display.md` | Frontend-only: show the `> POT:` line for **all** game types (races + lottery), not just lottery; generalize `currentLotteryPot` → `currentPot` in `useGame.ts` (seed `Σ bets` + lottery base only for lottery, accumulate live via `/Bets`), move the POT `<p>` out of the lottery-only block in `GameTerminal.vue` + a second POT line in the running/ended view (AC #8); reuse `formatAmount`; no backend/DB/SignalR change | Shipped              |
| `specs/characters-away.md`  | Characters Away near-miss reveal. Spec body is the original 3/2/1 pinned-milestone design; **shipped** behaviour (see spec Post-ship notes + `features/characters-away.md`) is a bottom-docked closest-approach row per own choice (KMP `away = L − k`) that climbs + loops every 2s + types out the winner, plus in-game `> POT:` and a backend fallback terminal reveal | Shipped              |
| `specs/audio-overhaul.md`   | Audio overhaul with AI — **(1/3) manifest & Suno-generation spec**. Sound manifest + per-item Suno prompts + the frontend integration contract for cards 2 (#34) & 3 (#35). **Shipped reality differs:** the 3/2/1 proximity "coin" bleeps were dropped (Suno couldn't produce them) → **9 generated mp3s, not 12**, and no proximity audio. See `features/audio-overhaul.md`. Frontend-only; branch `feature/audio-overhaul` | Shipped              |
| `specs/audio-overhaul-generation.md` | Audio overhaul — **(2/3) asset generation spec**. Suno-MCP + ffmpeg generation pipeline with approve-each review. **Shipped reality:** generated **manually in the Suno UI** (MCP ran out of credits) and post-processed with ffmpeg; **9 assets** committed to `monkify-front/src/assets/audios` (kept `error1.wav`, dropped `success1.wav` + old `.wav`s); the 3 coin bleeps were cut. See `features/audio-overhaul.md` | Shipped              |
| `specs/audio-overhaul-wiring.md` | Audio overhaul — **(3/3) wiring spec**. `helpers.ts` master gain/mute, idle + one-shot Game Starting + random running (`GameAudio.vue`), own-bet/error/button, bettors-only victory/lose, header mute/volume, timing fixes; asset swap. **Shipped extras beyond spec:** proximity bleeps **omitted** (assets dropped); added a menu loop on the games page, a `/`→`/game` redirect, and a game-connection teardown fix. See `features/audio-overhaul.md` | Shipped              |
| `specs/payment-methods-refactor.md` | Removes operator `PaymentModel`; per-game **`PaymentMethod`** (`Standard`/`SolanaToken`); per-operator **registered Solana tokens** (encrypted key) via admin portal; unified `IPaymentMethod` strategy+resolver; ledger split — `Bet`/`TransactionLog` carry `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`. **Shipped** (encryption deviates: AES-GCM/config passphrase, not Data Protection) | Shipped |
| `specs/iframe-embedding.md` | Frontend-only: let a **regulated (Model A)** casino embed the game in an `<iframe>`. New `useEmbed` (`window.self!==window.top` \| `?embed=1`) + `utils/embed.ts` postMessage emitter (`{source:'space-stroker',type,payload}`, `targetOrigin:'*'`, secret-free, USD); embedded 401/expired/inactive-operator emits `token-expired` + light "relaunching" state instead of `/error` (branch in `mainService.ts` + `useOperator.ts`); lifecycle emits `ready`/`game-started`/`bet-placed`/`bet-settled`(bettors-only)/`game-ended`/`resize`(hint) from `useGame.ts`+`App.vue`; fills the frame (`main.css`); permissive `frame-ancestors *` in `nginx.conf`; static `public/embed-example.html` + `EMBEDDING.md`. **No backend.** Branch `feature/iframe-embedding` | Shipped              |
| `specs/iframe-crypto-games.md` | Frontend-only, **stacked on #3**: make paid **`SolanaToken` (on-chain)** bets work inside the #3 `<iframe>`. Extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move into a **same-origin popup** on the game host (route `/wallet-bridge`, `WalletBridgePage.vue`) that `postMessage`s the signature + wallet back (`source:'space-stroker-wallet'`, `utils/walletBridge.ts`); `useGame.bet()` acquires the signature in-frame **or** via popup (`useEmbeddedWallet.ts`), popup opened **synchronously** on click (blocked → visible affordance); relax the `GameConsole` `connected` gate when embedded; #3 lifecycle payloads made **token-native** (never USD). **No backend.** Branch `feature/iframe-crypto-games` | Shipped              |
| `specs/game-type-stats.md` | Per-game general stats per game type (all-time): `GET /games/{parametersId}/stats` → biggest pot, biggest single payout, rounds played, total paid out, hottest choice (most-picked, preset types only); **on-demand aggregate + Redis cache, no new entity/migration** (documented reversal of the maintained-counter idea); lazy-loaded `/game` stats panel; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type; token-gated + operator-scoped | Shipped |
| `specs/provably-fair.md` | Commit-reveal server seed (committed on the **first bet**, revealed after `Ended`), portable **HMAC-SHA256 keystream** RNG (rejection sampling, canonical bet order) replacing `System.Random`/`Int32`, Stake-style client seed + per-bet `nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /games/{id}/verification`, standalone `public/verify.html` verifier (in-terminal recompute dropped — terminal shows commitment/reveal only). Greenfield migration (drop `Game.Seed`; add `ServerSeed`/`ServerSeedHash`, `Bet.Nonce`). | Shipped |
| `specs/wallet-display-name.md` | **Full-stack:** make SolanaToken player display-name/preferences key off the **connected wallet** everywhere. Frontend: reactive per-wallet load (`App.vue`, wallet sent only in a SolanaToken context), customization-modal **gate** ("Connect wallet to customize" when no wallet AND (SolanaToken game OR no usable `sub`)), currency dropdown hidden in a SolanaToken context. **Backend (scope corrected):** prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`), and `GameService.InsertBet` captures the wallet address before nulling `OperatorPlayer`. Child of umbrella #46, escrow-independent | Shipped |
| `specs/lottery-pot-stacking.md` | **Full-stack:** un-won lottery pots **stack** into the next round via a running `Game.CarriedExtra` (preset no-winner draw carries `prev + Σ losing stakes`; under-subscription refunds stakes and carries the base unchanged; a paid win resets to the config base). `pot = Σ bets + LotteryPot + CarriedExtra`. Admin **deactivation transfers** the un-won pot to an eligible same-operator/same-method/same-token active lottery (blocks when none). Both Standard + SolanaToken; player `> POT:` reflects the jackpot; seeded 1-min preset lottery for testing. | Shipped |
| `specs/main-production-operator.md` | Default **production** operator **"Main"** (fixed RFC-4122 id, own registered Solana token + 3 SolanaToken default types: 6X race/preset "horse race"/24h lottery, free bets off) seeded in **all** envs (**prod = Main only**, non-prod also keeps DEV Cassino). New **production-enabled** `POST /launch-token/default` mints an **anonymous Main token** (empty `sub`, Main-only); frontend auto-mints on no-token, silent re-mint on 401, real tokens win. Seeding/config + thin mint endpoint + FE fallback — **no schema/migration; integration test waived**. | Shipped |
| `specs/mobile-layout.md` | **Frontend-only:** responsive **mobile layout** for the two main player pages — the games lobby (`/game`) and the game screen (`/game/play`) — for **portrait + landscape phones**, standalone and embedded. Mobile engages **below `lg` (1024px)** (reuses the `GameMonkey hidden lg:block` boundary, so landscape phones are covered). Lobby: 1-col condensed cards. Game: a `GAME | CHAT·STATS` segmented toggle (monkey hidden; terminal + compact bet panel — no amount field, lever = PLACE BET; chat/stats full-width). Customization modal made mobile-friendly (`verify.html` already responsive). Net-new **mobile crypto wallet-connect** ("open in wallet app" deep-link to the Phantom/Solflare in-app browser; Android MWA deferred). No backend change. | Shipped |
| `specs/rebrand-spacestroker.md` | **Rebrand refactor (all repos):** deep rename **`Monkify → SpaceStroker`** (brand proper noun only; monkey theme stays) and **`Session → Game`** (pure mechanical, whole `Game*` family) across code, DB (greenfield rename migration), SignalR (`ActiveGamesHub` `/Hubs/Games`), routes (`/games`, FE `/space-stroker/…`), config (`spacestrokersettings.json`, `GeneralSettings:Games`), and docs. Clean break (no aliases). Tooling (`monkify-*` skills, MonkAIfy board, clone URLs, repo-folder names) and the Solana token symbol (#62/#63) are out of scope. **(Historical: SpaceStroker was later renamed to Space Stroker — frontend #74, backend/infra #75.)** | Shipped               |
| `specs/lp-space-stroker-relaunch.md` | **Frontend-only (LP):** finish the LP launch pass #49 deferred, on the **arcade-overhaul** redesign — full **SpaceStroker** rebrand (wordmark SPACE·STROKER, kill every `$MONKIFY`), **token-free/USDC** messaging, **remove the Tokenomics section** → a short honest **"no token (yet)"** note (token maybe later, no ticker/CA), roadmap perks reframed future/earned (no holder-gating, no "$MONKIFY live"), hero secondary CTA → community "Join the troop", "Play now" → **flagged placeholder** app URL (real route `/game`), trust points kept (provably-fair + instant on-chain payouts + auto-refund); Edson/art/story/skin unchanged. Reuses branch `feature/rebranding-spacestroker` (reconciled overhaul); no backend/DB/test. | Shipped |
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
| `specs/games-history.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** a standalone **Games History** page (`/game/history`, reached from a new header **menu button** that also holds Settings; audio + wallet stay on the header). New read-only `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` → `PaginatedList<GameHistoryDto>` — **one game type at a time** (dropdown), **only `RewardForWinnersCompleted`** rounds, newest first, `pageSize`≤30, a **"mine only"** toggle. Rows: ended date · type name · winning choice · pot (`Σ paid bets` + lottery base + carried extra) · winner count · short `serverSeedHash` · nullable **own-bet** block (choice/stake/won/payout) — **no character stream**. Money rules mirror **game-type-stats** (free bets excluded, single currency per type, `useDisplayCurrency`); "mine" identity resolves like the prefs handlers (`wallet` ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`). **Public page cached 5 min in Redis** per operator+type+page+size; caller columns joined **live**. **Verify = link-out only** to the existing `/verification` payload + `public/verify.html` (no in-app replay). **No entity/migration.** New feature ⇒ mandatory happy-path integration test + handler/service unit tests. | Shipped |
| `specs/main-only-player-entry.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** remove the **DEV Cassino** operator so **only Main** is seeded in all envs; player-only entry at `/game` (retire the dev-launch picker + `VUE_APP_DEV_LAUNCH`); token-gated admin (capture `#admin_token` at the game page, remove `POST /dev/admin-token`, repoint `POST /dev/launch-token`→Main); Main default types `AllowFreeBet = !IsProduction()` with a startup self-heal (`ExecuteUpdate`; the `SpaceStrokerDbContext` is `NoTracking` by default so load-mutate-`SaveChanges` no-ops). Existing DEV Cassino rows purged by hand via `SpaceStroker.DbConsole` (no cleanup code). **No schema/migration.** New feature ⇒ happy-path integration test (`OperatorSeedingTests`) + `DevControllerTests` rewrite. | Shipped |
| `specs/choice-exclusivity.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** in **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is **exclusive** to the first player who bets it in a round; a different player who takes the same normalized sequence is rejected (and **auto-refunded** for a paid `SolanaToken` bet, since the on-chain debit precedes validation — free/Standard are reject-only). **Preset games unchanged** (shared choices). Enforced at two layers: an app-level cross-player check in `RegisterBetHandler.ValidateBetParameters` (routed through the existing `RefundInvalidBet` helper + new `ErrorMessages.ChoiceAlreadyTaken`) **and** a DB unique index on a new NULL-for-preset shadow column `Bet.ExclusiveChoice` (`(GameId, ExclusiveChoice)`; NULLs distinct → preset unaffected), with `GameService.InsertBet` surfacing the 23505 race-loser as `ChoiceAlreadyTaken`. Also fixes the raw-vs-normalized per-player duplicate query. Frontend pre-pay guard in `useGame.bet()` blocks a taken sequence before payment (best-effort; backend authoritative). Winner becomes singular ⇒ winner-takes-all (no reward-math change). New migration `AddBetExclusiveChoice`. New feature ⇒ mandatory happy-path integration test. | Shipped |
| `features/choice-exclusivity.md` | Distilled: in **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is reserved by the **first** bettor per round; a different player taking the same normalized choice is rejected (`ErrorMessages.ChoiceAlreadyTaken`) and **auto-refunded** for a paid `SolanaToken` bet (Standard rejected pre-debit; free log-only). Cross-player check in `RegisterBetHandler.ValidateBetParameters` **and** a DB unique index `(GameId, ExclusiveChoice)` (`Bet.ExclusiveChoice` = normalized choice for free-text, `NULL` for preset ⇒ NULLs distinct, preset games still share); `GameService.InsertBet` maps the 23505 race-loser to `ChoiceAlreadyTakenException`. Same-player repeats still hit `DuplicateBet`; frontend pre-pay guard in `useGame.bet()`; free-text winner becomes singular ⇒ **winner-takes-all** (no reward-math change). | Shipped |
| `specs/fix-pricing.md` | **Minor change (`monkify-backend` + `monkify-front` + `monkify-meta`):** launch pricing fixes for the seeded **Main** game types + the lottery POT display. (A) Per-type `RequiredAmount` in `DefaultGameParameters.GenerateMainDefaultParameters` — **Cosmic Sprint = 1**, **Beast Mode = 1**, **Space Jackpot = 2** (was all `2` via `Base`); Space Jackpot `LotteryPot` **stays 5**; broad dev + crypto catalogues stay `2`. **Seed constants only** — no reconciliation, no SQL patch, **no migration**; applied to existing dev/prod via the user's **teardown + reseed**. (B) Fix `POT: 0 USDC` on Space Jackpot: add base `LotteryPot` to `ActiveGameDto` and seed the frontend `> POT:` from the **game-instance DTO alone** (`isLottery ? lotteryPot + carriedExtra + Σ bets : Σ bets`), removing the fragile `selectedGame.lotteryParameters` cross-fetch dependency so a fresh 5-base lottery reads `POT: 5 USDC`. Display-only; settlement untouched. **No new integration test** (Minor change). **Shipped extras from QA:** the real `POT: 0` cause was a `currentPot = 0` reset in `useGame.updateNewGame` that ran *after* `getBets` (clobbering the seed on every load/rollover) — removed; plus a lottery **FREQUENCY** fix (`formatLotteryFrequency` now parses the TimeSpan day component, so 24h reads "24 HOURS" not "1 HOUR"). | Shipped |
| `specs/solana-connection-rpc.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** keep crypto play alive under mainnet 429s + fix the transfer path. New thin **`POST /rpc`** JSON-RPC passthrough in `SpaceStroker.Api` reusing the key-bearing `TokenSettings.ClusterUrl` (no new secret/provider), gated by **Origin allowlist (`CorsOriginResolver`) + per-IP `AspNetCoreRateLimit` rule + strict 7-method allowlist**; **NOT** launch-token gated (keeps the no-token wallet-bridge popup working). Frontend `useConnection.ts`: **public stays primary**, a **429-only** `withFailover` retries on the proxy and sticks for a **~60s cooldown** (`VUE_APP_SOLANA_PROXY_URL` unset ⇒ no-op); **`confirmTransaction` stays on public** (never proxy a socket). Bundled reliability fixes in `sendToken`: **single-tx idempotent-ATA transfer (1 approval, not 3)** via **sign-once + `sendRawTransaction`**, optional **ComputeBudget priority fee** (`0` disables), **integer/BigInt** amount math. New feature ⇒ mandatory happy-path integration test (`RpcProxyTests` against a fake RPC) + method/Origin unit tests. **No entity/migration.** Trello #87 ([7Fm2RhGB](https://trello.com/c/7Fm2RhGB)). | Shipped |
| `specs/near-miss-dock-bg.md` | **Bug fix, `monkify-front` + `monkify-meta` — no backend:** the bottom-docked near-miss (`> N AWAY!`) overlay in `GameTerminal.vue` is `position: sticky; bottom: 0` inside the scrolling terminal but `background: transparent`, so the auto-scrolling stream bleeds through behind the rows ("letters on top of letters"). Fix = give `.near-miss-dock` an **opaque near-black `background: #050505`** (matches the desktop cabinet screen sampled ~`#040404`; a scoped `.mobile-terminal` override keeps `#0b0d18` for the mobile CRT panel) plus a `box-shadow` covering the form's 24px bottom padding gap + left/right sub-pixel edges; keep the 1px green top separator, no gradient. **Frontend-only CSS; no entity/migration/test** (bug fix, not a new feature). Trello #90 ([h2CVRNby](https://trello.com/c/h2CVRNby)). | Shipped |
| `specs/standardize-scrollbars.md` | **Frontend-only (`monkify-front` + `monkify-lp` + `monkify-meta`), QOL/styling:** replace the raw default OS/browser scrollbars on most scrollable surfaces (reported case: the in-game chat panel) with **one minimal, on-brand scrollbar** applied app-wide as a fallback — thin **8px**, transparent track, subtle neutral thumb `rgba(255,255,255,0.18)`→`0.30` hover, **WebKit** (`*::-webkit-scrollbar*`) **+ Firefox** (`scrollbar-width/color`). Global rule in `monkify-front/src/main.css` and `monkify-lp/src/styles/global.css`; two preserved exceptions in the front — the game terminal keeps its green `.terminal-scroll` (Firefox parity + `border-radius:0` added) and `.side-tabstrip` stays hidden (both win by specificity). No JS/backend/env/token/migration. Not a new feature ⇒ no integration test. Trello #91 ([cGrBi584](https://trello.com/c/cGrBi584)). | Shipped |
| `features/standardize-scrollbars.md` | Distilled: **one global minimal scrollbar** (thin 8px, transparent track, neutral `rgba(255,255,255,0.18)`→`0.30` hover, WebKit + Firefox) applied app-wide as a `*` fallback in both `monkify-front` (`src/main.css`) and `monkify-lp` (`src/styles/global.css`); two preserved front exceptions — the green game terminal `.terminal-scroll` (Firefox parity + square thumb added; WebKit look unchanged) and the hidden `.side-tabstrip` (scoped, unchanged), both winning by specificity. Frontend/CSS-only; no JS/backend/env/token/migration. | Shipped |
| `specs/bet-choice-casing.md` | **Bug fix (Minor change), `monkify-backend` + `monkify-front` + `monkify-meta`:** uppercase bets (e.g. `ASDADA`) were silently lowercased by the backend (so they matched + paid) but the frontend sent/tracked/win-recognized the **raw** choice **case-sensitively** vs the lowercase typed stream → near-miss stuck at "N AWAY!" and no win reveal. Fix: frontend normalizes the choice **once** in `useGame.bet()` (`userWord.trim().toLowerCase()`, propagates to POST body + near-miss + win-recognition) and downcases the maska input on entry; backend stops silently lowering (`Bet.Choice` trim-only, drop `RegisterBetHandler` `.ToLower()`) and now **rejects** uppercase / internal-space / symbol as `InvalidCharacters` (preset games → `InvalidChoice`), keeping whitespace trim. No schema/migration; unit tests updated (uppercase → invalid; case/whitespace refund retargeted lowercase); FE has no test runner so near-miss casing verified by manual QA; not a new feature ⇒ no integration test. Trello #93 ([7yXgbqcQ](https://trello.com/c/7yXgbqcQ)). | Shipped |
| `specs/fair-stats-menu.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** pull **STATS** and **FAIR** out of the `/game` `GameSidePanel` tabs into the **header menu**, leaving the docked cabinet **chat-only** (chrome kept, tab bar dropped; mobile segmented `GAME \| CHAT·STATS` → **`GAME \| CHAT`**). **Stats** becomes a dedicated page (`/game/stats`): a **new GLOBAL section** + the existing per-type stats behind a game-type dropdown. Global = new read-only **`GET /games/stats`** → operator-scoped, **single dominant currency** (USDC on prod Main), on-demand + Redis-cached (120s), **no entity/migration** — mirrors `GameStatsService`; today's headline tiles aggregated across types **+ 6 new tiles** (total volume wagered, unique players, total bets placed, winners paid, biggest win multiplier, most-played type); free bets excluded from money, only `RewardForWinnersCompleted` rounds. **Fair** becomes a full-screen **modal + overlay** in `GameHeader` (like the settings modal): an always-on *how-to-verify* explainer + the existing current-round `GameFairPanel` checks (populate only on `/game/play` after the round ends; off-game → explainer + "watch a round to verify it"). Both **hidden when embedded**. New feature ⇒ mandatory happy-path integration test (`GlobalStatsFlowTests`, dedicated operator for determinism) + service/handler unit tests. Trello #89 ([w9jCXK0N](https://trello.com/c/w9jCXK0N)). | Shipped |
| `features/fair-stats-menu.md` | Distilled: **Stats** + **Fair** move from the `/game` side-panel tabs to the **header menu** (Settings · Stats · Fair · History, hidden when embedded); the docked cabinet is **chat-only** (chrome kept); mobile segmented `GAME \| CHAT`. **Stats page** (`/game/stats`) = a new operator-wide **GLOBAL** section (`GET /games/stats`, single dominant currency, on-demand + Redis 120s, **no entity/migration**; only `RewardForWinnersCompleted` rounds, free bets excluded from money but counted in participation; 10 tiles incl. total volume wagered / unique players / biggest win multiplier / most-played type; non-leaking → zeroed for unknown/expired operators) + the existing per-type stats behind a dropdown, with one page-level house-cut badge. **Fair modal** = always-on how-to-verify explainer + the reused current-round `GameFairPanel` (verifies on `/game/play` after the round ends; off-game → "watch a round to verify it", no fetch). | Shipped |
| `specs/background-page.md` | **Frontend-only (Minor change), `monkify-front`:** a chrome-free `/background` route that renders **only** the animated backdrop — a reusable visual asset to iframe/screen-record. The backdrop is already global (both `GameBackground` layers + `.section-vignette` live in `App.vue`), so the page is an **empty component** and the work is removing what sits in front of it: a declarative `meta: { chromeless: true }` route flag replaces `App.vue`'s hard-coded `route.name !== 'WalletBridge'` header check (`WalletBridge` gets the flag too). Registered in production; outside the operator-scoped `/game/*` tree, so boot loads no operator → no token, no API call, no `/error` dead-end. `TorpedoSpawner` stays but is inert (bet-driven). No backend/DB/test. Implementation note: adding `meta` forced retyping `routes.ts` `RouteRecord[]` → `RouteRecordRaw[]` (the former is vue-router's *normalized* record). Distilled into `architecture.md` (Minor change → no `features/` doc). | Shipped |
| `specs/mobile-wallet-connect.md` | **Frontend-only (`monkify-front` + `monkify-meta`) — no backend:** in-place **mobile** Solana wallet connect for standalone mobile web, so a `SolanaToken` bet can be funded **without** being dumped into the wallet's in-app browser. **Single "Connect Wallet" button → one wallet-picker modal** with two tiers: **Tier 1 native deeplink (stay in browser) = Phantom + Solflare + Backpack** (one encrypted `x25519`/`tweetnacl` adapter; `connect` → approve → return, then **`signAllTransactions` → we self-broadcast**), and **Tier 2 one-tap "open in app" = Coinbase + Trust + OKX + Bitget** (browse link into the wallet's in-app browser where `sendToken` runs unchanged; Glow/Ultimate/Exodus dropped — WalletConnect-only). WalletConnect + MWA rejected. New `useMobileWallet` + `mobileWalletProtocol` util + `MobileWalletModal`; the deeplink **return is a page navigation** (often a new Android tab), so **`main.ts` pre-router param capture + `localStorage` pending-hop + auto-resume on boot** (kills the connect loop), the deeplink `redirect_link` keeping **`?type=`** so the return loads the exact game, **round-changed guard before broadcast**, a **wait-for-`/Bets`-stream** on resume so the placed bet shows without a refresh, and a **blockhash-staleness clean-retry**; the session surfaces as the app's **effective `publicKey`/`connected`** (bet gate + `YOU`/customization identity + **balance** unchanged). **Android connect-method toggle** (in-place deeplink *vs* open-in-wallet-browser) + new-tab disclaimer, and **in-app-browser auto-connect**. Session persists until a deliberate **Disconnect**; all mobile connect entry points share the modal. **No new npm dep** (base58 inlined). **Standalone only** (embedded iframe stays on the popup bridge). No backend/DB change; **frontend feature ⇒ no `SpaceStroker.Tests.Integration`** (device QA). Distilled in `features/mobile-wallet-connect.md`. Trello #92 ([Csb9U6bn](https://trello.com/c/Csb9U6bn)). | Shipped |
| `features/mobile-wallet-connect.md` | Distilled: standalone-mobile Solana wallet connect via a single **Connect Wallet** modal — **Tier 1 deeplink** (Phantom/Solflare/Backpack, `signAllTransactions` + self-broadcast, round-guard-before-broadcast) with an **iOS in-place hop / Android connect-method toggle** (deeplink + new-tab disclaimer, or open-in-wallet-browser) and **in-app-browser auto-connect**; **Tier 2 open-in-app** (Coinbase/Trust/OKX/Bitget). `localStorage` return stash + `?type=` return kill the Android new-tab connect loop; resume waits for the live `/Bets` stream; balance/identity read the effective wallet. No backend change. | Shipped |
| `specs/idle-websocket-disconnect.md` | **Frontend-only (Minor change), `monkify-front` + `monkify-meta` — no backend:** stop paying for websockets nobody is watching. The two game-screen SignalR hubs (`Games` + `RecentBets`) are **proactively stopped** on idle and refuse to reconnect until the player clicks. **Two independent timers, whichever fires first:** `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS` (default 30 min continuously `document.hidden`) and `VUE_APP_IDLE_DISCONNECT_INACTIVE_MS` (default 60 min zero input, visible or not — the safety net for a visible-but-abandoned desktop tab). A **visible + interacting** tab is never cut. New `useIdleDisconnect` composable (module singleton; elapsed computed from **timestamps**, never a `setTimeout` callback, because background timers freeze) + `GameIdleOverlay.vue` (`> DISCONNECTED — INACTIVE` + **RECONNECT**); resume re-runs the **full** join path via `updateNewGame(true)` (round rolled over ⇒ new `gameId`; chat empty; own-bet near-miss rows re-seeded). `initalizeWebsocketConnections` gated on the flag; **`onreconnecting`/`onclose` idle checks on both hubs** kill the `.withAutomaticReconnect()` revival of a frozen tab's dropped socket (without them the feature is a no-op on mobile); `bet()` blocked while disconnected. Embedded: same rules + a new `idle-disconnected` postMessage (no resume event). **In-flight mobile wallet hop exempt** from the hidden timer. No entity/migration/backend/unit test; FE has no test runner ⇒ manual QA (**§13 matrix not yet run at ship time**). ⚠️ **Partly superseded by `specs/unified-websocket-connection.md` (#120):** there is now **one** connection, not two, the watch is owned by `useGameHub` (armed on connect, stopped with the socket) rather than by `GamePage`'s mount/unmount, and the `initalizeWebsocketConnections` idle gate was removed — thresholds, detection and explicit resume are unchanged. Trello #96 ([fwdi7Fgh](https://trello.com/c/fwdi7Fgh)). | Shipped |
| `specs/frontend-helius-gateway.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** get the SPA off the public Solana mainnet RPC, which is unusable from a production browser (so today the *whole* paid `SolanaToken` path is broken on mainnet, not just the header balance). Wallet-info reads move to a new **`GET /solana/balance`** (+ uncached **`/solana/balance/live`**) on `SpaceStroker.Api` — Redis-cached ~10s per `mint+wallet`, restricted to **active registered `OperatorToken` mints**, gated exactly like `/rpc` (server-side Origin allow-list + per-IP rate rule, **no** launch token, so the wallet-bridge popup keeps working) and served through a new **`ISolanaService.GetTokenBalance`** so it inherits #62's retry + one-shot public fallback + masked logging **and** is coverable by the `FakeSolanaService` harness. The **transaction path stays JSON-RPC over the existing `/rpc` proxy but flips from 429-failover to PRIMARY** (`VUE_APP_SOLANA_PROXY_URL` was never even emitted by the deploy tool, so the #87 proxy has been inert in production); `confirmTransaction`'s **WebSocket is replaced by a bounded `getSignatureStatuses` poll** (a socket can't be proxied), and the method allow-list is **narrowed 7 → 3**. Three RPC round-trips are **deleted**: mint decimals (already on `ActiveGame.tokenDecimals`), the dead SOL `getBalance`, and the token-account lookup (ATA derived locally, 2 calls → 1). Balance poll slowed (env-tunable 30s) and gated on `useIdleDisconnect`. Local/devnet keep hitting public directly ⇒ no key, no credits. **No entity/migration/Terraform change.** New feature ⇒ mandatory happy-path integration test (`SolanaBalanceTests`). Trello #99 ([8JhYpx52](https://trello.com/c/8JhYpx52)). | Shipped |
| `features/frontend-helius-gateway.md` | Distilled: the SPA no longer calls a public Solana RPC. Balances come from **`GET /solana/balance`** (+ uncached `/solana/balance/live` — a separate **route**, since the rate limiter can't budget a query flag), Redis-cached per `mint+wallet`, restricted to **active registered `OperatorToken` mints**, one vague message for every rejection, no-ATA ⇒ `0` (never retried), served via `ISolanaService.GetTokenBalance` (local ATA derivation ⇒ **1** RPC call, inheriting #62's retry + one-shot fallback + masking). The **`/rpc` proxy became the primary transport** (not a 429 fallback; `withFailover`/cooldown deleted) and `confirmTransaction`'s **WebSocket became a `getSignatureStatuses` poll** — a socket can't be proxied; allow-list **7 → 3**. Deleted: the per-bet decimals lookup (now `ActiveGame.tokenDecimals`, threaded explicitly; missing ⇒ **abort the bet**, never guess an exponent), the dead SOL balance read, `sendSol`. Poll 10s → env-tunable 30s and **stops with `useIdleDisconnect`**. Local/devnet keep hitting public directly ⇒ no key, no credits. Ships a **latent money-bug fix**: seed-time caching of un-hydrated game types served a null token block for 12h, so paid bets could target the `VUE_APP_*` env defaults instead of the registered token/receiver. | Shipped |
| `specs/promotion-channels.md` | **Content/research (Minor change), `monkify-meta` only — no code:** the deliverable `marketing/promotion-channels.md`, a **live-verified, prioritized directory of named promotion targets** (the crypto / gambling / web3-gaming / meme communities, servers, subreddits, creators and X Spaces hosts to actually show up in) — the research that the **X (#76)** and **Telegram (#77)** campaigns both deferred in their "where the audience is" sections. **Hard rule: every entry opened live at write time, nothing from model memory**; unverifiable ⇒ dropped or flagged `⚠ unverified`; per-entry `as of` date + a §6 re-verification protocol so the list decays honestly. Per entry: link · size · activity signal · fit tags · **self-promo rule** (`allowed`/`ask an admin`/`banned`/`unknown — ask first`) · tier · one-line "the move here" · impersonation note. **T1** ~10–15 start-here · **T2** ~20–40 · **T3** assessed-and-rejected **with the reason**; per-platform minimums (≥8 TG, ≥8 Discord, ≥6 X, ≥5 subreddits, ≥8 streamers/Spaces) with any shortfall stated, never padded. Every platform the card names gets entries **or** an explicit "assessed — not worth it" verdict (incl. Facebook). $0 organic, with openly-published paid rates fenced into a "not endorsed" appendix. Positioning = **web3 dapp on Solana, not a licensed gambling operator**; **no platform-ToS/compliance research** (user decision, risk accepted). Also repoints #76 §4.3 + #77 §4.4 at the new doc. No integration test / `features/` doc (content-only). **Delivered 2026-07-26: 55 verified entries + 26 assessed-and-rejected; Telegram (6/8) and X Spaces (0) stated as shortfalls rather than padded.** Trello #100 ([L3qmI4qj](https://trello.com/c/L3qmI4qj)). | Shipped |
| `specs/analytics.md` | **Frontend-only (`monkify-front` + `monkify-lp` + `monkify-meta`) — no backend:** install **GA4** across both properties so we can answer "how many came, from where, and did they play?". **One property / one data stream / one measurement id** for `www.spacestroker.com` + `app.spacestroker.com` — same registrable domain, so the `_ga` cookie is shared and the LP→app hop stays **one session** (a bet is attributed to `utm_source=x`, not to `referral/www`); raw `gtag.js`, **not** GTM. The id rides the existing **`frontBuild`** block (`VUE_APP_GA_MEASUREMENT_ID` + `PUBLIC_GA_MEASUREMENT_ID`) which `Deployer.BuildStaticSite` already writes into **both** repos ⇒ **no deploy-tool code change**; **empty id ⇒ no script, no cookie, no banner** (local/docker/dev-GCP never report). **Consent Mode v2 default `denied`** + an equally-weighted Accept/Reject banner on both properties, choice in an **`ss_consent` cookie scoped to `.spacestroker.com`** so it carries across the hop, plus a new LP **`/cookies`** notice page (the first link the LP footer has ever had). Events: LP `cta_play_click` via a **delegated document listener** (the hero buttons are un-hydrated Astro islands — a Vue `@click` would silently never fire), app `wallet_connect_attempt/success/failure`, `bet_placed` (**count only** — instrumented at `useGame.submitBet`, the single choke point every bet path funnels through), `game_type_selected`, manual SPA pageviews. **No amounts, wallet addresses or player identifiers ever leave the browser.** Fully **off when embedded**, on the `chromeless` routes and on the static `verify.html`/`embed-example.html`. `@vercel/analytics` removed (inert since the LP moved to GCP). Level **Feature**, but the integration test is **waived** (zero backend code) — `features/analytics.md` still required at ship. Trello #101 ([Hbfp779A](https://trello.com/c/Hbfp779A)). | Shipped |
| `features/analytics.md` | Distilled: **one GA4 property** across `www.` + `app.spacestroker.com` (same registrable domain ⇒ shared `_ga` cookie ⇒ the LP→app hop is **one session**, so a bet keeps its original campaign). Tag loads only when a build-time id is present (`VUE_APP_GA_MEASUREMENT_ID` / `PUBLIC_GA_MEASUREMENT_ID`, both from the existing `frontBuild` block — **no deploy-tool change**); **empty ⇒ no script, no cookie, no banner** (local/docker/dev never report). Consent Mode v2 default `denied` + equal-weight Accept/Reject banner; the answer rides an **`ss_consent` cookie on `.spacestroker.com`** so both hosts honour one choice (localStorage cannot cross the two origins). Events: `cta_play_click` (4 LP CTAs, via a **delegated listener** — the hero buttons are un-hydrated Astro islands where a Vue `@click` never fires), `wallet_connect_attempt/success/failure` (+`connect_path`), `game_type_selected`, `bet_placed` (**count only**, at `useGame.submitBet` — the single choke point all bet paths share), manual per-route `page_view`. **No amounts, wallet addresses or player identity ever sent**; outcome not tracked. **Off entirely when embedded**, on `chromeless` routes and on the static `verify.html`/`embed-example.html`. `@vercel/analytics` removed (inert since the LP moved to GCP). New LP `/cookies` notice + footer link; `marketing/utm-conventions.md`. Frontend-only ⇒ **no backend surface**, integration test waived. | Shipped |
| `specs/teardown-resume.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no backend/front:** make `spacestroker-deploy teardown` **resumable**. Today `Deployer.DestroyWorkload()` runs a **full `terraform apply`** before `destroy` just to clear Cloud SQL `deletion_protection`, so re-running a teardown that failed part-way **re-creates everything the failed pass destroyed** (observed 2026-07-26: rebuilt Redis + Artifact Registry, then died on the image the same pass had deleted, never reaching `destroy`). Fix, all in the C# tool: (1) a **targeted** apply on `module.data.google_sql_database_instance.postgres` only — a **state-only** update, since that is the *Terraform-level* guard, not `settings.deletion_protection_enabled` — **skipped entirely** when the instance is no longer in state (`terraform state list` probe, **fail-closed** on a non-zero probe); (2) a **bounded blind retry** over `destroy` (**3 passes, 60s/120s**, any non-zero exit — the 3 known ordering failures each clear once the blocking resource is really gone, and `ProcessRunner` doesn't redirect stderr); (3) **leftover classification decides the exit code** — empty ⇒ 0, only `module.network.*` (VPC/subnet/PSA, all zero-cost, held by the async Google-managed `serverless-ipv4-*` reservation) ⇒ **0 + loud warning**, anything billable ⇒ **1** with the addresses listed; (4) a **`--purge` stop-guard** that refuses to destroy the state bucket while any workload state remains (it would orphan those resources untracked). Decisions extracted into a pure `TeardownDecisions` class covered by a **new `SpaceStroker.Deploy.Tests`** xUnit project (16 tests — the only automated tests in the deploy stack). **No Terraform change**; `deploy`-side "push images before apply" stays a separate card. Trello #98 ([7a9aYZiG](https://trello.com/c/7a9aYZiG)). | Shipped |
| `specs/static-site-status-codes.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no app code:** both static sites served the right HTML with the **wrong HTTP status**. One hardcoded line (`not_found_page = "index.html"` in the shared `static_site` module) made **every** `app.spacestroker.com` deep link return **404** while rendering the SPA, and made `spacestroker.com/<unknown>` return the **LP homepage** with a 404 instead of the branded `404.html` that **already builds and already deploys** (`monkify-lp/src/pages/404.astro` — live at `/404.html`, just unreachable). Cosmetic to a human, broken for everything that reads the status: uptime monitors on deep links, Discord/X link unfurls, crawlers, and a missing JS chunk that returns `text/html` instead of a clean 404. Fix is **two different mechanisms on purpose**: the SPA fallback moves to the **load balancer** (a GCS website config can never return 200) as a `custom_error_response_policy` on a **`/*` path rule** rewriting 404 → `/index.html` + `override_response_code = 200`, while `/js/* /css/* /img/* /media/*` get a **more specific, policy-free** path rule so real asset misses still 404 — relying on documented **longest-path-first** matching. ⚠️ The refined card's carve-out design was **wrong** and is corrected in the spec: an omitted `path_rule` policy **inherits** `pathMatcher.defaultCustomErrorResponsePolicy`, so the policy must live on the `/*` rule with matcher + url_map defaults left empty. The app bucket drops `not_found_page` entirely (keeping `main_page_suffix`); the lp bucket points it at `404.html` and **keeps** the 404 status (one-pager, no client routes to rescue). `www.` inherits via the shared `lp` path matcher. **Config only — never applied or `terraform validate`-ed** (no terraform in the authoring environment); takes effect at the next `apply` + CDN invalidation. Cache-Control is a **companion card** ([bSSvCfKF](https://trello.com/c/bSSvCfKF)). Trello #102 ([mkogi4tR](https://trello.com/c/mkogi4tR)). | Shipped (not yet applied) |
| `specs/static-site-cache-control.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no app code, no Terraform:** both static-site buckets serve **every** object with the GCS default `public, max-age=3600`, because `Deployer.UploadAndInvalidate` uploads with a bare `gsutil -m rsync -r -d` and sets no metadata. That caches `index.html` (which must never be cached) for an hour and immutable hashed chunks for only an hour — and since `-d` **deletes** the previous build's chunks, a returning player boots a stale HTML naming files that no longer exist. Fix, all in the C# tool: a new pure **`StaticSiteCache`** class turns the built `dist` listing into an **ordered plan of gsutil invocations** — (1) per-hashed-prefix rsync **without `-d`** at `max-age=31536000, immutable` (front `js/css/img/media`, lp `_astro`), so the previous build's chunks are **retained** for a session that lazily fetches one (`GameFairPanel.vue:228`'s verify Web Worker is the live case); (2) a root rsync **with `-d`** at `no-cache`, its single `-x` alternation shielding the asset prefixes **and** dropping `*.map` (6 public sourcemaps stop being uploaded); (3) **`setmeta` convergence passes** per tier — required because `rsync` compares checksums, **not metadata**, so ~19 MB of already-uploaded `img/`+`media/` would otherwise stay at 1h forever. Assets upload **before** the HTML that names them, and every upload-time header is **at least as conservative** as its final value, so an intermediate window can only under-cache. Tier rule tests `.html` at **any depth** (the lp's `/cookies` page builds to `cookies/index.html`); an unknown file or directory falls to the explicit 1h tier. Fonts stay 1h (**unhashed** ⇒ immutable would strand a swap for a year). Retained assets are **pruned automatically after 24h** via per-deploy build manifests (`gs://<bucket>/_builds/<utc-timestamp>.txt`, shielded from the root `-d`): a **GCS lifecycle rule cannot express this** because what must be measured is **retirement**, not object age — an image unchanged for months would be reaped the instant it left a build. The window derives from the **open-tab** horizon (`useIdleDisconnect` idles out at 30-60 min), not from `Cache-Control`; the prune **fails closed** at every read. The `/*` CDN invalidation is kept verbatim. `StaticSiteCacheTests` + `BuildManifestTests` in the existing `SpaceStroker.Deploy.Tests` (76 total). Trello #103 ([bSSvCfKF](https://trello.com/c/bSSvCfKF)). | Shipped (not yet applied) |
| `specs/robots-txt.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-lp` + `monkify-meta`:** give the three public production hosts a deliberate crawler policy. Today only the LP has a `robots.txt`; **`app.spacestroker.com` answers `/robots.txt` with the SPA's HTML under a 200** (the #102 `/*` error policy rewrites the bucket 404 → `index.html` + 200, and RFC 9309 skips unparseable lines ⇒ **effectively allow-all**), and `api.spacestroker.com` **404s** — also read as allow-all. Fix: **app** gets `public/robots.txt` = `Disallow: /` **plus `X-Robots-Tag: noindex`** on the front backend bucket (`Disallow` alone never de-indexes, and blocking the crawl stops Google ever reading a `noindex` meta) — one line on `module "front_site"`, `lp_site` deliberately untouched; **API** gets `GET /robots.txt` on the existing `HomeController` (`[Route("/")]`, outside `ProtectedPrefixes` ⇒ no launch token, no Terraform, works locally) — ⚠️ needs its own **`[Produces("text/plain")]`** to override the class-level `[Produces("application/json")]`; **LP** keeps `Allow: /` + its sitemap but blocks **AI training** crawlers (`GPTBot`, `ClaudeBot`, `Google-Extended`, `meta-externalagent`, `CCBot`, `Bytespider`) while leaving **AI search/answer** crawlers (`OAI-SearchBot`, `PerplexityBot`, `Claude-SearchBot`, …) and **all link-preview agents** (`facebookexternalhit`, `Twitterbot`, `TelegramBot`) allowed — every token verified live against vendor docs 2026-07-27; `anthropic-ai` **dropped** (undocumented), `Applebot-Extended` **deferred** (Apple's page unretrievable). No `Disallow: /404` (it would hide the LP 404's own `noindex`). **No entity/migration/SPA/LP-content change.** Dev environment **out of scope** (it keeps serving prod's `Allow: /`). `terraform apply` is an ops step. Also bundled at ship time (out of spec, by explicit decision — spec §10.5): the **flaky-integration-test fix** — `GlobalStatsFlowTests` + `GamesHistoryFlowTests` seeded **three** winning-eligible preset choices while paying into only **two**, so ~1 round in 3 legitimately produced no *paid* winner ⇒ `WinnersPaid`/`WinnerCount` = 0. A control run on clean `origin/main` reproduced it, proving it pre-dated the card; **not** a timing race. Fixed by dropping the unbacked `donkey` choice from both seeds. Trello #105 ([6aOoMq2n](https://trello.com/c/6aOoMq2n)). | Shipped (not yet applied) |
| `specs/player-lockouts.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** players are intermittently locked out of `app.spacestroker.com` by a Cloud Armor `429` returned **before the app runs** (Armor denials carry no CORS headers, so the browser reports a CORS error and the app "looks broken"). Root cause confirmed from prod LB logs: **143 API requests in ~3 min** against a 100/60 s limit, from **17 document loads across ~5 tab contexts** (three tabs opened 12 ms apart), with `/players/preferences` at ~2.3/load and `/games/active-types` at ~2.0/load — bursts as tight as **seven identical GETs in 14 ms**. Two compounding causes, two fixes. **(1) Frontend:** a module-local **`singleFlight(key, fn)`** in `mainService.ts` — while a request for a key is in flight every later caller receives that same promise; the entry is deleted in `finally`, so it is **not a cache** (nothing survives across time ⇒ no stale value can reach a bet, payout or refund). Applied via an **allow-list of five** boot-path functions (`getGamesTypes`, `getPlayerPreferences`, `getFxRates`, `getOperator`, `getDefaultLaunchToken`); the other eight are deliberately untouched — a blanket GET dedupe was **rejected** because it would collapse `getTokenBalance(live:true)`, which exists precisely for the moments that must not be stale. Keys include params so `getPlayerPreferences(wallet)` and `getPlayerPreferences()` stay distinct (**different players**). The measured burst is `App.vue:204-213` + `useGameChat.ts:43-51` firing on the **same wallet-connect tick** (`main.ts:107` sets `autoConnect: true`). **One file changes.** **(2) Terraform:** the priority-1000 Armor rule changes `action` **`rate_based_ban` → `throttle`**, threshold **100 → 600**/60 s, and `ban_duration_sec = 300` is **deleted** — the incident's damage came from the *ban*, not the threshold (static assets kept returning 200 so the shell rendered, every API call 429'd, and each reload **renewed** the ban). ⚠️ `throttle` is **unverified** — `terraform plan` gates the merge. **Single-flight is per-tab and cannot dedupe across tabs**, which is why the Armor change is not optional. Split out: **#107** retry affordance, **#108** IP+JA4 keying. **Measured after shipping: 4 → 3 preferences calls on a SolanaToken game page with a wallet; the lobby never duplicated (1:1). The spec's "~143 → ~85-95" estimate is WITHDRAWN — the real saving is ~1 request per game-page load (~10%), because the "every boot doubles its calls" premise was false. The Armor `throttle` change is the load-bearing half and is NOT yet applied.** Trello #104 ([SpTvJGi9](https://trello.com/c/SpTvJGi9)). | Shipped (Terraform not yet applied) |
| `specs/fair-modal-off-screen.md` | **Bug fix, `monkify-front` + `monkify-meta` — no backend:** on a GPU desktop the **FAIR** modal opens with half the panel above the viewport and its dim overlay covering only the header strip, because `GameHeader.vue`'s root carries `backdrop-blur-md` — and a non-`none` `backdrop-filter` makes an element the **containing block for `position: fixed` descendants**, so `inset-0` resolves against the 64px header box. Invisible on phones (and on hardware-acceleration-off desktops) because `arcade.css:269` strips `backdrop-filter` under `html.light-graphics`, which `usePerfMode` forces below `lg`. Fix = **two different mechanisms on purpose**: `GameFairModal` wraps itself in **`<Teleport to="#app">`** (the pattern `PlayerCustomizationModal.vue:140` already uses), while the two `fixed inset-0` **click-away backdrops** (`GameHeader.vue:221`, `WalletButtons.vue:80`) are **deleted** in favour of a new **`useClickOutside`** composable — ⚠️ correcting the refined card, they **cannot** be teleported: App.vue's root is `relative z-10`, its own stacking context, so a teleported sibling either paints above the whole app (swallowing clicks on the dropdown) or below it (never clicked). The wallet popover is a **latent** case (mobile-UA branch; only reachable with the blur at ≥1024px on a mobile UA) fixed defensively. No Esc/scroll-lock/focus-trap, no header restyle. Bug fix ⇒ **no unit/integration test**, no `features/` doc; QA covers normal + light graphics, mobile and embedded. Convention recorded in `conventions/frontend.md`. Trello #109 ([ye9CYjpo](https://trello.com/c/ye9CYjpo)). | Shipped |
| `specs/mobile-preset-keypad.md` | **Minor change / QOL, `monkify-front` + `monkify-meta` — no backend:** on a phone, a **preset-choice** game (Main's `6X Main Derby` / Beast Mode) renders its picker as 8 hard-coded `button<N>.png` images in a `grid-cols-4 gap-1.5`. Each PNG is **70×91 with an opaque light-grey plate baked in** — art drawn to blend into the *desktop cabinet* (`terminal.png`) — so on the mobile panel's `from-[#20293e] to-[#161c2c]` gradient they read as **eight floating grey squares**; `button1` is 91px tall and `button3` 90px, so the rows don't align. Replaced on the **mobile path only** by named keys built from the `arcade.css` material system: 2-column grid, each key = 1-based index + the choice **NAME**, real `<button :disabled>` elements reusing the `GameMobileTabs` active idiom (`bg-primary text-[#241400]` + amber glow) for the selected state. Fixes three defects in the same 10 lines: **nothing showed what you picked** (the mobile `<input>` renders only for free-text games and `hover:brightness-125` never fires on touch), the **hard 8-key cap** (`[...Array(8).keys()]` — the backend sets **no** maximum on `PresetChoices`, so a 12-choice game had 4 unreachable choices), and **silent dead taps** (`placeBet()` calls `leverError()`, which plays a sound at a lever that doesn't exist on mobile). Also: keys dim + go inert when `allowBets` is false, PLACE BET disabled until a choice is selected, copy `> PICK YOUR NUMBER` → `> PICK YOUR CHOICE` (operators can create preset games with arbitrary words). Selection stays **derived from `userWord`** — a local ref would go stale when `placeBet()` and `GamePage.vue:61-63` clear it. **Desktop is untouched** (cabinet art, lever, 8-PNG keypad, and the assets all stay); its own 8-key cap is a deliberate known gap needing its own card. Mobile is **always** `html.light-graphics` (`usePerfMode` short-circuits `< lg`), so the keys must carry no `backdrop-filter`/`blur()`/infinite animation. No backend, DB, SignalR or API change ⇒ **no unit or integration test**; `monkify-front` has no test framework, so done = `yarn lint` + `yarn build` + manual phone QA. Trello #94 ([uJdWRk7g](https://trello.com/c/uJdWRk7g)). **Shipped deviation:** keys use plain Tailwind, **not** `.btn-arcade` — `main.css` imports `arcade.css` *after* `@tailwind utilities`, so that class's `justify-content`/padding beat equal-specificity utilities (now recorded in `conventions/frontend.md`); consequently no `arcade.css` change and no extracted component were needed. Distilled into `features/mobile-layout.md`. | Shipped |
| `specs/entry-path-recovery.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** a transient API failure during boot strands a standalone player on `/error`, a page whose whole body is `<p>This game is unavailable.</p>` — no retry, no reload, no link — and because Cloud Armor's `throttle` enforces across the **whole 60 s interval**, a manual reload inside a saturated interval re-fails *and* spends another request. Fix = **classify + recover in place**, not just a retry button: a pure `isTransient(error)` + a pure jittered delay schedule (**3 retries at ~5 s / ~20 s / ~45 s, ±20 %, ≈70 s total — deliberately longer than the 60 s enforcement window** so one attempt lands in a fresh interval; jitter stops everyone behind one VPN exit retrying in lockstep) driven by a `useBootRecovery` module-singleton that shows a one-line `> RECONNECTING…` state (no countdown/attempt counter — that invites the reload we're preventing). ⚠️ **The load-bearing finding: the 429 does NOT arrive as a status.** Armor denies at the edge before the app runs, so the response carries **no CORS headers**, the browser blocks it, and axios sees `error.response === undefined` — which is why the incident was reported as "a CORS error". A `status === 429 \|\| status >= 500` classifier would **never fire on the incident**; transient = **no response at all**, or 429/5xx/408. All **three** routes to `/error` hardened (`useOperator` bare `catch` — the one that fired; `main.ts` boot-mint guard; the 401 interceptor, whose `getDefaultLaunchToken` flattened every error to `null` and whose `window.location.assign` full reload would wipe an in-flight back-off ⇒ router navigation via a new `navigation.ts` shim, since the router is never exported). `getGamesTypes` joins the back-off (wrapped **outside** `singleFlight`); prefs/FX deliberately don't. Embedded gets the same classification with **no new postMessage** (`token-expired` is the wrong signal for a 429 — it makes the casino mint a token that cannot help). `/error` always shows RETRY. Plus a **new priority-800 Cloud Armor rule** giving `/health` + `/Hubs/*` a looser ~3000/60 s bucket (a *throttle*, not a blanket `allow` — an exemption would leave `/Hubs/*/negotiate` with no ceiling while #112 then had the in-app limiter under suspicion — since **confirmed and fixed**), the `patchRule` action-change comment, and the `deploy/README.md:117` `production.auto.tfvars.json` → `production.tfvars.json` correction. No entity/migration/backend; `monkify-front` has no test runner and none is added ⇒ the two decision-bearing functions must be **pure and exported** so a future runner can pin them. `terraform apply` is an ops step. Trello #110 ([KvfUl6ff](https://trello.com/c/KvfUl6ff)). | Shipped |
| `specs/rate-limiter-keying.md` | **Minor change, `monkify-backend` + `monkify-meta` — no frontend:** the in-app AspNetCoreRateLimit limiter keys **every rule on the load balancer**, so all rules are **one global bucket shared by every player on earth** — live in production. `Program.cs:135-141` sets up `UseForwardedHeaders` but never sets **`ForwardLimit`**, and ASP.NET's default of `1` walks one hop from the **right** of `X-Forwarded-For`, which is the proxy that *added* it. Measured in prod 2026-07-27 (probe deployed + reverted in `28a5c9c`): `raw=[<client>,34.120.43.169] hops=2 resolved=34.120.43.169 realIp=[]` on every path. Worst case live: `*:/solana/balance/live` at **20/min globally**, called once per player per round on bet placement + win settlement. Three fixes: **(1)** `ForwardLimit = 2` as a hardcoded constant (a knob is a foot-gun — `null` or a large value trusts the attacker-controlled **leftmost** entry; `RealIpHeader: "X-Forwarded-For"` is **rejected** for the same reason). **(2)** Move `app.UseIpRateLimiting()` from `:143` to **after** `app.UseCors()` at `:151` — the limiter short-circuits a 429 with no `Access-Control-Allow-Origin`, so the browser reports a **CORS error**, indistinguishable from an outage. **(3)** **Delete** the `api/*` rule, which has never matched a request (`api` parses as the *hostname* in `{verb}:{path}`) — deletion, **not** repair: a repaired catch-all shipped while `ForwardLimit` was still `1` would have made a global 100/min bucket on the boot path and locked out everyone at once, which is why the card's "ForwardLimit first" ordering constraint **dissolves** here. Also `*:/rpc` **120 → 240**/min, because `useConnection.ts:87-112` polls `getSignatureStatuses` **1/s for up to 45 s** as `POST /rpc` (one path ⇒ one counter), so one slow-confirming bet costs ~47 — and a throttled `/rpc` mid-confirm strands tokens that **already left the player's wallet**; the protection given up is ~nil since `/rpc` allows an **absent** `Origin` by design and the `run.app` URL bypasses the LB (own card). Adds `EndpointWhitelist: ["*:/health","*:/Hubs/*"]` (wildcard form **verified** to match, via a catch-all host + a control test). Bet path stays **unlimited** app-side, pinned by an **exact-set** assertion on the rule list. **No entity/migration/frontend change.** **Shipped extra — a SECOND pre-existing bypass:** AspNetCoreRateLimit 4.0.2 defaults **`RealIpHeader` to `"X-Real-IP"`** and prefers it over the connection address; the ALB forwards whatever the client sends, so a caller supplying that header got a **fresh bucket per request** and walked through every rule over the ordinary public path — pinned to `""`, without which `ForwardLimit = 2` would have been trivially defeatable. Also **measured**: the specific `*:/solana/balance/live` rule (20) wins over the broader `*:/solana/*` (120). ⚠️ **Accepted: Helius spend will rise** — today's global buckets accidentally throttle legitimate traffic, and there is no fix that doesn't release it. Trello #112 ([h27WZeQC](https://trello.com/c/h27WZeQC)). | Shipped |
| `specs/auto-bet-seeder.md` | **Minor change, `monkify-meta` only — no backend/front:** a local **`SpaceStroker.Seeder`** console app (new project in `deploy/tools/`, next to `SpaceStroker.Deploy`/`SpaceStroker.DbConsole`) that seeds **one real paid bet** into **Cosmic Sprint** and **Beast Mode** on **production** whenever their current round is empty — a round with zero bets reads as a dead game, and nobody wants to be first in when `MinimumNumberOfPlayers` is 2. Runs a pass on start then every **6h**; **self-limiting** because its own prior bet makes the round non-empty, so at most one seeded bet is outstanding per game and no state file is needed. A **pure client of shipped endpoints**: `POST /launch-token/default` (anonymous Main token — **no admin token, no operator signing key**; TTL is 3600 s so it is re-minted every pass), `GET /games/active-types` (resolves both types **by name**; `GameParametersDto` already carries mint/decimals/receiver/stake/preset choices as public data), `GET /games/{gameTypeId}` (the emptiness check), `GET /solana/balance` (pre-flight), `POST /rpc` for build→send→confirm (**the 3-method allow-list is not widened**) and `POST /games/{gameId}/bets`. Money safety is the spine: a **fail-fast startup guard** rejects a seeder wallet equal to `tokenReceiverPublicKey` (`ValidateBetPayment` matches by **owner**, so a self-transfer nets 0 and the bet is rejected as underpaid), retry is **phase-aware** (free before the transfer; once a signature exists it is **never re-sent** — poll `getSignatureStatuses`, then POST with that same signature, which at worst hits `PaymentSignatureHasBeenUsed`), and a confirmed stake is always spent into whichever round is accepting, or logged as an **ORPHAN PAYMENT**. ⚠️ Solnet's `sendTransaction` must pass `preflightCommitment: Confirmed` to match a `Confirmed` blockhash, and `/rpc` rejects JSON-RPC **batch** arrays. `MinimumNumberOfPlayers = 2` means a seeded bet with no human joiner **stays committed indefinitely** (races never auto-refund) — recovery is the manual `POST /admin/bets/refund`. Space Jackpot deliberately excluded. **No entity/migration/backend/frontend change**; Minor change ⇒ **no integration test, no `features/` doc**, but a new `SpaceStroker.Seeder.Tests` unit project. ⚠️ **Shipped without a live run** — the on-chain half is correct-by-construction but behaviourally unverified (no transfer sent, no bet placed); the Solnet signatures, keypair round-trip, 94 unit tests and both fail-fast config paths **were** verified by execution. Trello #113 ([AhRfdxga](https://trello.com/c/AhRfdxga)). | Shipped |
| `specs/gcp-dashboards.md` | **Minor change, `monkify-meta` + `monkify-backend` — no frontend:** production observability as IaC. A tenth workload module `modules/monitoring` (gated on a new `enable_dashboards`, **derived by the deploy tool** from `environment == "production"`, module-level `count` so development state holds no `module.monitoring` address at all) creates **one** dashboard `spacestroker-prod-overview`, two uptime checks (`api…/health`, `app…`, `period = 300s` so they stay a rounding error against the priority-800 Armor bucket), an email notification channel, a `google_billing_budget` (50/90/100%), and **two** `google_logging_metric` over the LB logs — one on `enforcedSecurityPolicy`, one on **`previewSecurityPolicy`** (a `preview = true` rule populates the *other* field, so aiming at the enforced one yields a permanently-zero panel reading as "the WAF never false-positives", when the priority-900 OWASP rule is in preview precisely because it does, on Solana base58 bet payloads). Both metrics label on rule priority + action, bounded cardinality by design. ⚠️ **The card's headline ask is not buildable as asked, three times over.** (1) **Cloud Monitoring cannot chart billing cost at all** — no cost data source, no iframe/embed widget — so "expenses, biggest cost desc" becomes a markdown link to Billing → Reports (already exactly that view) plus the budget, which supplies what Reports cannot: being *told*. (2) **No built-in metric carries a request path** (`run.googleapis.com/request_count` has `service_name`/`revision_name`/`response_code` only) and the `api` path matcher has just a `default_service`, so `matched_url_path_rule` is one constant ⇒ per-endpoint **descoped** to totals + response-code class. (3) **"How many users were rate limited" is undeliverable** — log-based metrics cannot do distinct counts, and per #111 one IP is not one user (a VPN exit is one Armor bucket for everyone behind it) ⇒ denied *requests* + a documented Log Analytics query. **Backend half is load-bearing, not incidental:** `DependencyInjection.cs:126-147` writes Serilog's plain-text template to stdout, so Cloud Run files every line at severity **`DEFAULT`** and an `Error` is indistinguishable from a `Warning` — "count the errors" has nothing to count. A new `SpaceStroker.Common/Logging/GoogleCloudJsonFormatter.cs` (`ITextFormatter`, **no new NuGet** — Common already has Serilog 4.0.0, Infrastructure has Sinks.Console 5.0.1) emits JSON with a top-level `severity` **in Production only**, with the exception appended into `message` so a stack trace is **one** entry rather than N (which also lets Error Reporting see it); `CompactJsonFormatter` is explicitly rejected (`@l` is not read by Cloud Run). That unlocks the built-in `logging.googleapis.com/log_entry_count` (labelled by `severity`) ⇒ **no log-based metric for errors/warnings at all**. Integration suite provably unaffected — `IntegrationTestFixture.cs:136-143` replaces `Log.Logger` after the hosts build. Also: dashboard JSON extracted to `dashboard.json.tftpl` via `templatefile()` so the ~600-line blob stays diff-reviewable; budget + channel **skip on empty inputs** rather than failing the apply; five new ids must join `EnvNaming.DerivedIds()` or the `validate` AC passes vacuously; two `gcloud services enable` targets (`monitoring`, `billingbudgets`) are **missing from `FIRST-DEV-DEPLOY.*`** and the budget needs a **billing-account-level** IAM grant no project role can give. **No entity/migration/frontend change**; Minor change ⇒ no integration test, no `features/` doc. ⚠️ Cloud SQL/Redis `metric.type` strings are **unvalidated** here (no GCP access, no terraform binary) and fail as **empty charts, not apply errors**. Trello #114 ([SIfSHGs9](https://trello.com/c/SIfSHGs9)). | Shipped |
| `specs/quick-tour-modals.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** pixel-art **Quick Tour** onboarding modals. Four tours keyed by **game SHAPE, never by `gameTypeId` or name** (GUIDs churn on every teardown+reseed and operators create their own types): `overview` (lobby basics, 4–5 steps) plus three archetypes — `preset` (Beast Mode), `race` (Cosmic Sprint), `lottery` (Space Jackpot); a **preset lottery matches nothing and gets no auto-tour** (not an error — `How to play` still opens the overview). Mode name, ticket price and draw frequency are read **live off the `ActiveGame` DTO**, so copy stays true for any operator-created type; the mockup's *"Pick an animal number (1-8)"* is **not shipped** (preset choices are operator free text and the mobile keypad is name-based/uncapped since #94). `overview` auto-opens on the first lobby visit, a mode tour after the type resolves on `/game/play`; **never two at once**, never on rollover, **suppressed under the boot-recovery / idle overlays**, and **hidden entirely when embedded**. X · GOT IT! · **Esc** (new) · backdrop all close **and** mark seen; one re-open point (a `How to play` ☰ menu row). **"First time" = local ∪ server**: production Main mints an **anonymous token with an empty `sub`**, so a player has no server identity until a wallet connects and `localStorage` (`space-stroker.tour.seen`) is the only store that works for the common case; a new nullable CSV **`OperatorPlayer.SeenTours`** rides the existing prefs GET/PATCH (wallet-then-`sub` identity per #46), the **PATCH unions rather than overwrites**, and local-only keys are pushed up **once** on wallet connect. Keys are **versioned** (`overview@1`, …) so a future copy rewrite can re-show a tour; ships at `@1` ⇒ nobody sees one twice. Art reuses existing assets only (#116 owns new artwork). ⚠️ Code findings: **no second prefs GET** (publish `seenTours` through `useDisplayCurrency.loadPreferences` — `singleFlight` dedupes only *concurrent* calls), `singleFlight` responses are **shared and read-only**, **Teleport does not escape** the recovery/idle overlays (explicit guards required), `selectedGame` has **two** assignment sites (watch it, don't hook a call site), and `formatLotteryFrequency` must be **extracted** from `GameTerminal.vue`. One additive migration; Feature ⇒ mandatory happy-path integration test. Trello #115 ([dwPf7li8](https://trello.com/c/dwPf7li8)). | Shipped (backend half NOT shipped — see the banner in the spec) |
| `features/quick-tour-modals.md` ⚰️ | ⚰️ **RETIRED — the Quick Tour was DELETED from the product on 2026-08-02 (#130).** Read `features/how-to-play.md` for what runs. The doc is **kept, not deleted**, for the three things it decided that still bind: **archetype-by-SHAPE** (extracted verbatim into `utils/gameArchetype.ts`), the **copy rules** (never "animal"/"1-8"; and *the word is "word"*), and **why persisting a UI dismissal was rejected** — it would mean prompting a Solana signature to close an onboarding modal. Everything after its "What survived the deletion" section is **history, not behaviour**. Its 10 art components live on, renamed into `components/guide/`. Originally: pixel-art **Quick Tour** onboarding — `overview` on the first lobby visit plus one of three **shape-matched** mode archetypes (`preset`/`race`/`lottery`; a preset lottery matches none and gets no tour) after the game type resolves, name/price/frequency read live off `ActiveGame`, re-openable from one `How to play` ☰ row, absent when embedded. Dismissals live in **`localStorage` only** (versioned keys, all `@1`); X/GOT IT!/**Esc**/backdrop all close **and** mark seen. ⚠️ **The spec's whole backend half was dropped before merge:** `remove-operators` made `PATCH /players/preferences` signature-proved and display-name-specific, so persisting a dismissal would have meant prompting a **Solana signature to close an onboarding modal** (and the overview fires where no wallet is connected); an unsigned endpoint keyed on a public wallet was rejected too. No entity, migration, handler change or backend test — and `useQuickTour` makes **no network call at all**. Auto-open refuses under **every** blocking overlay — boot recovery, idle, relaunching, the `z-[70]` **18+/Terms gate** (which both watchers take as a *dependency*, so the tour fires on ACCEPT) and the display-name prompt — because a Teleported modal is their **sibling**. Mobile folds the lever step into "tap PLACE BET" (4 steps) and freezes all art under `light-graphics`. Ships with a **sticky header** (`overflow-x` moved to `<html>`). | Shipped |
| `specs/unified-websocket-connection.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-meta`:** collapse the **two** SignalR connections every player holds into **one hub and one tab-scoped connection**. `RecentBetsHub` carries no logic of its own (its `JoinGame`/`LeaveGame` are byte-identical to `ActiveGamesHub`'s) and `GameService` injects `IHubContext<RecentBetsHub>` it **never reads**, so `BetNotificationConsumer` simply publishes `{gameId}/Bets` on `ActiveGamesHub` — **same group, same event name, same payload** — and the hub, its `MapHub("/Hubs/RecentBets")` route and the dead ctor param are **deleted** (clean break: no players yet, so no dual-publish window). The bigger half is lifetime: today both sockets are `stop()`ed and rebuilt on **every rollover** (2 negotiates + 2 upgrades per player per round), so a new **`useGameHub`** composable owns one connection for the tab — lazy on first game load, kept across rollovers, game-type switches and back-to-lobby, swapping groups via `LeaveGame`/`JoinGame` instead of reconnecting. **Per-game event names are kept** (`{0}/Terminal|Status|DisplaySpeed|ChatMessage|Bets` + broadcast `GameReceived`): flattening to static names would rewrite the contract everywhere for zero socket-count gain and is *less* safe, since after `LeaveGame(old)` the old-named events simply stop arriving. ⚠️ **Spec-time finding:** the shipped idle watch is `GamePage`-scoped (`onMounted`/`onUnmounted`), so a tab-scoped socket would be **unpoliced in the lobby** — `useGameHub` now owns `startIdleWatch`/`stopIdleWatch` so an abandoned tab still stops pinning a Cloud Run request. Motivation is concurrency: the API sets no `max_instance_request_concurrency` (⇒ platform default 80 concurrent requests/instance, a WebSocket pinning one for its lifetime ⇒ ~40 players/instance today), and every negotiate counts against the per-IP Armor rule that #111 showed is one bucket per shared VPN exit. **No entity/migration/config/API change; player-visible behaviour identical.** Minor change ⇒ no `features/` doc, but an integration test is required anyway (AC #2) because bets silently vanishing is the only failure mode with no symptom. Trello #120 ([b2PoXJ1I](https://trello.com/c/b2PoXJ1I)). | Shipped |
| `features/wallet-connect-name-prompt.md` | **Frontend-only (`monkify-front`), no spec:** a player who connects a wallet with **no display name for that wallet** is asked once, right then, via a small dedicated prompt (name field + `SAVE`/`SKIP`) instead of being left as `ANON` until they find the header menu. Gates: player routes only (never the `/wallet-bridge` popup or the admin portal), **never embedded**, a wallet-identity context with a wallet connected, a preferences read that **succeeded**, no stored name, and not already dismissed. `SKIP`/backdrop/`ESC` write `space-stroker.namePrompt.skipped.<wallet>` — **once per wallet, remembered**; a saved name needs no flag. Fires on **auto-reconnect** and on a hydrated **mobile deeplink** session, not just a manual click. Saves through the existing `PATCH /players/preferences` `{ displayName, wallet }`. Two supporting changes: `loadPreferences` now returns whether the read succeeded (the shared `displayName` keeps its previous value on failure, which would read as "no name"), and `walletForPrefs` also treats a token with **no usable `sub`** as a wallet-identity context — mirroring `PlayerCustomizationModal`'s `useWalletIdentity` and fixing the lobby reading preferences against an **empty identity** under the anonymous Main token. **No backend/DB/DTO/SignalR change**; FE has no test runner ⇒ lint + build + local Docker QA. | Shipped              |
| `specs/fix-bets-logic.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-meta`:** two new **per-round bet limits**, so one player can no longer hold a disproportionate share of a round's choice space. **(1)** A game type with **non-empty `PresetChoices`** (Beast Mode) accepts **at most one bet per player per round** — today one wallet can back all 8 beasts and, once a second player joins, holds 8 of the 9 bets. **(2)** In **every** type, the round's **only bettor** cannot place a second bet until a bet from a **different** player exists — after that, **no further cap** (the rolling-cap variant was rejected as unexplainable in the UI and refund-heavy). Both live in `RegisterBetHandler.ValidateBetParameters`, inserted **after** the per-player duplicate check and **before** the free-text exclusivity block, so a blocked bet is caught after `ValidateSolanaPayment` (⇒ must refund) but **before** `RunPreBetCheck` (never reaches the operator's RG provider) and **before** `DebitStandard` (a `Standard` bet is never debited for a bet we reject). Rejections reuse the shipped `RefundInvalidBet` helper + two new `ErrorMessages` (`OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`), with a **pre-payment guard in `useGame.bet()` + `GameConsole`** so a crypto player is never charged for a bet that will be refunded. ⚠️ **Load-bearing spec-time finding:** a **free bet can carry an `OperatorPlayerId`** — the client sends the wallet on free bets in a `SolanaToken` game — so the gate is **`!_isFreeBet && OperatorPlayerId != null`**, not the bare null check the card described; a bare check would limit free bets and break solo free-bet QA. Also found: the existing index `(GameId, OperatorPlayerId, Choice)` already serves both queries ⇒ **no column, no index, no migration**, and `RefundInvalidBet` throws, so the new blocks need no `return`. Every persisted `Bet` row counts regardless of `Status` (a refunded bet still holds your preset slot); limits reset on rollover; Rule 1 is **not** race-proof (application check only — worst case one extra same-wallet bet, no money lost, knowingly accepted). **Orbit (#121–#123) gets no exemption** — an Orbit bet consumes the player's preset slot. Minor change ⇒ **no integration test, no `features/` doc**; `features/games.md` §9 + `features/choice-exclusivity.md` corrected at ship. Trello #126 ([wF5zmsss](https://trello.com/c/wF5zmsss)). | Shipped |
| `specs/legal-docs-self-exclusion.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-lp` + `monkify-meta`:** the "cover our asses" legal pass. (1) A **blocking 18+/Terms entry gate** on **both** public properties (LP-only would miss anyone landing on `app.` directly), ACCEPT + DECLINE, remembered in an **`ss_terms`** cookie scoped to `.spacestroker.com` exactly like the shipped `ss_consent`; the cookie-consent banner renders **only after** acceptance (merging the two overlays is **rejected** — bundling analytics consent with terms acceptance invalidates the cookie consent). Hidden when embedded, on `chromeless` routes, `/error`, `/admin` and on the legal pages themselves (someone in trouble must reach the help links without accepting anything). (2)+(3) LP pages **`/terms`** (grounded in shipped behaviour — non-custodial, USDC/Solana, the 0.5% crypto house cut, exactly when a bet is and is not auto-refunded, **and an explicit "not legal advice, not lawyer-reviewed"** note; **no** restricted-jurisdictions clause, since we do not geo-block) and **`/responsible-gambling`** (help-org links + the honest admission that a per-wallet exclusion is bypassed by connecting another wallet). (4) **Per-wallet self-exclusion**: two nullable columns on `OperatorPlayer` (`SelfExcludedAt` = the flag, `SelfExcludedUntil` = expiry, **NULL ⇒ permanent**), 24h/7d/30d/90d/permanent with **no early undo**, **extend-only**, proved by an **ed25519 `signMessage`** over a Redis-held single-use nonce (verified with the already-referenced **Solnet.Wallet**, no new NuGet) — without proof any public wallet address could be permanently locked out. Enforced in `RegisterBetHandler` **before** `ValidateSolanaPayment`, for **every** operator always (**not** via the opt-in `IPreBetCheckResolver`), blocking paid **and** free bets; state rides the existing `GET /players/preferences`. ⚠️ **Four spec-time corrections to the refined card, all from code exploration:** the inline reject-and-refund path **never refunds** (the event names a `Bet` row that is only inserted later, so `RefundInvalidBetConsumer.LoadBet` returns null and logs a warning — affecting choice-exclusivity, duplicate-bet, game-not-valid and invalid-choice, all while showing the player `RefundWarning`); persisting the rejected bet as `NeedsRefunding` to fix it is **rejected** because **nothing filters bets by status** (`GetBetsByGameId` feeds both the typer and the provably-fair replay, `Bet.PublicBetStatus` is dead code) so it could win a round and change a verified outcome — refunds are therefore issued **bet-less from the event** (new `GameId` + signature on `RefundInvalidBetEvent`, signature-keyed idempotency, `CryptoTransactionLog.BetId` made nullable); `ErrorMessages.RgSelfExcluded` reads *"contact your operator"* and is wrong when **we** are the operator, so a new `SelfExclusionActive` message is added; and mobile Tier-1 deeplink **`signMessage` does not exist** and must be built. Trello #125 ([3kPjRxsJ](https://trello.com/c/3kPjRxsJ)). | Shipped |
| `features/legal-docs-self-exclusion.md` | Distilled: an **18+/Terms entry gate** on both public properties (blocking, ACCEPT **and** DECLINE, remembered in a version-carrying **`ss_terms`** cookie scoped to `.spacestroker.com`; the cookie banner waits for it, and it is hidden when embedded / chromeless / `/error` / admin / on the legal pages themselves), LP **`/terms`** + **`/responsible-gambling`** pages (grounded in real behaviour, **not legal advice**, no restricted-jurisdictions clause since we do not geo-block, no legal entity named — a stated gap), and **per-wallet self-exclusion** (`OperatorPlayer.SelfExcludedAt`/`SelfExcludedUntil`, **null-with-flag = permanent**; 24h→permanent, extend-only, **never lifted early**; blocks paid **and** free bets; ed25519 signature over a single-use Redis nonce via the already-referenced Solnet.Wallet; enforced for **every** operator before `ValidateSolanaPayment`, not via the opt-in RG hook). ⚠️ Ships a **money fix beyond the feature**: the inline reject-and-refund paths **never refunded** (the event named a `Bet` row inserted only later), so four shipped rejection paths kept a paid crypto player's stake while showing them the refund warning — refunds are now issued **bet-less from the event**, keyed on a new indexed `CryptoTransactionLog.RelatedSignature` (persisting the bet instead was rejected: nothing filters bets by status, so it could enter the typer and change a provably-fair outcome). | Shipped |
| `specs/remove-operators.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** delete the **entire operator concept**. Only one operator ("Main") ever existed, yet it reaches into 197 of 367 backend `.cs` files: `Operator`/`OperatorToken` tables, a signed **launch token** on every player call, the unused **`Standard`** settlement path, a per-operator RG hook with no adapter, a split money ledger, and per-operator cache keys and scope filters. **A new `GeneralSettings:Operator` config section becomes the operator** — onboarding another one means deploying again with a different `spacestrokersettings.json`, not inserting a row (which is why the section keeps the now-ironic name). Player endpoints go **public** (identity = the connected wallet; the token proved nothing the wallet doesn't); the **admin token survives**, keyed from config with **no `iss` check**, and `POST /dev/admin-token` returns. `OperatorPlayer` → wallet-keyed **`Player`** (`Currency` dropped with display currency). **`PaymentMethod` is deleted entirely** — enum, columns and the one-strategy resolver. **The money ledger unifies into ONE `TransactionLog`**: verified never 1:N (placement ⇒ crypto row only, credit/refund ⇒ 1:1), and once `Standard` goes the Solana path is its only writer, so `CryptoTransactionLog` merges in and is dropped — collapsing three columns stored twice (`WriteLedger` passes the *same* `signature` to both) and, as a **bonus fix**, making `BetId` **nullable**, which is exactly why today's bet-less refund path writes **no generic ledger row at all**. Placement rows type as **`Debit`** (safe: `BetDomainService` already never subtracts them — pinned by a test). `ExternalTransactionReference` → **`Signature`** everywhere (~73 backend refs, **0 frontend**; the wire already said `paymentSignature`). Default game types move from `DefaultGameParameters.cs` into config, seeded **create-only by name** so a restart never resurrects a deactivated type. `ISecretProtector` deleted. **`PATCH /players/preferences` gains an ed25519 wallet signature** over a **purpose-keyed** nonce generalised from self-exclusion (a public, spoofable rename was rejected). Migration is **greenfield**. Branding removed; **iframe embedding kept** (operator-free in code). ⚠️ `constraints.md` §6 (vague operator messaging) becomes **obsolete** and must be removed at ship; `sales/` + `pitch/` stay **out of scope** and need a follow-up card. Trello #128 ([TWf2oXxi](https://trello.com/c/TWf2oXxi)). | Ready |
| `specs/lottery-draw-time.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** fix the **24-day Space Jackpot** (`"24:00:00"` parses as 24 **days** in .NET; `remove-operators` rewrote the correct `TimeSpan.FromHours(24)` into it) **and** give a lottery an optional **fixed UTC draw-time anchor** so draws stop drifting. New nullable **`LotteryParameters.DrawTimeUtc`** (+ one additive migration) with a pure `LotterySchedule.NextDrawTime`: slots sit on the **absolute grid** `{ epoch + DrawTimeUtc + k·Frequency }` (epoch = `DateTime.MinValue`, **not** "today", so the grid survives restarts), integer-tick maths only (ticks ≈ 6.4e17 exceed `double`'s exact range), and the earliest slot at least a configurable **`MinimumLotteryDrawLeadSeconds`** (600) away — so 24h + 22:00 is **22:00 UTC daily with zero drift**, and 4h + 22:00 is 02/06/10/14/18/22. Null anchor ⇒ today's rolling `UtcNow + Frequency`, untouched. Because seeding is **create-only by name**, a config fix alone reaches nobody: a narrow **startup self-heal** (`ExecuteUpdate`, mirroring `AllowFreeBet`) patches lottery `Frequency` + `DrawTimeUtc` only, and a new **`CorrectScheduledLotteryDraws`** step recomputes `StartDate` **and reschedules Hangfire** for open **anchored** `WaitingBets` rounds (both directions — it also re-arms a draw missed during downtime; unanchored rounds are excluded or they would be pushed out forever). ⚠️ Three load-bearing spec-time findings: **a lottery bet never publishes `BetPlacedEvent`** (`RegisterBetHandler:130` publishes only when `LotteryParameters == null`), so the Hangfire job at `StartDate` is the **sole** draw trigger and `StartDate` alone changes nothing; the lead **must** exceed `MinimumWaitPeriodForBets` or `BetPlacedConsumer:65` returns before the refund branch and the round is **stuck forever holding real money**; and the stale 24-day job is **provably harmless** (`GameAcceptingBets` guard + `TryStartGame`'s conditional `UPDATE … WHERE Status = WaitingBets`) ⇒ no job-id column. Frontend shows the anchor (lobby badge third line, terminal `DAILY AT 22:00 UTC`); countdown untouched. Admin draw-time input and any seed-time validation guard **explicitly out of scope**. Feature ⇒ mandatory happy-path integration test + `features/` doc at ship. Trello #132 ([cGlkxiYo](https://trello.com/c/cGlkxiYo)). | Shipped |
| `features/lottery-draw-time.md` | Distilled: a lottery type may pin its draw to a fixed **UTC time of day** (`LotteryParameters.DrawTimeUtc`, nullable — null keeps the rolling `UtcNow + Frequency`). Slots sit on the absolute grid `{ epoch + DrawTimeUtc + k·Frequency }` (epoch = `DateTime.MinValue`, **not** "today", so it survives restarts; integer-tick maths because `double` can't hold ~6.4e17 ticks exactly), taking the earliest slot ≥ `MinimumLotteryDrawLeadSeconds` (600) away. **Space Jackpot = daily at 22:00 UTC**, zero drift — it was drawing every **24 days** because `"24:00:00"` parses as 24 days in .NET. The lead **must** sit between `MinimumWaitPeriodForBets` and `Frequency`: under it, `BetPlacedConsumer` returns before the under-subscribed refund branch and the round is stuck forever holding real money. Seeding is create-only, so a **startup self-heal** patches just `Frequency` + `DrawTimeUtc`, and a **startup realignment** recomputes `StartDate` **and reschedules Hangfire** for off-grid open anchored rounds (both directions ⇒ it also re-arms a draw missed during downtime; unanchored rounds excluded or they'd be pushed out every boot; idempotent). The stale Hangfire job needs no cancelling (`GameAcceptingBets` guard + `TryStartGame`'s conditional UPDATE). UTC only (no DST), admin portal can't anchor, no seed-time validation. | Shipped |
| `features/home-dashboard.md` | Distilled: the app has a **front door**. `/` renders the starting page (hero, the real picker, the player's own numbers, panelled sections leading to every surface) instead of redirecting into the bare lobby; `/game` still renders the plain lobby, because that is the URL `EMBEDDING.md` points integrators at. **The `/space-stroker` prefix is gone** — every path is prefix-free, a **strip-prefix shim** redirects old URLs (query **and** hash preserved, because a mobile wallet deeplink hop straddling a deploy returns a player to the old path mid-bet), and a **generic catch-all → `/error`** closes the pre-existing blank-page gap; the two catch-alls coexist by vue-router **specificity ranking, not declaration order**. **Embedded `/` redirects to `/game`** and the Quick Tour `overview@1` auto-open moved from the lobby to `/` (every guard intact; a player deep-linking to `/game` gets no overview). The **picker card** was rebuilt: per-archetype art and accent (`lottery → preset → race`, exhaustive — never by name or `gameTypeId`, which is a fresh GUID on every reseed), the LP's colour mapping, chips floating over the artwork, and the rule that **nothing on it ever renders as `0`** (absent beats zero). ⚠️ **`loading="lazy"` never fires inside these `clip-path` frames** — Chrome folds ancestor clips into the intersection check, so every card renders an empty black frame forever, even for a `data:`-URI image. Live figures cost **three `GET /games/{id}` calls and no backend**, on mount + a 15 s-guarded tab-refocus, with **no SignalR** (`useGameHub` keeps its one-connection/one-group invariant). Backend adds two public reads: **`GET /players/stats?wallet=`** (11 aggregate metrics + currency descriptor, 30 s Redis TTL, **zeroed stats with 200** for an unknown wallet) and **`GET /games/history?take=`** (newest settled rounds across every type in one call, `take` clamped, every `My*` column null). **No entity, no migration, no Consumers/SignalR change, no rate-limit rule.** | Shipped |
| `features/how-to-play.md` | Distilled, **frontend-only**: `/how-to-play` is **one linkable guide page that nothing auto-opens**, replacing the deleted Quick Tour; `/responsible-gaming` replaces the RG modal (a **material-preserving** grey-panel swap that keeps both excluded states, the two-step Permanent confirm and the honest "a different wallet bypasses this" note). **EIGHT registry-driven, deep-linkable sections** — `what-is-this` · `where-to-start` · `how-a-round-goes` · `what-you-win` · `the-modes` · `provably-fair` · `refunds` · `your-setup` — and **the order is an argument**: the idea, getting in, the round, the money, which table, why believe it, what goes wrong, the optional knobs. The spec was written for five; they shipped, were reviewed and were **rebuilt** (spec D4) because the page had no introduction, named three of five sections after **modals**, buried the live mode cards, and left *how much do I win* in a subordinate clause — **the UI was kept wholesale, only the sections and copy changed**. ⚠️ **The word is "word"** (you pick a word and bet on it; "a run of characters" / "back a run" / "make your call" were tried and **rejected by the owner**), the prose states **no character count** (it is per-game `choiceRequiredLength`, so any number is wrong for some game — it appears only on the mode cards), and copy stays preset-agnostic (#115). `refunds` must stay honest: a **lottery** under-subscribed at draw time refunds, a **race** below its minimum **waits and is not refunded**. `what-you-win` states **no house-cut percentage** (per-deployment, published live on `ActiveGame.commissionPercentage` → point at the STATS panel) and never promises "you cannot win less than you staked" (`BetRewardBiggerThanThePot` is an error branch). Mode cards are archetype-matched **by SHAPE** via `utils/gameArchetype` (a preset lottery renders **no card**) and draw **`utils/gameArt.artFor`** — the lobby picker's own illustration — because the three guide terminal recreations all render the same green console; they are the **only** section depending on the catalogue fetch, and the other seven render in every state. ⚠️ The section rail highlights **whichever section fills most of the screen**: an `IntersectionObserver` **cannot** express that (it reports each element's own ratio; the overtake moment is not a threshold on either), so it is a **rAF-coalesced scroll listener**, plus one anchor exception (`top` in `[64,184]` wins) or clicking a short nav entry lands the highlight on its taller neighbour. Two dead rules recorded so they do not return. The **black material's one consumer is now `GuideTip`** (2 tips, bookending; `refunds`/`what-you-win` get none — tone breaks the tie). Both routes redirect to `GameLobby` when embedded via one `EMBED_REDIRECT_ROUTES` set, neither is an `isPlayerRoute()`, and the router has **no `scrollBehavior`** so the page handles `route.hash` itself. Also fixed: **`applySelfExclusionPreferences` was a dead export** since `remove-operators` — `selfExcluded` was always `false` after a reload and the client pre-bet guards were dead code (backend always enforced it) — now wired into `loadPreferences`, so those guards are **live again on the money screen**. New event `guide_section_click`; both tour events deleted. Open: **the copy is unreviewed** (own card), Orbit deferred, and the RG **mobile deeplink return has never run on a physical phone**. Trello #130 ([mzhftjAl](https://trello.com/c/mzhftjAl)). | Shipped |
| `specs/home-dashboard.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** give the app a **front door**. A new **landing page at `/`** (`HomePage` + `src/components/home/`) with two designed states — **cold** (no wallet: welcome/onboarding-led, connect invitation, `GET /players/stats` **not called at all**) and **connected** (stats-led) — carrying the **real mode cards** (`GameTypes.vue` reused verbatim, so #116 art and #129 practice mode land here for free), **personal stats**, quick links (Stats · History · Fair · Settings, embed-gated), a how-it-works entry (opens the Quick Tour until **#130** ships and repoints it) and a **global-stats teaser** reusing `GET /games/stats`. `/game` keeps rendering today's plain lobby untouched (it is the documented iframe target + the `?type=` deep-link); **embedded `/` redirects to `/game`** via a router guard, so `HomePage` never mounts in a frame. Quick Tour **`overview@1` auto-open moves from the lobby to `/`**, every guard intact. Folded in because it rewrites the same routes: the **`/space-stroker` prefix is dropped** (`/` · `/game` · `/game/play` · `/game/history` · `/game/stats` · `/admin` · `/wallet-bridge` · `/background` · `/error`) behind a **strip-prefix redirect shim** that preserves query **and** hash — without it a surviving old URL is a *blank page* (no catch-all + the LB `/*` fallback serves `index.html`) and a **mobile wallet deeplink hop straddling the deploy strands a paid bet** — plus a **generic catch-all → `/error`** closing that pre-existing gap. Backend adds exactly one public, wallet-keyed read **`GET /players/stats?wallet=`** (11 metrics: rounds · bets · most-played mode · wagered · won · net · win rate · biggest win · biggest multiplier · current + best streak) — **on-demand aggregate over existing rows + a 30 s Redis TTL, no entity, no migration, no Consumers/SignalR change**; money rules mirror `game-type-stats`/`fair-stats-menu` exactly (only `RewardForWinnersCompleted` rounds, free bets excluded from money but counted in participation, payouts from `Credit` ledger rows), an unknown wallet returns **zeroed stats with 200**. ⚠️ Code findings: Fair + Settings open state is **local to `GameHeader.vue`** and must be promoted to a shared `useHeaderModals` (a second modal instance is not an option); a **stale unrouted `HomePage.vue` stub** already exists and must be overwritten deliberately; **no rate-limit rule may be added** (`RateLimitSettingsTests` pins an exact set); the two catch-alls rely on vue-router **specificity ranking**, not declaration order; `EMBEDDING.md`/`embed-example.html` still document a `#launch_token` flow that no longer exists (paths corrected, flow **not** restored); LB path rules verified **prefix-agnostic**. Feature ⇒ mandatory happy-path integration test + a `features/` doc at ship. Trello #131 ([ijRUhWbX](https://trello.com/c/ijRUhWbX)). | Shipped |
| `specs/modal-panel-design-system.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** **one shared dialog shell + TWO PANELS + one named z-index scale**, migrated across every overlay, the side panel and four pages, so #130 (How to play) builds on a settled system instead of inventing a fourth look. Source of truth is the Claude Design export `Space Stroker Modals.dc.html` (chrome direction **1b hairline**); its own spec card is headed *"SHARED BY BOTH TIERS / WHAT DIFFERS"*, i.e. the two panels **coexist** — an earlier pass that read this as "two materials, one of which dies with the Quick Tour" was wrong and is superseded (spec §3.1 tables all nine corrections). **GREY** = `--color-surface` on a 1px hairline, amber title left behind a 6px square, title/footer bars sunk to `--color-surface-bar`, bordered VT323 `X`. **BLACK** = `--color-void-deep`, 2px cyan frame + glow, centred cyan title between pixel sparkles, one centred amber ghost. **Shared:** `compact 380`/`standard 520`/`wide 720`/`xwide 1024`, 36·44px titlebar, 18px body, 12–14px footer, 4px radius, `90dvh`, the **dithered** backdrop (void 55% + 9px 45° gradient — **never `backdrop-filter`**, which `html.light-graphics` strips on every phone), the full a11y package (`role=dialog`, `aria-modal`, `aria-labelledby`, focus trap, safe restore, scroll lock), a first-class **`dismissible: false`** mode, and dismissal by X / Esc / backdrop / primary. **Choosing a panel is behavioural:** grey when the player is *doing* something or the tone is serious; black only when brief-and-informative **and** light — today the quick tours alone. Also new: a full-bleed **`ERR`/`OK` status strip**, the flat **`.btn-dialog`** family (`.btn-arcade` stays the cabinet button), and **two** mobile treatments — bottom sheet for `compact`/`standard`, **full screen** for `wide`/`xwide`. Esc is routed by a module-level **`useOverlayStack`** so a blocker on top *swallows* it rather than closing the modal underneath. ⚠️ **The layering bug is not a z-index bug.** `App.vue`'s root is its own stacking context, so anything inside it composites at **10** and can never paint above a teleported modal, whatever number it carries — `GameIdleOverlay` at `z-40` was losing for that reason, and **teleporting it, not renumbering it, is the fix**; the scale (`base 0 · app 10 · furniture 20 · header 40 · popover 50 · modal 60 · blocking 70 · legal 80`) therefore documents that **every value ≥ 60 must be `<Teleport to="#app">`**. ⚠️ **`arcade.css` beats Tailwind utilities**, so the cap, radius and mobile treatments live in the class — a `max-h-[90dvh]` utility beat `.dialog--fullscreen` and clamped the full-screen tour to 90% of the viewport until it was moved. ⚠️ **`MobileWalletModal` teleported to `body` at `z-[100]`** — the wallet picker could **cover the 18+/Terms gate**; the scale closes it. `BootRecovering`/`EmbedRelaunching` are **not overlays** but blocks rendered *instead of* `<main>` (they teleport out of that flow position; `App.vue`'s `v-if` chain is deliberately untouched so `<main>` still never mounts during recovery). Named exceptions: the game furniture, **the docked side-panel cabinet** (metal bezel, rivets, recessed CRT — **AC #15 amended**) and `GameTypeCard`. Scope grew at the owner's call: `ResponsibleGamingModal` migrated rather than left bespoke, `QuickTourModal` rebuilt on the black panel, `xwide` added, and `/game/history` + `/game/stats` on a grey page fill while `/error`, `/soon` and the dashboard **deliberately** stay near-black. **No backend, entity, migration, API or SignalR change; `monkify-front` has no test runner** ⇒ `yarn lint` + `yarn build` + manual QA. **AC #10/#20 verified in emulation only — no real iOS device.** Minor change ⇒ no integration test, no `features/` doc. Trello #124 ([NIMydn5I](https://trello.com/c/NIMydn5I)). | Shipped |
| `specs/how-to-play.md` | **Feature, `monkify-front` + `monkify-meta` — no backend:** replace the overlay-only onboarding with **one readable, linkable guide page**. New route **`/how-to-play`** (`HowToPlay`) — five **registry-driven**, deep-linkable sections (`where-to-start` · `how-the-game-works` · `provably-fair` · `player-settings` · `refunds`) behind a sticky section nav (a chip row below `lg`); **Orbit is deferred** to a card created at ship, and the registry is what makes it a one-entry addition later. The **Quick Tour is deleted outright** — modal, composable, constants, both auto-open watchers, all seven guards, the versioned `space-stroker.tour.seen` set and both GA4 tour events — so **nothing is ever unprompted again** (knowingly accepted: `HomeHowItWorks` on `/` is the onboarding now). Its **10 art components survive**, renamed into `components/guide/` with the `TourStep` prop replaced by a flat `GuideArt` and every measured `artClass` kept verbatim. `resolveTourKey` is extracted to `utils/gameArchetype.ts`, so mode content stays **archetype-matched by SHAPE** (never by id or name) off the live catalogue with real ticket price + `formatLotteryFrequency` frequency; a preset lottery matches nothing and renders **no card**. **Responsible Gaming becomes `/responsible-gaming`** and the modal is deleted — the riskiest part, since the mobile wallet-deeplink return must re-point at the route (a bet hop and a self-exclusion hop are **indistinguishable** in the return params; only what was stashed tells them apart). Neither route is an `isPlayerRoute()`, and **both redirect to `/game` when embedded** through one shared `beforeEach`. ⚠️ Code findings: **`applySelfExclusionPreferences` is a dead export** — `selfExcluded` is always `false` after a reload, so the excluded state is unreachable and the client pre-bet guards are dead (backend still enforces it) — **fixed here**; the router has **no `scrollBehavior`**, so a cold `#anchor` load scrolls nowhere unless the page handles it; `getGamesTypes` is `runWithRecovery`-wrapped, so a flaky network raises the blocking `> RECONNECTING…` overlay over the guide; `TourBetPanelArt` is static *because* it only ever rendered under `isMobile`, a guarantee a page does not carry; and under-subscribed **races are never refunded** (only lotteries are), which the Refunds copy must not misstate. Design decisions: the guide's **tip/callout block adopts the black panel** (otherwise deleting the tours leaves the black material with zero consumers — the dangling-rule condition #124 was filed to fix), `/how-to-play` takes near-black **`.arcade-frame`** and `/responsible-gaming` takes **`.arcade-frame--grey`**. Frontend-only ⇒ **no integration test is possible**; `features/how-to-play.md` is still mandatory and `features/quick-tour-modals.md` is retired. ⚠️ **Shipped 2026-08-02 with EIGHT sections, not the five specced here** — the five shipped, were reviewed and were rebuilt; see **spec D4**, **§7.4.1** (the active-section rule, twice wrong) and **§7.5.-1** (the copy rules, incl. *the word is "word"*). Trello #130 ([mzhftjAl](https://trello.com/c/mzhftjAl)). | Shipped |
| `specs/control-sizing-system.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** **one control-sizing scale** every button and input family consumes, so any button and any input in the same row are the same height **by construction** rather than by hand-tuned padding. Flagship symptom: the Settings custom-seed row, where `.dialog-input` (44.7) sits beside `.btn-dialog` (33.2) because **neither declares a height**. Three steps as `--ctl-h-sm/md/lg` + `--ctl-px-*` in `colors.css` — **34.4 / 40 / 48**, with `sm` and `lg` pinned to the arcade values that already ship (`2.15rem` / `3rem`) so the lever, wallet cluster, dashboard and `GameTypeCard` do not move a pixel — stepping to **40 / 44** below 1023px in **one media block** with **no call-site class change**. **Height is not skin (D1):** `.btn-arcade` joins for height and keeps its bevel + 5px key-drop, which **overhangs the border-box on purpose** (D3). The flat family defaults to `md`, so `.btn-dialog` **and** the input share one default — *that* is the actual fix. `.dialog-input` → **`.input-arcade`**, one class for dialogs and pages, **renamed not aliased**, plus `--select` (`appearance: none` + a two-gradient chevron that reads `--color-ink-mute` directly, since a data URI cannot resolve a custom property). `.dialog-field` joins at `md` and centres by **`line-height`, never `display:flex`** — its one consumer needs `truncate`, and `text-overflow` does not apply to an anonymous flex item. **Both wrapper components deleted** (`SecondaryButton` 0 consumers; `PrimaryButton` 4) — ⚠️ it is **not** a pure pass-through: `HomeHero` renders `<wallet-buttons variant="ghost" size="md">`, so both branches of both props are live, and **`md:w-[200px]` must survive** the dead-utility deletion or the sticky header reflows. ⚠️ **The focus ring MUST be `outline`, not `box-shadow`:** `.btn-arcade--primary`'s key-drop **is** a box-shadow and `html.light-graphics .btn-arcade--primary` re-declares it at **(0,2,1)**, beating `.btn-arcade:focus-visible` at **(0,2,0)** — a box-shadow ring would wipe the key-drop on desktop **and be invisible on every phone**. `.btn-arcade` gains a focus ring **it has never had** (there is no global focus rule in the app). **Five** shipped dead-utility stacks recorded, all the same cascade hazard (`arcade.css` is imported after `@tailwind utilities`): `asLink`, `text-xs`/`text-sm`, and **three** `text-primary`/`text-cyan` colours that lose — the `/responsible-gaming` select, the client seed and the nonce **are all meant to be amber/cyan and render in ink today**. Documented non-members: **`.dialog-row`**, text-link buttons, **`/admin`** (its `.admin-input` stays copy-pasted in two files **on purpose**) and `.dialog__close`. ⚠️ **Adopting `.btn-dialog` on `MINE ONLY` silently kills its active state** unless it moves into `arcade.css` — the cascade hazard biting during the fix itself. `.btn-dialog--tour` is **deleted** (0 consumers since #130). Minor change ⇒ **no integration test, no `features/` doc** — the rule lives in `conventions/frontend.md`; `monkify-front` has no test runner ⇒ `yarn lint` + `yarn build` + manual QA on desktop, phone and with `html.light-graphics` forced. **Shipped: all 20 ACs measured in a live browser. ⚠️ A SIXTH dead-utility stack was found in QA — and it was the flagship row itself: the Settings seed row NEVER shared a row on desktop, on `main` either. `sm:basis-auto` is applied exactly as authored, but `flex-basis: auto` DEFERS to the item's own `width`, and the input class declares `width: 100%` — so the input's flex base size was the whole row and SET/ROTATE wrapped underneath. Fixed with `sm:basis-0` (a property the class does not own). This is a distinct shape of the cascade hazard: D18's five stacks all LOSE their tie; this one WINS and still does nothing. Also shipped beyond spec: `display: block` on `.dialog-field` (height is ignored on an inline box) and an `.input-arcade--auto` modifier (the two filter-bar selects need shrink-to-fit, and a `w-auto` utility would lose the tie). See spec §16.** Trello #135 ([jMuAlPBV](https://trello.com/c/jMuAlPBV)). | Shipped |
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

**Nothing is mid-implementation.** The Orbit release train (#121/#122/#123, branch `release/orbit`) is
queued; it is not merged to `main`. **`feature/mobile-game-design-update`** (#134) has a spec and
worktrees but no code yet.

⚠️ **#134 rewrites `GameConsole` / `GameTerminal` / `GameMonkey` / `GamePage`.** Those four were
deliberately out of scope for #135 (just shipped), so the two never shared a file — but #134 now
starts from a `main` that carries the control scale. Anything it builds takes its height from
`--ctl-h-*`, not from hand-tuned padding.

- _Last shipped:_ **Control sizing system** (Minor change, **frontend-only**) —
  `feature/control-sizing-system` merged to `main` in `monkify-front` + `monkify-meta` on
  **2026-08-02**; spec `specs/control-sizing-system.md`, distilled in **`conventions/frontend.md`**
  (no `features/` doc — it is a refactor of an existing system, so the rule lives with the conventions
  it amends). Trello #135 ([jMuAlPBV](https://trello.com/c/jMuAlPBV)).
  One `--ctl-h-sm/md/lg` (34.4 / 40 / 48, stepping to 40 / 44 / 48 below 1023px) + `--ctl-px-*` scale
  in `colors.css` that **every in-scope button and input family consumes**, so any two controls in a
  row are the same height **by construction**. Each family keeps its own skin and font ramp — **height
  is not skin**. `sm` and `lg` are pinned to the arcade values that already shipped, so the lever,
  wallet cluster, dashboard and cards did not move a pixel (measured: still exactly 48 and 34.4).
  Deleted `PrimaryButton.vue`, `SecondaryButton.vue` and `.btn-dialog--tour`; renamed `.dialog-input`
  → `.input-arcade` (**not aliased**); added `.btn-icon`, `.btn-dialog--active`, `.input-arcade--select`
  and `.input-arcade--auto`; replaced four hand-rolled `box-shadow` focus rings with **one `outline`**
  treatment that also gives `.btn-arcade` a ring it had never had.
  ⚠️ **Rules worth carrying:** the focus ring **must** be `outline` — `html.light-graphics` re-declares
  `box-shadow` on `.btn-arcade--primary` at (0,2,1) and would wipe the key-drop on desktop *and* be
  invisible on every phone. And the cascade hazard has **two** shapes now, not one: five stacks that
  **lose** their specificity tie, plus **`sm:basis-auto`, which wins its tie and still does nothing**
  because `flex-basis: auto` defers to the `width` the class owns — that one had kept the card's own
  flagship row from ever being a row. See spec §16.
- _Shipped before that:_ **How to play** (Feature, **frontend-only**) — `feature/how-to-play`
  merged to `main` in `monkify-front` + `monkify-meta` on **2026-08-02**; spec
  `specs/how-to-play.md`, distilled in `features/how-to-play.md`. Trello #130
  ([mzhftjAl](https://trello.com/c/mzhftjAl)).
  `/how-to-play` replaces the Quick Tour with **one linkable page nothing auto-opens**, and
  `/responsible-gaming` replaces the RG modal. **Eight** registry-driven sections, not the five the
  spec was written against: the shipped five were rebuilt on owner review because the page had **no
  introduction** (it opened on "connect a wallet" and never said what the game was), named three of
  five sections after **modals**, buried the live mode cards at the tail of a four-job section, and
  answered *how much do I win* in a subordinate clause (spec D4). **The UI was kept wholesale — only
  the section set and the copy changed.**
  ⚠️ Rules worth carrying: **the word is "word"** (you pick a word and bet on it — "a run of
  characters" was tried and rejected); the mode cards draw **`utils/gameArt.artFor`**, the lobby's own
  illustration, because the three guide terminal recreations all render the same green console; the
  **black material's one consumer is now the guide's `GuideTip`** (it would otherwise have zero, the
  dangling-rule condition #124 was filed to fix); and the section rail highlights **whichever section
  fills most of the screen** — an `IntersectionObserver` **cannot** express that, so it is a
  rAF-coalesced scroll listener with one anchor exception for nav clicks.
  Also fixed in passing: **`applySelfExclusionPreferences` was a dead export** since
  `remove-operators`, so `selfExcluded` was always `false` after a reload and the client pre-bet
  guards were dead code — they are **live again on the money screen**.
  Open follow-ups: the **copy has not been reviewed by the team** (its own Trello card), the **Orbit
  section is deferred**, and `/responsible-gaming`'s **mobile deeplink return has still never run on a
  physical phone**.
- _Previously:_ **Consolidated modal + panel design system** (Minor change) —
  `feature/modal-panel-design-system` (**`monkify-front`** + **`monkify-meta`**; **no backend**),
  **merged 2026-08-02**, Trello #124 ([NIMydn5I](https://trello.com/c/NIMydn5I)).
  Every overlay in the app now goes through **one shell** (`ui/ArcadeModal.vue`) wearing one of
  **two panels**: **grey** (`--color-surface`, 1px hairline, amber title left behind a 6px square,
  sunken title/footer bars) and **black** (`--color-void-deep`, 2px cyan frame + glow, centred cyan
  title between pixel sparkles, one centred amber ghost action). They share the whole geometry —
  sizes `compact 380 / standard 520 / wide 720 / xwide 1024`, 36/44px titlebar, 18px body, 12–14px
  footer, 4px radius, 90dvh, the dither backdrop, and dismissal by X / Esc / backdrop / primary.
  **Which panel a surface gets is behavioural, not decorative:** grey when the player is *doing*
  something or the tone is serious; black only when the surface is both brief-and-informative **and**
  light — today the quick tours, and nothing else. See `conventions/frontend.md` → "Two panels".
  Three things worth carrying forward. **The layering bug was never a z-index bug:** `App.vue`'s root
  is its own stacking context, so anything inside it composites at `10` and can never paint above a
  teleported modal, whatever number it carries — `GameIdleOverlay` at `z-40` was losing for that
  reason and **teleporting it, not renumbering it, was the fix**. Hence the rule that every value
  ≥ 60 must be `<Teleport to="#app">`. **`arcade.css` beats Tailwind utilities**, so the shell's cap,
  radius and mobile treatments live in the class, not in utilities on the component — a
  `max-h-[90dvh]` utility silently beat `.dialog--fullscreen` and clamped the full-screen tour to 90%
  of the viewport until it was moved. And **`MobileWalletModal` used to teleport to `body` at
  `z-[100]`**, i.e. the wallet picker could cover the 18+/Terms gate; the named scale closes that.
  Shipped with **AC #10 and #20 verified in emulation only** — no real iOS device was available, and
  the body scroll lock vs the bottom sheet is the thing to check first on a real iPhone.
  **#130 (How to play) builds on this.** Note that two of its assumptions changed: `QuickTourModal`
  is no longer a bespoke overlay (it is the black panel on the shared shell) and
  `ResponsibleGamingModal` was migrated rather than left alone.

- _Previously shipped:_ **Starting page (home dashboard) + drop the `/space-stroker` prefix** (Feature) —
  `feature/home-dashboard` (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`**),
  **merged 2026-08-01**, Trello #131 ([ijRUhWbX](https://trello.com/c/ijRUhWbX)).
  The app had no front door: `/space-stroker/game` *was* the product — a 16-line wrapper around three
  cards — and Stats, History, Provably Fair, Settings and the tutorial were reachable only from the ☰
  menu, which a first-time player has no reason to open. Now `/` is a real page, and the redundant
  route prefix is gone (the app is hosted at `app.spacestroker.com`). The prefix drop was folded in
  rather than split off because both halves rewrite the same routes.
  Two things about it are worth carrying forward. **The shim is not cosmetic:** without it a surviving
  old URL renders a *blank page* (there was no catch-all, and the LB's `/*` fallback serves
  `index.html` happily), and a **mobile wallet deeplink hop straddling the deploy** returns a player to
  the old path **mid-bet** — which is why the redirect preserves query *and* hash. A generic catch-all
  → `/error` closes the blank-page gap for every other unmatched path.
  **The picker card went through four design passes**, and the durable lessons are in
  `features/home-dashboard.md`: art and accent chosen by **archetype** (never by name or `gameTypeId`,
  which is a fresh GUID on every reseed, and whose old `gameCharacterType` mapping rendered two of the
  three shipping cards identically); **never headline a zero** (an absent chip says nothing, a
  fabricated one says something false); and `loading="lazy"` **silently never fires** inside a
  `clip-path` frame, which rendered every card as an empty black box even for an image webpack had
  inlined as a `data:` URI.
  Backend stayed small: two public reads (`GET /players/stats?wallet=`, `GET /games/history?take=`),
  both on-demand aggregates over existing rows behind short Redis TTLs — **no entity, no migration, no
  Consumers or SignalR change, and no new rate-limit rule**. Live picker figures cost three
  `GET /games/{id}` calls and **no socket**, deliberately: `useGameHub` keeps its one-connection,
  one-group invariant, and the price is a figure that can be one focus-cycle stale.
  ⚠️ Shipped with two gaps: **AC24/AC38b were never exercised against a funded round** (the live
  `POT NOW` / `FILLING` variants and the green players dot were verified with a temporary local stub
  and by the shipped `computePot` arithmetic, not by a real bet), and the USDC-mark guard (AC32) is not
  exercisable while every game settles in USDC.

- _Previously:_ **Lottery draw time (UTC anchor)** (Feature) — `feature/lottery-draw-time`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`**), **merged 2026-07-30**,
  Trello #132 ([cGlkxiYo](https://trello.com/c/cGlkxiYo)).
  **Space Jackpot was drawing every 24 days.** `remove-operators` rewrote the correct
  `TimeSpan.FromHours(24)` seed as the config string `"24:00:00"` — and .NET's `TimeSpan` parser
  reads a leading component above 23 as **days**, so a player buying a "daily" ticket waited ~24
  days while the lobby, terminal and landing page all advertised a daily draw. Fixing the number
  alone would have reached **nobody**: seeding is **create-only by name**, `CloseOngoingGames` only
  closes `GameStarting`/`InProgress`, and `OpenNewGames` skips types that already have a live round —
  so the open round would have kept its 24-day `StartDate` and its Hangfire job across every restart,
  never rolling over, leaving the fix inert for 24 days. Hence a **startup self-heal** (the two
  lottery scheduling fields only) plus a **startup realignment** of off-grid open rounds that updates
  `StartDate` **and** reschedules the job.
  The card also grew a second half mid-refinement: a nullable **`LotteryParameters.DrawTimeUtc`**
  anchor, because even a correct period drifts (`StartDate = UtcNow + Frequency` at creation walks
  forward by however long the previous round took). Draws now sit on the absolute grid
  `{ epoch + DrawTimeUtc + k·Frequency }`; **Space Jackpot draws at 22:00 UTC daily** with zero drift.
  Three findings shaped it, all from reading the code rather than the card: **a lottery bet never
  publishes `BetPlacedEvent`** (`RegisterBetHandler` publishes only when `LotteryParameters == null`),
  so the scheduled job is the *sole* draw trigger; the lead **must** exceed `MinimumWaitPeriodForBets`
  or `BetPlacedConsumer` returns before the refund branch and the round is **stuck forever holding
  real money**; and the stale Hangfire job is **provably harmless**, so no job-id column was needed.
  Level `Feature` ⇒ `features/lottery-draw-time.md` + 2 integration tests. 508 unit / 80 integration
  green. Carried an unrelated prettier fix to `monkify-front/src/utils/walletDeepLink.ts` that `main`
  could not build without.

- _Last shipped:_ **Quick Tour Modals** (Feature, **frontend-only**) — `feature/quick-tour-modals`
  (**`monkify-front`** + **`monkify-meta`**), **merged 2026-07-30**,
  Trello #115 ([dwPf7li8](https://trello.com/c/dwPf7li8)).
  Pixel-art onboarding: an `overview` tour on the first lobby visit and one of three **mode
  archetypes** (`preset` / `race` / `lottery`) after the game type resolves, matched by game **SHAPE**
  (never `gameTypeId` or name — GUIDs churn on every reseed and types are admin-created), with
  name/price/frequency read live off `ActiveGame`. One `How to play` ☰ row re-opens them; nothing
  auto-opens when embedded. Dismissals live in **`localStorage` only**
  (`space-stroker.tour.seen`); keys are versioned, all shipped `@1`, and X / GOT IT! / **Esc** /
  backdrop all close **and** mark seen.
  **Two things landed on `main` mid-flight and each one rewrote a decision.** (1) The 18+/Terms gate
  is **`z-[70]`** against the tour's `z-[60]`, so an auto-opened tour sat *invisible underneath it*
  while still owning the Esc handler — and an unaccepted gate is simultaneous with a first lobby
  visit, i.e. the common case, not an edge. Both watchers now take `termsAccepted` as a **dependency**
  rather than only a guard, so the tour fires on ACCEPT. Generalised: a Teleported modal is a
  **sibling** of every blocking overlay, so each one (boot recovery, idle, relaunching, terms gate,
  display-name prompt) needs an explicit refusal — DOM nesting protects nothing. (2) **Remove
  Operators** deleted the feature's entire persistence layer: `OperatorPlayer` became `Player` behind a
  **signature-proved** prefs write, and `useDisplayCurrency` — where the tour published its seen-set —
  was deleted. Persisting a dismissal server-side would now mean asking a player to **sign a Solana
  message to close an onboarding modal** (and the overview fires where no wallet is connected), so the
  server half was **dropped entirely**: no column, no migration, no handler change. The accepted cost
  is that a tour can reappear on a different browser.
  Frontend `lint` + `build` green; the backend contributes nothing (its branch is identical to `main`).
  ⚠️ Open: no `role="dialog"`/`aria-modal` on the modal.

- _Previously shipped:_ **Remove Operators** (Feature) — `feature/remove-operators`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`**), **merged 2026-07-30**,
  Trello #128 ([TWf2oXxi](https://trello.com/c/TWf2oXxi)).
  **The deployment is now the operator.** Space Stroker was built as a multi-tenant B2B platform — an
  `Operator` row owned every game type, held an RS256 launch keypair, carried a contract window and
  branding, and (via `OperatorToken`) owned the Solana token — but only one operator ever existed, so
  the apparatus was deleted: **197 of 367 backend files** mentioned it. `GeneralSettings:Operator`
  (`Token` + `AdminToken` + `DefaultGames`, seeded **create-only by name**) replaces it; onboarding
  another operator means deploying again with a different `spacestrokersettings.json`.
  Two shipped subsystems went with it rather than being made operator-less: the off-chain **`Standard`**
  payment method (nobody left to debit — taking the `PaymentMethod` enum, the strategy/resolver, the
  operator wallet API and the whole FX/display-currency stack) and the **per-operator RG pre-bet hook**
  (it resolved off an `Operator` and no adapter was ever configured; first-party per-wallet
  self-exclusion is untouched and is now the only RG control that runs). `ISecretProtector` went too —
  encrypting a config value with a key stored in the same config bought nothing once no user could type
  a private key into the admin portal.
  **Every player endpoint is public** (a paid bet still costs real money, so the open surface is not
  free to spam) and identity is the **connected wallet** (`OperatorPlayer` → `Player`). Because a wallet
  address is public, the display-name write is gated on an **ed25519 signature** over a
  **purpose-scoped** single-use nonce — the same mechanism self-exclusion already used, generalised so
  a challenge minted for one flow cannot be replayed against the other.
  **One ledger:** `CryptoTransactionLog` merged into `TransactionLog` (verified lossless — the
  relationship was never one-to-many, and once `Standard` was gone the Solana path was the only writer,
  making the generic table a strict subset). `BetId` became **nullable**, which turned the shipped
  bet-less-refund special case into an ordinary row; `ExternalTransactionReference` → `Signature`.
  **The mandatory integration test earned its keep**: `OperatorlessFlowTests` caught a real bug —
  `GameService.InsertBet` detaches `bet.Game` and `bet.Player` before `AddAsync` so EF will not
  re-insert a persisted graph, but restored only `Game`, so **every placement ledger row was written
  with a blank `Wallet`**. 492 unit and 77 integration green. It also exposed a pre-existing ~17% flake
  in the provably-fair happy path: the typer's character cap is a **fixed multiple** of the
  expected-chars-to-win (`p95 × 0.9 / mean`) whatever the search space, so ~1 race in 6 reaches the cap
  and is decided by the fallback.
  ⚠️ Open: **`sales/` and `pitch/` still describe the multi-tenant operator product** and need their
  own card. Greenfield migration — no in-flight round or carried lottery pot survives the deploy.

- _Previously shipped:_ **Legal Documentation & Per-Wallet Self-Exclusion** (Feature) —
  `feature/legal-docs-self-exclusion` (**`monkify-backend`** + **`monkify-front`** + **`monkify-lp`** +
  **`monkify-meta`**), **merged 2026-07-29**, Trello #125 ([3kPjRxsJ](https://trello.com/c/3kPjRxsJ)).
  A blocking **18+/Terms gate** on both public properties (one version-carrying `ss_terms` cookie on
  `.spacestroker.com`, so the LP→app hop asks once; the cookie banner now waits for it, because consent
  to analytics must stay separable from accepting the Terms), LP **`/terms`** + **`/responsible-gambling`**
  pages, and **per-wallet self-exclusion** — 24h→permanent, extend-only, **never lifted early**, blocking
  paid **and** free bets, proved by an **ed25519 signature** over a single-use nonce (wallet addresses
  are public, so an unproven request would let anyone lock anyone out).
  **The load-bearing find was not the feature.** The inline reject-and-refund paths in
  `RegisterBetHandler` **never refunded anything**: every one runs before `InsertBet`, so
  `RefundInvalidBetConsumer` could not load the bet and returned after a warning — four shipped paths
  (choice-already-taken, duplicate bet, game-not-valid, invalid choice) kept a paid crypto player's
  stake **while showing them `RefundWarning`**. Fixing it by persisting the bet as `NeedsRefunding` was
  **rejected**: nothing filters bets by status (`GetBetsByGameId` feeds the typer **and** the
  provably-fair replay; `Bet.PublicBetStatus` is dead code), so a rejected bet could win a round and
  change a verified outcome. Refunds are now **bet-less from the event**, keyed on a new indexed
  `CryptoTransactionLog.RelatedSignature` — the payment reversed, since the row's own `Signature` is
  the refund's transaction. That mistake was in the spec and was caught only by the integration test.
  574 unit (**+29**) and 85 integration (**+4**) green. ⚠️ Open: the mobile deeplink `signMessage` hop
  is **device-unverified**; no legal entity/governing law is published (deliberate); a bet-less refund
  that exhausts its retries has **no automated recovery**.

- _Previously shipped:_ **Fix bets logic — per-round bet limits** (Minor change) —
  `feature/fix-bets-logic` (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`**),
  **merged 2026-07-29**, Trello #126 ([wF5zmsss](https://trello.com/c/wF5zmsss)).
  Nothing capped how many bets one player could hold in a round: a wallet could back **all 8 beasts**
  in *Beast Mode* and hold 8 of the 9 bets the moment a second player joined, and the first bettor of
  an empty round could stack bets before anyone could compete for the remaining sequences. Two guards
  now sit in `RegisterBetHandler.ValidateBetParameters`, **after** the duplicate check and **before**
  the free-text exclusivity check — placement that matters, because it is after `ValidateSolanaPayment`
  (the crypto bet already paid ⇒ must be refunded, not merely rejected) and before both
  `RunPreBetCheck` (a blocked bet never reaches the operator's RG provider) and `DebitStandard` (a
  `Standard` bet is never debited for a bet we reject): **(1)** preset types accept **one bet per
  player per round** (`OneBetPerPresetRound`); **(2)** every type blocks a second bet while the bettor
  is the round's **only** one (`SecondBetNeedsAnotherPlayer`), unlocked by any bet from a different
  `OperatorPlayerId` — a `NULL` one counts — after which there is **no further cap**. Frontend refuses
  both before payment (`useGame.iHaveBetThisRound` / `anotherPlayerHasBet`, read off the live
  `{gameId}/Bets` list so the UI unblocks itself), with the backend authoritative.
  ⚠️ **The load-bearing find came from spec-time code exploration, not refinement:** the card recorded
  the gate as `OperatorPlayerId != null`, but a **free** bet in a `SolanaToken` game *does* resolve an
  `OperatorPlayer` (the client sends the wallet on free bets), so that bare check would have limited
  free bets and **broken solo free-bet QA** — the exact thing the exemption existed to protect. The
  shipped gate is **`!_isFreeBet && OperatorPlayerId != null`**, pinned by a dedicated unit test.
  Two more findings shrank the work: the existing index `(GameId, OperatorPlayerId, Choice)` already
  serves both queries ⇒ **no column, no index, no migration**, and `RefundInvalidBet` throws, so the
  guards need no `return`. Every persisted `Bet` row counts **regardless of `Status`** (an
  admin-refunded bet still holds your preset slot); both limits reset on rollover.
  **Knowingly accepted:** Rule 1 is **application-level only** — no unique index like
  `(GameId, ExclusiveChoice)` — so two simultaneous same-wallet bets can both slip through; the worst
  case is one extra same-wallet bet and **no money is lost**, and Rule 2 is count-based so it could
  not be indexed anyway. **Orbit (#121–#123) gets no exemption** — an Orbit bet will consume the
  player's preset slot, flagged for #122's refinement.
  540 unit tests green (**10 added**) + integration **81\81** (**0 added** — `Minor change` exemption),
  with one existing test adapted: `CryptoBets_ResolveAndReuse_SameOperatorPlayerByWallet` placed two
  paid bets from one wallet while it was the round's only bettor, so a second player's bet was
  inserted between them — a true positive, not a test workaround. **Minor change ⇒ no
  `features/fix-bets-logic.md`**; distilled into `features/games.md` §9,
  `features/choice-exclusivity.md`, `features/free-bet-mode.md` and `glossary.md`.
  ⚠️ **Pre-existing flake seen once while shipping:**
  `ProvablyFairFlowTests.HappyPath_EndedGame_VerificationReproducesWinnerAndHashHolds` failed on one
  full run and passed in isolation and on a clean re-run. It uses two distinct players, so neither new
  rule can reach it; its closing assertion (the round must find a natural match before the character
  cap) is probabilistic. **Not investigated — needs its own card if it recurs.**

- _Previously shipped:_ **Unified WebSocket Connection** (Minor change) —
  `feature/unified-websocket-connection` (**`monkify-backend`** + **`monkify-front`** +
  **`monkify-meta`**; `monkify-lp` never had a branch), **merged 2026-07-29**, Trello #120
  ([b2PoXJ1I](https://trello.com/c/b2PoXJ1I)).
  Every player held **two** SignalR connections and both were `stop()`ed and rebuilt on **every round
  rollover** — 2 negotiates + 2 WebSocket upgrades per player per round. Now there is **one hub and
  one connection per browser tab**. `RecentBetsHub` is deleted along with its `/Hubs/RecentBets` route
  and the `IHubContext<RecentBetsHub>` that `GameService` injected and **never read**;
  `BetNotificationConsumer` publishes `{gameId}/Bets` on `ActiveGamesHub` under the **same group and
  event name**, so no payload or contract changed. A new **`useGameHub`** composable
  (`monkify-front/src/hooks/useGameHub.ts`) owns the tab's connection — opened lazily on the first
  game load, kept across rollovers, game-type switches and lobby round-trips, with each round doing a
  **group swap** (`LeaveGame`/`JoinGame` + bind/unbind `{gameId}/…`) instead of a reconnect;
  `GameReceived` is bound once for the tab. `useGame` keeps its **exported surface unchanged**, so not
  one component needed editing.
  **The load-bearing find came from spec-time code exploration, not refinement:** the idle watch was
  armed and disarmed by `GamePage`'s mount/unmount, so a tab-scoped socket would have been
  **unpoliced the moment the player walked back to the lobby** — an abandoned tab pinning a Cloud Run
  request forever, the exact cost the card exists to remove. `useGameHub` now owns the watch (armed on
  connect, stopped in `stopForIdle`), and the `initalizeWebsocketConnections` idle gate was dropped so
  a lobby click after an idle stop cannot land on a dead screen.
  Motivation is concurrency: the API sets no `max_instance_request_concurrency` (⇒ platform default
  **80** concurrent requests/instance, a WebSocket pinning one for its lifetime ⇒ ~40 players/instance
  before, ~80 after), and every negotiate counts against the per-IP Armor rule that #111 showed is one
  bucket per shared VPN exit. ⚠️ That 80 is **inferred from the setting's absence** in Terraform, not
  read from live Cloud Run, and **setting it explicitly is still an unclaimed follow-up card**.
  530 unit tests green (**0 added** — a pure compile-surface edit across 5 test files) + integration
  **81/81** (**1 added**: `HubBetNotificationTests`, required by AC #2 even though the level exempts
  it, because bets silently vanishing is the only failure mode with no symptom). **Minor change ⇒ no
  `features/unified-websocket-connection.md`**; distilled into `architecture.md` §6, `features/games.md`,
  `features/game-pot-display.md` and `conventions/frontend.md`, with a superseded-in-part banner on
  `specs/idle-websocket-disconnect.md`.
  ⚠️ **Unrelated pre-existing bug surfaced while deploying this card:**
  `monkify-backend/scripts/setup_local_environment.bat` exits **255** on its own post-deploy
  "Verifying the served bundle" step — a PowerShell one-liner (`ForEach-Object{$_.Groups[1].Value}`)
  that cmd tries to run as a program. It runs *after* the front image is built and started, so the
  deploy is fine, but every run reports failure. **Needs its own card.**

- _Previously shipped:_ **GCP Dashboards** (Minor change) — `feature/gcp-dashboards`
  (**`monkify-meta`** + **`monkify-backend`**; `monkify-front` / `monkify-lp` never had a branch),
  **merged 2026-07-28**, Trello #114 ([SIfSHGs9](https://trello.com/c/SIfSHGs9)).
  Production observability as Terraform — a tenth workload module `modules/monitoring`: one dashboard
  `spacestroker-prod-overview`, two uptime checks, an email notification channel, a spend budget
  (50/90/100%) and two Cloud Armor log-based metrics. `enable_dashboards` is **derived** by the deploy
  tool from `environment == "production"` rather than authored, and the **module block carries
  `count`**, so a development plan holds **no `module.monitoring` address at all** — not zero-instance
  resources. Dev and prod share one GCP project, so that distinction is what keeps a dev teardown away
  from production-facing monitoring objects.
  **The backend half was the load-bearing one.** `AddLogs` now emits **structured JSON in Production
  only** (`SpaceStroker.Common/Logging/GoogleCloudJsonFormatter.cs`, no new NuGet). Without it the
  card's error/warning panels were undeliverable: Cloud Run files plain-text stdout as `textPayload` at
  severity **`DEFAULT`**, so an `Error` and a `Warning` were indistinguishable in Cloud Logging and
  "count the errors" had nothing to count. With real severities the panels read the **built-in**
  `logging.googleapis.com/log_entry_count`, so **no log-based metric was needed for them**. The
  exception is appended into `message` so a stack trace is **one** entry rather than N.
  ⚠️ **Three of the card's asks were not buildable as written**, and were rewritten during refinement
  rather than faked: billing cost **cannot be charted** in Cloud Monitoring at all (no cost data source,
  no embed widget ⇒ a link to Billing → Reports plus the budget), **no built-in metric carries a request
  path** (`run.googleapis.com/request_count` has none and the `api` path matcher has only a
  `default_service` ⇒ per-endpoint descoped to response-code class), and **distinct-user counts are
  impossible** from log-based metrics (⇒ denied *requests* + a Log Analytics query; per #111 one IP is
  not one user).
  ⚠️ **The preview-WAF metric reads `previewSecurityPolicy`, not `enforcedSecurityPolicy`** — a
  `preview = true` rule populates only the former, so the wrong field yields a permanently-zero panel
  reading as "the WAF never false-positives", when the priority-900 OWASP rule is in preview precisely
  because it does, on Solana base58 bet payloads.
  ⚠️ **Two operator prerequisites**, both now documented: `monitoring.googleapis.com` +
  `billingbudgets.googleapis.com` were missing from the `gcloud services enable` list in
  `deploy/docs/FIRST-DEV-DEPLOY.*`, and the budget needs **`roles/billing.costsManager` on the billing
  account** — no project-level role grants it. Empty `gcp.billingAccountId`/`gcp.alertEmail` ⇒ budget
  and channel are skipped and the apply still succeeds.
  530 unit tests green (**16 added**: `GoogleCloudJsonFormatterTests` ×13 new, `AddLogsProviderTests`
  ×3 appended) + 83 deploy-tool tests green (**5 added**: `EnvNamingTests`, so `validate` actually
  checks the six new ids instead of passing vacuously). **Minor change ⇒ no
  `features/gcp-dashboards.md`**; distilled into `features/terraform-gcp-deploy.md` → Observability,
  `conventions/backend.md` → Logging, and `architecture.md` §13.
  Integration **80/80 green** — but only after a **load-sensitive flake** was chased down, and it is
  worth its own card.
  ⚠️ **`ProvablyFairFlowTests.HappyPath_EndedGame_VerificationReproducesWinnerAndHashHolds` fails
  under machine load.** It asserts `WinnerDecidedByFallback == false` ("this round found a natural
  match, so it must have stopped short of the cap"), but the typer is **wall-clock paced**
  (`typer-pacing-scalability`), so on a loaded machine the round reaches `MaxCharacters` before a
  natural match, the fallback fires and the assertion flips. **Measured, not guessed:** feature branch
  **2/2 full-suite runs FAILED while other builds/tests were running**, then **0/1 failures on the
  same branch with the machine quiet**; clean `main` **0/2** (also quiet). The correlation is with
  **load, not with the branch**.
  It also **cannot** be this card mechanically: the integration hosts run as **`Development`**
  (`IntegrationTestFixture.cs:107,166`), so `IsProduction()` is false, the formatter is never
  instantiated, and the line actually executed — `logBuilder.WriteTo.Console()` — is byte-identical to
  `main`. The entire backend diff is that one unreferenced-in-Development file, a `using`, and two
  **unit**-project test files; the integration assembly is untouched.
  Same family as the `GlobalStatsFlowTests`/`GamesHistoryFlowTests` flake fixed under #105 but a
  **different root cause** (wall-clock pacing under load, not under-backed seed choices).
  **Needs its own card** — the assertion should tolerate a fallback-decided round, or the harness
  should pin the pacing.
  ⚠️ **Merged committed-but-NOT-applied** — no terraform binary and no GCP access in the authoring
  environment, so nothing was `terraform validate`-ed and the **Cloud SQL/Redis `metric.type` strings
  are unverified**; a wrong one renders an **empty chart, not an apply error**. Acceptance criteria
  **1** and **13** need a real apply. Joins #102/#103/#105/#110 in the pending-apply queue.

- _Previously shipped:_ **Auto Bet Seeder** (Minor change) — `feature/auto-bet-seeder`
  (**`monkify-meta`** only; `monkify-backend` and `monkify-front` never had a branch),
  **merged 2026-07-28**, Trello #113 ([AhRfdxga](https://trello.com/c/AhRfdxga)).
  A local **`SpaceStroker.Seeder`** console app (`deploy/tools/`, assembly `spacestroker-seeder`)
  that places **one real paid bet** into **Cosmic Sprint** and **Beast Mode** on production whenever
  their current round has no bets — an empty round reads as a dead game, and with
  `MinimumNumberOfPlayers = 2` nobody wants to be the first one in. A pass runs on start and then
  every 6h. It adds **no product code**: it only calls endpoints that already ship. It is
  **self-limiting** by construction — its own bet from the previous pass is what makes the round
  non-empty on the next one — so at most one seeded bet is outstanding per game, with no state file.
  **Three code-exploration findings shaped the design** and are the reason a spec existed rather than
  just the card: the launch token's TTL is **3600 s**, shorter than the 6h interval, so it is
  re-minted every pass; Solnet's `sendTransaction` defaults `preflightCommitment` to **`Finalized`**,
  which cannot see a blockhash fetched at `Confirmed` (the identical trap `solana-connection-rpc` hit
  on the frontend); and `ValidateBetPayment` matches the pre/post token balances **by owner**, so a
  seeder wallet that is also the token owner nets a delta of **0** and every bet is rejected as
  underpaid *after* the transfer confirmed — which is what makes the fail-fast startup guard
  load-bearing rather than defensive.
  Money safety is structural: retry is **phase-aware**, so once a transfer signature exists the
  transfer is never re-sent (only the read-only status poll and the same-signature bet POST may
  repeat), and no pass can spend two stakes for one bet. A confirmed stake is always spent into
  whichever round is accepting — emptiness gates whether we *pay*, not where an already-paid stake
  lands — or logged as an `ORPHAN PAYMENT` with its full signature.
  **94 unit tests added** (`SpaceStroker.Seeder.Tests`), 0 changed; the 78 existing
  `SpaceStroker.Deploy.Tests` are untouched. Minor change ⇒ **no integration test and no
  `features/auto-bet-seeder.md`**; distilled into `features/terraform-gcp-deploy.md` →
  "Console tools", which also had its stale test counts corrected (76/43 → 78/44).
  ⚠️ **Two things outstanding.** The on-chain half **has never run**: no transfer sent, no bet placed
  (`constraints.md` §5 forbids an agent pointing it at production, and the path needs a funded
  wallet), so the `TransferChecked` shape, the `preflightCommitment` fix, the status poll and the JSON
  field mappings are unverified — run it first with a wallet holding roughly one stake plus a little
  SOL. And the accepted cost stands: `MinimumNumberOfPlayers = 2`, so a seeded bet that never attracts
  a human **stays committed indefinitely** (races never auto-refund); recovery is the manual
  `POST /admin/bets/refund` → 12h sweep.

- _Previously shipped:_ **In-app rate limiter keying** (Minor change) — `feature/rate-limiter-keying`
  (**`monkify-backend`** + **`monkify-meta`**; `monkify-front` untouched), **merged 2026-07-28**,
  Trello #112 ([h27WZeQC](https://trello.com/c/h27WZeQC)). Every in-app `AspNetCoreRateLimit` rule was
  keyed on the **load balancer's** address, so all rules were **one global bucket shared by the whole
  player base** — measured in production, not inferred (`ForwardLimit` unset ⇒ ASP.NET's default of `1`
  walks one hop from the **right** of `X-Forwarded-For`, and the rightmost entry is the proxy that
  *added* it). Worst case live: `*:/solana/balance/live` at **20/min in total**, called once per player
  per round on bet placement and win settlement. Three fixes: **`ForwardLimit = 2`** (a hardcoded
  constant — the hop count is deployment topology, and `null` would trust the attacker-controlled
  *leftmost* entry); **`UseIpRateLimiting()` moved below `UseCors()`**, because the limiter
  short-circuits and its 429 previously carried no `Access-Control-Allow-Origin`, so the browser
  reported a **CORS error** indistinguishable from an outage (this is why #104's lockout read as "the
  app is broken"); and the **`api/*` rule deleted**, having never matched a request (`api` parses as the
  *hostname* in `{verb}:{path}`). Deleting rather than repairing it is what keeps
  `POST /games/{id}/bets` unlimited app-side — now pinned by an **exact-set** assertion, since a
  rejected paid bet strands tokens that already left the wallet with no `Bet` row and no auto-refund.
  Also `*:/rpc` **120 → 240**/min (`confirmSignature` polls `getSignatureStatuses` 1/s for up to 45 s
  over that one path, so a slow-confirming bet costs ~47) and a new `EndpointWhitelist` for `/health` +
  `/Hubs/*`, whose wildcard form was **verified** by a second host carrying a 1/min catch-all plus a
  control test.
  **The card's "`ForwardLimit` must land before the `api/*` repair" ordering constraint dissolved** —
  it applied to *repairing* the rule; deletion matches nothing today and is safe in any order.
  ⚠️ **A SECOND, pre-existing bypass was found and closed.** AspNetCoreRateLimit **4.0.2 defaults
  `RealIpHeader` to `"X-Real-IP"`** and resolves the client from that header **in preference to** the
  connection address. Google's ALB does not originate it but forwards whatever the **client** sends, so
  a caller supplying its own `X-Real-IP` got a **fresh bucket per request** and walked through every
  rule over the ordinary public path — the `/rpc` and `/solana/*` caps protecting the paid Helius quota
  were already evadable, independent of `ForwardLimit`. Pinned to `""`; without it the `ForwardLimit`
  fix would have passed every test and bought almost nothing in production. Also **measured**, not
  assumed: with `EnableEndpointRateLimiting: true` the **specific** rule wins (`/solana/balance/live`
  allows exactly **20**, not the broader rule's 120).
  510 unit + 80 integration tests green; 18 tests added across `RateLimitSettingsTests` and
  `RateLimitKeyingTests`, no pre-existing test modified. **Minor change ⇒ no
  `features/rate-limiter-keying.md`**; distilled into `architecture.md` → Edge, `conventions/backend.md`
  → "Middleware ordering and client-IP keying", `features/frontend-helius-gateway.md` and
  `features/terraform-gcp-deploy.md` → Cloud Armor.
  ⚠️ **Two things outstanding:** the **accepted Helius cost increase** (the old global buckets were
  suppressing legitimate traffic and the fix releases it — watch the credit burn for a day or two after
  the prod deploy) and the **`run.app` ingress bypass** (the API is `INGRESS_TRAFFIC_ALL`, so a direct
  caller forges `X-Forwarded-For` and mints a fresh bucket per request). This is therefore a
  **fairness** fix between real players, **not** an abuse defence; ingress restriction is its own card.

- _Previously shipped:_ **Entry-path recovery** (Minor change) — `feature/entry-path-recovery`
  (**`monkify-front`** + **`monkify-meta`**; no backend — that half is #112), **merged 2026-07-27**,
  Trello #110 ([KvfUl6ff](https://trello.com/c/KvfUl6ff)). A transient API failure at boot stranded a
  standalone player on `/error` — a page whose entire body was `<p>This game is unavailable.</p>`, with
  no retry, no reload and no link — and because Cloud Armor's throttle enforces across the **whole**
  60 s interval, the manual reload that was the player's only recourse re-failed *and* spent another
  request. Three independent routes reached that dead end and none could tell "rate-limited for 20
  seconds" from "this operator is gone". Transient failures are now classified and absorbed behind a
  bounded **jittered back-off (~5 s / 20 s / 55 s)** shown as a one-line `> RECONNECTING…` state;
  `/error` always offers RETRY; `getGamesTypes` joined the back-off so a throttled lobby no longer
  renders as zero games. ⚠️ **The finding the whole card turned on: Armor's 429 never arrives as a
  status.** It is emitted before the app runs and carries no CORS headers, so the browser blocks it and
  axios sees `error.response === undefined` — which is why the incident was reported as "a CORS error",
  and why a `status === 429 || status >= 500` classifier would have **never fired on it**. Transient
  therefore means **no response at all**, or 429/5xx/408. Two corrections landed against the spec's own
  design: the schedule's last delay moved **45 s → 55 s** because `[5,20,45]` ±20 % bottoms out at 56 s
  and so **failed the >60 s invariant at the jitter floor** (~1 run in 8 would have failed exactly as
  before), and `useOperator` needed a second guard because `ensureLaunchToken()` sits outside its
  try/catch. Also lands a **priority-800 Armor rule** giving `/health` + `/Hubs/*` a looser 3000/60 s
  bucket (a throttle, not an exemption — an exemption would leave `/Hubs/*/negotiate` uncapped while
  #112 then had the in-app limiter under suspicion — since **confirmed and fixed**), the `patchRule` action-change warning in
  `modules/edge/main.tf`, and the `deploy/README.md:117` `production.tfvars.json` fix.
  **Minor change ⇒ no `features/entry-path-recovery.md`**; distilled into `conventions/frontend.md`
  (transient-vs-terminal, the `navigation.ts` shim, `/error` is never a dead end),
  `features/terraform-gcp-deploy.md` → Cloud Armor, and `architecture.md` → Edge.
  ⚠️ **Two caveats.** The Armor rule is **merged but NOT applied** (`terraform apply` is an ops step) —
  it joins #102/#103/#105 in that queue. And **nothing here was verified in a browser**: the spec's
  11-scenario QA matrix was never run and acceptance criteria 1-17 and 22 are behaviourally unverified,
  because `monkify-front` has no test runner and the feature was shipped without a local deploy.
  Verification stopped at `yarn lint`, `yarn build`, and `terraform fmt`/`validate`.

- _Previously shipped:_ **Mobile preset keypad** (Minor change / QOL, frontend-only) —
  `feature/mobile-preset-keypad` (**`monkify-front`** `bc33cd0` + **`monkify-meta`**; backend
  untouched), **merged 2026-07-27**, Trello #94 ([uJdWRk7g](https://trello.com/c/uJdWRk7g)). On a phone
  a preset-choice game (Main's **Beast Mode**) drew its picker with the desktop cabinet's 8 numbered
  `button<N>.png` images. Each is **70×91 with an opaque grey plate baked in** — art made to blend into
  `terminal.png` — so on the mobile panel's `from-[#20293e] to-[#161c2c]` gradient they read as eight
  floating grey squares, made worse by `gap-1.5` and by `button1` being 91px tall against `button3`'s
  90px, which misaligned the rows. Mobile now builds its own keys from plain Tailwind + arcade tokens:
  **one key per `presetChoices` entry (no cap)**, each carrying the choice **NAME** + its 1-based index,
  with an explicit selected state **derived from `userWord`** (never stored, so it self-clears on a
  successful bet and on rollover). Three latent defects went with it: nothing had shown **what you
  picked** (the mobile `<input>` renders only for free-text games and `hover:` never fires on touch);
  the **hard `[...Array(8)]` cap** made choices 9+ unreachable even though `ValidatePresetChoices` sets
  no maximum; and a no-selection or betting-closed tap was a **silent dead end**, because the rejection
  path calls `leverError()` — a sound played at a lever that does not exist on mobile. Keys now dim and
  go inert when `allowBets` is false, PLACE BET is disabled until a choice is picked (but stays live at
  `RewardForWinnersCompleted` so `NEXT ROUND` works), and the label is the operator-safe
  `> PICK YOUR CHOICE`. **Desktop is untouched** and still owns the PNGs; its own 8-key cap is a known
  gap left for its own card. ⚠️ The spec proposed `.btn-arcade` and that was **wrong** — `main.css`
  imports `arcade.css` *after* `@tailwind utilities`, so its `justify-content`/padding beat
  equal-specificity utilities and the keys would have been centered and cramped; the rule now lives in
  `conventions/frontend.md`. Minor change ⇒ **no unit or integration test** (`monkify-front` has no test
  framework); verified by lint, build, and a browser pass that included a real free bet and a real
  two-player race. AC 13 (label ellipsis) and AC 18 (mobile-UA wallet affordance) were **not** fully
  verified — the emulated viewport would not go below ~502px. See `features/mobile-layout.md`.

- _Previously shipped:_ **FAIR modal is off screen** (Bug fix, frontend-only) —
  `feature/fair-modal-off-screen` (**`monkify-front`** + **`monkify-meta`**), **merged 2026-07-27**,
  Trello #109 ([ye9CYjpo](https://trello.com/c/ye9CYjpo)). On any GPU-rasterizing desktop the **FAIR**
  modal opened with roughly its top half above the viewport and its dim covering only the 64px header
  strip — verification was effectively unreachable for most desktop players. Cause: `GameHeader.vue`'s
  root carries `backdrop-blur-md`, and a non-`none` `backdrop-filter` makes an element the
  **containing block for `position: fixed` descendants**, so `inset-0` resolved against the header box
  rather than the viewport. It read as "PC only" because `arcade.css` strips `backdrop-filter` under
  `html.light-graphics`, which `usePerfMode` forces below `lg` — phones and hardware-acceleration-off
  desktops were working **by accident**. Two other overlays were caught in the same trap and failing
  silently: the ☰ menu and wallet-popover click-away backdrops covered only the header strip, so
  clicking out in the page never dismissed them. The fix is deliberately **two mechanisms**: the modal
  wraps itself in **`<Teleport to="#app">`** (the pattern `PlayerCustomizationModal` already used),
  while the click-aways are **deleted** in favour of a new **`useClickOutside`** composable — they
  cannot be teleported, because `App.vue`'s root is `relative z-10`, its own stacking context, so the
  header's `z-40` is sealed inside it and a teleported sibling either paints above the whole app
  (swallowing the dropdown's own clicks) or below it (never clicked at all). That correction overturned
  the refined card's plan and is why the spec, not the card, is authoritative here. Bug fix ⇒ no unit
  or integration test; verified by lint, build, and a **measured** browser pass in which the trap was
  first reproduced with a probe element (1442×63 vs a 1442×822 viewport). The convention now lives in
  `conventions/frontend.md` so the next overlay does not repeat it.

- _Previously shipped:_ **Player lockouts: duplicate concurrent API calls + Cloud Armor ban** (Minor change) —
  `feature/player-lockouts` (**`monkify-front`** + **`monkify-meta`**; backend untouched), **merged
  2026-07-27**, Trello #104 ([SpTvJGi9](https://trello.com/c/SpTvJGi9)). Players were intermittently
  locked out of `app.spacestroker.com` by a Cloud Armor `429` returned **before the app runs** — and
  because Armor denials carry no CORS headers, the browser reported it as a CORS error, so it read as
  "the app is broken". Two fixes. **(1)** A module-local **`singleFlight(key, fn)`** in `mainService.ts`
  hands every caller arriving while a request is in flight the *same* promise, clearing the entry in
  `finally`. It is **not a cache** — nothing survives across time — which is what makes it safe near the
  money path; a blanket GET dedupe was **rejected** because it would collapse
  `getTokenBalance(live:true)`, turning a rate-limit fix into a payout bug. Applied to an allow-list of
  **five** boot-path functions; keys carry their params so the wallet and launch-token-`sub` identities
  never share a promise. **(2)** The priority-1000 Armor rule moved from `rate_based_ban` (100/60 s,
  **300 s ban**) to **`throttle`** (600/60 s, no ban) — the damage came from the *ban*, not the
  threshold: static assets kept returning 200 so the shell rendered while every API call failed, and
  each reload re-armed the ban.
  ⚠️ **Measured reality differs from the spec's claim.** `/players/preferences` went **4 → 3** on a
  SolanaToken game page with a wallet (the two same-tick `?wallet=` calls collapsed; the two `sub`-keyed
  calls ~6 s apart correctly did not), and **the lobby never duplicated at all**. The spec's
  "~143 → ~85-95" is **withdrawn** — the premise that every boot doubled its calls was false. Real
  saving ≈ **1 request per game-page load (~10%)**, so **the Armor change is the load-bearing half**.
  3 of 13 acceptance criteria passed; 2 failed *as written* (the criteria were wrong, not the code), 1
  partial, 3 unexercisable locally, 4 pending the operator/ship. `monkify-front` has **no test
  framework**, so nothing pins this against regression — that cost landed on criteria 05/06/07.
  **Minor change ⇒ no `features/player-lockouts.md`**; distilled into `features/terraform-gcp-deploy.md`
  → Cloud Armor, `architecture.md` → Edge, and `conventions/frontend.md` → Concurrent-request dedupe.
  See the warning below: **not applied yet**.

> ⚠️ **Merged but NOT yet applied:** **Player lockouts** (Trello #104, `specs/player-lockouts.md`,
> merged 2026-07-27). The Armor rule on `main` now says `action = "throttle"`, 600/60 s, no
> `ban_duration_sec`, but **no `terraform apply` has run** — production still **bans for 300 s at
> 100/60 s**, so players can still be locked out. Unlike #102 this config *was* checked locally
> (`fmt -check`, `init -backend=false`, `validate` — all green), but ⚠️ **`validate` cannot prove the
> action is valid**: it accepts a deliberately bogus action string, so the provider has no client-side
> enum check and neither does `plan`. Only **`apply`** can reject `throttle`. If it does, fall back to
> `rate_based_ban` with the shortest `ban_duration_sec` the API accepts. Acceptance criteria 10-12 are
> unverified until then. The **frontend** half needs only a normal front deploy.

- _Last shipped:_ **Static-site upload Cache-Control** (Infra/DevOps, Minor change) —
  `feature/static-site-cache-control` (**`monkify-meta` only**; no backend/front/LP), **merged
  2026-07-27**, Trello #103 ([bSSvCfKF](https://trello.com/c/bSSvCfKF)). Both buckets served **every**
  object with GCS's default `public, max-age=3600`, because `Deployer.UploadAndInvalidate` was a single
  header-less `gsutil -m rsync -r -d`. That cached `index.html` — the file that *names* the current
  hashed chunks — for an hour, while caching immutable chunks for only an hour; and because `-d`
  deletes the previous build's chunks, a returning player booted a stale HTML pointing at files that no
  longer existed. A new pure **`StaticSiteCache`** turns the built `dist` listing into an **ordered plan
  of gsutil calls**: hashed prefixes sync **without `-d`** at `immutable`, the root syncs **with `-d`**
  at `no-cache` behind a single `-x` alternation (gsutil honours only one) that shields the asset
  prefixes plus `_builds/` and drops `*.map`, then **`setmeta` convergence passes** per tier — required
  because `rsync` compares checksums, **not metadata**, so ~19 MB of already-uploaded `img/`+`media/`
  would otherwise have stayed at 1h forever. Assets upload **before** the HTML that names them, and no
  intermediate state is ever *more* cached than the final one. The tier rule tests `.html` at **any
  depth** — the lp's `/cookies` builds to `cookies/index.html`, which a first-path-segment rule would
  have filed as 1h, re-creating the bug one directory down — and anything unrecognised falls to 1h.
  Retained assets are **pruned automatically after 24h** via per-deploy build manifests, because what
  has to be measured is **retirement**, not object age: an image unchanged for months has an ancient
  creation date, so a GCS lifecycle rule would reap it the instant it left a build. That window comes
  from the **open-tab** horizon (`useIdleDisconnect` idles out at 30-60 min), not from `Cache-Control` —
  HTML now revalidates on every load and can never name a stale chunk. Every prune read **fails
  closed**. 76 deploy-tool tests. See the warning below: **not applied yet**.

- _Previously shipped:_ **robots.txt** (Minor change) — `feature/robots-txt` (**`monkify-backend`** +
  **`monkify-front`** + **`monkify-lp`** → **`master`** + **`monkify-meta`**), **shipped 2026-07-27**,
  Trello #105 ([6aOoMq2n](https://trello.com/c/6aOoMq2n)). The three public hosts now answer crawlers
  deliberately. **`app.spacestroker.com`** was the real bug: it had no `robots.txt`, and since #102 the
  LB rewrites a bucket 404 into `/index.html` with a **200**, so `/robots.txt` returned the **SPA's HTML
  under a 200** — and RFC 9309 skips unparseable lines, making that indistinguishable from **allow-all**
  on a host linked from the LP's "PLAY NOW". It is now `Disallow: /` plus **`X-Robots-Tag: noindex`** on
  the front bucket, because `Disallow` alone never *de-indexes* — blocking the crawl also stops Google
  ever reading a `noindex` meta. **`api.spacestroker.com`** (previously a 404, also read as allow-all)
  serves `GET /robots.txt` from `HomeController`, which needed its own **`[Produces("text/plain")]`** to
  override the class-level `[Produces("application/json")]`. The **LP stays indexable** but denies AI
  **training** crawlers (`GPTBot`, `ClaudeBot`, `Google-Extended`, `meta-externalagent`, `CCBot`,
  `Bytespider`) while keeping AI **search** crawlers and **all link-preview agents** allowed — every token
  verified against vendor documentation on 2026-07-27; `anthropic-ai` **dropped** (undocumented) and
  `Applebot-Extended` **deferred** (Apple's page unreachable at spec time). Dev is knowingly out of scope.
  Also carried, out of spec by explicit decision (spec §10.5): the **flaky integration tests** —
  `GlobalStatsFlowTests` / `GamesHistoryFlowTests` seeded **three** winning-eligible preset choices but
  paid into only **two**, so ~1 run in 3 legitimately had no paid winner; a control run on clean
  `origin/main` reproduced it. **Minor change ⇒ no `features/robots-txt.md`.**
  ⚠️ **The `X-Robots-Tag` header is NOT applied** — see below.

> ⚠️ **Merged but NOT yet applied:** **Static-site upload Cache-Control** (Trello #103,
> `specs/static-site-cache-control.md`, merged 2026-07-27). The deploy tool is on `main` and its 76
> unit tests are green, but **no deploy has run**, so every object in both buckets still carries the
> old `public, max-age=3600` and `rsync -d` has not yet stopped deleting old chunks. The upload and
> prune *argument shapes* have never executed against real GCS — only their decision logic is tested —
> so the first live run is that deploy; the prune's fail-closed guards are what make that safe.
> Acceptance criteria 14-19 on the card are unverified until then. **One-time residual window:** anyone
> holding an `index.html` cached under today's `max-age=3600` stays stranded for up to an hour after
> that first deploy — no code can shorten it.

> ⚠️ **Merged but NOT yet applied:** **robots.txt** (Trello #105, `specs/robots-txt.md`, merged
> 2026-07-27). The `X-Robots-Tag: noindex` header on the front backend bucket is on `main` and passes
> `terraform fmt` + `terraform validate`, but no `terraform apply` has run, so `app.spacestroker.com`
> does not send it yet. The other three pieces need only a normal deploy of each property.

> ⚠️ **Merged but NOT yet applied:** **Static-site HTTP status codes** (Trello #102,
> `specs/static-site-status-codes.md`, merged 2026-07-27). The Terraform is on `main`, but no
> `terraform apply` has run and the config was never `terraform validate`-ed.
> ⚠️ **Correction (2026-07-27, #104):** the reason recorded here — "no terraform binary in the authoring
> environment" — was **wrong, or has since changed**. **Terraform 1.13.4 IS installed**; #104 ran
> `fmt -check`, `init -backend=false` and `validate` locally with no credentials and no contact with
> production. This config can and should be validated. (Note what `validate` does **not** do: it accepts
> a deliberately bogus `action` string on an Armor rule, so it checks structure and attribute existence,
> **not** enum values — those are rejected server-side at `apply`.) **Production still serves the old
> behaviour** — every
> `app.spacestroker.com` deep link returns 404 with the SPA shell, and `spacestroker.com/<unknown>`
> returns the LP homepage with a 404. The fix takes effect at the next `apply` + CDN invalidation;
> acceptance criteria 1-13 on the card are unverified until then. **Also note:**
> `feature/static-bucket-armor` is still unmerged and edits three of the same Terraform files — see
> `specs/static-site-status-codes.md` §7 for the merge map before landing it.

- _Last shipped:_ **Static-site HTTP status codes** (Infra/DevOps, Minor change) —
  `feature/static-site-status-codes` (**`monkify-meta` only**), **merged 2026-07-27**, Trello #102
  ([mkogi4tR](https://trello.com/c/mkogi4tR)). One hardcoded `not_found_page = "index.html"` in the
  shared `static_site` module made both static sites return the wrong HTTP status: every SPA deep
  link 404'd while rendering fine, and the LP's already-deployed branded `404.html` was unreachable
  because the homepage was served in its place. Because a GCS website config **can never return a
  200**, the SPA fallback moved to the **load balancer** — a `custom_error_response_policy` on a
  **`/*` path rule** of the `app` matcher (404 → `/index.html`, `override_response_code = 200`) —
  while `/js/ /css/ /img/ /media/` are carved out by a **more specific, policy-free** rule so a
  missing hashed chunk still 404s instead of silently returning HTML. The policy sits on the `/*`
  rule and **not** the matcher default because an omitted `path_rule` policy **inherits** the
  matcher default, which would have defeated the carve-out. The lp bucket simply points
  `not_found_page` at `404.html` and **keeps** the 404 status. See the warning above: **not applied
  yet**.

- _Previously shipped:_ **Resumable teardown** (Infra/DevOps, Minor change) — `feature/teardown-resume`
  (**`monkify-meta` only**; no backend/front/LP), **shipped 2026-07-27**, Trello #98
  ([7a9aYZiG](https://trello.com/c/7a9aYZiG)). `spacestroker-deploy teardown` is now **resumable**:
  re-running it never creates a resource. The full pre-destroy `terraform apply` — which used to
  rebuild everything a failed pass had destroyed — became a **targeted** apply on
  `module.data.google_sql_database_instance.postgres` (a **state-only** write, since that is the
  Terraform-level guard), **skipped entirely** when the instance is no longer in state, with the
  `terraform state list` probe **failing closed** rather than reading an error as "nothing left".
  `destroy` now retries itself **3 passes, 60s/120s**, on any non-zero exit — the three known
  ordering failures all clear once the blocking resource is really gone. **What survives in state
  decides the exit code**, not Terraform's: empty ⇒ 0; only zero-cost `module.network.*` (held by the
  asynchronous Google-managed `serverless-ipv4-*` reservation) ⇒ **0 + a loud warning**; anything
  billable ⇒ **1** with the addresses listed. **`--purge` now stops** before destroying the state
  bucket while any workload state remains, since that bucket is the only record of those resources.
  Decisions extracted into a pure `TeardownDecisions` class and covered by a new
  **`SpaceStroker.Deploy.Tests`** project (16 tests — the deploy stack's first automated tests).
  No Terraform, backend or frontend change; the real GCP behaviour lands on the next ops teardown.
  Teardown contract distilled into `features/terraform-gcp-deploy.md`.

- _Last shipped:_ **Analytics for LP and app** (Feature, frontend-only) — `feature/analytics`
  (**`monkify-front`** + **`monkify-lp`** + **`monkify-meta`** → `main`/`master`), **shipped
  2026-07-27**, Trello #101 ([Hbfp779A](https://trello.com/c/Hbfp779A)). Both public sites now report
  to **one GA4 property**: because `www.` and `app.spacestroker.com` share the registrable domain, the
  `_ga` cookie is shared and the LP→app hop stays a **single session**, so `bet_placed` is attributed
  to the campaign that brought the visitor instead of to `referral/www`. The tag loads only when a
  build-time measurement id is present (`VUE_APP_GA_MEASUREMENT_ID` / `PUBLIC_GA_MEASUREMENT_ID`, both
  carried by the **existing `frontBuild` block** — the deploy tool already writes it into both repos,
  so **no `SpaceStroker.Deploy` change was needed**); **empty ⇒ no script, no cookie, no banner**,
  which is what keeps local, docker and the development environment out of production data. Consent
  Mode v2 defaults to `denied` behind an equal-weight Accept/Reject banner, and the answer rides an
  **`ss_consent` cookie scoped to `.spacestroker.com`** so a choice made on the LP is honoured by the
  app — `localStorage` could not, the two hosts being different origins. Events carry **no money and
  no player identity** (`bet_placed` is a count; outcome is not tracked), and analytics is **off
  entirely when embedded**, on the `chromeless` routes, and on the static `verify.html` /
  `embed-example.html`. The LP's `@vercel/analytics` was removed — inert since the LP moved to GCP —
  and gained a `/cookies` notice page (the footer's first link). CTA clicks use **one delegated
  document listener** because the hero buttons are un-hydrated Astro islands where a per-component
  Vue `@click` silently never fires. **No backend surface at all** ⇒ the mandatory happy-path
  integration test was waived with that written reason; verification was builds plus browser checks.
  Nothing is measured until the GA4 property exists and its custom event parameters are registered as
  custom dimensions (both console steps). See `features/analytics.md`, `specs/analytics.md`,
  `marketing/utm-conventions.md`.

- _Last shipped:_ **Promotion channels directory** (Content/research, Minor change) —
  `feature/promotion-channels` (**`monkify-meta` only**; no backend/front/LP), **shipped 2026-07-26**.
  Delivered **`marketing/promotion-channels.md`**: the **live-verified, tiered directory of named
  promotion targets** that the **X #76** and **Telegram #77** campaigns both deferred — **55 verified
  entries** (Discord 13 · X accounts 7 · X Communities 8 · Reddit 8 · Kick 6 · Twitch 3 · YouTube 3 ·
  Farcaster 1) plus **26 assessed-and-rejected** records carrying reusable reasons. **Every entry was
  opened live on 2026-07-26; nothing from model memory** — each row records size, a concrete activity
  observable, fit tags, a self-promo rule (`allowed` / `ask an admin` / `banned` / `unknown — ask
  first`, never assumed), a one-line angle and an impersonation note, with an `as of` date and a §6
  re-verification protocol (entries >3 months = leads, not facts). $0 organic; the three places that
  openly sell access sit in a fenced "not endorsed" §7 appendix (none published a price).
  **#76 §4.3 + #77 §4.4 rewritten as pointers** so targets live in exactly one place.
  Minimums met for Discord (13), X (15), Reddit (8) and streamers (12); **Telegram fell short at 6 of 8
  and X Spaces at 0** — both stated in-doc as shortfalls rather than padded, because X has removed its
  Spaces search filter entirely and public Solana Telegram proved to be substantially a coordinated
  pump funnel (four channels, ~710k subscribers, one paid bot). The pass also corrected the from-memory
  names in #76 §4.3: `@solanagaming` and "Solana Meme Coin" hold up, while **`@SolanaInsiders` has been
  dormant since Jul 2025**, **r/CryptoGaming is restricted and near-dead**, and "PLAY SOLANA" is real
  but only 2.3k. Verification is tool-dependent — **X Communities and Kick are reachable only from a
  signed-in browser**; Facebook group search is closed to us entirely. No code, no test, **no
  `features/` doc** (content card — this row is the KB record, matching #76/#77).
  Spec `specs/promotion-channels.md`. Trello #100 ([L3qmI4qj](https://trello.com/c/L3qmI4qj)).

- _Last shipped:_ **Frontend Helius gateway** (Feature, full-stack) — `feature/frontend-helius-gateway`
  (**`monkify-backend`** + **`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-26**.
  The SPA no longer calls a public Solana RPC in any deployed environment. Balances come from a new
  Redis-cached, registered-mint-only **`GET /solana/balance`** (+ uncached `/solana/balance/live`,
  a separate **route** because `AspNetCoreRateLimit` cannot budget a query-string value), gated exactly
  like `/rpc` (Origin allow-list + per-IP rules, **no** launch token, so the wallet-bridge popup still
  works) and served through **`ISolanaService.GetTokenBalance`** — local ATA derivation, **one**
  upstream call, inheriting #62's retry + one-shot fallback + masked logging. The **`/rpc` proxy became
  the primary transport** rather than a 429 fallback (`withFailover` + cooldown deleted), and
  `confirmTransaction`'s **WebSocket became a bounded `getSignatureStatuses` poll** — a socket cannot be
  proxied — with the allow-list narrowed **7 → 3**. Three RPC round-trips were deleted outright: the
  per-bet decimals lookup (now `ActiveGame.tokenDecimals`, threaded explicitly through all four bet
  paths; **missing ⇒ abort the bet**, never guess an exponent), the dead SOL balance read and `sendSol`.
  The balance poll went 10s → env-tunable **30s** and now **stops with `useIdleDisconnect`**.
  `frontBuild` finally emits `VUE_APP_SOLANA_PROXY_URL` (its absence is why the #87 proxy had never
  actually run in production). **No entity/migration/Terraform change.**
  ⚠️ Shipped with a **latent money-bug fix** found during QA: `SpaceStroker.Consumers` cached the game
  types it had just built in memory when seeding Main on a fresh database, so `/games/active-types`
  served a **fully null token block for 12h** — and the SPA silently defaulted mint/receiver to
  `VUE_APP_*` env vars, meaning a paid bet in that window could pay the env defaults instead of the
  operator's registered token and receiver. Also fixed **two tests that were already red on `main`**
  (`DefaultGameParametersTests`, `ChoiceExclusivityFlowTests`). 498 unit + 69 integration green.
  Spec `specs/frontend-helius-gateway.md`, distilled `features/frontend-helius-gateway.md`;
  `features/solana-connection-rpc.md` marked **superseded**. Trello #99 ([8JhYpx52](https://trello.com/c/8JhYpx52)).

- _Last shipped:_ **Idle websocket disconnect** (Minor change, frontend-only) —
  `feature/idle-websocket-disconnect` (**`monkify-front`** + **`monkify-meta`** → `main`),
  **shipped 2026-07-26**. The SPA now closes both game-screen SignalR hubs when nobody is watching, so
  abandoned tabs stop pinning Cloud Run instances: **30 min hidden** or **60 min without input**
  (env-tunable via `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS` / `_INACTIVE_MS`), whichever fires first — a
  visible + interacting tab is never cut. Elapsed time comes from **timestamps** re-evaluated on
  `visibilitychange`/`focus`/`pageshow` + a 30s tick (background timers freeze), and **both hubs carry
  `onreconnecting` idle checks** so `.withAutomaticReconnect()` can't revive a frozen tab's dropped
  socket. Recovery is an explicit `RECONNECT` overlay running the full join path; `bet()` is blocked
  while disconnected; embedded gets a new `idle-disconnected` postMessage; an in-flight mobile wallet
  hop is exempt. **`monkify-backend` untouched**; Minor change ⇒ no dedicated `features/` doc
  (distilled into `architecture.md` + `features/iframe-embedding.md` +
  `features/mobile-wallet-connect.md`) and no integration test. ⚠️ **Shipped with the §13 manual QA
  matrix not yet executed** — lint + build green only. Spec `specs/idle-websocket-disconnect.md`.
  Trello #96 ([fwdi7Fgh](https://trello.com/c/fwdi7Fgh)).

- _Last shipped:_ **Mobile wallet connect** (Feature, frontend-only) — `feature/mobile-wallet-connect`
  (**`monkify-front`** + **`monkify-meta`** → `main`), **shipped 2026-07-25**. Standalone-mobile Solana
  wallet connect via a single **Connect Wallet** modal: **Tier 1 deeplink** (Phantom/Solflare/Backpack,
  `signAllTransactions` + self-broadcast, round-guard-before-broadcast) with an **iOS in-place hop /
  Android connect-method toggle** (deeplink + new-tab disclaimer, or open-in-wallet-browser) and
  **in-app-browser auto-connect**; **Tier 2 open-in-app** (Coinbase/Trust/OKX/Bitget). `localStorage`
  return stash + `?type=` return kill the Android new-tab connect loop; the sign-resume waits for the
  live `/Bets` stream; balance/identity read the effective wallet. No backend change; frontend feature
  ⇒ no integration test (device QA on `app.dev.spacestroker.com`). Spec `specs/mobile-wallet-connect.md`,
  distilled `features/mobile-wallet-connect.md`. Trello #92 ([Csb9U6bn](https://trello.com/c/Csb9U6bn)).

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
  `DefaultGameParametersTests`). Spec `specs/main-production-operator.md` (the distilled `features/`
  doc was deleted when operators were removed); Trello #60 ([bdkTZJOL](https://trello.com/c/bdkTZJOL)).
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
  tests green.** Spec `specs/payment-methods-refactor.md` (the distilled `features/` doc was deleted
  when operators were removed). Trello #38
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
  games. Trello #31 ([JZcMz3ma](https://trello.com/c/JZcMz3ma)). See `features/admin-page.md`.
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
  descoped to a separate story. Trello #20. Spec `specs/rg-pre-bet-hook.md` — **the hook was deleted
  outright when operators were removed**.)
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
- A **bet** is placed via `POST /games/{id}/bets` — a **public** endpoint; there is no launch token
and no operator. The handler validates the on-chain Solana payment signature, persists the bet, writes
the `Debit` placement row to the single `TransactionLog` ledger, and publishes a `BetPlacedEvent`.
- **Games** progress through a state machine driven by events on the **MassTransit Postgres SQL
transport** (no broker) consumed in `SpaceStroker.Consumers`: `BetPlaced → RunGame → RewardGame`, with
`RefundInvalidBet` for failures.
- The **typer** (`SpaceStrokerTyper`) runs the game: it emits random characters over **SignalR** until
a bet's choice appears; winners are then rewarded via **Solana** SPL token transfers.
- **PostgreSQL** (EF Core / Npgsql) is the store. **Redis** caches active parameters + global stats,
  holds single-use wallet-proof nonces, and backs SignalR. **Hangfire** (on PostgreSQL) schedules
  lottery games.
- **Frontend** (`monkify-front`) is Vue 3 + TypeScript: composables in `src/hooks`, an axios
service layer in `src/services`, and SignalR clients to stream the live typer + bets.
