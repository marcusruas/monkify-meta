# Monkey Duel — the Treasury Is the House — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A House duel has no house bet row: the house word is drawn onto the round when the creator's bet lands, the treasury pays a winning player 1.98 × stake, and the treasury check is shared with Hyper Roll.

**Architecture:** New nullable `Game.HouseWord`. `RegisterBetHandler.RegisterDuelBet` draws and stores it (under the duel lock, and — for a paid bet — the shared treasury lock). The typer races the creator's bet against `HouseWord` (Hyper Roll's "board word with no bet" pattern); the reward pot counts the House round's stake once more for the house's virtual side, so the existing commission maths yields 1.98 × stake. `HouseEdgeCoverage` gains a flat multiplier so House duels are exposure in the same budget. The HouseSeed counterparty service, its consumer/event and the HouseSeed duel coverage are deleted; the duel advisory lock survives as `IDuelLockService`.

**Tech Stack:** .NET 8, EF Core (Npgsql), MassTransit, xUnit + Shouldly + Moq, Testcontainers; Vue 3 + TypeScript.

**Spec:** `monkify-meta/specs/duel-treasury-house.md` (read it first).

## Global Constraints

- Worktrees: `E:\Monkify\.worktrees\duel-treasury-house\{monkify-backend,monkify-front,monkify-meta}`, branch `feature/duel-treasury-house`. Never edit the hubs. Push after each commit (`git push origin feature/duel-treasury-house`).
- Files are **CRLF**; C# files start with a UTF-8 BOM. Edit with tools that preserve both; never `sed -i` over `.cs` (flips CRLF repo-wide).
- Unit tests: `cd monkify-backend/src/SpaceStroker.Tests && dotnet test --filter "FullyQualifiedName~UnitTests&FullyQualifiedName!~ProductionGames"`. Integration: `cd monkify-backend/src/SpaceStroker.Tests.Integration && dotnet test --filter "FullyQualifiedName~<Class>"` (Docker must be running). Never the whole `dotnet test`.
- Migrations: `dotnet ef migrations add <Name> --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Consumers` from `monkify-backend` — **never `--no-build`**; read the generated file.
- Every refusal keeps `ErrorMessages.HouseEdgeUnderMaintenance` ("Mode is under maintenance. Please try again later.") — no new player-facing copy (constraints §6).
- House duel payout multiplier = `2 − CommissionPercentage/100` (1.98 at 2%). Commission comes from `GeneralSettings.Operator.Token.CommissionPercentage`.
- The house word draw is unchanged: `DuelHouseWordGenerator.DeriveWord(serverSeed, gameId, attempt, pool)` for attempts 0..9, skipping words already taken in the round. KEEP-IN-SYNC with `monkify-front/src/utils/provablyFair.ts` and `public/verify.html`.
- OpenRoom (PvP) duels, Cosmic Sprint's house seed and Hyper Roll behaviour must not change.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

---

### Task 1: `Game.HouseWord` — column, DTO, conditional write

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Domain/Games/Entities/Game.cs` (after `DuelPoolSnapshot`, ~line 132)
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (Game config, next to `DuelPoolSnapshot` ~line 180)
- Create: migration `DuelHouseWordOnRound` (generated)
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs` (lines ~42-78)
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/IGameService.cs`, `GameService.cs` (next to `TryBindDuelRound`, ~line 744)
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelEntityMappingTests.cs`, `.../UnitTests/Contracts/DuelContractTests.cs`

**Interfaces — Produces:**
- `Game.HouseWord : string?` (varchar 20)
- `ActiveGameDto.HouseWord : string?`; `ActiveGameDto.IsFull` = `ClaimedByPlayerId != null || HouseWord != null`
- `IGameService.TrySetDuelHouseWord(Guid gameId, string houseWord) : Task<bool>` — `UPDATE "Games" SET "HouseWord"=@word, "UpdatedDate"=@updatedDate WHERE "Id"=@id AND "HouseWord" IS NULL AND "DuelKind" = 1`
- `GameService.SetDuelHouseWordSql` (public const, like `BindDuelRoundSql`)

- [ ] **Step 1: failing tests.** In `DuelContractTests` add:
```csharp
[Fact]
public void AHouseRoundWithAHouseWord_IsFull_AndPublishesTheWord()
{
    var game = new Game(Parameters.Id) { DuelKind = DuelKind.House, Stake = 0.05m, HouseWord = "meteor", Parameters = Parameters };
    var dto = new ActiveGameDto(game, null);
    dto.IsFull.ShouldBeTrue();
    dto.HouseWord.ShouldBe("meteor");
}

[Fact]
public void AnOpenRoomIsFullOnlyOnceClaimed()
{
    var game = new Game(Parameters.Id) { DuelKind = DuelKind.OpenRoom, Stake = 0.05m, Parameters = Parameters };
    new ActiveGameDto(game, null).IsFull.ShouldBeFalse();
    game.ClaimedByPlayerId = Guid.NewGuid();
    new ActiveGameDto(game, null).IsFull.ShouldBeTrue();
}
```
(Reuse the file's existing `Parameters` fixture name — read the file; adapt the fixture accessor, not the assertions.) In `DuelEntityMappingTests` add a round-trip: save a `Game` with `HouseWord = "planet"` through the in-memory context and read it back equal; and assert the EF model's `HouseWord` max length is 20 (`context.Model.FindEntityType(typeof(Game))!.FindProperty(nameof(Game.HouseWord))!.GetMaxLength().ShouldBe(20)`).
- [ ] **Step 2:** run `--filter "FullyQualifiedName~DuelContractTests|FullyQualifiedName~DuelEntityMappingTests"` → compile FAIL (no `HouseWord`).
- [ ] **Step 3: implement.** `Game.cs`:
```csharp
/// <summary>
/// Monkey Duel, House kind: the house's word, drawn from the committed seed when the creator's bet
/// registers (specs/duel-treasury-house.md §4.1). The house places NO bet; the typer races this word.
/// Null on every other round.
/// </summary>
public string? HouseWord { get; set; }
```
DbContext: `builder.Property(x => x.HouseWord).HasMaxLength(20).IsRequired(false);`. DTO: `public string? HouseWord { get; set; }`, set `HouseWord = game.HouseWord;`, `IsFull = game.ClaimedByPlayerId != null || game.HouseWord != null;`. GameService:
```csharp
public const string SetDuelHouseWordSql =
    @"UPDATE public.""Games""
         SET ""HouseWord"" = @houseWord,
             ""UpdatedDate"" = @updatedDate
       WHERE ""Id"" = @id AND ""HouseWord"" IS NULL AND ""DuelKind"" = @house";

public async Task<bool> TrySetDuelHouseWord(Guid gameId, string houseWord)
{
    var affectedRows = await _context.Database.ExecuteSqlRawAsync(
        SetDuelHouseWordSql,
        new Npgsql.NpgsqlParameter("@houseWord", houseWord),
        new Npgsql.NpgsqlParameter("@updatedDate", DateTime.UtcNow),
        new Npgsql.NpgsqlParameter("@id", gameId),
        new Npgsql.NpgsqlParameter("@house", (int)DuelKind.House));
    return affectedRows > 0;
}
```
Then generate the migration `DuelHouseWordOnRound`; it must contain exactly `AddColumn<string>("HouseWord", "Games", maxLength: 20, nullable: true)` and its drop in `Down`.
- [ ] **Step 4:** tests PASS; full unit suite PASS.
- [ ] **Step 5:** commit "Add Game.HouseWord for a House duel's drawn word, published on the round DTO."

---

### Task 2: Shared coverage understands a flat duel multiplier

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Domain/Games/Services/HouseEdgeCoverage.cs`
- Create: `monkify-backend/src/SpaceStroker.Domain/Games/Services/DuelHouseRules.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeCoverageTests.cs` (existing file; add cases) and new `DuelHouseRulesTests.cs` next to it

**Interfaces — Produces:**
- `HouseEdgeRoundBook(Guid GameId, IReadOnlyDictionary<string,decimal> Multipliers, IReadOnlyCollection<HouseEdgeStake> Stakes, decimal? FlatMultiplier = null)` — when set, every word pays `FlatMultiplier`.
- `HouseEdgeNewBet(Guid? GameId, string Choice, decimal Amount, decimal? Multiplier = null)` — used to price the bet when its round is not in the book (instead of `HouseEdgeRules.MaxMultiplier`).
- `DuelHouseRules.PayoutMultiplier(decimal commissionPercentage) : decimal` = `2m - commissionPercentage / 100m`
- `DuelHouseRules.MaxStakeFromHeadroom(decimal headroom, decimal payoutMultiplier, decimal typeMaximumStake) : decimal` = `Math.Clamp(headroom / payoutMultiplier, 0, typeMaximumStake)` rounded **down** to 9 dp.

- [ ] **Step 1: failing tests.**
```csharp
public class DuelHouseRulesTests
{
    [Theory]
    [InlineData(2.0, 1.98)]
    [InlineData(0, 2.0)]
    public void PayoutMultiplier_IsTwoLessTheCut(decimal commission, decimal expected)
        => DuelHouseRules.PayoutMultiplier(commission).ShouldBe(expected);

    [Fact]
    public void MaxStake_IsHeadroomOverTheMultiplier_ClampedToTheType()
    {
        DuelHouseRules.MaxStakeFromHeadroom(0.099m, 1.98m, 0.1m).ShouldBe(0.05m);
        DuelHouseRules.MaxStakeFromHeadroom(10m, 1.98m, 0.1m).ShouldBe(0.1m);
        DuelHouseRules.MaxStakeFromHeadroom(-1m, 1.98m, 0.1m).ShouldBe(0m);
    }
}
```
In `HouseEdgeCoverageTests` add:
```csharp
[Fact]
public void AHouseDuelRound_IsPricedAtItsFlatMultiplier()
{
    var duel = new HouseEdgeRoundBook(Guid.NewGuid(), new Dictionary<string, decimal>(),
        [new HouseEdgeStake("quirky", 0.1m)], FlatMultiplier: 1.98m);
    var d = HouseEdgeCoverage.Evaluate(1m, [duel], new HouseEdgeNewBet(null, "banana", 0m, Multiplier: 1.98m));
    d.Exposure.ShouldBe(0.198m);
}

[Fact]
public void ANewDuelBetInItsOwnRound_UsesTheFlatMultiplier_NotTheLadderMax()
{
    var id = Guid.NewGuid();
    var own = new HouseEdgeRoundBook(id, new Dictionary<string, decimal>(), [], FlatMultiplier: 1.98m);
    HouseEdgeCoverage.Evaluate(1m, [own], new HouseEdgeNewBet(id, "quirky", 0.1m, 1.98m)).Exposure.ShouldBe(0.198m);
}

[Fact]
public void ANewDuelBetWithNoRoundInTheBook_UsesItsOwnMultiplier()
    => HouseEdgeCoverage.Evaluate(1m, [], new HouseEdgeNewBet(null, "quirky", 0.1m, 1.98m)).Exposure.ShouldBe(0.198m);

[Fact]
public void HyperRollAndHouseDuelExposure_Share_OneBudget()
{
    var hyper = new HouseEdgeRoundBook(Guid.NewGuid(), new Dictionary<string, decimal> { ["banana"] = 10m },
        [new HouseEdgeStake("banana", 0.05m)]);                                   // 0.5 exposure
    var duel = new HouseEdgeRoundBook(Guid.NewGuid(), new Dictionary<string, decimal>(),
        [new HouseEdgeStake("quirky", 0.1m)], FlatMultiplier: 1.98m);               // 0.198 exposure
    var d = HouseEdgeCoverage.Evaluate(0.7m, [hyper, duel], new HouseEdgeNewBet(null, "x", 0.01m, 1.98m));
    d.Exposure.ShouldBe(0.5m + 0.198m + 0.0198m);
    d.IsCoverable.ShouldBeFalse();
}
```
- [ ] **Step 2:** run `--filter "FullyQualifiedName~HouseEdgeCoverageTests|FullyQualifiedName~DuelHouseRulesTests"` → FAIL (compile).
- [ ] **Step 3: implement.** Add the optional record parameters. In `Evaluate`: `if (ownRound == null) exposure += newBet.Amount * (newBet.Multiplier ?? HouseEdgeRules.MaxMultiplier);`. In `WorstCase`: `decimal MultiplierOf(string choice) => round.FlatMultiplier ?? (round.Multipliers.TryGetValue(choice, out var m) ? m : bestOnBoard);`. Create:
```csharp
namespace SpaceStroker.Domain.Games.Services
{
    /// <summary>
    /// Monkey Duel against the house (specs/duel-treasury-house.md): the treasury is the counterparty, and a
    /// winning player is paid the two-stake pot less the cut on the profit — stake × (2 − rate).
    /// </summary>
    public static class DuelHouseRules
    {
        public static decimal PayoutMultiplier(decimal commissionPercentage) => 2m - commissionPercentage / 100m;

        public static decimal MaxStakeFromHeadroom(decimal headroom, decimal payoutMultiplier, decimal typeMaximumStake)
        {
            if (headroom <= 0m || payoutMultiplier <= 0m)
                return 0m;
            var raw = Math.Min(headroom / payoutMultiplier, typeMaximumStake);
            return Math.Round(raw, 9, MidpointRounding.ToZero);
        }
    }
}
```
- [ ] **Step 4:** PASS (and the whole `HouseEdgeCoverageTests` still PASS — existing Hyper Roll cases unchanged).
- [ ] **Step 5:** commit "Let the shared treasury coverage price a House duel at its flat 1.98 multiplier."

---

### Task 3: `HouseEdgeCoverageService` counts House duels as exposure

**Files:**
- Modify: `monkify-backend/src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs`
- Test: `monkify-backend/src/SpaceStroker.Tests/UnitTests/Services/HouseEdgeCoverageServiceTests.cs` (existing; if absent, create with the in-memory context pattern used by `BetPreChecksTests`)

**Interfaces — Consumes:** Task 2 records. **Produces:** `ExposureRounds` also selects House duel rounds; their book has `FlatMultiplier = DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage)`.

- [ ] **Step 1: failing test.** Persist (in-memory) one House duel round `Status=WaitingBets, DuelKind=House, Parameters.IsDuel=true, Stake=0.1` with one paid bet `quirky` 0.1; mock `ISolanaService.GetSolBalanceForRequest(treasury)` → 1m; settings `CommissionPercentage = 2`. `EvaluateAsync(new HouseEdgeNewBet(null, "x", 0m, 1.98m))` → `Exposure == 0.198m`. Second case: same round but `DuelKind = OpenRoom` → `Exposure == 0m` (PvP is never treasury exposure).
- [ ] **Step 2:** FAIL (exposure 0).
- [ ] **Step 3: implement.** `ExposureRounds` predicate:
```csharp
return games.Where(x => (x.Parameters.IsHouseEdge || (x.Parameters.IsDuel && x.DuelKind == DuelKind.House))
    && unsettled.Contains(x.Status)
    && (x.Id == newBetGameId || x.Bets.Any(b => b.Amount > 0 && !SettledBetStatuses.Contains(b.Status))));
```
Projection adds `IsDuel = x.Parameters.IsDuel`; book: `new HouseEdgeRoundBook(x.Id, board, stakes, x.IsDuel ? duelMultiplier : null)` where `var duelMultiplier = DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage);`. Update the doc comments (the lock and the exposure now cover House duels too).
- [ ] **Step 4:** PASS; full `HouseEdgeCoverage*` + Hyper Roll unit tests PASS.
- [ ] **Step 5:** commit "Count House duel rounds in the shared treasury exposure."

---

### Task 4: The duel lock as its own service

**Files:**
- Create: `SpaceStroker.Infrastructure/Services/Games/DuelLockService.cs`, `IDuelLockService.cs`
- Modify: `SpaceStroker.Infrastructure/DependencyInjection.cs:~60` (register next to the existing duel coverage registration)
- Create test: `SpaceStroker.Tests/UnitTests/Services/DuelLockServiceTests.cs`

**Interfaces — Produces:**
```csharp
public interface IDuelLockService
{
    /// Takes the duel advisory lock (same key and lock_timeout as DuelHouseCoverageService) inside a
    /// transaction it owns, or inside the caller's open transaction.
    Task<IDuelLock> AcquireLockAsync(CancellationToken cancellationToken);
}
public interface IDuelLock : IAsyncDisposable { Task CommitAsync(CancellationToken cancellationToken); }
```
`DuelLockService.AdvisoryLockKey = 0x4D4F4E4B4455454CL` (the SAME key — PvP binds/claims must keep serializing) and `DuelLockService.LockStatements()` returning the same two statements `DuelHouseCoverageService` runs (copy its `AcquireLockAsync`/lock-statement/lock-class code, lines ~98-132; `DuelHouseCoverageService` itself is left in place until Task 8).

- [ ] **Step 1: failing test** — `DuelLockServiceTests.LockStatements_CapTheWaitThenTakeTheDuelKey`: `DuelLockService.LockStatements()` has 2 entries; the first's `Format` contains `set_config('lock_timeout'`, the second's `GetArguments()` contains `DuelLockService.AdvisoryLockKey`, which equals `0x4D4F4E4B4455454CL`.
- [ ] **Step 2:** FAIL (no type).
- [ ] **Step 3:** create the service, register `services.AddScoped<IDuelLockService, DuelLockService>();`.
- [ ] **Step 4:** `dotnet build` + test PASS; full unit suite PASS.
- [ ] **Step 5:** commit "Give the duel's advisory lock its own service."

---

### Task 5: `RegisterDuelBet` draws the house word; the treasury checks the paid House bet

**Files:**
- Modify: `SpaceStroker.Domain/Games/Services/DuelHouseWordGenerator.cs` (add the draw helper)
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` (ctor ~lines 40-75, `HandleRequest` ~250-258, `RegisterDuelBet` ~684-794)
- Test: `SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseWordGeneratorTests.cs`, `SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` + `DuelHarness.cs`

**Interfaces — Consumes:** `IGameService.TrySetDuelHouseWord` (T1), `HouseEdgeNewBet(...Multiplier)` (T2), `IDuelLockService` (T4), `IHouseEdgeCoverageService` (existing). `RegisterBetHandler` stops depending on `IDuelHouseCoverageService` (ctor swap to `IDuelLockService`). **Produces:**
```csharp
// DuelHouseWordGenerator
public const int MaxWordAttempts = 10;
/// First derived word (attempts 0..MaxWordAttempts-1) not in `taken`, or null.
public static string? DrawAvailableWord(string serverSeed, Guid gameId, IReadOnlyList<string> pool, IReadOnlyCollection<string> taken)
```

- [ ] **Step 1: failing tests.**
  - `DuelHouseWordGeneratorTests`: `DrawAvailableWord` returns `DeriveWord(seed,id,0,pool)` when nothing is taken; returns attempt 1's word when attempt 0's word is taken; returns null for an empty pool.
  - `RegisterBetHandlerTests` (duel harness), all on a **House** round:
    1. Paid creator bet, treasury coverable → the round's `HouseWord` equals `DrawAvailableWord(round.ServerSeed, round.Id, pool, [creatorWord])`; exactly ONE bet row; **no `DuelCreatorBetPlacedEvent` published** (assert on the message-bus mock).
    2. Paid creator bet, treasury **not** coverable (`IHouseEdgeCoverageService.EvaluateAsync` → `IsCoverable=false`) → refunded with `HouseEdgeUnderMaintenance`, `HouseWord` still null.
    3. `Games.HouseEdgeEnabled = false` → refunded with `HouseEdgeUnderMaintenance`.
    4. The coverage call is made with `new HouseEdgeNewBet(roundId, creatorWord, stake, 1.98m)` (verify on the mock with `CommissionPercentage = 2`).
    5. Free creator bet → `HouseWord` set, coverage never called.
    6. Empty `DuelPoolSnapshot` → refused (`GameNotValidForBets`) and refunded if paid.
    7. OpenRoom creator + joiner → unchanged (no coverage call, no `HouseWord`).
  Adapt `DuelHarness` to the new constructor (`IDuelLockService`, `IHouseEdgeCoverageService`, `IGameService.TrySetDuelHouseWord` — the harness's real `GameService` works against the in-memory DB only if its raw SQL is avoided; follow how the harness already fakes `TryBindDuelRound`).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3: implement.** `DrawAvailableWord` (moved from `DuelHouseBetService.DeriveAvailableWord`, made pure):
```csharp
public static string? DrawAvailableWord(string serverSeed, Guid gameId, IReadOnlyList<string> pool, IReadOnlyCollection<string> taken)
{
    if (pool.Count == 0) return null;
    for (var attempt = 0; attempt < MaxWordAttempts; attempt++)
    {
        var candidate = DeriveWord(serverSeed, gameId, attempt, pool);
        if (!taken.Contains(candidate, StringComparer.Ordinal)) return candidate;
    }
    return null;
}
```
In `RegisterDuelBet`: replace `_duelCoverage` with `_duelLock` (`IDuelLockService`); after `BindOrClaimDuelRound` succeeds as **creator on a House round**:
```csharp
if (refusal == null && isCreator && _bet.Game!.DuelKind == DuelKind.House)
{
    if (!(_settings.Games?.HouseEdgeEnabled ?? true))
        refusal = ErrorMessages.HouseEdgeUnderMaintenance;
    else
    {
        if (!_isFreeBet)
        {
            // Joins the duel lock's transaction (AcquireLockAsync takes the lock inside an open one).
            _coverageLock = await _houseEdgeCoverage.AcquireLockAsync(cancellationToken);
            var multiplier = DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage);
            houseDecision = await _houseEdgeCoverage.EvaluateAsync(
                new HouseEdgeNewBet(_bet.GameId, _bet.Choice, _bet.Amount, multiplier), cancellationToken);
        }

        if (_isFreeBet || houseDecision?.IsCoverable == true)
        {
            var round = await _spacestrokerDbContext.Games.AsNoTracking()
                .Where(x => x.Id == _bet.GameId)
                .Select(x => new { x.ServerSeed, x.DuelPoolSnapshot })
                .FirstAsync(cancellationToken);
            var pool = (round.DuelPoolSnapshot ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var word = round.ServerSeed == null ? null
                : DuelHouseWordGenerator.DrawAvailableWord(round.ServerSeed, _bet.GameId, pool, [_bet.Choice]);

            if (word == null || !await _gameService.TrySetDuelHouseWord(_bet.GameId, word))
                refusal = ErrorMessages.GameNotValidForBets;
            else
                _bet.Game.HouseWord = word;
        }
    }
}
```
and replace the old HouseSeed "FAIL CLOSED" block with: a paid House creator bet with `houseDecision?.IsCoverable != true` → `Log.Error(...)` (treasury balance, exposure, fee reserve) + `RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance)`. The lock commit/dispose: the coverage lock joined the duel lock's transaction, so commit happens once through `_duelLock!.CommitAsync`; dispose `_coverageLock` in `ReleaseDuelLockAsync`. Delete `_duelNeedsHouseCounterparty`, its assignment and the `DuelCreatorBetPlacedEvent` publish in `HandleRequest` (~256-257), and the `using ...Consumers.DuelHouseBet;`. Keep the `catch` that refunds with the maintenance sentence on any unexpected failure; reword its log to "treasury" not "HouseSeed".
- [ ] **Step 4:** PASS; full unit suite PASS.
- [ ] **Step 5:** commit "Draw a House duel's word onto the round at the creator's bet, checked against the shared treasury; the duel lock stands alone."

---

### Task 6: Opening a House room and quoting its ceiling use the treasury

**Files:**
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/OpenDuelRoomHandler.cs:41-51`
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/CheckBetAvailability/CheckBetAvailabilityHandler.cs` (duel branch ~110-180, ctor)
- Test: `.../UnitTests/Handlers/OpenDuelRoomHandlerTests.cs`, `.../UnitTests/Handlers/CheckBetAvailabilityHandlerTests.cs` (the `AvailabilityHarness` mocks `IHouseEdgeCoverageService` now, not the duel coverage)

**Interfaces — Consumes:** `DuelHouseRules` (T2), `IHouseEdgeCoverageService`. Both handlers stop depending on `IDuelHouseCoverageService`.

- [ ] **Step 1: failing tests.**
  - Open: House room with `HouseEdgeEnabled=false` → 400 `GameNotValidForBets`; House room with treasury decision not coverable for `RequiredAmount` → 400; coverable → opens. OpenRoom unaffected by both.
  - Availability (House, via `AvailabilityHarness`):
    - Mount quote (no GameId, amount 0): coverage mocked with `TreasuryBalance=1m, Exposure=0.3m, FeeReserve=0.001m` → `MaxStake == DuelHouseRules.MaxStakeFromHeadroom(1m-0.001m-0.3m, 1.98m, 0.1m)` (= 0.1).
    - Same with `TreasuryBalance=0.2m, Exposure=0.1m, FeeReserve=0.001m` → `MaxStake == 0.05m` (0.099/1.98).
    - Null balance → `MaxStake == 0`.
    - Paid amount coverable → Open with that MaxStake; not coverable → Unavailable (maintenance sentence) with MaxStake.
    - `HouseEdgeEnabled=false` → `UnderMaintenance` for House; PvP still Open.
    - Keep the existing PvP tests (no ceiling, floor refusal) green.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3: implement.** Open: replace the HouseSeed call with
```csharp
if (kind == DuelKind.House)
{
    if (!(_settings.Games?.HouseEdgeEnabled ?? true))
        Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);
    var multiplier = DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage);
    var decision = await _coverage.EvaluateAsync(new HouseEdgeNewBet(null, string.Empty, parameters!.RequiredAmount, multiplier), cancellationToken);
    if (!decision.IsCoverable)
        Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);
}
```
(inject `IHouseEdgeCoverageService _coverage` and `GeneralSettings _settings`). Availability, House branch (after the PvP early return and the type-bounds check):
```csharp
if (!(_settings.Games?.HouseEdgeEnabled ?? true))
    return BetAvailabilityDto.UnderMaintenance;

var multiplier = DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage);
// The ceiling is priced WITHOUT the asked stake (amount 0): headroom = balance − fees − existing exposure.
var ceilingDecision = await _coverage.EvaluateAsync(new HouseEdgeNewBet(request.Body.GameId, string.Empty, 0m, multiplier), cancellationToken);
var maxStake = ceilingDecision.TreasuryBalance is decimal balance
    ? DuelHouseRules.MaxStakeFromHeadroom(balance - ceilingDecision.FeeReserve - ceilingDecision.Exposure, multiplier, typeCeiling)
    : 0m;

if (amount == 0m)                 // mount quote, or a free bet
    return BetAvailabilityDto.OpenWithMaxStake(maxStake);

var decision = await _coverage.EvaluateAsync(new HouseEdgeNewBet(request.Body.GameId, request.Body.Choice ?? string.Empty, amount, multiplier), cancellationToken);
return decision.IsCoverable
    ? BetAvailabilityDto.OpenWithMaxStake(maxStake)
    : BetAvailabilityDto.UnavailableWithMaxStake(maxStake);
```
Remove the old HouseSeed mount-quote block and the `IDuelHouseCoverageService` field.
- [ ] **Step 4:** PASS; full unit suite PASS.
- [ ] **Step 5:** commit "Open and quote a House duel against the treasury and Hyper Roll's maintenance switch."

---

### Task 7: Start, race, reward and verify a House duel with no house bet

**Files:**
- Modify: `SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs:110`
- Modify: `SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` (`SetBets` ~226, `SetCharactersOnTyper` free-text branch ~294)
- Modify: `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` (`SetPotAmount` ~217)
- Modify: `SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs:~105-113`
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs:~106-110`
- Test: `.../UnitTests/Domain/Games/SpaceStrokerTyperTests.cs` (or the duel typer test file present), `BetDomainServiceTests.cs`, `BetPlacedConsumerTests.cs`, `GetGameVerificationHandlerTests.cs`, `RewardGameConsumerTests.cs` (whichever exist — grep; add cases to the existing ones)

- [ ] **Step 1: failing tests.**
  - Typer: a House duel round with one bet `quirky` and `HouseWord = "meteor"` → `typer.Bets` has keys `{quirky:1, meteor:0}`; the character pool contains `m,e,t,o,r`; a fallback that lands on `meteor` gives `NumberOfWinners == 0`.
  - `BetDomainService`: House duel `Stake = 0.1`, one paid winning bet 0.1, commission 2% → `CalculateRewardForBet` = `0.198` (i.e. `0.1 × 1.98`). OpenRoom with two 0.1 bets still `0.198` for the winner (unchanged).
  - `BetPlacedConsumer`: a House duel round with ONE bet starts (`TryStartGame` called); an OpenRoom with one bet does not.
  - Verification: House round with `HouseWord="meteor"` and one bet → `dto.HouseWord == "meteor"` and the recomputed choices include `meteor`.
  - `RewardGameConsumer`: a House duel paid winner reads the treasury balance first and, when it cannot cover `0.198`, marks the bet `NeedsManualAnalysis` (same as Hyper Roll).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3: implement.**
```csharp
// BetPlacedConsumer
var isHouseDuel = game.Parameters.IsDuel && game.DuelKind == DuelKind.House;
var requiredPlayers = game.Parameters.IsHouseEdge || isHouseDuel ? 1 : game.Parameters.MinimumNumberOfPlayers;
```
Typer `SetBets`, after the preset branch and before the bet loop:
```csharp
// A House duel's opponent word has no bet (specs/duel-treasury-house.md): race it like a board word.
if (game.Parameters.IsDuel && game.DuelKind == DuelKind.House && !string.IsNullOrEmpty(game.HouseWord))
{
    if (game.HouseWord.Length > QueueLength)
        QueueLength = game.HouseWord.Length;
    Bets.TryAdd(game.HouseWord, 0);
}
```
Typer free-text branch: after adding the bets' characters, `if (!string.IsNullOrEmpty(game.HouseWord)) foreach (var c in game.HouseWord) availableCharacters.Add(c);`.
`SetPotAmount`: after the lottery/carry lines —
```csharp
// A House duel: the treasury stands in for the house's stake, so the pot is two stakes and a winning
// player's commission is the same 2% of an S profit as a PvP duel → stake × (2 − rate).
if (game.Parameters?.IsDuel == true && game.DuelKind == DuelKind.House && game.Stake is decimal houseStake)
    PotAmount += houseStake;
```
`RewardGameConsumer`: `var treasuryFunded = isHouseEdge || (game.Parameters?.IsDuel == true && game.DuelKind == DuelKind.House);` and use it for the balance read (`if (treasuryFunded && paidWinners.Any())`) — keep `isHouseEdge` for the payouts list. Verification: `if (game.DuelKind == DuelKind.House) dto.HouseWord = game.HouseWord;` (replace the ClaimedByPlayerId lookup and its comment with one line of rationale).
- [ ] **Step 4:** PASS; full unit suite PASS.
- [ ] **Step 5:** commit "Race, pay and verify a House duel against the round's house word, paid by the treasury."

---

### Task 8: Delete the HouseSeed counterparty and the HouseSeed duel coverage

**Files:**
- Delete: `SpaceStroker.Infrastructure/Services/Duel/DuelHouseBetService.cs`, `IDuelHouseBetService.cs`, `SpaceStroker.Infrastructure/Consumers/DuelHouseBet/` (both files)
- Delete: `SpaceStroker.Domain/Games/Services/DuelHouseCoverage.cs`, `SpaceStroker.Infrastructure/Services/Games/DuelHouseCoverageService.cs`, `IDuelHouseCoverageService.cs` and its DI registration (nothing references them after Tasks 5-6)
- Delete tests: `SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseCoverageTests.cs`, `SpaceStroker.Tests/UnitTests/Services/DuelHouseCoverageServiceTests.cs`
- Modify: `SpaceStroker.Consumers/Program.cs` (lines 14, 55, 106, 204-206)
- Modify: `SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs:~113` (comment only: duels no longer place a HouseSeed bet)
- Delete tests: `SpaceStroker.Tests/UnitTests/Services/DuelHouseBetServiceTests.cs`, `DuelHouseBetHarness.cs`

- [ ] **Step 1:** grep `DuelHouseBet|DuelCreatorBetPlaced|DuelHouseCoverage|IDuelHouseCoverageService|MaxWordAttempts` across `src` — every hit must be one of the files above or Task 5's generator.
- [ ] **Step 2:** delete/modify; `dotnet build` PASS; full unit suite PASS.
- [ ] **Step 3:** commit "Remove the HouseSeed duel counterparty: its service, consumer, event and endpoint."

---

### Task 9: Integration tests — the treasury is the house

**Files:**
- Modify: `SpaceStroker.Tests.Integration/MonkeyDuelFlowTests.cs` (the House tests), `MonkeyDuelCoverageTests.cs` (HouseSeed → treasury), `MonkeyDuelCancelTests.cs` / `MonkeyDuelConditionalUpdateTests.cs` (only where they assume a house bet row), `Harness/IntegrationTestFixture.cs` (helpers: set the treasury balance on the fakes; `GetGameAsync` returns `HouseWord`)

- [ ] **Step 1: write/rewrite** (they fail against a tree without Tasks 1-8, pass with them):
  1. `HouseDuel_PlayerWins_TreasuryPaysStakeTimesTwoLessTheCut`: open House, one paid bet (word outside the pool), steer the fallback draw onto the player's word (reuse the seed-steering approach in `OrbitWalletFlowTests.FindServerSeedFor`, but with prefix = gameId + the ONE bet seed and choices = {player word, HouseWord}), wait `RewardForWinnersCompleted`; exactly one bet row; one Credit of `stake × (2 − rate)`; **no transfer from `HouseSeedWallet`** in `ConsumersSolanaService.Transfers`; `HouseWord` equals `DrawAvailableWord(...)`.
  2. `HouseDuel_HouseWins_PaysNobody`: steer onto `HouseWord`; zero Credit rows; bet `Status` not Rewarded.
  3. `HouseDuel_OrbitFunded_WinLandsInOrbit` (in `OrbitWalletFlowTests`, reuse its helpers): Orbit wager on a House round, steer to a player win, `WinCredit == stake × 1.98`.
  4. `HouseDuel_VerificationReproducesTheHouseWordAndRace`: the verification payload's `HouseWord` equals the stored one and `DuelHouseWordGenerator.DrawAvailableWord` from the revealed seed; `bets.Count == 1`.
  5. `SharedExposure_AHyperRollRoundBlocksAHouseDuel_AndTheReverse` (in `MonkeyDuelCoverageTests`): set the fake treasury balance so one 0.1 Hyper Roll worst case fits but not also a 0.1 house duel; the duel's availability quote is refused; free the Hyper Roll round; the duel is accepted. Then the reverse.
  6. `HouseEdgeSwitchOff_ClosesTheHouseTab`.
  Delete the tests that only proved HouseSeed placement/coverage (house bet row, HouseSeed rotation for the house word, HouseSeed balance floors); keep every PvP test unchanged.
- [ ] **Step 2:** run `--filter "FullyQualifiedName~MonkeyDuel|FullyQualifiedName~OrbitWalletFlowTests|FullyQualifiedName~HyperRoll|FullyQualifiedName~DefaultGameSeeding"` → all PASS. Then the full unit suite once more.
- [ ] **Step 3:** commit "Integration: a House duel is one bet, paid by the treasury, verified from the round's house word."

---

### Task 10: Frontend — the rival word comes from the round

**Files:**
- Modify: `monkify-front/src/types/index.ts` (`GameInstance`: add `houseWord?: string | null`)
- Modify: `monkify-front/src/components/game/GameTerminal.vue` (`duelRivalBet` ~184, `duelRivalLabel` ~187, rival word uses ~823 and ~903)
- Check: `monkify-front/src/utils/provablyFair.ts` / `public/verify.html` (no code change expected: the prefix is built from the bets, which is now one)

- [ ] **Step 1:** add
```ts
// The rival's word: a House round's is the round's own `houseWord` (the house places no bet —
// specs/duel-treasury-house.md); an OpenRoom's is the other bet's choice.
const duelRivalWord = computed(() =>
  gameInstance.value?.duelKind === DuelKind.House
    ? (gameInstance.value?.houseWord ?? '')
    : (duelRivalBet.value?.choice ?? '')
);
```
and use it at the header (`duelRivalWord ? duelRivalWord.toUpperCase() : '??????'`) and the VS splash (`:rival-word="duelRivalWord"`). `duelRivalLabel` for a House round stays `'THE HOUSE'` (it no longer finds a bet, so its existing fallback already returns that — verify).
- [ ] **Step 2:** gates in the worktree: junction the hub `node_modules` (`cmd /c mklink /J <wt>\node_modules E:\Monkify\monkify-front\node_modules`), then `node node_modules/typescript/bin/tsc --noEmit -p tsconfig.json`, `node node_modules/eslint/bin/eslint.js <changed files>`, prettier `--check` on **LF copies** in the scratchpad. Remove the junction with `[System.IO.Directory]::Delete(path,$false)` afterwards (never `rm -rf`).
- [ ] **Step 3:** commit "Read a House duel's rival word from the round, not a house bet."

---

### Task 11: Deploy locally and prove it end to end

- [ ] Rebuild api + consumers from the worktree **without** the HouseSeed override (`docker compose -p monkify-backend build api consumers && docker compose -p monkify-backend up -d --no-deps api consumers` from `monkify-backend`); the new migration applies on Consumers start. Rebuild the front from the worktree (remove any `node_modules` junction and `dist`; `yarn.lock` already copied; regenerate `.env.production.local` for localhost as `setup_local_environment.bat` does).
- [ ] Prove: `/games/active-types` shows the duel; `POST /games/{duel}/availability {amount:0}` quotes a treasury-based `maxStake` (0.1 with the funded devnet treasury); play a free House duel in the scripted browser (`scratchpad/pw/duel.js`) and screenshot the VS splash (rival word shown) and the result; `docker logs` Consumers show no `DUEL HOUSE` lines and no Error.
- [ ] Update `.worktrees/.deployed-stack.json`.
