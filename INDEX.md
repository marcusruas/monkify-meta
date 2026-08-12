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
| `features/remove-operators.md` | **The deployment is the operator.** Deletes the whole multi-tenant apparatus — `Operator`/`OperatorToken`/`OperatorPlayer`, the `X-Launch-Token` surface, the off-chain `Standard` payment method + `PaymentMethod` enum, the per-operator RG pre-bet hook, `ISecretProtector`, operator branding and the FX/display-currency stack. Everything token/game/admin now reads from **`GeneralSettings:Operator`** (`Token` + `AdminToken` + `DefaultGames`, seeded create-only by name); player endpoints are **public** and identity is the **connected wallet** (`Player`), with the display-name write ed25519-signature-gated on a purpose-scoped nonce. **One ledger:** `CryptoTransactionLog` merged into `TransactionLog` (nullable `BetId`, `RelatedSignature`; `ExternalTransactionReference` → `Signature`). Greenfield migration. 492 unit + 77 integration green | Shipped              |
| `features/provably-fair.md` | Commit-reveal `ServerSeed` (committed on the first bet, revealed after `Ended`), portable HMAC-SHA256 keystream RNG + labelled fallback, Stake-style client seed + `Bet.Nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /games/{id}/verification` + offline `public/verify.html`; terminal shows the `> SERVER SEED:` reveal only (the `> SERVER SEED HASH:` commitment line was removed from the terminal 2026-07-25 — it lives in the **FAIR** tab + `verify.html`). **Verification surface superseded by `features/provably-fair-final.md`** | Shipped              |
| `specs/free-bet-mode.md`    | Full implementation spec for dev-only Free Bet Mode                                      | Shipped              |
| `specs/typer-pacing-scalability.md` | Typer wall-clock pacing — consistent duration across configs and servers           | Shipped              |
| `specs/player-customization-modal.md` | Header-gear customization modal: edit display name (all operators) + preferred display currency (regulated only); FreeCurrencyAPI USD-base rates cached in Redis 6h, `GET /fx/rates`, new `GET /players/preferences`, PATCH allow-list, `DisplayName` on bet DTO/SignalR; display-only (settlement stays USD), no migration | Shipped              |
| `specs/game-chat.md`     | Per-game chat over `ActiveGamesHub` (`SendChatMessage` → `{gameId}/ChatMessage`); **v2:** nickname = player display name (client-resolved, crypto must connect to send), DB persistence (`GameChatMessage`) via event→consumer + daily purge (`Chat.RetentionDays`, default 7), client-only 200-char/1s limits, no history load (late joiners see nothing), read-only on end, reset on rollover; right-sidebar `GameChatPanel` | Shipped (v2)         |
| `specs/game-pot-display.md` | Frontend-only: show the `> POT:` line for **all** game types (races + lottery), not just lottery; generalize `currentLotteryPot` → `currentPot` in `useGame.ts` (seed `Σ bets` + lottery base only for lottery, accumulate live via `/Bets`), move the POT `<p>` out of the lottery-only block in `GameTerminal.vue` + a second POT line in the running/ended view (AC #8); reuse `formatAmount`; no backend/DB/SignalR change | Shipped              |
| `specs/characters-away.md`  | Characters Away near-miss reveal. Spec body is the original 3/2/1 pinned-milestone design; **shipped** behaviour (see spec Post-ship notes + `features/characters-away.md`) is a bottom-docked closest-approach row per own choice (KMP `away = L − k`) that climbs + loops every 2s + types out the winner, plus in-game `> POT:` and a backend fallback terminal reveal | Shipped              |
| `specs/audio-overhaul.md`   | Audio overhaul with AI — **(1/3) manifest & Suno-generation spec**. Sound manifest + per-item Suno prompts + the frontend integration contract for cards 2 (#34) & 3 (#35). **Shipped reality differs:** the 3/2/1 proximity "coin" bleeps were dropped (Suno couldn't produce them) → **9 generated mp3s, not 12**, and no proximity audio. See `features/audio-overhaul.md`. Frontend-only; branch `feature/audio-overhaul` | Shipped              |
| `specs/payment-methods-refactor.md` | Removes operator `PaymentModel`; per-game **`PaymentMethod`** (`Standard`/`SolanaToken`); per-operator **registered Solana tokens** (encrypted key) via admin portal; unified `IPaymentMethod` strategy+resolver; ledger split — `Bet`/`TransactionLog` carry `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`. **Shipped** (encryption deviates: AES-GCM/config passphrase, not Data Protection) | Shipped |
| `specs/iframe-embedding.md` | Frontend-only: let a **regulated (Model A)** casino embed the game in an `<iframe>`. New `useEmbed` (`window.self!==window.top` \| `?embed=1`) + `utils/embed.ts` postMessage emitter (`{source:'space-stroker',type,payload}`, `targetOrigin:'*'`, secret-free, USD); embedded 401/expired/inactive-operator emits `token-expired` + light "relaunching" state instead of `/error` (branch in `mainService.ts` + `useOperator.ts`); lifecycle emits `ready`/`game-started`/`bet-placed`/`bet-settled`(bettors-only)/`game-ended`/`resize`(hint) from `useGame.ts`+`App.vue`; fills the frame (`main.css`); permissive `frame-ancestors *` in `nginx.conf`; static `public/embed-example.html` + `EMBEDDING.md`. **No backend.** Branch `feature/iframe-embedding` | Shipped              |
| `specs/iframe-crypto-games.md` | Frontend-only, **stacked on #3**: make paid **`SolanaToken` (on-chain)** bets work inside the #3 `<iframe>`. Extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move into a **same-origin popup** on the game host (route `/wallet-bridge`, `WalletBridgePage.vue`) that `postMessage`s the signature + wallet back (`source:'space-stroker-wallet'`, `utils/walletBridge.ts`); `useGame.bet()` acquires the signature in-frame **or** via popup (`useEmbeddedWallet.ts`), popup opened **synchronously** on click (blocked → visible affordance); relax the `GameConsole` `connected` gate when embedded; #3 lifecycle payloads made **token-native** (never USD). **No backend.** Branch `feature/iframe-crypto-games` | Shipped              |
| `specs/game-type-stats.md` | Per-game general stats per game type (all-time): `GET /games/{parametersId}/stats` → biggest pot, biggest single payout, rounds played, total paid out, hottest choice (most-picked, preset types only); **on-demand aggregate + Redis cache, no new entity/migration** (documented reversal of the maintained-counter idea); lazy-loaded `/game` stats panel; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type; token-gated + operator-scoped | Shipped |
| `specs/wallet-display-name.md` | **Full-stack:** make SolanaToken player display-name/preferences key off the **connected wallet** everywhere. Frontend: reactive per-wallet load (`App.vue`, wallet sent only in a SolanaToken context), customization-modal **gate** ("Connect wallet to customize" when no wallet AND (SolanaToken game OR no usable `sub`)), currency dropdown hidden in a SolanaToken context. **Backend (scope corrected):** prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`), and `GameService.InsertBet` captures the wallet address before nulling `OperatorPlayer`. Child of umbrella #46, escrow-independent | Shipped |
| `specs/lottery-pot-stacking.md` | **Full-stack:** un-won lottery pots **stack** into the next round via a running `Game.CarriedExtra` (preset no-winner draw carries `prev + Σ losing stakes`; under-subscription refunds stakes and carries the base unchanged; a paid win resets to the config base). `pot = Σ bets + LotteryPot + CarriedExtra`. Admin **deactivation transfers** the un-won pot to an eligible same-operator/same-method/same-token active lottery (blocks when none). Both Standard + SolanaToken; player `> POT:` reflects the jackpot; seeded 1-min preset lottery for testing. | Shipped |
| `specs/mobile-layout.md` | **Frontend-only:** responsive **mobile layout** for the two main player pages — the games lobby (`/game`) and the game screen (`/game/play`) — for **portrait + landscape phones**, standalone and embedded. Mobile engages **below `lg` (1024px)** (reuses the `GameMonkey hidden lg:block` boundary, so landscape phones are covered). Lobby: 1-col condensed cards. Game: a `GAME | CHAT·STATS` segmented toggle (monkey hidden; terminal + compact bet panel — no amount field, lever = PLACE BET; chat/stats full-width). Customization modal made mobile-friendly (`verify.html` already responsive). Net-new **mobile crypto wallet-connect** ("open in wallet app" deep-link to the Phantom/Solflare in-app browser; Android MWA deferred). No backend change. | Shipped |
| `specs/lp-space-stroker-relaunch.md` | **Frontend-only (LP):** finish the LP launch pass #49 deferred, on the **arcade-overhaul** redesign — full **SpaceStroker** rebrand (wordmark SPACE·STROKER, kill every `$MONKIFY`), **token-free/USDC** messaging, **remove the Tokenomics section** → a short honest **"no token (yet)"** note (token maybe later, no ticker/CA), roadmap perks reframed future/earned (no holder-gating, no "$MONKIFY live"), hero secondary CTA → community "Join the troop", "Play now" → **flagged placeholder** app URL (real route `/game`), trust points kept (provably-fair + instant on-chain payouts + auto-refund); Edson/art/story/skin unchanged. Reuses branch `feature/rebranding-spacestroker` (reconciled overhaul); no backend/DB/test. | Shipped |
| `specs/lp-redesign.md` | **Frontend-only (LP):** selective visual redesign — adopt the winning pieces of a Claude design mock onto the live LP (`monkify-lp` off `master`), re-implemented in Astro/Vue inside the arcade skin: two-column hero + local LIVE pill, 3-card how-it-works + FAIR PLAY bar, new image-forward `#modes`, restyled EDSON.LOG story, two-column `#token`, header (coin + Modes/Story/Roadmap/Troop + PLAY NOW), footer tagline, simplified/perk-free roadmap, richer per-section art. Skips the fabricated wins/pot ticker + tabbed modes. Preserves brand / token-free / trust / placeholders; no backend/DB/test. | Shipped |
| `specs/masstransit-postgres-transport.md` | **Backend infra:** replace the MassTransit **RabbitMQ** transport with the **Postgres SQL transport** (8.5.4) — no event-contract/handler-logic change. Messaging store = a separate DB **`spacestroker_messaging`** on the same Postgres instance, provisioned by `SpaceStroker.Consumers`' MT migration hosted service at startup; API stays publisher-only. Reuse the game-DB user (needs CREATE DATABASE) via a new `ConnectionStrings:Messaging`; keep the 8 named endpoints; rename publisher `IRabbitMQService`→`IMessageBus`. Bonus: game DB `monkify → spacestroker` + `monkify-network → spacestroker-network`. Drops the local `rabbitmq` container. **Blocks #61.** | Shipped |
| `specs/terraform-gcp-deploy.md` | **Infra/DevOps (all repos, meta-primary):** production **GCP** deploy as Terraform + two local **C# console apps** (deploy `[api\|consumers\|front\|lp]` + `teardown`; read-write DB REPL over both DBs via Cloud SQL Auth Proxy) — all in `monkify-meta/deploy/`. API + Consumers → **Cloud Run** (API autoscale min≥1; Consumers CPU-always-on, `var.consumer_count`=1, internal ingress — hosts `/hangfire` + owns migrations); front + lp → **GCS + Cloud CDN**; **Cloud SQL 16** (1 instance, DBs `spacestroker` + `spacestroker_messaging`) + **Memorystore Redis 7**, **private-IP only** (VPC + NAT + connector); **Artifact Registry**; external **HTTPS LB + Cloud Armor**; **Secret Manager** synced from a local unversioned `secrets.<env>.json`; Cloud DNS + managed TLS on new **spacestroker.com**; TF split **persistent** (DNS/domain, `prevent_destroy`) vs **workload** (teardown target, keeps domain). Backend: add `/health` to both hosts + forwarded-headers + config-driven CORS (no game change). Builds on **#64** (Postgres transport, shipped). | Shipped |
| `specs/crypto-commission.md` | **Full-stack:** re-add a house **commission on winnings, crypto (`SolanaToken`) games only** — `0.5%` of **player-funded winnings above each winner's own stake**, with the house-funded lottery base (`LotteryPot + CarriedExtra`) excluded (≡ 0.5% of losing-bet stakes); a winner never nets below stake. Configurable global rate `GeneralSettings.Token.CommissionPercentage` (default 0.5, `0` disables). Realized by crediting winners less on-chain (remainder stays in the token account) — **no new transfer, no ledger row, no migration**. Reward math in `BetDomainService.CalculateRewardForBet`; rate exposed on `GameParametersDto`→`ActiveGame`; UI = **STATS side-panel badge only** (method-aware `0.5%`/`0% HOUSE CUT`). Standard/USD games unchanged. Shipped alongside a pre-existing multi-winner reward-division fix (frozen winner set + paid-winner divisor). | Shipped |
| `specs/refund-safeguards.md` | **Full-stack:** make `NeedsRefunding` a real, self-clearing bet lifecycle + recover stuck money. (1) A **12h Hangfire sweep** (`PendingRefundSweepJob`) publishes `RefundInvalidBetEvent` for every `NeedsRefunding` bet. (2) `RefundInvalidBetConsumer` now sets `Bet.Status = Refunded` on any **successful** refund (all refunds, the 3 inline triggers included → they end `Refunded` not `Made`); failure leaves it `NeedsRefunding` to retry next sweep. (3) New operator-admin `POST /admin/bets/refund { paymentSignature }` marks a bet (found by indexed `ExternalTransactionReference`, operator-scoped) `NeedsRefunding` — blocks `Rewarded`/`Refunded` (double-pay) + free bets, no game-state restriction, idempotent; money moves via the sweep (single executor, ~≤12h). **No schema/migration.** New admin panel in `AdminPage`/`GameTypeManager`. | Shipped |
| `specs/telegram-marketing-campaign.md` | **Content (Minor change), `monkify-meta`-only:** a **$0 organic Telegram launch campaign** for Space Stroker (T-0 = 2026-07-23) as a new `marketing/telegram-launch-campaign.md`. Sibling of the Twitter card #76, adapted to Telegram: **a single community group** (announcements + chat), `[PLAY_URL]` **in-body + inline PLAY NOW button + pin** (bot/admin-delivered, plain-link fallback), **6 required sections** (group description + pinned welcome / message copy / schedule / growth playbook / media direction / **group setup + moderation & anti-scam**), Telegram as the **retention hub with X (#76) funnelling in**. ≥3 teasers + ≥5-message launch sequence + ≥5 first-week; relative-day T-2…T+7; token-free/anti-scam guardrails; existing Edson/arcade assets only. No integration test / `features/` doc. | Shipped |
| `specs/app-design-refinement.md` | **Frontend-only (app):** port the `monkify-lp` arcade design system into the game app `monkify-front` to close the LP↔app quality gap — **chrome overhaul, in-play game screen NOT restructured** (inherits tokens/fonts/buttons only). Translate the LP's OKLCH `@theme` tokens into `colors.css`+`tailwind.config.js` (collapse the 3 drifting blues `#3668d4`/`#5b7fcc`/README + stray `#00b7ff` to one system), re-implement the LP material classes (`.arcade-panel`/`.btn-arcade`/`.chip`/`.crt-screen`/`.pixel-notch`/glows) in a v3 `@layer components`, collapse to **2 fonts** (Press Start 2P + VT323; drop Pixelify Sans **+ Silkscreen** + Courier New), `.btn-arcade` buttons (kill `bg-blue-500`/undefined `.title`), frosted **always-visible** header + coin glow, re-tokenized reactive background + LP richness, LP-quality lobby (keep clip-path cards), re-skinned shared surfaces. **Preserves** the operator-themable `--color-secondary` hook. No backend/DB/test. **Shipped** — see `features/app-design-refinement.md`. | Shipped |
| `specs/twitter-marketing-campaign.md` | **Marketing content (Minor change), `monkify-meta` only:** a **$0-budget organic Twitter/X launch campaign** for **@SpaceStroker** ahead of the **2026-07-23** crypto launch. Deliverable `marketing/twitter-launch-campaign.md` — account **bio** (143 chars), ready-to-paste **tweet copy** (4 pre-launch teasers, a 6-tweet launch-day thread, 6 first-week posts incl. an Edson lore thread), a **relative-day schedule** (T-2…T+7), an **organic growth playbook** (cold-start seeding, named Solana communities + spoofing warning, reply-first tactics, hashtags, post times, free KOL outreach + a precedent scan), and **per-tweet media direction** reusing existing Edson/arcade assets. English-only; **link-in-bio + first-reply** with a `[PLAY_URL]` placeholder; token-free USDC/Solana, provably-fair, "No token. Yet." + anti-scam, **no-financial-promise / no-fake-metrics** guardrails. Sibling of Telegram #77. No code; **no integration test / `features/` doc** (content-only). | Shipped |
| `specs/lp-final-adjustments.md` | **Frontend-only (LP), `monkify-lp` (`master`) + `monkify-meta`:** final pre-launch honesty/polish pass on the Space Stroker LP. Correct the 3 factually-wrong `GameModes.vue` mode texts vs the seeded **Main** types (Sprint = one 6-char **alphanumeric** race, not "6 letters · 6 numbers"; Derby = back 1 of **8** runners, not the invented "rabbit/monkey/jaguar/falcon"; Millions = **once-a-day/24h** lottery, not "every 4h"); rename to **Cosmic Sprint / Beast Mode / Space Jackpot**, wire new `cosmicsprint.gif` + `spacejackpot.gif` (+ posters, uniform square `object-cover` frames); **remove** the `#token` "TBD coin" markers (visual final); Header logo `href="#"`→`#hero`; **fill** PLAY_URL (`app.spacestroker.com`) + X/Telegram, **remove Discord**, `site`=`spacestroker.com`; audit roadmap HI-SCORE as goals + `/placeholder.png` unreachable. No backend/front/DB/test. | Shipped |
| `specs/light-graphics-mode.md` | **Frontend-only (app), `monkify-front` only — LP explicitly out of scope:** fix the near-unplayable frame rate when Chrome's **hardware acceleration is off**. (A) **Delete `NebulaField.vue`** outright — 5 clouds of 40–82vw radial gradient carrying `blur(24–54px)` **and** `mix-blend-mode: screen`, restyled every rAF tick; confirmed live as the dominant cost. (B) A **light graphics mode** with **no UI whatsoever** — resolved by **mobile short-circuit** (`< lg`/1024px ⇒ always light) → **WebGL unmasked-renderer probe** (SwiftShader/llvmpipe/software ANGLE) → **~2s rAF FPS sample**; both inconclusive ⇒ normal. New `usePerfMode` composable + a `html.light-graphics` root class; strips all `backdrop-filter`/`blur()`, the infinite animations (`bar-scan`/`pulse`/`blink`/`crt-flicker`/`wobble`, LP `drift-*`/`coinFloat`/`marquee`/`sectionBgScroll`), `mix-blend-mode` overlays and multi-layer glows; background **keeps drifting, heavily reduced** (cap 8, ~15 FPS, no bg scroll, torpedoes off). Mascot stays animated; `.section-vignette` stays (confirmed free). Includes an rAF efficiency audit (per-frame style-object rebuilds, `right`→`transform` on the torpedo blast). **`monkify-lp` is out of scope** (its findings are parked in the spec's §9 for a future card). **No backend/DB/unit/integration test** (bug fix / QOL). | Ready |
| `specs/provably-fair-final.md` | **Full-stack:** finish provably fair — put verification **in the app** and make the endpoint cheap. New lazy **`FAIR`** tab in `GameSidePanel` (mobile: inside the `CHAT·STATS` segment; **hidden when embedded**), **locked until `Ended`**: two **instant** browser checks (commitment `SHA256(serverSeed)==hash` + own bet seed `HMAC(clientSeed, nonce)`) and an **opt-in** `RECOMPUTE OUTCOME` in a **cancellable Web Worker** with progress, plus **COPY VERIFICATION JSON** and **rollover retention** of the last ended round; **`public/verify.html` kept** (the spec said delete; 3-way duplication remains). Backend: persist **`Game.CharactersTyped` (`int?`)** at run end so `winnerDecidedByFallback` is **derived, never replayed** (the handler's full ≤5M-character replay is deleted), and **cache** the payload (`GameVerification:{gameId}`, 12h) — written only at a **final** status, with the operator check kept **before** the cache read. Reach = current round only; no round history, no tokenless verification. | Shipped |
| `specs/terraform-development-deploy.md` | **Infra/DevOps (Minor change), `monkify-meta` only:** make a **development** teardown wipe everything — after `teardown --env development` nothing Terraform created remains in GCP, only the **GoDaddy registration of `spacestroker.com`**. Because dev and prod share **one GCP project**, this first makes the stack **environment-scoped**: `name_prefix = spacestroker-<envCode>` derived from `secrets.<env>.json` → `environment` (`development→dev`; the full word breaks the SA 30-char and VPC-connector 25-char caps), per-env **state bucket** (`spacestroker-tfstate-<envCode>-<projectId>`), per-env **Cloud SQL + Memorystore**, per-env **Secret Manager ids / Artifact Registry repo / GCS buckets**, per-env `<env>.tfvars.json` (the `.auto.` name is dropped — it cross-contaminates envs) and `TF_DATA_DIR`, plus **fail-fast id-cap validation** (`validate` command). Teardown then destroys workload → persistent (zone + state bucket, via a **local state migration**; `prevent_destroy` can't be variable-driven ⇒ protected/ephemeral resource pairs on `protect_persistent`) → orphan-secret sweep → local generated-file cleanup, guarded by **hard-block on production** + **refuse the registrable apex zone**; GoDaddy is only warned about, never called. `deploy --env development` **auto re-bootstraps** the persistent layer. No backend/front change ⇒ **no unit/integration test**. | Shipped |
| `specs/games-history.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** a standalone **Games History** page (`/game/history`, reached from a new header **menu button** that also holds Settings; audio + wallet stay on the header). New read-only `GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet` → `PaginatedList<GameHistoryDto>` — **one game type at a time** (dropdown), **only `RewardForWinnersCompleted`** rounds, newest first, `pageSize`≤30, a **"mine only"** toggle. Rows: ended date · type name · winning choice · pot (`Σ paid bets` + lottery base + carried extra) · winner count · short `serverSeedHash` · nullable **own-bet** block (choice/stake/won/payout) — **no character stream**. Money rules mirror **game-type-stats** (free bets excluded, single currency per type, `useDisplayCurrency`); "mine" identity resolves like the prefs handlers (`wallet` ?? token `sub` → `OperatorPlayer.Id` → `Bet.OperatorPlayerId`). **Public page cached 5 min in Redis** per operator+type+page+size; caller columns joined **live**. **Verify = link-out only** to the existing `/verification` payload + `public/verify.html` (no in-app replay). **No entity/migration.** New feature ⇒ mandatory happy-path integration test + handler/service unit tests. | Shipped |
| `specs/choice-exclusivity.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** in **free-text games** (empty `PresetChoices`, race or lottery) each typed sequence is **exclusive** to the first player who bets it in a round; a different player who takes the same normalized sequence is rejected (and **auto-refunded** for a paid `SolanaToken` bet, since the on-chain debit precedes validation — free/Standard are reject-only). **Preset games unchanged** (shared choices). Enforced at two layers: an app-level cross-player check in `RegisterBetHandler.ValidateBetParameters` (routed through the existing `RefundInvalidBet` helper + new `ErrorMessages.ChoiceAlreadyTaken`) **and** a DB unique index on a new NULL-for-preset shadow column `Bet.ExclusiveChoice` (`(GameId, ExclusiveChoice)`; NULLs distinct → preset unaffected), with `GameService.InsertBet` surfacing the 23505 race-loser as `ChoiceAlreadyTaken`. Also fixes the raw-vs-normalized per-player duplicate query. Frontend pre-pay guard in `useGame.bet()` blocks a taken sequence before payment (best-effort; backend authoritative). Winner becomes singular ⇒ winner-takes-all (no reward-math change). New migration `AddBetExclusiveChoice`. New feature ⇒ mandatory happy-path integration test. | Shipped |
| `specs/standardize-scrollbars.md` | **Frontend-only (`monkify-front` + `monkify-lp` + `monkify-meta`), QOL/styling:** replace the raw default OS/browser scrollbars on most scrollable surfaces (reported case: the in-game chat panel) with **one minimal, on-brand scrollbar** applied app-wide as a fallback — thin **8px**, transparent track, subtle neutral thumb `rgba(255,255,255,0.18)`→`0.30` hover, **WebKit** (`*::-webkit-scrollbar*`) **+ Firefox** (`scrollbar-width/color`). Global rule in `monkify-front/src/main.css` and `monkify-lp/src/styles/global.css`; two preserved exceptions in the front — the game terminal keeps its green `.terminal-scroll` (Firefox parity + `border-radius:0` added) and `.side-tabstrip` stays hidden (both win by specificity). No JS/backend/env/token/migration. Not a new feature ⇒ no integration test. Trello #91 ([cGrBi584](https://trello.com/c/cGrBi584)). | Shipped |
| `specs/fair-stats-menu.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** pull **STATS** and **FAIR** out of the `/game` `GameSidePanel` tabs into the **header menu**, leaving the docked cabinet **chat-only** (chrome kept, tab bar dropped; mobile segmented `GAME \| CHAT·STATS` → **`GAME \| CHAT`**). **Stats** becomes a dedicated page (`/game/stats`): a **new GLOBAL section** + the existing per-type stats behind a game-type dropdown. Global = new read-only **`GET /games/stats`** → operator-scoped, **single dominant currency** (USDC on prod Main), on-demand + Redis-cached (120s), **no entity/migration** — mirrors `GameStatsService`; today's headline tiles aggregated across types **+ 6 new tiles** (total volume wagered, unique players, total bets placed, winners paid, biggest win multiplier, most-played type); free bets excluded from money, only `RewardForWinnersCompleted` rounds. **Fair** becomes a full-screen **modal + overlay** in `GameHeader` (like the settings modal): an always-on *how-to-verify* explainer + the existing current-round `GameFairPanel` checks (populate only on `/game/play` after the round ends; off-game → explainer + "watch a round to verify it"). Both **hidden when embedded**. New feature ⇒ mandatory happy-path integration test (`GlobalStatsFlowTests`, dedicated operator for determinism) + service/handler unit tests. Trello #89 ([w9jCXK0N](https://trello.com/c/w9jCXK0N)). | Shipped |
| `specs/windowed-stats-query-performance.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** the Stats page stops materialising **every completed round ever** in application memory. `GlobalStatsService` loaded all completed round ids → every bet of those rounds → every credit log; `GameStatsService` chained **three** such loads per type. The fix has two halves and the visible one is **not** the load-bearing one: **aggregation moves into Postgres** (~8 bounded `count`/`sum`/`max` queries, each returning at most one row per active type), and on top of that a **Weekly / Monthly / All-time** selector — **rolling** 7/30 days, not calendar (no timezone or week-start to commit to; windows never collapse on the 1st of a month). **Defaults to All time on both page and API**, so no shipped surface changes silently and the trust tiles keep their real numbers; an unrecognised `window` value coerces to `all` rather than 400ing, mirroring the history endpoint's out-of-range page. One page-level selector drives **both** sections (global + per-type), which makes "hottest choice" hottest-**in-window**. Riding along from the same 2026-08-04 query audit: the **`OrbitReconciliationJob` N+1** (one `SUM` per `OrbitBalance` row, 4×/day, linear in the enrolled player base → one grouped **LEFT JOIN**; the LEFT is load-bearing — an INNER would silently skip a balance row with an empty ledger, which is exactly the drift the job exists to catch), **keyset pagination** replacing `OFFSET` on games history (measured 45ms → 0.8ms at 1M rows), an **upper clamp on `pageNumber`** that also bounds unbounded `GamesHistory:{id}:{page}:{size}` cache-key growth, **in-process single-flight** on the stats keys (accepted limit: one aggregation per Cloud Run instance, not per deployment), and `CancellationToken` threaded through **twelve** heavy reads. ⚠️ **Branch is `feature/query-indexes`, not `feature/<slug>`** — it also carries Trello **#133 (Table indexes)**, and the keyset item depends on an index that migration adds. ⚠️ **Open gate before merge:** that migration's **write cost was never measured** — indexing `GameBets.Status` may forfeit **HOT updates** on the settlement path (before it, neither `Status` nor `UpdatedDate` was indexed, and `UpdateBetStatusWithoutSaving` touches only those two) to buy ~80ms/day from a twice-daily sweep; if net-negative, that one index is dropped. Code exploration also found `GameStatsPanel.vue` is **dead** (unreferenced since `fair-stats-menu`) and `Game.EndDate` is **nullable on completed rounds**, so the window filter must honour the `CreatedDate` fallback without an unsargable `COALESCE`. New feature ⇒ mandatory happy-path integration test. **Two things changed between spec and ship:** keyset pagination was **NOT** implemented (it needs a cursor and the endpoint is page-number addressed — only the cache-key bound landed, the ~45ms deep page remains), and the write gate **dropped `GameBets(Status)`** after measuring bet settlement go from **92% HOT to 0% HOT**. Distilled: `features/windowed-stats-query-performance.md`. Trello #148 ([UhmNIabO](https://trello.com/c/UhmNIabO)) + #133 ([hSF7flvu](https://trello.com/c/hSF7flvu)). | Shipped |
| `specs/mobile-wallet-connect.md` | **Frontend-only (`monkify-front` + `monkify-meta`) — no backend:** in-place **mobile** Solana wallet connect for standalone mobile web, so a `SolanaToken` bet can be funded **without** being dumped into the wallet's in-app browser. **Single "Connect Wallet" button → one wallet-picker modal** with two tiers: **Tier 1 native deeplink (stay in browser) = Phantom + Solflare + Backpack** (one encrypted `x25519`/`tweetnacl` adapter; `connect` → approve → return, then **`signAllTransactions` → we self-broadcast**), and **Tier 2 one-tap "open in app" = Coinbase + Trust + OKX + Bitget** (browse link into the wallet's in-app browser where `sendToken` runs unchanged; Glow/Ultimate/Exodus dropped — WalletConnect-only). WalletConnect + MWA rejected. New `useMobileWallet` + `mobileWalletProtocol` util + `MobileWalletModal`; the deeplink **return is a page navigation** (often a new Android tab), so **`main.ts` pre-router param capture + `localStorage` pending-hop + auto-resume on boot** (kills the connect loop), the deeplink `redirect_link` keeping **`?type=`** so the return loads the exact game, **round-changed guard before broadcast**, a **wait-for-`/Bets`-stream** on resume so the placed bet shows without a refresh, and a **blockhash-staleness clean-retry**; the session surfaces as the app's **effective `publicKey`/`connected`** (bet gate + `YOU`/customization identity + **balance** unchanged). **Android connect-method toggle** (in-place deeplink *vs* open-in-wallet-browser) + new-tab disclaimer, and **in-app-browser auto-connect**. Session persists until a deliberate **Disconnect**; all mobile connect entry points share the modal. **No new npm dep** (base58 inlined). **Standalone only** (embedded iframe stays on the popup bridge). No backend/DB change; **frontend feature ⇒ no `SpaceStroker.Tests.Integration`** (device QA). Distilled in `features/mobile-wallet-connect.md`. Trello #92 ([Csb9U6bn](https://trello.com/c/Csb9U6bn)). | Shipped |
| `specs/frontend-helius-gateway.md` | **Full-stack (`monkify-backend` + `monkify-front` + `monkify-meta`):** get the SPA off the public Solana mainnet RPC, which is unusable from a production browser (so today the *whole* paid `SolanaToken` path is broken on mainnet, not just the header balance). Wallet-info reads move to a new **`GET /solana/balance`** (+ uncached **`/solana/balance/live`**) on `SpaceStroker.Api` — Redis-cached ~10s per `mint+wallet`, restricted to **active registered `OperatorToken` mints**, gated exactly like `/rpc` (server-side Origin allow-list + per-IP rate rule, **no** launch token, so the wallet-bridge popup keeps working) and served through a new **`ISolanaService.GetTokenBalance`** so it inherits #62's retry + one-shot public fallback + masked logging **and** is coverable by the `FakeSolanaService` harness. The **transaction path stays JSON-RPC over the existing `/rpc` proxy but flips from 429-failover to PRIMARY** (`VUE_APP_SOLANA_PROXY_URL` was never even emitted by the deploy tool, so the #87 proxy has been inert in production); `confirmTransaction`'s **WebSocket is replaced by a bounded `getSignatureStatuses` poll** (a socket can't be proxied), and the method allow-list is **narrowed 7 → 3**. Three RPC round-trips are **deleted**: mint decimals (already on `ActiveGame.tokenDecimals`), the dead SOL `getBalance`, and the token-account lookup (ATA derived locally, 2 calls → 1). Balance poll slowed (env-tunable 30s) and gated on `useIdleDisconnect`. Local/devnet keep hitting public directly ⇒ no key, no credits. **No entity/migration/Terraform change.** New feature ⇒ mandatory happy-path integration test (`SolanaBalanceTests`). Trello #99 ([8JhYpx52](https://trello.com/c/8JhYpx52)). | Shipped |
| `specs/promotion-channels.md` | **Content/research (Minor change), `monkify-meta` only — no code:** the deliverable `marketing/promotion-channels.md`, a **live-verified, prioritized directory of named promotion targets** (the crypto / gambling / web3-gaming / meme communities, servers, subreddits, creators and X Spaces hosts to actually show up in) — the research that the **X (#76)** and **Telegram (#77)** campaigns both deferred in their "where the audience is" sections. **Hard rule: every entry opened live at write time, nothing from model memory**; unverifiable ⇒ dropped or flagged `⚠ unverified`; per-entry `as of` date + a §6 re-verification protocol so the list decays honestly. Per entry: link · size · activity signal · fit tags · **self-promo rule** (`allowed`/`ask an admin`/`banned`/`unknown — ask first`) · tier · one-line "the move here" · impersonation note. **T1** ~10–15 start-here · **T2** ~20–40 · **T3** assessed-and-rejected **with the reason**; per-platform minimums (≥8 TG, ≥8 Discord, ≥6 X, ≥5 subreddits, ≥8 streamers/Spaces) with any shortfall stated, never padded. Every platform the card names gets entries **or** an explicit "assessed — not worth it" verdict (incl. Facebook). $0 organic, with openly-published paid rates fenced into a "not endorsed" appendix. Positioning = **web3 dapp on Solana, not a licensed gambling operator**; **no platform-ToS/compliance research** (user decision, risk accepted). Also repoints #76 §4.3 + #77 §4.4 at the new doc. No integration test / `features/` doc (content-only). **Delivered 2026-07-26: 55 verified entries + 26 assessed-and-rejected; Telegram (6/8) and X Spaces (0) stated as shortfalls rather than padded.** Trello #100 ([L3qmI4qj](https://trello.com/c/L3qmI4qj)). | Shipped |
| `specs/analytics.md` | **Frontend-only (`monkify-front` + `monkify-lp` + `monkify-meta`) — no backend:** install **GA4** across both properties so we can answer "how many came, from where, and did they play?". **One property / one data stream / one measurement id** for `www.spacestroker.com` + `app.spacestroker.com` — same registrable domain, so the `_ga` cookie is shared and the LP→app hop stays **one session** (a bet is attributed to `utm_source=x`, not to `referral/www`); raw `gtag.js`, **not** GTM. The id rides the existing **`frontBuild`** block (`VUE_APP_GA_MEASUREMENT_ID` + `PUBLIC_GA_MEASUREMENT_ID`) which `Deployer.BuildStaticSite` already writes into **both** repos ⇒ **no deploy-tool code change**; **empty id ⇒ no script, no cookie, no banner** (local/docker/dev-GCP never report). **Consent Mode v2 default `denied`** + an equally-weighted Accept/Reject banner on both properties, choice in an **`ss_consent` cookie scoped to `.spacestroker.com`** so it carries across the hop, plus a new LP **`/cookies`** notice page (the first link the LP footer has ever had). Events: LP `cta_play_click` via a **delegated document listener** (the hero buttons are un-hydrated Astro islands — a Vue `@click` would silently never fire), app `wallet_connect_attempt/success/failure`, `bet_placed` (**count only** — instrumented at `useGame.submitBet`, the single choke point every bet path funnels through), `game_type_selected`, manual SPA pageviews. **No amounts, wallet addresses or player identifiers ever leave the browser.** Fully **off when embedded**, on the `chromeless` routes and on the static `verify.html`/`embed-example.html`. `@vercel/analytics` removed (inert since the LP moved to GCP). Level **Feature**, but the integration test is **waived** (zero backend code) — `features/analytics.md` still required at ship. Trello #101 ([Hbfp779A](https://trello.com/c/Hbfp779A)). | Shipped |
| `specs/teardown-resume.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no backend/front:** make `spacestroker-deploy teardown` **resumable**. Today `Deployer.DestroyWorkload()` runs a **full `terraform apply`** before `destroy` just to clear Cloud SQL `deletion_protection`, so re-running a teardown that failed part-way **re-creates everything the failed pass destroyed** (observed 2026-07-26: rebuilt Redis + Artifact Registry, then died on the image the same pass had deleted, never reaching `destroy`). Fix, all in the C# tool: (1) a **targeted** apply on `module.data.google_sql_database_instance.postgres` only — a **state-only** update, since that is the *Terraform-level* guard, not `settings.deletion_protection_enabled` — **skipped entirely** when the instance is no longer in state (`terraform state list` probe, **fail-closed** on a non-zero probe); (2) a **bounded blind retry** over `destroy` (**3 passes, 60s/120s**, any non-zero exit — the 3 known ordering failures each clear once the blocking resource is really gone, and `ProcessRunner` doesn't redirect stderr); (3) **leftover classification decides the exit code** — empty ⇒ 0, only `module.network.*` (VPC/subnet/PSA, all zero-cost, held by the async Google-managed `serverless-ipv4-*` reservation) ⇒ **0 + loud warning**, anything billable ⇒ **1** with the addresses listed; (4) a **`--purge` stop-guard** that refuses to destroy the state bucket while any workload state remains (it would orphan those resources untracked). Decisions extracted into a pure `TeardownDecisions` class covered by a **new `SpaceStroker.Deploy.Tests`** xUnit project (16 tests — the only automated tests in the deploy stack). **No Terraform change**; `deploy`-side "push images before apply" stays a separate card. Trello #98 ([7a9aYZiG](https://trello.com/c/7a9aYZiG)). | Shipped |
| `specs/static-site-status-codes.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no app code:** both static sites served the right HTML with the **wrong HTTP status**. One hardcoded line (`not_found_page = "index.html"` in the shared `static_site` module) made **every** `app.spacestroker.com` deep link return **404** while rendering the SPA, and made `spacestroker.com/<unknown>` return the **LP homepage** with a 404 instead of the branded `404.html` that **already builds and already deploys** (`monkify-lp/src/pages/404.astro` — live at `/404.html`, just unreachable). Cosmetic to a human, broken for everything that reads the status: uptime monitors on deep links, Discord/X link unfurls, crawlers, and a missing JS chunk that returns `text/html` instead of a clean 404. Fix is **two different mechanisms on purpose**: the SPA fallback moves to the **load balancer** (a GCS website config can never return 200) as a `custom_error_response_policy` on a **`/*` path rule** rewriting 404 → `/index.html` + `override_response_code = 200`, while `/js/* /css/* /img/* /media/*` get a **more specific, policy-free** path rule so real asset misses still 404 — relying on documented **longest-path-first** matching. ⚠️ The refined card's carve-out design was **wrong** and is corrected in the spec: an omitted `path_rule` policy **inherits** `pathMatcher.defaultCustomErrorResponsePolicy`, so the policy must live on the `/*` rule with matcher + url_map defaults left empty. The app bucket drops `not_found_page` entirely (keeping `main_page_suffix`); the lp bucket points it at `404.html` and **keeps** the 404 status (one-pager, no client routes to rescue). `www.` inherits via the shared `lp` path matcher. **Config only — never applied or `terraform validate`-ed** (no terraform in the authoring environment); takes effect at the next `apply` + CDN invalidation. Cache-Control is a **companion card** ([bSSvCfKF](https://trello.com/c/bSSvCfKF)). Trello #102 ([mkogi4tR](https://trello.com/c/mkogi4tR)). | Shipped (not yet applied) |
| `specs/static-site-cache-control.md` | **Infra/DevOps (Minor change), `monkify-meta` only — no app code, no Terraform:** both static-site buckets serve **every** object with the GCS default `public, max-age=3600`, because `Deployer.UploadAndInvalidate` uploads with a bare `gsutil -m rsync -r -d` and sets no metadata. That caches `index.html` (which must never be cached) for an hour and immutable hashed chunks for only an hour — and since `-d` **deletes** the previous build's chunks, a returning player boots a stale HTML naming files that no longer exist. Fix, all in the C# tool: a new pure **`StaticSiteCache`** class turns the built `dist` listing into an **ordered plan of gsutil invocations** — (1) per-hashed-prefix rsync **without `-d`** at `max-age=31536000, immutable` (front `js/css/img/media`, lp `_astro`), so the previous build's chunks are **retained** for a session that lazily fetches one (`GameFairPanel.vue:228`'s verify Web Worker is the live case); (2) a root rsync **with `-d`** at `no-cache`, its single `-x` alternation shielding the asset prefixes **and** dropping `*.map` (6 public sourcemaps stop being uploaded); (3) **`setmeta` convergence passes** per tier — required because `rsync` compares checksums, **not metadata**, so ~19 MB of already-uploaded `img/`+`media/` would otherwise stay at 1h forever. Assets upload **before** the HTML that names them, and every upload-time header is **at least as conservative** as its final value, so an intermediate window can only under-cache. Tier rule tests `.html` at **any depth** (the lp's `/cookies` page builds to `cookies/index.html`); an unknown file or directory falls to the explicit 1h tier. Fonts stay 1h (**unhashed** ⇒ immutable would strand a swap for a year). Retained assets are **pruned automatically after 24h** via per-deploy build manifests (`gs://<bucket>/_builds/<utc-timestamp>.txt`, shielded from the root `-d`): a **GCS lifecycle rule cannot express this** because what must be measured is **retirement**, not object age — an image unchanged for months would be reaped the instant it left a build. The window derives from the **open-tab** horizon (`useIdleDisconnect` idles out at 30-60 min), not from `Cache-Control`; the prune **fails closed** at every read. The `/*` CDN invalidation is kept verbatim. `StaticSiteCacheTests` + `BuildManifestTests` in the existing `SpaceStroker.Deploy.Tests` (76 total). Trello #103 ([bSSvCfKF](https://trello.com/c/bSSvCfKF)). | Shipped (not yet applied) |
| `specs/robots-txt.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-lp` + `monkify-meta`:** give the three public production hosts a deliberate crawler policy. Today only the LP has a `robots.txt`; **`app.spacestroker.com` answers `/robots.txt` with the SPA's HTML under a 200** (the #102 `/*` error policy rewrites the bucket 404 → `index.html` + 200, and RFC 9309 skips unparseable lines ⇒ **effectively allow-all**), and `api.spacestroker.com` **404s** — also read as allow-all. Fix: **app** gets `public/robots.txt` = `Disallow: /` **plus `X-Robots-Tag: noindex`** on the front backend bucket (`Disallow` alone never de-indexes, and blocking the crawl stops Google ever reading a `noindex` meta) — one line on `module "front_site"`, `lp_site` deliberately untouched; **API** gets `GET /robots.txt` on the existing `HomeController` (`[Route("/")]`, outside `ProtectedPrefixes` ⇒ no launch token, no Terraform, works locally) — ⚠️ needs its own **`[Produces("text/plain")]`** to override the class-level `[Produces("application/json")]`; **LP** keeps `Allow: /` + its sitemap but blocks **AI training** crawlers (`GPTBot`, `ClaudeBot`, `Google-Extended`, `meta-externalagent`, `CCBot`, `Bytespider`) while leaving **AI search/answer** crawlers (`OAI-SearchBot`, `PerplexityBot`, `Claude-SearchBot`, …) and **all link-preview agents** (`facebookexternalhit`, `Twitterbot`, `TelegramBot`) allowed — every token verified live against vendor docs 2026-07-27; `anthropic-ai` **dropped** (undocumented), `Applebot-Extended` **deferred** (Apple's page unretrievable). No `Disallow: /404` (it would hide the LP 404's own `noindex`). **No entity/migration/SPA/LP-content change.** Dev environment **out of scope** (it keeps serving prod's `Allow: /`). `terraform apply` is an ops step. Also bundled at ship time (out of spec, by explicit decision — spec §10.5): the **flaky-integration-test fix** — `GlobalStatsFlowTests` + `GamesHistoryFlowTests` seeded **three** winning-eligible preset choices while paying into only **two**, so ~1 round in 3 legitimately produced no *paid* winner ⇒ `WinnersPaid`/`WinnerCount` = 0. A control run on clean `origin/main` reproduced it, proving it pre-dated the card; **not** a timing race. Fixed by dropping the unbacked `donkey` choice from both seeds. Trello #105 ([6aOoMq2n](https://trello.com/c/6aOoMq2n)). | Shipped (not yet applied) |
| `specs/player-lockouts.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** players are intermittently locked out of `app.spacestroker.com` by a Cloud Armor `429` returned **before the app runs** (Armor denials carry no CORS headers, so the browser reports a CORS error and the app "looks broken"). Root cause confirmed from prod LB logs: **143 API requests in ~3 min** against a 100/60 s limit, from **17 document loads across ~5 tab contexts** (three tabs opened 12 ms apart), with `/players/preferences` at ~2.3/load and `/games/active-types` at ~2.0/load — bursts as tight as **seven identical GETs in 14 ms**. Two compounding causes, two fixes. **(1) Frontend:** a module-local **`singleFlight(key, fn)`** in `mainService.ts` — while a request for a key is in flight every later caller receives that same promise; the entry is deleted in `finally`, so it is **not a cache** (nothing survives across time ⇒ no stale value can reach a bet, payout or refund). Applied via an **allow-list of five** boot-path functions (`getGamesTypes`, `getPlayerPreferences`, `getFxRates`, `getOperator`, `getDefaultLaunchToken`); the other eight are deliberately untouched — a blanket GET dedupe was **rejected** because it would collapse `getTokenBalance(live:true)`, which exists precisely for the moments that must not be stale. Keys include params so `getPlayerPreferences(wallet)` and `getPlayerPreferences()` stay distinct (**different players**). The measured burst is `App.vue:204-213` + `useGameChat.ts:43-51` firing on the **same wallet-connect tick** (`main.ts:107` sets `autoConnect: true`). **One file changes.** **(2) Terraform:** the priority-1000 Armor rule changes `action` **`rate_based_ban` → `throttle`**, threshold **100 → 600**/60 s, and `ban_duration_sec = 300` is **deleted** — the incident's damage came from the *ban*, not the threshold (static assets kept returning 200 so the shell rendered, every API call 429'd, and each reload **renewed** the ban). ⚠️ `throttle` is **unverified** — `terraform plan` gates the merge. **Single-flight is per-tab and cannot dedupe across tabs**, which is why the Armor change is not optional. Split out: **#107** retry affordance, **#108** IP+JA4 keying. **Measured after shipping: 4 → 3 preferences calls on a SolanaToken game page with a wallet; the lobby never duplicated (1:1). The spec's "~143 → ~85-95" estimate is WITHDRAWN — the real saving is ~1 request per game-page load (~10%), because the "every boot doubles its calls" premise was false. The Armor `throttle` change is the load-bearing half and is NOT yet applied.** Trello #104 ([SpTvJGi9](https://trello.com/c/SpTvJGi9)). | Shipped (Terraform not yet applied) |
| `specs/entry-path-recovery.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** a transient API failure during boot strands a standalone player on `/error`, a page whose whole body is `<p>This game is unavailable.</p>` — no retry, no reload, no link — and because Cloud Armor's `throttle` enforces across the **whole 60 s interval**, a manual reload inside a saturated interval re-fails *and* spends another request. Fix = **classify + recover in place**, not just a retry button: a pure `isTransient(error)` + a pure jittered delay schedule (**3 retries at ~5 s / ~20 s / ~45 s, ±20 %, ≈70 s total — deliberately longer than the 60 s enforcement window** so one attempt lands in a fresh interval; jitter stops everyone behind one VPN exit retrying in lockstep) driven by a `useBootRecovery` module-singleton that shows a one-line `> RECONNECTING…` state (no countdown/attempt counter — that invites the reload we're preventing). ⚠️ **The load-bearing finding: the 429 does NOT arrive as a status.** Armor denies at the edge before the app runs, so the response carries **no CORS headers**, the browser blocks it, and axios sees `error.response === undefined` — which is why the incident was reported as "a CORS error". A `status === 429 \|\| status >= 500` classifier would **never fire on the incident**; transient = **no response at all**, or 429/5xx/408. All **three** routes to `/error` hardened (`useOperator` bare `catch` — the one that fired; `main.ts` boot-mint guard; the 401 interceptor, whose `getDefaultLaunchToken` flattened every error to `null` and whose `window.location.assign` full reload would wipe an in-flight back-off ⇒ router navigation via a new `navigation.ts` shim, since the router is never exported). `getGamesTypes` joins the back-off (wrapped **outside** `singleFlight`); prefs/FX deliberately don't. Embedded gets the same classification with **no new postMessage** (`token-expired` is the wrong signal for a 429 — it makes the casino mint a token that cannot help). `/error` always shows RETRY. Plus a **new priority-800 Cloud Armor rule** giving `/health` + `/Hubs/*` a looser ~3000/60 s bucket (a *throttle*, not a blanket `allow` — an exemption would leave `/Hubs/*/negotiate` with no ceiling while #112 then had the in-app limiter under suspicion — since **confirmed and fixed**), the `patchRule` action-change comment, and the `deploy/README.md:117` `production.auto.tfvars.json` → `production.tfvars.json` correction. No entity/migration/backend; `monkify-front` has no test runner and none is added ⇒ the two decision-bearing functions must be **pure and exported** so a future runner can pin them. `terraform apply` is an ops step. Trello #110 ([KvfUl6ff](https://trello.com/c/KvfUl6ff)). | Shipped |
| `specs/rate-limiter-keying.md` | **Minor change, `monkify-backend` + `monkify-meta` — no frontend:** the in-app AspNetCoreRateLimit limiter keys **every rule on the load balancer**, so all rules are **one global bucket shared by every player on earth** — live in production. `Program.cs:135-141` sets up `UseForwardedHeaders` but never sets **`ForwardLimit`**, and ASP.NET's default of `1` walks one hop from the **right** of `X-Forwarded-For`, which is the proxy that *added* it. Measured in prod 2026-07-27 (probe deployed + reverted in `28a5c9c`): `raw=[<client>,34.120.43.169] hops=2 resolved=34.120.43.169 realIp=[]` on every path. Worst case live: `*:/solana/balance/live` at **20/min globally**, called once per player per round on bet placement + win settlement. Three fixes: **(1)** `ForwardLimit = 2` as a hardcoded constant (a knob is a foot-gun — `null` or a large value trusts the attacker-controlled **leftmost** entry; `RealIpHeader: "X-Forwarded-For"` is **rejected** for the same reason). **(2)** Move `app.UseIpRateLimiting()` from `:143` to **after** `app.UseCors()` at `:151` — the limiter short-circuits a 429 with no `Access-Control-Allow-Origin`, so the browser reports a **CORS error**, indistinguishable from an outage. **(3)** **Delete** the `api/*` rule, which has never matched a request (`api` parses as the *hostname* in `{verb}:{path}`) — deletion, **not** repair: a repaired catch-all shipped while `ForwardLimit` was still `1` would have made a global 100/min bucket on the boot path and locked out everyone at once, which is why the card's "ForwardLimit first" ordering constraint **dissolves** here. Also `*:/rpc` **120 → 240**/min, because `useConnection.ts:87-112` polls `getSignatureStatuses` **1/s for up to 45 s** as `POST /rpc` (one path ⇒ one counter), so one slow-confirming bet costs ~47 — and a throttled `/rpc` mid-confirm strands tokens that **already left the player's wallet**; the protection given up is ~nil since `/rpc` allows an **absent** `Origin` by design and the `run.app` URL bypasses the LB (own card). Adds `EndpointWhitelist: ["*:/health","*:/Hubs/*"]` (wildcard form **verified** to match, via a catch-all host + a control test). Bet path stays **unlimited** app-side, pinned by an **exact-set** assertion on the rule list. **No entity/migration/frontend change.** **Shipped extra — a SECOND pre-existing bypass:** AspNetCoreRateLimit 4.0.2 defaults **`RealIpHeader` to `"X-Real-IP"`** and prefers it over the connection address; the ALB forwards whatever the client sends, so a caller supplying that header got a **fresh bucket per request** and walked through every rule over the ordinary public path — pinned to `""`, without which `ForwardLimit = 2` would have been trivially defeatable. Also **measured**: the specific `*:/solana/balance/live` rule (20) wins over the broader `*:/solana/*` (120). ⚠️ **Accepted: Helius spend will rise** — today's global buckets accidentally throttle legitimate traffic, and there is no fix that doesn't release it. Trello #112 ([h27WZeQC](https://trello.com/c/h27WZeQC)). | Shipped |
| `specs/gcp-dashboards.md` | **Minor change, `monkify-meta` + `monkify-backend` — no frontend:** production observability as IaC. A tenth workload module `modules/monitoring` (gated on a new `enable_dashboards`, **derived by the deploy tool** from `environment == "production"`, module-level `count` so development state holds no `module.monitoring` address at all) creates **one** dashboard `spacestroker-prod-overview`, two uptime checks (`api…/health`, `app…`, `period = 300s` so they stay a rounding error against the priority-800 Armor bucket), an email notification channel, a `google_billing_budget` (50/90/100%), and **two** `google_logging_metric` over the LB logs — one on `enforcedSecurityPolicy`, one on **`previewSecurityPolicy`** (a `preview = true` rule populates the *other* field, so aiming at the enforced one yields a permanently-zero panel reading as "the WAF never false-positives", when the priority-900 OWASP rule is in preview precisely because it does, on Solana base58 bet payloads). Both metrics label on rule priority + action, bounded cardinality by design. ⚠️ **The card's headline ask is not buildable as asked, three times over.** (1) **Cloud Monitoring cannot chart billing cost at all** — no cost data source, no iframe/embed widget — so "expenses, biggest cost desc" becomes a markdown link to Billing → Reports (already exactly that view) plus the budget, which supplies what Reports cannot: being *told*. (2) **No built-in metric carries a request path** (`run.googleapis.com/request_count` has `service_name`/`revision_name`/`response_code` only) and the `api` path matcher has just a `default_service`, so `matched_url_path_rule` is one constant ⇒ per-endpoint **descoped** to totals + response-code class. (3) **"How many users were rate limited" is undeliverable** — log-based metrics cannot do distinct counts, and per #111 one IP is not one user (a VPN exit is one Armor bucket for everyone behind it) ⇒ denied *requests* + a documented Log Analytics query. **Backend half is load-bearing, not incidental:** `DependencyInjection.cs:126-147` writes Serilog's plain-text template to stdout, so Cloud Run files every line at severity **`DEFAULT`** and an `Error` is indistinguishable from a `Warning` — "count the errors" has nothing to count. A new `SpaceStroker.Common/Logging/GoogleCloudJsonFormatter.cs` (`ITextFormatter`, **no new NuGet** — Common already has Serilog 4.0.0, Infrastructure has Sinks.Console 5.0.1) emits JSON with a top-level `severity` **in Production only**, with the exception appended into `message` so a stack trace is **one** entry rather than N (which also lets Error Reporting see it); `CompactJsonFormatter` is explicitly rejected (`@l` is not read by Cloud Run). That unlocks the built-in `logging.googleapis.com/log_entry_count` (labelled by `severity`) ⇒ **no log-based metric for errors/warnings at all**. Integration suite provably unaffected — `IntegrationTestFixture.cs:136-143` replaces `Log.Logger` after the hosts build. Also: dashboard JSON extracted to `dashboard.json.tftpl` via `templatefile()` so the ~600-line blob stays diff-reviewable; budget + channel **skip on empty inputs** rather than failing the apply; five new ids must join `EnvNaming.DerivedIds()` or the `validate` AC passes vacuously; two `gcloud services enable` targets (`monitoring`, `billingbudgets`) are **missing from `FIRST-DEV-DEPLOY.*`** and the budget needs a **billing-account-level** IAM grant no project role can give. **No entity/migration/frontend change**; Minor change ⇒ no integration test, no `features/` doc. ⚠️ Cloud SQL/Redis `metric.type` strings are **unvalidated** here (no GCP access, no terraform binary) and fail as **empty charts, not apply errors**. Trello #114 ([SIfSHGs9](https://trello.com/c/SIfSHGs9)). | Shipped |
| `features/quick-tour-modals.md` ⚰️ | ⚰️ **RETIRED — the Quick Tour was DELETED from the product on 2026-08-02 (#130).** Read `features/how-to-play.md` for what runs. The doc is **kept, not deleted**, for the three things it decided that still bind: **archetype-by-SHAPE** (extracted verbatim into `utils/gameArchetype.ts`), the **copy rules** (never "animal"/"1-8"; and *the word is "word"*), and **why persisting a UI dismissal was rejected** — it would mean prompting a Solana signature to close an onboarding modal. Everything after its "What survived the deletion" section is **history, not behaviour**. Its 10 art components live on, renamed into `components/guide/`. Originally: pixel-art **Quick Tour** onboarding — `overview` on the first lobby visit plus one of three **shape-matched** mode archetypes (`preset`/`race`/`lottery`; a preset lottery matches none and gets no tour) after the game type resolves, name/price/frequency read live off `ActiveGame`, re-openable from one `How to play` ☰ row, absent when embedded. Dismissals live in **`localStorage` only** (versioned keys, all `@1`); X/GOT IT!/**Esc**/backdrop all close **and** mark seen. ⚠️ **The spec's whole backend half was dropped before merge:** `remove-operators` made `PATCH /players/preferences` signature-proved and display-name-specific, so persisting a dismissal would have meant prompting a **Solana signature to close an onboarding modal** (and the overview fires where no wallet is connected); an unsigned endpoint keyed on a public wallet was rejected too. No entity, migration, handler change or backend test — and `useQuickTour` makes **no network call at all**. Auto-open refuses under **every** blocking overlay — boot recovery, idle, relaunching, the `z-[70]` **18+/Terms gate** (which both watchers take as a *dependency*, so the tour fires on ACCEPT) and the display-name prompt — because a Teleported modal is their **sibling**. Mobile folds the lever step into "tap PLACE BET" (4 steps) and freezes all art under `light-graphics`. Ships with a **sticky header** (`overflow-x` moved to `<html>`). | Shipped |
| `specs/unified-websocket-connection.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-meta`:** collapse the **two** SignalR connections every player holds into **one hub and one tab-scoped connection**. `RecentBetsHub` carries no logic of its own (its `JoinGame`/`LeaveGame` are byte-identical to `ActiveGamesHub`'s) and `GameService` injects `IHubContext<RecentBetsHub>` it **never reads**, so `BetNotificationConsumer` simply publishes `{gameId}/Bets` on `ActiveGamesHub` — **same group, same event name, same payload** — and the hub, its `MapHub("/Hubs/RecentBets")` route and the dead ctor param are **deleted** (clean break: no players yet, so no dual-publish window). The bigger half is lifetime: today both sockets are `stop()`ed and rebuilt on **every rollover** (2 negotiates + 2 upgrades per player per round), so a new **`useGameHub`** composable owns one connection for the tab — lazy on first game load, kept across rollovers, game-type switches and back-to-lobby, swapping groups via `LeaveGame`/`JoinGame` instead of reconnecting. **Per-game event names are kept** (`{0}/Terminal|Status|DisplaySpeed|ChatMessage|Bets` + broadcast `GameReceived`): flattening to static names would rewrite the contract everywhere for zero socket-count gain and is *less* safe, since after `LeaveGame(old)` the old-named events simply stop arriving. ⚠️ **Spec-time finding:** the shipped idle watch is `GamePage`-scoped (`onMounted`/`onUnmounted`), so a tab-scoped socket would be **unpoliced in the lobby** — `useGameHub` now owns `startIdleWatch`/`stopIdleWatch` so an abandoned tab still stops pinning a Cloud Run request. Motivation is concurrency: the API sets no `max_instance_request_concurrency` (⇒ platform default 80 concurrent requests/instance, a WebSocket pinning one for its lifetime ⇒ ~40 players/instance today), and every negotiate counts against the per-IP Armor rule that #111 showed is one bucket per shared VPN exit. **No entity/migration/config/API change; player-visible behaviour identical.** Minor change ⇒ no `features/` doc, but an integration test is required anyway (AC #2) because bets silently vanishing is the only failure mode with no symptom. Trello #120 ([b2PoXJ1I](https://trello.com/c/b2PoXJ1I)). | Shipped |
| `specs/fix-bets-logic.md` | **Minor change, `monkify-backend` + `monkify-front` + `monkify-meta`:** two new **per-round bet limits**, so one player can no longer hold a disproportionate share of a round's choice space. **(1)** A game type with **non-empty `PresetChoices`** (Beast Mode) accepts **at most one bet per player per round** — today one wallet can back all 8 beasts and, once a second player joins, holds 8 of the 9 bets. **(2)** In **every** type, the round's **only bettor** cannot place a second bet until a bet from a **different** player exists — after that, **no further cap** (the rolling-cap variant was rejected as unexplainable in the UI and refund-heavy). Both live in `RegisterBetHandler.ValidateBetParameters`, inserted **after** the per-player duplicate check and **before** the free-text exclusivity block, so a blocked bet is caught after `ValidateSolanaPayment` (⇒ must refund) but **before** `RunPreBetCheck` (never reaches the operator's RG provider) and **before** `DebitStandard` (a `Standard` bet is never debited for a bet we reject). Rejections reuse the shipped `RefundInvalidBet` helper + two new `ErrorMessages` (`OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`), with a **pre-payment guard in `useGame.bet()` + `GameConsole`** so a crypto player is never charged for a bet that will be refunded. ⚠️ **Load-bearing spec-time finding:** a **free bet can carry an `OperatorPlayerId`** — the client sends the wallet on free bets in a `SolanaToken` game — so the gate is **`!_isFreeBet && OperatorPlayerId != null`**, not the bare null check the card described; a bare check would limit free bets and break solo free-bet QA. Also found: the existing index `(GameId, OperatorPlayerId, Choice)` already serves both queries ⇒ **no column, no index, no migration**, and `RefundInvalidBet` throws, so the new blocks need no `return`. Every persisted `Bet` row counts regardless of `Status` (a refunded bet still holds your preset slot); limits reset on rollover; Rule 1 is **not** race-proof (application check only — worst case one extra same-wallet bet, no money lost, knowingly accepted). **Orbit (#121–#123) gets no exemption** — an Orbit bet consumes the player's preset slot. Minor change ⇒ **no integration test, no `features/` doc**; `features/games.md` §9 + `features/choice-exclusivity.md` corrected at ship. Trello #126 ([wF5zmsss](https://trello.com/c/wF5zmsss)). | Shipped |
| `specs/legal-docs-self-exclusion.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-lp` + `monkify-meta`:** the "cover our asses" legal pass. (1) A **blocking 18+/Terms entry gate** on **both** public properties (LP-only would miss anyone landing on `app.` directly), ACCEPT + DECLINE, remembered in an **`ss_terms`** cookie scoped to `.spacestroker.com` exactly like the shipped `ss_consent`; the cookie-consent banner renders **only after** acceptance (merging the two overlays is **rejected** — bundling analytics consent with terms acceptance invalidates the cookie consent). Hidden when embedded, on `chromeless` routes, `/error`, `/admin` and on the legal pages themselves (someone in trouble must reach the help links without accepting anything). (2)+(3) LP pages **`/terms`** (grounded in shipped behaviour — non-custodial, USDC/Solana, the 0.5% crypto house cut, exactly when a bet is and is not auto-refunded, **and an explicit "not legal advice, not lawyer-reviewed"** note; **no** restricted-jurisdictions clause, since we do not geo-block) and **`/responsible-gambling`** (help-org links + the honest admission that a per-wallet exclusion is bypassed by connecting another wallet). (4) **Per-wallet self-exclusion**: two nullable columns on `OperatorPlayer` (`SelfExcludedAt` = the flag, `SelfExcludedUntil` = expiry, **NULL ⇒ permanent**), 24h/7d/30d/90d/permanent with **no early undo**, **extend-only**, proved by an **ed25519 `signMessage`** over a Redis-held single-use nonce (verified with the already-referenced **Solnet.Wallet**, no new NuGet) — without proof any public wallet address could be permanently locked out. Enforced in `RegisterBetHandler` **before** `ValidateSolanaPayment`, for **every** operator always (**not** via the opt-in `IPreBetCheckResolver`), blocking paid **and** free bets; state rides the existing `GET /players/preferences`. ⚠️ **Four spec-time corrections to the refined card, all from code exploration:** the inline reject-and-refund path **never refunds** (the event names a `Bet` row that is only inserted later, so `RefundInvalidBetConsumer.LoadBet` returns null and logs a warning — affecting choice-exclusivity, duplicate-bet, game-not-valid and invalid-choice, all while showing the player `RefundWarning`); persisting the rejected bet as `NeedsRefunding` to fix it is **rejected** because **nothing filters bets by status** (`GetBetsByGameId` feeds both the typer and the provably-fair replay, `Bet.PublicBetStatus` is dead code) so it could win a round and change a verified outcome — refunds are therefore issued **bet-less from the event** (new `GameId` + signature on `RefundInvalidBetEvent`, signature-keyed idempotency, `CryptoTransactionLog.BetId` made nullable); `ErrorMessages.RgSelfExcluded` reads *"contact your operator"* and is wrong when **we** are the operator, so a new `SelfExclusionActive` message is added; and mobile Tier-1 deeplink **`signMessage` does not exist** and must be built. Trello #125 ([3kPjRxsJ](https://trello.com/c/3kPjRxsJ)). | Shipped |
| `specs/remove-operators.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** delete the **entire operator concept**. Only one operator ("Main") ever existed, yet it reaches into 197 of 367 backend `.cs` files: `Operator`/`OperatorToken` tables, a signed **launch token** on every player call, the unused **`Standard`** settlement path, a per-operator RG hook with no adapter, a split money ledger, and per-operator cache keys and scope filters. **A new `GeneralSettings:Operator` config section becomes the operator** — onboarding another one means deploying again with a different `spacestrokersettings.json`, not inserting a row (which is why the section keeps the now-ironic name). Player endpoints go **public** (identity = the connected wallet; the token proved nothing the wallet doesn't); the **admin token survives**, keyed from config with **no `iss` check**, and `POST /dev/admin-token` returns. `OperatorPlayer` → wallet-keyed **`Player`** (`Currency` dropped with display currency). **`PaymentMethod` is deleted entirely** — enum, columns and the one-strategy resolver. **The money ledger unifies into ONE `TransactionLog`**: verified never 1:N (placement ⇒ crypto row only, credit/refund ⇒ 1:1), and once `Standard` goes the Solana path is its only writer, so `CryptoTransactionLog` merges in and is dropped — collapsing three columns stored twice (`WriteLedger` passes the *same* `signature` to both) and, as a **bonus fix**, making `BetId` **nullable**, which is exactly why today's bet-less refund path writes **no generic ledger row at all**. Placement rows type as **`Debit`** (safe: `BetDomainService` already never subtracts them — pinned by a test). `ExternalTransactionReference` → **`Signature`** everywhere (~73 backend refs, **0 frontend**; the wire already said `paymentSignature`). Default game types move from `DefaultGameParameters.cs` into config, seeded **create-only by name** so a restart never resurrects a deactivated type. `ISecretProtector` deleted. **`PATCH /players/preferences` gains an ed25519 wallet signature** over a **purpose-keyed** nonce generalised from self-exclusion (a public, spoofable rename was rejected). Migration is **greenfield**. Branding removed; **iframe embedding kept** (operator-free in code). ⚠️ `constraints.md` §6 (vague operator messaging) becomes **obsolete** and must be removed at ship; `sales/` + `pitch/` stay **out of scope** and need a follow-up card. Trello #128 ([TWf2oXxi](https://trello.com/c/TWf2oXxi)). | Ready |
| `specs/lottery-draw-time.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** fix the **24-day Space Jackpot** (`"24:00:00"` parses as 24 **days** in .NET; `remove-operators` rewrote the correct `TimeSpan.FromHours(24)` into it) **and** give a lottery an optional **fixed UTC draw-time anchor** so draws stop drifting. New nullable **`LotteryParameters.DrawTimeUtc`** (+ one additive migration) with a pure `LotterySchedule.NextDrawTime`: slots sit on the **absolute grid** `{ epoch + DrawTimeUtc + k·Frequency }` (epoch = `DateTime.MinValue`, **not** "today", so the grid survives restarts), integer-tick maths only (ticks ≈ 6.4e17 exceed `double`'s exact range), and the earliest slot at least a configurable **`MinimumLotteryDrawLeadSeconds`** (600) away — so 24h + 22:00 is **22:00 UTC daily with zero drift**, and 4h + 22:00 is 02/06/10/14/18/22. Null anchor ⇒ today's rolling `UtcNow + Frequency`, untouched. Because seeding is **create-only by name**, a config fix alone reaches nobody: a narrow **startup self-heal** (`ExecuteUpdate`, mirroring `AllowFreeBet`) patches lottery `Frequency` + `DrawTimeUtc` only, and a new **`CorrectScheduledLotteryDraws`** step recomputes `StartDate` **and reschedules Hangfire** for open **anchored** `WaitingBets` rounds (both directions — it also re-arms a draw missed during downtime; unanchored rounds are excluded or they would be pushed out forever). ⚠️ Three load-bearing spec-time findings: **a lottery bet never publishes `BetPlacedEvent`** (`RegisterBetHandler:130` publishes only when `LotteryParameters == null`), so the Hangfire job at `StartDate` is the **sole** draw trigger and `StartDate` alone changes nothing; the lead **must** exceed `MinimumWaitPeriodForBets` or `BetPlacedConsumer:65` returns before the refund branch and the round is **stuck forever holding real money**; and the stale 24-day job is **provably harmless** (`GameAcceptingBets` guard + `TryStartGame`'s conditional `UPDATE … WHERE Status = WaitingBets`) ⇒ no job-id column. Frontend shows the anchor (lobby badge third line, terminal `DAILY AT 22:00 UTC`); countdown untouched. Admin draw-time input and any seed-time validation guard **explicitly out of scope**. Feature ⇒ mandatory happy-path integration test + `features/` doc at ship. Trello #132 ([cGlkxiYo](https://trello.com/c/cGlkxiYo)). | Shipped |
| `specs/home-dashboard.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta`:** give the app a **front door**. A new **landing page at `/`** (`HomePage` + `src/components/home/`) with two designed states — **cold** (no wallet: welcome/onboarding-led, connect invitation, `GET /players/stats` **not called at all**) and **connected** (stats-led) — carrying the **real mode cards** (`GameTypes.vue` reused verbatim, so #116 art and #129 practice mode land here for free), **personal stats**, quick links (Stats · History · Fair · Settings, embed-gated), a how-it-works entry (opens the Quick Tour until **#130** ships and repoints it) and a **global-stats teaser** reusing `GET /games/stats`. `/game` keeps rendering today's plain lobby untouched (it is the documented iframe target + the `?type=` deep-link); **embedded `/` redirects to `/game`** via a router guard, so `HomePage` never mounts in a frame. Quick Tour **`overview@1` auto-open moves from the lobby to `/`**, every guard intact. Folded in because it rewrites the same routes: the **`/space-stroker` prefix is dropped** (`/` · `/game` · `/game/play` · `/game/history` · `/game/stats` · `/admin` · `/wallet-bridge` · `/background` · `/error`) behind a **strip-prefix redirect shim** that preserves query **and** hash — without it a surviving old URL is a *blank page* (no catch-all + the LB `/*` fallback serves `index.html`) and a **mobile wallet deeplink hop straddling the deploy strands a paid bet** — plus a **generic catch-all → `/error`** closing that pre-existing gap. Backend adds exactly one public, wallet-keyed read **`GET /players/stats?wallet=`** (11 metrics: rounds · bets · most-played mode · wagered · won · net · win rate · biggest win · biggest multiplier · current + best streak) — **on-demand aggregate over existing rows + a 30 s Redis TTL, no entity, no migration, no Consumers/SignalR change**; money rules mirror `game-type-stats`/`fair-stats-menu` exactly (only `RewardForWinnersCompleted` rounds, free bets excluded from money but counted in participation, payouts from `Credit` ledger rows), an unknown wallet returns **zeroed stats with 200**. ⚠️ Code findings: Fair + Settings open state is **local to `GameHeader.vue`** and must be promoted to a shared `useHeaderModals` (a second modal instance is not an option); a **stale unrouted `HomePage.vue` stub** already exists and must be overwritten deliberately; **no rate-limit rule may be added** (`RateLimitSettingsTests` pins an exact set); the two catch-alls rely on vue-router **specificity ranking**, not declaration order; `EMBEDDING.md`/`embed-example.html` still document a `#launch_token` flow that no longer exists (paths corrected, flow **not** restored); LB path rules verified **prefix-agnostic**. Feature ⇒ mandatory happy-path integration test + a `features/` doc at ship. Trello #131 ([ijRUhWbX](https://trello.com/c/ijRUhWbX)). | Shipped |
| `specs/modal-panel-design-system.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** **one shared dialog shell + TWO PANELS + one named z-index scale**, migrated across every overlay, the side panel and four pages, so #130 (How to play) builds on a settled system instead of inventing a fourth look. Source of truth is the Claude Design export `Space Stroker Modals.dc.html` (chrome direction **1b hairline**); its own spec card is headed *"SHARED BY BOTH TIERS / WHAT DIFFERS"*, i.e. the two panels **coexist** — an earlier pass that read this as "two materials, one of which dies with the Quick Tour" was wrong and is superseded (spec §3.1 tables all nine corrections). **GREY** = `--color-surface` on a 1px hairline, amber title left behind a 6px square, title/footer bars sunk to `--color-surface-bar`, bordered VT323 `X`. **BLACK** = `--color-void-deep`, 2px cyan frame + glow, centred cyan title between pixel sparkles, one centred amber ghost. **Shared:** `compact 380`/`standard 520`/`wide 720`/`xwide 1024`, 36·44px titlebar, 18px body, 12–14px footer, 4px radius, `90dvh`, the **dithered** backdrop (void 55% + 9px 45° gradient — **never `backdrop-filter`**, which `html.light-graphics` strips on every phone), the full a11y package (`role=dialog`, `aria-modal`, `aria-labelledby`, focus trap, safe restore, scroll lock), a first-class **`dismissible: false`** mode, and dismissal by X / Esc / backdrop / primary. **Choosing a panel is behavioural:** grey when the player is *doing* something or the tone is serious; black only when brief-and-informative **and** light — today the quick tours alone. Also new: a full-bleed **`ERR`/`OK` status strip**, the flat **`.btn-dialog`** family (`.btn-arcade` stays the cabinet button), and **two** mobile treatments — bottom sheet for `compact`/`standard`, **full screen** for `wide`/`xwide`. Esc is routed by a module-level **`useOverlayStack`** so a blocker on top *swallows* it rather than closing the modal underneath. ⚠️ **The layering bug is not a z-index bug.** `App.vue`'s root is its own stacking context, so anything inside it composites at **10** and can never paint above a teleported modal, whatever number it carries — `GameIdleOverlay` at `z-40` was losing for that reason, and **teleporting it, not renumbering it, is the fix**; the scale (`base 0 · app 10 · furniture 20 · header 40 · popover 50 · modal 60 · blocking 70 · legal 80`) therefore documents that **every value ≥ 60 must be `<Teleport to="#app">`**. ⚠️ **`arcade.css` beats Tailwind utilities**, so the cap, radius and mobile treatments live in the class — a `max-h-[90dvh]` utility beat `.dialog--fullscreen` and clamped the full-screen tour to 90% of the viewport until it was moved. ⚠️ **`MobileWalletModal` teleported to `body` at `z-[100]`** — the wallet picker could **cover the 18+/Terms gate**; the scale closes it. `BootRecovering`/`EmbedRelaunching` are **not overlays** but blocks rendered *instead of* `<main>` (they teleport out of that flow position; `App.vue`'s `v-if` chain is deliberately untouched so `<main>` still never mounts during recovery). Named exceptions: the game furniture, **the docked side-panel cabinet** (metal bezel, rivets, recessed CRT — **AC #15 amended**) and `GameTypeCard`. Scope grew at the owner's call: `ResponsibleGamingModal` migrated rather than left bespoke, `QuickTourModal` rebuilt on the black panel, `xwide` added, and `/game/history` + `/game/stats` on a grey page fill while `/error`, `/soon` and the dashboard **deliberately** stay near-black. **No backend, entity, migration, API or SignalR change; `monkify-front` has no test runner** ⇒ `yarn lint` + `yarn build` + manual QA. **AC #10/#20 verified in emulation only — no real iOS device.** Minor change ⇒ no integration test, no `features/` doc. Trello #124 ([NIMydn5I](https://trello.com/c/NIMydn5I)). | Shipped |
| `specs/how-to-play.md` | **Feature, `monkify-front` + `monkify-meta` — no backend:** replace the overlay-only onboarding with **one readable, linkable guide page**. New route **`/how-to-play`** (`HowToPlay`) — five **registry-driven**, deep-linkable sections (`where-to-start` · `how-the-game-works` · `provably-fair` · `player-settings` · `refunds`) behind a sticky section nav (a chip row below `lg`); **Orbit is deferred** to a card created at ship, and the registry is what makes it a one-entry addition later. The **Quick Tour is deleted outright** — modal, composable, constants, both auto-open watchers, all seven guards, the versioned `space-stroker.tour.seen` set and both GA4 tour events — so **nothing is ever unprompted again** (knowingly accepted: `HomeHowItWorks` on `/` is the onboarding now). Its **10 art components survive**, renamed into `components/guide/` with the `TourStep` prop replaced by a flat `GuideArt` and every measured `artClass` kept verbatim. `resolveTourKey` is extracted to `utils/gameArchetype.ts`, so mode content stays **archetype-matched by SHAPE** (never by id or name) off the live catalogue with real ticket price + `formatLotteryFrequency` frequency; a preset lottery matches nothing and renders **no card**. **Responsible Gaming becomes `/responsible-gaming`** and the modal is deleted — the riskiest part, since the mobile wallet-deeplink return must re-point at the route (a bet hop and a self-exclusion hop are **indistinguishable** in the return params; only what was stashed tells them apart). Neither route is an `isPlayerRoute()`, and **both redirect to `/game` when embedded** through one shared `beforeEach`. ⚠️ Code findings: **`applySelfExclusionPreferences` is a dead export** — `selfExcluded` is always `false` after a reload, so the excluded state is unreachable and the client pre-bet guards are dead (backend still enforces it) — **fixed here**; the router has **no `scrollBehavior`**, so a cold `#anchor` load scrolls nowhere unless the page handles it; `getGamesTypes` is `runWithRecovery`-wrapped, so a flaky network raises the blocking `> RECONNECTING…` overlay over the guide; `TourBetPanelArt` is static *because* it only ever rendered under `isMobile`, a guarantee a page does not carry; and under-subscribed **races are never refunded** (only lotteries are), which the Refunds copy must not misstate. Design decisions: the guide's **tip/callout block adopts the black panel** (otherwise deleting the tours leaves the black material with zero consumers — the dangling-rule condition #124 was filed to fix), `/how-to-play` takes near-black **`.arcade-frame`** and `/responsible-gaming` takes **`.arcade-frame--grey`**. Frontend-only ⇒ **no integration test is possible**; `features/how-to-play.md` is still mandatory and `features/quick-tour-modals.md` is retired. ⚠️ **Shipped 2026-08-02 with EIGHT sections, not the five specced here** — the five shipped, were reviewed and were rebuilt; see **spec D4**, **§7.4.1** (the active-section rule, twice wrong) and **§7.5.-1** (the copy rules, incl. *the word is "word"*). Trello #130 ([mzhftjAl](https://trello.com/c/mzhftjAl)). | Shipped |
| `specs/control-sizing-system.md` | **Minor change, `monkify-front` + `monkify-meta` — no backend:** **one control-sizing scale** every button and input family consumes, so any button and any input in the same row are the same height **by construction** rather than by hand-tuned padding. Flagship symptom: the Settings custom-seed row, where `.dialog-input` (44.7) sits beside `.btn-dialog` (33.2) because **neither declares a height**. Three steps as `--ctl-h-sm/md/lg` + `--ctl-px-*` in `colors.css` — **34.4 / 40 / 48**, with `sm` and `lg` pinned to the arcade values that already ship (`2.15rem` / `3rem`) so the lever, wallet cluster, dashboard and `GameTypeCard` do not move a pixel — stepping to **40 / 44** below 1023px in **one media block** with **no call-site class change**. **Height is not skin (D1):** `.btn-arcade` joins for height and keeps its bevel + 5px key-drop, which **overhangs the border-box on purpose** (D3). The flat family defaults to `md`, so `.btn-dialog` **and** the input share one default — *that* is the actual fix. `.dialog-input` → **`.input-arcade`**, one class for dialogs and pages, **renamed not aliased**, plus `--select` (`appearance: none` + a two-gradient chevron that reads `--color-ink-mute` directly, since a data URI cannot resolve a custom property). `.dialog-field` joins at `md` and centres by **`line-height`, never `display:flex`** — its one consumer needs `truncate`, and `text-overflow` does not apply to an anonymous flex item. **Both wrapper components deleted** (`SecondaryButton` 0 consumers; `PrimaryButton` 4) — ⚠️ it is **not** a pure pass-through: `HomeHero` renders `<wallet-buttons variant="ghost" size="md">`, so both branches of both props are live, and **`md:w-[200px]` must survive** the dead-utility deletion or the sticky header reflows. ⚠️ **The focus ring MUST be `outline`, not `box-shadow`:** `.btn-arcade--primary`'s key-drop **is** a box-shadow and `html.light-graphics .btn-arcade--primary` re-declares it at **(0,2,1)**, beating `.btn-arcade:focus-visible` at **(0,2,0)** — a box-shadow ring would wipe the key-drop on desktop **and be invisible on every phone**. `.btn-arcade` gains a focus ring **it has never had** (there is no global focus rule in the app). **Five** shipped dead-utility stacks recorded, all the same cascade hazard (`arcade.css` is imported after `@tailwind utilities`): `asLink`, `text-xs`/`text-sm`, and **three** `text-primary`/`text-cyan` colours that lose — the `/responsible-gaming` select, the client seed and the nonce **are all meant to be amber/cyan and render in ink today**. Documented non-members: **`.dialog-row`**, text-link buttons, **`/admin`** (its `.admin-input` stays copy-pasted in two files **on purpose**) and `.dialog__close`. ⚠️ **Adopting `.btn-dialog` on `MINE ONLY` silently kills its active state** unless it moves into `arcade.css` — the cascade hazard biting during the fix itself. `.btn-dialog--tour` is **deleted** (0 consumers since #130). Minor change ⇒ **no integration test, no `features/` doc** — the rule lives in `conventions/frontend.md`; `monkify-front` has no test runner ⇒ `yarn lint` + `yarn build` + manual QA on desktop, phone and with `html.light-graphics` forced. **Shipped: all 20 ACs measured in a live browser. ⚠️ A SIXTH dead-utility stack was found in QA — and it was the flagship row itself: the Settings seed row NEVER shared a row on desktop, on `main` either. `sm:basis-auto` is applied exactly as authored, but `flex-basis: auto` DEFERS to the item's own `width`, and the input class declares `width: 100%` — so the input's flex base size was the whole row and SET/ROTATE wrapped underneath. Fixed with `sm:basis-0` (a property the class does not own). This is a distinct shape of the cascade hazard: D18's five stacks all LOSE their tie; this one WINS and still does nothing. Also shipped beyond spec: `display: block` on `.dialog-field` (height is ignored on an inline box) and an `.input-arcade--auto` modifier (the two filter-bar selects need shrink-to-fit, and a `w-auto` utility would lose the tie). See spec §16.** Trello #135 ([jMuAlPBV](https://trello.com/c/jMuAlPBV)). | Shipped |
| `specs/mobile-game-design-update.md` | **Frontend feature (visual), `monkify-front` + `monkify-meta` — no backend:** bring the **mascot** and the **real arcade cabinet with the lever** to the mobile `/game/play` screen, per the card's mock. The GAME segment becomes a **three-band column** — tabs → `.mobile-terminal` → **Edson** → **console pinned bottom** — that fits a portrait phone with no page scroll; priority when tight is **console > terminal > Edson** (Edson shrinks first), and a viewport too short (sideways, ~336px) **scrolls** rather than crushing the bands. The console is the **real desktop art re-composed as TWO plate modules**: visor + `keyboard.png` left, **lever** right. ⚠️ `terminal.png` is **970×420 containing THREE plates side by side**, not one — each module shows one well via `background-size: 300% 100%` + `background-position-x: 0%/100%`. **Today's compact gradient bet panel is deleted**, which knowingly **reverts the mobile half of `specs/mobile-preset-keypad.md`**: preset games go back to the 8 `button<N>.png` keys **inside the plate**, **numbers only** (the terminal's `> CHOICES:` is the mapping), capped at 8 — so the 8-key cap becomes **one shared gap** for the existing desktop-cap card. The **lever is always pullable** (invalid pull ⇒ twitch + `error1.wav` via the existing `leverError()`, never dimmed) and doubles as **next round** at `RewardForWinnersCompleted`; **no `NEXT ROUND` label anywhere**. The in-console `CONNECT WALLET` goes — the **header `CONNECT` is the only affordance** (verified: `WalletButtons.vue` already branches on `isMobileDeeplinkContext()` → `openWalletModal()`), with the **embedded popup-blocked retry kept on the plate**. Edson **animates** on mobile, but the desktop **eager 8-GIF preload (7.70 MB measured)** is replaced by `monkeyIdle.gif` at load + a post-first-paint `requestIdleCallback` prefetch; while the visor is focused Edson hides so the visor and lever clear the native keyboard. ⚠️ Code findings: **`GameConsole` is rendered INSIDE `GameTerminal`** (`:517`) on both paths and must move out on the mobile path (Edson sits between them), and `GameTerminal`'s `animate-rise-in` wrapper carries **no height of its own** so it collapses unless made a real flex band; the desktop key art has **no selected state** (`hover:` never fires on touch) while **AC #16 requires a lit key**, so a mobile-only derived lit state is added (spec §3.1 records the card-prose ↔ AC conflict); mobile is **always** `html.light-graphics`, which strips `.blink` / `.animate-wobble` — the visor placeholder will not blink and that is expected; `isMobileDeeplinkContext()` needs a **real mobile UA**, so DevTools emulation cannot verify the connect path. Tabs keep the `GAME \| CHAT` labels (the mock's `>_ TERMINAL` / `💬 CHAT` relabel is declined). No backend/API/SignalR/DB change ⇒ **no unit test and no integration test** (`AGENTS.md` rule 8 N/A — no backend surface); `monkify-front` has no test runner, so done = `yarn lint` + `yarn build` + manual phone QA. ⚠️ **Shipped 2026-08-02 with three deviations the spec was NOT written for — see spec §16.** The tabs did **not** keep the filled-amber active state (**AC #17**); the selected tab is the lit screen (true black + `#4ade80` + a 2px bar). **Desktop is no longer pixel-identical** (**AC #18** amended): the chat composer moved onto `--ctl-h-sm`, **overriding `control-sizing-system` D5**, and `.animate-wobble` left the `html.light-graphics` kill list. The mobile screen panel was re-skinned to true-black glass in a 7px `#5c6a8c` housing. **Verification is partial:** layout, console composition, short-viewport scroll and the lever tap target were browser-checked; betting, the preset keypad, per-status animations, keyboard focus, wallet connect, near-miss and rollover were **not** exercised. Trello #134 ([yPwW5Awx](https://trello.com/c/yPwW5Awx)). | Shipped |
| `specs/mobile-console-edge-cases.md` | **Bug fix (frontend-only), `monkify-front` + `monkify-meta` — no backend:** the two mobile edge cases the card owner screenshotted. **(1) 600–1023px wide and ≥600px tall now render the real PC three-plate console** (visor+keyboard · lever · preset keypad) instead of the phone's two-plate cabinet stretched across a tablet — via a `wide` prop that only swaps the desktop branch's height classes, so the desktop composition **is** the deliverable ("minus the BG" = the band only, no `GameTerminal` room art). **(2) the lever scales with its plate** on the compact cabinet. Root cause of (2) is documented precisely: percentage padding resolves against the containing block's **width on every side**, so `py-[11%]` grew with the plate while `22dvh`/195px capped the height — and a 140×220 lever in a wide short plate is always height-bound, so the padding taxed the only axis that mattered. Decisions: **the 600px threshold is derived, not chosen** (`0.4H + 306 ≤ H − 54` ⇒ H ≥ 600) and is applied to **both axes**, so landscape phones keep the compact cabinet; the band is aspect-locked at **2.73** (desktop's own measured aspect, scale-invariant) and its height is bounded through the width; the lever's `w-[53%]` was **tuned by measurement** after `w-[46%]` regressed small phones. Frontend-only ⇒ **no unit or integration test** (`AGENTS.md` rule 8 scopes that to new features; `monkify-front` has no test runner) — done = `yarn lint` + a measured live-browser pass at 14 viewport sizes on both a free-text and a preset game. ⚠️ Sub-600px widths are **unreachable by resizing a real Chrome window**; they were verified through a same-origin **iframe harness**. **AC #7 was corrected at ship rather than claimed** — a flat 44.5% replaces a proportion that drifted 43.4–49.2% across ≤400px, so individual widths move by up to ~10% relative. Trello #137 ([PvfaU50d](https://trello.com/c/PvfaU50d)). | Shipped |
| `specs/practice-mode.md` | **Frontend feature, `monkify-front` + `monkify-meta` — no backend:** a fully interactive **practice round** at **`/game/practice`** that runs entirely in the browser — no backend call, no SignalR, no wallet, no money — so a first-time visitor can see a round end without connecting a wallet, paying on-chain and waiting for the second player every seeded type requires. Reached from the **three Practice CTAs already on `/`** (`HomeHero` ×2 + `HomeFeatureCards`), which are repointed off `/soon`; **`/soon` + `ComingSoonPage.vue` are deleted** (practice was its last consumer) and a bookmarked `?feature=practice` falls to the catch-all → `/error` (never externally linked; the app host is `Disallow: /` + `X-Robots-Tag: noindex`). ⚠️ **The card's original "in the lobby" wording is superseded — `/game` is untouched.** A **fixed, hard-coded** free-text race (6 chars, letters-only) so it runs with the API down; **one synthetic opponent** and a **uniform draw ≈50/50**, which is the only shape that can teach **losing** and mirrors the real `MinimumNumberOfPlayers: 2`. Money is a hard-coded **`1 USDC` ticket / `2 USDC` pot** marked not-charged (`formatGameAmount` renders it verbatim from a synthetic config — no fork). **Full cabinet fidelity** (plate art, working lever, Edson, audio) reproduced in practice-owned components, on the **post-#134** layout. ⚠️ **Several spec decisions were superseded by owner feedback during implementation — `features/practice-mode.md` is the current truth, and it lists them.** Shipped with the **real** timings, not compressed ones: 10 s countdown (backend `TimeUntilGameStarts`, which is also what makes Edson's 8800 ms glasses beat land), a batched ~59 k-character stream (the real terminal flushes at `TerminalBatchLimit` 500), the **real payout copy** (`> PAID TO:`, `> REWARDS SENT…`) with one sticky banner carrying the not-charged statement, the full three-track audio bed, and a **lever that doubles as "next round"** over the shared `VUE_APP_NEW_GAME_DELAY` hold. ⚠️ **Load-bearing:** 26 letters × 6 chars ≈ 309 M windows vs ~59 k characters per run, so a *"type until the word appears"* loop **never terminates** — the winner is **drawn** then revealed (and appended to the end of the stream, highlighted, because a real race ends *on* the winning window), exactly as `DecideFallbackWinner` + `useNearMiss.finish` already do; a natural hit on either choice still beats the draw. ⚠️ **`useNearMiss` is a module singleton** (shared with the live screen) — practice reuses the **pure** `utils/nearMiss.ts` with its own state, and the store is **provide/inject**, not module scope, so practice state cannot outlive the page. Self-excluded wallets are **blocked, fail-open** (page loads preferences itself, like `ResponsibleGamingPage`; not an `isPlayerRoute()`; skipped entirely with no wallet, so a first-time visitor makes **zero** requests); redirects to the lobby when **embedded**; **no side panel, no chat**, **no fairness content at all**, and **no "play for real" CTA** (the header is the exit). Consciously accepted: practice can be looped indefinitely with no stopping point, and the `1`/`USDC`/6-char values are hard-coded and will drift. Frontend-only ⇒ **integration test waived** (precedent `analytics`, `mobile-wallet-connect`); no test runner ⇒ lint + build + manual QA. Trello #129 ([WKXsNym4](https://trello.com/c/WKXsNym4)). | Shipped |
| `specs/orbit-custodial-wallet.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta` — child 1/3 of the Orbit umbrella (#118):** the platform's first **custody of real player money**. A house-held **Orbit balance** funded by fixed **1/5/10 USDC** on-chain deposits, an **append-only `OrbitTransaction` ledger** that is authoritative for the balance (materialized `OrbitBalance` column self-heals to `SUM(Amount)` over **all** rows, no status filter), and an on-demand cash-out. Everything is keyed by **pool** `(OrbitAccount, MintAddress, PlayerId)`, so reconciliation is `SUM(balance)` per pool against **that** account's ATA and a config rotation starts a new pool at zero while the old one stays intact and withdraw-only. Auth is **two-layer**: a **stateless HMAC session** (`X-Orbit-Session`, 7d, no store, **not revocable**) for reads, **plus a fresh purpose-scoped ed25519 signature on every money movement** — reusing the shipped public `GET /players/nonce?purpose=` with two new `WalletNoncePurpose` values, so **no new nonce endpoint and no frontend protocol work** (mobile `signMessage` deeplink is already shipped). ⚠️ **Amends #118 decision 5:** deposit credit is **session-only** — the wallet-signed SPL transfer *is* the proof, so a second prompt buys nothing and costs a mobile app-hop; **cash-out keeps its signature**, and so does **disabling** Orbit (it forces a cash-out). Cash-out **reserves at request** (Pending row + atomic conditional debit in one transaction) then Consumers executes — **debit before transfer, always**; failure appends a `CashOutReversal`. **Self-exclusion disables Orbit and force-cashes-out**, preserving the enrolment's settings. Adds `GameParameters.AllowOrbit` (config-driven per type + admin checkbox + name-matched `ExecuteUpdate` self-heal that **must** drop the 12h `ActiveParameters` cache key). Five new **public** `/orbit/*` endpoints ⇒ 5 rate-limit rules that trip #112's exact-set assertion by design, plus a Cloud Armor WAF check. Hourly cash-out sweep (never re-drives `Executing`), 6h reconciliation. Design settled (modal **A**, header cluster **A**). **No auto-betting** — that is #122. Trello #121 ([R4Qe0jxX](https://trello.com/c/R4Qe0jxX)). | Shipped |
| `specs/orbit-auto-bet-engine.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta` — child 2/3 of the Orbit umbrella (#118):** the engine that spends #121's custodial balance. For every enrolled+funded player it places **one real bet per round** on their chosen type, funded by a **house-signed transfer out of the Orbit account**, then credits winnings back in and fires the "cash out at" threshold — writing the `BetDebit` / `WinCredit` / `Refund` ledger types #121 defined and never wrote. ⚠️ **There is no round-created bus event today** (`GameCreatedEvent` is SignalR-to-browsers only), so `CreateGame` starts publishing it and a **two-stage fan-out** hangs off it: a planner decides viability once and arms the gate, then emits **one `PlaceOrbitBetEvent` per player** for concurrent placement with per-player retry isolation. Placement happens **only into a round that can actually reach `MinimumNumberOfPlayers`** (an under-subscribed *race* never self-refunds, so a lone Orbit stake would strand); otherwise it waits for the human bet that makes it viable. The **`BetDebit` row IS the claim** — `OrbitTransaction.GameId` + a unique `(pool, PlayerId, GameId)` index, written in the same transaction as the atomic debit and **before** the transfer, so a redelivery loses on `23505` having moved nothing (NULLs-distinct, like `Bet.ExclusiveChoice`). A start **gate** (`Game.ExpectedOrbitBets` + `OrbitGateExpiresAt`, 45s ceiling) arms **only when Orbit is load-bearing**, so a paying human never waits on a bot, and wakes early via an atomic decrement + a `ScheduleLottery`-style Hangfire backstop. Choices and the client seed **derive from the `ServerSeed`**, which now commits at **round creation** (it committed on the first bet). ⚠️ `ValidateBetPayment` is **skipped** for Orbit bets — not an optimisation: it asserts sender == the *player's* wallet and would reject every Orbit stake — and the marker lives on the **MediatR request, never the public request body**, or any caller could mint unfunded stakes. Frontend is two owner-only surfaces (private `ORBIT` tag + a "round covered" note) and turns out to be a **flag + copy change**, since an Orbit bet already trips `iHaveBetThisRound`. Trello #122 ([h7MF4Oky](https://trello.com/c/h7MF4Oky)). | Shipped |
| `specs/orbit-notifications.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta` — child 3/3 of the Orbit umbrella (#118):** tell the player what Orbit did. A **per-player group on the existing `ActiveGamesHub`**, joined with the shipped **Orbit session token** (`JoinOrbitNotifications`) and named **server-side** from the token's `PlayerId` — joining by wallet was rejected because a wallet address is public on every bet list, so an Orbit group keyed on one would leak both the win amounts and the fact that the player uses Orbit at all. It fires on **every Orbit win**, carrying **amount + game type and deliberately no balance** (the threshold cash-out fires the instant the credit lands, so a pushed balance can already be zero) — the client toasts and re-reads. **Telegram covers cash-outs only**, and now **all four** sources (`Manual`, `Threshold`, `ForcedOnDisable`, `ForcedOnSelfExclusion` — the two `Forced*` values did not exist when #118 said "manual and threshold", and a forced cash-out is exactly the one the player did not initiate); a failed/reversed transfer sends **nothing**. Linking needs **no poller and no webhook**: a `t.me/<bot>?start=<code>` deep link plus a VERIFY press making **one** `getUpdates` call, with the code and the read offset in **Redis** and the offset advancing **only on a match** (advancing always would eat another player's pending `/start`). **Telegram is inert without a bot token** — `GET /orbit` exposes `telegramEnabled` so the whole section hides locally. Also: the **"paused — insufficient funds"** state (⚠️ `features/orbit-auto-bet-engine.md` claims it exists; it does not) derived client-side with one toast per pause episode, a **session-only UNLINK**, and the **first-win upsell** on **paid** wins only (a free-coin win cannot fund a custodial USDC balance), tutorial tier, once per browser. ⚠️ **One migration after all** (`AddOrbitCashOutFloor`) — the card and this spec both said none, but live testing showed "cash out at" only fires on a **win**, so a balance falling through it is never caught; the new **`CashOutFloor`** (own `OrbitCashOutSource.Floor`, exempt from the daily cap) is evaluated from `RewardGameConsumer` once the round **settles**, never at placement — a stake leaves the balance immediately, so evaluating earlier could cash a player out while their bet was still riding. `OrbitEnrollment.TelegramChatId` still needed nothing, having been created by #121 for this card. Absorbs #121's deploy gap: all **three** Orbit secret ids land in `deploy/terraform/workload/main.tf` **+ `deploy/secrets.example.json`** (⚠️ `EnvNaming.DerivedIds()` needed **no** change — it enumerates the operator's own secrets file). Also fixes two #122 engine defects it could not be tested without: the `null`-only re-plan guard that locked Orbit out of a round stamped `0`, and a cold boot losing its first `GameOpenedEvent`s because `OpenNewGames` ran before the transport database existed (startup moved to `ApplicationStarted`). Trello #123 ([NIH7XFHi](https://trello.com/c/NIH7XFHi)). | Shipped |
| `specs/practice-mode-changes.md` | **Feature (frontend-only), `monkify-front` + `monkify-meta` — no backend:** turns the shipped `/game/practice` round into a **guided tutorial**. (A) The player **always wins** — the uniform draw and the natural-hit override leave the outcome path and the lose branch is deleted (⚠️ **reverses** `specs/practice-mode.md`'s unbiased-draw + "teach LOSING" decisions). (B) The stream **keep-up glitch** is fixed: `PracticeTerminal`'s scroll watcher was `flush: 'pre'` (scrolling before the DOM was patched) and `PracticeTyper` rendered a **second, decoupled** copy of the stream on its own 50/35/25 ms interval — the interval is deleted, `terminalOutput` is rendered directly, the scroll runs post-flush; crop rule kept. (C) A **4-step skippable talking bubble** above Edson (verbatim copy) + an arrow at `#practice-word-input`, `SKIP`/`CONTINUE` + `skip all`, skip-1 auto-bets from a new pool disjoint from `PRACTICE_OPPONENT_WORDS`; runs **once per page entry**, nothing persisted. ⚠️ **Each bubble HOLDS the round** (countdown / race mid-stream / rollover) — an **accumulated-pause offset** on the single-timestamp clock, with the near-miss wall-clock timestamps rebased on release. Edson pinned to `monkeyTyping3.gif` for the whole run; audio unpaused. Integration test **waived** (no backend surface). Shipped to `main` with the **`release/ui-ux-changes`** train on **2026-08-04**. Trello #142 ([EfIRqHbT](https://trello.com/c/EfIRqHbT)) | Shipped |
| `specs/app-home-changes.md` | **Minor change (frontend-only), `monkify-front` + `monkify-meta` — no backend:** a polish pass on the front door `/`. Copy: the hero's quiet fallback → *"Place a bet and get ready for the next round."* (connected state only, post-#138); the how-it-works section titled **`WANNA LEARN HOW TO PLAY?`** with its `Three beats` eyebrow dropped, beats relabelled `Place a bet` / `Edson starts key-stroking` / `First bet typed wins` (**sub-copy verbatim**) and the button → `Learn more`. Layout: the **`PLAYERS` chip is removed** from race/preset picker cards (`TICKET`, the lottery `POT` chip and the `FILLING`/`LIVE NOW` pill all stay) on **both `/` and `/game`** — the picker is one shared component; the **`TRY IT FREE` panel is deleted** (post-#138 the cold hero's own CTA is labelled `Try it free`) and `PROVABLY FAIR` goes full width, with the `home_link_click`/`practice` event **re-homed** into `HomePage.startPractice` so the GA series survives. Requests: `THE FLOOR`'s `GET /games/stats` + `GET /games/history` are **deferred to a one-shot `IntersectionObserver`** (retry + `failed` states preserved; `loading` stays `true` until a read completes), and the boot catalogue response is **reused** by `GameTypes` instead of re-requested. ⚠️ **Two card premises corrected in the spec:** the per-type `GET /games/{id}` fan-out **stays** (owner rejected removing it — it feeds the POT chip, the status pill, the picker eyebrow **and** #138's hero live line + Join/Watch CTA), and the "duplicate `/games/active-types`" is **already deduped by `singleFlight`** (#104), so item 6 is a cleanup, not a saving (before-count 6, not 8; after 5 before scroll). No entity/migration/unit/integration test. Merged into the **`release/ui-ux-changes`** train (2026-08-03) with #142 + #144, and **on `main` since 2026-08-04**. Beat heading shipped as `EDSON STARTS KEY-STROKING` (owner change at ship time). One late owner change rode the train: the newcomer's quiet-floor **primary** hero CTA is now `TUTORIAL MODE`, while its secondary and the cold hero keep `TRY IT FREE`. Trello #143 ([eS4SpiZ6](https://trello.com/c/eS4SpiZ6)). | Shipped |
| `specs/how-to-play-changes.md` | **Minor change (frontend-only), `monkify-front` + `monkify-meta` — no backend:** a copy-and-structure pass on the shipped `/how-to-play` guide. (A) The first **three** sections (`what-is-this` + `where-to-start` + `how-a-round-goes`) **merge into one**, `how-the-game-works` — a short premise lead-in beside the kept `typing.gif`, a **7-step** `GuideRail` (`PULL THE LEVER` folds into `PLACE YOUR BET`, keeping its "no lever on a phone" clause), the fairness bridge paragraph, and the black `THE WHOLE GAME` tip; both dropped section intro lines go. (B) A **new final step `WIN THE POT`** with the only new asset, **`won.gif`** (570×520, 111 KB) — copy corrected off the card's draft, which said "your **choice**" (an owner-rejected wording) and "the **whole** pot" (false: same-word winners split it, and a house cut applies above stake). (C) Space Jackpot's `DRAW EVERY 24 HOURS` → **`EVERY DAY AT 10PM (UTC)`**, hardcoded **but gated on live values** (24 h **and** `drawTimeUtc == 22:00`) — never a name match, since `GuideModeCards` matches by SHAPE; ⚠️ **no backend work**, `drawTimeUtc` already ships on `ActiveGame` and the guide simply never passed it. (D) `YOUR SETUP` → **`PLAYER PREFERENCES`** (`player-preferences`), intro line → "You are free to edit:", `NONE OF IT REQUIRED` tip deleted. Sections **8 → 6**; four ids retire with **no alias map** (nothing links to them; an unknown hash already lands at the top by design). ⚠️ Keeping the surviving tip is **load-bearing** — it leaves `.arcade-frame--black` with one consumer instead of zero. Art otherwise **unchanged**; the dead `lever` / `bet` / `pick-terminal` kinds stay registered. Integration test **waived** (no backend surface); `features/how-to-play.md` corrected at ship. ⚠️ **Shipped with SIX rail steps, not seven** — `PLACE YOUR BET` was dropped at ship time (owner call) for repeating its neighbours, so the page no longer states anywhere that **betting closes when the round starts** or that **a phone has no lever** (known gap; fold into `PICK YOUR WORD`, do not reinstate the row). Merged into **`release/ui-ux-changes`** 2026-08-03 and **on `main` since 2026-08-04**. Trello #144 ([aUrybUea](https://trello.com/c/aUrybUea)) | Shipped |
| `specs/commission-rate-1-percent.md` | **Minor change, `monkify-backend` + `monkify-lp` + `monkify-meta` — `monkify-front` NOT affected:** raise the house commission on winnings from **0.5% to 1%**. The behavior change is **one config value** (`GeneralSettings.Operator.Token.CommissionPercentage: 0.5 → 1.0` in the committed `spacestrokersettings.json`) — no code, no entity, **no migration**, no API/SignalR change; `BetDomainService` already divides the percent by 100. ⚠️ **The card's "0.5% per pot" is NOT what ships and the base is deliberately unchanged:** the cut is `rate × max(0, winnerShare − ownStake − houseFundedShare)` — profit above the winner's own stake, house-funded `LotteryPot + CarriedExtra` excluded ≡ `rate × losing stakes` — so as a share of the pot the yield is 0.83% (6 bets/1 winner), 0.50% (heads-up), 0.33% (3 bets/2 winners). A pot-percentage rake was **rejected** (it would tax the winner's own stake and the house's own jackpot, breaking the shipped "a winner never nets less than they staked" promise). Rate stays **committed-config-only** — no deploy `appConfig`/Secret Manager override — so **prod reads the repo file and BOTH Cloud Run hosts must be redeployed together** (Consumers = payout, Api = badge). The **rate-change boundary is accepted, not engineered away** (no per-`Game` snapshot ⇒ no migration): a round in flight at restart settles at 1%, mitigated procedurally by deploying with **no paid round open** — the exposure is **Space Jackpot** (24h lottery). Frontend needs **zero changes** (`GameStatsPanel` + `GameStatsPage` render the backend value; the how-to-play guide deliberately names no number), but the **LP legal Terms page** hardcodes "0.5% house cut" and is corrected to 1% **and future-proofed** (rule-first, defers to the in-app badge). Ships **one guard assertion** (`GameFlowTests.cs:123` `ShouldBeGreaterThan(0m)` → `ShouldBe(1m)`) because nothing otherwise asserts the new rate — a silent revert would cost revenue with a green suite; the `BetDomainServiceTests` 0.5% fixtures are **deliberately untouched** (they test the formula, not the rate). Minor change ⇒ **no new integration test, no new `features/` doc** — the existing `PaidCryptoRace_AppliesCommission_CreditsPotMinusCut` already covers the path and derives its expectation from the configured rate, and `features/crypto-commission.md` is corrected at ship. ⚠️ **Shipped with the guard in the FAST unit suite (`UnitTests/Api/CommissionRateTests.cs`), not by tightening `GameFlowTests.cs:123`** — spec §10.1 row 2 is deliberately unimplemented; the fast suite runs on every backend change while the integration suite is Docker-gated (user-approved deviation from AC2's letter). Trello #147 ([quCTPKxZ](https://trello.com/c/quCTPKxZ)). | Shipped |
| `specs/orbit-feedback-adjustments.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta` — branched off #141, NOT off `main`:** the six pieces of Orbit feedback from live testing, in one card. **(1)** An **optional fixed word** per enrolment: on **free-text** types it is a **standing claim on that game type** (new `OrbitChoiceClaim` row whose existence *is* the claim, unique `(GameParametersId, Choice)`) blocking other Orbit players **and human bettors** in every round — even rounds Orbit does not bet (STOPPED, out of funds, cool-off), because a conditional claim could be lost mid-losing-streak. Released on change/clear/**disable**, **re-validated** on re-enable (never silently restored), re-claimed on a game-type switch (old claim released only once the new one succeeds). **Preset picks are honoured but NEVER reserved** — reserving them would cap a preset round at 8 bets. A blocked human sees the existing `ChoiceAlreadyTaken` + `RefundInvalidBet` path, **byte-identical to a same-round collision**, because Orbit's invisibility to other players is the #118 rule. Word unset ⇒ `OrbitChoiceGenerator.DeriveChoice` unchanged; `Bet.Seed` and the outcome keystream are untouched, so **provable fairness is not broken**. **(2)** **Partial cash-out** (amount floored at `MinimumCashOut`, ceiling the ledger balance, one-tap MAX) and **both** 24h rules → **1h**: the one-`Manual`-per-UTC-day cap becomes a rolling gap (`ManualCashOutGapMinutes`, 60) and `ReEnableLockHours` 24 → 1 — they move together, since the lock exists only to stop off/on/off walking around the cap. `Threshold`/`Floor`/`Forced*` stay exempt and still take the **whole** balance. **(3)** Three **Telegram-only** toggles (Cashout on, Zero out on, **Any win off**) — a deliberate reversal of #123's no-Telegram-for-wins, made safe by being opt-in; "zero out" = the balance can no longer cover the stake, which needed a **server-side** `ZeroOutNotifiedAt` because the shipped pause state is client-derived in `localStorage`. **(4)** A **session-gated, paged, uncached `GET /orbit/bets`** rendered below the `/orbit` main card — bets and results only, no skipped rounds; ⚠️ pairing is on **`BetId`, not `GameId`** (`GameId` is set on `BetDebit` rows only — it *is* the placement claim), and a `BetDebit` with a **null `BetId`** is excluded because that placement never became a bet. **(5)** **STOP** — a new state separate from `Enabled` that skips the planner and **nothing else**: no forced cash-out, no cool-off, claim still held, deposits/cash-outs/notifications live, instant resume; the automatic `isPausedForFunds` keeps the word "paused". **(6)** Deposits accepted **during the cool-off** (enable-first refusal stays). One additive migration. ⚠️ **Three shipped deviations from this spec:** the endpoint is **`GET /orbit/history`**, not `/orbit/bets` (`RateLimitSettingsTests` guards the money bet path with a blunt `/bets` substring check, and weakening that guard for a read-only page was the wrong trade); the **`useGame.bet()` pre-pay guard (§7.5) was NOT built** — it needs an "is this word reserved?" lookup, which *is* the Orbit-membership probe the #118 invisibility rule forbids, so protection is the backend refusal + automatic refund; and deposits-during-cool-off needed an **explicit named exception to enable-first**, not a deletion (disabling clears `Enabled` *before* writing the row the cool-off keys on, so removing the cool-off refusal alone was a no-op). ⚠️ **Fixed a pre-existing money bug on the way:** `OrbitLedgerService.ResolveBalance` read the ledger sum and the materialized column in **two statements** under READ COMMITTED, so they could straddle a commit and "self-heal" the column to a mid-flight total, leaving a player **short by exactly one stake** — now one **REPEATABLE READ** snapshot with the correction guarded on the column still holding what was read. The merge-order hazard is **resolved**: #141 shipped first, 2026-08-06. Trello #150 ([fiKfaawz](https://trello.com/c/fiKfaawz)). | Shipped |
| `specs/orbit-single-control.md` | **Feature, `monkify-backend` + `monkify-front` + `monkify-meta` — ⚠️ no Trello card (designed and shipped in-session with the owner, 2026-08-07), continues `feature/orbit-new-ui`, front+backend ship together (API contract change):** Orbit's FOUR points of control (`Enabled` with forced-cash-out-on-disable + cool-off, #150's `Stopped`, the re-enable lock, the manual gap) collapse to **ONE** — `Enabled` alone, with pause semantics: flipping it **moves no money, signs nothing, has no cool-off** (`Stopped` dropped via migration `MergeOrbitStoppedIntoEnabled`; `OrbitReEnableLock`/`ReEnableLockHours`/`COOLING OFF` **deleted**, `ForcedOnDisable` left enum-only for ledger history). "Leave with my money" = a plain **manual cash-out**; a full cash-out IS `Manual` and consumes the rolling gap, which closes the off/on/off loophole the lock existed for and is now Orbit's **only** cool-off. **Deposits gate on the enrolment existing** (game pick creates the row) — a parked Orbit takes deposits and only RUN starts play; the auto-park persists `Enabled = false`. **Word claim follows playability** (`Enabled` AND balance ≥ stake; preference survives, RUN re-attempts). Front: control **duo** (RUN/PAUSE + neutral CASH OUT), 4-value `orbitStatus`, live gap countdown with self-enabling controls. §7.5 carries the same-day post-deploy owner review of the modal: switch guard (any game mismatch, running or parked; switch clears a non-fitting word), game picker riding the open panel to the game page, auto-point, the word gating ADD MONEY + rules, preset picker-only + membership validity, `markHandled` silencing the raw 400 toast. | Shipped |
| `specs/orbit-new-ui.md` | **UI re-presentation (frontend-only), `monkify-front` + `monkify-meta` — no backend (AC17 asserts a zero diff):** the `/orbit` page rebuilt from two owner mockups after the redesigned modal (`66e4df3`) absorbed turn-on and add-money, leaving the page's step metaphor describing a setup order the modal no longer defers to. `orbit-page-restructure.md`'s **five-step collapse-to-dashboard list becomes five tabs** (`① GAME · ② MONEY · ③ STOP RULES · ④ RECOVER · ⑤ TELEGRAM` — all reachable at any time, none collapsing), and run/stop/turn-off leave the card entirely for a **control trio below it**, specified against **all seven** `orbitStatus` values, with the **current state rendered as current** (filled, inert, `aria-disabled`) rather than as a pressable button — the mockup drew `RUN` and `STOP` equally pressable under a `● RUNNING` header, which asks the player to work out which state they are already in. `TURN ORBIT OFF` is an **outline** control, deliberately subordinate: it forces a cash-out and starts the cool-off while the pause beside it is free. ⚠️ **Turn-off is kept OUT of `STOP ORBIT`** — folding them would re-couple exactly what #150 split, making every casual stop cost a forced cash-out and a re-enable wait. `MONEY` owns **both** everyday money moves (add + cash out); tab 4 narrows to **`RECOVER`** (the signature repair path) because depositing must not sit behind a tab that reads as troubleshooting. Type scale steps **up one class** under a `.orbit-page` scope (title 20px, section title back to **11px/ink-dim**, body 20px, stat values 32px, fields `--ctl-h-lg`) — ⚠️ *not* a reversal of the 2026-08-05 9px ruling, whose stated principle is **"the RATIO to what a title introduces, not an absolute floor"**; the controls (48px) and numbers (32px) scale with it, so 11px ink-dim ranks *lower* than 9px did in the 380px panel. Adds **`--ctl-h-xl` (56px)** to the control-sizing scale in `colors.css` — an extension documented in the same commit, deliberately **without** a mobile step-up (matching `--ctl-h-lg`, which has none; the trio takes its touch target from width). ⚠️ **Three components are SHARED with the redesigned modal and must not be reshaped** — `OrbitStatusBanner`, `OrbitAmountField` and `OrbitWordField` are imported by **both** surfaces, so the tabs consume them as-is and wrap rather than edit (AC12 asks for byte-identical *files*, while their *rendering* inside `.orbit-page` is expected to scale); `OrbitStepRow` has one consumer and is **deleted**. `OrbitPage.vue` drops **1,379 → ~250 lines**, each tab owning its fields *and* its handlers — explicitly to avoid the trap that took `OrbitModal.vue` from the 916 lines #141 complained about to 1,397. Dropped from the mockups: the **`ROUNDS TO PLAY` stepper** (no `roundsToPlay` exists — it needs a column, a migration, planner logic and an integration test, and cannot be faked with `cashOutFloor`, which is balance-based so any win pushes the target out of reach) and the **`× CLOSE` / `Press ESC`** chrome (modal furniture on a route; the page's `◀ Back` already holds that corner). Bet log stays **below** the card — a record, not a setting. Tab state is local, **not** a route param, so `/orbit` keeps one URL and `EMBED_REDIRECT_ROUTES` is unchanged; the page opens on `② MONEY` when `needsFirstDeposit`/`isPausedForFunds`. Mobile: the strip **scrolls** with scroll-snap (two rows rejected — breaks the seam that makes tabs read as tabs; `<select>` rejected — hides the map of what Orbit can do). Page-level notices sit **above the tab bar** so a result raised in one tab survives switching to another. No integration test (rule 8 scopes to **new features**; this re-presents shipped behaviour with zero backend change) — the bar is `yarn lint` + `yarn build` + a **seven-state manual pass**. ⚠️ **Merge-order hazard: `feature/orbit-ui-overhaul` must land in `main` first**, or this branch drags #141 *and* #150 in with it. Branch `feature/orbit-new-ui` in all three repos, pushed; worktrees keep the older `orbit-ui-overhaul` directory slug. Designed in a brainstorming session 2026-08-06; **no Trello card**. ⚠️ **Shipped 2026-08-07** via `feature/orbit-new-ui` → `main` — the merge-order hazard is spent (#141 landed 2026-08-06 first), and the same merge carried `specs/orbit-single-control.md`, which reworked this spec's control **trio** into the RUN/PAUSE + CASH OUT **duo** (its §7.3 status table supersedes this one's seven-state trio). | Shipped |
| `specs/house-seed-bet.md` | **Feature, `monkify-backend` + `monkify-meta` — `monkify-front` NOT affected:** every newly created round is automatically seeded with **one ordinary paid bet from a dedicated house wallet**, on **all three** game types (Cosmic Sprint, Beast Mode, Space Jackpot), **in production only** (hard `IsProduction()` gate, no config flag). A new `HouseSeedBetConsumer` on the already-published `GameOpenedEvent` waits a **randomised delay clamped inside `MinimumWaitPeriodForBets`**, then `HouseSeedBetService` derives a choice from the round's committed `ServerSeed` (reusing `OrbitChoiceGenerator.DeriveChoice`), signs an SPL `TransferChecked` with memo `seed:{gameId}`, and registers the bet through the ordinary MediatR `RegisterBetRequest`. ⚠️ **The defining property: the seeded bet is an ORDINARY bet — no marker column, no migration, no DTO field, no validation bypass, no payout redirect, no branch anywhere.** That is possible because the house wallet is **both the transfer's sender and the bet's own `Wallet`**, so `ValidateBetPayment` (sender matched on `bet.GetWalletAddress()`, recipient on `token.TokenOwnerPublicKey`) **passes untouched** — the exact situation that forced Orbit to carry `Bet.FundedByOrbit`, whose **custody account** signs for a *different* player's wallet. ⚠️ **`FundedByOrbit` must NOT be set here** (it would skip a validation that passes and route a win to the player-custody account). It **counts toward `MinimumNumberOfPlayers`** for free via `BetPlacedConsumer`'s distinct-`PlayerId` count, so **one real player now starts a round**; it funds the pot, can win, and is paid/refunded by the existing paths. Two code-exploration findings promoted to spec decisions: the house **`Player` row must be resolved/created BEFORE the choice is derived** (both derivations key on a `Guid playerId`, which `RegisterBetHandler.UpsertPlayerAsync` only creates *during* registration), and a free-text candidate must clear **`OrbitChoiceClaims`** as well as `GameBets` (#150's cross-round reservation, `RegisterBetHandler.cs:286-292` — a pre-existing gap in `PlaceOrbitBetConsumer`, recorded but NOT fixed here). Treasury guard: house wallet == `TokenOwnerPublicKey` (or == Orbit) ⇒ **seeding disabled + `Fatal` log, host still boots** (aborting Consumers would take down the game loop over a seeding misconfiguration). **Retires `SpaceStroker.Seeder`** (`specs/auto-bet-seeder.md`, marked superseded) — running both would put two house wallets in a round and reach the 2-player minimum with **no humans in it**. Accepted: seeded bets count in analytics/stats/history (no marker to filter on), **no kill switch without a deploy**, and seeding is **refused on any type with `MinimumNumberOfPlayers < 2`** because the grinding-resistance invariant needs at least one human bet seed in the typer keystream. Trello #155 ([34W17kio](https://trello.com/c/34W17kio)). ⚠️ **Not yet live in production** — the gate is `IsProduction()` and the house wallet must still be created, funded (token + SOL), verified as neither the treasury nor Orbit, and put in Secret Manager; until then the resolver reports "not configured" and seeding is inert. 901 unit / 116 integration green (48 + 6 added, no existing test changed), verified after merging `origin/main` in — the `orbit-new-ui` train had changed `ISolanaService`'s signature meanwhile. Trello #155 ([34W17kio](https://trello.com/c/34W17kio)). | Shipped |
| `specs/money-changing-animations.md` | **Feature, `monkify-front` + `monkify-meta` — `monkify-backend` NOT affected:** money stops moving silently. One shared `MoneyValue` readout rolls a figure old → new (~600ms, `tabular-nums`) while a floating ± chip rises and fades — **credit green (`--color-live`), debit DIM INK (`--color-ink-mute`), never red**, which is what keeps `orbit-ui-overhaul.md`'s *red = warning only* rule alive (a per-round red debit chip on a **healthy** Orbit would burn it). The app has **zero** existing count-up/flash/transition infrastructure: `arcade.css` has no `@keyframes` at all and Tailwind declares no animation extension, so this is greenfield. ⚠️ **Only ATTRIBUTED changes animate** — either the amount is known up front (stake, deposit, cash-out: applied optimistically) or the *cause* is known and the read is **armed** (win, refund: the discovered delta publishes only if its sign matches the cause). Unattributed poll drift, `visibilitychange` catch-up, idle return, mint switch and first load land **silently**, because a `−1.00` stake and a `+5.00` win inside one poll window would otherwise collapse into a single misleading `+4.00`. Surface half: the header's two 40px `.btn-door` squares become **balance-carrying pills** (`[◎ 5.00]` violet Orbit, `[▤ 12.34]` amber wallet) — colour+icon supply context, **no text label** — and the `SPACE STROKER` wordmark is **cut below `lg`** to pay for the width (the bar is 54px there while `--ctl-h-md` *grows* to 44px). ⚠️ **This knowingly REVERSES the 2026-08-04 "a DOOR, not a readout" call** recorded in `OrbitLauncher.vue:7-17` + `GameHeader.vue:258-274`, narrowly (a door that has a number to show shows it); re-taken by the owner 2026-08-07. Orbit's auto-bet stake **pushes no event and is not polled**, so it is decremented **optimistically frontend-only** when the live bet stream shows the round's own bet is Orbit-funded — chosen over adding a bet-placed kind to `OrbitNotification`, which would have made a UI card cross-repo; accepted: silent when Orbit bets on a game the player is not watching. Wallet balance is lifted out of `TokenBalance.vue`'s private state into a **`useTokenBalance` singleton** (required, not tidying — the delta chip needs ONE place that knows old vs new), killing the two duplicate Orbit fetches; ⚠️ code finding: `getTokenBalance` is one of the few GETs **not** behind `singleFlight`, so three undeduplicated readers hit the endpoint today. ⚠️⚠️ **Spec-resolved hazard (§7.3.2): the optimistic Orbit delta must NEVER be written into `orbit.value.balance`** — `useOrbit.ts:1077`'s auto-park watcher would then **PATCH `enabled: false` in the DB on a guess**; it lives in a separate display-only `orbitOptimisticDelta` and every affordability rule keeps reading server truth. Phones keep the full animation (a **one-shot**, a documented exception to light-graphics' `animation: none` policy for *infinite* animations — do **not** add it to the strip list); `prefers-reduced-motion` keeps the information and drops the movement (snap + in-place chip). Mockups gated the build: 3 header + 3 animation options, user picked one of each before production code. **Picks: header H1 "twin pills"; signed-out Orbit reads `ORBIT` at full pill width (gated on `hasSession && orbit !== null`, never on the balance, so a real `0.00` still renders); animation A2 "ledger slip".** No backend change ⇒ **no integration test** (`constraints.md` §8 scopes to `monkify-backend`) and `monkify-front` has **no test harness at all** — the bar is `yarn lint` + `yarn build` + a manual pass. ⚠️ **Shipped 2026-08-08 with THREE owner deviations from this spec** (the feature doc is authoritative where they disagree): (1) **the chip rises from BELOW and never above the number** — §7.4/D3-D4's *"the downward travel carries the direction"* is **false**, travel is identical both ways and colour + the `+/−` sign carry direction alone; structural, because the header bar is the top of the viewport and a chip above would be clipped; (2) **a credit HOLDS ~2.5s, a debit does not** — one `CHIP_MS = 900` became `CHIP_MS_CREDIT = 3400` / `CHIP_MS_DEBIT = 900`, which **must stay in sync with the CSS durations**; (3) **header figures are 10px/12px with a `$` prefix**, header-only. ⚠️ **One instruction could NOT be followed:** §7.2.5/§7.3.3's "move the watchers to module scope" **crashes the app at boot** (they derive from `useWallet()`, which throws until `main.ts` runs `initWallet()`, and module bodies execute at import time) — the shipped code uses the files' existing register-once flag idiom instead. Trello #151 ([AaPjqrw5](https://trello.com/c/AaPjqrw5)). | Shipped |
| `specs/redesign-nice-win-modal.md` | **Redesign (frontend-only), `monkify-front` + `monkify-meta` — no backend (AC17 asserts a zero diff):** `OrbitFirstWinModal.vue` — the once-ever Orbit upsell that fires after a player's first **paid** win — was an 832px **black** panel with a cyan `NICE WIN!` titlebar and **28 words** of documentation-voice copy that never said *Orbit*. It becomes `variant="orbit"` + `size="compact"` (**540**, not the doc's stale 608 — `ArcadeModal.vue:173` is truth), titled **`ORBIT`** with `<OrbitMark />` in the `titlebar-lead` slot, `NICE WIN!` promoted to a large **in-body** headline, the **winning word rendered as the hero** beneath it, and **one** short supporting line. ⚠️ **The supporting line is CONDITIONAL on game type, and that is the design, not a nicety:** `orbit-single-control.md` makes two qualifiers mandatory whenever word ownership is advertised (**free-text games only**, **only while Orbit runs**), and this modal fires after *any* paid win — so a flat "your word stays yours" is **false** for a Beast Mode winner. Rendering the claim beat only where it is true removes the need for `HomeOrbitCard`'s "type-it-yourself games" clause; `while it runs` stays inline. #118 is unbroken: it says **that** a word can be claimed, never **which** or **whose**, and adds no reserved-word probe. ⚠️ **Expression trap, spec §7.2:** the free-text flag must be `!!selectedGame.value && !presetChoices?.length` — the natural `!selectedGame.value?.presetChoices?.length` is `true` when the game is **undefined**, which would assert word ownership on unknown ground (AC8). ⚠️ **`variant="black"` / `.dialog--black` is KEPT and becomes CONSUMER-LESS** (deliberate, D2) — the tutorial/help **overlay** tier therefore has no implementation and `conventions/frontend.md` must say so; `.arcade-frame--black` (the guide's `GuideTip`) is the black *material's* remaining live consumer and is untouched. Two new `arcade.css` content helpers (`.dialog-headline` ink, `.dialog-word` full `--color-orbit` per #141's "frame deep, content full" split) with explicit wrap/shrink maths for the **20-character** maximum choice. **Rejected:** showing any amount (the payout is unknown at that moment — settlement is async and the pot splits across winners minus commission), any motion, and touching the trigger/guards/`seen` key. Rule 8's integration test **does not apply** (no backend surface); `monkify-front` has no test runner, so the bar is `yarn lint` + `yarn build` + the 8-step manual pass. Trello #157 ([fcklCgcd](https://trello.com/c/fcklCgcd)). | **Ready for implementation** |
| `specs/api-error-messages.md` | **UX/bug fix + copy pass, `monkify-front` + `monkify-backend` + `monkify-meta`:** a rejected bet says **"Request failed with status code 400"** — `services/handlers.ts` toasts `error.message` (the axios status line) and discards the sentence the backend wrote (*"This sequence has already been taken by another player… Your bet should be refunded soon."*). Fixed **in the interceptor**, so all **23** `mainAPI` call sites (14 `mainService` + 9 `orbitService`) are fixed at once with no per-call-site wiring: toast `messages[].value` when usable, else one friendly fallback (`Something went wrong — please try again.`), **deduped by message text** via `toastId`, `handled === true` still winning, raw error to `console.error` only, status codes never reaching the player. New `utils/apiError.ts` replaces **5** hand-rolled extractions. The terminal keeps its generic `> There was an error processing your bet` — `GameTerminal.vue` and `submitBet`'s catch are untouched, and all four bet paths (free, desktop SolanaToken, wallet-bridge popup, mobile deeplink resume) inherit the fix through `submitBet`. ⚠️ **Four card claims the code contradicts (spec §3 — the spec wins):** (**F1**) the specified backend rewordings are **already byte-identical to the live `.resx`**, so the copy pass was **retargeted** (owner, 2026-08-07) at the awkward *live* payment-signature keys (`SignatureWithoutBetAccount`/`WithoutOwnerAccount`'s "signature **have** not sent", `SignaturePaid{Less,More}ThanBetAmount`'s trailing space + "less tokens…informed in the request", `InvalidPaymentSignature`, `PaymentSignatureHasBeenUsed`, `AdviceOnDifferentSignatureBetAmount`'s sentence fragment — **`{0}` must survive**, `SolanaService.cs:674` formats `SupportEmail` into it — and `FailedToRegisterBet`); `ChoiceAlreadyTaken`, `RefundWarning`, `GameNotValidForBets`, `OneBetPerPresetRound`, `SecondBetNeedsAnotherPlayer`, `DuplicateBet*Error` and **all five `BetValidationResult` `[Description]`s stay untouched** — `BetValidationResult.cs` is not edited; (**F2**) the tests assert **symbolically**, so `run_tests.bat` must be green with **zero test edits** (the one literal, `ChoiceExclusivityFlowTests.cs:103` `ShouldContain("invalid characters")`, points at a key this card does not touch) — an edit means a forbidden key was reworded; (**F3**) `MessageType` is `Informational 0 / Alert 1 / **ValidationFailure 2** / Error 3` and serializes **numerically** (no `JsonStringEnumConverter`), so `BetRefundManager.vue:13` + `GameTypeManager.vue:48` filtering `type === 1 /* ValidationFailure */` match **Alert**, never match, and always fall through to a generic fallback — the helper prefers **2**, which knowingly **amends AC 11** (those two screens start showing real messages); (**F5**) `'token-expired'` exists only as an `EmbedEventType` union member and `setRelaunching` has **zero callers**, and `mainAPI` has no 401 branch at all — AC 8 is a **no-regression** check, not a flow to preserve. ⚠️ **Work the card did not have (§7.4, AC 15):** once the toast is meaningful, every site that already reports the failure itself would say it **twice** — `useGame` (~381), `useTokenBalance` (**both** catch branches, or the silent background poll starts toasting), `DisplayNamePrompt`, `PlayerCustomizationModal` must mark `handled = true`, per the precedent `useOrbit.ts:212` documents. ⚠️ **#150 invisibility:** the cross-round reserved-word reject stays **byte-identical** to a same-round collision (same message, refund path and timing) — a distinct "reserved" wording announces that someone runs Orbit; no pre-pay claim check and no reserved-word endpoint (accepted cost: retrying the same word pays and refunds again). ⚠️ `constraints.md` §6 preserved — `GameNotValidForBets` and every auth/wallet-proof message stay exactly as vague. ⚠️ The pre-pay guard (`useGame.ts:605-616`) inspects only the **live bet list**, so it already blocks the card's headline same-round scenario before payment — the **cross-round Orbit claim is the only reliable repro**. ⚠️ Free bets get **no** refund sentence for free: `RefundInvalidBet` appends `RefundWarning` only on the paid branch. Noted, not fixed (§3 F6): the `Wallet*`/`Rg*` families + `SignatureForInvalidToken` are **dead** (0 refs, operator-era leftovers still saying "contact your operator"), and `ParameterNotFound` vs `ParameterHasNoActiveGames` distinguish two game-type states in tension with §6. No integration test required (`AGENTS.md` rule 8 scopes to **new features**). ⚠️ **Two owner decisions at implementation time that the spec did not foresee:** the **trailing space** on `SignaturePaid{Less,More}ThanBetAmount` was **load-bearing** (the only separator before `AdviceOnDifferentSignatureBetAmount`), so `SolanaService.GenerateErrorMessageForTokenExchange` joins with an explicit `" "` and that file joins the backend's two; and **§7.4's "own toast — unchanged" was wrong for three sites** — `GamePage.vue`, `GameTypes.vue` and `ResponsibleGamingPage.vue` toasted **`err.message` themselves** (the status line), with `ResponsibleGamingPage` **not** marking `handled` so a failed self-exclusion showed it **twice** — all three fixed via a second helper `displayableErrorMessage(err, fallback)` that keeps a genuinely thrown sentence (a wallet that cannot sign) but never an HTTP status line. Shipped green: **901/901** unit + **116/116** integration, **zero test-file edits**. ⚠️ Known gap left in place: 8 `useOrbit.ts` sites still fall back to `error.message`. Trello #158 ([cmb6wfm7](https://trello.com/c/cmb6wfm7)). | Shipped |
| `specs/background-lifecycle-hardening.md` | **Bug fix + QOL (frontend-only), `monkify-front` + `monkify-meta` — no backend at all:** switching Chrome tabs while rounds roll over drives the game screen into a broken, laggy state ("many BGs", nothing spawning, speed never recovers), and the terminal scrolls **horizontally**. Six defects, one root-cause family: **the animation layer trusts `setTimeout` and in-flight transitions across tab hide/show**, and Chrome throttles (then freezes) timers in hidden tabs. ⚠️ **The headline defect is arithmetic, not throttling:** `GameBackground.vue:308-311` adds the pause duration to `speedTransitionStartTime` — but if the `speedMultiplier` watcher already fired **while hidden** it had reset that stamp to a *later* `performance.now()`, so the sum lands in the **future**, `progress` goes negative, the ease function **squares** it (`2*p*p`) and the transition runs **backwards**; speed is then **cubed** (`SPEED_BOOST_FACTOR = 3`), making a wrong `currentSpeed` up to a **~27× error** on both drift and spawn cadence. Fix = **resync, not resume**: re-derive speed from the CURRENT game status, advance/drop elements by elapsed wall-clock, re-arm spawning off `document.hidden` directly (not the desyncable `let isPageVisible`), sweep orphan torpedoes. A full remount was **rejected** (visible starfield restart on every tab switch). Every timer becomes a **wall-clock deadline + `visibilitychange` catch-up**, the convention `useIdleDisconnect.ts:26-29` already states ("Timestamps, NOT timers"). `gradualSpeedReset()` keeps its `setTimeout` **only** as the visible-tab fast path. `TorpedoSpawner`'s 15s removal timer was already **shorter than its own 22s `blast` animation** → `animationend` + a `BLAST_MS = 22000` deadline. **Scope added in refinement:** one exported **`BG_DENSITY` (0..1)** in a new `src/constants/background.ts` scaling the per-layer element cap, the spawn rate **and** a new concurrent torpedo cap together — today 2 layers × `MAX_ELEMENTS 64` = **up to 128** elements, and the secondary layer spawns every **~37ms** at speed 3 (`1000/27`), so a `SPAWN_DELAY_FLOOR_MS` lands with it. ⚠️ **Light mode is a `Math.min` FLOOR over the density value, never a competing knob** — `BG_DENSITY` can only make light mode cheaper, never heavier (`features/light-graphics-mode.md` stays true). The speed→spawn coupling is **kept** (fast typing must still read as a busier sky); density **divides** the delay so both dimensions move the same direction under a future slider. **Same density everywhere** — no per-route override, or `/space-stroker/background` would drift from the in-game look it exists to capture. ⚠️ **Two findings from code exploration the card did not have** (spec §1.2): (**A**) the `@media (max-width: 768px) { background-size: 200% }` rule is **dead code** — it is declared *before* the `<=1024px` `150%` rule at equal specificity, so **source order makes 150% win at every width ≤768px**; the seam fix must derive the shift from what the browser **actually applies**, and must **not** "fix" 200% into effect (that would change the mobile zoom, which the card forbids). (**B**) `overflow-x-hidden` alone would **clip a winner's raw external id** — `winnerLabel` falls back to `return winner` and those `PAID TO` `<p>`s set no wrapping, rendering in exactly the non-`InProgress` state where the X-scroll was reported; the overflow fix therefore carries `break-all` with it. Rule 8's integration test **does not apply** (bug fix + QOL, zero backend surface); `monkify-front` has no test runner, so the bar is `yarn lint` + a 14-step manual pass. ⚠️ **The numeric `BG_DENSITY` default is NOT decided in the spec** — the implementer picks a start value, deploys to the local Docker stack, and the **user approves the look** before the card advances (AC 17); the ACs assert the mechanism, never a magic number. Trello #149 ([No2Z0NMP](https://trello.com/c/No2Z0NMP)). ⚠️ **Two things changed at implementation time and the spec records both:** a **seventh defect was folded in** on the user's call (§1.4, §7.7, AC 19) — DaisyUI's `:root, [data-theme]` base rule outranks `main.css`'s bare `html`, so the **root element** painted `#2A303C` instead of `--color-void`, and because the root's background paints the **canvas** while `<body>` paints only its own viewport-height box, every page taller than the screen carried a hard colour seam one viewport down (fixed with `html:root, body`, which is (0,1,1) and so wins regardless of stylesheet order — a plain `:root` would only tie); and **`BG_DENSITY` landed at `0.45`** (29 elements/layer vs 64, ~33 on screen vs up to 128, torpedo cap 5, light mode still floored at 8/layer), approved at the AC-17 visual gate. **Post-ship:** defect #6's tile-seam fix is **near-unreachable in practice** — light mode freezes the backdrop below 1024px, so only a viewport of exactly 1024px was ever wrong; it is insurance, and defect #1 was the actual cause of "many BGs". | **Shipped** |
| `specs/graphics-settings-controls.md` | **QOL / small frontend change (not a "feature"), `monkify-front` + `monkify-meta` — zero backend surface:** the app's visual cost is decided **for** the player. Light graphics mode (#83) is auto-detected on every load with **no toggle and nothing persisted**, and the starfield density is the hard-coded module constant `BG_DENSITY = 0.45` (#149) — whose own comment says it exists so *"a future user-facing slider must be able to write THIS VALUE ONLY"*. This card is that slider plus the manual switch, both in the existing SETTINGS modal (`PlayerCustomizationModal.vue`). **Manual choice wins permanently** — auto-detection only *seeds* a browser that never touched the control (rejected: tri-state Auto/On/Off; rejected: one-way force-ON, whose unticking would silently re-tick and read as a bug). **Phones stay locked light below 1024px**, rendered **ticked + disabled with a note rather than hidden**, which keeps every rule that depends on "every viewport below 1024px is permanently light-graphics" intact (frozen backdrop drift, torpedoes off, the unreachable tile seam, the `.money-value*` exception). Density is a **percentage `0–100%`, default 45%, 5% steps** driving all three #149 consumers together (element cap, spawn cadence, torpedo cap); **`0%` is genuinely off** — no sprites, no spawning, no torpedoes and an **idle rAF loop**, with the static backdrop tile, `.section-vignette` and `--color-void` still rendering. ⚠️ **Light mode stays a `Math.min` FLOOR over the density, never a competing knob**, and `usePerfMode.ts` learns the **override only** — it must **not** become density-aware (rule written in the KB twice). **`localStorage` only, zero backend** (a hardware setting belongs to the device, and it must work with no wallet connected); **instant apply, and `CANCEL` does not revert** — `SAVE` only renders with a wallet connected and prompts a signature, so routing graphics through it would leave a wallet-less player unable to save at all. ⚠️ **Ten code-exploration findings the card did not have** (spec §4), of which four bind hard: (**F2**) `/background`'s canonical-defaults rule **cannot** be a route-local prop — both `GameBackground` layers live globally in `App.vue` — and a route-`meta` watcher alone paints the stored density for the first frames, so the canonical flag must be **seeded synchronously from `window.location.pathname`**; (**F3**) the "~13%" boundary in AC 9/10 is **10% on a 5% grid** (`round(64 × d) ≥ 8` ⇔ `d ≥ 0.1172`), so 15%+ is flat at 8 and only 10% (→6) and 5% (→3) bite; (**F4**) idling the rAF loop at 0% also **freezes the backdrop drift**, because `animate()` is the only thing advancing `bgPosition` — intended, not a bug; (**F7**) the accessible info `i` the card asks for **already exists** as `OrbitInfoTip.vue` but hovers/focuses violet, so it is promoted to `ui/ArcadeInfoTip.vue` with a `tone` prop and the four Orbit call sites updated (renamed, **not aliased** — the #135 call). Also: `.orbit-autopick` is **page-scoped** in `OrbitGameTab.vue`, so the modal needs new `.dialog-check*` classes in `arcade.css` joining the "ONE focus treatment" list, and the two degenerate-value guards (`Math.max(1, …)`, `Math.max(density, 0.01)`) need a **zero carve-out**. `AGENTS.md` rule 8's integration test **does not apply** (zero backend surface, and not a new feature); `monkify-front` has **no test runner**, so the bar is `yarn lint` + `yarn build` + a 17-step manual pass. Trello #161 ([FX5TpF6l](https://trello.com/c/FX5TpF6l)). ⚠️ **Shipped 2026-08-09.** Two spec deviations, both recorded in §14: **no element-count hint** in the slider readout (it would mean exporting `MAX_ELEMENTS` out of `GameBackground.vue`, which the KB pins there) and `GuidePlayerPreferences` drops to `columns="2"` so the new fourth row does not orphan. **One owner revision at the visual gate:** the group first shipped with the slider **stacked under its label**, making it ~72px tall against the checkbox's ~25px — rebuilt as a single centre-aligned row with the label *before* the track in `.dialog-copy` and a fixed-width readout. **Four ACs shipped unverified** (mid-round SignalR/audio, tab-hidden-across-rollover, embedded iframe, a real screen reader) — each exercises machinery this change does not touch, but none was driven end to end. Distilled in [`features/graphics-settings-controls.md`](features/frontend-ux.md). | **Shipped** |
| `specs/icons-and-menu-overhaul.md` | **Feature (frontend-only), `monkify-front` + `monkify-meta` — `monkify-backend` NOT affected (no entity/enum/handler/consumer/EF/migration/endpoint/SignalR surface; game art is client-side in `utils/gameArt.ts`):** the app has **no icon system** — nine feather-style **stroke** SVGs pasted inline in two files (`GameHeader.vue` speaker ×4 + hamburger, `WalletButtons.vue` wallet ×3), a literal VT323 letter **`X`** as every modal's close button, `✓`/`✗`/`▶`/`▼` **text characters** standing in for glyphs, `spinner.svg` and `usdc.svg`. The only real pixel art is Orbit's `OrbitMark` + `OrbitGlyph`, whose own source says they are *"NOT the seed of an app-wide icon system (#124 D7/AC16)"* — **this card retires that rule** and makes Orbit's technique the house technique. Ships **26 icons** in one `ICONS` registry + `ui/AppIcon.vue`: pixel-grid inline SVG, `shape-rendering="crispEdges"`, `currentColor`, `aria-hidden` (icons are **decorative** — the accessible name always comes from the control). **Two grids, declared PER ICON:** coarse **8×8** (each cell 2 units of a 16-unit box) is the default, exact at **16/24/32/40**; fine **16×16** is a **standing, reusable standard** for any future subject the coarse grid cannot hold, exact only at **16/32/48** — today **`practice` alone** uses it (a console gamepad: wings, d-pad cross, four-button diamond and two centre dashes do not exist in eight cells across). That two-grid split is **why the menu renders at 32px** — the one size both grids land on — and why the popover widened **234 → 270px**. Style **A · Solid** (rejected: **B Capped**, a notched keycap plate whose +4 units break 24px and which is heavy furniture on a 40px header button; **C Duotone**, which muddies below ~20px and fights bright surfaces). ⚠️ **Orbit's seven drawings are folded in BYTE-FOR-BYTE as raw rects** and are **not rasterised** by the renderer — that verbatim path is what guarantees *zero visual change* at their 20 call sites; **Orbit's mark is NOT redrawn** (a heavier re-cut was mocked and rejected). ⚠️ **Stake and pot split, Orbit included** (owner confirmed): **one coin = the stake** you put in, **a stack of three = the pot** on the table, and the three-coin stack is Orbit's shipped `stake` glyph **reassigned to `pot`** — so `OrbitStatusBanner`, `OrbitStatStrip` and `/orbit` move to the single coin, and the code comment arguing *"Three coins seen edge-on: what ONE round costs"* must be **rewritten, not left contradicting**. The ☰ menu becomes a **3×3 compact pad** (270px, 32px icons above centred PS2P labels): Home · Practice · Orbit · Stats · Fair · How to play · History · Responsible · Settings — **Home and Practice are new to the menu**, **`/game` gets no cell**, and Fair + Settings keep cells that look identical to the route cells despite opening modals. Current page gets an amber wash + `aria-current="page"`; dismissal unchanged (click / outside / Esc) with **no arrow-key roving**. **Embedded renders the SAME grid** with its two survivors (History + Settings) — no alternate layout, because the embed must not own the only conditional branch. ⚠️ **Five findings from code exploration the card did not have** (spec §1.3): (**A**) only **one** of the four `▶` sites is a real disclosure (`OrbitPage.vue:552`) — the others are a navigation button and two inline text links, so only that one rotates 90°; (**B**) the artifact's `tone:` fields are **preview-cell tinting, not fills** — promoting them would break AC3 *and* turn Orbit's `run` glyph green, so **only `win-at` and `falls-to` carry a baked fill**; (**C**) Orbit's call sites render at 11/13/14/18/20/26/28/30/52px, **off the new ladder** — **zero visual change wins**, do not "fix" them; (**D**) `.dialog__close` boxes the glyph at 20px (22px black), so the close icon renders at **16px** and the now-dead `font-family`/`font-size` go; (**E**) `html.light-graphics` strips the popover's blur but never makes it **opaque** — the pad needs a named class + an explicit override. Frontend-only ⇒ **no unit or integration test** (`monkify-front` has no test suite; `AGENTS.md` rule 8 has no backend happy path to drive) — the bar is `yarn lint` + `yarn build` + a 14-step browser pass with **0 console errors**. Drawings transcribed **verbatim** in spec §5 from the design artifact (revision 5). Trello #159 ([1omDew5a](https://trello.com/c/1omDew5a)). ⚠️ **Shipped 2026-08-09 with FIVE divergences — `features/icons-and-menu-overhaul.md` is current truth where they disagree:** (1) **24 icons, not 26** — the owner put Orbit's PER ROUND readouts on the **`usdc.svg` brand mark** (the asset the lobby's TICKET/POT chips already use), which left both pixel coins with **zero consumers**, so the stake/pot split above never shipped and both drawings were deleted (a pixel USDC is un-buildable: `AppIcon` renders **one fill per drawing**, and "blue disc + white `$`" needs two); `usdc.svg` itself was replaced with the **official Circle mark**, which also changed the lobby chips. (2) **The pad is 250px / 24px icons / VT323 15px**, not 270 / 32 / PS2P — rejected on sight as "way too big", and **the font was the defect**: PS2P's advance is exactly 1.0em against VT323's ~0.4em, so **no** PS2P size fits (at 8px labels still overflowed and `RESPONSIBLE` made its column 106px, shipping 75/114/85 until `minmax(0, 1fr)` fixed it); 24px knowingly gives up D18's both-grids-exact property. (3) **`wallet` was redrawn on the FINE grid** — two fine drawings now, not one; fine is free there only because every wallet call site renders at 16px, and opting in **costs the 24px rung**. (4) **Two spec statements were wrong:** `HomeOrbitCard`'s `OrbitMark` import **is** used (kebab-case `<orbit-mark>`, which a PascalCase grep misses), and "RESPONSIBLE fits an ~86px cell" was false. (5) **Scope gap closed** — §7 only ever wired the menu cells, so identity marks were added to `/game/stats`, `/game/history`, `/how-to-play`, `/responsible-gaming`, the Fair + Settings modals and the **five front-door section cards** (`HomeSection` gains an `icon` prop that **replaces** the `+ TITLE +` pluses, generalising `ArcadeModal`'s `.dialog--has-lead` rule). Verified before merge: `yarn lint` clean + `yarn build` complete + browser QA; **zero test files touched** (no backend surface, no front test suite). | **Shipped** |
| `specs/off-page-game-presentation.md` | **Bug fix (frontend-only), `monkify-front` + `monkify-meta` — `monkify-backend` NOT affected (no entity, enum, handler, consumer, endpoint, header, EF mapping, migration or SignalR contract):** `GameBackground` (×2) and `TorpedoSpawner` are mounted in `App.vue`, so they render on **every** page while reading `useGame` — a module singleton whose SignalR subscription is tab-scoped and **deliberately** outlives `GamePage` (`specs/unified-websocket-connection.md`). Leaving `/game/play` mid-round therefore leaves that round's `DisplaySpeed` / `Bets` / `Status` events driving the global layer: the sky keeps ramping to speed 2/3 on `/`, `/game`, `/orbit`, `/how-to-play`, `/game/history`, `/game/stats`, a torpedo fires per incoming bet, and the victory/lose one-shot (`useGame.ts:491`, module scope) lands on top of the lobby track or into silence. The in-game music **loops** already stop correctly (`GameAudio` unmounts and calls `stopLoop`) — the starfield/torpedo layer is the only game visual that is global, which is why it is the only thing that leaks. Fix = a **presentation gate at the consumer**: one module-level `inGame` flag in `useGame.ts`, written by an `App.vue` route watcher (`route.name === 'Game'`), read by three consumers — `:speed-multiplier="inGame ? typingSpeed : 1"` on both backgrounds, a guard in the torpedo bet watcher, and the sting. ⚠️ **`detachFromGame()` on leaving was REJECTED** — the same `Status → Ended` drives the balance re-read with win/refund attribution, the embedded `bet-settled` / `game-ended` signals and `useOrbitRedirect`, so detaching would silently break settlement feedback for the common case (bet → walk to the lobby → round settles while away). ⚠️ **`typingSpeed` itself is never mutated** — the typer and Edson read the same ref, so zeroing it would leave a returning player looking at a stalled round until the next `DisplaySpeed` event; gating at the consumer makes the return instant and free. The sting is **suppressed AND consumed** (`endAudioFiredForGame` still stamped) so it can never fire late; the **Orbit first-win modal is deliberately KEPT** firing off-page (paid wins only — the one off-page effect that is useful rather than decorative); nothing is purged on leaving (in-flight sprites and a torpedo mid-blast finish normally). ⚠️ **Seven code-exploration findings the card did not have** (spec §1.2), of which one changes the implementation: (**F1**) the card's suggested `v-if` on `<torpedo-spawner>` would **PURGE** — `onUnmounted` removes every tracked node and `#torpedo-container` is the component's own template root, so unmounting takes every torpedo out of the DOM instantly, violating AC 3; the gate goes on the **bet watcher**, and the `v-if` is left alone. (**F6**) that guard must sit **after** the `ignoreInitialBet` one-shot or an off-page bet leaves it armed and swallows the first torpedo after the player returns. (**F2**) `endAudioFiredForGame` is already stamped one line **before** the `playAudio`, so "consumed, not deferred" needs no reordering. (**F3**) `/background` currently spawns torpedoes for a live round too — fixed for free. (**F7**) the return needs **no new arithmetic**: `GameBackground`'s existing `speedMultiplier` watcher and `handleVisibilityChange` (which re-derives from the prop) cover AC 8 + AC 9 — **do not add a repair path**, that arithmetic is the ~27× speed error of #149. `/background` stops inheriting a live round's ramp — intended, and exactly what its `canonicalGraphics` flag already asks for. Bug fix with zero backend surface ⇒ **rule 8's integration test does not apply**, and `monkify-front` has **no test runner**: the bar is `yarn lint` + `yarn build` + an 11-step manual pass. Trello #162 ([FwzvH8fm](https://trello.com/c/FwzvH8fm)). ⚠️ **Shipped 2026-08-10 with ZERO deviations** — all three files landed exactly as §8.1 specified. ⚠️ **The §11 browser QA pass was WAIVED by the owner**, so **AC 1–10 shipped verified only by `yarn lint` + `yarn build` and code review**, never driven against a running app; AC 7 (balance re-read + embedded `bet-settled` / `game-ended`) is untouched code that nobody watched settle a round. Distilled in [`features/off-page-game-presentation.md`](features/frontend-ux.md). | **Shipped** |
| `specs/orbit-modal-improvements.md` | **Minor change (frontend-only), `monkify-front` + `monkify-meta` — `monkify-backend` NOT affected (no entity, handler, consumer, migration, endpoint or SignalR contract; every value it renders is already on `GET /orbit`):** the Orbit **panel** (`OrbitModal.vue`) and the `/orbit` page drifted apart after the single-control merge — the page collapsed to ONE RUN/RESUME↔PAUSE toggle plus a neutral CASH OUT (`OrbitControlBar`), while the panel kept the older **transport pair** (`RUN ORBIT` + `STOP ORBIT`, both always drawn, the matching one disabled) and had **no cash-out at all**, so money-out from a live round meant leaving for `/orbit`. The panel now mounts the **shared `OrbitControlBar` in a compact variant** (one component, both surfaces — a second copy is exactly how the status tag and the buttons came to disagree before) and gains **CASH OUT straight to the wallet signature** with no inline confirm (the signature IS the confirmation) and **never word-gated** (ADD MONEY keeps its gate — money-out must never be harder to reach than money-in). ⚠️ **THE SPEC'S CHANGE ③ WAS BUILT AND REJECTED ON REVIEW — read `specs/orbit-modal-improvements.md` §17 before §3.** Replacing the panel's `AUTO CASHOUT` editors with two icon'd button-links (STOP RULES + NOTIFS) was implemented, shown on screen and rejected ("this feels like a grid of buttons"); it was reverted from `HEAD`, so the editors and their `.rule-field*` styles are **byte-identical to `main`**. Consequently `orbit-ui-overhaul.md` §3.1 is **NOT** re-opened, `orbit-single-control.md`'s account of the panel's *editable rules* stays **true**, and **`src/constants/icons.ts` is untouched** (no `notify` glyph; the registry still carries exactly two baked fills). **The Telegram hook rides the panel's way-out link instead:** it reads *"Set up notifications and more in Orbit settings"* → `/orbit?tab=telegram` where a bot is configured, or *"More in Orbit settings"* → `/orbit` where none is — both halves flip together, so the copy never promises a tab the page will not render. `/orbit` gains the **validated `?tab=<id>` query** matched against the *visible* tab list, falling back to `game`; an explicit tab **beats** the "open on MONEY when unfunded" default. **Auto-save fix:** the word already debounces at 600 ms on both surfaces (false alarm, left alone) — the real defect is the **rule fields committing on blur/Enter only**, and it shipped on **BOTH** surfaces, not just the page: `OrbitStopRulesTab.vue` flushes **on unmount**, `OrbitModal.vue` flushes **on `open` going false** (⚠️ *not* `onBeforeUnmount` — the panel is `v-if`-ed on a feature flag, stays mounted all session, and Esc / backdrop / ✗ never fire blur). ⚠️ **1.2 s and not 600 ms** because every prefix of a number is a legal value and `CashOutAtThreshold` fires at *balance ≥ it* — a briefly-saved `1` while typing `100` can cash out for real and pause Orbit. ⚠️ **Findings from the code, not the card:** every `.orbit-trio*` rule in `arcade.css` was scoped under **`.orbit-page`**, so the bar rendered **unstyled** in the panel (fixed by de-scoping the block **and both `:focus-visible` lists**, plus a `--compact` modifier and the reduced-motion block the page instance never had); `useOrbit.cashOut` **returns rather than throws** on a mobile `'hopped'`, so the panel compares the balance across the await instead of toasting unconditionally; the debounce is armed from **`@input`**, never a `watch` on the input refs, which `syncRuleFields()` would re-arm into a **PATCH loop**; and `OrbitControlBar` gained a **`selfExcluded`** blocker (bound on both surfaces) because the panel's old `canRun` carried that guard and the bar had no equivalent — which also closes the same hole on `/orbit`. No unit or integration test (`monkify-front` has no test runner; rule 8 scopes to new features with a backend happy path) — the bar was `yarn lint` + `yarn build` + owner QA on the local Docker stack with `VUE_APP_ORBIT_ENABLED` on. Trello #163 ([77laekQh](https://trello.com/c/77laekQh)). ⚠️ **Shipped 2026-08-10.** Distilled in [`features/orbit-modal-improvements.md`](features/orbit.md). | **Shipped** |
| `specs/orbit-stop-loss-trigger.md` | **Minor change, `monkify-backend` + `monkify-meta` — `monkify-front` NOT affected (no entity, EF mapping, migration, config key, endpoint, event or SignalR contract):** Orbit's three settlement stop rules — the cash-out **floor**, the take-profit **threshold** and the **zero-out** — all decide on `IOrbitLedgerService.ResolveBalance`, which by then is **one stake too low**. `RunGameConsumer.cs:50-61` creates the successor round (→ `GameOpenedEvent` → the planner claims, debits and signs round **N+1**'s stake) **before** publishing `RewardGameEvent` at line 66, so `RewardGameConsumer.EvaluateOrbitFloors` reads a balance the next round already spent from — a race between two on-chain paths, so it misfires intermittently. Three symptoms from one read: the floor **cashes a player out mid-bet** (the exact thing settlement-time evaluation exists to prevent), the zero-out announces "out of funds" **and releases the reserved word** (the per-round claim churn its own comment at `OrbitBetSettlementService.cs:232-239` claims to prevent is happening today), and the take-profit can be **missed entirely**. ⚠️ **Second defect, found in refinement:** `RewardGameConsumer.cs:57-62` returns before the evaluation, so a round ending with **no winner** — a preset type whose typed choice had no backers, i.e. exactly a total-loss round — **never checks the floor at all**. Fix = a new **`ResolveSettledBalance`** on `IOrbitLedgerService` (ledger balance **+** stakes of `BetDebit` rows whose round is in `Game.ActiveGamesStatus`), read by **all three** rules, plus the evaluation on the no-winners exit. ⚠️ **Decision input ONLY** — cash-out amounts still come from the raw ledger (`OrbitCashOutReserver.cs:41`), so a threshold cash-out can pay one stake under the number set; that stake is committed to a live round. ⚠️ **Allow-list, not a deny-list of terminal statuses:** at its own settlement round N is `RewardForWinnersInProgress`, so `ActiveGamesStatus` excludes it **for free** — a deny-list adds it back and suppresses the very trip being fixed. ⚠️ **Read the ledger BEFORE the riding stakes** — the opposite order fails toward *firing early*, which is unrecoverable, while this order fails toward *deferring*, which self-corrects. ⚠️ **A second half was added after live testing (spec §3.2):** the settled balance fixes *when* the floor trips, but the successor round's stake is **already signed** by the time it does — so the player was cashed out one stake short (7 on a floor of 8) and **watched one more round play**. A stop rule evaluated only at settlement is always one bet behind. So reaching the floor now also stops the **dispatch**: `ResolveCandidates` excludes a player at or below their `CashOutFloor` (`Amount > CashOutFloor` — the same strictly-above comparison `EvaluateFloor` makes, keep them identical), and `CashOutFloorStoppedPlayers` cashes out anyone the settlement pass can no longer reach, since the gate alone would **strand** a balance that hit the floor with nothing left in flight. Result: cashed out at the floor **exactly**, no tail round. ⚠️ This **narrows decision 4** — the one-stake shortfall now applies to the **threshold only**. **Rejected:** rolling the successor over after rewards (delays every round for every player by the payout time and couples rollover to the reward path — `CreateGamesInterval` is dead config and `OpenNewGames` runs only at Consumers startup, so a missed exit path stops a game type rolling until a restart), gating the planner **on whether the previous round has settled** (a different gate from the one adopted — that one needs a 4th re-plan trigger and changes start-gate timing), and a `RidingBetStaleAfterMinutes` cutoff (a race has no stale-round timeout — only lotteries self-refund — so a quiet `WaitingBets` round is *healthy*, and a clock cannot tell it from #153's jam). A stuck round **defers** the stop loss, accepted: no loss has occurred yet. A floor trip does **not** write `OrbitEnrollment.Enabled`. ⚠️ **Supersedes a discarded 2026-08-07 design of the same name** (provisional stop + staleness knob + planner gate + hourly Hangfire sweep + client-derived UI state), archived as tag `archive/orbit-stop-loss-trigger-v1` in all three repos — **reference only, never a base**. Trello #154 ([kCzYE9Bi](https://trello.com/c/kCzYE9Bi)). | Shipped |
| `specs/coinbase-wallet-funding.md` | **`Feature`, `monkify-meta` only — `monkify-backend` + `monkify-front` NOT affected (no entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract, composable or component):** a standalone console app `spacestroker-fund-wallets` in a **new `tools/` solution** (`SpaceStroker.Tools.sln`, sibling of `deploy/`, which card #63's ghost-wallet snipe app will later join) that funds a pool of Solana wallets with **SOL** out of a Coinbase account, so #63's ghost wallets hold SOL before they have to buy anything. **CDP Secret API Key** (server-to-server) → `POST /v2/accounts/{id}/transactions` (`type: send`, `currency: SOL`, `network: solana`), scope `wallet:transactions:send` — **not OAuth2**, which forces 2FA per send and kills unattended batch funding. Reads `PublicKey` + `SOLAmount` (+ optional `Label`) and **ignores unknown JSON fields**, so #63's pool file pastes in verbatim while this tool never binds a `PrivateKey`. **Real-money guardrails are the feature:** dry-run default with `--execute` **plus** a typed confirmation of the batch total; a **full preflight** (valid 32-byte base58 addresses, no duplicates, amounts ≥ `minAmountSol`, Coinbase balance ≥ batch total **+ network-fee headroom** — the fee is charged on top, `SOLAmount` is what the destination *receives*) that aborts before send #1; each wallet's `idem` UUID **derived deterministically** from the plan hash + address + lamports so a repeat run is deduped **server-side even if the receipts file is lost**; a receipts JSON that doubles as **resume state** (`Confirmed` skipped, `Sent`/`Pending` polled and **never re-sent**); and a **plan-drift guard** — editing the pool changes the derived idem, so a drifted resume is refused without `--force-plan-change` (⚠️ the one way this tool can double-fund). Sends are **spread across a configurable window in randomized order with jitter**, amounts exact (inherits #63's anti-fingerprint constraint: Coinbase sends leave shared hot wallets, so **clustering**, not the sender, is the signal). "Funded" = Coinbase `completed` **and** an observed on-chain balance **delta** at the destination (Solnet `GetBalanceAsync` against a preflight snapshot). One wallet's failure never stops the batch; exit non-zero, re-run targets only the unfunded. ⚠️ **Code-exploration findings:** `monkify-meta/.gitignore` ignores only `sales/pdf/` — secrets safety in `deploy/` comes from a **nested** `.gitignore`, so `tools/.gitignore` is mandatory or a live withdrawal credential becomes committable; **.NET 8 has no Ed25519** (BouncyCastle, and the CDP secret's first **32 of 64** decoded bytes are the seed), ECDSA JWS needs **P1363 `r‖s`, not DER**; Solnet's `new PublicKey(string)` is **not** validation (use `PublicKey.IsValid` + a 32-byte decode assertion); the `amount` string needs **invariant** formatting (a `pt-BR` machine emits `"2,54"`). The project is split **Domain / Infrastructure / Application** with matching namespaces — `Domain/` is IO-free, which is precisely what makes the money-safety rules unit-testable without a fake sender (`Preflight.Check` takes the balance as a **decimal**, not a `CoinbaseClient`). That split is now a **standing rule** in [`conventions/backend.md`](conventions/backend.md) → "The folder tree must show the architecture", binding on every .NET project including the console tools. ⚠️ Two build-time gotchas: `BouncyCastle.Cryptography` 2.4.0 **collides** with the `Portable.BouncyCastle` 1.9.0 Solnet drags in (every `Ed25519*` type goes ambiguous, CS0433) — declare 1.9.0 and drop the newer one; and the **.NET 10 SDK writes `.slnx`**, so `dotnet new sln` needs `--format sln` to match the workspace. **No fake Coinbase sender** (owner's call) — unit tests cover the pure logic only (plan/idem, preflight, receipts/resume, spread schedule); the mandatory backend **integration test is explicitly waived** (zero backend surface, precedent `specs/main-production-operator.md`), and a live funding run is an **operator** step. Trello #165 ([9OoHAuEa](https://trello.com/c/9OoHAuEa)). ⚠️ **Shipped 2026-08-11.** Distilled in [`features/coinbase-wallet-funding.md`](features/token-ops.md). ⚠️ **AC1/AC10/AC11/AC13/AC14 have never run against a live CDP key** — the live round-trip is an operator step. | Shipped |
| `specs/nightfall-dashboard.md` | **`Feature`, `monkify-meta` only — `monkify-backend` + `monkify-front` NOT affected (no entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract, composable or component):** **Nightfall**, the **exit** side of the ghost-wallet pool (#63 buys, #165 funds, this sells) — a new `SpaceStroker.Nightfall` project joining the existing `tools/SpaceStroker.Tools.sln`. A **localhost-only (`127.0.0.1`) ASP.NET host** serves a static `wwwroot/` dashboard (**no JS build step** — `tools/` stays a pure .NET solution) and does all key handling, quoting, signing and broadcasting **in-process**: the browser only ever receives public data, never a `PrivateKey`. Own git-ignored `tools/secrets.nightfall.json` (RPC, Token CA, Jupiter + sell options, `Alias`/`PublicKey`/`PrivateKey` pool) that **ignores unknown fields**, so #63's pool file pastes in verbatim. Card face = alias · SOL · **% of total mint supply** · token amount · a compact **estimated** SOL value (one Jupiter price quote per refresh cycle, **not** one per wallet); **hover** = the live quote for the current `SELL %` (SOL out, price impact, exact amount) and, once sold, **realized SOL + signature**. **Click selects** (ctrl/shift/drag-marquee/select-all) and an explicit **`SELL SELECTED`** deals — the card's literal "click = sell" was dropped so a stray click can never open a live sell dialog. One **`SELL %`** field, **blank ⇒ 100%**, applied to **each wallet's own balance** (not to mint supply). **Jupiter builds the swap** (quote + swap-**instructions**, so the instruction set can be asserted before signing) and **our own keyed RPC** does every read and every broadcast — automatically correct whether the token is still on the pump.fun bonding curve or has migrated. ⚠️ **Real-money guardrails are the feature:** **`SIMULATE` ON at startup** (never persisted, checked server-side), a yes/no confirm showing per-wallet amounts + the batch quoted SOL total, a per-wallet **`maxPriceImpactPercent` abort before broadcast**, `slippageBps`, **low-SOL exclusion** (`minSolForFeeSol` → top up with #165), a **concurrency cap** (`maxConcurrentSells`, default 4), a fresh balance read **at execution** (never the polled value), a **duplicate-in-flight rejection**, and `nightfall-receipts.json` rewritten **after every state change** (quoted vs realized SOL, signature, status). Retryable (429/transport/expired blockhash) retries with backoff+jitter; terminal (slippage, no route, insufficient balance, guard failure) surfaces with the reason — **the batch never stops**. A 100% sell closes the emptied token account in a **separate follow-up transaction** (appending it to the swap could revert the sale); a partial sell does not. ⚠️ **Code-exploration findings:** `tools/.gitignore` misses **both** new filenames — `nightfall-receipts.json` is **not** ignored (`receipts*.json` anchors at the start) and `secrets.nightfall.example.json` **is** ignored (the tracked template could never be committed); both verified with `git check-ignore`. **Solnet 6.1.0 + versioned transactions / address lookup tables is the one open unknown** — every transaction in this workspace is built with the **legacy** `TransactionBuilder`, and Jupiter routes routinely need ALTs; the spec gives a primary path (assemble v0 locally) **and** a documented fallback (Jupiter `/swap` wire bytes, still guarded by `SwapInstructionsGuard`). Also: `Microsoft.NET.Sdk.Web` (not the funder's plain SDK), `new PublicKey(string)` is **not** validation, never route money through a `double`, a missing account is a **zero balance not an error**, `preflightCommitment` must match the blockhash commitment, and the keyed RPC URL is **masked to host-only** wherever logged. **Level `Feature`** ⇒ `features/nightfall-dashboard.md` at ship time; the backend **integration test is explicitly waived** (zero backend surface, precedent `specs/coinbase-wallet-funding.md` + `specs/main-production-operator.md`). Unit tests on the IO-free `Domain/` are mandatory. **3 Claude Design mockups are approved at implementation time before any UI code.** A live sell is an **operator** step. Trello #166 ([mUDhAdBu](https://trello.com/c/mUDhAdBu)). ⚠️ **Shipped 2026-08-11.** Distilled in [`features/nightfall-dashboard.md`](features/token-ops.md). **The primary transaction path did NOT survive:** Solnet 6.1.0 has no versioned-transaction or lookup-table support, so the swap is not assembled locally — Jupiter's `/swap` supplies the ready v0 transaction and `TransactionSigner` signs it **without parsing**, with `/swap-instructions` fetched only to feed the guard. **AC07/AC14/AC15/AC19/AC23/AC24/AC26 have never run against a wallet holding tokens.** | Shipped |
| `specs/ghost-wallet-snipe.md` | **`Feature`, `monkify-meta` only — `monkify-backend` + `monkify-front` NOT affected (no entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract, composable or component):** a second console app `spacestroker-snipe-token` joining `tools/SpaceStroker.Tools.sln` — the **buy** half of [`coinbase-wallet-funding.md`](specs/coinbase-wallet-funding.md)'s funding half. Drives a pool of wallets we control to a configured share of our own pump.fun token: **`SupplyPercentage` is a percent number of TOTAL supply** (`0.4504` ⇒ 0.4504%) resolved against the mint's on-chain supply, **one buy per wallet in ascending `alias` order** (⚠️ a deliberate change from the card's "largest supply first") at randomized offsets across a 60-minute window. **Execution is our own keyed RPC + a hand-built pump.fun `buy` instruction** (Solnet) — no PumpPortal, no Jupiter; the discriminator is **computed** as `SHA256("global:buy")[0..8]` and ⚠️ **the account list was re-derived from mainnet at build time** — the program's own on-chain Anchor IDL plus decoded live buys — which **corrected five spec assumptions**: the curve's quote side is `*_quote_reserves` (not `*_sol_reserves`) and it also carries `creator`/`is_mayhem_mode`/`is_cashback_coin`/**`quote_mint` (a curve need not be SOL-quoted — non-SOL ones are refused)**; `buy` takes a **third arg** `track_volume` (three encodings live, all landing; we omit it by default); **live buys carry 18 accounts against the IDL's 16** (two unnamed trailing accounts — we send the declared 16 plus a `snipe.extraAccounts` escape hatch, and the failure mode is a reverted tx, not lost SOL); **the token program varies per mint** (SPL Token *and* Token-2022 are both live, so ATAs are derived by hand — Solnet's helper hardcodes SPL Token); and **fee rates are `global`-account config** (95 + 5 bps), read at runtime. Every PDA derivation is pinned to a real mainnet transaction in `PumpFunProgramTests`, so a redeploy fails a test rather than an operator's launch. ⚠️ **Solnet.Programs 6.1.0 has NO `ComputeBudgetProgram`** — the priority-fee instructions are hand-built (`[2, u32]` limit, `[3, u64]` price). **Supply target wins over budget** (no global SOL cap; bounded only by each wallet's balance) while **every transaction still carries `maxSolCost`** = quote × (1 + tolerance), capped at what the wallet can pay. **Bonding curve only** — preflight refuses a graduated CA, mid-run graduation stops the remainder and reports where. ⚠️ **On-chain balance is the resume truth, receipts are only an audit trail:** Solana has no server-side idempotency token, so a receipts-driven resume could double-buy after a crash between confirmation and write — resume recomputes the shortfall from live balances instead, which makes every re-run idempotent by construction. Money gates mirror the funder (dry run default, `--execute` + typed quoted total, all-at-once preflight). Artifacts: two **independently produced** files (HTML + JSON, drift risk accepted by decision) under `tools/reports/<timestamp>/`. ⚠️ **Code-exploration finding: `tools/.gitignore` covers neither** — `reports/` is committable and `snipe-receipts.json` does **not** match the anchored `receipts*.json` glob, so both rules are mandatory. Pool file is **shared with the funder** (which already ignores unknown fields naming this card), plus a new required-unique **`alias`** field; the sniper is the only tool that ever binds a `PrivateKey`. **Disclosure: the pool stays undisclosed**, superseding card #63's own comment 1/4 (announced + publicly locked) — and **detection-evasion features are explicitly out of scope**, as is the sell/exit path (explored in refinement, dropped: it would need the AMM venue this spec excludes). Integration test **waived** (zero backend surface, precedent `main-production-operator.md`); distilled in [`features/ghost-wallet-snipe.md`](features/token-ops.md). **118 unit tests green** (the funder's 79 untouched); the instruction layout, every PDA and the curve decoder are asserted against real mainnet data. ⚠️ **Shipped 2026-08-11 without a live `--execute` buy ever running** — the submit/confirm path, the ATA-creation branch, the slippage revert and the mid-run graduation stop are unexercised, and a live run is the only way to settle the 16-vs-18 account question. It IS live-verified read-only: dry run + `status` against mainnet (supply, **Token-2022** detection, fee decode, curve decode, ladder), and the graduation refusal fired for real when the fixture token graduated mid-session. Trello #63 ([nokWHm7z](https://trello.com/c/nokWHm7z)). | Shipped |
| `specs/tools-refactor.md` | **`Minor change`, `monkify-meta` only — `monkify-backend` + `monkify-front` NOT affected (no entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract, composable or component):** housekeeping in two halves. **(1) `tools/`:** one shared git-ignored **`tools/secrets.json`** for every project in `SpaceStroker.Tools.sln` — shared `solana.rpcUrl` + `wallets[]` at the top level, one named section per tool, and Nightfall folded in under a new **`nightfall`** section (⚠️ an explicit **reversal** of [`specs/nightfall-dashboard.md`](specs/nightfall-dashboard.md) decision #4: its "different blast radius" rationale was already moot, because #63's sniper had **already** put wallet `PrivateKey`s in the shared file next to the Coinbase withdrawal credentials — so no new blast radius is created); all run state, receipts and reports move to a git-ignored **`tools/Receipts/`** (collapsing three `.gitignore` hacks into one rule) with a **fatal legacy-receipts check on the funder**, whose Coinbase `idem` is derived from the plan hash and would otherwise read a moved file as "no prior run" and **re-send**; **`SpaceStroker.DbConsole` moves** `deploy/tools/` → `tools/` (⚠️ **no code change** — `FindSecretsFile` walks up from `AppContext.BaseDirectory` and still reaches the workspace root, so it keeps reading **`deploy/secrets.<env>.json`**, a **documented exception** to the one-secrets-file rule because Cloud SQL connection strings are env-scoped and owned by the deploy tool); the dead **`SpaceStroker.Seeder`** remnants are deleted (⚠️ **source already gone and in no `.sln`** — only `bin`/`obj` + `deploy/secrets.seeder.json` remain, so it is a working-tree cleanup, and `architecture.md` §8e's "Replaces `SpaceStroker.Seeder`" line plus the unrelated **backend auto-bet seeder** docs stay untouched); and **`deploy/tools/` is flattened** so `SpaceStroker.Deploy` + its tests sit alone in `deploy/` under a renamed `SpaceStroker.Deploy.sln`. The layout becomes a standing rule in [`conventions/backend.md`](conventions/backend.md) + `AGENTS.md`. **(2) KB reduction:** delete-outright (no archive folder) sweep of `features/` + `specs/` — dead capabilities (the operator/multi-tenant stack), one-off bug-fix/QOL docs (`dev-qol-stuff`), superseded chains (`provably-fair` → `-final`, `audio-overhaul` ×3, `rebrand-*`, `lp-*`, `orbit-*`) and `features/` docs that are near-duplicate prose of their `specs/` sibling — then **merge the survivors into ~12 domain docs**. ⚠️ **`features/remove-operators.md` is MERGE, not DELETE** — it is the only doc explaining today's operator-free architecture. Runs in **4 phases with a hard user-approval gate after the inventory** (§9.3): ~100 irreversible deletions are proposed by criteria but decided by the user. `INDEX.md` keeps its format — **only map rows change**, and the ~210 KB "Feature currently in flight" section is left **byte-identical by decision** (knowingly accepted as the largest remaining token cost). No integration test / no `features/` doc (`Minor change`). **Shipped 2026-08-11** — the sweep took `features/` **65 → 15 files** (690 → 180 KB), `specs/` **118 → 80** (4,460 → 3,273 KB) and `INDEX.md` **517 → 415 KB**, with **zero dead links repo-wide**. ⚠️ **Two deliberate deviations:** AC15's "in-flight section byte-identical" was **not** met — it conflicted with AC14 (zero dead links), because that section held **30 links to deleted/merged docs**; the user chose to repoint them, so **only link targets changed and every word of prose is preserved**. And `deploy/secrets.seeder.json` was **left for the operator** (untracked, unrecoverable, and a secrets file an agent must not read). Trello #167 ([Co35YRGt](https://trello.com/c/Co35YRGt)). | Shipped |
| `features/games.md` | The **core game loop** and every rule layered on it: game types + the three play modes (free-type race / preset race / lottery), the seeded **Main** catalogue, the play flow, the state machine, the SignalR contract, the typer internals, reward & refund math — plus **choice exclusivity** (free-text choices are exclusive per round, and an Orbit reserved word is a **cross-round** claim), **free-bet mode** (dual-guarded, detected at runtime from `amount == 0` + no signature), the **pot display**, the **house seed bet** (an *ordinary* bet — no marker, no branch; ⚠️ never set `FundedByOrbit` on it), **lottery draw time** (fixed UTC grid anchored at `DateTime.MinValue`; ⚠️ the `"24:00:00"` → 24-**days** `TimeSpan` trap), **lottery pot stacking** (`Game.CarriedExtra`), **typer pacing** (⚠️ pacing-only — same seed ⇒ same stream and winner) and the **near-miss `> N AWAY!` reveal** (KMP tail-prefix, not Hamming). | Living |
| `features/money.md` | Commission, payouts and refunds. The **house cut is 1%** of profit **above each winner's own stake** with the house-funded lottery base excluded ⇒ ⚠️ **the yield as a share of the pot is always lower than the nominal rate** (0.83% for 6 bets/1 winner, 0.50% heads-up, 0.33% for 3 bets/2 winners). ⚠️ The committed `spacestrokersettings.json` value **is** the production rate, read at **payout time** and never snapshotted, so a change needs both Cloud Run hosts redeployed with no paid round open; the LP Terms page is the one hand-written number. Includes the multi-winner **paid-winner divisor / frozen winner-set** fix. Refunds: `NeedsRefunding`/`Refunded` is a **self-clearing lifecycle** with a 12h Hangfire sweep and an operator-admin `POST /admin/bets/refund`, guarded against double-pay on an already-`Rewarded` bet. | Living |
| `features/provably-fair.md` | Commit-reveal `ServerSeed` (committed on the **first bet** via an atomic first-writer-wins `UPDATE`, revealed after `Ended`), a portable **HMAC-SHA256 keystream** RNG with rejection sampling (no modulo bias), Stake-style client seed + `Bet.Nonce` (⚠️ the raw client seed **never leaves the browser**), the reveal-gated `GET /games/{id}/verification` (⚠️ **the server never replays the stream** — `Game.CharactersTyped` is persisted in the same `SaveChanges` as `WinningChoice` and the fallback flag is *derived*; `null` means unknown, never `false`), the in-app **FAIR** panel (locked until terminal, two instant checks, opt-in cancellable Web Worker replay) and the **kept** offline `public/verify.html`. ⚠️ `ProvablyFair.cs`, `utils/provablyFair.ts` and `verify.html` must stay byte-identical. | Living |
| `features/player.md` | Identity and preferences. **The connected wallet is the only identity**: one `Player` row per wallet (`ExternalPlayerId`, unique, no PII), `Bet.PlayerId` nullable, no `Bet.Wallet` column, anonymous free bets carry none. ⚠️ Free bets always count **individually** toward the min-player check. Display names are **signature-gated** (purpose-scoped nonce → ed25519 → `PATCH`) because a wallet address is public; ⚠️ the canonical message is a **shared byte-identical contract** between handler and SPA. Covers the settings modal (name server-backed; seed + graphics `localStorage`-only) and the once-per-wallet "pick your name" prompt. ⚠️ `GameService.InsertBet` must capture wallet + display name **before** detaching and **restore both** navigations — that has bitten twice. | Living |
| `features/orbit.md` | **Custodial balance + auto-betting** — the platform's first custody of real player money, ⚠️ **dark in production behind `VUE_APP_ORBIT_ENABLED`**. A player funds an Orbit balance with on-chain USDC; the house places **one real bet per round** out of it, indistinguishable from a human's. ⚠️ The Orbit keypair is **deliberately separate** from the bet/payout keypair (otherwise balances are uncheckable **and** an Orbit bet becomes a same-account on-chain **no-op**); every row is stamped with its `(account, mint)` **pool**, and the invariant is `balance == SUM(transactions)` over **all** rows with no status filter. `Bet.FundedByOrbit` exists *only* because custody signs for a different player's wallet. Four control flags collapsed to **one** (`Enabled` = play/pause). Plus notifications (private SignalR channel, Telegram on cash-out), reserved words, partial cash-out and stop-loss. | Living |
| `features/stats-and-history.md` | Everything that reports on past rounds. Per-type + deployment-wide **Stats** (`GET /games/{id}/stats`, `GET /games/stats`) with a **rolling** `week`/`month`/`all` window (⚠️ never calendar; an unrecognised value coerces to `all` with **HTTP 200**, and the DTO **echoes the applied window** so a coerced value can't caption all-time numbers "last 7 days"), all computed as **bounded Postgres aggregates** (⚠️ the window filter must not use `COALESCE` or it defeats the covering indexes). **Games History** (`/game/history`) with split caching — public page cached 5 min, own-bet columns joined **live** on cloned rows, ⚠️ **out-of-range pages never cached** (unbounded `pageNumber` would mint unlimited keys). Shared money rules: only `RewardForWinnersCompleted` counts, free bets excluded from money but counted in participation. Plus **GA4 analytics** — one property across LP+app, consent-gated via a `.spacestroker.com` cookie, ⚠️ no money and no identity ever sent. | Living |
| `features/embedding.md` | Running the game inside a third party's `<iframe>`. Runtime detection (`window.self !== window.top`), the stable secret-free parent contract `{ source: 'space-stroker', type, payload }` (`ready` / `game-started` / `bet-placed` / `bet-settled` bettors-only / `game-ended` / `idle-disconnected` / `resize` hint), and — because wallet extensions **refuse to inject cross-origin** — paid `SolanaToken` bets via a **same-origin `/wallet-bridge` popup** opened **synchronously in the click gesture**, speaking a distinct `space-stroker-wallet` envelope with strict origin+source checks. ⚠️ Reduced by `remove-operators`: the launch-token handoff, the `token-expired` branch and the inactive-operator case are gone — a site just points an iframe at the app. ⚠️ `frame-ancestors *` is a deploy-time dependency. | Living |
| `features/infra.md` | Deploy, transport, RPC and the API edge. **GCP topology** (Cloud Run API public / Consumers internal + CPU-always-on and owning migrations, GCS+CDN front·lp, private Cloud SQL 16 + Memorystore, HTTPS LB + Cloud Armor), env-scoped naming (`spacestroker-<envCode>`; ⚠️ the full word breaks the SA 30-char and connector 25-char caps) and the persistent/workload Terraform split. **MassTransit Postgres SQL transport** — no broker; Consumers provisions, the API is publisher-only; ⚠️ at-least-once delivery made safe by **ledger-based** idempotency. **Solana access**: the SPA no longer touches a public RPC — `GET /solana/balance` (⚠️ `rawAmount` is a **string**; the mint must be the configured one; every rejection is one vague message; a missing ATA reads `0` and is **never retried**). **API errors** reach the player from the envelope (⚠️ `type` is numeric — `ValidationFailure` is **2**). **Rate limiting**: ⚠️ `RealIpHeader` pinned to `""` (a live prod bypass), and `POST /bets` deliberately **unlimited**. ⚠️ **Several infra specs shipped but were never `terraform apply`-ed** — see "Applied vs merged". | Living |
| `features/frontend-ux.md` | How the app *presents* the game. The ported arcade **design system** (one cyan accent, two fonts, ⚠️ `arcade.css` is plain CSS **not** `@layer components` — a Vue CLI css-loader gotcha), the pixel-art **icon language** and the ☰ **3-column icon pad** (⚠️ the embed renders the **same grid**, not an alternate layout), **mobile** below `lg` (segmented `GAME | CHAT`, absolutely stacked so a hidden view can't intercept taps; lever = PLACE BET; multi-wallet deeplinks), **background performance** (⚠️ `NebulaField.vue` deleted outright; light mode resolved per load with no UI; ⚠️ `BG_DENSITY` makes normal mode **29/layer not 64** and light mode a `Math.min` **ceiling**), the **off-page rule** (⚠️ structural — `GameBackground`/`TorpedoSpawner` live in `App.vue` and read a module singleton that outlives `GamePage`), **audio** (⚠️ 9 mp3s not 12 — the proximity bleeps were dropped) and **chat** (reuses `ActiveGamesHub`, no history replay). | Living |
| `features/home-and-onboarding.md` | How a first-time visitor arrives and learns. The **home dashboard** at `/` (⚠️ the `/space-stroker` URL prefix is gone — which is why the LP CTA targets the bare origin), the `/how-to-play` guide replacing the deleted **Quick Tour** (⚠️ nothing auto-opens any more, on any route, for any player; the guide deliberately names **no commission number**), `/responsible-gaming`, and `/game/practice` — a complete round running **entirely in the browser** with no request, socket, wallet or money, that ⚠️ **works with the API down** and is built to *be* the real game screen, not approximate it. ⚠️ Its three follow-up specs ride `release/ui-ux-changes` and are **not on `main`**. | Living |
| `features/token-ops.md` | ⚠️ **Ops tooling that moves real money — nothing here runs in Cloud Run, the Docker stack or any request path.** The three-tool ghost-wallet lifecycle in `monkify-meta/tools/`: **fund** (Coinbase → SOL; ⚠️ the derived `idem` means editing an amount/address between runs is **the one path that can double-fund**; Travel Rule data is mandatory), **buy** (pump.fun; ⚠️ `SupplyPercentage` is a percent of **total supply**; on-chain balance is the resume truth, not receipts; the 16-vs-18 account question is pinned by a test) and **sell** (Nightfall; ⚠️ keys stay **in-process** and the browser DTO has no key member; `SIMULATE` at every launch enforced **server-side**; a click **selects**, never sells; blank `SELL %` ⇒ 100% of the wallet's **own** balance). ⚠️ **Read the verification-status section before trusting a green run** — large parts of all three live paths have never executed. | Living |
| `features/lp.md` | The public marketing site (`monkify-lp`, Astro 5 + Vue 3 + Tailwind v4, static, on **`master`**, no test suite). Token-free **USDC** positioning with a "No token. Yet." card and an anti-scam line, the arcade-cabinet layout (two-column hero + a ⚠️ **purely cosmetic** LIVE typing pill that makes no money claim and issues no request, `#modes`, EDSON.LOG story, roadmap as **goals** not metrics), and real launch/social links (`GAME_URL` = the **bare origin**). ⚠️ The three mode cards **must match the seeded Main types** (Cosmic Sprint = 6-char **alphanumeric**; Beast Mode = 1 of **8**; Space Jackpot = **daily/24h**) — never "6 letters/6 numbers", the invented animal set, or "every 4h". ⚠️ `terms.astro` is the one hand-written commission rate and must track any change. | Living |
| `features/admin-and-ops.md` | The admin portal at `/admin`: an admin-scoped RS256 token (`X-Admin-Token`) validated against the **configured** keypair, create/list/deactivate game types across all three modes (**deactivate-or-create, no in-place edit** — which is why recreating a type resets its stats and why an un-won lottery pot must be **transferred**), the bet-refund panel, and `POST /dev/admin-token` outside production. The operator half was reworked away by `remove-operators`. | Living |
| `features/legal.md` | Legal documentation and **per-wallet self-exclusion**. The legal pages, and the exclusion state (`SelfExcludedAt` / `SelfExcludedUntil`) that lives on the `Player` row and is evaluated **per bet**, so an expiry needs no scheduled job. Uses the same purpose-scoped signed-nonce mechanism as preferences — ⚠️ a challenge minted for one purpose cannot be replayed against the other. | Living |
| `features/remove-operators.md` | **The deployment is the operator.** Deleted the whole multi-tenant apparatus — `Operator`/`OperatorToken`/`OperatorPlayer`, the `X-Launch-Token` surface, the off-chain `Standard` payment method + `PaymentMethod` enum, the per-operator RG pre-bet hook, `ISecretProtector`, operator branding and the FX/display-currency stack. Everything token/game/admin reads from **`GeneralSettings:Operator`**; player endpoints are **public** and identity is the **connected wallet**. **One ledger:** `CryptoTransactionLog` merged into `TransactionLog`. Greenfield migration. ⚠️ **Kept as its own doc on purpose — it is the only place today's operator-free architecture is explained.** | Living |
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

**Tools refactor** ([#167](https://trello.com/c/Co35YRGt)) — **landed on `main` on 2026-08-11** via
`feature/tools-refactor` in **`monkify-meta` only** (**no `monkify-backend` and no `monkify-front`
branch — zero backend and frontend surface**). Spec:
[`specs/tools-refactor.md`](specs/tools-refactor.md). **405 unit tests green** (322 across
`tools/SpaceStroker.Tools.sln`, up from 191; 83 in the new `deploy/SpaceStroker.Deploy.sln`).
**Level: `Minor change`** ⇒ **no** dedicated `features/<slug>.md` doc and **no** happy-path
integration test — the existing KB docs the moves touch were corrected instead.

**The sweep, measured:** `features/` **65 → 15 files** (690 → 180 KB), `specs/` **118 → 80**
(4,460 → 3,273 KB), `INDEX.md` **517 → 415 KB**, and **zero dead links repo-wide** (61 were broken
mid-sweep; all repaired).

Two halves. **(1) `tools/` consistency:** one shared `tools/secrets.json` (shared `solana.rpcUrl` +
`wallets[]` at the top level, one named section per tool, Nightfall folded in under a new
**`nightfall`** section), one git-ignored **`tools/Receipts/`** for all run state and reports,
`SpaceStroker.DbConsole` moved in from `deploy/tools/`, the dead `SpaceStroker.Seeder` remnants
deleted, `deploy/tools/` flattened so `SpaceStroker.Deploy` sits alone in `deploy/`, and the whole
layout written into `conventions/backend.md` + `AGENTS.md`. **(2) KB reduction:** a delete-outright
sweep of `features/` + `specs/` (dead capabilities, one-off bug-fix docs, superseded chains,
near-duplicate `features/`↔`specs/` pairs) followed by a merge of the survivors into ~12
domain-boundary docs.

⚠️ **Three things the implementer must not get wrong.** First, the Nightfall secrets merge is an
explicit **reversal** of [`specs/nightfall-dashboard.md`](specs/nightfall-dashboard.md) decision #4,
which kept that file separate because the shared one holds live Coinbase **withdrawal** credentials —
"a different blast radius". The reversal is safe **only because the rationale was already moot**:
#63's sniper had already put wallet `PrivateKey`s into `tools/secrets.json`, so spend keys and
withdrawal credentials are **already** co-located and nothing new is exposed. The stale code comment
asserting the opposite must be replaced, not left contradicting the code. Second, moving the funder's
default receipts path is a **money-safety change**: `spacestroker-fund-wallets` derives each Coinbase
`idem` from the plan hash, so a receipts file it cannot see reads as "no prior run" and a re-run can
**re-send** — hence the mandatory fatal legacy-file check, and hence the decision to leave the
existing loose receipts files exactly where they are rather than auto-migrating them. Third, the KB
sweep is **irreversible in-tree** and therefore **gated**: the implementer produces a full
classification inventory of every `features/` + `specs/` file and **stops for user approval** before a
single `git rm`. `features/remove-operators.md` is a **merge, not a delete** — it is the only doc that
explains today's operator-free architecture.

⚠️ **`INDEX.md` is deliberately only half-fixed, and the plan changed once during the sweep.** By
decision, the sweep touches **map rows only** — the ~206 KB "Feature currently in flight" section
(this one) keeps every one of its ~20 already-shipped features, and `ship-feature` will keep appending
to it. It remains the single largest block of tokens every agent session pays for, knowingly accepted.
**But it is not byte-identical, as the card's AC15 originally required:** it held **30 links to docs
the sweep deleted or merged**, which collided head-on with AC14 (zero dead links). The user chose to
**repoint them** — so **only link targets changed and every word of prose is preserved** (verified
programmatically). Read AC15 with that correction.

Two things code exploration settled, both of which held: `SpaceStroker.DbConsole` needed **no code
change** to move (its `FindSecretsFile` walks up from `AppContext.BaseDirectory` and still reaches the
workspace root, so it keeps reading `deploy/secrets.<env>.json` — a **documented exception** to the
one-secrets-file rule), and the `SpaceStroker.Seeder` **source was already deleted** and in no `.sln`.
⚠️ Of its leftovers the `bin`/`obj` residue was removed but **`deploy/secrets.seeder.json` was left in
place for the operator** — untracked, unrecoverable, and a secrets file an agent must not read.

⚠️ **One operator step remains before the tools run:** fold `secrets.nightfall.json` into
`tools/secrets.json` under a `nightfall` key. Until then `spacestroker-nightfall` stops with a message
naming the missing section.

---

**Nightfall dashboard** ([#166](https://trello.com/c/mUDhAdBu)) — **landed on `main` on 2026-08-11**
via `feature/nightfall-dashboard` in **`monkify-meta` only** (**no `monkify-backend` and no
`monkify-front` branch — zero backend and frontend surface**). 112 unit tests green (191 across the
`tools/` solution). Spec: [`specs/nightfall-dashboard.md`](specs/nightfall-dashboard.md), distilled in
[`features/nightfall-dashboard.md`](features/token-ops.md). **Level: `Feature`** ⇒ the
dedicated feature doc was created, while the mandatory happy-path integration test in
`SpaceStroker.Tests.Integration` was **explicitly waived on the card** — a standalone local tool has
no backend code path to exercise (same waiver as
[`specs/coinbase-wallet-funding.md`](specs/coinbase-wallet-funding.md) and
[`specs/main-production-operator.md`](features/remove-operators.md)).

**Nightfall is the exit side of the ghost-wallet pool** — [#63](https://trello.com/c/nokWHm7z) buys,
[#165](https://trello.com/c/9OoHAuEa) funds, this one sells. A new **`SpaceStroker.Nightfall`**
project joins `tools/SpaceStroker.Tools.sln`: a **localhost-only ASP.NET host** serving a static
`wwwroot/` dashboard with **no JS build step**, so `tools/` stays a pure .NET solution. ⚠️ **The
architecture is a money-safety decision, not a preference:** a browser that can sell must hold the
pool's private keys, so the host keeps them in-process and the browser receives **only public data**
— the response DTOs have no `PrivateKey` member at all, which makes "no key ever reaches the
browser" structural rather than a review item.

⚠️ **The guardrails are the feature.** `SIMULATE` is **ON at every startup** (never persisted, and
enforced **server-side** so a browser cannot post its way past it); **click selects and never
sells** (an explicit `SELL SELECTED` deals, because click would otherwise mean two different things
depending on whether the mouse moved); the `SELL %` field is **blank ⇒ 100% of each wallet's own
balance**, never a share of mint supply; a per-wallet **price-impact abort** fires *before*
broadcast; wallets below `minSolForFeeSol` are **excluded with the reason named**; concurrency is
**capped**; the amount is **recomputed from a fresh balance read at execution**; a wallet already in
flight **cannot be re-submitted**; and `nightfall-receipts.json` (quoted **vs** realized SOL,
signature, status) is rewritten after **every** state change. One yes/no confirmation is the only
gate the card asked for — which is exactly why `SIMULATE` defaults on.

**Jupiter builds the swap; our own keyed RPC sends it.** Jupiter never sees a private key and never
touches the chain — it supplies route data and the wire transaction, and everything is signed locally
and broadcast through `solana.rpcUrl`. This survives the pump.fun → PumpSwap/Raydium migration with
no code change. A 100% sell closes the emptied token account in a **separate follow-up transaction**
— appending `closeAccount` to the swap could revert the sale itself.

⚠️ **This card is the standing warning that a spec's primary technical path can simply not exist.**
The spec named assembling a v0 transaction locally as the primary path, with Jupiter's prebuilt
`/swap` as a documented fallback. **Solnet 6.1.0 has no `VersionedTransaction`, no `MessageV0` and no
address-lookup-table type at all** — only the legacy `Message`/`Transaction`/`TransactionBuilder`
family (`SolanaService.cs:406` is the workspace's only prior art, and it is legacy). Jupiter routes
routinely need lookup tables, so the primary path was impossible and **the fallback shipped**:
`TransactionSigner` signs Jupiter's wire transaction **without parsing** it — a signature covers the
message bytes and the wire format is `[shortvec count][64-byte signature]…[message]`, so splicing
into slot 0 is correct for legacy and v0 alike — while `/swap-instructions` is still fetched purely
to feed `SwapInstructionsGuard` (program-id allowlist, authority, mints, amount). The guard vets the
semantics, the signer vets the shape, and decision #6's "assert what we sign" survives intact.
Carrying a written fallback in the spec turned a redesign into a half-hour decision.

⚠️ **Three bugs here were caught by a test or a live smoke run, never by review** — all three read
as correct. (1) **`new Account(privateKey, publicKey)` does not derive anything**; Solnet stores the
address you hand it, so a "does this key control this address" check written against it passes for
any pairing. Derive from the seed with `Ed25519PrivateKeyParameters` (**first 32** of the 64 secret
bytes — the same 32-of-64 lesson #165 recorded for the CDP key). (2) **A fire-and-forget batch must
not run under an endpoint's `CancellationToken`** — that is `HttpContext.RequestAborted` and fires
the moment the response completes, so the API returns a batch id and nothing ever happens; use the
application lifetime token. (3) A `SemaphoreSlim` disposed with `using` in the method that *starts*
background work is pulled out from under it. Also: **Jupiter's `priceImpactPct` is a fraction, not a
percentage** (`"0.0123"` = 1.23%) — reading it literally would have made the price-impact abort ~100×
too permissive, i.e. it would never have fired.

⚠️ **Two `.gitignore` gaps were found and fixed** (both verified with `git check-ignore`):
`nightfall-receipts.json` was **not** ignored, because `receipts*.json` anchors at the start of the
name — so the audit trail, with addresses and signatures, was committable; and
`secrets.nightfall.example.json` **was** ignored by `secrets*.json`, so the tracked template could
never have been committed. Now `*receipts*.json` plus a `!secrets.nightfall.example.json` negation.
`tools/.gitignore` remains load-bearing: the `monkify-meta` root `.gitignore` covers only
`sales/pdf/`.

⚠️ **Seven acceptance criteria shipped unverified against a wallet holding tokens** — AC07, AC14,
AC15, AC19, AC23, AC24 and AC26 each need a real mainnet sale, which `constraints.md` §5 makes an
**operator** step (spec §14). What *was* exercised live, read-only: the mint read, batched balance
reads, static-file serving, the zero-balance gate, a receipt written with no private key, SSE
delivery and the mode toggle. The other criteria are covered by the 112-test suite.
**Ghost wallet snipe app** ([#63](https://trello.com/c/nokWHm7z)) — **landed on `main` on 2026-08-11**
via `feature/ghost-wallet-snipe` in **`monkify-meta` only** (**no `monkify-backend` and no
`monkify-front` branch — zero backend and frontend surface**). 118 unit tests green, the funder's 79
untouched. Spec: [`specs/ghost-wallet-snipe.md`](specs/ghost-wallet-snipe.md), distilled in
[`features/ghost-wallet-snipe.md`](features/token-ops.md). **Level: `Feature`** ⇒ the
dedicated feature doc was created, while the mandatory happy-path integration test in
`SpaceStroker.Tests.Integration` was **explicitly waived on the card** (a standalone console app with no
backend code path to exercise — same waiver as
[`specs/coinbase-wallet-funding.md`](specs/coinbase-wallet-funding.md) and
[`specs/main-production-operator.md`](features/remove-operators.md)).

`spacestroker-snipe-token` is the **buy** half of the pair whose funding half shipped as
`spacestroker-fund-wallets`: it drives the same pool file to a configured share of our own pump.fun
token, one buy per wallet in **alias order** across a 60-minute window, and writes an HTML + JSON
report of achieved-vs-target per wallet.

⚠️ **Shipped without a live `--execute` buy ever running.** The submit/confirm/balance-delta path, the
ATA-creation branch, the slippage revert and the mid-run graduation stop have never touched real money,
and a live run is the only way to settle whether the two undeclared trailing accounts are required —
an **operator** step (`constraints.md` §5). What *is* live-verified, read-only: supply reads,
**Token-2022** detection, fee-rate and curve decoding, the ladder, and the graduation refusal, which
fired for real when the fixture token graduated mid-session.

⚠️ **The spec's pump.fun protocol facts were wrong until they were read off the chain.** Implementation
pulled the program's own on-chain Anchor IDL plus decoded live buys and corrected five of them: the
curve's quote side is `*_quote_reserves` (not `*_sol_reserves`) and it also carries
`creator`/`is_mayhem_mode`/`is_cashback_coin`/**`quote_mint`** (a curve need not be SOL-quoted —
non-SOL ones are refused); `buy` takes a **third arg** (`track_volume`); **live buys carry 18 accounts
against the IDL's 16**; **the token program varies per mint** (SPL Token *and* Token-2022, so ATAs are
derived by hand — Solnet's helper hardcodes SPL Token); and **fee rates are `global`-account config**
(95 + 5 bps), read at runtime. Every derivation is pinned to a real mainnet transaction in
`PumpFunProgramTests`, so a redeploy fails a test instead of an operator's launch. ⚠️ **On-chain
balance — not the receipts file — is the resume authority**, because Solana has no server-side
idempotency token and a receipts-driven resume can double-buy after a crash. ⚠️ `tools/.gitignore`
gained **two mandatory rules** (`reports/`, `snipe-receipts*.json`) — the pre-existing
`receipts*.json` glob is anchored at the filename start and matched neither.

**Coinbase wallet funding** ([#165](https://trello.com/c/9OoHAuEa)) — **landed on `main` on
2026-08-11** via `feature/coinbase-wallet-funding` in **`monkify-meta` only** (**no `monkify-backend`
and no `monkify-front` branch — zero backend and frontend surface**). 60 unit tests green. Spec:
[`specs/coinbase-wallet-funding.md`](specs/coinbase-wallet-funding.md), distilled in
[`features/coinbase-wallet-funding.md`](features/token-ops.md). **Level: `Feature`** ⇒
the dedicated feature doc was created, while the mandatory happy-path integration test in
`SpaceStroker.Tests.Integration` was **explicitly waived on the card** — the deliverable is a
standalone console app with no backend code path to exercise (same waiver as
[`specs/main-production-operator.md`](features/remove-operators.md)).

⚠️ **Five acceptance criteria shipped unverified against a live key** — AC1 (dry-run plan table),
AC10/AC11 (on-chain confirmation and the unconfirmed case), AC13 (partial failure) and AC14 (missing
scope / allowlist) have never made a real Coinbase call, because the card ruled out a fake sender. The
live round-trip is an **operator step** (spec §14). The other 15 are covered by the test suite.

⚠️ **This card also set a workspace-wide convention.** `conventions/backend.md` → **"The folder tree
must show the architecture"** now requires every .NET project — backend services and the console tools
alike — to group files into **Domain / Infrastructure / Application** folders with matching namespaces,
mirrored in the test project. The rule exists because the layer boundary is the *testability* boundary:
`Domain/` being IO-free is what lets the money-safety rules be unit-tested with no fake Coinbase sender
(`Preflight.Check` takes the balance as a `decimal`, not a `CoinbaseClient`). `deploy/tools/SpaceStroker.Deploy`
predates the rule and is **still flat** — bring it into line opportunistically, not as a drive-by refactor.

A new **`tools/`** solution (`SpaceStroker.Tools.sln`, sibling of `deploy/`) holds
`spacestroker-fund-wallets`, which funds a pool of Solana wallets with SOL out of a Coinbase account
via a **CDP Secret API Key** on `POST /v2/accounts/{id}/transactions` — the funding half of
[#63 "Create ghost wallet snipe app"](https://trello.com/c/nokWHm7z), which will join the same
solution. ⚠️ **The guardrails are the feature, not ceremony:** dry-run default + `--execute` + a
typed confirmation; a preflight that aborts before the first send (addresses, duplicates, amounts,
balance ≥ total **+ network-fee headroom**); a **deterministic `idem`** derived from the plan so a
repeat run is deduped server-side even with the receipts file deleted; and a **plan-drift refusal** —
editing the pool between runs changes that idem, which is the one path that can double-fund. Sends
are spread across a window in randomized order with jitter (Coinbase sends leave shared hot wallets,
so **clustering** is the fingerprint), and a wallet counts as funded only on Coinbase `completed`
**plus** an on-chain balance delta.

**Orbit modal not loading the game type** ([#164](https://trello.com/c/R8RHJgeY)) — **landed on
`main` on 2026-08-10** via `feature/orbit-modal-game-type` in **`monkify-front`** +
**`monkify-meta`** (**no `monkify-backend` branch — zero backend surface**). Spec:
[`specs/orbit-modal-game-type.md`](features/orbit.md). **Level: `Bug fix`** ⇒ no new
`features/` doc — the behaviour is distilled into
[`features/orbit-modal-improvements.md`](features/orbit.md) ("The panel can always
set the game it needs"), and the picker accounts in
[`features/orbit-single-control.md`](features/orbit.md) and
[`features/orbit-ui-overhaul.md`](features/orbit.md) ("away from a round only", "no game
`<select>`") are amended in place — they are **no longer true**.

A **new player** who sets Orbit up from the panel is left with a panel where **nothing works**: no
game pointer and no control that can set one. Two defects stack — `OrbitModal.refresh()` is the only
path that runs the post-load steps, so a player with no stored session bails out at `loadIfSignedIn`
(`OrbitModal.vue:867`) and **SIGN IN** (`onSignIn`, `:920`) calls `load()` **and nothing else**; and
the picker that could set the pointer is suppressed by `&& !currentGame` (`:1174`) on exactly the
page where the player is standing. Fix = **one shared post-load helper** (`resumePendingDeposit` →
`autoPointAtCurrentGame`) that **both** read paths end in, the picker rendering on
**`!gameParametersId` alone**, and a **contextual picker** (preselected to the round, reworded, no
`router.push` when the pick is the page already on screen).

⚠️ **Both halves ship together on purpose.** `autoPointAtCurrentGame` is best-effort and **silent**
(`catch` → `clearError()`), so a refused or offline PATCH rebuilds the identical dead end even with
the sign-in path fixed — that is the failure path the code's own comment at `:1332` already
anticipates. The silence **stays**: the now-visible picker is the consequence, not a toast about a
write nobody asked for. ⚠️ The **self-excluded** wallet is the one wallet that always meets the
picker (`autoPointAtCurrentGame` skips it at `:898`), so the picker renders **disabled** with
*"Auto-play is unavailable on this wallet."* **repeated inside** its block — the copy at `:1495`
lives in the `v-else` body the picker branch never reaches and must not be moved.

**Orbit stop loss trigger** ([#154](https://trello.com/c/kCzYE9Bi)) — **landed on `main` on
2026-08-10** via `feature/orbit-stop-loss-trigger` in **`monkify-backend`** + **`monkify-meta`** (**no
`monkify-front` branch — zero frontend surface**). 931 unit + 116 integration green. Spec:
[`specs/orbit-stop-loss-trigger.md`](specs/orbit-stop-loss-trigger.md). **Level: `Minor change`** ⇒
**no dedicated `features/orbit-stop-loss-trigger.md`**; the behaviour is folded into
[`orbit-auto-bet-engine.md`](features/orbit.md),
[`orbit-notifications.md`](features/orbit.md),
[`orbit-feedback-adjustments.md`](features/orbit.md) and `glossary.md`, each of
which previously claimed the floor and zero-out acted on a resolved bet — **true only as of this
card**.

Orbit's cash-out **floor**, take-profit **threshold** and **zero-out** all decide on a custodial
balance that the *next* round has already spent from, because `RunGameConsumer.cs:50-61` opens the
successor round — and lets the Orbit planner debit and sign its stake — **before** publishing
`RewardGameEvent` at line 66. The floor therefore cashes players out mid-bet, the zero-out announces
"out of funds" and surrenders the reserved word on the same wrong number, and the take-profit can be
missed. Fix = **`ResolveSettledBalance`** (ledger balance **+** stakes of `BetDebit` rows whose round
is in `Game.ActiveGamesStatus`) read by all three rules — **decision input only**, cash-outs keep
sizing from the raw ledger. ⚠️ Also fixes a second defect the card did not have: a round ending with
**no winner** returns at `RewardGameConsumer.cs:57-62` and **never evaluates the stop rules at all**.

⚠️ **Live testing then exposed the other half (spec §3.2), and it is now part of this card.** The
settled balance fixes *when* the floor trips, but the next stake is **already signed** by then — so
the player was cashed out for 7 on a floor of 8 and watched one more round play. Reaching the floor
now also stops the **dispatch** (`ResolveCandidates` excludes anyone at or below their
`CashOutFloor`), with `CashOutFloorStoppedPlayers` cashing out whoever the settlement pass can no
longer reach, because the gate alone would strand that balance. Cashed out at the floor exactly, no
tail round. This **narrows decision 4** to the threshold only.

⚠️ **A previous design for this card was built and discarded.** Its commits are archived on tag
**`archive/orbit-stop-loss-trigger-v1`** in `monkify-backend` (`2c0ef3f`), `monkify-front`
(`6e19e9c`) and `monkify-meta` (`f224555`) — **reference only, never a base**. It defined "riding" as
a deny-list of terminal statuses, which counts round N's own stake at round N's settlement and
suppresses the trip it was meant to fix; see spec §3.1 before reading that code.

**Orbit modal improvements** ([#163](https://trello.com/c/77laekQh)) **landed on `main` on
2026-08-10** via `feature/orbit-modal-improvements` in **`monkify-front`** + **`monkify-meta`** (**no
`monkify-backend` branch — zero backend surface**, so `AGENTS.md` rule 8's integration test had
nothing to exercise, and `monkify-front` has no test runner). Distilled in
[`features/orbit-modal-improvements.md`](features/orbit.md); spec:
[`specs/orbit-modal-improvements.md`](specs/orbit-modal-improvements.md).

The Orbit **panel** converges on the `/orbit` page after the single-control merge: the transport pair
(`RUN ORBIT` + `STOP ORBIT`) is replaced by the **shared `OrbitControlBar` in a compact variant** —
one component, both surfaces — and the panel gains **CASH OUT** (full balance, straight to the wallet
signature, no inline confirm, **never word-gated** while ADD MONEY keeps its gate). ⚠️ **Two behaviour
changes fall out deliberately:** the panel's PAUSE was dead while `unfunded` and its RUN never said
RESUME — the bar's state table wins both.

⚠️ **THE SPEC'S CHANGE ③ WAS BUILT AND REJECTED — read `specs/orbit-modal-improvements.md` §17 before
§3.** Replacing position ③'s `AUTO CASHOUT` editors with two icon'd links (STOP RULES + NOTIFS) was
implemented, shown on screen and rejected; it was reverted from `HEAD`, so the editors are
byte-identical to `main`. **`orbit-ui-overhaul.md` §3.1 is therefore NOT re-opened**,
`orbit-single-control.md`'s account of the panel's *editable rules* stays **true** (only its account
of the transport pair is false), and **`src/constants/icons.ts` is untouched** — no `notify` glyph.

The Telegram hook rides the panel's **way-out link** instead: *"Set up notifications and more in Orbit
settings"* → `/orbit?tab=telegram` where a bot is configured, *"More in Orbit settings"* → `/orbit`
where none is. Both halves flip together, so the copy never promises a tab the page will not render.
`/orbit`'s new validated **`?tab=`** query beats the unfunded-lands-on-MONEY default; unknown or
hidden ids fall back to `game`.

The rule auto-save fix shipped on **BOTH** surfaces, not just the page: a **1.2 s** debounce armed
from `@input`, cancelled by blur/Enter, flushed once — **on unmount** for `OrbitStopRulesTab.vue`,
**on `open` going false** for `OrbitModal.vue`. ⚠️ The panel's flush is deliberately *not*
`onBeforeUnmount`: it is `v-if`-ed on a feature flag, stays mounted all session, and Esc / the
backdrop / ✗ never fire blur. The word fields already debounce at 600 ms and are **left alone**.
⚠️ **Blockers found in the code, not on the card:** the `.orbit-trio*` CSS was scoped under
`.orbit-page` and rendered the bar **unstyled** inside the panel (de-scoped, **including both
`:focus-visible` lists**); `useOrbit.cashOut` **returns rather than throws** on a mobile `'hopped'`,
so the panel compares the balance across the await rather than toasting unconditionally; arming the
debounce from a `watch` lets `syncRuleFields()`-on-refusal **re-arm it into a PATCH loop**; and
`OrbitControlBar` needed a **`selfExcluded`** blocker the panel's old `canRun` had carried.

**Off-page game presentation gate** ([#162](https://trello.com/c/FwzvH8fm)) **landed on `main` on
2026-08-10** via `feature/off-page-game-presentation` in **`monkify-front`** + **`monkify-meta`**
(**no `monkify-backend` branch — zero backend surface**, so `AGENTS.md` rule 8's integration test had
nothing to exercise, and `monkify-front` has no test runner). Distilled in
[`features/off-page-game-presentation.md`](features/frontend-ux.md); spec:
[`specs/off-page-game-presentation.md`](specs/off-page-game-presentation.md).
⚠️ **The bar it actually cleared was `yarn lint` + `yarn build` only** — the owner waived the 11-step
browser pass, so **AC 1–10 shipped unverified against a running app.** AC 7 (balance re-read +
embedded `bet-settled` / `game-ended`) is the first thing to exercise if this area misbehaves.

A round the player walked away from keeps driving the app's global animation and audio layer:
`GameBackground` (×2) and `TorpedoSpawner` live in `App.vue` and read `useGame`, whose SignalR
subscription is tab-scoped and **deliberately** outlives `GamePage`. The fix is a **presentation gate
at the consumer** — a module-level `inGame` flag in `useGame.ts` written by an `App.vue` route watcher
(`route.name === 'Game'`), the same shape as the shipped `setCanonicalGraphics` precedent — read by
the two `:speed-multiplier` bindings (`inGame ? typingSpeed : 1`), the torpedo bet watcher, and the
victory/lose one-shot. ⚠️ **The socket is NOT detached** (that would break the balance re-read, the
embedded `bet-settled` / `game-ended` signals and `useOrbitRedirect`) and **`typingSpeed` is NEVER
mutated** (the typer and Edson read the same ref; the return must stay instant). The sting is
**suppressed and consumed**, the **Orbit first-win modal is deliberately kept** firing off-page, and
**nothing is purged** on leaving. ⚠️ **Do not implement the torpedo gate as a `v-if`** — unmounting
`TorpedoSpawner` takes `#torpedo-container` and every torpedo in it out of the DOM, and the guard must
sit **after** the `ignoreInitialBet` one-shot. ⚠️ **Add no repair path for the return** — the existing
`speedMultiplier` watcher and `handleVisibilityChange` already cover it; repairing a paused transition
is the ~27× speed error of #149.

**Graphics settings controls** ([#161](https://trello.com/c/FX5TpF6l)) **landed on `main` on
2026-08-09** via `feature/graphics-settings-controls` in `monkify-front` + `monkify-meta` (**no
`monkify-backend` branch — zero backend surface**, so `AGENTS.md` rule 8's integration test had
nothing to exercise, and `monkify-front` has no test harness: the bar was `yarn lint` + `yarn build` +
an in-browser pass at 1440 and 500). Distilled in
[`features/graphics-settings-controls.md`](features/frontend-ux.md); spec:
[`specs/graphics-settings-controls.md`](specs/graphics-settings-controls.md).

The two knobs #83 and #149 already built become real controls in the SETTINGS modal: a **light
graphics mode checkbox** (with an info `i`) and a **sky density slider** (`0–100%`, default 45%, 5%
steps), persisted in `localStorage` with **zero backend**. **Manual choice wins permanently** —
auto-detection only *seeds* a browser that never touched the control — while **phones stay locked
light below 1024px**, shown as ticked + disabled rather than hidden. **`0%` is genuinely off**: no
sprites, no spawning, no torpedoes and an **idle rAF loop**, with the backdrop tile, vignette and void
colour still rendering. ⚠️ **Light mode remains a `Math.min` floor over the density, never a competing
knob**, and `usePerfMode.ts` learns the **override only** — it must not become density-aware.
⚠️ Four spec findings bind hard: `/background`'s canonical-defaults rule needs a flag **seeded
synchronously from `window.location.pathname`** (the backdrop is global in `App.vue`, and route `meta`
is not resolved at first paint); the "~13%" boundary is really **10% on a 5% step grid**; idling the
loop at 0% also **freezes the backdrop drift** (intended — `animate()` is what advances `bgPosition`);
and the accessible info `i` already exists as `OrbitInfoTip.vue`, so it is promoted to
`ui/ArcadeInfoTip.vue` with a `tone` prop rather than copied. It is a **QOL change, not a new
feature** — rule 8's integration test does not apply. ⚠️ **The GRAPHICS group is ONE centre-aligned
row and that is a deliberate revision** — stacked, the slider column was ~72px against the checkbox's
~25px and set the height of the whole section. ⚠️ **Four ACs shipped unverified** (mid-round
SignalR/audio, tab-hidden-across-rollover, embedded iframe, a real screen reader); each exercises
machinery this change does not touch, but none was driven end to end.
**Icons and Menu overhaul** ([#159](https://trello.com/c/1omDew5a)) **landed on `main` on 2026-08-09**
via `feature/icons-and-menu-overhaul` in **`monkify-front`** + **`monkify-meta`** (**no
`monkify-backend` branch — zero backend surface**: no entity, enum, handler, consumer, EF mapping,
migration, endpoint or SignalR contract, and game art is resolved client-side in `utils/gameArt.ts`).
Frontend-only ⇒ **no unit or integration test** (`monkify-front` has no test suite and rule 8 had no
backend happy path to drive): the bar was `yarn lint` + `yarn build` + a browser pass at 0 console
errors, all green before the merge. Distilled in
[`features/icons-and-menu-overhaul.md`](features/frontend-ux.md); spec:
[`specs/icons-and-menu-overhaul.md`](specs/icons-and-menu-overhaul.md).

One **pixel-art icon registry** (`src/constants/icons.ts` + `ui/AppIcon.vue`, **24 icons**) replaces
nine inline feather-**stroke** SVGs, the VT323 letter `X`, the `✓ ✗ ▶ ▼` text characters and
`spinner.svg` — and every page/feature now carries an identity mark the way Orbit already did. **Two
grids declared per icon:** coarse **8×8** by default (exact at 16/24/32/40), fine **16×16** as a
standing opt-in standard (exact at 16/32/48), used today by **`practice` and `wallet`** — ⚠️ opting in
**costs the 24px rung**. ⚠️ **This retires the `#124` D7/AC16 rule** — `OrbitMark` and `OrbitGlyph` are
deleted, their drawings fold into the registry as **raw rects emitted verbatim** (the renderer never
rasterises them), which is what guarantees **zero visual change** at their call sites; Orbit's mark is
**not** redrawn, and its off-ladder sizes (11/13/14/18/20/26/28/30/52px) stay. Icons are
**decorative** (`aria-hidden` unconditional) and paint **`currentColor`**; **exactly two** carry a
baked fill (`win-at`, `falls-to`) and there must not be a third — which is also why a **brand mark
cannot live in the registry**: `AppIcon` renders one fill per drawing.

⚠️ **The card's stake/pot coin split never shipped.** The owner put Orbit's PER ROUND readouts on
**`usdc.svg`** — the same brand asset the lobby's TICKET and POT chips already use — which left both
pixel coins with **zero consumers**, so they were deleted (26 → **24** icons) rather than kept as
dangling rules; `usdc.svg` itself became the **official Circle mark**, which also changed the lobby
chips. ⚠️ **The pad shipped at 250px / 24px icons / VT323 15px**, not the spec's 270 / 32 / Press
Start 2P: PS2P's advance is exactly 1.0em against VT323's ~0.4em, so **no** PS2P size fits this pad —
the font, not the size, was the defect. The ☰ menu is a **3-column icon pad** (Home · Practice ·
Orbit · Stats · Fair · How to play · History · Responsible · Settings; **`/game` gets no cell**, and
the Orbit cell is gated on `orbitEnabled`, so with Orbit dark the pad is **eight** cells), with
`aria-current="page"` on the current cell and the **same grid** rendering the embed's two survivors.
Identity marks also reached the **pages and modals** — a scope gap the spec's call-site list had left
open — plus the five front-door section cards, where a mark **replaces** the `+ TITLE +` pluses the
same way `.dialog--has-lead` drops the dialog's amber square. Convention written up in
[`conventions/frontend.md`](conventions/frontend.md) → "Icons — one registry, two grids, always
decorative".

**Background & game display lifecycle hardening** ([#149](https://trello.com/c/No2Z0NMP)) **landed on
`main` on 2026-08-09** via `feature/background-lifecycle-hardening` in `monkify-front` +
`monkify-meta` (**no `monkify-backend` branch — zero backend surface**, so `AGENTS.md` rule 8's
integration test had nothing to exercise, and `monkify-front` has no test harness: the bar was
`yarn lint` + `yarn build` + an in-browser pass at 1440 and 500). Distilled in
[`features/background-lifecycle-hardening.md`](features/frontend-ux.md); spec:
[`specs/background-lifecycle-hardening.md`](specs/background-lifecycle-hardening.md).

Switching Chrome tabs while rounds rolled over left the game screen broken and laggy — and the
terminal scrolled horizontally. **Seven** defects, one root cause: **the animation layer trusted
`setTimeout` and in-flight transitions across tab hide/show**, which Chrome throttles in hidden tabs.
Tab return is now a **resync to a clean state** (re-derive speed from the *current* props, advance/drop
stale elements by wall-clock, re-arm spawning off `document.hidden` directly, sweep orphan torpedoes)
rather than a resume; every deadline is a **wall-clock timestamp + `visibilitychange` catch-up**, the
convention `useIdleDisconnect.ts:26-29` already spells out — now written up in
[`conventions/frontend.md`](conventions/frontend.md) → "Deadlines, not timers". ⚠️ The headline defect
was **arithmetic**: adding the pause duration to a `speedTransitionStartTime` the hidden-tab watcher
had already advanced pushed it into the **future**, the ease function **squared** the negative
progress, and the cubed `SPEED_BOOST_FACTOR` turned that into a **~27× speed error**. One exported
**`BG_DENSITY` (0..1), shipped at `0.45`**, scales the element cap, the spawn rate and a new torpedo
cap together, with **light mode as a `Math.min` floor over it, never a competing knob** — normal mode
is now **29 elements/layer, not 64** (~33 sprites on screen vs up to 128), torpedoes cap at 5, and
`spawnDelay` is floored at 150ms. The future slider must write **only** that constant; it is not built.
⚠️ **Defect #7 was folded in during implementation** (user's call): DaisyUI's `:root, [data-theme]`
base rule outranks `main.css`'s bare `html`, so the **root element** painted `#2A303C` instead of
`--color-void` — and since the root's background paints the **canvas** while `<body>` paints only its
own viewport-height box, every page taller than the screen carried a hard colour seam one viewport
down. Fixed with `html:root, body`; see `conventions/frontend.md` → "DaisyUI's base layer outranks bare
element selectors". ⚠️ Two spec findings that still bind: the `<=768px` `background-size: 200%` rule is
**dead code** (declared before the `<=1024px` `150%` rule at equal specificity, so 150% wins on phones
— it must **stay** dead, since making 200% apply would change the mobile zoom), and `overflow-x-hidden`
alone would **clip a winner's raw external id** on the `PAID TO` line, so the overflow fix carries
`break-all`. ⚠️ **Post-ship note:** the tile-seam fix (defect #6) is **near-unreachable in practice** —
light mode freezes the backdrop below 1024px, so only a viewport of exactly 1024px was ever affected;
it is kept as correctness insurance and was **not** the cause of the reported "many BGs".

**Redesign nice win modal** ([#157](https://trello.com/c/fcklCgcd)) **landed on `main` on 2026-08-08**
via `feature/redesign-nice-win-modal` in `monkify-front` + `monkify-meta` (**no `monkify-backend`
branch — zero backend surface**, so `AGENTS.md` rule 8's integration test had nothing to exercise, and
`monkify-front` has no test harness at all: the bar was `yarn lint` + `yarn build` + an in-browser
pass at 1440/390/320, re-verified after merging `origin/main` in). **Spec created 2026-08-07,
rewritten at the visual gate 2026-08-08.** No new `features/` doc — this redesigns a surface
`features/orbit-ui-overhaul.md` already describes, and that doc carries the update. Spec:
[`specs/redesign-nice-win-modal.md`](specs/redesign-nice-win-modal.md). The first-win Orbit upsell
moves off the **black** panel onto the **Orbit** material at `compact` (540) and keeps its
game-type-**conditional** supporting line — the word-ownership beat renders only on free-text games,
because it is simply false beside Beast Mode.

⚠️ **The first build of this card was rejected twice by the owner** ("still ugly", then "nice win
repeated 2 times… too much empty black space"), and the surface is now a **celebration first, an
upsell second** — which reverses four decisions the spec had recorded (D4 sprite size, D6 headline
placement, D10 word colour, D13 no motion). What shipped: titlebar **`CONGRATS!`**, body headline
**`NICE WIN!`** at 34px with **`you won your first game`** under it (the bar and the sub-line are
**one sentence split in two** — the full 33-character string clips on every phone at the bar's 14px),
Edson at **168px**, and the winning word inside a **miniature CRT** (`.win-screen`) that quotes the
game's own terminal: black glass, a `>` prompt, Press Start 2P in **phosphor green**, typed out one
character at a time and settling into a blinking block caret. ⚠️ That green is a **new named palette
exception** — `--color-phosphor` (`#4ade80`, the terminal's own), licensed **only** for quoted machine
output drawn on a screen. ⚠️ The word is sized from its **character count** via a container query
(`min(44px, 100cqi / cells / 1.06)`), because Press Start's advance is exactly **1.0em** against
VT323's 0.4em — a fixed size that flatters a 5-letter word overflows the 20-character
`choiceRequiredLength` ceiling. ⚠️ Leaves `variant="black"` **consumer-less on purpose** (kept, not
deleted), which makes the documented tutorial/help **overlay** tier implementation-free —
`conventions/frontend.md` records that, and corrects `compact` **608 → 540** in four places.

**Real API error messages** ([#158](https://trello.com/c/cmb6wfm7)) **landed on `main` on 2026-08-08**
via `feature/api-error-messages` in `monkify-backend` + `monkify-front` + `monkify-meta`. Distilled into
[`features/api-error-messages.md`](features/infra.md); spec
[`specs/api-error-messages.md`](specs/api-error-messages.md). Verified on the branch before merge:
`run_tests.bat` **901/901** and `run_integration_tests.bat` **116/116**, both green with **zero
test-file edits** exactly as F2 predicted, plus `yarn lint` + `yarn build`.

A rejected bet used to tell the player **"Request failed with status code 400"** — `services/handlers.ts`
toasted `error.message` (the axios status line) and threw away the sentence the backend actually wrote
(`useGame.ts:571-578` then writes only the generic terminal line). The fix lives in the **interceptor**,
so all **23** `mainAPI` endpoints are fixed at once: toast `messages[].value` when present, else one
friendly fallback, deduped by message text, with `handled === true` still winning and the raw error
going to `console.error` only. A shared `utils/apiError.ts` replaces **5** hand-rolled extractions.
⚠️ **Four code findings the refined card got wrong** (spec §3, and the spec wins): (**F1**) the backend
copy pass the card specifies is **already byte-identical to the live `.resx`**, so it was **retargeted**
by the owner at the genuinely awkward *live* payment-signature strings — `ChoiceAlreadyTaken`,
`RefundWarning`, `GameNotValidForBets` and all five `BetValidationResult` descriptions stay **untouched**;
(**F2**) the tests assert **symbolically** (`ErrorMessages.X` / `StringValueOf()`), so the suite must go
green with **zero test edits** — an edit means a forbidden key was reworded; (**F3**) `MessageType` is
`ValidationFailure = 2`, serialized numerically, so the two admin screens filtering `type === 1` have
**never** matched and always fell through to a generic fallback — fixed, which knowingly amends AC 11;
(**F5**) `token-expired` / `setRelaunching` have **zero emitters** in `src/` and `mainAPI` has no 401
branch, so AC 8 is a no-regression check, not a flow to preserve. ⚠️ **New work the card did not have:**
making the toast meaningful means every site that already reports a failure itself must mark
`handled = true` or the player reads the same sentence twice — `useGame` (~381), `useTokenBalance`
(**both** branches, or background polls start toasting), `DisplayNamePrompt`, `PlayerCustomizationModal`.
⚠️ The reserved-word reject stays **byte-identical** to a same-round collision (#150 — a distinct
wording announces Orbit), and `constraints.md` §6 vagueness is preserved. ⚠️ The pre-pay guard
(`useGame.ts:605-616`) already blocks the card's headline scenario before payment, so the **cross-round
Orbit claim is the only reliable repro**. No integration test required (`AGENTS.md` rule 8 scopes to new
features; this is a UX/bug fix + copy pass).

⚠️ **Two things the spec did not foresee, both decided by the owner at implementation time.**
(1) The **trailing space** on `SignaturePaid{Less,More}ThanBetAmount` was **load-bearing** — it was the
only separator before `AdviceOnDifferentSignatureBetAmount`, so stripping it as §6.2 asks would have
produced `…than this bet requires.If your transaction sent…`. `SolanaService.GenerateErrorMessageForTokenExchange`
now joins with an explicit `" "`, adding that file to the backend's two.
(2) **§7.4's "own toast — unchanged" was wrong for three sites**: `GamePage.vue`, `GameTypes.vue` and
`ResponsibleGamingPage.vue` toasted **`err.message` themselves**, i.e. the status line the card exists to
kill — and `ResponsibleGamingPage` did **not** mark `handled`, so a failed self-exclusion showed
"Request failed with status code 400" **twice**. All three fixed, via a second helper
`displayableErrorMessage(err, fallback)` that keeps a genuinely thrown sentence (a wallet that cannot
sign) but never lets an HTTP status line through.
⚠️ **Known gap, deliberately left:** 8 sites in `useOrbit.ts` (~646, 730, 781, 997, 1023, 1042, 1075,
1102) still fall back to `error.message`, so a 500 with no envelope leaks the status line into Orbit's
inline `lastError`. One-line fix each via `displayableErrorMessage`.

**Money change animations** ([#151](https://trello.com/c/AaPjqrw5)) **landed on `main` on 2026-08-08**
via `feature/money-changing-animations` in `monkify-front` + `monkify-meta` (**no `monkify-backend`
branch — zero backend surface**, so `AGENTS.md` rule 8's integration test had nothing to exercise, and
`monkify-front` has no test harness at all: the bar was `yarn lint` + `yarn build` + a manual pass,
re-verified after merging `origin/main` in). Distilled into
[`features/money-changing-animations.md`](features/frontend-ux.md); spec
[`specs/money-changing-animations.md`](specs/money-changing-animations.md).

Money no longer moves silently. One shared `MoneyValue` readout (value roll + floating `± ` chip,
**green credit / dim-ink debit, never red**) serves the two header pills and Orbit's two `ORBIT HOLDS`
figures, and the header's two icon doors are now **balance-carrying pills** with the wordmark cut below
`lg`. Only **attributed** changes animate; poll drift, catch-up reads and first loads land silently.
⚠️ Knowingly reverses the 2026-08-04 *"a DOOR, not a readout"* call (re-taken by the owner
2026-08-07), narrowly — a door that has a number to show shows it; `SIGN IN` stays deleted and signed
out the pill reads `ORBIT`. ⚠️ **Three things a future change must not get wrong:** the optimistic
Orbit delta stays **out of** `orbit.value.balance` (it would trip the auto-park watcher into a DB write
on a guess); `.money-value*` must **not** be added to `arcade.css`'s light-graphics `animation: none`
strip list (a one-shot, and every phone is in light-graphics); and `CHIP_MS_CREDIT`/`CHIP_MS_DEBIT`
must stay in sync with the CSS durations. ⚠️ Shipped with **three owner deviations** from the spec
(chip rises from below, credit dwells ~2.5s, header `$` prefix at 10/12px) and **one spec instruction
that could not be followed** (module-scope watchers crash at boot) — all recorded in the feature doc
and the spec's banner.

**Home Orbit card** ([#156](https://trello.com/c/k9cAI4ag)) **landed on `main` on 2026-08-07** via
`feature/home-orbit-card` in `monkify-front` + `monkify-meta` (**no `monkify-backend` branch — zero
backend surface**, so `AGENTS.md` rule 8's integration test had nothing to exercise; distilled into
[`features/home-dashboard.md`](features/home-and-onboarding.md), not its own feature doc). The front door
at `/` now carries one full-width `ORBIT` panel between PROVABLY FAIR and YOUR RECORD — a lead
paragraph, three beats and one `Set up Orbit` CTA — plus the word-claim row `GuideOrbit.vue` had
always been missing. It is **static copy that opens no request**: no `GET /orbit`, no hub group, no
signature, identical for a cold visitor and a running Orbit player, and the home page's "zero Orbit
calls" property is unchanged. `HomeSection` gained a third `tone="orbit"` and `arcade.css` one
`.eyebrow--orbit` rule. ⚠️ Gated on `orbitEnabled`, so it shipped with **zero production visibility**
while Orbit stays dark behind `VUE_APP_ORBIT_ENABLED` (#146). ⚠️ The word claim is now **advertised
publicly** for the first time — a human refused with `ChoiceAlreadyTaken` can *infer* an Orbit claim,
accepted knowingly; identity stays invisible (#118 unbroken).

**House seed bet** (#155) **landed on `main` on 2026-08-07** via `feature/house-seed-bet` in
`monkify-backend` + `monkify-meta` (`monkify-front` untouched). Every newly created round now opens
with one **ordinary** paid house bet on all three game types — **production only**, hard
`IsProduction()` gate, no kill switch without a deploy. The bet carries **no marker of any kind**;
`ValidateBetPayment` passes because the house wallet is both the sender and the bet's wallet, and
`FundedByOrbit` is deliberately never set. Because it counts as a distinct player, **one real player
is now enough to start a round**. `SpaceStroker.Seeder` is deleted; `specs/auto-bet-seeder.md` is
marked superseded. ⚠️ **Not yet running in production** — the house wallet still has to be created,
funded and put in Secret Manager (an operator step).

**Orbit single control** (⚠️ **no Trello card**) **landed on `main` on 2026-08-07** via
`feature/orbit-new-ui` in `monkify-front` + `monkify-backend` (shipped **together** — the API
contract changed). One flag: `Enabled` IS play/pause — `Stopped` dropped, the forced cash-out on
disable and the re-enable cool-off **deleted**, the rolling manual cash-out gap the only cool-off
left (a live countdown on the cash-out controls, never a state). Deposits gate on the enrolment
existing; the word claim follows playability; the modal became the small `/orbit` page (switch
guard, game picker, word gate). Distilled in
[`features/orbit-single-control.md`](features/orbit.md); spec
[`specs/orbit-single-control.md`](specs/orbit-single-control.md). Verified before the merge:
**853 unit tests green**, `yarn lint` clean. ⚠️ Orbit remains **dark in production** behind
`VUE_APP_ORBIT_ENABLED`. Migration `MergeOrbitStoppedIntoEnabled` applies on the next Consumers
start.

**Orbit feedback adjustments** (#150) **landed on `main` on 2026-08-06** via
`feature/orbit-feedback-adjustments` across all three repos, right behind #141 — the merge-order
hazard it carried is spent. Distilled in
[`features/orbit-feedback-adjustments.md`](features/orbit.md); spec
[`specs/orbit-feedback-adjustments.md`](specs/orbit-feedback-adjustments.md). Adds the reserved Orbit
word, STOPPED, partial cash-out, `GET /orbit/history`, per-event Telegram toggles and
cool-off deposits, plus one additive migration (`20260805154055_AddOrbitFeedbackAdjustments`).
⚠️ It also fixed a **pre-existing** money bug in `OrbitLedgerService.ResolveBalance` — see the feature
doc. ⚠️ Orbit remains **dark in production** behind `VUE_APP_ORBIT_ENABLED`.

**Orbit UI/UX overhaul** (#141) **landed on `main` on 2026-08-06** via `feature/orbit-ui-overhaul`
across `monkify-front`, `monkify-backend` (the deposit validator only) and `monkify-meta`. Distilled in
[`features/orbit-ui-overhaul.md`](features/orbit.md); specs
[`specs/orbit-page-restructure.md`](features/orbit.md) (**read first**) and
[`specs/orbit-ui-overhaul.md`](features/orbit.md) (*partially superseded*). Verified before the
merge: **814 unit tests green**, integration suite green, `yarn lint` + `yarn build` clean. ⚠️ Orbit remains **dark in
production** behind `VUE_APP_ORBIT_ENABLED`.

**Backfill `Players` in `RemoveOperators`** (production incident fix) **landed on `main` on
2026-08-05** via `feature/remove-operators-backfill` across `monkify-backend` (one migration file)
and `monkify-meta` (`monkify-front` untouched — the defect is server-side only). Spec:
[`specs/remove-operators-backfill.md`](features/remove-operators.md). Deployed to production
the same day: `api-00012-pxg` + `consumers-00009-w45`, commission now **1.0%**, bet history verified
intact across the migration.

The 2026-08-05 production deploy of `main` failed and was rolled back:
`20260729190119_RemoveOperators` is a **greenfield** migration that drops `OperatorPlayers`, renames
`GameBets.OperatorPlayerId` → `PlayerId`, then adds an FK to a **brand-new, empty `Players`** — so on
a database that already holds bets, every row is orphaned and it dies with `23503`. The transaction
rolled back whole, so no production data was lost. The fix stashes the player rows before the drop
and replays them (de-duplicated onto the narrower `ExternalPlayerId`-only unique key, with the
affected bets repointed) before the FK is added.

⚠️ Two general findings from it, worth carrying forward:

- **`ApplyMigrations` swallows its exception** and startup continues; the deploy tool still prints
  `Deploy complete.` and exits 0. **A green deploy is not evidence that migrations applied.**
- **A Terraform apply revokes IAM on retired secrets**, which silently blocks rolling Cloud Run back
  to an older revision until the bindings are re-granted.

**The home-hero sprite + wallet-connected animation** (Minor change, frontend-only) **landed on
`main` on 2026-08-05** via `feature/home-hero-sprite` across `monkify-front` and `monkify-meta`
(`monkify-backend` untouched — zero diff). Trello **#145**
([6T053xnq](https://trello.com/c/6T053xnq)), spec
[`specs/home-hero-sprite.md`](features/home-and-onboarding.md), distilled into
[`features/home-dashboard.md`](features/home-and-onboarding.md) → "The hero band".

Two findings from it are **general**, not hero-specific, and worth carrying forward:

- ⚠️ **A play-once GIF is NOT replayed by remounting its `<img>`.** Webpack inlines assets under 8 KB
  as **data URIs**, and Chrome keys its decoded-image cache by **URL** — so a fresh element on the
  same URI is handed back the *same finished animation* and renders the frozen last frame. Replaying
  needs a **changed URL**; a `#fragment` suffices, since it does not touch the payload. The spec
  asserted a `:key` remount was enough and it would have shipped broken.
- ⚠️ **A `html.light-graphics .x { }` rule DOES work inside `<style scoped>`** when `.x` belongs to
  that component — the scope attribute lands on `.x` and the root is an ordinary ancestor.
  `specs/light-graphics-mode.md` §7.5's caveat is about targeting the **root itself**; corrected there.

**Previously shipped —** **Windowed stats + query-layer performance** (Feature) **landed on `main` on
2026-08-05** via `feature/query-indexes`, shipping Trello **#148** and **#133** together across
`monkify-backend`, `monkify-front` and `monkify-meta`. Spec
[`specs/windowed-stats-query-performance.md`](specs/windowed-stats-query-performance.md), distilled
into [`features/windowed-stats-query-performance.md`](features/stats-and-history.md).

Two deliberate gaps left behind, both documented in the feature doc under "Known limits": history
pagination is still `OFFSET`-based (~45ms deep pages at 1M rows), and the recent-rounds feed is
unimproved (~94ms, Seq Scan — its filter matches ~99% of `Games`, so a scan is the correct plan).

- _Previously shipped:_ **Hide Orbit behind a frontend env flag** (QOL / release-gating change) — spec
`specs/orbit-frontend-flag.md`, Trello #146 ([zJsoFVaW](https://trello.com/c/zJsoFVaW)).
`feature/orbit-frontend-flag` merged to `main` in **`monkify-front`** + **`monkify-backend`** +
**`monkify-meta`** on **2026-08-04**; branch and worktrees deleted. Distilled into
`features/orbit-custodial-wallet.md` → "The production gate" and `conventions/frontend.md` →
"Feature flags".

Orbit (#121/#122/#123) is on `main` in both repos but is **not production-deliverable** — still in
testing and improvements — so every other change that lands on `main` is blocked behind it. One
build-time frontend flag, **`VUE_APP_ORBIT_ENABLED`** → `orbitEnabled` in
`src/constants/featureFlags.ts`, **fail-closed** (`=== 'true'`), hides every Orbit surface **and**
silences every Orbit runtime path so a production build can ship with Orbit dark.

- ⚠️ **Fail-closed is the opposite test from its neighbour** `allowFreeBets` (`!== 'false'`,
  fail-open). Intentional: an un-configured production build must hide Orbit, not ship it.
  `allowFreeBets` is **not** changed. Cost: every developer's git-ignored `.env.local` lacks the key,
  so Orbit vanishes from local `yarn serve` until each dev adds the line.
- ⚠️ **The gate is UI *and* runtime**, at **mount points *and* inside the composables** — a
  `v-if`-only gate would still call `/orbit/*`, join the hub group and poll for 45 s after every
  round. The first-win upsell is invoked from **`useGame`'s victory one-shot**, not from a template,
  which is why the composable guard is the load-bearing one.
- ⚠️ **Guard inside `useFirstWinUpsell.maybeOpen()`, never `useGame.ts:335`.** That block also plays
  the **victory audio** and posts the embedded **`bet-settled`** message the casino uses to refresh
  the player's balance — gating it would silently break both.
- ⚠️ **The backend engine keeps running.** The safety argument is *no enrolled players + no
  `AllowOrbit = true` game type ⇒ the planner plans nothing* — it rests on production **state**, not
  on code. At ship time the **owner attested** production met both conditions (AC 12); that was an
  attestation, **not** a query this repo ran, so **re-verify before any deploy that flips the flag**.
  A non-zero result blocks the deploy and reopens the rejected backend kill switch.
- ⚠️ **AC 11 (local Docker stack) was never run** — the running stack was left intact by choice, so
  the `setup_local_environment.bat` line is verified by reading, not by a rebuild. First person to run
  that script should confirm `.env.production.local` carries `VUE_APP_ORBIT_ENABLED=true`.
- ⚠️ **`feature/orbit-ui-overhaul` worktrees are also open** on `monkify-front` + `monkify-meta` and
  touch the same files (`GameHeader.vue`, `components/orbit/*`). Whichever lands second **must
  re-apply the gates** — a rebase that drops an `orbitEnabled` check ships Orbit into production.
- **Deliberately NOT gated:** the admin `Allow Orbit auto-play` checkbox (`allowOrbit` is
  creation-only on a deactivate-or-create portal, so hiding it would permanently strand every game
  type created while Orbit is dark). **No escape hatch** — build-time only.
- **Accepted limitation:** a wallet already holding an Orbit balance cannot cash out on a flag-off
  build (recovery = a flag-on build or a manual backend payout). Exposure ≈ nil — production has
  never rendered the enrolment UI.
- `monkify-backend` gets **one line** in `scripts/setup_local_environment.bat` — **no C#, no
  migration, no test change**. Integration test **waived** (no backend surface); no unit test.

- _Previously:_ **House commission 0.5% → 1%** (Minor change) — merged to `main` (and `master` for
the LP) on **2026-08-04** via `feature/commission-rate-1-percent` in **`monkify-backend`** +
**`monkify-lp`** + **`monkify-meta`**; branch and worktrees deleted. Spec
`specs/commission-rate-1-percent.md`, distilled into the existing `features/crypto-commission.md` (no
new `features/` doc — Minor change). Trello #147 ([quCTPKxZ](https://trello.com/c/quCTPKxZ)).
**`monkify-front` was not affected** — every badge renders the rate live from the backend, so the rate
rise was zero frontend work.
  Raised `GeneralSettings.Operator.Token.CommissionPercentage` from `0.5` to `1.0` in the committed
  `spacestrokersettings.json` — the **whole** behavior change; no code, no entity, **no migration**. The
  commission **base is unchanged**: profit above each winner's own stake with the house-funded lottery
  base excluded, so the yield as a share of the pot is **0.83%** (6 bets/1 winner), **0.50%** (heads-up),
  **0.33%** (3 bets/2 winners) — the card's "per pot" wording was rejected, a pot rake would tax the
  winner's own stake and the house's own jackpot. Shipped with a **guard test in the fast unit suite**
  (`UnitTests/Api/CommissionRateTests.cs` — the whole change is a config value, so nothing else asserts
  it), the **LP Terms page** at *"currently 1%"* + deferring to the in-app badge, and comment fixes.
  ⚠️ **Merging did NOT change the live rate:** prod reads the committed file, so **both Cloud Run hosts
  must be redeployed together, with no paid round open** at the switch (Space Jackpot's 24h round is the
  exposure — the clean window is just after its 22:00 UTC draw). Until that deploy, production still
  charges 0.5%.

- _Previously:_ the **`release/ui-ux-changes`** train **landed on `main` on 2026-08-04**, taking all
three of its cards to **Shipped** in one merge — `monkify-front` (29 commits) and `monkify-meta`
(11 commits); `monkify-backend` was never on the train. The release branch is deleted.

- _Last shipped:_ **Practice mode changes** (Feature, frontend-only) — merged into
  `release/ui-ux-changes` on 2026-08-03 and **on `main` since 2026-08-04**. Spec
  `specs/practice-mode-changes.md`, distilled into `features/practice-mode.md`. Trello #142
  ([EfIRqHbT](https://trello.com/c/EfIRqHbT)). Repos **`monkify-front`** + **`monkify-meta`**;
  **`monkify-backend` is not affected**.
  Turns `/game/practice` into a guided tutorial: the player **always wins**, the terminal's
  stream-lag glitch is fixed, and Edson narrates four skippable talking-bubble beats — bubbles 2 and 4
  **hold the round** (an accumulated-pause offset on the round clock), bubble 3 deliberately does not.
  It **reversed** four rules in `specs/practice-mode.md` / `features/practice-mode.md` (the unbiased
  draw, "teach LOSING", "real timings, never compressed", and `PracticeTyper` mirroring `GameTyper`) —
  **already corrected** in both docs. Integration test **waived** (no backend surface).

- **App home changes** (Minor change, **frontend-only**) — merged into `release/ui-ux-changes`
  (2026-08-03) in `monkify-front` + `monkify-meta` and **on `main` since 2026-08-04**; the feature
  branch and its worktrees are gone. Spec `specs/app-home-changes.md`, distilled into
  `features/home-dashboard.md`. Trello #143 ([eS4SpiZ6](https://trello.com/c/eS4SpiZ6)).
  A polish pass on `/`: the hero's quiet line, the `PLAYERS` chip off race/preset picker cards (**both
  `/` and `/game`** — one shared component), the how-it-works section retitled
  `WANNA LEARN HOW TO PLAY?` with relabelled beats and a `Learn more` button, the `TRY IT FREE` panel
  deleted in favour of a full-width `PROVABLY FAIR` (its `practice` analytics event re-homed into
  `HomePage`), plus `THE FLOOR`'s two reads deferred to an `IntersectionObserver` and the boot
  catalogue reused.
  The train also carried **#142** (practice mode changes) and **#144** (how-to-play changes); it was cut
  from `main` at `993261e`, so it already contained #138's trimmed hero.
  ⚠️ **One late change rode the train outside any card's scope** (owner's call at ship, 2026-08-04):
  `HomeHero`'s newcomer **primary** CTA on a quiet floor is now labelled **`TUTORIAL MODE`** instead of
  `TRY IT FREE`. Only that one of the three sites changed — the newcomer's **secondary** CTA (a round is
  live) and the **cold** hero's button still say `TRY IT FREE`, so `/game/practice` deliberately has two
  names. Recorded in `features/home-dashboard.md` → "The connected state's two shapes".
  ⚠️ **Two of the card's own premises are corrected in the spec:** the per-type `GET /games/{id}`
  fan-out **stays** (removing it would gut the live layer and #138's hero CTA), and the "duplicate
  `/games/active-types`" is already collapsed by `singleFlight` (#104) — so that item is a cleanup, not
  a request saving.
  ⚠️ **The envelope trap this card fell into**, recorded in `features/home-dashboard.md` → "Live
  figures": `mainService.getGamesTypes()` returns `DefaultResponse<ActiveGame[]>`, **not** the array.
  Assigning it whole to `gameTypes` rendered one nameless `TICKET undefined` card per envelope field and
  an eyebrow reading `0 games` — and **`yarn lint`, `yarn build` and `tsc --noEmit` all passed**, because
  nothing in `monkify-front` type-checks `.vue` script blocks (`vue-tsc` is not a dependency). Grepping a
  built bundle for copy strings does not catch a broken render; open the page.

- **How to play changes** (Minor change, frontend-only), spec `specs/how-to-play-changes.md`,
  Trello #144 ([aUrybUea](https://trello.com/c/aUrybUea)). `feature/how-to-play-changes` merged into
  `release/ui-ux-changes` in `monkify-front` + `monkify-meta` on **2026-08-03**, and **on `main` since
  2026-08-04**. `monkify-backend` is unaffected.
  Restructures the shipped `/how-to-play` guide: the first three sections **merged into one**
  (`how-the-game-works`) as a **six-step** rail, a new **`WIN THE POT`** step with the only new asset
  (`won.gif`), Space Jackpot's card now tells the **real anchored draw time**
  (`EVERY DAY AT 10PM (UTC)`, gated on live values via `isDailyAt` — no backend work), and
  `YOUR SETUP` became **`PLAYER PREFERENCES`**. Sections **8 → 6**; four anchor ids retired with no
  alias map. `features/how-to-play.md` + `specs/how-to-play.md` were corrected in the same merge.
  ⚠️ **Shipped with six rail steps, not the seven specced:** `PLACE YOUR BET` was dropped at ship time
  (owner call) for repeating its neighbours — so the page states **nowhere** that betting closes when
  the round starts, or that a phone has no lever. Known gap: fold both into `PICK YOUR WORD`; do not
  reinstate the row. Integration test **waived** (no backend surface).
  The train also carried **#142** (Practice mode changes) and **#143** (App home changes); all three
  reached `main` together on **2026-08-04**.

- _Previously shipped:_ **Trim the home hero** (Minor change, frontend-only) — `feature/home-hero-trim`
  merged to `main` in `monkify-front` + `monkify-meta` on **2026-08-03**; spec
  `specs/home-hero-trim.md`, distilled in `features/home-dashboard.md` → "The hero band".
  Trello #138 ([U1Sdsk6Y](https://trello.com/c/U1Sdsk6Y)). `HomeHero.vue` on `/` went from **531 px to
  249–250 px cold / 223 px connected**, so `+ PICK YOUR GAME +` and the mode-card tops clear the fold
  at 1366×768 (picker heading 617 → 336 px, first card 672 → 391 px). The 533 KB `edson-ship.gif` gave
  way to the 45 KB `guide/game-modes.gif`, `shrink-0` and sized by **height** so the art can never
  drive the band, hidden below `sm`, and **looping even under `html.light-graphics`** — a knowing
  scoped exception to `specs/light-graphics-mode.md` §7.10, not a repeal.
  ⚠️ **Deliberately reverses `specs/home-dashboard.md` decisions 23/28/29** (the raised padding, the
  dominating saucer, the perf-mode still swap) — that section carries a post-ship note. The frameless
  rule (22–23) and the one-handler entry rule (34) stand.
  ⚠️ **Owner feedback during implementation went well beyond the spec's §7 design pass** — page-wide
  section rhythm, a **two-shape connected hero** (first-time vs returning), mobile centring, and the
  `38ch`→`62ch` correction AC2 forced. **`specs/home-hero-trim.md` §14 and
  `features/home-dashboard.md` are the current truth; §7 is the pre-implementation design.**
  ⚠️ The hero sprite is **temporary** — [#145](https://trello.com/c/6T053xnq) replaces it with real
  art plus a wallet-connected animation, and deletes the now-unimported `edson-ship.*` (559 KB).

The Orbit release train (#121/#122/#123, branch `release/orbit`) **has merged to `main`** in
`monkify-front` + `monkify-meta` (it was queued when this section was last written).

- _Last shipped:_ **Practice mode** (Frontend feature) — `feature/practice-mode` merged to `main` in
  `monkify-front` + `monkify-meta` on **2026-08-03**; spec `specs/practice-mode.md`, distilled in
  `features/practice-mode.md`. Trello #129 ([WKXsNym4](https://trello.com/c/WKXsNym4)).
  `/game/practice` runs a complete round in the browser — no request, no socket, no wallet, no money —
  so a first-time visitor can watch a race end without connecting a wallet, paying on-chain and
  waiting for the second player every seeded type requires. `/soon` + `ComingSoonPage.vue` are deleted
  with it. **`/game` and every real-game money guard are untouched** — that isolation is the feature's
  central constraint, so treat any diff inside `useGame` / `GameConsole` / `GameTerminal` /
  `GameMonkey` / `GamePage` as out of scope.
  ⚠️ Owner feedback during implementation superseded several spec decisions (real timings instead of
  compressed, batched streaming, real payout copy, full audio bed, lever-as-next-round, no fairness
  line). **`features/practice-mode.md` is the current truth and lists every deviation.**
  ⚠️ Two traps: the winner must be **drawn, not typed** (a "generate until the word appears" loop
  never terminates at 26⁶ windows vs ~59 k characters), and **`useNearMiss` is a module singleton**
  shared with the live game screen — practice reuses the pure `utils/nearMiss.ts` instead.

- _Previously:_ **Mobile game design update** (Frontend feature, visual) —
  `feature/mobile-game-design-update` merged to `main` in `monkify-front` + `monkify-meta` on
  **2026-08-02**; spec `specs/mobile-game-design-update.md`, distilled in
  `features/mobile-game-design-update.md`. Trello #134 ([yPwW5Awx](https://trello.com/c/yPwW5Awx)).
  The mobile `/game/play` GAME view is now a **three-band column** — tabs → terminal → Edson →
  cabinet pinned bottom — built from the real desktop art (`terminal.png` plates, the black visor,
  `keyboard.png`, the lever). The compact gradient bet panel is gone, which knowingly reverts the
  mobile half of `specs/mobile-preset-keypad.md`.
  ⚠️ **Three owner decisions taken after the card was written, so the card's own prose contradicts
  the shipped code in two places — the code is the newer decision:**
  **(1)** the `GAME | CHAT` tabs did **not** keep the filled-amber active state the card and **AC #17**
  promised; the selected tab is now the lit screen itself (true black + `#4ade80` phosphor + a 2px
  bar), because the amber chip fought the CRT. **(2)** the card's *"Desktop — not touched, in any
  way"* (**AC #18**) no longer holds: the chat composer moved onto `--ctl-h-sm` (**overriding
  `specs/control-sizing-system.md` D5**, which had scoped it out) and `.animate-wobble` was pulled
  out of the `html.light-graphics` kill list, which also reaches a desktop running a software
  rasterizer. **(3)** the mobile screen panel was re-skinned mid-flight to true black glass in a
  `#5c6a8c` 7px housing with 4px corners.
  ⚠️ **Verification is partial and deliberately recorded as such:** layout order without page scroll,
  console composition, short-viewport scrolling, Edson-shrinks-first at 360px and the lever tap
  target were checked in a live browser. **Betting through the lever, the preset keypad, the
  per-status animations, the native-keyboard focus behaviour, wallet connect, near-miss during a live
  race and round rollover were NOT exercised** — they need a real round and a wallet. All 18 ACs are
  still unchecked on the card on purpose.
- _Shipped before that:_ **Control sizing system** (Minor change, **frontend-only**) —
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
- _Previously:_ **How to play** (Feature, **frontend-only**) — `feature/how-to-play`
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
