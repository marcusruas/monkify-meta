# Windowed Stats + Query-Layer Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the Stats page materialising every completed round ever in application memory, add a Weekly/Monthly/All-time selector on top of it, and land four further query-layer fixes from the same audit.

**Architecture:** Aggregation moves out of C# and into Postgres — each stat becomes a bounded `count`/`sum`/`max` returning at most one row per active game type, never one row per round or bet. A `StatsWindow` enum adds an optional rolling date bound (7/30 days) that defaults to all-time so no shipped surface changes silently. `ICacheService` gains single-flight so a cache miss can't stampede. Three unrelated fixes ride along: an N+1 in the Orbit reconciliation job, `OFFSET` pagination on games history, and missing cancellation tokens.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, MediatR, xUnit + Shouldly + Moq, Testcontainers (PostgreSQL/Redis), Vue 3 + TypeScript.

**Authoritative spec:** `monkify-meta/specs/windowed-stats-query-performance.md`. Trello [#148](https://trello.com/c/UhmNIabO).

## Global Constraints

- **Branch is `feature/query-indexes`**, NOT `feature/<slug>`. Worktrees: `.worktrees/query-indexes/<repo>`. Never work in a hub; hubs stay clean on `main`.
- **Level is `Feature`** ⇒ a happy-path integration test is MANDATORY (Task 9).
- Backend tests run **only** via `scripts/run_tests.bat`; integration via `scripts/run_integration_tests.bat`. Never a bare `dotnet test` (the ProductionGames Monte Carlo suite is slow).
- **No new entity, no new column, no new migration.** The only migration on this branch is the existing `20260804173850_AddQueryCoveringIndexes`. If Task 10 concludes `GameBets(Status)` must go, **regenerate** that migration — do not stack a second one.
- EF mapping stays **inline** in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- `SpaceStrokerDbContext` is **NoTracking by default** — a load-mutate-`SaveChanges` silently persists nothing.
- Vague messaging: an unknown/inactive game type keeps collapsing to `ErrorMessages.ParameterNotFound`. The window must never become a channel that reveals whether a type exists.
- Mock-only values in any committed config file.
- Money rules unchanged: only `RewardForWinnersCompleted` rounds count; free bets (`Amount == 0`) are excluded from every money figure but **counted** in `TotalBetsPlaced` / `UniquePlayers` / `BetCount`.

**Deviation from the spec's task ordering, deliberate:** the spec lists `CancellationToken` threading as item [6]. This plan folds it into each service's own task instead, so every interface signature is edited **once** (window + token together) rather than twice.

---

### Task 1: `StatsWindow` value object

**Files:**
- Create: `monkify-backend/src/SpaceStroker.Domain/Games/ValueObjects/StatsWindow.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Domain/Games/StatsWindowTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum StatsWindow { All = 0, Week = 1, Month = 2 }`; `static StatsWindow StatsWindowExtensions.Parse(string? raw)`; `static DateTime? StatsWindowExtensions.Cutoff(this StatsWindow window, DateTime utcNow)`. Tasks 3–6 consume all three.

- [ ] **Step 1: Write the failing test**

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.ValueObjects;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games
{
    public class StatsWindowTests
    {
        [Theory]
        [InlineData("week", StatsWindow.Week)]
        [InlineData("WEEK", StatsWindow.Week)]
        [InlineData("month", StatsWindow.Month)]
        [InlineData("all", StatsWindow.All)]
        public void Parse_ShouldMapKnownValues_CaseInsensitively(string raw, StatsWindow expected)
            => StatsWindowExtensions.Parse(raw).ShouldBe(expected);

        // Decision 7: unrecognised input coerces to All rather than erroring, mirroring the history
        // endpoint's out-of-range page returning an empty page instead of a 400.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("fortnight")]
        public void Parse_ShouldCoerceUnknownToAll(string? raw)
            => StatsWindowExtensions.Parse(raw).ShouldBe(StatsWindow.All);

        [Fact]
        public void Cutoff_ShouldBeNullForAll()
            => StatsWindow.All.Cutoff(new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)).ShouldBeNull();

        [Fact]
        public void Cutoff_ShouldBeRollingSevenDaysForWeek()
        {
            var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
            StatsWindow.Week.Cutoff(now).ShouldBe(new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public void Cutoff_ShouldBeRollingThirtyDaysForMonth()
        {
            var now = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
            StatsWindow.Month.Cutoff(now).ShouldBe(new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts/run_tests.bat`
Expected: FAIL — `StatsWindow` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace SpaceStroker.Domain.Games.ValueObjects
{
    /// <summary>
    /// The time span a stats read covers. ROLLING, not calendar (refinement decision 3): rolling avoids
    /// committing to a timezone and a week-start, and the window is always full size, so tiles never
    /// collapse to near-zero on the 1st of a month.
    /// </summary>
    public enum StatsWindow
    {
        All = 0,     // sentinel: no date filter at all
        Week = 1,    // rolling: last 7 days
        Month = 2,   // rolling: last 30 days
    }

    public static class StatsWindowExtensions
    {
        /// <summary>
        /// LENIENT on purpose (refinement decision 7). An unrecognised value resolves to
        /// <see cref="StatsWindow.All"/> rather than a 400, mirroring the history endpoint where an
        /// out-of-range page returns an empty page instead of an error.
        /// </summary>
        public static StatsWindow Parse(string? raw)
            => raw?.Trim().ToLowerInvariant() switch
            {
                "week" => StatsWindow.Week,
                "month" => StatsWindow.Month,
                _ => StatsWindow.All,
            };

        /// <summary>Null for <see cref="StatsWindow.All"/> — the caller then applies no date bound.</summary>
        public static DateTime? Cutoff(this StatsWindow window, DateTime utcNow)
            => window switch
            {
                StatsWindow.Week => utcNow.AddDays(-7),
                StatsWindow.Month => utcNow.AddDays(-30),
                _ => null,
            };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `scripts/run_tests.bat`
Expected: PASS — 8 new cases green.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain/Games/ValueObjects/StatsWindow.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/StatsWindowTests.cs
git commit -m "Add StatsWindow value object with lenient parsing and rolling cutoffs."
```

---

### Task 2: Single-flight on `ICacheService`

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Cache/ICacheService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Cache/MemoryCacheService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Cache/RedisCacheService.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Services/CacheServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `CacheSettings(string Key, TimeSpan? AbsoluteExpiration)` — already declared in `CacheHelper.cs`, namespace `SpaceStroker.Infrastructure.Services.Cache`.
- Produces: `Task<T> ICacheService.GetOrCreateAsync<T>(CacheSettings settings, Func<Task<T>> factory)`. Tasks 3, 4 and 7 consume it.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetOrCreateAsync_ShouldRunFactoryOnce_UnderConcurrentMisses()
{
    var service = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
    var settings = new CacheSettings("single-flight-test", TimeSpan.FromMinutes(5));
    var invocations = 0;

    async Task<int> Factory()
    {
        Interlocked.Increment(ref invocations);
        await Task.Delay(50);       // hold the lock long enough for the others to queue
        return 42;
    }

    var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetOrCreateAsync(settings, Factory)));

    invocations.ShouldBe(1);
    results.ShouldAllBe(r => r == 42);
}

[Fact]
public async Task GetOrCreateAsync_ShouldNotCache_AndShouldStayUsable_WhenFactoryThrows()
{
    var service = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
    var settings = new CacheSettings("single-flight-throw", TimeSpan.FromMinutes(5));

    await Should.ThrowAsync<InvalidOperationException>(
        () => service.GetOrCreateAsync<int>(settings, () => throw new InvalidOperationException("boom")));

    // The semaphore must have been released in a finally, or this second call deadlocks forever.
    var recovered = await service.GetOrCreateAsync(settings, () => Task.FromResult(7));
    recovered.ShouldBe(7);
}

[Fact]
public async Task GetOrCreateAsync_ShouldNotTakeTheLock_OnAHit()
{
    var service = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
    var settings = new CacheSettings("single-flight-hit", TimeSpan.FromMinutes(5));

    await service.GetOrCreateAsync(settings, () => Task.FromResult(1));
    var second = await service.GetOrCreateAsync<int>(settings, () => throw new InvalidOperationException("factory must not run on a hit"));

    second.ShouldBe(1);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts/run_tests.bat`
Expected: FAIL — `GetOrCreateAsync` is not a member of `MemoryCacheService`.

- [ ] **Step 3: Write minimal implementation**

Interface:

```csharp
public interface ICacheService
{
    bool TryGet<T>(CacheSettings settings, out T value);
    void Set<T>(CacheSettings settings, T value);
    void Remove(CacheSettings settings);

    /// <summary>
    /// Read-through with SINGLE-FLIGHT: concurrent misses on one instance collapse to one factory
    /// invocation and the rest await its result. Bounds a cache-miss stampede on the expensive stats
    /// aggregations, which recur every 120s per key.
    ///
    /// ⚠️ Per-INSTANCE, not per-deployment. The API is multi-instance on Cloud Run, so N instances can
    /// still each run one aggregation. Accepted (refinement decision 6) — a Redis distributed lock on a
    /// public read path buys less than the failure modes it adds.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(CacheSettings settings, Func<Task<T>> factory);
}
```

Add to **both** `MemoryCacheService` and `RedisCacheService` (identical body — the locking is transport-agnostic; only `TryGet`/`Set` differ, and those are already virtual through the interface):

```csharp
// Keyed by cache key. Ceiling is 3 windows x (1 global + #game types) plus the history keys, so it is
// naturally bounded — deliberately NO eviction, which would race removal against acquisition for no
// real gain.
private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

public async Task<T> GetOrCreateAsync<T>(CacheSettings settings, Func<Task<T>> factory)
{
    if (TryGet<T>(settings, out var hit) && hit != null)
        return hit;

    var gate = Locks.GetOrAdd(settings.Key, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync();

    try
    {
        // Re-check: the winner may have populated the key while we queued.
        if (TryGet<T>(settings, out var populated) && populated != null)
            return populated;

        var value = await factory();
        Set(settings, value);
        return value;
    }
    finally
    {
        // MUST be a finally. A throwing factory that skipped this would deadlock every later reader of
        // this key for the life of the process.
        gate.Release();
    }
}
```

`RedisCacheService` needs `using System.Collections.Concurrent;`; `MemoryCacheService` already has it.

- [ ] **Step 4: Run test to verify it passes**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 5: Mirror the three cases onto Redis**

Add the same three tests to `UnitTests/Services/RedisCacheServiceTests.cs` against `RedisCacheService`, following that file's existing mocking of `IConnectionMultiplexer`/`IDatabase`.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services/Cache/ src/SpaceStroker.Tests/UnitTests/Services/CacheServiceTests.cs src/SpaceStroker.Tests/UnitTests/Services/RedisCacheServiceTests.cs
git commit -m "Add single-flight GetOrCreateAsync to ICacheService and both implementations."
```

---

### Task 3: `GlobalStatsService` — SQL aggregation + window + cancellation

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/IGlobalStatsService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/GlobalStatsService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/GlobalStats.cs` (add `AppliedWindow`)
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Services/GlobalStatsServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `StatsWindow`, `StatsWindowExtensions.Cutoff` (Task 1); `ICacheService.GetOrCreateAsync` (Task 2).
- Produces: `Task<GlobalStats> GetGlobalStats(IEnumerable<GameParameters> activeTypes, StatsWindow window, CancellationToken cancellationToken)`; `GlobalStats.AppliedWindow`; `CacheHelper.GenerateGlobalStatsCacheSettings(StatsWindow)`. Task 5 consumes the signature; Task 6 consumes `AppliedWindow`.

- [ ] **Step 1: Write the failing tests**

Extend `GlobalStatsServiceTests` with, at minimum:

```csharp
[Fact]
public async Task GetGlobalStats_Week_ShouldCountOnlyRoundsEndedInTheLastSevenDays() { /* seed 2 in-window + 2 out-of-window completed rounds; assert RoundsPlayed == 2 and money reflects only the in-window bets */ }

// Game.EndDate is NULLABLE even on completed rounds — features/games-history.md documents CreatedDate
// as the fallback. A round with a null EndDate but an in-window CreatedDate MUST be included.
[Fact]
public async Task GetGlobalStats_Week_ShouldFallBackToCreatedDate_WhenEndDateIsNull() { }

[Fact]
public async Task GetGlobalStats_All_ShouldMatchTodaysNumbers() { /* pins the no-regression case */ }

[Fact]
public async Task GetGlobalStats_ShouldExcludeFreeBetsFromMoney_ButCountThemInParticipation() { }

[Fact]
public async Task GetGlobalStats_ShouldAddLotteryBaseToBiggestPot_PerType() { }

[Fact]
public async Task GetGlobalStats_EmptyWindow_ShouldReturnZeroedStats_NotThrow() { }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — signature does not take a window.

- [ ] **Step 3: Add the window to the cache key**

```csharp
public static CacheSettings GenerateGlobalStatsCacheSettings(StatsWindow window)
    => new($"GlobalStats:{window}", TimeSpan.FromSeconds(120));
```

- [ ] **Step 4: Rewrite the service body**

Replace the three unbounded loads. Build the base queryables once:

```csharp
var cutoff = window.Cutoff(DateTime.UtcNow);

// ⚠️ NOT (g.EndDate ?? g.CreatedDate) >= cutoff — a COALESCE is unsargable and forfeits
// IX_Games_ParametersId_Status_EndDate_CreatedDate. Both arms below are index-usable.
IQueryable<Game> completed = _context.Games
    .Where(g => typeIds.Contains(g.ParametersId) && g.Status == GameStatus.RewardForWinnersCompleted);

if (cutoff != null)
    completed = completed.Where(g => g.EndDate >= cutoff || (g.EndDate == null && g.CreatedDate >= cutoff));

var bets = _context.GameBets.Join(completed, b => b.GameId, g => g.Id, (b, g) => b);

var credits = _context.TransactionLogs
    .Where(t => t.Type == TransactionLogType.Credit && t.BetId != null)
    .Join(bets, t => t.BetId!.Value, b => b.Id, (t, b) => t);
```

Then each figure is its own bounded aggregate — every one returns a single row except `BiggestPot`:

```csharp
result.RoundsPlayed        = await completed.CountAsync(ct);
result.TotalBetsPlaced     = await bets.CountAsync(ct);
result.UniquePlayers       = await bets.Select(b => b.PlayerId).Distinct().CountAsync(ct);
result.TotalVolumeWagered  = await bets.Where(b => b.Amount > 0m).SumAsync(b => (decimal?)b.Amount, ct) ?? 0m;
result.TotalPaidOut        = await credits.SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
result.WinnersPaid         = await credits.Select(t => t.BetId).Distinct().CountAsync(ct);
result.BiggestSinglePayout = await credits.GroupBy(t => t.BetId)
                                          .Select(g => g.Sum(x => x.Amount))
                                          .DefaultIfEmpty(0m).MaxAsync(ct);
```

`BiggestPot` needs the double-group so the result set stays bounded — grouping to one row **per round** would reintroduce exactly the unbounded materialization this task removes:

```csharp
var maxPotPerType = await _context.GameBets
    .Where(b => b.Amount > 0m)
    .Join(completed, b => b.GameId, g => g.Id, (b, g) => new { g.ParametersId, b.GameId, b.Amount })
    .GroupBy(x => new { x.ParametersId, x.GameId })
    .Select(g => new { g.Key.ParametersId, Pot = g.Sum(x => x.Amount) })
    .GroupBy(x => x.ParametersId)
    .Select(g => new { ParametersId = g.Key, MaxPot = g.Max(x => x.Pot) })
    .ToListAsync(ct);                                     // <= #active types rows

result.BiggestPot = maxPotPerType
    .Select(x => x.MaxPot + lotteryBaseByType[x.ParametersId])
    .DefaultIfEmpty(0m).Max();
```

`BiggestWinMultiplier` — per-bet credit sum joined back to stake, maximum taken in SQL:

```csharp
result.BiggestWinMultiplier = await credits
    .GroupBy(t => t.BetId)
    .Select(g => new { BetId = g.Key, Sum = g.Sum(x => x.Amount) })
    .Join(bets.Where(b => b.Amount > 0m), c => c.BetId!.Value, b => b.Id, (c, b) => c.Sum / b.Amount)
    .DefaultIfEmpty(0m).MaxAsync(ct);
```

`MostPlayedType*` — one row:

```csharp
var top = await completed.GroupBy(g => g.ParametersId)
    .Select(g => new { Id = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .FirstOrDefaultAsync(ct);
```

Wrap the whole body in `_cache.GetOrCreateAsync(cacheSettings, async () => { … })` and set `result.AppliedWindow = window`.

**Accepted trade, state it in a comment:** ~8 round trips instead of 3. Each is bounded and index-served, so total latency falls sharply even though the query count rises.

- [ ] **Step 5: Run tests to verify they pass**

Run: `scripts/run_tests.bat`
Expected: PASS, including the pre-existing `GlobalStatsServiceTests` cases unchanged (the `All` window must reproduce today's numbers exactly).

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services/Games/ src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs src/SpaceStroker.Tests/UnitTests/Services/GlobalStatsServiceTests.cs
git commit -m "Aggregate global stats in SQL and add the rolling window."
```

---

### Task 4: `GameStatsService` — same treatment

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/IGameStatsService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/GameStatsService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Services/GameStatsServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `StatsWindow` (Task 1), `GetOrCreateAsync` (Task 2), the window-filter shape from Task 3 Step 4.
- Produces: `Task<GameTypeStats> GetStats(GameParameters parameters, StatsWindow window, CancellationToken cancellationToken)`; `CacheHelper.GenerateGameStatsCacheSettings(Guid, StatsWindow)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task GetStats_Week_ShouldScopeEveryTileToTheWindow() { }
[Fact] public async Task GetStats_Week_ShouldFallBackToCreatedDate_WhenEndDateIsNull() { }
[Fact] public async Task GetStats_ShouldReturnHottestChoiceWithinTheWindowOnly() { }
[Fact] public async Task GetStats_ShouldKeepTheOrdinalTiebreak_OnEqualPickCounts() { }
[Fact] public async Task GetStats_FreeTextType_ShouldReturnNoHottestChoice() { }
[Fact] public async Task GetStats_EmptyWindow_ShouldReturnZeroedStats() { }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts/run_tests.bat` → FAIL (signature).

- [ ] **Step 3: Cache key + implementation**

```csharp
public static CacheSettings GenerateGameStatsCacheSettings(Guid parametersId, StatsWindow window)
    => new($"GameStats:{parametersId}:{window}", TimeSpan.FromSeconds(120));
```

Same `completed`/`bets`/`credits` composition as Task 3 but scoped to `parameters.Id`. `BiggestPot` is a single group here (one type, so the lottery base is a scalar):

```csharp
var maxPot = await _context.GameBets
    .Where(b => b.Amount > 0m)
    .Join(completed, b => b.GameId, g => g.Id, (b, g) => new { b.GameId, b.Amount })
    .GroupBy(x => x.GameId)
    .Select(g => g.Sum(x => x.Amount))
    .DefaultIfEmpty(0m).MaxAsync(ct);

stats.BiggestPot = maxPot + lotteryBase;
```

Hottest choice moves into SQL, preserving the existing count-desc / choice-asc tiebreak:

```csharp
if (parameters.PresetChoices != null && parameters.PresetChoices.Any())
{
    var top = await bets.Where(b => b.Amount > 0m)
        .GroupBy(b => b.Choice)
        .Select(g => new { Choice = g.Key, Count = g.Count() })
        .OrderByDescending(x => x.Count).ThenBy(x => x.Choice)
        .FirstOrDefaultAsync(ct);

    if (top != null)
        stats.HottestChoice = new HottestChoice { Choice = top.Choice, PickCount = top.Count };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts/run_tests.bat` → PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Aggregate per-type stats in SQL and add the rolling window."
```

---

### Task 5: Endpoints, requests, handlers, DTOs

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Api/Controllers/GamesController.cs` (`:57` global, `:90` per-type)
- Modify: `.../Handlers/Games/GetGlobalStats/GetGlobalStatsRequest.cs` + `GetGlobalStatsHandler.cs`
- Modify: `.../Handlers/Games/GetGameTypeStats/GetGameTypeStatsRequest.cs` + `GetGameTypeStatsHandler.cs`
- Modify: `.../Contracts/Games/GlobalStatsDto.cs`, `GameTypeStatsDto.cs`
- Test: `UnitTests/Handlers/GetGlobalStatsHandlerTests.cs`, `UnitTests/Handlers/GetGameTypeStatsHandlerTests.cs`, `UnitTests/Contracts/GlobalStatsDtoTests.cs` (all extend)

**Interfaces:**
- Consumes: `StatsWindowExtensions.Parse` (Task 1); the service signatures from Tasks 3–4.
- Produces: `GET /games/stats?window=`, `GET /games/{parametersId}/stats?window=`; both DTOs carry `Window` (serialized lowercase: `"all" | "week" | "month"`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Handler_ShouldDefaultToAll_WhenWindowOmitted() { }
[Fact] public async Task Handler_ShouldResolveWeek() { }
[Fact] public async Task Handler_ShouldCoerceUnknownWindowToAll() { }
[Fact] public async Task Handler_ShouldForwardTheCancellationToken() { }

// Vague messaging must survive the new param — the window is not an existence oracle.
[Fact] public async Task GetGameTypeStats_UnknownType_ShouldReturnParameterNotFound_ForEveryWindow() { }

[Fact] public void Dto_ShouldEchoTheAppliedWindow_IncludingACoercedOne() { }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts/run_tests.bat` → FAIL.

- [ ] **Step 3: Implement**

Controller — parse at the edge so an invalid value can never reach the handler:

```csharp
[HttpGet("stats")]
public async Task<IActionResult> GetGlobalStats([FromQuery] string? window = null)
{
    return await ProcessRequest(new GetGlobalStatsRequest(StatsWindowExtensions.Parse(window)));
}

[HttpGet("{parametersId}/stats")]
public async Task<IActionResult> GetGameTypeStats(Guid parametersId, [FromQuery] string? window = null)
{
    return await ProcessRequest(new GetGameTypeStatsRequest(parametersId, StatsWindowExtensions.Parse(window)));
}
```

Requests:

```csharp
public record GetGlobalStatsRequest(StatsWindow Window) : IRequest<GlobalStatsDto> { }
public record GetGameTypeStatsRequest(Guid ParametersId, StatsWindow Window) : IRequest<GameTypeStatsDto> { }
```

Handlers pass `request.Window` and `cancellationToken` through. **Do not reorder** the existing
`parameters == null || !parameters.Active` guard in `GetGameTypeStatsHandler` — it must still run
before any stats work so the vague-error path is unchanged.

DTOs gain `public string Window { get; set; }`, set from the service's applied window lowercased
(`stats.AppliedWindow.ToString().ToLowerInvariant()`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts/run_tests.bat` → PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Expose the stats window on both endpoints and echo it in the DTOs."
```

---

### Task 6: Frontend — the window selector

**Files:**
- Modify: `monkify-front/src/types/index.ts` (`GameTypeStats` `:64`, `GlobalStats` `:77`)
- Modify: `monkify-front/src/services/mainService.ts` (`getGameTypeStats` `:155`, `getGlobalStats` `:162`)
- Modify: `monkify-front/src/pages/GameStatsPage.vue`

**Interfaces:**
- Consumes: the `window` query param and DTO field from Task 5.
- Produces: no exported API beyond the page itself.

⚠️ **`src/components/game/GameStatsPanel.vue` is DEAD CODE** — nothing has rendered it since
`fair-stats-menu` moved stats to the page; the only reference is a comment in `GameFairPanel.vue:18`.
Do **not** touch it. Deleting it is out of scope.

- [ ] **Step 1: Types**

```typescript
export type StatsWindow = 'all' | 'week' | 'month';
```

Add `window: StatsWindow;` to both `GlobalStats` and `GameTypeStats`.

- [ ] **Step 2: Service**

```typescript
const getGameTypeStats = async (parametersId: string, window: StatsWindow = 'all') => {
  const params = window === 'all' ? undefined : { window };
  const { data } = await mainAPI.get<DefaultResponse<GameTypeStats>>(
    `/games/${parametersId}/stats`, { params }
  );
  return data;
};

const getGlobalStats = async (window: StatsWindow = 'all') => {
  const params = window === 'all' ? undefined : { window };
  const { data } = await mainAPI.get<DefaultResponse<GlobalStats>>('/games/stats', { params });
  return data;
};
```

Omitting the param for `all` keeps the shipped URL shape byte-identical in the default case
(acceptance criterion 1). Follow the existing `singleFlight` helper (`:175`) and put the window in the
flight key so two windows can never collapse onto one promise.

- [ ] **Step 3: Page**

- Add `const window = ref<StatsWindow>('all')` and a three-option control, defaulting to **All time**.
- On change, re-fetch **both** sections.
- ⚠️ **`loadGlobal` (`:34`) has no request-sequence guard.** The per-type loader has one (`reqSeq`,
  `:67`); add the equivalent to `loadGlobal`, or a slow `all` response will land after a fast `week`
  response and repaint stale numbers.
- Label each tile from the DTO's echoed `window`, **not** from local state, so a coerced value shows
  the truth.
- Leave the page-level house-cut badge (`:21-23`) outside the windowed sections — it comes from
  configuration, not from rounds.

- [ ] **Step 4: Verify the build**

Run: `yarn lint && yarn build` in `.worktrees/query-indexes/monkify-front`.
⚠️ Prettier enforces `endOfLine: lf` — CRLF fails the build. Fix with `unix2dos`/`dos2unix` as needed.

- [ ] **Step 5: Commit**

```bash
git commit -am "Add the Weekly/Monthly/All-time selector to the stats page."
```

---

### Task 7: `GameHistoryService` — keyset pagination, page clamp, cancellation

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/IGameHistoryService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Handlers/Games/GetGamesHistory/GetGamesHistoryHandler.cs`
- Test: `UnitTests/Services/GameHistoryServiceTests.cs`, `UnitTests/Handlers/GetGamesHistoryHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `IX_Games_ParametersId_Status_EndDate_CreatedDate` from the branch's existing migration.
- Produces: unchanged public contract — still `PaginatedList<GameHistoryDto>`, still an empty page past the end.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task GetHistory_ShouldReturnEveryRoundExactlyOnce_WalkingAllPages() { }
[Fact] public async Task GetHistory_ShouldHandleTiesOnEndDate_WithoutDuplicatingOrSkipping() { }
[Fact] public async Task GetHistory_PastTheLastPage_ShouldReturnAnEmptyPage_NotThrow() { }
[Fact] public async Task Handler_ShouldClampPageNumberToTheRealPageCount() { }
```

The tie test is the one that matters: `EndDate` collisions are what a naive keyset gets wrong.

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts/run_tests.bat` → FAIL.

- [ ] **Step 3: Implement**

- In `BuildPageAsync`, replace `Skip((pageNumber-1) * pageSize)` with a keyset seek on the
  `(EndDate, CreatedDate)` tuple, matching the index's `DESC, DESC` direction. Keep the existing
  `OrderByDescending(EndDate).ThenByDescending(CreatedDate)`.
- Keep the **total count** query as-is — it already runs as an index-only scan on
  `(ParametersId, Status)`, which is why the branch's migration keeps that narrow index.
- In `GetGamesHistoryHandler`, add the upper clamp beside the existing lower one:

```csharp
var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
var pageNumber = Math.Max(1, request.PageNumber);
// Upper-clamping bounds the GamesHistory:{id}:{page}:{size} cache key, which was previously
// unbounded — an iterating caller could mint unlimited 5-minute keys, each holding a page of rows.
```

- Thread `CancellationToken` through both `IGameHistoryService` methods and every EF call in them.

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts/run_tests.bat` → PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Replace OFFSET history pagination with a keyset seek and clamp pageNumber."
```

---

### Task 8: `OrbitReconciliationJob` — collapse the N+1

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Jobs/OrbitReconciliationJob.cs` (`:36-51`)
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Jobs/OrbitReconciliationJobTests.cs` (create if absent)

**Interfaces:**
- Consumes: nothing from earlier tasks — fully independent, can be done in any order.
- Produces: no signature change; `Run()` is unchanged externally.

- [ ] **Step 1: Write the failing tests**

```csharp
// The LEFT JOIN case. A balance row with a non-zero column and NO transactions at all is exactly the
// drift this job exists to catch — an INNER JOIN would silently skip it and turn a loud bug quiet.
[Fact] public async Task ReconcilePlayerColumns_ShouldReportABalanceWithNoTransactions() { }

[Fact] public async Task ReconcilePlayerColumns_ShouldReportADriftedBalance() { }
[Fact] public async Task ReconcilePlayerColumns_ShouldStaySilentWhenColumnMatchesLedger() { }
[Fact] public async Task ReconcilePlayerColumns_ShouldIssueOneQuery_NotOnePerBalanceRow() { }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts/run_tests.bat` → FAIL (the no-transactions case reports nothing today only if the join is wrong; the query-count case fails outright).

- [ ] **Step 3: Implement**

Replace the `foreach` over `balances` (one `SumAsync` per row) with a single grouped query that LEFT
JOINs balances to their ledger sum, then log the mismatches. `ReconcilePoolsAgainstChain` is
**unchanged** — it is bounded by pool count and its per-pool on-chain read cannot be batched.

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts/run_tests.bat` → PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "Collapse the Orbit reconciliation N+1 into one grouped query."
```

---

### Task 9: Mandatory happy-path integration test

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Tests.Integration/GlobalStatsFlowTests.cs`
- Modify: `monkify-backend/src/SpaceStroker.Tests.Integration/GameStatsFlowTests.cs`

**Interfaces:**
- Consumes: the endpoints from Task 5.
- Produces: nothing consumed downstream.

This task is **MANDATORY** — the card's `Level` is `Feature` (`constraints.md` §3).

- [ ] **Step 1: Write the integration test**

Following the `GameFlowTests` pattern (real API + Consumers hosts against real Postgres/Redis
containers, Solana mocked): seed a type, drive at least two rounds to settlement through the real API,
then assert:

- `GET /games/stats` (no param) returns the all-time payload.
- `GET /games/stats?window=week` counts the just-settled rounds (they are seconds old).
- `GET /games/stats?window=fortnight` returns **200** with the all-time payload, not a 400.
- `GET /games/{id}/stats?window=week` agrees with the global figures for a single-type deployment.

⚠️ The harness's caches are **global and shared across tests** — assert deltas or invalidate
explicitly, or a neighbouring test's rounds will bleed into these numbers.

- [ ] **Step 2: Run the integration suite**

Run: `scripts/run_integration_tests.bat` (Docker required)
Expected: PASS, 98 existing + the new cases.

- [ ] **Step 3: Commit**

```bash
git commit -am "Add the happy-path integration test for windowed stats."
```

---

### Task 10: Write-cost benchmark and the `GameBets(Status)` decision

**Files:**
- Create: `monkify-backend/src/SpaceStroker.Tests.Integration/Benchmarks/WriteBenchmarkTests.cs`
- Modify: `monkify-backend/src/SpaceStroker.Tests.Integration/Benchmarks/BenchmarkQueries.cs` (add the windowed query so its plan is verified)
- Possibly modify: `SpaceStrokerDbContext.cs` + regenerate `AddQueryCoveringIndexes`

**Interfaces:**
- Consumes: the existing `BenchmarkSeeder`, `IndexSets`, `IndexBenchmarkFixture`.
- Produces: `TestReports/write-benchmark.md`.

This is the **gate the card owns**. All evidence behind `AddQueryCoveringIndexes` is read latency.

- [ ] **Step 1: Add the write phase**

Under each index set, at the 1M scale: time N single-row bet **inserts** and N single-row bet **status
updates**, each in its own transaction (the shape `RegisterBetHandler` and
`UpdateBetStatusWithoutSaving` actually issue — not a bulk load). Reset counters with
`pg_stat_reset_single_table_counters('"GameBets"'::regclass)` before each run and read
`n_tup_upd` / `n_tup_hot_upd` from `pg_stat_user_tables` after.

- [ ] **Step 2: Add the windowed query to the read rig**

Add the `window=week` global-stats query to `BenchmarkQueries.All(...)` and confirm from its `EXPLAIN`
that the filter from Task 3 Step 4 produces an **index scan**, not a Seq Scan.

- [ ] **Step 3: Run and decide**

Run: `scripts/run_index_benchmarks.bat`

- If `GameBets(Status)` is **net-positive or neutral** → keep it; record the numbers on the card.
- If **net-negative** → remove `builder.HasIndex(x => x.Status)` from the `Bet` block in
  `SpaceStrokerDbContext`, then `dotnet ef migrations remove` + `dotnet ef migrations add
  AddQueryCoveringIndexes` (regenerate — it has not shipped, so do not stack a second migration).
  Re-run `scripts/run_tests.bat` and `scripts/run_integration_tests.bat`.

- [ ] **Step 4: Commit**

```bash
git commit -am "Measure the index write cost and settle the GameBets(Status) decision."
```

---

## Self-Review

**Spec coverage:** §4.4 → Task 1. §6.4 → Task 2. §6.1–6.2 → Task 3. §6.3 → Task 4. §6.5 → Task 5. §7 → Task 6. §6.7 → Task 7. §6.6 → Task 8. §11 integration → Task 9. §6.9 → Task 10. §6.8 cancellation is folded into Tasks 3, 4, 5 and 7 rather than standing alone (see Global Constraints). No spec section is unimplemented.

**Type consistency:** `StatsWindow` / `StatsWindowExtensions.Parse` / `.Cutoff(DateTime)` are used with those exact names in Tasks 3–6. `GetOrCreateAsync<T>(CacheSettings, Func<Task<T>>)` matches between Task 2's definition and Tasks 3–4's use. `AppliedWindow` is produced in Task 3 and consumed in Task 5. Cache-key helpers keep their existing names, gaining a `StatsWindow` parameter.

**Known gap, deliberate:** the acceptance criterion "no service loads rows into memory to aggregate them" is asserted by code review and the `EXPLAIN` output in Task 10 Step 2, not by an automated test — there is no clean hook to assert "EF returned ≤ N rows" from a unit test. Flagged rather than faked.
