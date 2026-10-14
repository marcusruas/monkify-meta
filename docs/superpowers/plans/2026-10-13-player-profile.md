# Profile V1 (+ #206 Levels) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Public `/player/:wallet` profiles with 10 derived trophies, last-10 rounds, cosmetic levels, and a tappable `PlayerTag` → `PlayerCard` on every player-name surface.

**Architecture:** Two new public, stateless API reads (`GET /players/profile`, `GET /players/levels`) computed on read from `GameBets`/`Games`/`TransactionLog` with bounded aggregates + 60 s Redis caches; an additive `isHouse` on bet payloads. Front gets types/service/composables first (not gated), then a design preview artifact the owner must approve, then the UI (`PlayerTag`, `PlayerCard`, `PlayerProfilePage`, surface wiring).

**Tech Stack:** .NET 8, EF Core (Postgres / InMemory in unit tests), MediatR, xUnit + Shouldly + Moq, Testcontainers; Vue 3 + TS, Vue CLI, Tailwind 3.

**Spec:** `monkify-meta/specs/player-profile.md` (authoritative — read §3 decisions, §6, §7, §11 before any task).

**Worktrees:** `C:/Users/kurok/Desktop/monkify/.worktrees/player-profile/{monkify-backend,monkify-front,monkify-meta}` on `feature/player-profile`. Never touch the hubs.

## Global Constraints

- No entity, column or migration. No money/payout path touched.
- API stays stateless; both reads live on `SpaceStroker.Api` / `PlayersController`.
- Unavailable profile = HTTP 200 `{ available:false }`, byte-identical for malformed / house / self-excluded.
- Nothing on the wire flags a streamer or a self-exclusion (no new fields beyond the spec's DTOs).
- Trophies: no `SettlementCutoverUtc` floor. Levels: floored at the cutover, two-arm sargable predicate, never `COALESCE` in a WHERE.
- Do not copy `PlayerStatsService`'s load-every-bet pattern: scalar aggregates + one narrow per-round streak projection.
- Copy says "gamble", never "wager".
- Front identity = `useMobileWallet().effectivePublicKey`, never `useWallet().publicKey`.
- **No UI code (spec §7.3+) before the design artifact is approved and linked on card #186.**
- Unit tests via `monkify-backend/scripts/run_tests.bat`; integration via `scripts/run_integration_tests.bat`.

## Review Focus

1. A wallet with valid base58 but surrounding whitespace / mixed in a comma list with blanks (`levels?wallets=a,,b, `) → trimmed, blanks dropped, never a 400. (Task 7 test.)
2. A cached profile body + a rename or a new self-exclusion → rename shows at once, exclusion hides at once. (Task 6 tests.)
3. Round with two bets of the same player where only the later wins → round counts as won and recent-rounds shows the winning bet. (Tasks 4, 5 tests.)
4. `levels` request failure / 60 inputs → badges hidden, only first 50 answered, no toast. (Task 7 test + Task 11 store behaviour.)
5. A player tag tapped inside a clickable DuelBoard room / race card → card opens, room is NOT joined (`@click.stop`). (Manual QA in Task 17.)

---

## Phase A — Backend (`monkify-backend`, not gated)

### Task 1: `WalletAddress.IsValid` + `GameModeClassifier`

**Files:**
- Create: `src/SpaceStroker.Domain/Players/WalletAddress.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Players/GameModeClassifier.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Players/WalletAddressTests.cs`, `src/SpaceStroker.Tests/UnitTests/Services/GameModeClassifierTests.cs`

**Interfaces — Produces:**
- `static bool WalletAddress.IsValid(string? wallet)`
- `static class ProfileModes { const string Sprint="sprint", HyperRoll="hyperRoll", Duel="duel", Jackpot="jackpot"; }`
- `static string GameModeClassifier.Classify(GameParameters p)` and `static Expression<Func<GameParameters,string>> GameModeClassifier.Expression`

- [ ] Write tests: `IsValid` → false for null, "", "  ", "not-a-wallet"; true for `new Account().PublicKey.Key` and the same with surrounding spaces. Classifier: for the 4 flag combos (`IsDuel`, `IsHouseEdge`, `LotteryParameters != null`, none) `Classify` and `Expression.Compile()` agree and return duel/hyperRoll/jackpot/sprint; `IsDuel && IsHouseEdge` → duel (first match wins).
- [ ] Run, see compile failures.
- [ ] Implement:

```csharp
// WalletAddress.cs (namespace SpaceStroker.Domain.Players)
public static class WalletAddress
{
    public static bool IsValid(string? wallet)
    {
        var trimmed = wallet?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        try { _ = new Solnet.Wallet.PublicKey(trimmed); return true; }
        catch { return false; }
    }
}
```
(Check the Domain project references Solnet; if not, place it in `SpaceStroker.Infrastructure/Services/Players/WalletAddress.cs` — the spec allows "nearest existing helpers folder".)

```csharp
public static class GameModeClassifier
{
    public static readonly Expression<Func<GameParameters, string>> Expression = p =>
        p.IsDuel ? ProfileModes.Duel
        : p.IsHouseEdge ? ProfileModes.HyperRoll
        : p.LotteryParameters != null ? ProfileModes.Jackpot
        : ProfileModes.Sprint;
    private static readonly Func<GameParameters, string> Compiled = Expression.Compile();
    public static string Classify(GameParameters p) => Compiled(p);
}
```
- [ ] Run tests → pass. Commit `Profile: WalletAddress.IsValid + shared game mode classifier (#186).`

### Task 2: `PlayerLevels` threshold table

**Files:** Create `src/SpaceStroker.Domain/Players/PlayerLevels.cs`; `src/SpaceStroker.Infrastructure/Contracts/Players/PlayerLevelDto.cs` (record lives with the profile DTO in Task 6 — define it here in `PlayerProfileDto.cs` to avoid a second file); Test `UnitTests/Domain/Players/PlayerLevelsTests.cs`.

**Produces:** `PlayerLevels.Thresholds` (`IReadOnlyList<decimal>`, length 201), `int PlayerLevels.LevelFor(decimal gambled)`, `(int Level, decimal Floor, decimal? Next) PlayerLevels.Describe(decimal gambled)`; `record PlayerLevelDto(int Level, decimal Gambled, decimal LevelFloor, decimal? NextLevelAt)` with `Zero => new(0,0,0,0.2m)` and `static PlayerLevelDto From(decimal gambled)`.

- [ ] Tests (spec §11.1 Levels table): `LevelFor` at 0, 0.19, 0.2, 0.5, 0.99, 1 → 0,0,1,2,2,3; for n in {4,10,50,120,200}: `LevelFor(T[n]-0.01m)==n-1`, `LevelFor(T[n])==n`; table strictly increasing, `T[3]==1m`, `|T[120]-1_000_000| <= 10_000`, length 201; `LevelFor(T[200]*10)==200` and `PlayerLevelDto.From(that).NextLevelAt == null`; `From(0)` equals `Zero`.
- [ ] Implement: `T[0]=0, T[1]=0.2m, T[2]=0.5m`, `r = Math.Pow(10, 6.0/117)`, `T[n] = Math.Round((decimal)Math.Pow(r, n-3), 2)` for n=3..200; `LevelFor` = binary search for the largest index with `T[i] <= gambled`. If rounding to 2 dp makes two adjacent early entries equal (check L3..L6), keep strict increase by asserting in the test — fix by not rounding below 2 dp only if the test fails.
- [ ] Pass, commit `Levels: static threshold table (#206).`

### Task 3: `PlayerLevelService.GetGambledAsync`

**Files:** Create `Services/Players/IPlayerLevelService.cs`, `PlayerLevelService.cs`; modify `DependencyInjection.cs` (`services.AddScoped<IPlayerLevelService, PlayerLevelService>();` next to `IPlayerStatsService`); Test `UnitTests/Services/PlayerLevelServiceTests.cs`.

**Produces:** `Task<IReadOnlyDictionary<string, decimal>> GetGambledAsync(IReadOnlyCollection<string> wallets, CancellationToken ct = default)`. Ctor `(SpaceStrokerDbContext context, GeneralSettings settings = null)`; cutover from `settings?.Operator?.Token?.SettlementCutoverUtc` via `StatsWindowExtensions.EffectiveCutoff(StatsWindow.All, cutover, DateTime.UtcNow)`.

- [ ] Tests (InMemory, helpers copied from `PlayerStatsServiceTests`: `Type`, `Round`, `PlayerBet`, plus `Player(wallet)` = `new Player { ExternalPlayerId = wallet }`): paid bets in completed rounds of two types summed; free (Amount 0, EntryKind Free), holder (EntryKind Holder, Amount 0), `EntryKind.Streamer` paid bet, non-completed round → excluded; round with EndDate before cutover excluded; EndDate null + CreatedDate after cutover included; `FundedByOrbit`/`OrbitAutoPlaced` bets counted; active `StreamerWallet` row + self-excluded player still summed; unknown wallet absent; empty input → empty dict, no query.
- [ ] Implement exactly the spec §6.8.2 query:

```csharp
var q = from b in _context.GameBets
        join g in _context.Games on b.GameId equals g.Id
        join pl in _context.Players on b.PlayerId equals (Guid?)pl.Id
        where wallets.Contains(pl.ExternalPlayerId)
              && b.Amount > 0 && b.EntryKind != BetEntryKind.Streamer
              && g.Status == GameStatus.RewardForWinnersCompleted
        select new { pl.ExternalPlayerId, b.Amount, g.EndDate, g.CreatedDate };
if (cutoff != null)
    q = q.Where(x => x.EndDate >= cutoff || (x.EndDate == null && x.CreatedDate >= cutoff));
return await q.GroupBy(x => x.ExternalPlayerId)
              .Select(g => new { Wallet = g.Key, Sum = g.Sum(x => x.Amount) })
              .ToDictionaryAsync(x => x.Wallet, x => x.Sum, ct);
```
- [ ] Pass, commit `Levels: grouped gambled aggregate (#206).`

### Task 4: `PlayerProfileService.ComputeAchievementsAsync`

**Files:** Create `Services/Players/IPlayerProfileService.cs`, `PlayerProfileService.cs`, `Services/Players/AchievementKeys.cs`; register scoped in `DependencyInjection.cs`; Test `UnitTests/Services/PlayerProfileServiceTests.cs`.

**Produces:**
- `AchievementKeys.Ordered` = `["firstSprintWin","firstHyperRollWin","firstDuelWin","jackpotWinner","hyperHit","rivalSlayer","crowdBeater","winStreak3","winStreak5","explorer"]`
- `Task<IReadOnlyList<AchievementDto>> ComputeAchievementsAsync(Guid playerId, string wallet, CancellationToken ct = default)` — always 10 entries in that order.
- `Task<IReadOnlyList<ProfileRoundDto>> GetRecentRoundsAsync(Guid playerId, CancellationToken ct = default)` (Task 5).
- Ctor `(SpaceStrokerDbContext context, GeneralSettings settings = null, IStreamerLedger? streamerLedger = null)`; house wallets = `HouseWallets.Resolve(settings?.Operator?.Token)`.

- [ ] Tests — every row of spec §11.1 "PlayerProfileServiceTests" achievement cases (first-wins per mode with dates; earlier of two; EndDate null→CreatedDate; three non-completed statuses; Refunded/NeedsRefunding/Voided; free/holder/Orbit manual/auto count; `EntryKind.Streamer` doesn't; active streamer → all locked, inactive → counts; HYPER 10× vs SUPER vs <10×; Duel House vs OpenRoom; Sprint 4/5/4+house-seed players; anonymous free bets (PlayerId null) counted individually; W W L W W W → streak3 at round 6, streak5 locked; 5 wins case-insensitive; two-bet round any-win; explorer all-lost with latest-of-four date, 3 modes locked; pre-cutover win unlocks). Mode set up via `GameParameters` flags (`IsDuel`, `IsHouseEdge`, `LotteryParameters = new LotteryParameters(...)`).
- [ ] Implement:
  - `Q` = join bets/games/parameters, filters per spec §6.3, projecting `{ b.Id, b.PlayerId, b.Choice, g.Id GameId, g.WinningChoice, g.EndDate, g.CreatedDate, g.BoardMode, g.DuelKind, Mode = classifier }`. Use `GameModeClassifier.Expression` via a select that invokes it (`p.IsDuel ? ... : ...` inline in the projection if `Invoke` doesn't translate — keep the C# twin test as the guard).
  - Won predicate: `x.WinningChoice != null && x.Choice.ToUpper() == x.WinningChoice.ToUpper()`.
  - Rules 1–4: `Q.Where(won).GroupBy(Mode).Select(g => new { g.Key, First = g.Min(x => x.EndDate ?? x.CreatedDate) })` (≤ 4 rows).
  - Rule 5: won ∧ hyperRoll ∧ `BoardMode == "HYPER"` ∧ `_context.GameBoardChoices.Any(c => c.GameId == x.GameId && c.Choice == x.Choice && c.Multiplier >= 10)` → `Min(date)` (nullable `(DateTime?)`).
  - Rule 6: won ∧ duel ∧ `DuelKind == DuelKind.OpenRoom` → Min.
  - Rule 7: won ∧ sprint ∧ `_context.GameBets.Where(b2 => b2.GameId == x.GameId && status filter && !(b2.PlayerId != null && _context.Players.Any(pl => pl.Id == b2.PlayerId && houseWallets.Contains(pl.ExternalPlayerId)))).Select(b2 => b2.PlayerId ?? b2.Id).Distinct().Count() >= 5` → Min.
  - Rules 8–9: `Q.GroupBy(GameId).Select(g => new { g.Key, EndDate = g.Max(x=>x.EndDate), CreatedDate = g.Max(x=>x.CreatedDate), AnyWon = g.Any(won) })`, order in C# by `EndDate ?? CreatedDate` then `CreatedDate`, walk, stop when both found.
  - Rule 10: from rules-1–4-style grouping without the won filter; all four keys present → `Max` of the four mins.
  - Active streamer: `await _context.StreamerWallets.AnyAsync(s => s.Active && s.Wallet == wallet)` → return all locked, no other query.
- [ ] Pass, commit `Profile: derive the 10 trophies on read (#186).`

### Task 5: `GetRecentRoundsAsync`

**Files:** `PlayerProfileService.cs`; tests in `PlayerProfileServiceTests.cs`.

- [ ] Tests: 12 rounds → 10 newest desc; one row per round = winning bet else earliest `CreatedDate`; payout = Σ Credit rows; Streamer bet → `IStreamerLedger.WonFor` (Moq) and `Entry=="paid"`; free → `Stake 0, Entry "free"`; holder → `"ticket"`; lost → payout 0; `ViaOrbit = FundedByOrbit`; `DuelVsHouse` from `DuelKind.House`; `Mode`, `GameTypeName`, `BoardMode` mapped.
- [ ] Implement per spec §6.4 (step 1 uses `Q` minus the streamer filter, `GroupBy(GameId).Select(Max date)`, `OrderByDescending`, `Take(10)`; then load ≤10 games + parameters + the player's bets in them; credits grouped by BetId for the chosen bets only).
- [ ] Pass, commit `Profile: last 10 cross-mode rounds (#186).`

### Task 6: `GET /players/profile`

**Files:** Create `Contracts/Players/PlayerProfileDto.cs` (DTO, `AchievementDto`, `ProfileRoundDto`, `PlayerLevelDto` exactly per spec §6.1 — move Task 2's record here if created elsewhere), `Handlers/Players/GetPlayerProfile/GetPlayerProfileRequest.cs`, `GetPlayerProfileHandler.cs`; modify `CacheHelper.cs` (`GeneratePlayerProfileCacheSettings(wallet)` → `PlayerProfile:{wallet}`, 60 s, with a comment in the file's style), `PlayersController.cs` (`[HttpOptions("profile")]` + `[HttpGet("profile")]`); Test `UnitTests/Handlers/GetPlayerProfileHandlerTests.cs`.

**Consumes:** `IPlayerProfileService`, `IPlayerLevelService`, `PlayerLevelDto.From`, `HouseWallets.Resolve`, `WalletAddress.IsValid`, `ICacheService`.

- [ ] Tests (spec §11.1 handler table): malformed/blank → `Available false`, service mocks never called, cache never touched; each of 4 house wallets; self-excluded (future / permanent) → serialized JSON identical to malformed case; lapsed exclusion → available; no Player row → available, `JoinedAt null`, 10 locked, empty rounds, `cache.Set` never called; cached body + renamed player → new name returned, cached instance's `DisplayName` unchanged; exclusion set while cached → unavailable; `Level` equals `PlayerLevelDto.From(gambled)`; `TokenSymbol/Decimals` from settings token.
- [ ] Implement handler order 1–6 of spec §6.1. Clone with `cached with { }`-style: make `PlayerProfileDto` a `record` class (or add `Clone()` returning `MemberwiseClone`) and stamp `DisplayName`.
- [ ] Pass, commit `Profile: GET /players/profile (#186).`

### Task 7: `GET /players/levels`

**Files:** Create `Contracts/Players/PlayerLevelsDto.cs`, `Handlers/Players/GetPlayerLevels/{GetPlayerLevelsRequest,GetPlayerLevelsHandler}.cs`; modify `CacheHelper.cs` (`GeneratePlayerLevelCacheSettings(wallet)` → `PlayerLevel:{wallet}`, 60 s), `PlayersController.cs`; Test `UnitTests/Handlers/GetPlayerLevelsHandlerTests.cs`.

- [ ] Tests: input `"bad, <house>, <zero>, <w1>, <w1> ,,"` → only `w1` (level ≥1) in `Levels`; 60 distinct valid wallets → `GetGambledAsync` receives exactly the first 50; cached wallet + uncached → service called with only the uncached; misses written back incl. level 0; null/empty `wallets` → empty dict, no service call.
- [ ] Implement per spec §6.8.3 (cache holds `int`; `ICacheService.TryGet<int>` — if the interface constrains to reference types, cache a tiny `record CachedLevel(int Level)`).
- [ ] Pass, commit `Levels: batched GET /players/levels (#206).`

### Task 8: `isHouse` on bet payloads

**Files:** `Contracts/Games/BetDto.cs` (ctor gains `IReadOnlyCollection<string>? houseWallets = null`; `public bool IsHouse { get; set; } = houseWallets != null && bet.Player?.ExternalPlayerId is { } w && houseWallets.Contains(w);`), `ActiveGameDto.cs` (ctor gains the same optional param, passed to each `BetDto`), `GetActiveGameForParameterHandler.cs` (inject `GeneralSettings`, pass `HouseWallets.Resolve(_settings.Operator.Token)` at its three `new ActiveGameDto` sites), `Consumers/BetNotification/BetNotificationEvent.cs` (append `bool IsHouse = false`), `GameService.cs` `PublishBetNotification` (compute `HouseWallets.Resolve(_settings.Operator?.Token).Contains(wallet)`); check the SignalR consumer that forwards `BetNotificationEvent` still serializes the whole record (it should — the new field rides along). Tests: `UnitTests/Contracts/BetDtoTests.cs` (house seed wallet → true; normal → false; no list → false) and extend an existing GameService notification test if one asserts the event shape.

- [ ] Write tests, fail, implement, pass. Run the whole unit suite (`scripts/run_tests.bat`) here since ctors changed. Commit `Bets: additive isHouse flag on bet payloads (#186).`

### Task 9: Integration test `PlayerProfileFlowTests`

**Files:** Create `src/SpaceStroker.Tests.Integration/PlayerProfileFlowTests.cs` (+ add a `SelfExcludeAsync(Account, SelfExclusionDuration)` helper — copy the one in `SelfExclusionFlowTests`, or lift it into the fixture and reuse from both).

- [ ] Write the four scenarios of spec §11.2 with real keypairs (`new Wallet(WordCount.TwentyFour, WordList.English).GetAccount(0)`), paid bets of `Amount = 1` (≥ 0.2 → level ≥ 1; `gambled == 1`), read after `RewardForWinnersCompleted`. Winner = whichever `recentRounds[0].won`. Assertions: `available`, `joinedAt != null`, `firstSprintWin` unlocked with `unlockedAt == round EndDate` (read the game via `GetMostRecentGameForTypeAsync` or the fixture's game read), other 9 locked, `recentRounds[0].gameId == gameId`, payout > 0; `/players/stats` rounds == 1; loser `won=false`, trophy locked; self-exclude → unavailable; `ExpireSelfExclusionAsync` → available + trophy (profile body cache is bypassed by the live exclusion check; the post-expiry read can hit the cache — fine); `HouseSeedWallet` and `not-a-wallet` → unavailable; `levels?wallets=<winner>,<HouseSeedWallet>,not-a-wallet` → only winner with level == profile level.
- [ ] Run `scripts/run_integration_tests.bat` (Docker up) — filter to this class first, then full. Commit `Profile: happy-path integration test (#186, #206).`

---

## Phase B — Front plumbing (`monkify-front`, not gated)

### Task 10: Types, service, constants, utils

**Files:** `src/types/index.ts`, `src/services/mainService.ts`, `src/constants/achievements.ts` (new), `src/utils/explorer.ts`, `src/utils/clipboard.ts`, `src/utils/verifyRound.ts` (new), `src/utils/playerCard.ts`, `src/pages/GameHistoryPage.vue` (switch to `utils/verifyRound` — behaviour unchanged).

```ts
export type AchievementKey = 'firstSprintWin' | 'firstHyperRollWin' | 'firstDuelWin' | 'jackpotWinner'
  | 'hyperHit' | 'rivalSlayer' | 'crowdBeater' | 'winStreak3' | 'winStreak5' | 'explorer';
export interface ProfileAchievement { key: AchievementKey; unlocked: boolean; unlockedAt: string | null; }
export type ProfileMode = 'sprint' | 'hyperRoll' | 'duel' | 'jackpot';
export interface ProfileRound { gameId: string; endedAt: string; mode: ProfileMode; gameTypeName: string;
  boardMode: string | null; duelVsHouse: boolean; choice: string; stake: number;
  entry: 'paid' | 'free' | 'ticket'; won: boolean; payout: number; viaOrbit: boolean; }
export interface PlayerLevel { level: number; gambled: number; levelFloor: number; nextLevelAt: number | null; }
export interface PlayerProfile { available: boolean; wallet?: string | null; displayName?: string | null;
  joinedAt?: string | null; achievements: ProfileAchievement[]; recentRounds: ProfileRound[];
  level: PlayerLevel; tokenSymbol?: string; tokenDecimals?: number; }
export interface PlayerLevels { levels: Record<string, number>; }
// AppliedBet gains: isHouse?: boolean;
```
Service: `getPlayerProfile(wallet)` single-flighted like `getPlayerStats`; `getPlayerLevels(wallets: string[])` → `GET /players/levels` with `params: { wallets: wallets.join(',') }` (not single-flighted — the store batches). Export both in the default object. `explorer.ts`: add `accountExplorerUrl(wallet)` beside the existing helper. `clipboard.ts`: `copyText` if not already present.

- [ ] `yarn lint` + `yarn build` pass (copy hub `.env.local` + `yarn.lock` into the worktree first). Commit `Profile: front types, service and helpers (#186, #206).`

### Task 11: Composables

**Files:** `src/hooks/usePlayerProfile.ts`, `usePlayerCard.ts`, `useWalletConnectRequest.ts`, `usePlayerLevels.ts` (new) — behaviour exactly per spec §7.2 (30 s memo, seq guard, stats dropped when `!available`, levels store with 50 ms flush, chunk 50, 60 s TTL, absent → 0, failure keeps old values and waits TTL). `usePlayerCard` closes on `router.afterEach`.

- [ ] `yarn lint` + `yarn build`. Commit `Profile: profile, card, connect-request and level composables (#186, #206).`

---

## Phase C — Design gate

### Task 12: Design preview artifact

- [ ] Build an interactive HTML preview (retro arcade, real fonts VT323 / Press Start 2P / Silkscreen, brand colours from `src/assets/styles/colors.css`, real sprites from `src/assets`) covering every item of AC 13: profile page desktop + mobile, owner vs visitor, level header + progress bar (L0, mid, MAX), trophy shelf locked/unlocked, recent rounds, mini card popover + bottom sheet, `PlayerTag` with `LV n` badge on each surface (race card, bet list, Duel room + opponent, leaderboard row + podium, history YOU, home strip), slim home strip, PROFILE ☰ cell, states (loading, unavailable, self-excluded owner, no rounds, offline-retry).
- [ ] Publish it, give the owner the link, iterate until **explicit approval**; add the link to card #186 as a comment. **STOP here until approved.**

---

## Phase D — UI (`monkify-front`, only after Task 12 approval; the artifact wins on look)

### Task 13: `PlayerTag` + `PlayerCard`, mounted in `App.vue` (spec §7.3)
### Task 14: `PlayerProfilePage` + route `Player` + `EMBED_REDIRECT_ROUTES` (spec §7.4)
### Task 15: Tag surfaces — RaceScreen/RaceMainTab/playerCard, GameTerminal bet list + duel opponent, DuelBoard/DuelCard/DuelVsSplash, LeaderboardRow/Plinth, GameHistoryPage YOU (spec §7.5)
### Task 16: Home compact strip, ☰ PROFILE cell + icons, wallet pill "My profile" (both menus), stale comments (spec §7.6)

Each: implement from the approved artifact + spec section, `yarn lint` + `yarn build`, browser check on the worktree dev server (verified-free port) at desktop and CDP-emulated 360 px, commit per task.

### Task 17: Local QA + hand-off

- [ ] Rebuild backend from the worktree (`docker compose --project-name monkify-backend up -d --build --no-deps api consumers`), serve front from the worktree, run spec §13 Manual QA 1–9.
- [ ] Full `run_tests.bat` + `run_integration_tests.bat` green; hand off to the user (skill Step 9). No commit to `main`, no card move before the nod.
