# Feature — Windowed stats + query-layer performance

> Status: **shipped.** Authoritative spec: `../specs/windowed-stats-query-performance.md`
> (merged via `feature/query-indexes` on 2026-08-05). Ships Trello #148 **and** #133 (Table indexes)
> on the same branch.

## What it does

The Stats page (`/space-stroker/game/stats`) gains a **Weekly / Monthly / All-time** control that
drives both its sections at once, so a player can ask "what happened lately?" instead of only seeing
all-time totals.

The visible half is the smaller half. Both stats services used to load **every completed round ever,
then every bet of those rounds, then every credit log**, and aggregate them in C# — an unbounded
materialisation that grew with the game's whole history. Aggregation now happens in Postgres, which is
what actually removed the cost and is why all-time survives as an option without a rollup table.

Four further query-layer defects found in the same 2026-08-04 audit ship with it, plus the
`AddQueryCoveringIndexes` migration from #133.

## Core rules

### Windowing

- **`GET /games/stats` and `GET /games/{parametersId}/stats` take an optional `window`** of
  `week` | `month` | `all`.
- **Windows are ROLLING, not calendar** — `week` is the last 7 days, `month` the last 30. There is no
  timezone or week-start decision anywhere in the system, and a window is always full size, so tiles
  never collapse to near-zero at the start of a month.
- **Omitting the param means `all`**, and the response for the default case is byte-identical in shape
  to what shipped before, so an existing caller is unaffected.
- **An unrecognised value resolves to `all` with HTTP 200** — never a 400. This mirrors the
  games-history endpoint, where an out-of-range page returns an empty page rather than an error.
- **Both DTOs echo the window that was APPLIED** (`window: "all" | "week" | "month"`). Clients label
  tiles from that field, not from what they requested — otherwise a coerced value would caption
  all-time numbers "last 7 days" with nothing to catch it.
- **The Stats page defaults to All time** and one page-level control drives the global *and* per-type
  sections, so the two halves can never describe different periods.
- **Hottest choice is hottest-in-window** for preset types; a window with no paid preset bets returns
  none.
- **A window with no completed rounds renders the existing "No rounds yet" empty state**, not a row of
  zeros.
- Unchanged by the window: only `RewardForWinnersCompleted` rounds count, and free bets
  (`Amount == 0`) are excluded from every money figure but counted in participation.

### How the aggregates are computed

- **Every figure is a bounded aggregate computed by Postgres.** Each read returns at most one row per
  active game type — never one row per round or per bet. Adding a stat means adding a bounded
  aggregate, not another `.ToListAsync()`.
- **The window filter must not use `COALESCE`.** `Game.EndDate` is nullable even on completed rounds
  (`games-history.md` documents `CreatedDate` as its fallback), so the bound is written as
  `EndDate >= cutoff || (EndDate == null && CreatedDate >= cutoff)` — two index-usable arms.
  `(EndDate ?? CreatedDate) >= cutoff` is unsargable and forfeits the index.
- **`BiggestPot` groups twice.** Grouping only by round would return one row per completed round,
  reintroducing the unbounded result set; the second grouping collapses to one row per type so the
  in-memory lottery base can be added over a handful of rows.
- **MAX aggregates project to nullable.** `MaxAsync` over an empty sequence throws while SQL `MAX` over
  no rows is `NULL`, so an empty window returns 0 rather than blowing up.

### Cache

- **Stats cache keys carry the window** — `GlobalStats:{window}`, `GameStats:{parametersId}:{window}`.
  TTL stays 120s, and that TTL is also what bounds the rolling window's boundary drift.
- **`ICacheService.GetOrCreateAsync` single-flights a miss**: concurrent misses on one key collapse to
  one factory invocation. The semaphore registry is **static by necessity** — `ICacheService` is
  registered `Scoped`, so an instance field would give every request its own gate and dedupe nothing.
  Release is in a `finally`, or a throwing factory deadlocks every later reader of that key.
- ⚠️ **Per-instance, not per-deployment.** The API is multi-instance on Cloud Run, so N instances can
  still each run one aggregation. Accepted deliberately over a Redis lock on a public read path.

### The other four fixes

- **`OrbitReconciliationJob.ReconcilePlayerColumns` is no longer an N+1.** It was one `SUM` per
  `OrbitBalance` row — linear in the enrolled player base, four times a day. It is now two bounded
  queries. ⚠️ **A missing ledger group means a ledger of ZERO, not "skip this balance"**: a non-zero
  column with no transactions at all is exactly the drift this job exists to catch.
- **Out-of-range history pages are never cached.** `pageNumber` is unbounded on the public endpoint and
  is part of the `GamesHistory:{id}:{page}:{size}` key, so iterating it used to mint unlimited 5-minute
  keys. The page is **not** clamped to the last real page — an out-of-range page still returns an
  **empty page**, per the shipped contract.
- **History pagination still uses `OFFSET`.** Keyset pagination needs a cursor and the endpoint is
  page-number addressed, so it was deliberately deferred — see "Known limits".
- **`CancellationToken` is threaded** through `IGlobalStatsService`, `IGameStatsService` and
  `IGameHistoryService`, so a disconnecting client no longer leaves heavy reads running.

### Indexes (from #133, on this branch)

- `Games(Status)`, `Games(ParametersId, Status)`, `Games(ParametersId, Status, EndDate DESC, CreatedDate DESC)`,
  `GameBets(PlayerId, GameId)`, `GameParameters(Active, AllowOrbit)`, `OrbitEnrollments(GameParametersId, Enabled)`.
- ⚠️ **There is deliberately NO index on `GameBets.Status`.** It made the refund sweep 45.8ms → 6.3ms
  at 1M rows, but `UpdateBetStatusWithoutSaving` touches only `Status` and `UpdatedDate`, so with
  neither indexed Postgres applies those updates as **HOT** — zero index writes. Indexing `Status`
  measured **92% → 0% HOT** and +0.323ms per update. The sweep runs twice daily (~80ms/day earned)
  against ~0.65ms per settled bet, so it is net-negative past **~124 bets/day**. Do not re-add it
  without re-running the write benchmark.
- `Games(ParametersId, Status)` is kept **narrow on purpose** even though it is a prefix of the wider
  composite: the history `COUNT(*)` runs as an index-only scan and the wider index costs 16 more bytes
  per entry (6.9ms vs 11.0ms at 1M).

## Known limits

- **Deep history pages are still O(offset)** — ~45ms at 1M rows. Bounded by the real page count and
  absorbed by the 5-minute page cache. Eliminating it needs a cursor contract plus frontend work on
  `GameHistoryPage.vue`; that is a separate card.
- **The recent-rounds feed is unimproved** (~94ms at 1M, Seq Scan). Its filter matches ~99% of `Games`,
  so a scan is the correct plan; a `(Status, EndDate DESC)` index is an untested hypothesis.
- `GameStatsPanel.vue` is **dead code** — unreferenced since `fair-stats-menu` moved stats to the page.

## Key code touchpoints

**monkify-backend**

- `src/SpaceStroker.Domain/Games/ValueObjects/StatsWindow.cs` — the enum, lenient `Parse`, rolling `Cutoff`
- `src/SpaceStroker.Infrastructure/Services/Games/GlobalStatsService.cs` / `GameStatsService.cs`
- `src/SpaceStroker.Infrastructure/Services/Cache/SingleFlightGate.cs`, `ICacheService.cs`, `CacheHelper.cs`
- `src/SpaceStroker.Infrastructure/Handlers/Games/GetGlobalStats/`, `.../GetGameTypeStats/`
- `src/SpaceStroker.Api/Controllers/GamesController.cs` — parses `window` at the edge
- `src/SpaceStroker.Infrastructure/Jobs/OrbitReconciliationJob.cs`
- `src/SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`
- `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — index set + the reasoning
- `src/SpaceStroker.Infrastructure/Migrations/*_AddQueryCoveringIndexes.cs`
- `src/SpaceStroker.Tests.Integration/Benchmarks/` — read + write rigs, `scripts/run_index_benchmarks.bat`

**monkify-front**

- `src/pages/GameStatsPage.vue` — the period control, both loaders, sequence guards
- `src/services/mainService.ts` — `getGlobalStats(window)`, `getGameTypeStats(id, window)`
- `src/types/index.ts` — `StatsWindow`, `window` on both stats types
