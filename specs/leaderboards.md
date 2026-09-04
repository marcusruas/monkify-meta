# Space Stroker — "Leaderboards" Feature — Implementation Spec

> **Status: SHIPPED** — merged via `feature/leaderboards` to `main` on 2026-09-02, then extended by
> `feature/leaderboard-weekly-reset` (the anchored weekly reset, §3 decision 24) on 2026-09-04.
> Distilled into [`../features/leaderboards.md`](../features/leaderboards.md), which is the doc to
> read on an ordinary task; this spec stays the deep-design archive.
>
> ⚠️ Two boards ship **dark** behind fail-closed flags — see the feature doc before assuming one is
> broken. ⚠️ §14b records three silent failures the build hit; read it before touching the queries.
> ⚠️ **Decision 3 (rolling 7 days) is SUPERSEDED by decision 24** — the weekly boards reset on a fixed
> Friday 12:00 BRT anchor. Decision 3 is kept only for the reasoning behind the cutover floor and the
> unanchored fallback; do not implement from it.
>
> Originally: refined via Trello card
> [Leaderboards (#182)](https://trello.com/c/80OsGlGn/182-leaderboards) (2026-09-01).
> Repos affected: `monkify-backend`, `monkify-front`, and this doc in `monkify-meta`.
>
> **Purpose:** give players a way to see how they compare to everyone else. Three read-only boards in
> one carousel — a rolling-7-day **Gamble race**, a rolling-7-day **Weekly PNL**, and an all-time
> **Hall of Fame** of biggest win multipliers — on the home page and on a shareable `/leaderboard`
> route. Status and distribution only: **this feature moves no money and writes nothing.**

---

## 1. Summary

Today the app reports totals but never ranks. `GET /games/stats` is deployment-wide,
`GET /games/{id}/stats` is per type, and `GET /players/stats?wallet=` is a private mirror you can only
read if you already know the wallet. Nothing puts two players next to each other.

This card adds one public read that returns three ranked boards, and the UI to show them.

| Board | Metric | Window | Cutover floor | Money shown |
| --- | --- | --- | --- | --- |
| **Gamble race** (default slide) | `Σ` paid stake | the current leaderboard **week** — anchored, resets Fri 12:00 BRT (§3 decision 24) | **yes** | yes — `Σ` gambled |
| **Weekly PNL** | `Σ` credits − `Σ` paid stakes (may be negative) | same anchored week | **yes** | yes — net, signed |
| **Hall of Fame** ⚠️ *flagged off, not in phase 1* | `max(credit ÷ own stake)`, one row per player | **all time** (no 7-day window) | **yes** — see §3 decision 9 | yes — the SOL won on that bet |

Each board is filterable by game type (All types / one `GameParameters`), shows the top **10** in the
home-page section and the top **25** on `/leaderboard`, and pins the caller's own row below the list
when they rank outside the visible slice.

⚠️ **Phase 1 ships TWO of the three.** The Hall of Fame is built end to end but hidden behind
`VUE_APP_HALL_OF_FAME_ENABLED` (fail-closed, default `false`), so the carousel renders two chips —
see §3 decision 19. ⚠️ **All three boards share ONE layout** — the stepped plinths (§3 decision 20) —
varying only in colour, meta line and column header, so the board is one generic component.

**What this is NOT:** there is no prize, no pool, no season, no freeze, no payout. The rejected
alternative — a `LeaderboardSeason` / `LeaderboardAward` pair with a `PayLeaderboardPrizeConsumer` —
is out of scope by explicit owner decision (§3 decision 2, §9).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree per repo:** `feature/leaderboards` in `monkify-backend`, `monkify-front` and
  `monkify-meta`, each at `.worktrees/leaderboards/<repo>`. The shared checkouts stay on `main`.
- **Unit tests:** add/extend in `SpaceStroker.Tests` (run via `scripts/run_tests.bat`, never the full
  `dotnet test` suite); report the changed tests at the end of implementation.
- **Integration test is MANDATORY** — the card is `Level: Feature` (`AGENTS.md` rule 8). See §11.2.
- **EF mapping:** inline in `SpaceStrokerDbContext.OnModelCreating`. **Not applicable here — this
  feature adds no entity, no column, no index and no migration.**
- **Secrets/config:** mock-only values in committed files. This feature *reads* four configured public
  keys; it must never log or return a private key, and a `MOCK_` value is treated as "not configured".
- **Vague messaging (`constraints.md` §6):** an unknown or deactivated `parametersId` collapses to the
  same `ErrorMessages.ParameterNotFound` every other type-scoped read returns. The endpoint must never
  become a channel for probing which types or wallets exist.
- **No referral leak:** `ReferralCode` and partner data appear on no DTO, no SignalR event and no
  public endpoint. This response is no exception.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Card #178 is not merged in.** #182 was refined standalone by owner decision; #178's rake-funded pool, floor/ceiling and anti-farm positions are explicitly **not** carried in. |
| 2 | **No prizes, no payouts, no money movement.** Chosen over hand-paid prizes and over an automated payout consumer. The ranking logic is the valuable, testable part; a prize can be added later as its own card without changing any of it. Consequence: no season/freeze table, no prize wallet, no payout consumer, no solvency guard. |
| 3 | ⚠️ **SUPERSEDED by decision 24** — kept for the reasoning, which still explains why the fallback and the cutover floor are shaped as they are. Originally: **"Weekly" means rolling 7 days**, reusing the shipped `StatsWindow.Week` / `StatsWindowExtensions.EffectiveCutoff`. A UTC calendar week was rejected: with no prize there is nothing to freeze and pay, it would be the system's **first** calendar-week and week-start/timezone decision, and the board reads near-empty every Monday morning. |
| 4 | **Three metric slides + a game-type filter**, not a slide per type. Delivers the "Cosmic Sprint leaderboard" the card asked for without the carousel growing with the catalogue. Filter defaults to **All types**. |
| 5 | **House-controlled wallets are excluded by configured public key**, never by a marker column. The house seed bet is deliberately an ordinary bet with *"no marker column, no DTO field, no flag, no branch anywhere"* (`features/games.md` §14) and a `Player.IsHouse` column would break that. All four house accounts already publish a `PublicKey` in config (§6.4), so this needs no key derivation and no migration. |
| 6 | **The shipped money predicate is reused verbatim** — only `RewardForWinnersCompleted` rounds; free bets (`Amount == 0`) excluded from money; refunded / `NotEnoughPlayersToStart` / `ErrorWhenProcessingRewards` / `GameEndedAbruptely` excluded; `SettlementCutoverUtc` as a floor on the two weekly boards. A board whose numbers do not reconcile with the Stats page for the same player is a defect. |
| 7 | **Orbit bets count.** They are ordinary paid bets carried on the real player's `Player` row (`Bet.PlayerId`), so this needs no special handling at all. The **Orbit custody account is never a leaderboard entity** — it signs, it does not own the bet. |
| 8 | **No minimum participation on any board.** One qualifying bet is enough. With no prize there is nothing to farm, and a lucky one-round result is the same shareable asset the Hall of Fame exists for. |
| 9 | ⚠️ **The Hall of Fame is ALL-TIME *and* floored at `SettlementCutoverUtc` — the two are not in conflict.** "All time" means it takes **no 7-day window**, which is what distinguishes it from the other two boards. The cutover floor is the separate money-correctness rule every money read obeys, and it applies here **because the board states an amount**: the row shows the multiplier **and the SOL won** (owner, 2026-09-02), and a pre-cutover amount would print a USDC-era figure wearing a SOL mark — roughly a 200× misstatement, the exact class of bug the cutover exists to prevent. ⚠️ *An earlier revision EXEMPTED this board from the cutover, on the grounds that a multiplier is dimensionless. That reasoning was sound for a multiplier-only board and is **void now that the board carries money** — do not reinstate the exemption without also removing the amount.* Ranking stays on the **multiplier**, not the amount: a Space Jackpot ticket is 10× a Cosmic Sprint one, so the biggest multiplier is routinely not the biggest payout. |
| 10 | **Hall of Fame ranks people, not moments** — one row per player, their single best multiplier ever. One row per winning bet was rejected: a single hot streak would fill the board. |
| 11 | **Identity is `Player.DisplayName` falling back to a truncated wallet, with no opt-out.** A `Player.HideFromLeaderboard` column was rejected — it is a migration plus a signature-gated write, and it makes the board quietly not the real top N. Accepted cost: the board makes the biggest wallets discoverable in one click, which per-wallet stats today do not. |
| 12 | **Self-excluded players are hidden from all three boards while the exclusion is active.** Putting a self-excluded person's name on a gambling leaderboard uses their identity to promote a product they formally asked to be kept away from. Reuses `Player.SelfExcludedAt` / `SelfExcludedUntil`; they reappear when it lapses. Belongs in `features/legal.md` at ship time. |
| 13 | **Top 10 in the home section, top 25 on `/leaderboard`, plus the caller's own row pinned** when outside the visible slice. "Where am I?" is the question a leaderboard exists to answer. |
| 14 | **One endpoint returns all three boards.** A carousel is a widget the user *will* swipe, so lazy-loading per slide buys nothing and costs a spinner on every swipe. It also holds the home dashboard at **eight** reads on mount instead of ten (`specs/home-dashboard.md` decision 32 argued that count explicitly), and keeps the window/cutover asymmetry in one handler where it is explained once. |
| 15 | **Placement: a carousel section on `/` plus a dedicated `/leaderboard` page.** ⚠️ **It gets NO ☰ menu cell** — the pad is a 3-column **nine**-cell grid and `features/frontend-ux.md` is explicit that a tenth cell is not an option. Entry is the home section and a link from `/game/stats`; the route is otherwise unlisted, exactly like `/partner`. |
| 16 | **Tie-break is deterministic**: score descending, ties by **earliest qualifying bet** ascending. Never by wallet string (a permanent alphabetical advantage) and never unordered. |
| 17 | **No SignalR and no polling.** Fetch on mount and on `visibilitychange` focus, the pattern `specs/home-dashboard.md` decision 32 settled. `useGameHub` keeps its one-connection / one-group invariant. |
| 18 | **5 mockup variants of the whole carousel** (not 5 per board), one chosen and pinned on the card before implementation. The three boards are one component with different columns. ⚠️ The Claude Design MCP was **not connected** during refinement (`FIRST_PARTY_AUTH_REJECTED`) — run `/design-login` first. |
| 19 | ⚠️ **The Hall of Fame is FEATURE-FLAGGED OFF and does NOT ship in phase 1** (owner, 2026-09-01). It is built in full — endpoint, aggregate, tests — but hidden behind **`VUE_APP_HALL_OF_FAME_ENABLED`**, read through `src/constants/featureFlags.ts` with the same **fail-closed** `=== 'true'` test `orbitEnabled` uses, and **default false**. With the flag off the carousel renders **two** chips and the slide does not exist. The board is still returned by the API — the flag is frontend-only, as specified. |
| 20 | **All three boards take the SAME layout: the stepped plinths.** Height carries rank; the meta line under each plinth's figure names it. (A per-board treatment — a champion band on the gamble race — was drawn, reviewed and **rejected** by the owner on 2026-09-02: *"gamble race must be in the same format as the others"*.) What varies per board is only **colour** (amber / green-magenta / cyan), the **meta line** content, and the **column header** word. Consequence: the board stays **ONE generic component** configured per board, not three — which is also the cheaper thing to build and the easier thing to keep consistent. |
| 22 | ⚠️ **NARROWED BY DECISION 24 — this now applies only to the UNANCHORED (legacy rolling) mode, which production does not run.** ⚠️ **A quiet week does not empty the board** (owner, 2026-09-02). If the last 7 days contain no qualifying round **for the applied filter**, the board does **not** render empty: it anchors on the **most recent qualifying round** and shows the **7 days ending there**, and the response says so. Two sub-decisions, both deliberate: the fallback window is **rolling from the anchor**, not "the last calendar week that had data" — a calendar week would reintroduce the week-start/timezone decision §3 decision 3 exists to avoid; and it resolves **per filter**, so a game type nobody played this week shows that type's last active week, not the deployment's. ⚠️ The fallback window is **still floored at the cutover** — `anchor − 7d` may not reach past it. The genuine empty state (`no rounds yet`) now means only "this filter has never had a completed round". |
| 23 | **The window is a BADGE, not a caption, and every amount carries the SOL mark.** The two weekly boards must not be mistakable for all-time ones, so the window renders as an accent-bordered chip beside the type filter — `LAST 7 DAYS` / `ALL TIME` / the real dates on a fallback (`18–24 AUG`). ⚠️ It must **never** read "LAST 7 DAYS" when the data is not from the last 7 days. Money uses the existing **`src/assets/sol.svg`** brand mark, sized in `em` so it tracks the figure beside it — ⚠️ **deliberately NOT in the pixel registry**: `constants/icons.ts` states in as many words that `sol.svg` stays an `<img>`-style brand asset at `image-rendering: auto` while everything else is crisped pixel art. |
| 24 | ⚠️ **THE WEEKLY BOARDS RESET ON A FIXED WEEKLY ANCHOR — Friday 12:00 BRT** (owner, 2026-09-04; originally specified as Wednesday 19:00 BRT on 2026-09-02 and re-pointed before the feature ever shipped), **superseding decision 3**. The two weekly boards rank the slot `{anchor + k*7d}` that contains `now`, so the race restarts on a schedule instead of sliding with the clock. **Stored as a single UTC instant** (`GeneralSettings:Operator:Leaderboards:WeeklyResetAnchorUtc`, production `2026-09-04T15:00:00Z`) and stepped by whole weeks — ⚠️ **deliberately NOT a weekday + IANA timezone**: resolving `America/Sao_Paulo` at runtime would make the boards depend on the tz database being present and identically named in the Cloud Run image and on a Windows dev box, for a country that has had **no DST since 2019**. Only the **weekday and time-of-day** matter, not the date; editing the anchor **re-cuts every live week at once**. It lives in the deploy file's **`appConfig`** block beside `SettlementCutoverUtc`, **not** `secrets` — it is a public schedule, and `appConfig` reaches Cloud Run without the hand-added `base_secret_ids`/`base_secret_env` binding (`SecretBindingCheck` documents the split). ⚠️ **NULL = the legacy rolling 7 days**, fallback and all, which is the fail-soft default and what a local box and the integration suite run. ⚠️ **THE ANCHORED MODE NEVER FALLS BACK** (narrowing decision 22): a hard reset makes "empty" a true, self-explaining state, so a quiet board is shown as quiet and the client says *"no rounds yet this week"*. Falling back here would display **last week's winners** for the minutes between the reset and the first bet and then silently swap them out — hiding the very event the reset exists to stage. Accepted cost: a game type nobody has played this week shows an empty board rather than its last active week. ⚠️ The window is **still floored at `SettlementCutoverUtc`**. ⚠️ **The Redis key gains the period start**, or the reset would land up to a full 120 s TTL late — on the single most-watched moment the boards have. |
| 21 | ⚠️ **A bare figure is not acceptable** (owner, 2026-09-01: *"it must show clearly what that 1.84 SOL means"*). Every board labels its score **three** ways: a one-line **explainer** under the head (*"Ranked by total SOL gambled in the last 7 days"*), a **meta line under each plinth figure** naming the unit (`SOL GAMBLED` on the gamble race, `SOL` + the working `1.84 IN · 2.78 OUT` on the PNL board, game type + date on the Hall of Fame), and a **column header** above the list (`GAMBLED` / `NET` / `MULTIPLIER`). The PNL board shows the working specifically because a net figure without it is unreadable. |

---

## 4. Data model

**No change. This is the section that stays empty on purpose.**

- **No entity, no property, no EF mapping, no index, no migration.** Every figure is aggregated on
  demand from existing `Game` / `Bet` / `TransactionLog` / `Player` rows.
- This is consistent with the **deliberate, documented reversal** recorded in
  `features/stats-and-history.md`: the maintained-counter table was tried, rejected and removed, and
  the cost was solved by aggregating in Postgres rather than by a rollup.
- The existing covering indexes are what this feature depends on —
  `IX_Games_ParametersId_Status_EndDate_CreatedDate` (`AddQueryCoveringIndexes`). ⚠️ **Do not add an
  index speculatively.** `SpaceStrokerDbContext` carries a MEASURED verdict against a speculative
  `Bet` index ("NO INDEX ON Status — tried, MEASURED, and removed"); if a leaderboard query turns out
  to need one, measure first and add it in a follow-up with the numbers attached.

---

## 5. Configuration

**No new configuration key.** The feature *reads* four values that already exist under
`GeneralSettings:Operator:Token`:

| Key | Type | Role here |
| --- | --- | --- |
| `TokenOwnerPublicKey` | `TokenSettings` | Treasury — excluded |
| `Orbit:PublicKey` | `OrbitSettings` | Player custody — excluded |
| `HouseSeed:PublicKey` | `HouseSeedSettings` | The seed bettor — **the one that actually matters**; it bets in every round on every type |
| `ReferralPayout:PublicKey` | `ReferralPayoutSettings` | Partner payouts — excluded |

Two existing values are also read, unchanged: `SettlementCutoverUtc` (the floor) and
`Symbol` / `Decimals` (the currency descriptor echoed on the response).

⚠️ **`MOCK_` counts as absent.** Every one of the four settings classes already documents the
fail-closed `MOCK_` convention (`HouseSeedSettings.IsConfigured`, `ReferralPayoutSettings.IsConfigured`,
`OrbitAccountResolver`'s treatment of `MOCK_ORBIT_PUBLIC_KEY`). The exclusion list must **skip blank
and `MOCK_`-prefixed values** rather than putting them in the `NOT IN`, so a dev stack does not carry
four meaningless literals into every query.

---

## 6. Backend behaviour

Everything lives on the stateless **`SpaceStroker.Api`**. Nothing runs in `SpaceStroker.Consumers` —
no job, no consumer, no scheduled work, no message.

### 6.1 Endpoint — `GET /leaderboards`

```
GET /leaderboards?parametersId=<guid>&wallet=<base58 address>&size=<10|25>
```

New `SpaceStroker.Api/Controllers/LeaderboardsController.cs`, following `PlayersController` /
`GamesController`:

- **Public.** No auth header. Identity, when present, is the `wallet` query param — the same contract
  as `GET /players/stats` and `GET /players/preferences`.
- Wrapped in the standard `ApiResult<T>` envelope by `BaseController.ProcessRequest`.
- **`[HttpOptions("")]`** for CORS preflight, matching `PlayersController`'s existing
  `[HttpOptions("stats")]`.
- **`parametersId` omitted or blank ⇒ All types.** A `parametersId` that does not resolve to an active
  type ⇒ `ErrorMessages.ParameterNotFound` (§6.7).
- **`size`** is clamped to `[1, 25]` at the edge with a default of `10`, mirroring the games-history
  page-size clamp. Out-of-range coerces, it never 400s.
- **200 always**, except the vague `ParameterNotFound`. No wallet, an unknown wallet, or a wallet that
  never bet all return the boards with `you: null` on each.

```csharp
/// <summary>
/// The three public leaderboards in one read: the rolling-7-day gamble race, the rolling-7-day PNL
/// board, and the all-time hall of fame of biggest win multipliers. Public and wallet-keyed exactly
/// like GET /players/stats — it ranks facts that are already publicly readable by address. An
/// unknown wallet gets the boards with no own-rank row, never an existence-revealing error.
/// </summary>
/// <response code="200">Returns the three boards (each possibly empty).</response>
/// <response code="400">The game type was not found.</response>
[HttpGet]
public async Task<IActionResult> GetLeaderboards(
    [FromQuery] Guid? parametersId = null, [FromQuery] string? wallet = null, [FromQuery] int size = 10)
    => await ProcessRequest(new GetLeaderboardsRequest(parametersId, wallet, LeaderboardSize.Clamp(size)));
```

### 6.2 Request + handler — `Handlers/Leaderboards/GetLeaderboards/`

`GetLeaderboardsRequest.cs`

```csharp
public record GetLeaderboardsRequest(Guid? ParametersId, string? Wallet, int Size)
    : IRequest<LeaderboardsDto>;
```

`GetLeaderboardsHandler.cs` — modelled on `GetGlobalStatsHandler` (resolve → aggregate → map), with
the caller-resolution step copied from `GetGamesHistoryHandler.ResolveCallerPlayerId`:

1. `_gameService.GetAllActiveGameParameters()` → the active catalogue. This is the same read
   `GetGlobalStatsHandler` makes, and it is `ActiveParameters`-cached (12 h).
2. If `request.ParametersId` is set and is **not** in that dictionary → `ParameterNotFound` (§6.7).
   Otherwise the type id list is either the one requested type or every active type.
3. Resolve the caller's `Player.Id` from `wallet` — `Players.AsNoTracking().FirstOrDefaultAsync(x =>
   x.ExternalPlayerId == wallet)`, null when blank/unknown. **Null is not an error.**
4. `_leaderboardService.GetLeaderboards(typeIds, request.ParametersId, callerPlayerId, request.Size, ct)`.
5. `LeaderboardsDto.From(result, _settings.Operator.Token, activeTypes)`.

### 6.2.1 The weekly reset grid — `Domain/Leaderboards/Services/LeaderboardSchedule.cs`

Pure: no `DbContext`, no ambient clock, no `TimeZoneInfo`. Deliberately mirrors `LotterySchedule`,
which already solved "fixed grid, not a rolling offset" for lottery draws.

```csharp
public static readonly TimeSpan Period = TimeSpan.FromDays(7);

// The slot containing nowUtc; null when unanchored (=> caller keeps the rolling window).
public static DateTime? CurrentPeriodStart(DateTime nowUtc, DateTime? anchorUtc);

// CurrentPeriodStart + Period; null when unanchored. This is what the client gets as `resetsAt`.
public static DateTime? NextReset(DateTime nowUtc, DateTime? anchorUtc);
```

⚠️ **Integer ticks only**, for the reason `LotterySchedule` documents: ticks since year 1 (~6.4e17)
exceed `double`'s exact-integer range (~9e15), so a floating-point quotient lands on the wrong slot.

⚠️ **THE DIVISION MUST FLOOR, NOT TRUNCATE.** C# integer division rounds **toward zero**, so for an
instant *before* the anchor a naive `elapsed / period` yields `0` and hands back a period that has not
started — a window running into the **future**, with every completed round outside it and both weekly
boards silently empty, no error anywhere. This is **not** a hypothetical branch: the anchor shipped as
"today at 12:00 BRT", so every read before that instant took it on day one. Pinned by
`LeaderboardScheduleTests`, five of whose cases fail if the floor is removed.

⚠️ **The anchor's `Kind` is normalised, never converted.** The configuration binder hands back
`Unspecified` (or `Local`); the value is *read as* UTC so a dev box in BRT and a Cloud Run box in UTC
land on the same grid, and so Npgsql never sees a non-UTC `DateTime` on a `timestamptz` parameter.
`StatsWindowExtensions.EffectiveCutoff` and `ReferralEarningsService` carry the identical guard.

### 6.3 `ILeaderboardService` / `LeaderboardService` — `Services/Leaderboards/`

New folder `SpaceStroker.Infrastructure/Services/Leaderboards/` holding `ILeaderboardService.cs`,
`LeaderboardService.cs` and the plain result models `Leaderboards.cs` / `LeaderboardEntry.cs` — the
same shape `Services/Games/` uses for `GlobalStats` / `GlobalStatsService`.

⚠️ **This service follows `GlobalStatsService`, not `PlayerStatsService`.** `PlayerStatsService` pulls
one player's whole bet history into memory with `.ToListAsync()` and aggregates in C#; that is
acceptable for **one** player and catastrophic across all of them. Every figure below is aggregated by
Postgres and every result set is bounded by a `Take`.

#### 6.3.1 The two shared building blocks

```csharp
/// <summary>
/// Completed rounds of these types, between two optional bounds. The BOUNDS are the only thing that
/// differs between the three boards, and each resolves its own:
///
///  - the two WEEKLY boards pass what ResolveWeeklyWindow returns — the last 7 days floored at
///    SettlementCutoverUtc, or the fallback week when that is empty (decision 22);
///  - the HALL OF FAME passes (cutover, null) — no 7-day window, which is what "all time" means here,
///    but STILL the cutover floor, because that board states a SOL amount (decision 9).
///
/// ⚠️ Every one of them is floored at the cutover. Summing across it would add USDC-era figures to
/// SOL-era ones and report a number in no currency at all. See specs/sol-settlement.md §6.4.
///
/// ⚠️ Neither bound is written as (EndDate ?? CreatedDate). A COALESCE is unsargable and would forfeit
/// IX_Games_ParametersId_Status_EndDate_CreatedDate. Both arms below are index-usable — the identical
/// predicate GlobalStatsService uses.
/// </summary>
private IQueryable<Game> CompletedRounds(List<Guid> typeIds, DateTime? from, DateTime? to)
{
    var completed = _context.Games
        .Where(round => typeIds.Contains(round.ParametersId)
                     && round.Status == GameStatus.RewardForWinnersCompleted);

    if (from != null)
        completed = completed.Where(round => round.EndDate >= from
                                          || (round.EndDate == null && round.CreatedDate >= from));

    if (to != null)
        completed = completed.Where(round => round.EndDate <= to
                                          || (round.EndDate == null && round.CreatedDate <= to));

    return completed;
}

/// <summary>
/// The players who may appear on a board: not a house-controlled wallet, and not currently
/// self-excluded.
///
/// ⚠️ The self-exclusion predicate is written INLINE and is the exact negation of
/// Player.IsSelfExcluded(now). It cannot call that method — it is a C# method EF Core cannot
/// translate, and calling it would silently fall back to client evaluation over every Player row (or
/// throw, depending on the provider). Keep the two in step by hand; the unit test in §11.1 pins them
/// against each other.
/// </summary>
private IQueryable<Player> EligiblePlayers(DateTime now)
    => _context.Players.Where(player =>
        !_houseWallets.Contains(player.ExternalPlayerId)
        && (player.SelfExcludedAt == null
            || (player.SelfExcludedUntil != null && player.SelfExcludedUntil <= now)));

/// <summary>
/// The window a WEEKLY board actually covers — and the reason it is resolved rather than assumed.
///
/// Primary: the last 7 days, floored at the settlement cutover, exactly as every other stats read.
/// ⚠️ FALLBACK (§3 decision 22): if that window holds no qualifying round FOR THIS FILTER, the board
/// does not go empty — it anchors on the most recent qualifying round and covers the 7 days ending
/// there. A quiet week showing an empty leaderboard reads as a broken feature, which is what this
/// exists to prevent.
///
/// ⚠️ The anchor is found as the LATER of MAX(EndDate) and MAX(CreatedDate), deliberately NOT as
/// MAX(EndDate ?? CreatedDate): the COALESCE is unsargable and would scan, and two bounded MAXes are
/// both index-usable. CreatedDate <= EndDate on a completed round, so the later of the two is only
/// ever the CreatedDate when the newest round carries a null EndDate — which is precisely the row the
/// fallback wants.
///
/// ⚠️ The fallback window is STILL FLOORED AT THE CUTOVER: `anchor - 7d` is clamped, or a quiet week
/// would quietly reach back into the previous settlement asset and sum two currencies together.
///
/// Returns null bounds and IsFallback=false when the filter has never had a completed round — the
/// genuine empty state.
/// </summary>
private async Task<LeaderboardWindow> ResolveWeeklyWindow(List<Guid> typeIds, CancellationToken ct)
{
    var now = DateTime.UtcNow;
    var floor = StatsWindowExtensions.EffectiveCutoff(StatsWindow.Week, _settlementCutoverUtc, now);

    if (await CompletedRounds(typeIds, floor, null).AnyAsync(ct))
        return new LeaderboardWindow(floor, now, IsFallback: false);

    // Nothing this week. Find the newest qualifying round, bounded only by the cutover.
    var cutoverOnly = StatsWindowExtensions.EffectiveCutoff(StatsWindow.All, _settlementCutoverUtc, now);
    var all = CompletedRounds(typeIds, cutoverOnly, null);

    var maxEnd = await all.MaxAsync(round => (DateTime?)round.EndDate, ct);
    var maxCreated = await all.MaxAsync(round => (DateTime?)round.CreatedDate, ct);

    var anchor = maxEnd is { } e && maxCreated is { } c ? (e > c ? e : c) : (maxEnd ?? maxCreated);
    if (anchor == null)
        return new LeaderboardWindow(floor, now, IsFallback: false);   // never had a round

    var from = anchor.Value.AddDays(-7);
    if (cutoverOnly is { } bound && from < bound)
        from = bound;

    return new LeaderboardWindow(from, anchor, IsFallback: true);
}

/// <summary>Credits summed per bet — the shipped payout source (TransactionLogType.Credit).</summary>
private IQueryable<BetPayout> WonByBet()
    => _context.TransactionLogs
        .Where(log => log.Type == TransactionLogType.Credit && log.BetId != null)
        .GroupBy(log => log.BetId!.Value)
        .Select(perBet => new BetPayout { BetId = perBet.Key, Won = perBet.Sum(x => x.Amount) });
```

#### 6.3.2 Board 1 — Gamble race (the anchored week, cutover floored)

```csharp
var window = await ResolveWeeklyWindow(typeIds, cancellationToken);   // §6.3.1 — may be a fallback
var rounds = CompletedRounds(typeIds, window.From, window.To);

var paidBets = _context.GameBets
    .Where(bet => bet.Amount > 0m && bet.PlayerId != null)
    .Join(rounds, bet => bet.GameId, round => round.Id, (bet, _) => bet);

var scores = paidBets
    .Join(EligiblePlayers(now), bet => bet.PlayerId!.Value, player => player.Id,
          (bet, player) => new { player.Id, player.DisplayName, player.ExternalPlayerId,
                                 bet.Amount, bet.CreatedDate })
    .GroupBy(x => new { x.Id, x.DisplayName, x.ExternalPlayerId })
    .Select(perPlayer => new LeaderboardEntry
    {
        PlayerId = perPlayer.Key.Id,
        DisplayName = perPlayer.Key.DisplayName,
        Wallet = perPlayer.Key.ExternalPlayerId,
        Score = perPlayer.Sum(x => x.Amount),
        FirstQualifyingAt = perPlayer.Min(x => x.CreatedDate),
    });
```

Ordered `Score` desc, then `FirstQualifyingAt` asc, then `.Take(size)`. **One row per player, bounded
by `size`** — the `GROUP BY` runs in Postgres and the `LIMIT` is in the same statement.

#### 6.3.3 Board 2 — Weekly PNL (the anchored week, cutover floored)

Net is `Σ credits − Σ paid stakes` over the same rounds, in **one** statement via a left join:

```csharp
var perBet =
    from bet in paidBets
    join won in WonByBet() on bet.Id equals won.BetId into payouts
    from won in payouts.DefaultIfEmpty()
    select new { bet.PlayerId, bet.Amount, bet.CreatedDate, Won = won == null ? 0m : won.Won };

var scores = perBet
    .Join(EligiblePlayers(now), x => x.PlayerId!.Value, player => player.Id, (x, player) => new { … })
    .GroupBy(x => new { x.Id, x.DisplayName, x.ExternalPlayerId })
    .Select(perPlayer => new LeaderboardEntry
    {
        …,
        Score = perPlayer.Sum(x => x.Won) - perPlayer.Sum(x => x.Amount),   // negative is a real answer
        FirstQualifyingAt = perPlayer.Min(x => x.CreatedDate),
    });
```

- ⚠️ **Never clamp `Score`.** A losing week is a negative number and the board ranks it as one —
  the same rule `PlayerStatsService` already documents for `PlayerStats.Net`.
- Restricting to **paid** bets loses nothing: a free bet stakes 0 and receives 0
  (`features/money.md` §1), so it can contribute to neither side.
- `DefaultIfEmpty()` is what keeps a player who staked and won nothing on the board at a negative
  score. An inner join would silently drop every losing player — i.e. most of the board.

#### 6.3.4 Board 3 — Hall of Fame (all time, cutover-exempt, ratio only)

**Two bounded steps.** Step one ranks players; step two fetches the detail of the top N's best win.

```csharp
// STEP 1 — one row per player, bounded by size.
// ⚠️ ALL-TIME means NO 7-DAY WINDOW — it does NOT mean no cutover floor. This board states a SOL
// amount (decision 9), so it obeys the money rule like every other money read: the only bound is the
// cutover. An earlier revision passed no bound at all here; that was correct while the board showed
// a bare multiplier and is WRONG now that it shows what was won.
var cutoverOnly = StatsWindowExtensions.EffectiveCutoff(StatsWindow.All, _settlementCutoverUtc, DateTime.UtcNow);
var roundsAllTime = CompletedRounds(typeIds, cutoverOnly, null);

var winningBets =
    from bet in _context.GameBets.Where(b => b.Amount > 0m && b.PlayerId != null)
                                 .Join(roundsAllTime, b => b.GameId, r => r.Id,
                                       (b, r) => new { b.Id, b.PlayerId, b.Amount,
                                                       r.ParametersId, At = r.EndDate ?? r.CreatedDate })
    join won in WonByBet() on bet.Id equals won.BetId         // INNER join: only bets that were paid
    select new { bet.PlayerId, bet.ParametersId, bet.At, won.Won, Multiplier = won.Won / bet.Amount };

var scores = winningBets
    .Join(EligiblePlayers(now), …)
    .GroupBy(x => new { x.Id, x.DisplayName, x.ExternalPlayerId })
    .Select(perPlayer => new LeaderboardEntry
    {
        …,
        Score = perPlayer.Max(x => x.Multiplier),   // the RANK is the multiplier, never the amount
        FirstQualifyingAt = perPlayer.Min(x => x.At),
    });

// ⚠️ The amount is carried for DISPLAY only and must not touch the ordering. A Space Jackpot ticket
// is 10x a Cosmic Sprint one, so the biggest multiplier is routinely not the biggest payout —
// ranking on the amount would quietly turn this into a "biggest win" board, which is a different
// feature and one the ticket prices would decide rather than the players.

// STEP 2 — for those <= 25 player ids only, the single best winning bet, for the date + type columns.
var topIds = top.Select(x => x.PlayerId).ToList();
var best = await winningBets
    .Where(x => topIds.Contains(x.PlayerId!.Value))
    .GroupBy(x => x.PlayerId!.Value)
    .Select(g => g.OrderByDescending(x => x.Multiplier).ThenBy(x => x.At).First())
    .ToListAsync(cancellationToken);
// That row supplies the display columns for each of the top N: Won, ParametersId (→ game type name)
// and At (→ the date).
```

- Step 2 is deliberately a **second query over a fixed id list** rather than a lateral subquery folded
  into step 1: it keeps step 1 a plain `GROUP BY` that Postgres can plan simply, and it is bounded to
  at most 25 rows by construction. Matching the best row **by re-ordering**, never by comparing the
  multiplier decimal for equality — a `decimal` equality match on a computed ratio is a bug waiting to
  happen.
- ⚠️ `Score` here is a **multiplier**, not money. `LeaderboardEntry.Score` is reused for all three
  boards; the DTO's `board` field is what tells the client how to format it, and §6.6 forbids any
  money field being populated on this board.
- The `At` used for the date column and for the tie-break is `EndDate ?? CreatedDate` — the same
  fallback games-history uses, because `Game.EndDate` is nullable even on completed rounds.

#### 6.3.5 Ordering and tie-break (all three boards)

```csharp
.OrderByDescending(entry => entry.Score)
.ThenBy(entry => entry.FirstQualifyingAt)
.Take(size)
```

Deterministic by construction (decision 16). **Never** order by `Wallet` — a base58 string tie-break
hands a permanent advantage to addresses starting with a low character, forever.

### 6.4 The house-wallet exclusion — `Services/Leaderboards/HouseWallets.cs`

One shared helper, **not four inline literals**, resolved once from `GeneralSettings`:

```csharp
/// <summary>
/// The house-controlled wallets that must never appear on a leaderboard. All four accounts already
/// publish a PublicKey in config, so this needs no keypair derivation and no new setting.
///
/// ⚠️ The HouseSeed wallet is the one that actually matters: it places one ordinary paid bet in EVERY
/// newly created round on every type (specs/house-seed-bet.md), so without this filter it would
/// permanently own the top of the gamble race. The other three are excluded on principle — they are
/// house money and could bet.
///
/// ⚠️ A marker column was REJECTED. The seeded bet's defining property is that it has "no marker
/// column, no DTO field, no flag, no migration, no branch anywhere" and is indistinguishable from a
/// player's bet in the database and in every payload (features/games.md §14). Filtering by configured
/// address preserves that; a Player.IsHouse column would end it.
///
/// ⚠️ Blank and MOCK_ values are SKIPPED, not added to the list — the fail-closed convention every one
/// of these settings classes already documents. Putting "MOCK_HOUSE_SEED_PUBLIC_KEY" in a NOT IN is
/// harmless but meaningless, and a blank string in the list is noise in every generated query.
///
/// ⚠️ It reads the SAME config the wallets are configured from. Rotating a house key must move this
/// filter with it automatically — hard-coding an address here is how a rotated key silently returns
/// to the board.
/// </summary>
public static IReadOnlyList<string> Resolve(TokenSettings token) => …
```

Registered as part of `LeaderboardService`'s construction (the settings are already injected as
`GeneralSettings`), materialised once per service instance into `_houseWallets`.

### 6.5 The caller's own rank

Computed **live on every request**, never cached (§6.8). Skipped entirely when `callerPlayerId` is
null, and skipped per board when the caller is already inside the returned slice.

```csharp
var mine = await scores.FirstOrDefaultAsync(entry => entry.PlayerId == callerPlayerId, ct);
if (mine == null) return null;                       // never bet in the window, or is not eligible

var above = await scores.CountAsync(entry =>
        entry.Score > mine.Score
     || (entry.Score == mine.Score && entry.FirstQualifyingAt < mine.FirstQualifyingAt), ct);

mine.Rank = above + 1;
```

⚠️ **The tie-break in the rank count must be the SAME comparison as the ordering in §6.3.5**, or the
pinned row shows a rank that does not match where the player would appear in the list.

⚠️ **A caller who is a house wallet or is currently self-excluded gets `you: null`.** They are not in
`EligiblePlayers`, so they have no rank — deliberately, and the same answer a wallet that never bet
gets. This is correct: a self-excluded player must not be shown their own standing on a board they
have been removed from.

### 6.6 DTOs — `Contracts/Leaderboards/`

```csharp
public class LeaderboardsDto
{
    public LeaderboardBoardDto GambleRace { get; set; }
    public LeaderboardBoardDto WeeklyPnl { get; set; }
    public LeaderboardBoardDto HallOfFame { get; set; }

    /// <summary>
    /// The type filter that was APPLIED (null = all types). The client captions from THIS, not from
    /// what it requested — the same contract GlobalStatsDto.Window carries, and for the same reason.
    /// </summary>
    public Guid? ParametersId { get; set; }
    public IList<LeaderboardGameTypeDto> AvailableTypes { get; set; }

    public string? TokenSymbol { get; set; }
    public int? TokenDecimals { get; set; }
}

public class LeaderboardWindowDto
{
    /// <summary>"week" | "all" — which KIND of window this board takes.</summary>
    public string Kind { get; set; }
    /// <summary>Inclusive bounds actually applied (UTC), null on an all-time board with no cutover.</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>
    /// True when the last 7 days were empty and the board fell back to the most recent week that had
    /// results (§3 decision 22). ⚠️ The client MUST caption from From/To when this is set — rendering
    /// "LAST 7 DAYS" over a fallback window is the one thing decision 23 forbids.
    /// </summary>
    public bool IsFallback { get; set; }
}

public class LeaderboardBoardDto
{
    /// <summary>"gambleRace" | "weeklyPnl" | "hallOfFame" — how the client formats Score.</summary>
    public string Board { get; set; }
    /// <summary>The window applied to THIS board — the three are deliberately not the same.</summary>
    public LeaderboardWindowDto Window { get; set; }
    /// <summary>True on the hall of fame only: Score is a ratio, so the client must not append a symbol.</summary>
    public bool ScoreIsMultiplier { get; set; }
    public IList<LeaderboardRowDto> Rows { get; set; }
    /// <summary>The caller's own row when they rank outside Rows. Null otherwise. Never cached.</summary>
    public LeaderboardRowDto? You { get; set; }
}

public class LeaderboardRowDto
{
    public int Rank { get; set; }
    public string? DisplayName { get; set; }
    public string Wallet { get; set; }          // the client truncates; same as every other identified surface
    public decimal Score { get; set; }
    public bool IsYou { get; set; }
    /// <summary>Hall of fame only — when the best win happened, on which type, and what it paid.
    /// Null on the other two boards. `Won` is the SOL credited for that single bet (owner, 2026-09-02);
    /// it is DISPLAY ONLY and never participates in the ordering — see §6.3.4.</summary>
    public DateTime? AchievedAt { get; set; }
    public string? GameTypeName { get; set; }
    public decimal? Won { get; set; }

    /// <summary>
    /// A copy with the caller-specific fields cleared. ⚠️ Cached rows MUST be cloned before IsYou is
    /// stamped — the identical rule GameHistoryDto.ClonePublic() exists for. Stamping a shared cache
    /// entry in place poisons it with one caller's identity for every subsequent reader, and this
    /// codebase has already shipped that bug once on the history endpoint.
    /// </summary>
    public LeaderboardRowDto ClonePublic() => …;
}
```

⚠️ **`Score` on the hall of fame is the MULTIPLIER, and `Won` is the amount** — two different numbers
in two different columns, never conflated. `Won` exists because the owner asked the board to state what
was actually won (decision 9); it is display-only, and the ordering reads `Score`. ⚠️ That amount is
exactly why the board is floored at the cutover — if `Won` is ever removed again, the exemption may be
reinstated, and not before.

⚠️ **No `ReferralCode` and no partner field anywhere on this response.**

### 6.7 Errors and coercion

| Input | Result |
| --- | --- |
| `parametersId` absent / blank | All active types |
| `parametersId` unknown, inactive, or malformed | `ErrorMessages.ParameterNotFound` — the same vague message `GetGameTypeStatsHandler` and `GetGamesHistoryHandler` return. Existence and status are never revealed. |
| `size` out of `[1,25]` | Clamped, **200** |
| `wallet` absent / blank / unknown / never bet | 200, boards populated, `you: null` on each |
| The last 7 days are empty for this filter | 200, the board falls back to the most recent week with results and sets `window.isFallback` with the real `from`/`to` (§3 decision 22) |
| A filter that has NEVER had a completed round | 200, that board's `Rows` is **empty** — the genuine empty state, never a row of zeros and never an error |

### 6.8 Caching

```csharp
// The three PUBLIC board slices for one filter. 120s, matching GenerateGlobalStatsCacheSettings —
// these are the same aggregates over the same rows, and the same staleness argument applies.
//
// ⚠️ Keyed on the FILTER ONLY (type + size). The caller's wallet is deliberately NOT in the key: the
// public slice is identical for everyone, and keying by wallet would mint one entry per address on a
// public endpoint — the unbounded-key-growth trap specs/games-history.md documents for page numbers.
// The caller's own rank is joined LIVE on every request instead, and the cached rows are CLONED
// before IsYou is stamped.
//
// The 120s TTL also bounds the rolling window's boundary drift, exactly as it does for global stats.
public static CacheSettings GenerateLeaderboardsCacheSettings(Guid? parametersId, int size)
    => new($"Leaderboards:{parametersId?.ToString() ?? "all"}:{size}", TimeSpan.FromSeconds(120));
```

No invalidation: the TTL bounds it, and there is no write path to hook. A just-completed round can lag
the public slice by up to 120 s — accepted, and identical to the games-history trade-off. **The
caller's own rank never lags**, because it is not cached.

### 6.9 Rate limiting

**No new rule.** `RateLimitSettingsTests.GeneralRules_AreExactlyTheThreeQuotaProtectingPatterns` pins
the rule set to `*:/rpc`, `*:/solana/*`, `*:/solana/balance/live`; every other endpoint is
deliberately unlimited app-side. `/leaderboards` joins `/players/stats` and `/games/*/history` as an
unlimited read behind Cloud Armor's per-IP rule. ⚠️ Adding a rule here would break that exact-set test
by design — do not.

---

## 7. Frontend behaviour

### 7.1 Route and placement

- New route `Leaderboard` at **`/leaderboard`** in `src/routes.ts`.
  - ⚠️ **Unlisted in the ☰ pad**, with the same comment `Partner` carries: the pad is a 3-column
    nine-cell grid and a tenth cell would start a fourth row the grid can never fill.
  - Reached from the home-page section's "view full leaderboard" link and from a link on
    `/game/stats`.
  - **Not** an `isPlayerRoute()` in `App.vue` — it renders no display name of its own and its state
    comes from `useLeaderboards`. Matches `/orbit`, `/partner` and `/responsible-gaming`.
- New flag `hallOfFameEnabled` in **`src/constants/featureFlags.ts`**, beside `orbitEnabled`:
  `export const hallOfFameEnabled = process.env.VUE_APP_HALL_OF_FAME_ENABLED === 'true';`
  ⚠️ **Fail-closed by construction** — the `=== 'true'` test is what makes empty, absent, `"false"` and
  a typo all resolve to OFF, which is the discipline `orbitEnabled` and `token.ts` already document.
  `VUE_APP_HALL_OF_FAME_ENABLED=false` goes in `.env.example` with a comment, and a `frontBuild` key in
  `deploy/secrets.example.json`. ⚠️ `VUE_APP_*` is baked in at **BUILD** time — switching it on is a
  frontend rebuild + redeploy, not a config toggle.
- Add `'Leaderboard'` to **`EMBED_REDIRECT_ROUTES`** in `src/main.ts`. ⚠️ `'Home'` is **already** in
  that set, so the home-page section needs no separate embed guard — do not add a redundant
  `isEmbedded` check there.

### 7.2 The home-page section

- New `src/components/home/HomeLeaderboard.vue`, rendered in `HomePage.vue` **between
  `<home-player-stats>` and `<home-floor>`** — the page's beat is act → learn → trust → capability →
  *your* numbers → **everyone's ranking** → floor.
- Built on `HomeSection.vue`. ⚠️ It **must** pass a `title` — the prop is optional only because of the
  token card's `header` slot, and an untitled panel is a bug, not a variant. `tone: 'line'` (it is an
  informational panel, not a player-scoped one; cyan is reserved).
- Shows the top **10**, with a link through to `/leaderboard`.
- ⚠️ It does **not** gate on `connected`, unlike `<home-player-stats v-if="connected">`: a leaderboard
  is worth seeing before you have a wallet. The caller's own pinned row simply does not render.

### 7.3 The carousel

**No carousel component exists in the app today** — this is the first. It goes in
`src/components/leaderboard/` and is shared by the home section and the page:

| File | Role |
| --- | --- |
| `LeaderboardCarousel.vue` | The slides + the chip control. Default slide = **Gamble race**. ⚠️ Renders **two** chips unless `hallOfFameEnabled`. |
| `LeaderboardBoard.vue` | **One generic board**, used by all three: explainer line, the stepped-plinth top three, the column header strip, the list, and the pinned `YOU` row. Takes a per-board config (see below) — there is no per-board component. |
| `LeaderboardPlinth.vue` | One plinth: rank numeral, name, figure, meta line. Height comes from its rank. |
| `LeaderboardRow.vue` | One list row: rank, identity, score. `IsYou` takes the same treatment as the `YOU` label elsewhere. |
| `LeaderboardTypeFilter.vue` | All types / one type, from `availableTypes`. Defaults to All. |

The per-board config is a small literal map — the only thing that differs between the three:

| Board | Accent | Explainer | Column header | Plinth meta line |
| --- | --- | --- | --- | --- |
| Gamble race | `--color-primary` | "Ranked by total SOL gambled in the last 7 days" | `GAMBLED` | `SOL GAMBLED` / `<n> BETS` |
| Weekly PNL | `--color-live` / `--color-magenta` by sign | "Ranked by net profit — won minus gambled — in the last 7 days" | `NET` | `SOL` / `<in> IN · <out> OUT` |
| Hall of Fame | `--color-cyan` | "Ranked by biggest win multiplier — payout divided by your own stake — all time" | `MULTIPLIER` | `<game type>` / `<date>` |

- Swiping between slides costs **no request** — all three arrive in one response (decision 14).
- Changing the type filter refetches once.
- Identity: `displayName` when set, otherwise a truncated `wallet` — the fallback every identified
  surface already uses.
- Money renders **token-native** through the shared `useAmountFormatter` / `formatSol`.
  ⚠️ **The Hall of Fame slide renders `×n` and no amount, ever** — read `scoreIsMultiplier` and never
  append the token symbol on that board.
- **The window badge** (`LeaderboardWindowBadge.vue`) renders beside the type filter from
  `board.window`: `LAST 7 DAYS` when `kind == "week"` and not a fallback, `ALL TIME` when
  `kind == "all"`, and the real `from–to` dates when `isFallback`. ⚠️ **Caption from the response,
  never from what was requested** — the same rule `GlobalStatsDto.Window` already carries. On a
  fallback the board also renders a strip above the plinths: *"No rounds this week — showing the most
  recent week that had results."*
- **Money carries the SOL mark** — the existing `src/assets/sol.svg`, sized in `em` beside the figure.
  ⚠️ It is a **brand asset, not a pixel glyph**: it does not go in `constants/icons.ts` (which says so
  explicitly) and it renders at `image-rendering: auto` while the arcade face crisps everything else.
  The Hall of Fame's multiplier takes **no** mark — it is a ratio, not money.
- ⚠️ **No bare figures.** Every board carries a one-line explainer under the head, a **meta line under
  each plinth figure** naming the unit, and a **column header strip** over the list — `GAMBLED`, `NET`,
  `MULTIPLIER`. The PNL plinths additionally show the working (`1.84 IN · 2.78 OUT`) because a net
  figure without it is unreadable (§3 decision 21).
- Empty board → the `> no rounds yet` empty state, never a row of zeros.

### 7.4 Data

- New `src/hooks/useLeaderboards.ts` — fetch on mount and on `visibilitychange` focus. **No SignalR
  group is joined and no interval is set** (decision 17).
- New call in `src/services/mainService.ts`, following `getGlobalStats` / `getPlayerStats`: wrapped in
  `singleFlight`, and ⚠️ **deliberately not in `runWithRecovery`** — a leaderboard failure renders
  inline (`> leaderboard offline — retry`) and must never route the front door to `/error`, the same
  reasoning `getPlayerStats` carries.
- New types in `src/types/` mirroring §6.6.
- New `leaderboard` entry in `src/constants/icons.ts` (every page and feature carries an identity
  mark), rendered by `ui/AppIcon.vue`. ⚠️ Display-face copy carrying `$` or `!` goes through
  `ui/PixelText.vue`; `×` is safe in both faces.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
    participant SPA as monkify-front
    participant API as SpaceStroker.Api
    participant R as Redis
    participant PG as Postgres

    SPA->>API: GET /leaderboards?parametersId&wallet&size
    API->>API: GetAllActiveGameParameters() (12h cache)
    alt parametersId set and not active
        API-->>SPA: 400 ParameterNotFound (vague)
    end
    API->>PG: resolve wallet -> Player.Id (null is fine)
    API->>R: Leaderboards:{type}:{size}
    alt hit
        R-->>API: three public board slices
    else miss
        API->>PG: gamble race  (7d, cutover floor, GROUP BY player, LIMIT size)
        API->>PG: weekly PNL  (7d, cutover floor, GROUP BY player, LIMIT size)
        API->>PG: hall of fame (ALL TIME, no cutover, GROUP BY player, LIMIT size)
        API->>PG: hall of fame detail for the <=25 top ids
        API->>R: store (120s)
    end
    API->>API: clone rows, then stamp IsYou
    opt caller resolved and outside the slice
        API->>PG: own score + count of players above (per board)
    end
    API-->>SPA: three boards + applied filter + you
```

---

## 9. Out of scope

- **Any prize, pool, payout or funding model.** No `LeaderboardSeason`, no `LeaderboardAward`, no
  prize wallet, no payout consumer, no solvency guard. A prize is its own card.
- **Calendar weeks, seasons and freezes** — and any timezone or week-start decision.
- **A per-referrer / affiliate board** — blocked on the referral program and its own concern.
- **Anti-farm caps, qualification thresholds, one-prize-per-entity rules** — only meaningful with a
  prize attached.
- **A ☰ menu cell**, and any restructuring of the 3×3 icon pad.
- **An opt-out preference** (`Player.HideFromLeaderboard`).
- **SignalR / live-updating boards.**
- **Card #178** — refined standalone by owner decision; not merged into this card.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/SpaceStroker.Api/Controllers/LeaderboardsController.cs` | **New.** `GET /leaderboards` + `[HttpOptions]` preflight; clamps `size` at the edge |
| 2 | `src/SpaceStroker.Infrastructure/Handlers/Leaderboards/GetLeaderboards/GetLeaderboardsRequest.cs` | **New.** `(Guid? ParametersId, string? Wallet, int Size)` |
| 3 | `src/SpaceStroker.Infrastructure/Handlers/Leaderboards/GetLeaderboards/GetLeaderboardsHandler.cs` | **New.** Active-type resolve → vague `ParameterNotFound` → caller resolve → aggregate → map |
| 4 | `src/SpaceStroker.Infrastructure/Services/Leaderboards/ILeaderboardService.cs` | **New.** `GetLeaderboards(typeIds, parametersId, callerPlayerId, size, ct)` |
| 5 | `src/SpaceStroker.Infrastructure/Services/Leaderboards/LeaderboardService.cs` | **New.** The three bounded aggregates, the cutover asymmetry, eligibility, own rank |
| 6 | `src/SpaceStroker.Infrastructure/Services/Leaderboards/HouseWallets.cs` | **New.** Resolves the four configured public keys; skips blank/`MOCK_` |
| 7 | `src/SpaceStroker.Infrastructure/Services/Leaderboards/Leaderboards.cs` · `LeaderboardEntry.cs` · `LeaderboardWindow.cs` | **New.** Plain result models (the `GlobalStats` shape); `LeaderboardWindow(From, To, IsFallback)` is what `ResolveWeeklyWindow` returns |
| 8 | `src/SpaceStroker.Infrastructure/Contracts/Leaderboards/LeaderboardsDto.cs` · `LeaderboardBoardDto.cs` · `LeaderboardWindowDto.cs` · `LeaderboardRowDto.cs` · `LeaderboardGameTypeDto.cs` | **New.** Response DTOs + `ClonePublic()`; the row carries `Won` for the hall of fame |
| 9 | `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` | Add `GenerateLeaderboardsCacheSettings(parametersId, size)` — 120 s, filter-keyed |
| 10 | `src/SpaceStroker.Infrastructure/DependencyInjection.cs` | Register `ILeaderboardService` |
| 11 | `src/SpaceStroker.Api/Program.cs` (CORS/route wiring only if the controller needs it) | Verify the new controller is covered by the existing convention; no new policy |

**No migration. No `SpaceStrokerDbContext` change. No `SpaceStroker.Consumers` change.**

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/pages/LeaderboardPage.vue` | **New.** Top-25 carousel + type filter |
| 2 | `src/components/leaderboard/LeaderboardCarousel.vue` · `LeaderboardBoard.vue` · `LeaderboardPlinth.vue` · `LeaderboardRow.vue` · `LeaderboardTypeFilter.vue` · `LeaderboardWindowBadge.vue` | **New.** ONE generic board driven by a per-board config map (§7.3); the badge captions from `board.window`, including the fallback dates |
| 3 | `src/components/home/HomeLeaderboard.vue` | **New.** Top-10 `HomeSection` (must pass `title`), links through |
| 4 | `src/pages/HomePage.vue` | Render `<home-leaderboard>` between player stats and the floor |
| 5 | `src/hooks/useLeaderboards.ts` | **New.** Fetch on mount + `visibilitychange`; no hub, no interval |
| 6 | `src/services/mainService.ts` | `getLeaderboards(parametersId?, wallet?, size)` — `singleFlight`, **not** `runWithRecovery` |
| 7 | `src/types/index.ts` | `Leaderboards`, `LeaderboardBoard`, `LeaderboardRow`, `LeaderboardGameType` |
| 8 | `src/routes.ts` | `Leaderboard` → `/leaderboard`, with the unlisted-route comment |
| 9 | `src/main.ts` | Add `'Leaderboard'` to `EMBED_REDIRECT_ROUTES` |
| 10 | `src/constants/icons.ts` | New `leaderboard` pixel mark. ⚠️ The SOL mark is **not** added here — `src/assets/sol.svg` is reused as-is, per its own comment |
| 11 | `src/pages/GameStatsPage.vue` | A link through to `/leaderboard` (the second entry point) |
| 12 | `src/constants/featureFlags.ts` | **New flag** `hallOfFameEnabled` — fail-closed `=== 'true'`, beside `orbitEnabled` |
| 13 | `.env.example` | `VUE_APP_HALL_OF_FAME_ENABLED=false` + the "build-time, rebuild to switch on" comment |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/leaderboards.md` | **New.** This document |
| 2 | `INDEX.md` | Map row + "Feature currently in flight" |
| 3 | `deploy/secrets.example.json` | A `frontBuild` key for `VUE_APP_HALL_OF_FAME_ENABLED` (mock/placeholder only) |
| 4 | `features/leaderboards.md` | **At ship time** (`/monkify-ship-feature`) — `Level: Feature`; must record that the Hall of Fame ships DARK |
| 4 | `features/legal.md` | **At ship time** — record that self-excluded players are hidden from the boards |

---

## 11. Tests

### 11.1 Unit tests — `monkify-backend/src/SpaceStroker.Tests`

Run with `scripts/run_tests.bat` (never the full `dotnet test` suite — `ProductionSessions` is a slow
Monte Carlo suite).

| Test file | Test cases | Expected |
| --- | --- | --- |
| `UnitTests/Services/LeaderboardServiceTests.cs` | Gamble race ranks by summed paid stake, desc | Highest staker first |
| | Weekly PNL returns a **negative** score for a losing player | Not clamped, ranked below zero |
| | Weekly PNL keeps a player who won nothing on the board | The `DefaultIfEmpty` left join — an inner join would drop them |
| | Hall of Fame returns **one row per player**, their max multiplier | A player with three big wins appears once |
| | Hall of Fame includes a pre-`SettlementCutoverUtc` win | Cutover-exempt (decision 9) |
| | Gamble race and PNL **exclude** pre-cutover rounds | Cutover floor applied |
| | Free bets (`Amount == 0`) contribute to neither weekly board | Score unchanged |
| | Rounds not `RewardForWinnersCompleted` contribute nothing | Refunded / under-subscribed / errored / abrupt |
| | Ties resolve by earliest qualifying bet, and twice identically | Deterministic |
| | Own rank uses the **same** comparison as the ordering | Pinned rank matches list position |
| | Own rank is null for a caller who never bet | Not an error |
| `UnitTests/Services/LeaderboardWindowTests.cs` | A week with data returns that week, `IsFallback` false | The normal path |
| | An empty week falls back to the 7 days ending at the newest round | The core of decision 22 |
| | The fallback anchor is the newest round even when its `EndDate` is NULL | The two-MAX rule, not a COALESCE |
| | The fallback window is clamped at the cutover — `anchor − 7d` never reaches past it | Or a quiet week sums two currencies |
| | A filter that has never had a round returns empty with `IsFallback` false | The genuine empty state |
| | The fallback resolves **per filter** — a quiet type falls back independently of the deployment | Decision 22 |
| `UnitTests/Services/LeaderboardHallOfFameTests.cs` | Ranks on the multiplier, not on the amount won | A 10× ticket price must not decide the board |
| | The row carries the SOL won for **that** bet | Decision 9's amount |
| | Pre-cutover wins are **excluded** now that the board states an amount | ⚠️ The reversal of the old exemption |
| `UnitTests/Services/LeaderboardEligibilityTests.cs` | The house seed wallet is excluded | Absent from all three boards |
| | Treasury / Orbit / referral-payout wallets are excluded | Absent |
| | A changed `HouseSeed:PublicKey` is what gets excluded | Reads config, not a literal |
| | Blank and `MOCK_` public keys are **skipped**, not added to the filter | The list holds only real addresses |
| | A currently self-excluded player is excluded | Absent from all three |
| | A player whose exclusion has **lapsed** is included | `SelfExcludedUntil <= now` |
| | A **permanently** excluded player (`SelfExcludedUntil == null`) is excluded | The two-column rule |
| | ⚠️ The inline predicate matches `Player.IsSelfExcluded(now)` for every combination | Pins the hand-kept pair (§6.3.1) |
| `UnitTests/Handlers/GetLeaderboardsHandlerTests.cs` | Unknown / inactive `parametersId` → vague `ParameterNotFound` | Nothing reveals existence |
| | Blank `parametersId` → all active types | |
| | Blank / unknown wallet → 200 with `you: null` | Never 400, never 404 |
| | `size` out of range is clamped, not rejected | |
| | The response **echoes the applied** `parametersId` | The client captions from it |
| | The response carries **no `ReferralCode`** and no partner field | Contract check |
| `UnitTests/Contracts/LeaderboardRowDtoTests.cs` | `ClonePublic()` clears `IsYou` / `Rank` | The cache-poisoning guard |
| | A hall-of-fame row exposes **no money field** | Decision 9's hard constraint, structurally |

⚠️ **The Hall-of-Fame flag is FRONTEND-ONLY**, so no backend test asserts on it: the endpoint keeps
returning all three boards whatever the flag says. That is deliberate (the flag was specified as a
frontend flag), and the cost is that phase 1 pays for one aggregate nobody sees — bounded, cached for
120 s with the other two, and it makes switching the board on a rebuild rather than a deploy of both
hosts. `monkify-front` has no test runner, so the flag is covered by manual QA step 8 instead.

### 11.2 Integration test — `SpaceStroker.Tests.Integration/LeaderboardsFlowTests.cs`

**Mandatory** (`AGENTS.md` rule 8 — `Level: Feature`). Follow `GlobalStatsFlowTests` /
`PlayerStatsFlowTests` for the fixture shape.

**Happy path, end to end through two real hosts:** seed several completed rounds with paid bets from
three player wallets → `GET /leaderboards` → assert all three boards rank the players in the expected
order, with the expected scores, and that the applied filter is echoed.

Plus, in the same file:

1. **The house wallet is absent** from all three boards, even though it bet in every seeded round.
2. **A self-excluded player is absent** from all three boards.
3. **The Hall of Fame carries no amount** — assert on the serialized payload, not just the DTO.
4. **The caller's own rank** comes back pinned for a wallet outside the top slice, and `null` for a
   wallet that never bet.
5. **Two different wallets** hitting the same filter (second served from cache) get responses that do
   **not** carry each other's `IsYou` — the clone-before-stamp guard.

⚠️ **Harness facts worth knowing before extending these** (from `features/referral-program.md`):
seeding a `Player` through a handler's own in-memory context throws an EF identity conflict — seed
through a separate context, as `OrbitHandlerTestsBase` does; and `IntegrationTestFixture` configures
settings via `GeneralSettings__…` env vars **before the hosts build**, which is how the test supplies
a house-seed public key to exclude.

**`monkify-front` has no test runner** — `lint` + `build` only.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist (23 items).

1. Given completed rounds in the last 7 days, when a player opens the Leaderboard, then the Gamble race is the default slide and ranks players by total paid stake descending over that rolling window.
2. Given a player whose credits are less than their stakes over the window, when the Weekly PNL board is read, then their net is ranked as a negative value and is never clamped to zero.
3. Given a player with several big wins, when the Hall of Fame is read, then they appear exactly once with their single best multiplier rendered as `×n`, and no SOL amount appears in the row or anywhere in that board's payload.
4. Given rounds that completed before `SettlementCutoverUtc`, when the Gamble race or Weekly PNL board is read, then those rounds contribute nothing to either total.
5. Given a winning bet in a round that completed before `SettlementCutoverUtc`, when the Hall of Fame is read, then its multiplier is included in the ranking, because the ratio carries no currency.
6. Given the house seed wallet has placed a bet in every round, when any of the three boards is read, then the house wallet appears on none of them.
7. Given the house seed wallet is changed in `GeneralSettings`, when any board is read, then the newly configured address is excluded and no address is special-cased outside that configuration.
8. Given a bet placed by the Orbit engine on a player's behalf, when the boards are read, then it counts toward that player's row and the Orbit custody account never appears as an entry.
9. Given a player whose self-exclusion is currently active, when any of the three boards is read, then that player is absent from all of them; and given the exclusion has lapsed, when the boards are next read uncached, then they appear again.
10. Given a round that was refunded or ended `NotEnoughPlayersToStart`, `ErrorWhenProcessingRewards` or `GameEndedAbruptely`, when any board is read, then its bets contribute nothing to any ranking.
11. Given a free bet with `Amount == 0`, when the Gamble race or Weekly PNL board is read, then it contributes nothing to either total.
12. Given a ranked player with a `DisplayName`, when a board renders, then the display name is shown; and given a ranked player with none, then a truncated wallet address is shown instead.
13. Given two players with an identical score on a board, when that board is read twice, then both reads return them in the same order, tied on earliest qualifying bet and never on wallet string or at random.
14. Given a connected wallet that ranks outside the visible slice, when a board is read, then the caller's own row is returned with their true rank and is pinned below the list.
15. Given no wallet, an unknown wallet, or a wallet that has never bet, when the leaderboards are read, then HTTP 200 is returned with the boards populated and the caller's own rank null on each — never a 400 or a 404.
16. Given a `parametersId` that does not exist or has been deactivated, when the leaderboards are read, then the same vague `ParameterNotFound` is returned and nothing in the response reveals whether that type exists.
17. Given an unrecognised filter value, when the leaderboards are read, then HTTP 200 is returned with the default filter applied and the response echoes the filter that was actually applied, which is what the client captions from.
18. Given a game type with no qualifying completed rounds, when its board is read, then an empty state is rendered rather than a row of zeros or an error.
19. Given two different wallets request the same filter and the second is served from cache, when both responses are compared, then neither carries the other caller's own-rank row, proving the cached rows were cloned before stamping.
20. Given the Leaderboard is open, when the page is inspected, then no SignalR group has been joined and no polling interval is running; the data refreshes on mount and when the tab regains focus.
21. Given the app is running embedded in an iframe, when the home page renders, then the leaderboard section is absent and `/leaderboard` redirects like the other embed-excluded routes.
22. Given the leaderboard section on the home page and the `/leaderboard` page, when both render for the same filter, then the home section shows the top 10 and the page shows the top 25, and the header menu still has exactly nine cells.
23. Given any leaderboard response, when it is inspected, then it carries no `ReferralCode` and no referral-partner information of any kind.
24. Given `VUE_APP_HALL_OF_FAME_ENABLED` is unset, empty, `false` or misspelled, when the leaderboard renders, then the carousel shows exactly **two** chips, the Hall of Fame slide does not exist, and no route or link reaches it.
25. Given `VUE_APP_HALL_OF_FAME_ENABLED=true` in the build, when the leaderboard renders, then a third chip appears and the Hall of Fame slide is reachable.
26. Given any board renders, when a figure is shown, then it is labelled — an explainer line under the head, a `SOL GAMBLED` / `SOL` / game-type meta line under each plinth figure, and a column header naming the figure (`GAMBLED` / `NET` / `MULTIPLIER`).
27. Given all three boards render, when their layouts are compared, then they are the same stepped-plinth layout, differing only in accent colour, explainer, column header and plinth meta line.
28. Given the last 7 days contain no completed round for the applied filter, when a weekly board is read, then it returns the 7 days ending at the most recent qualifying round, flags the response as a fallback, and the badge shows those real dates rather than "LAST 7 DAYS".
29. Given a filter that has never had a completed round, when a weekly board is read, then it returns empty with no fallback flag — the genuine empty state.
30. Given a fallback window whose anchor is less than 7 days after `SettlementCutoverUtc`, when the board is read, then the window is clamped at the cutover and no pre-cutover round contributes.
31. Given the Hall of Fame renders, when a row is inspected, then it shows the multiplier as the rank and the SOL won beside it, and a pre-cutover win does not appear at all.
32. Given the Hall of Fame is read, when a player with a lower multiplier but a larger payout exists, then they rank **below** the higher multiplier — the board never orders by the amount.
33. Given any money figure renders on any board, when it is inspected, then it carries the `sol.svg` brand mark; and the Hall of Fame's multiplier carries no mark, because it is a ratio and not money.

---

## 13. Manual QA

1. Bring up the local stack (`setup_local_environment.bat`) and fill a few rounds — `/fill-crypto-clash`
   is the fastest way to get completed rounds with several distinct players.
2. Open `/` → the leaderboard section renders below your own record, showing the top 10, Gamble race
   selected.
3. Swipe/tab across all three slides — **no network request fires** (check devtools). Change the type
   filter — exactly one request fires.
4. `GET /leaderboards` directly and confirm: the house seed wallet is absent, `parametersId` is echoed,
   and the `hallOfFame` rows carry no amount field.
5. Self-exclude a wallet via `/responsible-gaming`, wait for the 120 s TTL, reload — that wallet is
   gone from all three boards.
6. Open `/leaderboard` with a connected wallet ranked below 25 — your row is pinned below the list
   with a rank matching where you would appear.
7. Load the app embedded (`?embed=1` or an iframe) — the home section is gone and `/leaderboard`
   redirects to the lobby.
8. **The quiet-week fallback.** Stop placing bets and move the local clock forward (or backdate the
   seeded rounds) so nothing falls inside the last 7 days. Reload: the board must still show results,
   the badge must show the real dates, and the "no rounds this week" strip must appear. Then filter to
   a game type that HAS had a round this week and confirm the badge flips back to `LAST 7 DAYS` —
   the fallback is per filter.
9. **The flag.** With the default build, confirm the carousel shows **two** chips and nothing reaches the
   Hall of Fame. Then rebuild the front with `VUE_APP_HALL_OF_FAME_ENABLED=true` and confirm a third chip
   appears. ⚠️ `VUE_APP_*` is baked in at build time — restarting the container is not enough, the
   frontend image has to be rebuilt.

---

## 14. Trello / pipeline

- **Source card:** [Leaderboards (#182)](https://trello.com/c/80OsGlGn/182-leaderboards) — list
  **Specs created**; the **Implementation** section on the card carries the branch, worktree, spec
  path and `Level`.
- **Worktrees:** `.worktrees/leaderboards/{monkify-backend,monkify-front,monkify-meta}`, branch
  `feature/leaderboards`, pushed to `origin`.
- **Next:** `/monkify-implement-feature 182` — ⚠️ run `/design-login` first, the 5 carousel mockups
  are a prerequisite of implementation (decision 18).
- **After merge:** `/monkify-ship-feature 182`.

---

## 14b. Implementation findings

Three things the build turned up that the design did not predict. All are recorded because each was
a *silent* failure — the code compiled and read correctly in every case.

1. ⚠️ **`DefaultIfEmpty()` over a PROJECTED type does not null-check.** The PNL board was specced as a
   left join onto a per-bet payout projection with a `payout == null` guard. That guard never fires:
   the provider yields a **non-null instance whose members are null**, so reading `payout.Won` throws
   *"Nullable object must have a value"*. Both payout lookups are now **correlated subqueries**
   (`.Sum(log => (decimal?)log.Amount) ?? 0m`), which have no empty side to guard. ⚠️ Do not
   "simplify" them back into a left join.
2. ⚠️ **Joining on `bet.PlayerId.Value` evaluates `.Value` on nulls.** `PaidBetsIn` filters
   `PlayerId != null` first, but the provider does not reliably order that filter before the key
   selector. Every join now goes through the **nullable side** (`bet => bet.PlayerId` against
   `player => (Guid?)player.Id`).
3. ⚠️ **Stakes are FIXED per game type**, so a gamble race cannot be separated by bet size — a player's
   total is `stake × rounds played`. The first integration test varied `Amount` to tell two players
   apart and every bet was refused with *"Requested bet amount is invalid for this game"*. The test
   now drives **two rounds** instead.

Two naming collisions worth knowing about, both compile-time: `LeaderboardWindowDto`'s factory is
`Of(...)` because `From` is already a property on it, and the `Leaderboards` service model must be
**aliased** inside `Contracts.Leaderboards` because the namespace's own last segment shadows the type.

One deviation from §6.6 as written: **`ClonePublic()` lives on `LeaderboardEntry`** (the service
model), not on the row DTO. That is where the cached object actually is, so it is the only place the
clone can protect anything — a DTO-level clone would have been dead code.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-09-02 | Owner rejected the word **"wager"** throughout. Renamed to **gamble** in every user-visible string AND in the wire contract (`wagerRace` -> `gambleRace`, `LeaderboardBoard.WagerRace` -> `GambleRace`), which is free precisely because nothing outside this branch consumes the endpoint yet. Two neighbouring surfaces were relabelled for consistency — `HomePlayerStats` and `GameStatsPage` sit beside the board and said "Wagered". ⚠️ The API FIELD names `totalWagered` / `totalVolumeWagered` are deliberately NOT renamed: they are shipped contract from the stats features and out of scope here. |
| 2026-09-02 | **Implemented** on `feature/leaderboards` — 1196 unit + 152 integration green. §14b records the three silent failures the build turned up (the `DefaultIfEmpty` null-check, the `.Value` join key, and fixed stakes defeating the first integration test) and the one deviation from §6.6 (`ClonePublic` sits on the service model, not the DTO). |
| 2026-09-02 | Design **approved**. Folded in: the window is a **badge** not a caption and money carries the **`sol.svg` mark** (decision 23); the Hall of Fame is **all-time but floored at the cutover** and now states the **SOL won** beside the multiplier, which **voids** the old cutover exemption (decision 9 rewritten); and a **quiet week falls back** to the most recent week with results rather than rendering empty (decision 22, new `ResolveWeeklyWindow` in §6.3.1, `LeaderboardWindowDto` in §6.6, AC 28–33). |
| 2026-09-02 | Second design pass: the per-board champion band was **rejected** — all three boards take the SAME stepped-plinth layout, so the board is one generic component driven by a per-board config map (decision 20 rewritten, §7.3 config table added, §10.2 and AC 26–27 updated). |
| 2026-09-01 | Owner review of the design mockups: Hall of Fame **flagged off for phase 1** (decision 19), per-board top-three treatments (decision 20, since reversed), and **no bare figures** — explainer line + column headers (decision 21). §1, §7, §10, §11, §12, §13 updated to match. |
| 2026-09-01 | Initial spec from the refined card (#182) + the refinement conversation. Code touchpoints verified against `GlobalStatsService`, `PlayerStatsService`, `StatsWindowExtensions`, `CacheHelper`, `Player`, the four `*Settings` public keys, `routes.ts` and `main.ts`. |
