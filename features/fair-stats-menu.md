# Feature — Fair & Stats to the menu; chat-only side panel

> Status: **shipped.** Authoritative spec: `../specs/fair-stats-menu.md`
> (merged via `feature/fair-stats-menu` on 2026-07-24).

## What it does

**Stats** and **Fair** are now first-class surfaces reached from the header menu, not tabs inside the
`/game` side panel. **Stats** is a dedicated page with a new **deployment-wide global** section plus the
existing per-game-type stats behind a dropdown. **Fair** is a full-screen modal that always teaches
players *how to verify a game* and, on `/game` after a round ends, runs the same current-round checks
the old FAIR tab did. Freed of both, the right-docked arcade cabinet is now **chat only** (its chrome
kept). The only new backend surface is one read: `GET /games/stats`.

## Core rules

- **Header menu = Settings · Stats · Fair · History** (+ dev Admin). `Stats` and `Fair` are new rows,
  rendered only when **not embedded** (`useEmbed().isEmbedded`) — regulated iframes keep today's
  menu (no in-frame stats/fairness). Audio + wallet stay on the header bar. The menu shows wherever
  player preferences load (lobby, `/game`, history, stats).
  ⚠️ **Superseded by #159:** the menu is no longer a list of rows. It is a **3-column icon pad** —
  Home · Practice · Orbit · Stats · Fair · How to play · History · Responsible · Settings — and the
  embed still renders **the same grid** with its two survivors (History + Settings), not an alternate
  layout. What holds from this card is unchanged: Stats and Fair are menu destinations, both hidden
  when embedded, both opening the same surfaces. See `features/icons-and-menu-overhaul.md`.
- **Side panel is chat-only.** `GameSidePanel` keeps its arcade-cabinet chrome (collapse / dock-rail /
  maximize) but the CHAT/STATS/FAIR tab bar is gone — it renders only `GameChatPanel`. Chat identity,
  limits, and lifecycle are unchanged.
- **Mobile segmented control is `GAME | CHAT`** (was `GAME | CHAT·STATS`); the mobile side panel is
  chat-only. Stats and Fair are reached from the mobile header menu.

### Stats page (`/game/stats`, route name `GameStats`)

- Arcade-panel page (mirrors `GameHistoryPage`), inside the app shell; `App.vue` loads player
  context (token + display prefs) for the `GameStats` route like the lobby/game/history.
- **One page-level house-cut badge** at the top (`"{n}% HOUSE CUT"` + trust blurb), from the global
  read's `commissionPercentage`. It is shown **once** for the whole page — not per section.
- **Global section (new).** Fetches `GET /games/stats` on mount and renders ten tiles: biggest pot,
  biggest single payout, total paid out, total volume wagered, rounds played, total bets placed,
  unique players, winners paid, biggest win multiplier (`×n`), most-played game (name + round count).
  Zero completed rounds → "No rounds yet".
- **Per-game section.** A game-type dropdown (from `getGamesTypes`, defaults to the first active type)
  drives `GET /games/{parametersId}/stats` and renders the existing per-type tiles (biggest pot,
  biggest single payout, total paid out, rounds played, hottest choice for preset types). A
  request-sequence guard drops stale dropdown responses.
- Money renders token-native through the shared amount formatter (`{n} USDC`). *(It used to branch on
  a `{ paymentMethod, tokenSymbol }` descriptor: `SolanaToken` token-native, `Standard`
  amounts rendered in the player's preferred display currency; both are gone.)*

### `GET /games/stats` (backend — new)

> ⚠️ **Superseded in part by `windowed-stats-query-performance.md` (2026-08-05).** The endpoint now
> takes an optional `window` (`week` | `month` | `all`, omitted ⇒ `all`), the page has a period control
> driving both sections, and the aggregation moved from C# into Postgres. The rules below still hold
> for the default all-time case.

- **Deployment-wide global stats, all-time BY DEFAULT.** No `parametersId` and no tenant — the endpoint is
  public. Read-only on the stateless **API**.
- **On-demand + Redis-cached** (`CacheHelper.GenerateGlobalStatsCacheSettings`, 120s, **one key per
  window** — it was a single global key before windowing; a miss is now single-flighted per instance),
  aggregated from existing `Game` / `GameBets` / `TransactionLogs` / `GameParameters` rows — **no new
  entity and no migration** (a direct generalization of `GameStatsService`).
- **Which types.** All **active** types. *(There used to be a dominant-group rule: only the
  **dominant** `(PaymentMethod, OperatorTokenId)` group was aggregated (most active types wins,
  deterministic tiebreak) — global is strictly **single-currency**, no cross-currency summing. For the
  production deployment every type was `SolanaToken` (USDC), so there was exactly one group. With one
  configured token there is nothing left to group by.)*
- **Which rounds / money.** Only `RewardForWinnersCompleted` rounds count. Free bets (`Amount == 0`)
  are **excluded from money** (volume, pot, payouts, multiplier) but **counted in participation**
  (total bets placed, unique players). `BiggestPot` is reconstructed per round as `Σ paid Bet.Amount`
  + that type's `LotteryParameters.LotteryPot` base (mirrors game-type-stats; `Game.CarriedExtra` not
  included). Payouts/winners/multiplier come from `Credit` `TransactionLog` rows;
  `BiggestWinMultiplier` is the max `payout ÷ stake` over paid winning bets.
- **Currency descriptor + house cut.** The DTO carries `TokenSymbol`, `TokenDecimals`, and
  `CommissionPercentage` (the configured rate). *(It also carried `PaymentMethod`, and the rate was 0 for
  `Standard`.)* The client formats amounts and shows the badge from it.
- **Non-leaking (vague messaging).** An unknown type resolves to an
  **empty active-type set → zeroed stats**, never a thrown or existence-revealing error. There is no
  vague-error path because there is nothing to reveal.

### Fair modal (`GameFairModal`)

- Full-screen modal + overlay opened from the header `Fair` row (absent when embedded).
- **The "how to verify a game" explainer is always shown** (commit-reveal, HMAC-SHA256 keystream,
  client seed + `Bet.Nonce`, offline `verify.html`) — useful even off `/game`.
- **Live current-round verification** reuses `GameFairPanel` unchanged (`:active="open"` drives its
  lazy fetch): on `/game/play` after the round ends it runs the instant checks + opt-in Web-Worker
  recompute + COPY VERIFICATION JSON exactly as the former FAIR tab; while the round runs it is locked
  ("verification unlocks when this round ends"); a round nobody bet on shows "no bets — nothing to
  verify".
- **Off `/game` (no live round)** `GameFairPanel` shows a "watch a round to verify it" empty state and
  performs **no** verification fetch (`hasLiveGame` guards the panel; its fetch watchers already
  early-return on a null game id).
- The provably-fair algorithm, `GET /games/{id}/verification`, and `public/verify.html` are unchanged.

## Key code touchpoints

**Backend (`monkify-backend`):**
- `SpaceStroker.Api/Controllers/GamesController.cs` — `GET /games/stats` action (before the
  `{gameTypeId:guid}` action, which gained the `:guid` constraint so `stats` binds unambiguously).
- `SpaceStroker.Infrastructure/Handlers/Games/GetGlobalStats/` — request + handler (active types →
  dominant-currency group → DTO; injects `GeneralSettings` for the commission; non-leaking).
- `SpaceStroker.Infrastructure/Services/Games/GlobalStatsService.cs` (+ `IGlobalStatsService`,
  `GlobalStats` result model) — on-demand aggregation + Redis cache.
- `SpaceStroker.Infrastructure/Contracts/Games/GlobalStatsDto.cs` — DTO + `From(stats, commissionPct)`.
- `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` — `GenerateGlobalStatsCacheSettings`.
- DI: `IGlobalStatsService → GlobalStatsService` in `Infrastructure/DependencyInjection.cs`.
- Tests: `SpaceStroker.Tests/UnitTests/Services/GlobalStatsServiceTests.cs`,
  `.../UnitTests/Handlers/GetGlobalStatsHandlerTests.cs`,
  `.../UnitTests/Contracts/GlobalStatsDtoTests.cs`,
  `SpaceStroker.Tests.Integration/GlobalStatsFlowTests.cs` (drives `GET /games/stats` end to end
  against a dedicated operator seeded via the new `IntegrationTestFixture.SeedIsolatedOperatorAsync`).

**Frontend (`monkify-front`):**
- `src/components/GameHeader.vue` — `Stats` + `Fair` menu rows (`v-if="!isEmbedded"`); mounts `GameFairModal`.
- `src/pages/GameStatsPage.vue` — **new** (global section + per-type dropdown; single house-cut badge).
- `src/components/game/GameFairModal.vue` — **new** (overlay + explainer + reused `GameFairPanel`).
- `src/components/game/GameFairPanel.vue` — adds `hasLiveGame` + the off-game "watch a round to verify it" empty state.
- `src/components/game/GameSidePanel.vue` — chat-only (tab bar removed; cabinet chrome kept).
- `src/components/game/GameMobileTabs.vue` — segment relabelled `CHAT·STATS` → `CHAT`.
- `src/services/mainService.ts` — `getGlobalStats()`; `src/types/index.ts` — `GlobalStats`.
- `src/routes.ts` — `GameStats` route; `src/App.vue` — boots operator context for it.
