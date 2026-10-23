# Feature — Leaderboards

> Status: **shipped.** Authoritative spec: [`../specs/leaderboards.md`](../specs/leaderboards.md)
> (merged via `feature/leaderboards` on 2026-09-02, Trello
> [#182](https://trello.com/c/80OsGlGn/182-leaderboards)).
>
> ⚠️ **Two of the three boards ship DARK.** Only the gamble race is visible; see "What actually
> renders" below before assuming a board is broken.

## What it does

Ranks players against each other for the first time. Three read-only boards in one carousel — a
weekly **gamble race** (Σ paid stake), a weekly **PNL** board (net, signed), and an all-time
**hall of fame** of biggest win multipliers — on a section of the home page and on a shareable
`/leaderboard` route. The two weekly boards **reset every Wednesday 15:00 UTC**.

⚠️ **It moves no money and writes nothing.** A prize pool was scoped out deliberately: there is no
season table, no prize wallet, no payout consumer and no solvency guard. Adding one is a new card.

## Core rules

### What counts

- Only rounds that reached **`RewardForWinnersCompleted`** count. Refunded, `NotEnoughPlayersToStart`,
  `ErrorWhenProcessingRewards` and `GameEndedAbruptely` contribute nothing.
- **Free bets (`Amount == 0`) are excluded** from every figure.
- **Space Jackpot holder tickets are excluded too** (#196). A holder ticket is amount 0, so the same
  `Amount > 0` filters drop it from the race, PNL and the hall of fame — a holder's jackpot win adds
  nothing to wagered, won or any multiplier. No code changed for this; it is **pinned by tests**
  (`LeaderboardServiceTests`, `LeaderboardHallOfFameTests`). ⚠️ Do not replace an `Amount > 0` filter
  with `!IsFreeBet` — `IsFreeBet` is **false** for a holder ticket, and its SOL credit would enter the
  boards with a zero stake. See [`space-jackpot-refactor.md`](space-jackpot-refactor.md).
- **`SettlementCutoverUtc` floors every board** — including the hall of fame; see below.
- **Streamer play bets are excluded** (#201, [`streamer-wallets.md`](streamer-wallets.md)). They are
  `Amount > 0`, so the amount filter does not catch them: `PaidBetsIn` and `WinningBets` add
  `EntryKind != Streamer` beside the unchanged `Amount > 0` — which covers the gamble race, weekly PnL,
  the hall of fame **and** the caller's own row (`RankOfAsync` reads the same queryables). Keyed on the
  bet's `EntryKind`, never the configured wallet list, so a later-deactivated streamer's play bets stay out.
- **An ACTIVE streamer wallet is excluded as a player too** (#201 follow-up, 2026-10-07). `EligiblePlayers`
  adds `!StreamerWallets.Any(s => s.Active && s.Wallet == player.ExternalPlayerId)` beside the house-wallet
  and self-exclusion predicates (one SQL `NOT EXISTS`, proven by `LeaderboardsFlowTests`), so the wallet's
  **real-money** bets — e.g. ones placed before it was registered — rank nothing and it gets no own row
  while it streams. Deactivate it and it is an ordinary player again (its paid bets rank; its play bets
  never do, per the bullet above).
- **Orbit bets count.** They are ordinary paid bets on the real player's `Player` row, so they need no
  special handling. ⚠️ The **Orbit custody account is never a leaderboard entity** — it signs, it does
  not own the bet.
- **No minimum participation.** One qualifying bet is enough on any board.

### Who is excluded

- ⚠️ **House-controlled wallets are excluded by CONFIGURED PUBLIC KEY, never by a marker column.** All
  four accounts (`TokenOwnerPublicKey`, `Orbit`, `HouseSeed`, `ReferralPayout`) already publish one, so
  this needs no keypair derivation and no migration. The **house seed** wallet is the one that matters:
  it bets in every round on every type and would otherwise own the top of the gamble race outright.
  A `Player.IsHouse` column was rejected — it would end the seeded bet's defining property of having
  "no marker column, no flag, no branch anywhere" (`games.md` §14). Blank and `MOCK_` values are
  skipped, so a dev stack carries no meaningless literals.
- ⚠️ **Self-excluded players are hidden from all three boards while the exclusion is active**, and
  reappear when it lapses. Putting a self-excluded person's name on a gambling leaderboard uses their
  identity to promote a product they formally asked to be kept away from. See [`legal.md`](legal.md).
- ⚠️ The eligibility predicate is written **INLINE** and is the exact negation of
  `Player.IsSelfExcluded(now)` — that method is C# EF Core cannot translate, so calling it would throw
  or silently fall back to client evaluation over every `Player` row. A theory test pins the two
  against each other across all four combinations; **keep them in step by hand**.

### The windows — the anchored weekly reset

There are **two modes**, and which one is live depends only on whether an anchor is configured.
Production is anchored; a local box with no override is not.

**Anchored (production).** The two weekly boards rank the slot `{anchor + k*7d}` that contains `now` —
`GeneralSettings:Operator:Leaderboards:WeeklyResetAnchorUtc`, production `2026-09-30T15:00:00Z`, which
is **Wednesday 15:00 UTC** (12:00 BRT). The race restarts on a schedule instead of sliding with the clock.
(It was Friday 15:00 UTC — `2026-09-04T15:00:00Z` — until `feature/leaderboard-history`; the move
re-cut the live week the moment it deployed.)

- ⚠️ **The anchor is a UTC INSTANT, not a weekday + timezone.** Nothing resolves `America/Sao_Paulo` at
  runtime — that would make the boards depend on the tz database being present and identically named in
  the Cloud Run image and on a Windows dev box, for a country with **no DST since 2019**. Only the
  **weekday and time-of-day** matter, not the date. Re-pointing it **re-cuts every live week at once**,
  and also re-labels the UI with no frontend build (the client renders the instant the API sends).
- ⚠️ **`LeaderboardSchedule`'s division must FLOOR, not truncate.** C# integer division rounds toward
  zero, so for an instant *before* the anchor a naive quotient returns a period that has not started —
  a window into the future, both weekly boards silently empty, no error. Not hypothetical: the anchor
  shipped as "today at 12:00 BRT". Five `LeaderboardScheduleTests` cases fail if the floor is removed.
- ⚠️ **THE ANCHORED MODE NEVER FALLS BACK.** A hard reset makes "empty" a true and self-explaining
  state, so a quiet board is shown as quiet and the client says *"no rounds yet this week"*. Falling
  back would show **last week's winners** between the reset and the first bet, then silently swap them
  — hiding the event the reset exists to stage. Cost, accepted: a type nobody played this week shows an
  empty board, not its last active week.
- ⚠️ **`window.resetsAt` is the client's MODE FLAG** — non-null means anchored. Never infer the mode
  from `window.from`.
- ⚠️ **The Redis key carries the period start**, or the reset would land up to a full 120 s TTL late,
  on the most-watched moment the boards have.
- ⚠️ **The integration fixture unanchors the suite** (`SetWeeklyResetAnchor(null)`), because the
  leaderboard tests back-date rounds by a few days: left anchored, whether a 3-day-old round is inside
  "this week" would depend on **which weekday the suite ran** — green Thursday, red Monday. The
  anchored path has its own test that sets the anchor explicitly and restores it in a `finally`.

**Unanchored (null anchor — local, and the integration suite).** The legacy **rolling 7 days**, with
the quiet-week fallback intact:

- If the last 7 days hold no qualifying round **for the applied filter**, the board anchors on the most
  recent qualifying round and shows the 7 days ending there, flagging `window.isFallback` (**only ever
  true in this mode**). Resolved per filter.
- ⚠️ The fallback anchor is the **later of `MAX(EndDate)` and `MAX(CreatedDate)`**, deliberately not
  `MAX(EndDate ?? CreatedDate)` — the COALESCE is unsargable and would forfeit the covering index.
  `CreatedDate <= EndDate` on a completed round, so the later value is only ever the `CreatedDate` when
  the newest round carries a null `EndDate`, which is precisely the row the fallback wants.

**Both modes** are floored at `SettlementCutoverUtc`, or a window would sum two settlement currencies.

### The hall of fame

- **All-time and floored at the cutover — the two are not in conflict.** "All time" means no 7-day
  window. The cutover floor is the money rule every money read obeys, and it applies here **because
  the board states an amount**.
- ⚠️ **An earlier revision EXEMPTED this board from the cutover**, on the grounds that a multiplier is
  a dimensionless ratio. That was sound while the board showed a bare `×n` and is **void now that it
  shows the SOL won** — a pre-cutover amount is a USDC-era figure, roughly a 200× misstatement under a
  SOL mark. **Do not reinstate the exemption without also removing the amount.**
- **Ranked on the MULTIPLIER, never the amount.** Stakes differ by an order of magnitude across modes
  (a Space Jackpot ticket was 10× a Cosmic Sprint one while it was paid — it is free and off the boards
  since 2026-10-01; Hyper Roll and duel stakes still vary), so
  the biggest multiplier is routinely not the biggest payout; ordering by the amount would let ticket
  prices decide the board. The amount is display-only, beside the ratio.
- One row per player — their single best win ever, not one row per winning bet.

### Ordering, identity and the caller's own row

- **Score descending, ties by earliest qualifying bet.** ⚠️ Never by wallet string — a base58 tie-break
  hands a permanent advantage to addresses starting with a low character, forever.
- Identity is `Player.DisplayName`, falling back to a truncated wallet. **No opt-out.** ⚠️ Accepted
  cost: the board makes the biggest wallets discoverable in one click, which per-wallet stats do not,
  and it is the first surface to put a player-chosen name on the home page and a shareable URL —
  display names are **not moderated**.
- The caller's own row is pinned below the list when they rank outside it, with their true rank.
  ⚠️ The rank count must use the **same** comparison as the ordering, or the pinned rank contradicts
  where the player would appear.
- A caller who is a house wallet or currently self-excluded gets **no** own row — the same answer a
  wallet that never bet gets, and the right one.

## The read

**`GET /leaderboards?parametersId=&wallet=&size=`** — public, on the stateless API. One call returns
**all three** boards, which is why swiping the carousel costs no request.

- `size` clamps to `[1,25]`; out of range coerces with **200**, never a 400.
- An unknown or deactivated `parametersId` → the same vague `ParameterNotFound` every type-scoped read
  returns.
- A missing, unknown or never-bet wallet → **200** with `you: null`. Never 400, never 404.
- ⚠️ **No entity, no EF mapping, no index, no migration.** Every figure is a bounded Postgres aggregate
  over existing `Game` / `Bet` / `TransactionLog` / `Player` rows — the same deliberate no-new-table
  decision [`stats-and-history.md`](stats-and-history.md) records.
- ⚠️ Cached in Redis for **120 s keyed on the FILTER ONLY**. The wallet is deliberately absent from the
  key — keying by address on a public endpoint is the unbounded-key-growth trap games-history
  documents. The caller's own rank is joined **live**, and the cached rows are **cloned before
  `IsYou` is stamped** (the poisoning bug this codebase already shipped once on history).
- Carries **no `ReferralCode`** and no partner data.

## What actually renders

⚠️ **Phase 1 ships ONE board.** The weekly PNL and the hall of fame are built end to end — endpoint,
aggregates, tests — but hidden behind fail-closed build-time flags:

| Flag | Default | Effect when off |
| --- | --- | --- |
| `VUE_APP_WEEKLY_PNL_ENABLED` | `false` | slide absent |
| `VUE_APP_HALL_OF_FAME_ENABLED` | `false` | slide absent |

- Both use the `=== 'true'` test `orbitEnabled` uses, so empty, absent, `"false"` and a typo all resolve
  to OFF. ⚠️ **Absence from a `frontBuild` block therefore means OFF** — `WriteFrontEnv` writes only the
  keys it finds and validates nothing, so a deploy whose secrets file omits them is correct, not broken.
  Both are published in `deploy/secrets.example.json` as `"false"` placeholders.
- ⚠️ `VUE_APP_*` is baked in at **BUILD** time — switching a board on is a frontend rebuild, not a
  container restart.
- ⚠️ **The API still computes and returns all three boards** whatever the flags say. Deliberate: the
  flags are frontend-only, which costs one bounded aggregate nobody looks at and buys the ability to
  release a board without touching the backend.
- With only one board left the carousel **drops its chip row entirely** — a one-option control is a
  control that cannot be used, the same call `OrbitGameTab.vue` made about its one-option dropdown.

## Presentation

- **One layout for all three boards**: stepped plinths (2/1/3, height carries rank) over a compact
  list, the caller's row pinned at the foot. What differs is only accent colour, explainer, column
  header and plinth meta line — so there is **one** board component driven by a config map, not three.
  A per-board champion band was drawn, reviewed and rejected.
- ⚠️ **No bare figures.** Every board carries an explainer naming the period **read from the response**
  (anchored: *"this week"* plus a `RESETS WEDNESDAYS 3 PM UTC` line derived from `window.resetsAt`;
  unanchored: *"in the last 7 days"*, or a fallback week's real dates — it can never claim a period the
  rows do not cover), a unit-naming meta line under each plinth figure, and a column header.
  ⚠️ The reset is rendered in **UTC and explicitly labelled**, never the viewer's local time: it is one
  weekly event the whole board shares, and localising it would give viewers near midnight a different
  weekday and quietly break the shared reference. The gamble race adds asterisked fine print:
  *"Total gambled, not deposited — Orbit re-bets what it wins, so this climbs past what you put in."*
  Turnover routinely exceeds deposits and that was read as a bug on first sight.
- ⚠️ **The word is "gamble", never "wager"** — owner decision, applied to the copy AND the wire
  contract (`gambleRace`).
- Money carries the `sol.svg` brand mark. ⚠️ The hall of fame's multiplier takes **no** mark — it is a
  ratio, not money.
- ⚠️ **`/leaderboard` gets NO ☰ menu cell.** The pad is a 4×2 grid of eight cells since #186 and a ninth
  cell is not an option ([`frontend-ux.md`](frontend-ux.md)); it is reached from the home section and
  `/game/stats`, and is unlisted like `/partner`. It joins `EMBED_REDIRECT_ROUTES`.
- **Rows and podium names are player tags** (#186): tapping one opens that player's mini profile card, and
  each carries its `LV n` badge — see [`player-profile.md`](player-profile.md). Since Profile V2 (#202) each
  row and podium tag also shows the player's **Edson profile icon** (24 px, `PlayerTag`'s `avatar` prop;
  never on a house tag or when embedded) — see [`profile-v2.md`](profile-v2.md).
- No SignalR group and no polling — fetch on mount and on `visibilitychange`.
- The home section **hides itself when every visible board is empty**, so on a fresh database it looks
  identical to the feature being absent. `/leaderboard` always renders.

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Api/Controllers/LeaderboardsController.cs` | `GET /leaderboards`, `[HttpOptions]`, size clamp |
| `SpaceStroker.Infrastructure/Handlers/Leaderboards/GetLeaderboards/` | Active-type resolve, vague not-found, caller resolve |
| `SpaceStroker.Infrastructure/Services/Leaderboards/LeaderboardService.cs` | The three aggregates, `ResolveWeeklyWindow`, own rank, clone-before-stamp |
| `.../Services/Leaderboards/HouseWallets.cs` | The four configured public keys; skips blank/`MOCK_` |
| `.../Services/Leaderboards/LeaderboardModels.cs` | `LeaderboardEntry` (+ `ClonePublic`), `LeaderboardWindow` (+ `ResetsAt`) |
| `SpaceStroker.Domain/Leaderboards/Services/LeaderboardSchedule.cs` | ⚠️ The weekly reset grid — pure, integer ticks, **floors** |
| `SpaceStroker.Domain/Configs/Entities/LeaderboardSettings.cs` | `WeeklyResetAnchorUtc`; null => rolling 7 days |
| `.../Contracts/Leaderboards/LeaderboardsDto.cs` | Response DTOs; wire names `gambleRace`/`weeklyPnl`/`hallOfFame` |
| `.../Services/Cache/CacheHelper.cs` | `GenerateLeaderboardsCacheSettings` — 120 s, keyed on filter **+ period start** |

**Frontend (`monkify-front`)** — `src/pages/LeaderboardPage.vue`, `src/components/leaderboard/*`,
`src/components/home/HomeLeaderboard.vue`, `src/hooks/useLeaderboards.ts`,
`src/constants/leaderboards.ts` (the per-board config + `availableBoards()`),
`src/constants/featureFlags.ts`, `src/services/mainService.ts`, `src/routes.ts`, `src/main.ts`.

**Tests** — `LeaderboardServiceTests`, `LeaderboardHallOfFameTests`, `LeaderboardWindowTests`,
`LeaderboardEligibilityTests`, `GetLeaderboardsHandlerTests`; integration `LeaderboardsFlowTests`.
⚠️ The integration suite is **the only place the SQL translation is proven** — the unit tests run on
the EF InMemory provider, which evaluates plenty Npgsql cannot.

## Traps this feature already paid for

1. ⚠️ **`DefaultIfEmpty()` over a PROJECTED type does not null-check.** It yields a non-null instance
   whose members are null, so a `payout == null` guard never fires and reading it throws. Both payout
   lookups are **correlated subqueries** for this reason — do not "simplify" them back into a left
   join. Without it, every losing player silently vanishes from the PNL board.
2. ⚠️ **Joining on `bet.PlayerId.Value` evaluates `.Value` on nulls** — the provider does not reliably
   order the null filter before the key selector. Every join goes through the **nullable side**.
3. ⚠️ **Stakes are FIXED per game type**, so a gamble race is separated by **rounds played**, not bet
   size. A test that varies `Amount` to tell players apart has every bet refused.
