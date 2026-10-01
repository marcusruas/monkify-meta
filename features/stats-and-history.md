# Domain — Stats, history and analytics

> **What this covers.** Everything that reports on rounds that already happened: the per-type and
> deployment-wide **Stats** page, the **Games History** page, the query-layer work that made both cheap,
> and the GA4 **analytics** that measure the funnel. All read-only, all on the stateless API.
>
> Deep design: [`../specs/game-type-stats.md`](../specs/game-type-stats.md) ·
> [`../specs/games-history.md`](../specs/games-history.md) ·
> [`../specs/fair-stats-menu.md`](../specs/fair-stats-menu.md) ·
> [`../specs/windowed-stats-query-performance.md`](../specs/windowed-stats-query-performance.md) ·
> [`../specs/analytics.md`](../specs/analytics.md).

## Money rules shared by every read here

- **Only `RewardForWinnersCompleted` rounds count.** Refunded / under-subscribed
  (`NotEnoughPlayersToStart`), `ErrorWhenProcessingRewards` and `GameEndedAbruptely` are excluded.
- **Free bets (`Amount == 0`) are excluded from every money figure** — pot, payout, total-paid,
  hottest-choice tally — but **counted in participation** (a round of only free bets still increments
  rounds played, and history's `BetCount` includes them).
- **Space Jackpot holder tickets follow the same split** (#196,
  [`space-jackpot-refactor.md`](space-jackpot-refactor.md)). Holder **credits** — real SOL, but the
  house's own money given away — are excluded from global and per-type `TotalPaidOut`, winners-paid and
  `BiggestSinglePayout`, and from a player's won / net / biggest win; a holder round never counts for
  `BiggestPot`. **Participation still counts them**: bets placed, rounds played and unique players
  include holder entries, like free bets. ⚠️ The exclusion is on the bet's `EntryKind == Holder`, not on
  `Amount == 0` — the credit row is a positive amount.
- **Money source:** `TotalPaidOut` / `BiggestSinglePayout` come from `TransactionLog` rows of type
  `Credit`. **`BiggestPot` is reconstructed per round** as `Σ paid Bet.Amount` (+
  `LotteryParameters.LotteryPot` + `Game.CarriedExtra` for lotteries) — the one persisted pot is a
  holder round's `Game.HouseFundedPot`, which the stats ignore and history shows.
- **Single currency.** Every game settles in the one configured token, so the DTO carries that token's
  symbol/decimals and the client renders token-native through the shared `useAmountFormatter`.
- **Vague errors.** An unknown or deactivated type collapses to the same vague `ParameterNotFound` —
  existence and status are never revealed.
- **No new table, no migration.** Everything is aggregated on demand from existing `Game` / `Bet` /
  `TransactionLog` / `Player` rows. ⚠️ This is a **deliberate, documented reversal** of the original
  maintained-counter idea.

## 1. Stats

**`GET /games/{parametersId}/stats`** (per type) and **`GET /games/stats`** (deployment-wide), both
public, both cached in Redis (~120 s per type).

- **Per-type tiles:** biggest pot, biggest single payout, rounds played, total paid out, and — for
  **preset types only** — the hottest choice (most paid bets, ordinal tiebreak; free-type races return
  none).
- **Global tiles (ten):** biggest pot, biggest single payout, total paid out, total volume wagered,
  rounds played, total bets placed, unique players, winners paid, biggest win multiplier (`×n`), and
  most-played game.
- ⚠️ **Type recreation resets stats.** The admin portal has no in-place edit (deactivate then create →
  a **new** `GameParameters` id), so a recreated type starts fresh.
- **Empty state:** zero completed rounds → "No rounds yet", never a row of zeros.

### Windowing

- Both endpoints take an optional **`window`** of `week` | `month` | `all`.
- ⚠️ **`SettlementCutoverUtc` is a FLOOR under whatever window was asked for.** Totals sum a bare
  numeric column that carries no currency, so a window reaching back past the day this deployment
  stopped settling in the previous asset would add those figures to SOL ones and report a number in no
  currency at all. The effective bound is the LATER of the two (`StatsWindowExtensions.EffectiveCutoff`),
  and it applies to global, per-type **and** per-player totals. Null cutover ⇒ no extra bound, which is
  what every deployment that never switched carries. See [`sol-settlement.md`](sol-settlement.md).
- ⚠️ **Only the SUMMING stops.** Games history, the recent-rounds feed and provably-fair verification
  still list and verify every pre-cutover round — nothing is relabelled, hidden or deleted.
- ⚠️ **Windows are ROLLING, not calendar** — `week` = last 7 days, `month` = last 30. There is **no
  timezone or week-start decision anywhere in the system**, and a window is always full size, so tiles
  never collapse to near-zero at the start of a month.
- **Omitting the param means `all`**, and that response is byte-identical in shape to what shipped
  before — an existing caller is unaffected.
- ⚠️ **An unrecognised value resolves to `all` with HTTP 200, never a 400** — mirroring games-history,
  where an out-of-range page returns an empty page rather than an error.
- ⚠️ **Both DTOs echo the window that was APPLIED.** Clients label tiles from that field, **not** from
  what they requested — otherwise a coerced value would caption all-time numbers "last 7 days" with
  nothing to catch it.
- The Stats page **defaults to All time**, and **one page-level control drives both sections**, so the
  two halves can never describe different periods.
- Hottest choice is hottest-**in-window**.

### How the aggregates are computed

⚠️ **Every figure is a bounded aggregate computed by Postgres.** Both services used to load *every*
completed round ever, then every bet of those rounds, then every credit log, and aggregate in C# — an
unbounded materialisation that grew with the game's whole history. Each read now returns **at most one
row per active game type**, never one per round or per bet.

**Adding a stat means adding a bounded aggregate, not another `.ToListAsync()`.**

⚠️ **The window filter must not use `COALESCE`** — `Game.EndDate` is nullable even on completed rounds,
and a `COALESCE` in the predicate is not sargable, so it would defeat the covering indexes
(`AddQueryCoveringIndexes`).

## 2. Games history

**`GET /games/{parametersId}/history?pageNumber&pageSize&mineOnly&wallet`** → `PaginatedList<GameHistoryDto>`,
newest first, public. Rendered at `/game/history`.

- **One game type at a time** via a dropdown — there is no cross-type "all rounds" view.
- **Paginated, lazy.** `pageSize` clamped to `[1, 30]`, `pageNumber` to `>= 1`. An out-of-range page
  returns an **empty page, not an error**. Exactly one page is fetched per view/action, and a
  request-sequence guard drops stale/overlapping responses.
- **Row fields:** ended date (`Game.EndDate`, fallback `CreatedDate`), type name, winning choice, pot,
  **bet count** (free included), winner count, and the **first 12 chars** of `ServerSeedHash`. ⚠️ The
  full typed character stream is **never** returned or shown. On a **holder round** (Space Jackpot) the
  pot is `Game.HouseFundedPot` — not stakes plus lottery base — and the winner count includes holder
  tickets (`GameHistoryService.BuildRow`), so the round reads as a real paid draw.
- **Own-bet columns (nullable):** `MyChoice`, `MyStake`, `MyWon`, `MyPayout` (`Σ` of the bet's `Credit`
  rows; 0 when lost, and 0 for a free-bet win). Null on rounds the caller didn't bet in. Multiple bets
  in one round → the winning bet is surfaced, else the first by `CreatedDate` (one row per round).
- **"Mine" identity is the connected `wallet`** → `Player.ExternalPlayerId` → `Bet.PlayerId` (`Bet`
  carries no raw wallet). `mineOnly` with no identifiable player returns an empty page while the public
  list still loads.
- ⚠️ **Caching is split.** The **public** page is cached 5 min per `(parametersId, pageNumber,
  pageSize)` with no invalidation (completed rounds are immutable); the caller's **own-bet columns are
  joined live** on every request and never cached under another player's key — **the service clones
  cached rows before stamping own columns**, so it never poisons a shared entry. Accepted trade: a
  just-completed round can lag on page 1 by up to the TTL.
- ⚠️ **An out-of-range page is never cached.** `pageNumber` has no upper bound on a public endpoint and
  it is part of the key, so caching those let a caller iterating `pageNumber` **mint unlimited 5-minute
  keys**, each holding a page of rows. Page 1 is always cached even when empty. The page is deliberately
  **not** clamped to the last real page — that would turn the documented empty-page response into a
  last-page response, silently changing a shipped contract.
- **Pagination is `OFFSET`-based and deep pages are O(offset)** — ~45 ms at 1M rows, bounded by the real
  page count and absorbed by the cache. Keyset was considered and **deferred**: it needs a cursor, and
  this endpoint is page-number addressed.
- **Verify is link-out only.** The per-row button fetches the round's payload from
  `GET /games/{id}/verification`, copies + downloads the JSON, and points at `public/verify.html`. There
  is **no in-app replay on the history page** — see [`provably-fair.md`](provably-fair.md).

## 3. Where these live in the UI

- ⚠️ **Neither is a side-panel tab any more.** `GameSidePanel` is **chat-only** (its arcade-cabinet
  chrome — collapse / dock-rail / maximize — kept). Stats and Fair became header-menu destinations; the
  mobile segmented control is `GAME | CHAT`.
- **Stats** is a dedicated page (`/game/stats`, route `GameStats`) with **one page-level house-cut
  badge** at the top from the global read's `commissionPercentage` — shown **once for the whole page**,
  not per section. See [`money.md`](money.md).
- **Both are hidden when embedded** (`useEmbed().isEmbedded`) — see [`embedding.md`](embedding.md).
- The header menu is a **3-column icon pad**, not a list of rows; the embed renders **the same grid**
  with its two survivors (History + Settings), not an alternate layout. See
  [`frontend-ux.md`](frontend-ux.md).

## 3b. Leaderboards (a separate read, same rules)

The ranked boards added by #182 live in [`leaderboards.md`](leaderboards.md), not here — but they
reuse **this document's money predicate verbatim** (completed rounds only, free bets excluded,
`SettlementCutoverUtc` as a floor) and the same "no new table, bounded Postgres aggregate" decision.
Two differences worth knowing before you touch either:

- ⚠️ Leaderboard windows have a **fallback** these reads do not: a weekly board with nothing in the
  last 7 days shows the most recent week that had results rather than rendering empty.
- ⚠️ Their cache is keyed on the **filter only**, with the caller's own row joined live and the cached
  rows cloned before stamping — the same discipline games history uses for its own-bet columns.

A figure that disagrees between a leaderboard and this page for the same player is a defect in one of
them; they are meant to reconcile.

## 4. Analytics (GA4)

Both public properties — the LP (`www.spacestroker.com`) and the app (`app.spacestroker.com`) — report
to **one GA4 property**. ⚠️ Because they share the registrable domain `spacestroker.com`, GA4 keeps a
**single session across the LP→app hop**, so a bet is attributed to the campaign that brought the
visitor rather than to "referral / www.spacestroker.com".

- **Enablement is build-time:** `VUE_APP_GA_MEASUREMENT_ID` (app, Vue CLI) and
  `PUBLIC_GA_MEASUREMENT_ID` (LP, Astro/Vite), both carrying the **same** id. ⚠️ **An empty or unset id
  means analytics does not exist** — no script request, no cookie, no consent banner, no console output.
  That is the normal state for local dev, docker-compose and the development GCP environment, so **only
  production reports**. Both ids live in the `frontBuild` block of `deploy/secrets.<env>.json`; only
  `secrets.example.json` is tracked and it carries empty placeholders.
- **Consent Mode v2.** `gtag` boots with `analytics_storage: 'denied'` **before** the tag loads, so the
  first hit can never write a cookie. A banner offers **Accept** and **Reject** with equal prominence;
  neither answer is re-prompted. A denied visitor still produces cookieless pings, so traffic volume and
  source survive.
- ⚠️ **The answer is stored in an `ss_consent` cookie** (180 days, `SameSite=Lax`, `Secure` on https)
  scoped to **`.spacestroker.com`** — *not* `localStorage`, which `www.` and `app.` would not share, so a
  choice made on the LP is honoured by the app. On any host that is **not** `*.spacestroker.com` the
  `domain` attribute is **omitted**, because a domain the browser rejects would silently write no cookie
  and re-prompt forever.
- **Measured:** pageviews / referrer / `utm_*` / coarse geo / device automatically, plus
  `cta_play_click` (LP, with which CTA), `wallet_connect_attempt` / `_success` / `_failure` (with
  `wallet_name` and `connect_path`; failures carry the error **name** only), `bet_placed` (a count:
  `game_type`, `payment_method`, `is_free`) and `token_click` (a **`source`** label and nothing else —
  no wallet, no amount, no mint).
- **`feedbacker_click`** (LP, a **`source`** label — `lp_rules` today), fired by the Feedbacker
  section's `Full rules →` link. ⚠️ **Deliberately NOT `cta_play_click`, and the LP's listener needed a
  change to keep it out.** That delegated listener fires for **any** clicked link whose href starts with
  `GAME_URL`, defaulting to `cta: 'unknown'`; `Full rules →` is the site's **first link to an app
  SUBPAGE** rather than the front door, so it would otherwise have landed in the play-CTA series
  indistinguishable from a genuine unlabelled play button, permanently inflating it with clicks that
  never reached the game. A link carrying `data-track-event` now fires that event with its
  `data-track-source` and returns; everything else behaves exactly as before. Same reasoning as
  `token_click` below. See [`feedbacker.md`](feedbacker.md).
- ⚠️ **`token_click` is deliberately separate from `home_link_click`.** The latter answers *"which
  surface do players reach for from the front door"*; the former answers *"how many people want to know
  about the coin"*, wherever it is shown. Its five sources (`home_buy`, `home_read_more`, `menu`,
  `guide_buy`, `guide_supply`) read as a funnel — front-door card → guide → chart — so folding them would
  make the front-door card look like it converts at a rate it does not. See
  [`token-launch.md`](token-launch.md).
- ⚠️ **No money and no player identity is ever sent** — `bet_placed` is a count, never an amount or a
  wallet.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Api/Controllers/GamesController.cs` | `GET {parametersId}/stats`, `GET stats`, `GET {parametersId}/history` |
| `SpaceStroker.Infrastructure/Handlers/Games/GetGameTypeStats/` · `GetGlobalStats/` · `GetGamesHistory/` | Requests + handlers (window coercion, paging clamp, identity, vague errors) |
| `SpaceStroker.Infrastructure/Services/Games/GameStatsService.cs` · `GameHistoryService.cs` | Postgres aggregation + Redis cache + live own-columns join |
| `SpaceStroker.Infrastructure/Contracts/Games/GameTypeStatsDto.cs` · `GlobalStatsDto` · `GameHistoryDto.cs` | DTOs (each echoes the applied window) |
| `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` | `GenerateGameStatsCacheSettings`, `GenerateGamesHistoryCacheSettings` |
| `Migrations/*_AddQueryCoveringIndexes.cs` | The covering indexes the window filter depends on |

**Frontend (`monkify-front`)** — `src/pages/GameStatsPage.vue`, `src/pages/GameHistoryPage.vue`,
`src/components/game/GameStatsPanel.vue`, `src/components/game/GameSidePanel.vue` (chat-only),
`src/hooks/useAmountFormatter.ts`, `src/services/mainService.ts`, `src/types/index.ts`, plus the GA4
consent banner and `gtag` bootstrap in both `monkify-front` and `monkify-lp`.

**Tests** — `GameStatsServiceTests`, `GetGameTypeStatsHandlerTests`, `GameHistoryServiceTests`,
`GetGamesHistoryHandlerTests`; integration `GameStatsFlowTests`, `GamesHistoryFlowTests`.

## ⚠️ Known issue surfaced (not caused) here

The stats integration test exercised a **multi-winner payout** for the first time and revealed that such
a payout can be **double-credited under broker redelivery** — a pre-existing idempotency race in
`RewardGameConsumer`, orthogonal to these read-only features (the stats correctly report whatever the
ledger holds). Tracked for a separate fix.
