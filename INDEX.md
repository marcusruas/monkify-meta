# Monkify — Project Knowledge Base (INDEX)

> **Read this file first on every task.** It is the map of the Monkify knowledge base.
> It tells you where things live, what each document is for, and the rules you must never break.

Monkify is a real-time, multiplayer betting game where a simulated "monkey" types random
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
   already exists in production (sessions, operators, payment flows, player identity, free-bet mode,
   etc.). The spec you are building is one piece of a shared system, not an isolated module.
3. While designing and coding, actively look for **cross-feature interactions**: shared entities
   and tables, request headers, payment and payout paths, session/bet lifecycle, operator scoping,
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

## Repositories (in this workspace, `e:\Monkify`)


| Folder             | What it is                                                               | Covered here       |
| ------------------ | ------------------------------------------------------------------------ | ------------------ |
| `monkify-backend`  | .NET 8 backend: API, background consumers, domain, infrastructure, tests | Yes (primary)      |
| `monkify-front`    | Vue 3 + TypeScript frontend (the game UI)                                | Yes (primary)      |
| `monkify-deployer` | Deployment assets/tooling                                                | Not yet documented |
| `monkify-lp`       | Landing page                                                             | Not yet documented |
| `monkify-meta`     | **This knowledge base**                                                  | —                  |


> Note: when the user says "the backend" they mean `monkify-backend`; "the front" / "the UI"
> means `monkify-front`. (The repo is `monkify-backend`, not "monkify-back".)

## Map of this knowledge base


| File                        | Purpose                                                                                  | Status               |
| --------------------------- | ---------------------------------------------------------------------------------------- | -------------------- |
| `INDEX.md`                  | This map. Read first.                                                                    | Drafted              |
| `architecture.md`           | Repos, services, RabbitMQ, PostgreSQL, Redis, SignalR, Solana, EF, payment processors    | Drafted              |
| `constraints.md`            | Non-negotiable rules: secrets, branches per repo, unit tests, migrations                 | Drafted              |
| `glossary.md`               | Session, Bet, Operator, Typer, SessionParameters, etc.                                   | Drafted              |
| `conventions/backend.md`    | EF inline in DbContext, migrations in Consumers, MediatR handlers, MassTransit consumers | TODO (stub)          |
| `conventions/frontend.md`   | Vue 3, composables (`src/hooks`), types in `src/types`, axios service layer              | TODO (stub)          |
| `features/sessions.md`      | Session lifecycle, the typer, rewards                                                    | Drafted              |
| `features/operators.md`     | Multi-tenant operators; distilled from `specs/operator-feature.md`                       | Shipped              |
| `features/free-bet-mode.md` | Dev/QA free bets without Solana payment; distilled from `specs/free-bet-mode.md`         | Shipped              |
| `features/operator-player-identity.md` | Unified `OperatorPlayer` model; remove `Bet.Wallet`; distilled from `specs/operator-player-identity.md` | Shipped              |
| `features/typer-pacing-scalability.md` | Wall-clock typer pacing, fallback winner, duration bounds; distilled from `specs/typer-pacing-scalability.md` | Shipped              |
| `features/launch-token-validation.md` | Signed casino launch flow (`X-Launch-Token`); `Operator.PaymentModel`; replaces `x-operator-id` | Shipped              |
| `features/operator-wallet-api.md` | Regulated (Model A) operator wallet debit/credit/refund; unified `TransactionLog`; commission removed | Shipped              |
| `features/dev-qol-stuff.md` | Dev QOL: operator from token (not URL), dev launch buttons (`VUE_APP_DEV_LAUNCH`), `Monkify.Tests.Integration` suite | Shipped              |
| `features/rg-pre-bet-hook.md` | Opt-in per-operator responsible-gaming pre-bet check (`IPreBetCheckService`); allow/deny/fail-closed before the wallet debit | Shipped              |
| `features/player-customization-modal.md` | Header-gear modal: display name (all operators) + preferred display currency (regulated only); `GET /fx/rates`, `GET /players/preferences`, client-side USD→fiat rendering; display-only | Shipped              |
| `features/session-chat.md`  | Per-session chat on `/game` via `ActiveSessionsHub`; display-name identity, DB audit persistence + daily purge, client 200-char/1s limits, no history replay | Shipped (v2)         |
| `features/operator-admin-page.md` | Operator self-service admin portal at `/monkify/admin`: admin-scoped RS256 token (`X-Admin-Token`), create/list/deactivate session types (all 3 modes), deactivate-or-create (no edit), dev header icon → `POST /dev/admin-token` | Shipped              |
| `features/sales-technical-documents.md` | B2B SaaS sales doc set in `sales/` — per-model pitch + under-NDA integration overview (Model A/B), anti-clone playbook; EN + PT-BR | Shipped              |
| `features/session-pot-display.md` | Frontend-only: the `> POT:` line is shown for **all** session types (races + lottery), seeded from `Σ bets` (+ lottery base), live via `/Bets`, visible during/after the run; distilled from `specs/session-pot-display.md` | Shipped              |
| `features/characters-away.md` | Near-miss reveal: bottom-docked `> N AWAY!` row per own choice (KMP tail-prefix `away = L − k`) that climbs, loops every 2s, and types out the winner on a win; in-game `> POT:`; backend emits the fallback-decided winner as a final terminal batch | Shipped              |
| `features/audio-overhaul.md` | Frontend-only AI (Suno) arcade-chiptune audio: idle/starting/random-running background per session status, own-bet/button/error SFX, bettors-only victory/lose, menu loop on the sessions page, header mute+volume (`localStorage`); 9 mp3 assets (kept `error1.wav`). Proximity "coin" bleeps dropped. Also: `/`→`/monkify/game` redirect + session-connection teardown fix | Shipped              |
| `features/payment-methods.md` | **Per-session** payment: `SessionParameters.PaymentMethod` (`Standard`=off-chain USD wallet / `SolanaToken`=on-chain per-operator registered token, key AES-GCM encrypted at rest); unified `IPaymentMethodService` strategy+resolver + availability gating; ledger split (`Bet`/`TransactionLog` `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`); admin token CRUD. **Removes `Operator.PaymentModel`** | Shipped              |
| `features/iframe-embedding.md` | Frontend-only: a **regulated (Model A)** casino embeds the game in an `<iframe>` — `useEmbed` detection, secret-free `{source:'monkify'}` postMessage lifecycle API, embedded 401→`token-expired`+"relaunching" (never `/error`), fills the frame, permissive `frame-ancestors *`, example page + `EMBEDDING.md`; distilled from `specs/iframe-embedding.md` | Shipped              |
| `features/iframe-crypto-sessions.md` | Frontend-only, **stacked on #3**: paid **`SolanaToken`** bets inside the iframe via a **same-origin wallet-bridge popup** (`/monkify/wallet-bridge`) that returns the on-chain signature+wallet (`source:'monkify-wallet'`); popup opened synchronously on click, token-native amounts, no new parent contract; distilled from `specs/iframe-crypto-sessions.md` | Shipped              |
| `features/session-type-stats.md` | Per-game all-time stats per session type via `GET /sessions/{parametersId}/stats` (biggest pot, biggest single payout, rounds played, total paid out, hottest choice for preset types) — **on-demand aggregate + Redis cache, no new entity/migration**; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type, operator-scoped/vague; rendered in a lazy **STATS** tab of the `/game` right-side panel (bundled with **CHAT** via `GameSidePanel`) | Shipped              |
| `features/provably-fair.md` | Commit-reveal `ServerSeed` (committed on the first bet, revealed after `Ended`), portable HMAC-SHA256 keystream RNG + labelled fallback, Stake-style client seed + `Bet.Nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /sessions/{id}/verification` + offline `public/verify.html`; terminal shows commitment/reveal only | Shipped              |
| `features/wallet-display-name.md` | SolanaToken player display-name/preferences key off the **connected wallet** everywhere: backend prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`); `InsertBet` carries `externalPlayerId` on live notifications; frontend reactive per-wallet load + "Connect wallet to customize" gate + currency hidden for SolanaToken | Shipped              |
| `specs/operator-feature.md` | Full implementation spec for Operators                                                   | Shipped              |
| `specs/free-bet-mode.md`    | Full implementation spec for dev-only Free Bet Mode                                      | Shipped              |
| `specs/operator-player-identity.md` | Operator-scoped unified player model (`OperatorPlayer`, remove `Bet.Wallet`)     | Shipped              |
| `specs/typer-pacing-scalability.md` | Typer wall-clock pacing — consistent duration across configs and servers           | Shipped              |
| `specs/launch-token-validation.md` | Signed casino launch flow — RS256 launch token, `Operator.PaymentModel`, prefs endpoint | Shipped              |
| `specs/operator-wallet-api.md` | Model A (Regulated) operator wallet debit/credit/refund adapters; unify `TransactionLog`, drop `RefundLog`, remove commission | Shipped              |
| `specs/dev-qol-stuff.md`    | Dev QOL: operatorId out of game URL (decode token `iss`), dev payment-model launch buttons, Testcontainers session integration tests | Shipped              |
| `specs/rg-pre-bet-hook.md`  | Responsible-gaming pre-bet check hook (`IPreBetCheckService`, opt-in per operator, fail-closed) wired into `RegisterBetHandler` before the wallet debit | Shipped              |
| `specs/sales-technical-documents.md` | Non-code: B2B SaaS sales docs in `sales/` — per-model (Model A/B) pitch + under-NDA integration overview (grounded in shipped code) under `model-a/`+`model-b/`, plus a single anti-clone playbook; EN + PT-BR; placeholders only, paid steps deferred | Shipped              |
| `specs/player-customization-modal.md` | Header-gear customization modal: edit display name (all operators) + preferred display currency (regulated only); FreeCurrencyAPI USD-base rates cached in Redis 6h, `GET /fx/rates`, new `GET /players/preferences`, PATCH allow-list, `DisplayName` on bet DTO/SignalR; display-only (settlement stays USD), no migration | Shipped              |
| `specs/session-chat.md`     | Per-session chat over `ActiveSessionsHub` (`SendChatMessage` → `{sessionId}/ChatMessage`); **v2:** nickname = player display name (client-resolved, crypto must connect to send), DB persistence (`SessionChatMessage`) via event→consumer + daily purge (`Chat.RetentionDays`, default 7), client-only 200-char/1s limits, no history load (late joiners see nothing), read-only on end, reset on rollover; right-sidebar `GameChatPanel` | Shipped (v2)         |
| `specs/operator-admin-page.md` | Operator self-service admin portal (`/monkify/admin`): admin-scoped RS256 token (`X-Admin-Token`, reuses `LaunchPublicKey`; not contract-gated), create/list/deactivate session types (all 3 modes; deactivate-or-create, no edit), dev header icon → `POST /dev/admin-token`; `AllowFreeBet` hidden | Shipped              |
| `specs/session-pot-display.md` | Frontend-only: show the `> POT:` line for **all** session types (races + lottery), not just lottery; generalize `currentLotteryPot` → `currentPot` in `useSession.ts` (seed `Σ bets` + lottery base only for lottery, accumulate live via `/Bets`), move the POT `<p>` out of the lottery-only block in `GameTerminal.vue` + a second POT line in the running/ended view (AC #8); reuse `formatAmount`; no backend/DB/SignalR change | Shipped              |
| `specs/characters-away.md`  | Characters Away near-miss reveal. Spec body is the original 3/2/1 pinned-milestone design; **shipped** behaviour (see spec Post-ship notes + `features/characters-away.md`) is a bottom-docked closest-approach row per own choice (KMP `away = L − k`) that climbs + loops every 2s + types out the winner, plus in-game `> POT:` and a backend fallback terminal reveal | Shipped              |
| `specs/audio-overhaul.md`   | Audio overhaul with AI — **(1/3) manifest & Suno-generation spec**. Sound manifest + per-item Suno prompts + the frontend integration contract for cards 2 (#34) & 3 (#35). **Shipped reality differs:** the 3/2/1 proximity "coin" bleeps were dropped (Suno couldn't produce them) → **9 generated mp3s, not 12**, and no proximity audio. See `features/audio-overhaul.md`. Frontend-only; branch `feature/audio-overhaul` | Shipped              |
| `specs/audio-overhaul-generation.md` | Audio overhaul — **(2/3) asset generation spec**. Suno-MCP + ffmpeg generation pipeline with approve-each review. **Shipped reality:** generated **manually in the Suno UI** (MCP ran out of credits) and post-processed with ffmpeg; **9 assets** committed to `monkify-front/src/assets/audios` (kept `error1.wav`, dropped `success1.wav` + old `.wav`s); the 3 coin bleeps were cut. See `features/audio-overhaul.md` | Shipped              |
| `specs/audio-overhaul-wiring.md` | Audio overhaul — **(3/3) wiring spec**. `helpers.ts` master gain/mute, idle + one-shot Game Starting + random running (`GameAudio.vue`), own-bet/error/button, bettors-only victory/lose, header mute/volume, timing fixes; asset swap. **Shipped extras beyond spec:** proximity bleeps **omitted** (assets dropped); added a menu loop on the sessions page, a `/`→`/monkify/game` redirect, and a session-connection teardown fix. See `features/audio-overhaul.md` | Shipped              |
| `specs/payment-methods-refactor.md` | Removes operator `PaymentModel`; per-session **`PaymentMethod`** (`Standard`/`SolanaToken`); per-operator **registered Solana tokens** (encrypted key) via admin portal; unified `IPaymentMethod` strategy+resolver; ledger split — `Bet`/`TransactionLog` carry `ExternalTransactionReference`+`PaymentMethod`, new `CryptoTransactionLog`. **Shipped** (encryption deviates: AES-GCM/config passphrase, not Data Protection) | Shipped |
| `specs/iframe-embedding.md` | Frontend-only: let a **regulated (Model A)** casino embed the game in an `<iframe>`. New `useEmbed` (`window.self!==window.top` \| `?embed=1`) + `utils/embed.ts` postMessage emitter (`{source:'monkify',type,payload}`, `targetOrigin:'*'`, secret-free, USD); embedded 401/expired/inactive-operator emits `token-expired` + light "relaunching" state instead of `/error` (branch in `mainService.ts` + `useOperator.ts`); lifecycle emits `ready`/`session-started`/`bet-placed`/`bet-settled`(bettors-only)/`session-ended`/`resize`(hint) from `useSession.ts`+`App.vue`; fills the frame (`main.css`); permissive `frame-ancestors *` in `nginx.conf`; static `public/embed-example.html` + `EMBEDDING.md`. **No backend.** Branch `feature/iframe-embedding` | Shipped              |
| `specs/iframe-crypto-sessions.md` | Frontend-only, **stacked on #3**: make paid **`SolanaToken` (on-chain)** bets work inside the #3 `<iframe>`. Extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move into a **same-origin popup** on the game host (route `/monkify/wallet-bridge`, `WalletBridgePage.vue`) that `postMessage`s the signature + wallet back (`source:'monkify-wallet'`, `utils/walletBridge.ts`); `useSession.bet()` acquires the signature in-frame **or** via popup (`useEmbeddedWallet.ts`), popup opened **synchronously** on click (blocked → visible affordance); relax the `GameConsole` `connected` gate when embedded; #3 lifecycle payloads made **token-native** (never USD). **No backend.** Branch `feature/iframe-crypto-sessions` | Shipped              |
| `specs/session-type-stats.md` | Per-game general stats per session type (all-time): `GET /sessions/{parametersId}/stats` → biggest pot, biggest single payout, rounds played, total paid out, hottest choice (most-picked, preset types only); **on-demand aggregate + Redis cache, no new entity/migration** (documented reversal of the maintained-counter idea); lazy-loaded `/game` stats panel; only `RewardForWinnersCompleted` rounds count, free bets excluded, single currency per type; token-gated + operator-scoped | Shipped |
| `specs/provably-fair.md` | Commit-reveal server seed (committed on the **first bet**, revealed after `Ended`), portable **HMAC-SHA256 keystream** RNG (rejection sampling, canonical bet order) replacing `System.Random`/`Int32`, Stake-style client seed + per-bet `nonce` (`Bet.Seed = HMAC(clientSeed, nonce)`), reveal-gated `GET /sessions/{id}/verification`, standalone `public/verify.html` verifier (in-terminal recompute dropped — terminal shows commitment/reveal only). Greenfield migration (drop `Session.Seed`; add `ServerSeed`/`ServerSeedHash`, `Bet.Nonce`). | Shipped |
| `specs/wallet-display-name.md` | **Full-stack:** make SolanaToken player display-name/preferences key off the **connected wallet** everywhere. Frontend: reactive per-wallet load (`App.vue`, wallet sent only in a SolanaToken context), customization-modal **gate** ("Connect wallet to customize" when no wallet AND (SolanaToken session OR no usable `sub`)), currency dropdown hidden in a SolanaToken context. **Backend (scope corrected):** prefs handlers **prefer the wallet when provided** (`sub`-first ignored the wallet for the dev/mixed operator whose token carries a `sub`), and `SessionService.InsertBet` captures the wallet address before nulling `OperatorPlayer`. Child of umbrella #46, escrow-independent | Shipped |
| `specs/non-custodial-escrow.md` | **Umbrella design** (epic #46) for the non-custodial SolanaToken escrow: per-session PDA vault owned by a new Anchor program, non-spendable resolver authority, on-chain `Tally` split, permissionless timeout refund; decomposed into escrow Phases 0–3 (#54–#59). Design basis for the phase specs. | Design (umbrella) |
| `specs/escrow-phase-0.md` | **Escrow Phase 0** (#54): the new **Rust/Anchor** program in `monkify-backend/program/` — `Config`/`Session`/`Vault`/`BetRecord`/`Tally` PDAs; `initialize_session`/`place_bet`/`resolve`/`claim`/`refund`/`cancel`/`close_session`(+`close_bet`); on-chain tally split, **0.5% commission at resolve** (settings-token mint only, via `Config`), permissionless refund, dust/unclaimed sweep to the Monkify house account; `anchor test` + **devnet** deploy. **No `.NET`/frontend** (Phase 1 = #55, Phase 2 = #56). | Ready |
| `skills/`                   | Agent Skills that power the Monkify task pipeline (see below)                            | Drafted              |


## Skills (the task pipeline)

Reusable Agent Skills live in `skills/`. They are the source of truth, version-controlled here.
Because this repo's `skills/` folder is **not** an auto-discovery path, AGENTS.md installs them into
the Cursor- and Claude-discovered locations (`.cursor/skills/` and `.claude/skills/`) on first use —
see "Skills — ensure installed" in `AGENTS.md`.


| Skill                         | Invoke                  | Purpose                                                                                                                                                                                                                                      |
| ----------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `skills/monkify-refine-tasks` | `/monkify-refine-tasks` | Pick a card from the Trello **monkaify** board and refine it (Monkify-wide, both apps) into a clear, unambiguous, spec-ready task; write it back and move it to **Refined**. Needs the `trello` MCP + Superpowers.                           |
| `skills/monkify-create-spec`  | `/monkify-create-spec`  | From a **Refined** card: ingest card + chat context, explore code, create `feature/<slug>` branches, write `specs/<slug>.md`, update INDEX, move card to **Specs created**. Needs Trello MCP.                                              |
| `skills/monkify-implement-feature` | `/monkify-implement-feature` | From a **Specs created** card: gather session + card + spec context, write an implementation plan (Superpowers **writing-plans**), implement it in the feature worktree(s) with unit + a mandatory happy-path integration test, **commit + push, deploy to the local Docker stack** (`setup_local_environment.bat`; detects a running stack and offers Replace/Combine/Skip, never auto-clobbers), have the user verify the running containers, then move card to **Implemented**. Needs Trello MCP + Superpowers + Docker. |
| `skills/monkify-ship-feature` | `/monkify-ship-feature` | Ship an **Implemented** feature: sync the knowledge base **on the feature branch** (from card + spec + branch code), then merge `feature/<slug>` → `main` in each affected repo (KB included), move card to **Shipped**, and delete the merged branches + worktrees. **Branch + spec read from card Implementation section.** Needs Trello MCP. |


## Feature currently in flight

- _Spec created (in flight):_ **Escrow Phase 0 — non-custodial SolanaToken escrow Anchor program** —
  `feature/escrow-phase-0` (`monkify-backend` + `monkify-meta`; **no frontend**), spec written
  2026-07-14. First child of umbrella epic **#46 refactor solana wallet flow**
  ([SX7avQTT](https://trello.com/c/SX7avQTT)); design `specs/non-custodial-escrow.md`. A new
  **Rust/Anchor** program living in **`monkify-backend/program/`** (not a new repo) that holds each
  paid `SolanaToken` session's pot in a **program-owned PDA vault** (authority = Session PDA, no
  keypair): `Config` (admin-set commission mint/destination) + `Session`/`Vault`/`BetRecord`/per-`(session,choice)`
  `Tally` PDAs; instructions `initialize_session` (freezes `server_seed_hash` + `commission_bps`, bounded
  deadline) / `place_bet` (deposit + tally++, rejects amount-0) / `resolve` (commit-reveal gated,
  **on-chain split** `amount_per_winner = floor((total − commission)/tally)`, **0.5% commission** only when
  `mint == settings-token mint`) / `claim` (winner pull, ATA init-if-needed) / `refund` (permissionless
  after a bounded deadline) / `cancel` / `close_session` (+ permissionless `close_bet`) with dust +
  30-day-unclaimed **sweep to the Monkify house account** and rent reclaim. Non-custodial (the resolver
  authority asserts only the winning choice + reveals the seed, can never move funds). Ships the program +
  `anchor test` suite + a **devnet** deploy only; single devnet upgrade key (mainnet multisig/timelock
  deferred to #59). Backend rewiring = **#55**, frontend = **#56**. Spec: `specs/escrow-phase-0.md`.
  Trello #54 ([YlxonX59](https://trello.com/c/YlxonX59)).
- _Last shipped:_ **Display name per connected wallet for SolanaToken sessions** —
  `feature/wallet-display-name` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-14**. Full-stack fix so SolanaToken display-name/preferences key off the **connected wallet**
  everywhere. **Backend (scope corrected from the original "frontend-only" plan):** the preference
  handlers now **prefer the wallet when provided** (`request.Wallet ?? sub`) — the previous `sub`-first
  resolution ignored the wallet for the **dev operator and any operator offering both Standard +
  SolanaToken sessions**, whose launch token always carries a non-empty `sub`, so the display name was
  saved to the `sub` `OperatorPlayer` while crypto bets (keyed by the wallet) rendered `ANON`; and
  `SessionService.InsertBet` now captures `GetWalletAddress()` **before** nulling `OperatorPlayer` so
  live `BetNotificationEvent`s carry `externalPlayerId` (own live crypto bets render `YOU`).
  **Frontend:** `App.vue` reloads preferences reactively on wallet/session-method change (sending the
  wallet **only** in a SolanaToken context, so a Standard player's stray wallet still writes to their
  `sub`); the customization modal shows a **"Connect wallet to customize"** gate when no wallet is
  connected AND (the session is `SolanaToken` OR there is no usable `sub`), and hides the
  preferred-currency dropdown for SolanaToken. Display-only; no entity/EF/migration/DTO/SignalR change.
  **342 unit + 32 integration tests green** (+2 precedence unit tests, +1 happy-path integration test
  `CryptoPreferences_TokenAlsoHasSub_WalletTakesPrecedenceOverSub`). See `features/wallet-display-name.md`;
  spec `specs/wallet-display-name.md`. Trello #58 ([sohAuZN0](https://trello.com/c/sohAuZN0)).
- _Last shipped:_ **Iframe embedding + embedded crypto (SolanaToken) play (2 stacked cards, one branch)** —
  `feature/iframe-crypto-sessions` (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-14**.
  Two frontend-only features merged to `main` in one stacked merge (the branch contained both #3's
  `feature/iframe-crypto-sessions` (`monkify-front` + `monkify-meta`; **no backend**), **shipped 2026-07-14**.
  Two frontend-only features merged to `main` in one stacked merge (the branch contained both #3's
  `4e53e02` and #41's `9784354`; `feature/iframe-embedding` is a contained ancestor). **#3 "Make the game
  Iframe-able"** ([RGvJZNTW](https://trello.com/c/RGvJZNTW)): a **regulated (Model A)** casino embeds the
  game in an `<iframe>` — `useEmbed` detection (`window.self !== window.top` \| `?embed=1`), a secret-free
  `{source:'monkify',type,payload}` postMessage lifecycle API (`ready`/`session-started`/`bet-placed`/
  `bet-settled` bettors-only/`session-ended`/`resize` hint), and the **core fix**: an embedded 401 /
  expired / invalid token / inactive operator emits `token-expired` + a light "relaunching" state instead
  of dead-ending on `/error` (branch in `mainService.ts` + `useOperator.ts`); app fills the frame
  (`main.css`), permissive `frame-ancestors *` in the committed `nginx.conf` (prod host must match), static
  `public/embed-example.html` + `EMBEDDING.md`. **#41 "Embedded crypto (SolanaToken) sessions"**
  ([PVUH9zJZ](https://trello.com/c/PVUH9zJZ)): paid **`SolanaToken`** (on-chain) bets inside that iframe —
  extensions don't inject cross-origin, so bet-time wallet connect + SPL transfer + signing move to a
  **same-origin popup** on the game host (route `/monkify/wallet-bridge` → `WalletBridgePage.vue`) that
  `postMessage`s the **signature + wallet** back over an internal same-origin channel
  (`source:'monkify-wallet'`, `utils/walletBridge.ts`); `useSession.bet()` acquires the signature in-frame
  (`sendToken`) **or** via the popup (`useEmbeddedWallet.ts`), opened **synchronously** on click (blocked →
  visible affordance); the `GameConsole` in-frame `connected` gate is relaxed when embedded; free (amount 0)
  SolanaToken sessions embed with no wallet/popup; #3's lifecycle payloads become **token-native**
  (`tokenSymbol`, never USD). Standalone play unchanged. See `features/iframe-embedding.md` +
  `features/iframe-crypto-sessions.md`; specs `specs/iframe-embedding.md`, `specs/iframe-crypto-sessions.md`.
- _Last shipped:_ **Per-game general stats (all-time, per session type)** —
  `feature/session-type-stats` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped
  2026-07-10**. Casino-style per-session-type stats via `GET /sessions/{parametersId}/stats` (biggest
  pot, biggest single payout, rounds played, total paid out, hottest-choice for preset types) rendered
  in a lazy **STATS** tab of the `/game` right-side panel (bundled with **CHAT** via `GameSidePanel`).
  **On-demand aggregate + Redis cache (~120s), no new entity/migration** — a documented reversal of the
  card's maintained-counter idea, justified because the panel fetches only on open (no lobby fan-out).
  Only `RewardForWinnersCompleted` rounds count; money from `TransactionLog` Credit rows; free bets
  excluded (free-only rounds still count as played); single currency per type (Standard USD + preferred
  display currency, SolanaToken token-native); token-gated + operator-scoped (vague on
  cross-operator/expired). **325 unit + 29 integration tests green.** Surfaced a pre-existing
  multi-winner reward double-credit race under redelivery (`RewardSessionConsumer`) — tracked
  separately. See `features/session-type-stats.md`; spec `specs/session-type-stats.md`; Trello #43
  ([oxZkifXh](https://trello.com/c/oxZkifXh)).
- _Last shipped:_ **Provably fair (commit-reveal + portable RNG + player verifier)** —
  `feature/provably-fair` (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped 2026-07-10**.
  Replaces the post-lock `Int32` + `System.Random` seed with a **commit-reveal `ServerSeed` committed on
  the first bet** (atomic first-writer-wins; `ServerSeedHash` published during betting, raw seed revealed
  after `Ended`) and a **portable HMAC-SHA256 counter keystream** (rejection sampling, canonical bet
  order `CreatedDate,Id`, labelled fallback) reproducible in any language. Client seed is **Stake-style**
  (persistent, settable/rotatable, localStorage) with a per-bet **`nonce`** — `Bet.Seed =
  HMAC-SHA256(clientSeed, nonce)`, raw client seed never leaving the browser. Reveal-gated
  `GET /sessions/{id}/verification` + `ServerSeedHash` on `ActiveSessionDto`; verification via that
  endpoint + standalone offline `public/verify.html` (**the in-terminal "Verify this round" recompute
  was dropped** — the terminal shows commitment/reveal only). Greenfield migration (drop `Session.Seed`;
  add `ServerSeed`/`ServerSeedHash`, `Bet.Nonce`; widen `Bet.Seed` to 64). **329 unit + 2 provably-fair
  integration tests green.** Bundled a `GameTypeCard` price fix (sessions-list price now mirrors the
  in-game `BET:` label). Out-of-repo `04 Fairness & game rules.pdf` regenerated to the new scheme. See
  `features/provably-fair.md`; spec `specs/provably-fair.md`. Trello #42
  ([XWAeIgIQ](https://trello.com/c/XWAeIgIQ)).
- _Last shipped:_ **Refactor payment models** — `feature/payment-methods-refactor`
  (`monkify-backend` + `monkify-front` + `monkify-meta`), **shipped 2026-07-08**. Removes the
  operator-level `Operator.PaymentModel` (`Regulated`/`Crypto`) and moves payment to a per-session
  **`SessionParameters.PaymentMethod`** (`Standard` = off-chain operator wallet, USD; `SolanaToken` =
  on-chain via a per-operator **registered `OperatorToken`**, token-native amounts). A unified
  `IPaymentMethodService` strategy + resolver dispatches Debit/Credit/Refund by session method
  (availability = wallet-adapter for Standard, ≥1 active token for SolanaToken); admin token
  register/list/deactivate (private key **encrypted at rest** — shipped as **AES-256-GCM keyed from a
  `GeneralSettings.SecretProtection` passphrase**, a deviation from the spec's Data-Protection plan); a
  ledger split (`Bet` & `TransactionLog` drop the Solana signature and carry `ExternalTransactionReference`
  + `PaymentMethod`; new `CryptoTransactionLog` holds on-chain detail); identity per method; display
  currency only for Standard; one dev operator (fixed RFC-4122 GUID
  `11111111-1111-4111-8111-111111111111`) pre-provisioned for both methods, with **all** default session
  types Standard plus **3** SolanaToken defaults. Dev/greenfield migration. **314 unit + 28 integration
  tests green.** See `features/payment-methods.md`; spec `specs/payment-methods-refactor.md`. Trello #38
  ([FH5TMT0T](https://trello.com/c/FH5TMT0T)).
- _Last shipped:_ **Audio overhaul with AI (3-card feature, one shared branch)** —
  `feature/audio-overhaul` (`monkify-front` + `monkify-meta`; **no backend**), 2026-07-07. A cohesive
  AI-generated (Suno) **warm arcade-chiptune** sound identity, all frontend and display-only: idle loop
  on `WaitingBets`, a **one-shot** Game Starting on `SessionStarting` (once per session, running variant
  preloaded here), a **random** of three running loops on `InProgress`; own-bet/button/error SFX;
  **bettors-only** victory/lose on `Ended` (once); a **menu loop on the sessions page** (admin stays
  silent); and a header **mute+volume** control (`localStorage` `monkify.audio`, master gain in
  `helpers.ts`, live re-apply). **9 generated mp3s** (kept `error1.wav`, dropped `success1.wav` + the 4
  old `.wav`s). Shipped as three cards on one branch: **(1/3)** manifest [#17 `9qiDtDAe`], **(2/3)**
  generate [#34 `OuXhyqyU`] (done **manually in the Suno UI** + ffmpeg), **(3/3)** wire [#35 `RvVdpO25`].
  **The 3/2/1 proximity "coin" bleeps were dropped** (Suno couldn't produce them) — near-miss visual
  unchanged, no proximity audio. **Extras beyond spec:** `/`→`/monkify/game` redirect, and a
  **session-connection teardown fix** (`disconnectAllPreviousConnections` enabled) that stops a stale
  session's SignalR events from corrupting the shared session state on switch/rollover. See
  `features/audio-overhaul.md`.
- _Previously shipped:_ **Characters Away (near-miss reveal)** — `feature/characters-away`
  (`monkify-front` + `monkify-backend` + `monkify-meta`), 2026-07-07. While a round runs, a
  bottom-docked terminal row per **own** bet choice shows how close the monkey is to typing it
  (`> N AWAY!`, KMP tail-prefix `away = L − k`): it climbs, **resets to the start and re-climbs after
  a 2s stall**, and on a win **types out the full word**; a `> POT:` line shows the in-game pot. New
  `src/utils/nearMiss.ts` + `src/hooks/useNearMiss.ts`, wired into `useSession.ts` (`ownBetChoices`,
  `feed`/`finish`/`reset`; `lastOwnBetChoice` untouched) + `GameTerminal.vue` dock. Backend:
  `SessionService.RunSession` emits the **fallback-decided** winner as a final `<sessionId>/Terminal`
  batch so the terminal ends with the word "typed" (unit-tested; display-only, outcomes unchanged).
  Trello #30 ([vlarPOaw](https://trello.com/c/vlarPOaw)). See `features/characters-away.md`.
- _In progress:_ **Play page UX fixes** — `feature/play-page-ux-fixes` (`monkify-front` +
  `monkify-meta`), started 2026-07-01; three independent frontend-only changes: (1) hide the wallet
  cluster (Connect Wallet button + USDC balance) in `GameHeader.vue` for **regulated** operators,
  (2) show `> BET: {requiredAmount} {betCurrency}` for **every** session type in `GameTerminal.vue`
  (`betCurrency`: crypto `USDC` / regulated `credits`), and (3) fix `GameMonkey.vue` so the
  received-bet animation caps at 2 and always settles to a resting frame instead of looping. No
  backend change; real-fiat currency for regulated operators is a deferred backend follow-up.
- _Last shipped:_ **Show session pot value** — `feature/session-pot-display` (`monkify-front` +
  `monkify-meta`), 2026-07-07; frontend-only. The `> POT:` line is now shown for **every** session
  type (free-type race, preset race, lottery), not just lottery: `useSession.ts` renames
  `currentLotteryPot` → `currentPot`, seeds it as `Σ bets` (+ `lotteryPot` base only for lottery), and
  accumulates live by removing the `isLottery` gate on the `/Bets` stream; `GameTerminal.vue` renders
  a universal POT line in the pre-run view (lottery block keeps only `> FREQUENCY:`) plus a second POT
  line in the running/ended view so it stays visible during and after the run (AC #8). Reuses
  `formatAmount`; free bets contribute 0; resets/reseeds on rollover; no backend/DB/SignalR change.
  Trello #32 ([0FJquICm](https://trello.com/c/0FJquICm)). See `features/session-pot-display.md`.
- Previously shipped: **Operator page (operator admin portal)** — `feature/operator-admin-page`,
  2026-07-06; operator self-service admin at `/monkify/admin` with admin-scoped RS256 token
  (`X-Admin-Token`, not contract-gated), create/list/deactivate session types (all 3 modes;
  deactivate-or-create, no edit), first-session-on-create event, deactivation refunds waiting
  sessions. Trello #31 ([JZcMz3ma](https://trello.com/c/JZcMz3ma)). See
  `features/operator-admin-page.md`.
- Previously shipped: **Add chat (session chat v2)** — `feature/session-chat`, 2026-07-03; per-session
  chat on `/game` via `ActiveSessionsHub` (`SendChatMessage`, display-name identity, audit DB +
  daily purge, client 200-char/1s limits, no history replay). Trello #4 (`7UnjEOom`). See
  `features/session-chat.md`.
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
  `/monkify/game/sessions`), dev mint returns `{ token }` only + non-expiring, silent auto-mint
  replaced by dev launch buttons gated on `VUE_APP_DEV_LAUNCH`, and a new `Monkify.Tests.Integration`
  Testcontainers suite. Trello #28. See `features/dev-qol-stuff.md`.
  Backlog follow-ups: Monkify commission per bet → https://trello.com/c/BXgxJcwP #26.

## The 60-second mental model

- **Backend** (`monkify-backend`) is a layered .NET 8 solution split into a web **API**
(`Monkify.Api`) and a background worker host (`Monkify.Consumers`). They share `Monkify.Domain`,
`Monkify.Infrastructure`, and `Monkify.Common`.
- A **bet** is placed via `POST /sessions/{id}/bets`. The handler validates the on-chain Solana
payment signature, persists the bet, and publishes a `BetPlacedEvent`.
- **Sessions** progress through a state machine driven by **RabbitMQ events** (via MassTransit)
consumed in `Monkify.Consumers`: `BetPlaced → RunSession → RewardSession`, with
`RefundInvalidBet` for failures.
- The **typer** (`MonkifyTyper`) runs the game: it emits random characters over **SignalR** until
a bet's choice appears; winners are then rewarded via **Solana** SPL token transfers.
- **PostgreSQL** (EF Core / Npgsql) is the store. **Redis** caches active parameters, FX rate tables
  (6h TTL), and backs SignalR. **Hangfire** (on PostgreSQL) schedules lottery sessions.
- **Frontend** (`monkify-front`) is Vue 3 + TypeScript: composables in `src/hooks`, an axios
service layer in `src/services`, and SignalR clients to stream the live typer + bets.
