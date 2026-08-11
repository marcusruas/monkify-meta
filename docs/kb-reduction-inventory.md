# Knowledge-base reduction — classification inventory

> **Temporary review artifact for Trello [#167](https://trello.com/c/Co35YRGt).** Produced by
> phase 1 of `specs/tools-refactor.md` §9. **Nothing has been deleted.** This file is the proposal
> the user approves, amends or rejects before a single `git rm` runs, and it is deleted again
> before the branch merges.

## Summary

| | Files | Size |
| --- | ---: | ---: |
| `features/` today | 65 | 689 KB |
| `specs/` today | 118 | 4460 KB |
| **Total classified** | **183** | **5149 KB** |

| Verdict | Files | Size |
| --- | ---: | ---: |
| `DELETE` | 41 | 1208 KB |
| `MERGE` | 133 | 3641 KB |
| `KEEP` | 9 | 299 KB |

Deletions break down as **3 in `features/`** and **38 in `specs/`**.

⚠️ **`MERGE` does not mean "delete the spec".** For `features/` it means the doc is folded into a
domain doc and its own file goes away. For `specs/` it means the deep-design archive **stays on
disk** and is repointed from the merged `features/` doc — `specs/` is the authoritative design
record, and losing a shipped design to save bytes is a bad trade. The `specs/` savings in this
card therefore come from the `DELETE` column only.

---

## The `DELETE` list (41 files, 1208 KB)

This is the part that is irreversible in-tree. Read it first.

| File | KB | Reason |
| --- | ---: | --- |
| `specs/orbit-ui-overhaul.md` | 81 | Its own header: PARTIALLY SUPERSEDED by orbit-page-restructure.md. |
| `specs/home-hero-trim.md` | 51 | Distilled into features/home-dashboard.md. |
| `specs/operator-admin-page.md` | 51 | Admin portal survives but is token-gated, not operator-scoped; current behaviour lives in features/admin-and-ops.md. |
| `specs/quick-tour-modals.md` | 50 | The feature was deleted from the product. |
| `specs/home-hero-sprite.md` | 49 | Distilled into features/home-dashboard.md. |
| `specs/launch-token-validation.md` | 41 | The X-Launch-Token surface was deleted with operators. |
| `specs/auto-bet-seeder.md` | 40 | Its own header: SUPERSEDED by house-seed-bet.md; the tool was deleted. |
| `specs/orbit-frontend-flag.md` | 40 | Temporary env flag; distilled into orbit-custodial-wallet. |
| `specs/solana-rpc-api-key.md` | 39 | Superseded by frontend-helius-gateway.md. |
| `specs/provably-fair.md` | 37 | Superseded by provably-fair-final.md. |
| `specs/idle-websocket-disconnect.md` | 37 | Superseded in part by unified-websocket-connection.md. |
| `specs/operator-wallet-api.md` | 35 | Model A debit/credit/refund adapters removed with operators. |
| `specs/main-production-operator.md` | 35 | Default-operator seeding gone. |
| `specs/home-orbit-card.md` | 33 | Distilled into features/home-dashboard.md. |
| `specs/orbit-page-restructure.md` | 33 | Its own header: PARTIALLY SUPERSEDED by orbit-new-ui.md. |
| `specs/rebrand-spacestroker.md` | 32 | Rename completed; historical. |
| `specs/solana-connection-rpc.md` | 32 | Transport half explicitly superseded by frontend-helius-gateway.md. |
| `specs/rg-pre-bet-hook.md` | 31 | Per-operator RG hook removed. |
| `specs/dev-qol-stuff.md` | 31 | Dev QOL; named by the card. |
| `specs/sales-technical-documents.md` | 31 | The B2B SaaS product no longer exists. |
| `specs/operator-feature.md` | 30 | Operators removed 2026-07-30. Describes a capability that no longer exists. |
| `specs/rebrand-space-stroker-backend.md` | 29 | Rename completed; historical. |
| `specs/orbit-modal-game-type.md` | 28 | Bug fix distilled into orbit-modal-improvements. |
| `specs/mobile-preset-keypad.md` | 26 | Distilled into features/mobile-layout.md. |
| `specs/rebrand-space-stroker.md` | 25 | Rename completed; historical. |
| `specs/restore-own-bets-on-refresh.md` | 25 | Frontend bug fix. |
| `specs/fair-modal-off-screen.md` | 23 | One-off modal fix. |
| `specs/wallet-balance-refresh.md` | 22 | Frontend bug fix. |
| `specs/operator-player-identity.md` | 21 | OperatorPlayer replaced by the wallet-keyed Player. |
| `specs/audio-overhaul-wiring.md` | 21 | 3 of 3 in the same chain. |
| `specs/fix-pricing.md` | 19 | One-off pricing fix. |
| `specs/bet-choice-casing.md` | 19 | Minor change; its own header says distilled into games.md + glossary.md, no features doc. |
| `specs/audio-overhaul-generation.md` | 17 | 2 of 3 in a superseded chain; shipped reality already differs from the spec. |
| `specs/no-db-logging.md` | 17 | One-off infra cleanup, already applied. |
| `specs/near-miss-dock-bg.md` | 12 | One-off CSS fix. |
| `features/quick-tour-modals.md` | 12 | Its own header: RETIRED, DELETED from the product 2026-08-02. Nothing it describes runs. |
| `specs/main-only-player-entry.md` | 10 | Transitional step on the way to remove-operators. |
| `specs/background-page.md` | 9 | Minor change; distilled into architecture.md. |
| `specs/remove-operators-backfill.md` | 7 | One-off production data fix, already applied. |
| `features/dev-qol-stuff.md` | 5 | Named by the card. Its own header already says "mostly historical" after remove-operators. |
| `features/sales-technical-documents.md` | 4 | Its own header: the product these documents describe no longer exists. |

---

## Proposed `features/` domain set

One doc per domain boundary instead of one per shipped card. `features/` goes from **65 files to
15**.

| Domain doc | Absorbs |
| --- | --- |
| `features/admin-and-ops.md` | `admin-page` |
| `features/embedding.md` | `iframe-crypto-games` · `iframe-embedding` |
| `features/frontend-ux.md` | `app-design-refinement` · `audio-overhaul` · `background-lifecycle-hardening` · `game-chat` · `graphics-settings-controls` · `icons-and-menu-overhaul` · `light-graphics-mode` · `mobile-console-edge-cases` · `mobile-game-design-update` · `mobile-layout` · `mobile-wallet-connect` · `money-changing-animations` · `off-page-game-presentation` · `standardize-scrollbars` |
| `features/games.md` | `characters-away` · `choice-exclusivity` · `free-bet-mode` · `game-pot-display` · `games` · `house-seed-bet` · `lottery-draw-time` · `lottery-pot-stacking` · `typer-pacing-scalability` |
| `features/home-and-onboarding.md` | `home-dashboard` · `how-to-play` · `practice-mode` |
| `features/infra.md` | `api-error-messages` · `frontend-helius-gateway` · `masstransit-postgres-transport` · `solana-connection-rpc` · `terraform-gcp-deploy` |
| `features/legal.md` | `legal-docs-self-exclusion` |
| `features/lp.md` | `lp-final-adjustments` · `lp-redesign` · `lp-space-stroker-relaunch` |
| `features/money.md` | `crypto-commission` · `refund-safeguards` |
| `features/orbit.md` | `orbit-auto-bet-engine` · `orbit-custodial-wallet` · `orbit-feedback-adjustments` · `orbit-modal-improvements` · `orbit-notifications` · `orbit-single-control` · `orbit-ui-overhaul` |
| `features/player.md` | `player-customization-modal` · `player-identity` · `wallet-connect-name-prompt` · `wallet-display-name` |
| `features/provably-fair.md` | `provably-fair` · `provably-fair-final` |
| `features/stats-and-history.md` | `analytics` · `fair-stats-menu` · `game-type-stats` · `games-history` · `windowed-stats-query-performance` |
| `features/token-ops.md` | `coinbase-wallet-funding` · `ghost-wallet-snipe` · `nightfall-dashboard` |

---

## Full classification (all 183 files)

| File | KB | Verdict | Domain | Reason |
| --- | ---: | --- | --- | --- |
| `features/admin-page.md` | 6 | **MERGE** | admin-and-ops | Admin portal still exists (token-gated); the operator half was already reworked away. |
| `features/analytics.md` | 9 | **MERGE** | stats-and-history | GA4 on LP + app; one facet of the measurement domain. |
| `features/api-error-messages.md` | 7 | **MERGE** | infra | Cross-cutting API error surface. |
| `features/app-design-refinement.md` | 8 | **MERGE** | frontend-ux | One pass of the app design system. |
| `features/audio-overhaul.md` | 6 | **MERGE** | frontend-ux | Audio is part of the app experience. |
| `features/background-lifecycle-hardening.md` | 13 | **MERGE** | frontend-ux | Background/display lifecycle. |
| `features/characters-away.md` | 7 | **MERGE** | games | Near-miss reveal is game-loop UI. |
| `features/choice-exclusivity.md` | 6 | **MERGE** | games | Bet-choice rule. |
| `features/coinbase-wallet-funding.md` | 12 | **MERGE** | token-ops | One of the three ghost-wallet lifecycle tools. |
| `features/crypto-commission.md` | 9 | **MERGE** | money | House cut on winnings. |
| `features/dev-qol-stuff.md` | 5 | **DELETE** | — | Named by the card. Its own header already says "mostly historical" after remove-operators. |
| `features/fair-stats-menu.md` | 9 | **MERGE** | stats-and-history | Where Fair/Stats live in the UI. |
| `features/free-bet-mode.md` | 4 | **MERGE** | games | Dev/QA bet path through the game loop. |
| `features/frontend-helius-gateway.md` | 10 | **MERGE** | infra | The current RPC transport. |
| `features/game-chat.md` | 4 | **MERGE** | frontend-ux | Per-game chat panel. |
| `features/game-pot-display.md` | 3 | **MERGE** | games | Pot line in the terminal. |
| `features/game-type-stats.md` | 7 | **MERGE** | stats-and-history | Per-type stats endpoint. |
| `features/games-history.md` | 8 | **MERGE** | stats-and-history | History page. |
| `features/games.md` | 27 | **KEEP** | games | Already the core-loop domain doc; becomes the merge target. |
| `features/ghost-wallet-snipe.md` | 12 | **MERGE** | token-ops | The buy half of the pool lifecycle. |
| `features/graphics-settings-controls.md` | 10 | **MERGE** | frontend-ux | Player graphics controls. |
| `features/home-dashboard.md` | 31 | **KEEP** | home-and-onboarding | Large and current; becomes the merge target for the entry-page domain. |
| `features/house-seed-bet.md` | 10 | **MERGE** | games | Every round opens with one house bet. |
| `features/how-to-play.md` | 18 | **MERGE** | home-and-onboarding | Guide page. |
| `features/icons-and-menu-overhaul.md` | 16 | **MERGE** | frontend-ux | Chrome/menu pass. |
| `features/iframe-crypto-games.md` | 5 | **MERGE** | embedding | Paid bets inside the iframe. |
| `features/iframe-embedding.md` | 6 | **MERGE** | embedding | The embed contract. |
| `features/legal-docs-self-exclusion.md` | 15 | **KEEP** | legal | Compliance surface (self-exclusion, legal pages) — standalone on purpose. |
| `features/light-graphics-mode.md` | 11 | **MERGE** | frontend-ux | Automatic light mode. |
| `features/lottery-draw-time.md` | 7 | **MERGE** | games | Lottery scheduling. |
| `features/lottery-pot-stacking.md` | 6 | **MERGE** | games | Jackpot carry. |
| `features/lp-final-adjustments.md` | 4 | **MERGE** | lp | LP pass 3 of 3. |
| `features/lp-redesign.md` | 5 | **MERGE** | lp | LP pass 2 of 3. |
| `features/lp-space-stroker-relaunch.md` | 3 | **MERGE** | lp | LP pass 1 of 3. |
| `features/masstransit-postgres-transport.md` | 5 | **MERGE** | infra | Messaging transport. |
| `features/mobile-console-edge-cases.md` | 7 | **MERGE** | frontend-ux | Mobile console fixes. |
| `features/mobile-game-design-update.md` | 9 | **MERGE** | frontend-ux | Mobile game screen. |
| `features/mobile-layout.md` | 8 | **MERGE** | frontend-ux | Mobile lobby + game layout. |
| `features/mobile-wallet-connect.md` | 10 | **MERGE** | frontend-ux | Mobile wallet deeplink. |
| `features/money-changing-animations.md` | 18 | **MERGE** | frontend-ux | Balance animations. |
| `features/nightfall-dashboard.md` | 14 | **MERGE** | token-ops | The sell half of the pool lifecycle. |
| `features/off-page-game-presentation.md` | 8 | **MERGE** | frontend-ux | Presentation gate. |
| `features/orbit-auto-bet-engine.md` | 19 | **MERGE** | orbit | Orbit 2/3. |
| `features/orbit-custodial-wallet.md` | 20 | **MERGE** | orbit | Orbit 1/3. |
| `features/orbit-feedback-adjustments.md` | 15 | **MERGE** | orbit | Orbit follow-up. |
| `features/orbit-modal-improvements.md` | 16 | **MERGE** | orbit | Orbit follow-up. |
| `features/orbit-notifications.md` | 25 | **MERGE** | orbit | Orbit 3/3. |
| `features/orbit-single-control.md` | 10 | **MERGE** | orbit | Orbit follow-up. |
| `features/orbit-ui-overhaul.md` | 15 | **MERGE** | orbit | Orbit follow-up. |
| `features/player-customization-modal.md` | 5 | **MERGE** | player | Display name / preferences. |
| `features/player-identity.md` | 3 | **MERGE** | player | The wallet is the player. |
| `features/practice-mode.md` | 23 | **MERGE** | home-and-onboarding | Tutorial / practice round. |
| `features/provably-fair-final.md` | 9 | **MERGE** | provably-fair | In-app verification. |
| `features/provably-fair.md` | 7 | **MERGE** | provably-fair | Commit-reveal scheme. |
| `features/quick-tour-modals.md` | 12 | **DELETE** | — | Its own header: RETIRED, DELETED from the product 2026-08-02. Nothing it describes runs. |
| `features/refund-safeguards.md` | 6 | **MERGE** | money | Refund lifecycle. |
| `features/remove-operators.md` | 9 | **KEEP** | — | The ONLY doc explaining today operator-free architecture. Never delete. |
| `features/sales-technical-documents.md` | 4 | **DELETE** | — | Its own header: the product these documents describe no longer exists. |
| `features/solana-connection-rpc.md` | 7 | **MERGE** | infra | Transport half superseded by Helius; the residue belongs in infra. |
| `features/standardize-scrollbars.md` | 3 | **MERGE** | frontend-ux | CSS-only pass. |
| `features/terraform-gcp-deploy.md` | 29 | **MERGE** | infra | The GCP stack. |
| `features/typer-pacing-scalability.md` | 2 | **MERGE** | games | Typer wall-clock pacing. |
| `features/wallet-connect-name-prompt.md` | 6 | **MERGE** | player | Name prompt on connect. |
| `features/wallet-display-name.md` | 6 | **MERGE** | player | Per-wallet display name. |
| `features/windowed-stats-query-performance.md` | 8 | **MERGE** | stats-and-history | Stats windowing + indexes. |
| `specs/analytics.md` | 40 | **MERGE** | stats-and-history | Deep-design archive for a live behaviour; distil into features/stats-and-history.md. |
| `specs/api-error-messages.md` | 40 | **MERGE** | infra | Deep-design archive for a live behaviour; distil into features/infra.md. |
| `specs/app-design-refinement.md` | 41 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/app-home-changes.md` | 33 | **KEEP** | — | NOT on main — rides release/ui-ux-changes. In-flight design. |
| `specs/audio-overhaul-generation.md` | 17 | **DELETE** | — | 2 of 3 in a superseded chain; shipped reality already differs from the spec. |
| `specs/audio-overhaul-wiring.md` | 21 | **DELETE** | — | 3 of 3 in the same chain. |
| `specs/audio-overhaul.md` | 42 | **MERGE** | frontend-ux | Keep the sound manifest / contract only. |
| `specs/auto-bet-seeder.md` | 40 | **DELETE** | — | Its own header: SUPERSEDED by house-seed-bet.md; the tool was deleted. |
| `specs/background-lifecycle-hardening.md` | 45 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/background-page.md` | 9 | **DELETE** | — | Minor change; distilled into architecture.md. |
| `specs/bet-choice-casing.md` | 19 | **DELETE** | — | Minor change; its own header says distilled into games.md + glossary.md, no features doc. |
| `specs/characters-away.md` | 28 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/choice-exclusivity.md` | 24 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/coinbase-wallet-funding.md` | 48 | **MERGE** | token-ops | Deep-design archive for a live behaviour; distil into features/token-ops.md. |
| `specs/commission-rate-1-percent.md` | 30 | **MERGE** | money | The live 1% rate. |
| `specs/control-sizing-system.md` | 49 | **MERGE** | frontend-ux | Reference system; distilled into conventions/frontend.md. |
| `specs/crypto-commission.md` | 22 | **MERGE** | money | Commission design. |
| `specs/dev-qol-stuff.md` | 31 | **DELETE** | — | Dev QOL; named by the card. |
| `specs/entry-path-recovery.md` | 40 | **MERGE** | infra | Carries a live NOT-yet-applied Cloud Armor caveat — the caveat must survive. |
| `specs/fair-modal-off-screen.md` | 23 | **DELETE** | — | One-off modal fix. |
| `specs/fair-stats-menu.md` | 34 | **MERGE** | stats-and-history | Deep-design archive for a live behaviour; distil into features/stats-and-history.md. |
| `specs/fix-bets-logic.md` | 34 | **MERGE** | games | Per-round bet limits — a live rule. |
| `specs/fix-pricing.md` | 19 | **DELETE** | — | One-off pricing fix. |
| `specs/free-bet-mode.md` | 22 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/frontend-helius-gateway.md` | 64 | **MERGE** | infra | Deep-design archive for a live behaviour; distil into features/infra.md. |
| `specs/game-chat.md` | 27 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/game-pot-display.md` | 17 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/game-type-stats.md` | 31 | **MERGE** | stats-and-history | Deep-design archive for a live behaviour; distil into features/stats-and-history.md. |
| `specs/games-history.md` | 30 | **MERGE** | stats-and-history | Deep-design archive for a live behaviour; distil into features/stats-and-history.md. |
| `specs/gcp-dashboards.md` | 36 | **MERGE** | infra | Observability. |
| `specs/ghost-wallet-snipe.md` | 54 | **MERGE** | token-ops | Deep-design archive for a live behaviour; distil into features/token-ops.md. |
| `specs/graphics-settings-controls.md` | 36 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/home-dashboard.md` | 144 | **MERGE** | home-and-onboarding | Deep-design archive for a live behaviour; distil into features/home-and-onboarding.md. |
| `specs/home-hero-sprite.md` | 49 | **DELETE** | — | Distilled into features/home-dashboard.md. |
| `specs/home-hero-trim.md` | 51 | **DELETE** | — | Distilled into features/home-dashboard.md. |
| `specs/home-orbit-card.md` | 33 | **DELETE** | — | Distilled into features/home-dashboard.md. |
| `specs/house-seed-bet.md` | 47 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/how-to-play-changes.md` | 38 | **KEEP** | — | NOT on main — rides release/ui-ux-changes. In-flight design. |
| `specs/how-to-play.md` | 80 | **MERGE** | home-and-onboarding | Deep-design archive for a live behaviour; distil into features/home-and-onboarding.md. |
| `specs/icons-and-menu-overhaul.md` | 67 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/idle-websocket-disconnect.md` | 37 | **DELETE** | — | Superseded in part by unified-websocket-connection.md. |
| `specs/iframe-crypto-games.md` | 35 | **MERGE** | embedding | Deep-design archive for a live behaviour; distil into features/embedding.md. |
| `specs/iframe-embedding.md` | 32 | **MERGE** | embedding | Deep-design archive for a live behaviour; distil into features/embedding.md. |
| `specs/launch-token-validation.md` | 41 | **DELETE** | — | The X-Launch-Token surface was deleted with operators. |
| `specs/legal-docs-self-exclusion.md` | 40 | **MERGE** | legal | Deep-design archive for a live behaviour; distil into features/legal.md. |
| `specs/light-graphics-mode.md` | 30 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/lottery-draw-time.md` | 38 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/lottery-pot-stacking.md` | 35 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/lp-final-adjustments.md` | 27 | **MERGE** | lp | Deep-design archive for a live behaviour; distil into features/lp.md. |
| `specs/lp-redesign.md` | 34 | **MERGE** | lp | Deep-design archive for a live behaviour; distil into features/lp.md. |
| `specs/lp-space-stroker-relaunch.md` | 23 | **MERGE** | lp | Deep-design archive for a live behaviour; distil into features/lp.md. |
| `specs/main-only-player-entry.md` | 10 | **DELETE** | — | Transitional step on the way to remove-operators. |
| `specs/main-production-operator.md` | 35 | **DELETE** | — | Default-operator seeding gone. |
| `specs/masstransit-postgres-transport.md` | 29 | **MERGE** | infra | Deep-design archive for a live behaviour; distil into features/infra.md. |
| `specs/mobile-console-edge-cases.md` | 19 | **MERGE** | frontend-ux | Mobile fixes. |
| `specs/mobile-game-design-update.md` | 48 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/mobile-layout.md` | 29 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/mobile-preset-keypad.md` | 26 | **DELETE** | — | Distilled into features/mobile-layout.md. |
| `specs/mobile-wallet-connect.md` | 48 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/modal-panel-design-system.md` | 56 | **MERGE** | frontend-ux | Design system. |
| `specs/money-changing-animations.md` | 53 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/near-miss-dock-bg.md` | 12 | **DELETE** | — | One-off CSS fix. |
| `specs/nightfall-dashboard.md` | 68 | **MERGE** | token-ops | Deep-design archive for a live behaviour; distil into features/token-ops.md. |
| `specs/no-db-logging.md` | 17 | **DELETE** | — | One-off infra cleanup, already applied. |
| `specs/off-page-game-presentation.md` | 25 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/operator-admin-page.md` | 51 | **DELETE** | — | Admin portal survives but is token-gated, not operator-scoped; current behaviour lives in features/admin-and-ops.md. |
| `specs/operator-feature.md` | 30 | **DELETE** | — | Operators removed 2026-07-30. Describes a capability that no longer exists. |
| `specs/operator-player-identity.md` | 21 | **DELETE** | — | OperatorPlayer replaced by the wallet-keyed Player. |
| `specs/operator-wallet-api.md` | 35 | **DELETE** | — | Model A debit/credit/refund adapters removed with operators. |
| `specs/orbit-auto-bet-engine.md` | 78 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-custodial-wallet.md` | 76 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-feedback-adjustments.md` | 53 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-frontend-flag.md` | 40 | **DELETE** | — | Temporary env flag; distilled into orbit-custodial-wallet. |
| `specs/orbit-modal-game-type.md` | 28 | **DELETE** | — | Bug fix distilled into orbit-modal-improvements. |
| `specs/orbit-modal-improvements.md` | 49 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-new-ui.md` | 30 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-notifications.md` | 61 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-page-restructure.md` | 33 | **DELETE** | — | Its own header: PARTIALLY SUPERSEDED by orbit-new-ui.md. |
| `specs/orbit-single-control.md` | 25 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-stop-loss-trigger.md` | 42 | **MERGE** | orbit | Deep-design archive for a live behaviour; distil into features/orbit.md. |
| `specs/orbit-ui-overhaul.md` | 81 | **DELETE** | — | Its own header: PARTIALLY SUPERSEDED by orbit-page-restructure.md. |
| `specs/payment-methods-refactor.md` | 55 | **MERGE** | money | SolanaToken + registered tokens survive; the Standard half is dead. |
| `specs/player-customization-modal.md` | 36 | **MERGE** | player | Deep-design archive for a live behaviour; distil into features/player.md. |
| `specs/player-lockouts.md` | 33 | **MERGE** | infra | Terraform not yet applied — live caveat. |
| `specs/practice-mode-changes.md` | 47 | **KEEP** | — | NOT on main — rides release/ui-ux-changes. In-flight design. |
| `specs/practice-mode.md` | 41 | **MERGE** | home-and-onboarding | Deep-design archive for a live behaviour; distil into features/home-and-onboarding.md. |
| `specs/promotion-channels.md` | 30 | **MERGE** | marketing | Marketing deliverable. |
| `specs/provably-fair-final.md` | 39 | **MERGE** | provably-fair | Deep-design archive for a live behaviour; distil into features/provably-fair.md. |
| `specs/provably-fair.md` | 37 | **DELETE** | — | Superseded by provably-fair-final.md. |
| `specs/quick-tour-modals.md` | 50 | **DELETE** | — | The feature was deleted from the product. |
| `specs/rate-limiter-keying.md` | 45 | **MERGE** | infra | Live security rule; conventions/backend.md depends on it. |
| `specs/rebrand-space-stroker-backend.md` | 29 | **DELETE** | — | Rename completed; historical. |
| `specs/rebrand-space-stroker.md` | 25 | **DELETE** | — | Rename completed; historical. |
| `specs/rebrand-spacestroker.md` | 32 | **DELETE** | — | Rename completed; historical. |
| `specs/redesign-nice-win-modal.md` | 50 | **MERGE** | frontend-ux | Win modal. |
| `specs/refund-safeguards.md` | 30 | **MERGE** | money | Deep-design archive for a live behaviour; distil into features/money.md. |
| `specs/remove-operators-backfill.md` | 7 | **DELETE** | — | One-off production data fix, already applied. |
| `specs/remove-operators.md` | 56 | **KEEP** | — | The design record for today architecture. Pairs with features/remove-operators.md. |
| `specs/restore-own-bets-on-refresh.md` | 25 | **DELETE** | — | Frontend bug fix. |
| `specs/rg-pre-bet-hook.md` | 31 | **DELETE** | — | Per-operator RG hook removed. |
| `specs/robots-txt.md` | 36 | **MERGE** | infra | X-Robots-Tag header NOT yet applied — live caveat. |
| `specs/sales-technical-documents.md` | 31 | **DELETE** | — | The B2B SaaS product no longer exists. |
| `specs/solana-connection-rpc.md` | 32 | **DELETE** | — | Transport half explicitly superseded by frontend-helius-gateway.md. |
| `specs/solana-rpc-api-key.md` | 39 | **DELETE** | — | Superseded by frontend-helius-gateway.md. |
| `specs/standardize-scrollbars.md` | 13 | **MERGE** | frontend-ux | Deep-design archive for a live behaviour; distil into features/frontend-ux.md. |
| `specs/static-site-cache-control.md` | 39 | **MERGE** | infra | Shipped but never applied — live caveat. |
| `specs/static-site-status-codes.md` | 26 | **MERGE** | infra | Shipped but never applied — live caveat. |
| `specs/teardown-resume.md` | 33 | **MERGE** | infra | Teardown semantics still current. |
| `specs/telegram-marketing-campaign.md` | 40 | **MERGE** | marketing | Marketing deliverable. |
| `specs/terraform-development-deploy.md` | 40 | **MERGE** | infra | Deep-design archive for a live behaviour; distil into features/infra.md. |
| `specs/terraform-gcp-deploy.md` | 42 | **MERGE** | infra | Deep-design archive for a live behaviour; distil into features/infra.md. |
| `specs/tools-refactor.md` | 39 | **KEEP** | — | This card own spec. |
| `specs/twitter-marketing-campaign.md` | 26 | **MERGE** | marketing | Marketing deliverable. |
| `specs/typer-pacing-scalability.md` | 21 | **MERGE** | games | Deep-design archive for a live behaviour; distil into features/games.md. |
| `specs/unified-websocket-connection.md` | 35 | **MERGE** | infra | The current websocket model. |
| `specs/wallet-balance-refresh.md` | 22 | **DELETE** | — | Frontend bug fix. |
| `specs/wallet-display-name.md` | 26 | **MERGE** | player | Deep-design archive for a live behaviour; distil into features/player.md. |
| `specs/windowed-stats-query-performance.md` | 33 | **MERGE** | stats-and-history | Deep-design archive for a live behaviour; distil into features/stats-and-history.md. |
