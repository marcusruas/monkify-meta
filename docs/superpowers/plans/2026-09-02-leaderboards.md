# Leaderboards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ship the three read-only leaderboards (wager race, weekly PNL, hall of fame) behind one public read and one carousel, with the hall of fame flagged off for phase 1.

**Architecture:** one `GET /leaderboards` on the stateless API returns all three boards. Every figure is a bounded Postgres aggregate over existing `Game` / `Bet` / `TransactionLog` / `Player` rows — **no entity, no migration**. The service follows `GlobalStatsService` (server-side `GROUP BY` + `LIMIT`), **never** `PlayerStatsService` (which materialises a player's whole history). The frontend renders one generic board component driven by a per-board config map.

**Tech Stack:** .NET 8 / EF Core / Npgsql / MediatR / Redis · Vue 3 + TypeScript (Vue CLI, `VUE_APP_*`).

**Spec:** `monkify-meta/specs/leaderboards.md` — authoritative. Section references below (§6.3.1 etc.) point at it; the long code blocks live there and are not duplicated here.

## Global Constraints

- Worktrees: `.worktrees/leaderboards/{monkify-backend,monkify-front,monkify-meta}`, branch `feature/leaderboards`. Hubs stay on `main`.
- **No entity, no EF mapping change, no index, no migration.**
- Backend unit tests via `scripts/run_tests.bat` (never the full `dotnet test` — `ProductionSessions` is a slow Monte Carlo suite). Integration via `scripts/run_integration_tests.bat`.
- **Bounded aggregates only** — no `.ToListAsync()` over rounds or bets; every board ends in `.Take(size)`.
- **Never `COALESCE` in a date predicate** — unsargable, forfeits `IX_Games_ParametersId_Status_EndDate_CreatedDate`. Use the two-arm form.
- Vague errors: unknown/inactive `parametersId` → `ErrorMessages.ParameterNotFound`. Never 400 on a filter/size/wallet.
- `Player.IsSelfExcluded` is a C# method EF **cannot translate** — the eligibility predicate is written inline.
- Cache key is the **filter only**; the caller's own rank is joined live and cached rows are **cloned before stamping**.
- Frontend flag `VUE_APP_HALL_OF_FAME_ENABLED`, fail-closed `=== 'true'`, default false.
- Mock-only secrets. No `ReferralCode` on any response.

---

### Task 1: House-wallet exclusion

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Leaderboards/HouseWallets.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/LeaderboardEligibilityTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> HouseWallets.Resolve(TokenSettings token)`

- [ ] **Step 1: Write the failing tests** — the four configured public keys are returned; a blank one is skipped; a `MOCK_`-prefixed one is skipped; a changed `HouseSeed.PublicKey` is what comes back (reads config, not a literal).
- [ ] **Step 2: Run, confirm it fails to compile** (`HouseWallets` does not exist).
- [ ] **Step 3: Implement** — collect `token.TokenOwnerPublicKey`, `token.Orbit?.PublicKey`, `token.HouseSeed?.PublicKey`, `token.ReferralPayout?.PublicKey`; `Where(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("MOCK_", StringComparison.OrdinalIgnoreCase))`; `Distinct().ToList()`. XML doc per §6.4 — say why a marker column was rejected.
- [ ] **Step 4: `scripts/run_tests.bat`** — green.
- [ ] **Step 5: Commit** `Add the house-wallet exclusion list for the leaderboards.`

---

### Task 2: Result models, eligibility and the window resolver

**Files:**
- Create: `Services/Leaderboards/LeaderboardEntry.cs`, `Leaderboards.cs`, `LeaderboardWindow.cs`, `ILeaderboardService.cs`, `LeaderboardService.cs` (skeleton + the two shared blocks + `ResolveWeeklyWindow`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/LeaderboardWindowTests.cs`, extend `LeaderboardEligibilityTests.cs`

**Interfaces:**
- Produces: `LeaderboardEntry { Guid PlayerId; string? DisplayName; string Wallet; decimal Score; DateTime FirstQualifyingAt; int Rank; decimal? Won; DateTime? AchievedAt; string? GameTypeName; }`
- Produces: `record LeaderboardWindow(DateTime? From, DateTime? To, bool IsFallback)`
- Produces: `Task<Leaderboards> ILeaderboardService.GetLeaderboards(List<Guid> typeIds, Guid? parametersId, Guid? callerPlayerId, int size, CancellationToken ct)`

- [ ] **Step 1: Write the failing eligibility tests** — house seed wallet excluded; currently self-excluded excluded; lapsed exclusion included; permanent exclusion (`SelfExcludedUntil == null`) excluded; and the parity test asserting the inline predicate agrees with `Player.IsSelfExcluded(now)` across all four combinations.
- [ ] **Step 2: Write the failing window tests** — the six cases in spec §11.1 (`LeaderboardWindowTests`).
- [ ] **Step 3: Run, confirm failures.**
- [ ] **Step 4: Implement `CompletedRounds(typeIds, from, to)`, `EligiblePlayers(now)`, `WonByBet()` and `ResolveWeeklyWindow`** exactly as §6.3.1, comments included — they carry the reasoning (two-MAX not COALESCE, the cutover clamp, why the predicate is inline).
- [ ] **Step 5: `scripts/run_tests.bat`** — green.
- [ ] **Step 6: Commit** `Add the leaderboard window resolver and eligibility predicate.`

---

### Task 3: The three board aggregates

**Files:**
- Modify: `Services/Leaderboards/LeaderboardService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/LeaderboardServiceTests.cs`, `LeaderboardHallOfFameTests.cs`

- [ ] **Step 1: Write the failing tests** — the eleven `LeaderboardServiceTests` rows and the three `LeaderboardHallOfFameTests` rows in spec §11.1.
- [ ] **Step 2: Run, confirm failures.**
- [ ] **Step 3: Implement the wager race** (§6.3.2) — `GROUP BY` player over paid bets joined to the window's rounds, `Score = Σ Amount`, `FirstQualifyingAt = MIN(CreatedDate)`.
- [ ] **Step 4: Implement the weekly PNL** (§6.3.3) — the `DefaultIfEmpty()` left join so a player who won nothing still ranks (an inner join drops most of the board), `Score = Σ Won − Σ Amount`, never clamped.
- [ ] **Step 5: Implement the hall of fame** (§6.3.4) — step 1 groups to `MAX(multiplier)` per player over `(cutover, null)`; step 2 fetches the best winning bet for the ≤25 top ids and supplies `Won` / `AchievedAt` / `GameTypeName`. ⚠️ Rank on the multiplier, never the amount.
- [ ] **Step 6: Apply the shared ordering** (§6.3.5) — `Score` desc, `FirstQualifyingAt` asc, `.Take(size)`. Never order by wallet.
- [ ] **Step 7: `scripts/run_tests.bat`** — green.
- [ ] **Step 8: Commit** `Add the three leaderboard aggregates.`

---

### Task 4: Own rank, cache and service assembly

**Files:**
- Modify: `Services/Leaderboards/LeaderboardService.cs`, `Services/Cache/CacheHelper.cs`
- Test: extend `LeaderboardServiceTests.cs`

- [ ] **Step 1: Write the failing tests** — own rank uses the same comparison as the ordering; null for a caller who never bet; null for a caller who is a house wallet or self-excluded.
- [ ] **Step 2: Add `GenerateLeaderboardsCacheSettings(Guid? parametersId, int size)`** — `$"Leaderboards:{parametersId?.ToString() ?? "all"}:{size}"`, 120 s, with the comment from §6.8 explaining why the wallet is **not** in the key.
- [ ] **Step 3: Implement `RankOf`** per §6.5 — `FirstOrDefault` for the caller's entry, then `CountAsync` of entries strictly above using the identical tie-break, `Rank = above + 1`.
- [ ] **Step 4: Assemble `GetLeaderboards`** — read the three public slices through `_cache.GetOrCreateAsync`, then **clone** and stamp `IsYou` / attach `You`.
- [ ] **Step 5: `scripts/run_tests.bat`** — green.
- [ ] **Step 6: Commit** `Add own-rank resolution and caching to the leaderboard service.`

---

### Task 5: DTOs, handler, controller, DI

**Files:**
- Create: `Contracts/Leaderboards/{LeaderboardsDto,LeaderboardBoardDto,LeaderboardWindowDto,LeaderboardRowDto,LeaderboardGameTypeDto}.cs`
- Create: `Handlers/Leaderboards/GetLeaderboards/{GetLeaderboardsRequest,GetLeaderboardsHandler}.cs`
- Create: `src/SpaceStroker.Api/Controllers/LeaderboardsController.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetLeaderboardsHandlerTests.cs`, `UnitTests/Contracts/LeaderboardRowDtoTests.cs`

- [ ] **Step 1: Write the failing tests** — the six handler rows and the two DTO rows in spec §11.1.
- [ ] **Step 2: Implement the DTOs** (§6.6) including `ClonePublic()` and `Won` on the row.
- [ ] **Step 3: Implement the handler** (§6.2) — active catalogue → vague `ParameterNotFound` → caller resolve (copied from `GetGamesHistoryHandler.ResolveCallerPlayerId`) → aggregate → map.
- [ ] **Step 4: Implement the controller** (§6.1) with `[HttpGet]`, `[HttpOptions]`, and `size` clamped at the edge to `[1,25]`.
- [ ] **Step 5: Register `ILeaderboardService`** in `DependencyInjection.cs` beside `IPlayerStatsService`.
- [ ] **Step 6: `scripts/run_tests.bat`** — green.
- [ ] **Step 7: Commit** `Expose GET /leaderboards.`

---

### Task 6: The mandatory integration test

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/LeaderboardsFlowTests.cs`

⚠️ Harness facts: seed a `Player` through a **separate** context (the handler's own in-memory context throws an EF identity conflict); `IntegrationTestFixture` sets settings via `GeneralSettings__…` env vars **before the hosts build**; `HouseSeedWallet` is already a fixture constant; `ExpireSelfExclusionAsync` exists. ⚠️ **A self-excluded player cannot place a bet** — place the bets first, self-exclude after.

- [ ] **Step 1: Write the happy path** — seed an isolated type, drive rounds to `RewardForWinnersCompleted` through the real API + Consumers, assert all three boards rank as expected and the applied filter is echoed.
- [ ] **Step 2: Add the five extra cases** from §11.2 — house wallet absent, self-excluded absent, hall of fame carries an amount but no pre-cutover row, own rank pinned/null, and two wallets not seeing each other's `IsYou`.
- [ ] **Step 3: Add the fallback case** — backdate every round out of the last 7 days, assert the board still returns rows with `window.isFallback` true and the real `from`/`to`.
- [ ] **Step 4: `scripts/run_integration_tests.bat`** — green (Docker required).
- [ ] **Step 5: Commit** `Add the leaderboards integration test.`

---

### Task 7: Frontend — types, service, hook, flag

**Files:**
- Modify: `src/types/index.ts`, `src/services/mainService.ts`, `src/constants/featureFlags.ts`, `.env.example`
- Create: `src/hooks/useLeaderboards.ts`

- [ ] **Step 1: Add the types** mirroring §6.6 (`Leaderboards`, `LeaderboardBoard`, `LeaderboardWindow`, `LeaderboardRow`, `LeaderboardGameType`).
- [ ] **Step 2: Add `getLeaderboards(parametersId?, wallet?, size)`** to `mainService` — `singleFlight` with the params in the key; ⚠️ **not** `runWithRecovery` (a leaderboard failure renders inline, never routes to `/error` — the reasoning `getPlayerStats` carries).
- [ ] **Step 3: Add `hallOfFameEnabled`** to `featureFlags.ts` (`=== 'true'`) and `VUE_APP_HALL_OF_FAME_ENABLED=false` to `.env.example` with the build-time comment.
- [ ] **Step 4: Add `useLeaderboards.ts`** — fetch on mount and on `visibilitychange`; no hub group, no interval.
- [ ] **Step 5: `yarn lint`** — clean.
- [ ] **Step 6: Commit** `Add the leaderboard client, types and feature flag.`

---

### Task 8: Frontend — the carousel components

**Files:**
- Create: `src/components/leaderboard/{LeaderboardCarousel,LeaderboardBoard,LeaderboardPlinth,LeaderboardRow,LeaderboardTypeFilter,LeaderboardWindowBadge}.vue`
- Modify: `src/constants/icons.ts`

- [ ] **Step 1: `LeaderboardWindowBadge.vue`** — captions from `board.window`: `LAST 7 DAYS` / `ALL TIME` / `from–to` when `isFallback`. ⚠️ Never the literal "LAST 7 DAYS" over a fallback.
- [ ] **Step 2: `LeaderboardPlinth.vue` + `LeaderboardRow.vue`** — the stepped plinth and the list row, money carrying `sol.svg`. ⚠️ The hall of fame's multiplier takes **no** mark.
- [ ] **Step 3: `LeaderboardBoard.vue`** — the generic board: explainer, plinths, column header strip, list, pinned `YOU`, the fallback strip, and the empty state. Driven by the per-board config map in §7.3.
- [ ] **Step 4: `LeaderboardCarousel.vue`** — the chips; ⚠️ two unless `hallOfFameEnabled`.
- [ ] **Step 5: `LeaderboardTypeFilter.vue`** + the `leaderboard` pixel mark in `icons.ts` (⚠️ **not** the SOL mark — that stays `sol.svg`).
- [ ] **Step 6: `yarn lint`** — clean.
- [ ] **Step 7: Commit** `Add the leaderboard carousel components.`

---

### Task 9: Frontend — page, route, home section

**Files:**
- Create: `src/pages/LeaderboardPage.vue`, `src/components/home/HomeLeaderboard.vue`
- Modify: `src/routes.ts`, `src/main.ts`, `src/pages/HomePage.vue`, `src/pages/GameStatsPage.vue`

- [ ] **Step 1: `LeaderboardPage.vue`** at `/leaderboard`, top 25, with the unlisted-route comment (⚠️ no ☰ cell — the pad is nine cells and a tenth is not an option).
- [ ] **Step 2: Add `'Leaderboard'` to `EMBED_REDIRECT_ROUTES`.** ⚠️ `'Home'` is already there — do not add a redundant `isEmbedded` guard to the home section.
- [ ] **Step 3: `HomeLeaderboard.vue`** on `HomePage.vue` between `<home-player-stats>` and `<home-floor>`, top 10, built on `HomeSection` (⚠️ must pass a `title`), and **not** gated on `connected`.
- [ ] **Step 4: Link through from `GameStatsPage.vue`.**
- [ ] **Step 5: `yarn lint && yarn build`** — clean.
- [ ] **Step 6: Commit** `Add the leaderboard page and home section.`

---

### Task 10: Verify, push, deploy

- [ ] **Step 1: `scripts/run_tests.bat`** and **`scripts/run_integration_tests.bat`** — both green; record which tests changed.
- [ ] **Step 2: `yarn lint && yarn build`** in the front worktree.
- [ ] **Step 3: Push both branches**, build the GitHub compare links.
- [ ] **Step 4: Ask the user** Replace / Combine / Skip — ⚠️ `typer-refactoring` is currently deployed; never auto-clobber.
- [ ] **Step 5: Deploy** (if not Skip) — ⚠️ copy the hub's `yarn.lock` into the front worktree first, and verify backend **and** front are Up together before claiming deployed.
