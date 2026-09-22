# Hyper Roll Game Modes — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Hyper Roll game type three selectable boards — AVERAGE (5 words), SUPER (6) and HYPER (8, unchanged) — so a player can pick their odds with a mode, and record which board each round used.

**Architecture:** Modes become rows in a new `GameBoardModes` child table of `GameParameters`, reconciled from `spacestrokersettings.json` exactly like the word pool already is. A ladder slot gains its **word length** (`length:multiplier:weight`), so `HouseEdgeBoard.Draw` groups candidates by word length instead of by `PresetChoice.Multiplier` — which is what makes a mode-dependent multiplier possible. `GameService.DrawHouseEdgeBoard` is the single choke point shared by `CreateGame`, `OpenSoloBoard` and `RerollSoloBoard`, so it takes the requested mode and every path follows.

**Tech Stack:** .NET 8, EF Core (inline `OnModelCreating`), MediatR, MassTransit, Postgres, xUnit + Shouldly + Moq, Testcontainers.

## Global Constraints

- Spec: `monkify-meta/specs/hyper-roll-modes.md`. The §4.1 ladders are **LOCKED** — never alter a multiplier, slot count, length tier or weight.
- Worktree: `E:\Monkify\.worktrees\hyper-roll-modes\monkify-backend`, branch `feature/hyper-roll-modes`. Never touch the hub.
- Unit tests run via `scripts/run_tests.bat` — **never** a bare `dotnet test` (the `ProductionSessions` Monte Carlo suite is slow).
- Integration tests run via `scripts/run_integration_tests.bat` (Docker required).
- EF mapping is **inline** in `SpaceStrokerDbContext.OnModelCreating`. No `IEntityTypeConfiguration`.
- Migrations live in `SpaceStroker.Infrastructure/Migrations/`, applied **only** by `SpaceStroker.Consumers`. Never hand-edit the model snapshot.
- ⚠️ `SpaceStrokerDbContext` defaults every query to `QueryTrackingBehavior.NoTracking`. A loaded-and-mutated entity persists **nothing** — mark changed columns with `_context.Entry(x).Property(...).IsModified = true`, as `UpdateGameStatus` does.
- ⚠️ A mis-weighted ladder or mis-tiered pool is a **MONEY bug**. Validation fails closed: refuse to open the round.
- `GameParameters.BoardLadder` / `RtpPercent` and `PresetChoice.Multiplier` are a **legacy HYPER mirror** (spec S3) — keep writing them, read none of them in new code.
- Player-facing refusals stay vague (`constraints.md` §6). An unknown mode is **never** its own error — it falls back to the default mode.
- Config in `spacestrokersettings.json` is product config, not a secret. Never write a real credential anywhere.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `SpaceStroker.Domain/Games/ValueObjects/HouseEdgeSlot.cs` | One ladder slot: length + multiplier + weight |
| `SpaceStroker.Domain/Games/Entities/GameBoardMode.cs` | **New** — one selectable board of a house-edge type |
| `SpaceStroker.Domain/Games/Entities/Game.cs` | `+ BoardMode` label |
| `SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs` | Ladder parse/format + per-mode validation |
| `SpaceStroker.Domain/Games/Services/HouseEdgeBoard.cs` | Draw a board by word **length** |
| `SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` | Config binding for `HouseEdge.Modes[]` |
| `SpaceStroker.Infrastructure/Services/Games/GameService.cs` | The one draw choke point + solo board open/reroll |
| `SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs` | Startup self-heal of modes |
| `SpaceStroker.Infrastructure/Handlers/Games/{OpenHouseEdgeBoard,RerollHouseEdgeBoard}/*` | Accept a mode |
| `SpaceStroker.Infrastructure/Contracts/Games/*.cs` | Publish modes + the round's mode |

---

### Task 1: The ladder slot carries its word length

**Files:**
- Modify: `SpaceStroker.Domain/Games/ValueObjects/HouseEdgeSlot.cs`
- Modify: `SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs` (`WordMinLength`, `ParseLadder`, `FormatLadder`, `TryParseLadder`)
- Modify: `SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` (`HouseEdgeLadderSlotSettings.Length`)
- Test: `SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs`

**Interfaces:**
- Produces: `HouseEdgeSlot(int Length, decimal Multiplier, int Weight)`; `HouseEdgeRules.ParseLadder(string?) -> IReadOnlyList<HouseEdgeSlot>`; `HouseEdgeRules.FormatLadder(IEnumerable<HouseEdgeSlot>) -> string` in `"length:multiplier:weight,…"` form; `HouseEdgeRules.WordMinLength == 4`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void ParseLadder_ReadsLengthMultiplierWeightTriples()
{
    var ladder = HouseEdgeRules.ParseLadder("4:3:305011,8:7:130719");

    ladder.Count.ShouldBe(2);
    ladder[0].ShouldBe(new HouseEdgeSlot(4, 3m, 305011));
    ladder[1].ShouldBe(new HouseEdgeSlot(8, 7m, 130719));
}

[Fact]
public void ParseLadder_RejectsTheOldTwoPartFormat()
    => Should.Throw<FormatException>(() => HouseEdgeRules.ParseLadder("10:91837,4:229591"));

[Fact]
public void ParseLadder_RejectsALengthOutsideThePoolRange()
    => Should.Throw<FormatException>(() => HouseEdgeRules.ParseLadder("3:3:500000,11:4:500000"));

[Fact]
public void FormatLadder_RoundTripsThroughParseLadder()
{
    const string ladder = "5:3:303797,9:9:101266";

    HouseEdgeRules.FormatLadder(HouseEdgeRules.ParseLadder(ladder)).ShouldBe(ladder);
}

[Fact]
public void WordMinLength_IsFourSoTheAverageBoardCanDrawFourLetterWords()
    => HouseEdgeRules.WordMinLength.ShouldBe(4);
```

- [ ] **Step 2: Run them and watch them fail**

Run: `scripts\run_tests.bat`
Expected: compile errors — `HouseEdgeSlot` has no 3-arg constructor.

- [ ] **Step 3: Widen the value object**

```csharp
/// <summary>
/// One slot of a house-edge board ladder (#193, modes): the WORD LENGTH this slot draws from the pool,
/// the multiplier the word pays, and the integer draw weight it wins with. A slot's chance is
/// Weight ÷ Σ(board weights), so its return is that chance × Multiplier.
///
/// ⚠️ Length, not multiplier, is what selects the word (#193): the same word pays differently on
/// different modes, so the pool can no longer carry an authoritative multiplier.
/// Stored in GameBoardMode.Ladder as "length:multiplier:weight".
/// </summary>
public readonly record struct HouseEdgeSlot(int Length, decimal Multiplier, int Weight);
```

- [ ] **Step 4: Update the rules**

In `HouseEdgeRules.cs`: set `public const int WordMinLength = 4;` (leave `WordMaxLength = 10`), then:

```csharp
public static string FormatLadder(IEnumerable<HouseEdgeSlot> ladder)
    => string.Join(",", ladder.Select(x =>
        x.Length.ToString(CultureInfo.InvariantCulture) + ":"
        + x.Multiplier.ToString("0.##", CultureInfo.InvariantCulture) + ":"
        + x.Weight.ToString(CultureInfo.InvariantCulture)));
```

and in `TryParseLadder`, replace the 2-part split with:

```csharp
var parts = token.Split(':', StringSplitOptions.TrimEntries);
if (parts.Length != 3
    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
    || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var multiplier)
    || !decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var weight))
{
    return $"BoardLadder '{boardLadder}' is not {LadderShape}: slot '{token}'.";
}

if (length < WordMinLength || length > WordMaxLength)
    return $"BoardLadder '{boardLadder}' slot '{token}': the word length must be {WordMinLength}–{WordMaxLength}.";

if (weight <= 0 || decimal.Truncate(weight) != weight || weight > int.MaxValue)
    return $"BoardLadder '{boardLadder}' slot '{token}': the weight must be a positive integer (at most {int.MaxValue}).";

slots.Add(new HouseEdgeSlot(length, multiplier, (int)weight));
```

Update `LadderShape` to `"a comma-separated list of length:multiplier:weight triples"`.

- [ ] **Step 5: Teach the config binding the new shape**

In `OperatorSettings.cs`:

```csharp
public class HouseEdgeLadderSlotSettings
{
    // #193 — the WORD LENGTH this slot draws from the pool. Length sets which words are eligible;
    // Multiplier is what the slot pays, and the two differ per mode.
    public int Length { get; set; }
    public decimal Multiplier { get; set; }
    public int Weight { get; set; }
}
```

and update `HouseEdgeSettings.LadderSlots()` to `new HouseEdgeSlot(x.Length, x.Multiplier, x.Weight)`.

- [ ] **Step 6: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the five new tests PASS. Other HouseEdge tests will now fail on the old 2-part literals — that is expected and is fixed in Task 10; do **not** fix them by weakening the format.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Tests
git commit -m "Carry the word length on a house-edge ladder slot."
```

---

### Task 2: `GameBoardMode` entity, `Game.BoardMode`, and the migration

**Files:**
- Create: `SpaceStroker.Domain/Games/Entities/GameBoardMode.cs`
- Modify: `SpaceStroker.Domain/Games/Entities/Game.cs`, `SpaceStroker.Domain/Games/Entities/GameParameters.cs`
- Modify: `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
- Create: `SpaceStroker.Infrastructure/Migrations/*_HyperRollModes.cs`

**Interfaces:**
- Produces: `GameBoardMode { Guid ParametersId; string Name; decimal RtpPercent; string Ladder; int Position; }`; `Game.BoardMode` (`string?`); `GameParameters.BoardModes` (`ICollection<GameBoardMode>`); `SpaceStrokerDbContext.GameBoardModes`.

- [ ] **Step 1: Create the entity**

```csharp
using SpaceStroker.Common.Models;
using System;

namespace SpaceStroker.Domain.Games.Entities
{
    /// <summary>
    /// One selectable board of a house-edge type (#193 Hyper Roll modes): AVERAGE, SUPER or HYPER.
    /// Reconciled from GeneralSettings:Operator:DefaultGames → HouseEdge.Modes at Consumers startup,
    /// the same way the word pool is.
    /// </summary>
    public class GameBoardMode : TableEntity
    {
        public GameBoardMode() { }

        public GameParameters Parameters { get; set; }
        public Guid ParametersId { get; set; }

        /// <summary>The player-facing name, and the value stored on Game.BoardMode ("AVERAGE"). Matched
        /// case-insensitively on the wire; persisted exactly as config spells it.</summary>
        public string Name { get; set; }

        /// <summary>This mode's return to player, in percent. Cross-checked against Ladder by HouseEdgeRules.</summary>
        public decimal RtpPercent { get; set; }

        /// <summary>"length:multiplier:weight" slots, invariant culture — one per board word.</summary>
        public string Ladder { get; set; }

        /// <summary>Dial order, left to right. The LOWEST Position is the type's DEFAULT mode.</summary>
        public int Position { get; set; }
    }
}
```

- [ ] **Step 2: Add the navigations**

`GameParameters.cs`:

```csharp
// #193 — the selectable boards of a house-edge type, lowest Position first. Empty for every other type.
public ICollection<GameBoardMode> BoardModes { get; set; }
```

`Game.cs`, next to `BoardChoices`:

```csharp
// #193 — the name of the board mode this round was drawn from ("AVERAGE"/"SUPER"/"HYPER"); null for
// every non-house-edge round. A LABEL for history, stats and the fairness panel: the round's
// GameBoardChoices already carry every multiplier and weight the draw and verification use, so
// nothing money-related reads this. Set at draw time; rewritten by a reroll that changes mode.
public string? BoardMode { get; set; }
```

- [ ] **Step 3: Map them inline**

In `SpaceStrokerDbContext.OnModelCreating`, immediately after the `GameBoardChoice` block:

```csharp
modelBuilder.Entity<GameBoardMode>(builder =>
{
    builder.HasKey(x => x.Id);
    builder.Property(x => x.ParametersId).IsRequired();
    builder.Property(x => x.Name).IsRequired().HasMaxLength(16);
    builder.Property(x => x.RtpPercent).HasPrecision(5, 2).IsRequired();
    builder.Property(x => x.Ladder).IsRequired().HasMaxLength(256);
    builder.Property(x => x.Position).IsRequired();
    // One row per (type, mode): the reconciler matches by name, and a duplicate would make the
    // default-mode pick non-deterministic.
    builder.HasIndex(x => new { x.ParametersId, x.Name }).IsUnique();
});
```

and in the `Game` block: `builder.Property(x => x.BoardMode).HasMaxLength(16).IsRequired(false);`

Add `public DbSet<GameBoardMode> GameBoardModes { get; set; }` beside `GameBoardChoices`.

- [ ] **Step 4: Generate the migration**

```bash
cd src/SpaceStroker.Infrastructure
dotnet ef migrations add HyperRollModes --startup-project ../SpaceStroker.Consumers
```

- [ ] **Step 5: Add the backfill to the generated `Up`**

At the end of `Up(MigrationBuilder)`, after the generated `CreateTable`/`AddColumn`:

```csharp
// #193 — every house-edge round that predates modes was drawn from the 8-word board, which is HYPER.
// Label them so history, stats and the fairness panel read correctly for rounds already played.
migrationBuilder.Sql(@"
    UPDATE ""Games"" g SET ""BoardMode"" = 'HYPER'
    FROM ""GameParameters"" p
    WHERE g.""ParametersId"" = p.""Id"" AND p.""IsHouseEdge"" = true;");
```

Do **not** hand-edit the snapshot; only this `Sql` call is added by hand.

- [ ] **Step 6: Verify it builds and the model snapshot is consistent**

Run: `dotnet build src/SpaceStroker.Infrastructure`
Expected: build succeeds, no pending-model-changes warning.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Infrastructure
git commit -m "Add the GameBoardModes table and the round's board-mode label."
```

---

### Task 3: Per-mode validation

**Files:**
- Modify: `SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs`
- Test: `SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs`

**Interfaces:**
- Produces: `HouseEdgeRules.ValidateMode(GameParameters, GameBoardMode, IReadOnlyCollection<PresetChoice>) -> HouseEdgeValidation`; `HouseEdgeRules.Validate(GameParameters, IReadOnlyCollection<PresetChoice>)` now means "has ≥1 mode and every mode is valid", and its failure reason names the mode.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void ValidateMode_AcceptsTheAverageBoardAgainstTheRealPool()
{
    var parameters = HouseEdgeParameters();
    var mode = new GameBoardMode { Name = "AVERAGE", RtpPercent = 91.50m, Ladder = AverageLadder };

    HouseEdgeRules.ValidateMode(parameters, mode, RealPool()).IsValid.ShouldBeTrue();
}

[Fact]
public void ValidateMode_RefusesAModeWhoseRtpDoesNotMatchItsWeights()
{
    var parameters = HouseEdgeParameters();
    var mode = new GameBoardMode { Name = "AVERAGE", RtpPercent = 80m, Ladder = AverageLadder };

    var result = HouseEdgeRules.ValidateMode(parameters, mode, RealPool());

    result.IsValid.ShouldBeFalse();
    result.Reason.ShouldContain("does not match RtpPercent");
}

[Fact]
public void ValidateMode_RefusesABoardThatPaysBackAHundredPercentOrMore()
{
    var parameters = HouseEdgeParameters();
    // One slot at 10x with all the weight returns 1000%.
    var mode = new GameBoardMode { Name = "BROKEN", RtpPercent = 1000m, Ladder = "6:10:1000000,7:2:1" };

    HouseEdgeRules.ValidateMode(parameters, mode, RealPool()).Reason.ShouldContain("hard cap");
}

[Fact]
public void ValidateMode_RefusesWhenThePoolHasTooFewWordsOfALength()
{
    var parameters = HouseEdgeParameters();
    var mode = new GameBoardMode { Name = "AVERAGE", RtpPercent = 91.50m, Ladder = AverageLadder };
    var poolWithoutFourLetterWords = RealPool().Where(x => x.Choice.Length != 4).ToList();

    var result = HouseEdgeRules.ValidateMode(parameters, mode, poolWithoutFourLetterWords);

    result.IsValid.ShouldBeFalse();
    result.Reason.ShouldContain("4-letter");
}

[Fact]
public void Validate_RefusesATypeWithNoModesAtAll()
{
    var parameters = HouseEdgeParameters();
    parameters.BoardModes = new List<GameBoardMode>();

    HouseEdgeRules.Validate(parameters, RealPool()).Reason.ShouldContain("no board modes");
}
```

with these fixtures in the test class:

```csharp
private const string AverageLadder = "4:3:305011,5:4:228758,6:5:183007,7:6:152505,8:7:130719";
```

- [ ] **Step 2: Run and watch them fail**

Run: `scripts\run_tests.bat`
Expected: compile error — `ValidateMode` does not exist.

- [ ] **Step 3: Implement `ValidateMode`**

Replace the body of today's `Validate` with a per-mode method, keeping every rule but retargeting rules 4, 6 and 7:

```csharp
/// <summary>
/// The money rules of ONE selectable board. Every rule that used to read the type's single BoardLadder
/// now reads this mode's ladder and this mode's RtpPercent (#193).
/// </summary>
public static HouseEdgeValidation ValidateMode(GameParameters parameters, GameBoardMode mode, IReadOnlyCollection<PresetChoice> activePool)
{
    if (!parameters.IsHouseEdge)
        return HouseEdgeValidation.Fail("The type is not a house-edge type.");

    if (parameters.MaximumStake is null)
        return HouseEdgeValidation.Fail("MaximumStake is required.");

    if (string.IsNullOrWhiteSpace(mode.Name))
        return HouseEdgeValidation.Fail("A board mode has no name.");

    var parseError = TryParseLadder(mode.Ladder, out var ladder);
    if (parseError != null)
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}': {parseError}");

    if (ladder.Count < BoardSizeMin)
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}' needs at least {BoardSizeMin} slots.");

    if (ladder.Any(x => !IsValidMultiplier(x.Multiplier)))
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}' has a multiplier outside 1.01–10.00 or with more than 2 decimals.");

    var totalWeight = ladder.Sum(x => (long)x.Weight);
    if (totalWeight <= 0 || totalWeight > MaxTotalWeight)
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}' weight total {totalWeight} must be above 0 and at most {MaxTotalWeight}.");

    var bestReturn = MaxReturnPercentOf(ladder);

    if (bestReturn >= MaxReturnCapPercent)
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}' best return {bestReturn:0.###}% is at or above the hard cap of {MaxReturnCapPercent}%.");

    if (Math.Abs(bestReturn - mode.RtpPercent) > RtpTolerancePercent)
        return HouseEdgeValidation.Fail($"Mode '{mode.Name}' best return {bestReturn:0.###}% does not match RtpPercent {mode.RtpPercent}% (tolerance {RtpTolerancePercent} points).");

    var rentFloorSol = (decimal)NativeSol.RentExemptMinimumLamports / NativeSol.LamportsPerSol;
    if (parameters.RequiredAmount < rentFloorSol)
        return HouseEdgeValidation.Fail($"The minimum stake {parameters.RequiredAmount} is below the rent-exempt floor {rentFloorSol}.");

    if (parameters.MaximumStake.Value < parameters.RequiredAmount)
        return HouseEdgeValidation.Fail("MaximumStake is below the minimum stake (RequiredAmount).");

    var alphabet = parameters.AllowedCharacters.StringValueOf();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var word in activePool)
    {
        if (word.Choice.Length < WordMinLength || word.Choice.Length > WordMaxLength)
            return HouseEdgeValidation.Fail($"Word '{word.Choice}' must be {WordMinLength}–{WordMaxLength} characters.");

        if (!word.Choice.All(alphabet.Contains))
            return HouseEdgeValidation.Fail($"Word '{word.Choice}' has characters outside the type's alphabet.");

        if (!seen.Add(word.Choice))
            return HouseEdgeValidation.Fail($"Word '{word.Choice}' is in the pool twice.");
    }

    // ⚠️ #193: the pool is grouped by word LENGTH, not by a per-word multiplier. A word's multiplier is
    // the SLOT's, and the same word pays differently on different modes.
    foreach (var tier in ladder.GroupBy(x => x.Length))
    {
        var available = activePool.Count(x => x.Choice.Length == tier.Key);
        if (available < tier.Count())
            return HouseEdgeValidation.Fail($"Mode '{mode.Name}': the pool has {available} active {tier.Key}-letter word(s) but the ladder needs {tier.Count()}.");
    }

    return HouseEdgeValidation.Ok;
}

/// <summary>The type-level gate: a house-edge type needs at least one mode, and every mode must be valid.</summary>
public static HouseEdgeValidation Validate(GameParameters parameters, IReadOnlyCollection<PresetChoice> activePool)
{
    if (!parameters.IsHouseEdge)
        return HouseEdgeValidation.Fail("The type is not a house-edge type.");

    var modes = parameters.BoardModes?.ToList() ?? new List<GameBoardMode>();
    if (modes.Count == 0)
        return HouseEdgeValidation.Fail("The house-edge type has no board modes configured.");

    foreach (var mode in modes)
    {
        var result = ValidateMode(parameters, mode, activePool);
        if (!result.IsValid)
            return result;
    }

    return HouseEdgeValidation.Ok;
}
```

- [ ] **Step 4: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the five new tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Tests
git commit -m "Validate a house-edge board per mode."
```

---

### Task 4: Draw the board by word length

**Files:**
- Modify: `SpaceStroker.Domain/Games/Services/HouseEdgeBoard.cs`
- Test: `SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeBoardTests.cs`

**Interfaces:**
- Consumes: `HouseEdgeSlot.Length` (Task 1).
- Produces: `HouseEdgeBoard.Draw(IReadOnlyCollection<PresetChoice>, IReadOnlyList<HouseEdgeSlot>, Random) -> List<GameBoardChoice>?` — unchanged signature, length-based selection.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Draw_PicksAWordOfTheSlotsLengthAndPaysTheSlotsMultiplier()
{
    var pool = new[] { Word("mars"), Word("pluto"), Word("banana") };
    var ladder = HouseEdgeRules.ParseLadder("4:3:500000,6:5:500000");

    var board = HouseEdgeBoard.Draw(pool, ladder, new Random(1))!;

    board.Single(x => x.Choice.Length == 4).Multiplier.ShouldBe(3m);
    board.Single(x => x.Choice.Length == 6).Multiplier.ShouldBe(5m);
}

[Fact]
public void Draw_GivesTheSameWordADifferentMultiplierOnADifferentMode()
{
    var pool = new[] { Word("banana"), Word("cosmos") };

    var average = HouseEdgeBoard.Draw(pool, HouseEdgeRules.ParseLadder("6:5:1000000"), new Random(1))!;
    var hyper = HouseEdgeBoard.Draw(pool, HouseEdgeRules.ParseLadder("6:4:1000000"), new Random(1))!;

    average.Single().Multiplier.ShouldBe(5m);
    hyper.Single().Multiplier.ShouldBe(4m);
}

[Fact]
public void Draw_NeverRepeatsAWordAcrossTwoSlotsOfTheSameLength()
{
    var pool = new[] { Word("asteroid"), Word("blastoff") };
    var ladder = HouseEdgeRules.ParseLadder("8:8:500000,8:8:500000");

    var board = HouseEdgeBoard.Draw(pool, ladder, new Random(7))!;

    board.Select(x => x.Choice).Distinct().Count().ShouldBe(2);
}

[Fact]
public void Draw_ReturnsNullWhenThePoolCannotFillALengthTier()
{
    var pool = new[] { Word("asteroid") };
    var ladder = HouseEdgeRules.ParseLadder("8:8:500000,8:8:500000");

    HouseEdgeBoard.Draw(pool, ladder, new Random(7)).ShouldBeNull();
}

private static PresetChoice Word(string choice) => new(choice) { Active = true };
```

⚠️ Note `Word(...)` sets **no** `Multiplier` — proving the draw no longer reads it.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts\run_tests.bat`
Expected: FAIL — the current draw groups by multiplier and finds no candidates, returning null.

- [ ] **Step 3: Group by length**

In `HouseEdgeBoard.Draw`, change the grouping key and the candidate filter, and take the multiplier from the slot:

```csharp
foreach (var tier in ladder.GroupBy(x => x.Length))
{
    var slots = tier.ToList();
    var candidates = activePool
        .Where(x => x.Active && x.Choice.Length == tier.Key)
        .Select(x => x.Choice)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    if (candidates.Count < slots.Count)
        return null;

    // Partial Fisher–Yates: the first slots.Count entries become a uniform sample without repeats, so
    // two slots of the same LENGTH never share a word.
    for (var i = 0; i < slots.Count; i++)
    {
        var j = rng.Next(i, candidates.Count);
        (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        board.Add(new GameBoardChoice
        {
            Choice = candidates[i],
            Multiplier = slots[i].Multiplier,
            Weight = slots[i].Weight,
        });
    }
}
```

Update the class XML doc: "words within a **length** tier are interchangeable".

- [ ] **Step 4: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the four new tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Tests
git commit -m "Draw a house-edge board by word length."
```

---

### Task 5: Configure the three modes and the two new word tiers

**Files:**
- Modify: `SpaceStroker.Common/spacestrokersettings.json`
- Modify: `SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs`
- Test: `SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`

**Interfaces:**
- Produces: `HouseEdgeSettings.Modes` (`List<HouseEdgeModeSettings>`); `HouseEdgeModeSettings { string Name; decimal RtpPercent; List<HouseEdgeLadderSlotSettings> Ladder; IReadOnlyList<HouseEdgeSlot> LadderSlots(); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void HyperRollConfiguresExactlyTheThreeLockedModes()
{
    var modes = HyperRoll().HouseEdge.Modes;

    modes.Select(x => x.Name).ShouldBe(new[] { "AVERAGE", "SUPER", "HYPER" });
    modes.Select(x => x.RtpPercent).ShouldBe(new[] { 91.50m, 91.14m, 91.84m });
    modes.Select(x => x.Ladder.Count).ShouldBe(new[] { 5, 6, 8 });
}

[Theory]
[InlineData("AVERAGE")]
[InlineData("SUPER")]
[InlineData("HYPER")]
public void EveryModesWeightsSumToOneMillion(string name)
{
    var mode = HyperRoll().HouseEdge.Modes.Single(x => x.Name == name);

    mode.Ladder.Sum(x => (long)x.Weight).ShouldBe(1_000_000L);
}

[Fact]
public void EveryModeCanBeFilledFromThePool()
{
    var houseEdge = HyperRoll().HouseEdge;
    var lengths = houseEdge.Words.GroupBy(x => x.Word.Length).ToDictionary(g => g.Key, g => g.Count());

    foreach (var mode in houseEdge.Modes)
        foreach (var tier in mode.Ladder.GroupBy(x => x.Length))
            lengths.GetValueOrDefault(tier.Key).ShouldBeGreaterThanOrEqualTo(tier.Count(),
                $"{mode.Name} needs {tier.Count()} {tier.Key}-letter word(s)");
}

[Fact]
public void ThePoolCoversFourToTenLetters()
{
    var lengths = HyperRoll().HouseEdge.Words.Select(x => x.Word.Length).Distinct().OrderBy(x => x);

    lengths.ShouldBe(new[] { 4, 5, 6, 7, 8, 9, 10 });
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `scripts\run_tests.bat`
Expected: compile error — `HouseEdgeSettings.Modes` does not exist.

- [ ] **Step 3: Add the settings class**

```csharp
/// <summary>
/// One selectable board of a house-edge type (#193). Seeded in array order, so the FIRST entry is the
/// type's default mode. The type-level RtpPercent/Ladder above remain as a legacy HYPER mirror.
/// </summary>
public class HouseEdgeModeSettings
{
    public string Name { get; set; }
    public decimal RtpPercent { get; set; }
    public List<HouseEdgeLadderSlotSettings> Ladder { get; set; } = new();

    public IReadOnlyList<HouseEdgeSlot> LadderSlots()
        => Ladder.Select(x => new HouseEdgeSlot(x.Length, x.Multiplier, x.Weight)).ToList();
}
```

and on `HouseEdgeSettings`: `public List<HouseEdgeModeSettings> Modes { get; set; } = new();`

- [ ] **Step 4: Write the config**

In `spacestrokersettings.json`, inside `"Hyper Roll"` → `HouseEdge`:

1. Add `"Length"` to each of the 8 existing legacy `Ladder` slots (6,7,8,8,9,9,10,10 against multipliers 4,6,8,8,9,9,10,10 — reorder the array so length and multiplier line up; the legacy ladder is only a mirror, and slot order is cosmetic).
2. Add the `"Modes"` array **exactly** as spec §6.1 lists it (three modes, ladders from spec §4.2).
3. Append the 20 new words to `"Words"` exactly as spec §6.2 lists them.

- [ ] **Step 5: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the four new tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Common src/SpaceStroker.Domain src/SpaceStroker.Tests
git commit -m "Configure the three Hyper Roll board modes and the 4- and 5-letter word tiers."
```

---

### Task 6: Seed and self-heal the modes

**Files:**
- Modify: `SpaceStroker.Consumers/Program.cs` (`ToGameParameters`, the post-reconcile validation log)
- Modify: `SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs`
- Test: `SpaceStroker.Tests/UnitTests/Services/Games/HouseEdgeReconcilerTests.cs` (create if absent)

**Interfaces:**
- Consumes: `HouseEdgeModeSettings` (Task 5), `GameBoardMode` (Task 2).
- Produces: a reconciled `GameBoardModes` set matching config after every Consumers boot.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Reconcile_InsertsModesThatConfigHasAndTheDatabaseDoesNot()
{
    using var context = InMemoryContext();
    SeedHyperRollWithoutModes(context);

    HouseEdgeReconciler.Reconcile(context, new[] { HyperRollConfig() });

    context.GameBoardModes.Select(x => x.Name).OrderBy(x => x)
        .ShouldBe(new[] { "AVERAGE", "HYPER", "SUPER" });
}

[Fact]
public void Reconcile_UpdatesALadderThatDriftedFromConfig()
{
    using var context = InMemoryContext();
    var parametersId = SeedHyperRollWithoutModes(context);
    context.GameBoardModes.Add(new GameBoardMode
    {
        ParametersId = parametersId, Name = "AVERAGE", RtpPercent = 50m, Ladder = "4:2:1000000", Position = 9,
    });
    context.SaveChanges();

    HouseEdgeReconciler.Reconcile(context, new[] { HyperRollConfig() });

    var average = context.GameBoardModes.Single(x => x.Name == "AVERAGE");
    average.RtpPercent.ShouldBe(91.50m);
    average.Position.ShouldBe(0);
    average.Ladder.ShouldBe("4:3:305011,5:4:228758,6:5:183007,7:6:152505,8:7:130719");
}

[Fact]
public void Reconcile_RemovesAModeConfigNoLongerDefines()
{
    using var context = InMemoryContext();
    var parametersId = SeedHyperRollWithoutModes(context);
    context.GameBoardModes.Add(new GameBoardMode
    {
        ParametersId = parametersId, Name = "LEGACY", RtpPercent = 91.84m, Ladder = "6:4:1000000", Position = 7,
    });
    context.SaveChanges();

    HouseEdgeReconciler.Reconcile(context, new[] { HyperRollConfig() });

    context.GameBoardModes.Any(x => x.Name == "LEGACY").ShouldBeFalse();
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `scripts\run_tests.bat`
Expected: FAIL — no modes are written.

- [ ] **Step 3: Seed modes on create**

In `Program.cs` `ToGameParameters`, inside the `config.HouseEdge != null` branch, after `PresetChoices`:

```csharp
// #193 — the selectable boards. Array order is dial order, so the FIRST entry is the default mode.
parameters.BoardModes = config.HouseEdge.Modes
    .Select((mode, index) => new GameBoardMode
    {
        Name = mode.Name,
        RtpPercent = mode.RtpPercent,
        Ladder = HouseEdgeRules.FormatLadder(mode.LadderSlots()),
        Position = index,
    })
    .ToList();
```

- [ ] **Step 4: Self-heal modes on boot**

In `HouseEdgeReconciler.Reconcile`, after the existing `GameParameters` `ExecuteUpdate` and before the words pass:

```csharp
// #193 — the selectable boards. Same create-only-seeding problem as the pool: a mode added to config
// would never reach a database that already has the row. Safe under a live round, because a round
// persists its own board rows and its BoardMode is a plain string, not a foreign key.
var configuredModes = houseEdge.Modes
    .Select((mode, index) => (mode.Name, mode.RtpPercent, Ladder: HouseEdgeRules.FormatLadder(mode.LadderSlots()), Position: index))
    .ToList();

var existingModes = context.GameBoardModes.AsNoTracking().Where(x => x.ParametersId == parametersId).ToList();

foreach (var configured in configuredModes)
{
    var row = existingModes.FirstOrDefault(x => x.Name == configured.Name);
    if (row == null)
    {
        context.GameBoardModes.Add(new GameBoardMode
        {
            ParametersId = parametersId.Value,
            Name = configured.Name,
            RtpPercent = configured.RtpPercent,
            Ladder = configured.Ladder,
            Position = configured.Position,
        });
        context.SaveChanges();
        changes++;
        continue;
    }

    if (row.RtpPercent == configured.RtpPercent && row.Ladder == configured.Ladder && row.Position == configured.Position)
        continue;

    var rowId = row.Id;
    changes += context.GameBoardModes
        .Where(x => x.Id == rowId)
        .ExecuteUpdate(setters => setters
            .SetProperty(x => x.RtpPercent, configured.RtpPercent)
            .SetProperty(x => x.Ladder, configured.Ladder)
            .SetProperty(x => x.Position, configured.Position));
}

var configuredNames = configuredModes.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
var orphaned = existingModes.Where(x => !configuredNames.Contains(x.Name)).Select(x => x.Id).ToList();
if (orphaned.Count > 0)
    changes += context.GameBoardModes.Where(x => orphaned.Contains(x.Id)).ExecuteDelete();
```

⚠️ `ExecuteUpdate`/`ExecuteDelete` are no-ops on the in-memory provider; the tests above therefore assert through a real-ish path — if a case cannot be driven in memory, assert the inserted/removed rows only and cover the update in the integration suite.

- [ ] **Step 5: Validate every mode at boot**

Where `Program.cs` runs `HouseEdgeRules.Validate` after reconciling, make sure the query that loads the type for validation includes `.Include(x => x.BoardModes)` — otherwise `Validate` sees no modes and logs a false failure.

- [ ] **Step 6: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the reconciler tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Consumers src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Seed and self-heal the Hyper Roll board modes."
```

---

### Task 7: Thread the mode through the one draw choke point

**Files:**
- Modify: `SpaceStroker.Infrastructure/Services/Games/GameService.cs` (`DrawHouseEdgeBoard`, `OpenSoloBoard`, `RerollSoloBoard`, `CreateGame`)
- Modify: `SpaceStroker.Infrastructure/Services/Games/IGameService.cs`
- Test: `SpaceStroker.Tests/UnitTests/Background/CreateGameTests.cs` + a new `GameServiceSoloBoardModeTests.cs`

**Interfaces:**
- Consumes: `HouseEdgeRules.ValidateMode` (Task 3), `HouseEdgeBoard.Draw` (Task 4), `GameBoardMode` (Task 2).
- Produces: `Task<Guid?> OpenSoloBoard(GameParameters parameters, string? mode)`; `Task<bool> RerollSoloBoard(Guid gameId, string? mode)`; both set `Game.BoardMode`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task OpenSoloBoard_UsesTheDefaultModeWhenNoneIsRequested()
{
    var gameId = await Service.OpenSoloBoard(HyperRoll, null);

    var game = Context.Games.Include(x => x.BoardChoices).Single(x => x.Id == gameId);
    game.BoardMode.ShouldBe("AVERAGE");
    game.BoardChoices.Count.ShouldBe(5);
}

[Fact]
public async Task OpenSoloBoard_HonoursTheRequestedModeCaseInsensitively()
{
    var gameId = await Service.OpenSoloBoard(HyperRoll, "hyper");

    var game = Context.Games.Include(x => x.BoardChoices).Single(x => x.Id == gameId);
    game.BoardMode.ShouldBe("HYPER");
    game.BoardChoices.Count.ShouldBe(8);
}

[Fact]
public async Task OpenSoloBoard_FallsBackToTheDefaultForAnUnknownMode()
{
    var gameId = await Service.OpenSoloBoard(HyperRoll, "NOPE");

    Context.Games.Single(x => x.Id == gameId).BoardMode.ShouldBe("AVERAGE");
}

[Fact]
public async Task RerollSoloBoard_SwitchesTheModeAndPersistsIt()
{
    var gameId = (await Service.OpenSoloBoard(HyperRoll, "AVERAGE"))!.Value;

    (await Service.RerollSoloBoard(gameId, "SUPER")).ShouldBeTrue();

    // ⚠️ NoTracking by default: this asserts the write actually reached the database.
    Context.ChangeTracker.Clear();
    var game = Context.Games.Include(x => x.BoardChoices).Single(x => x.Id == gameId);
    game.BoardMode.ShouldBe("SUPER");
    game.BoardChoices.Count.ShouldBe(6);
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `scripts\run_tests.bat`
Expected: compile error — `OpenSoloBoard` takes one argument.

- [ ] **Step 3: Give the choke point a mode**

```csharp
/// <summary>
/// Draws a validated house-edge board for a type IN A GIVEN MODE, reading the type row, its active pool
/// and its board modes from the DATABASE (never the caller's object — see CreateGame). Returns the
/// resolved mode name alongside the board. Shared by CreateGame, OpenSoloBoard and RerollSoloBoard so
/// the three can never draw differently.
/// </summary>
private async Task<(bool IsHouseEdge, string? Mode, List<GameBoardChoice>? Board)>
    DrawHouseEdgeBoard(Guid parametersId, GameParameters fallback, string? requestedMode)
{
    var stored = await _context.GameParameters
        .AsNoTracking()
        .Include(x => x.PresetChoices.Where(c => c.Active))
        .Include(x => x.BoardModes)
        .FirstOrDefaultAsync(x => x.Id == parametersId);

    var houseEdgeSource = stored ?? fallback;
    if (!houseEdgeSource.IsHouseEdge)
        return (false, null, null);

    var pool = stored?.PresetChoices?.ToList() ?? new List<PresetChoice>();

    // ⚠️ An unknown or missing mode falls back to the DEFAULT (lowest Position) rather than refusing:
    // mode selection sits on the path to a paid bet, and a stale browser value must never become a
    // refused bet. Names are matched case-insensitively; the stored spelling is what we persist.
    var modes = houseEdgeSource.BoardModes?.OrderBy(x => x.Position).ToList() ?? new List<GameBoardMode>();
    if (modes.Count == 0)
    {
        Log.Error("House-edge game type {ParametersId} has no board modes; no round opened.", parametersId);
        return (true, null, null);
    }

    var mode = modes.FirstOrDefault(x => string.Equals(x.Name, requestedMode, StringComparison.OrdinalIgnoreCase))
               ?? modes[0];

    var validation = HouseEdgeRules.ValidateMode(houseEdgeSource, mode, pool);
    if (!validation.IsValid)
    {
        Log.Error("House-edge game type {ParametersId} mode {Mode} is misconfigured; no round opened: {Reason}", parametersId, mode.Name, validation.Reason);
        return (true, mode.Name, null);
    }

    var board = HouseEdgeBoard.Draw(pool, HouseEdgeRules.ParseLadder(mode.Ladder), Random.Shared);
    if (board == null)
    {
        Log.Error("House-edge game type {ParametersId} cannot fill mode {Mode}'s ladder {Ladder}; no round opened.", parametersId, mode.Name, mode.Ladder);
        return (true, mode.Name, null);
    }

    var boardValidation = HouseEdgeRules.ValidateBoard(board);
    if (!boardValidation.IsValid)
    {
        Log.Error("House-edge game type {ParametersId} drew an invalid board; no round opened: {Reason}", parametersId, boardValidation.Reason);
        return (true, mode.Name, null);
    }

    return (true, mode.Name, board);
}
```

- [ ] **Step 4: Set the label on open and reroll**

`OpenSoloBoard(GameParameters parameters, string? mode)` — call `DrawHouseEdgeBoard(parameters.Id, parameters, mode)` and set `game.BoardMode = drawnMode;` before `AddAsync`.

`RerollSoloBoard(Guid gameId, string? mode)` — call `DrawHouseEdgeBoard(game.ParametersId, game.Parameters, mode)`, then alongside the existing seed/TTL writes:

```csharp
game.BoardMode = drawnMode;
_context.Entry(game).Property(x => x.BoardMode).IsModified = true;
```

`CreateGame` — call `DrawHouseEdgeBoard(parameters.Id, parameters, null)` and set `game.BoardMode` from the returned name.

Update `IGameService` signatures to match.

- [ ] **Step 5: Run the tests**

Run: `scripts\run_tests.bat`
Expected: the four new tests PASS; `CreateGameTests` updated for the default mode.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Draw and record the board mode on every house-edge round."
```

---

### Task 8: Accept a mode on the two board endpoints

**Files:**
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/OpenHouseEdgeBoard/OpenHouseEdgeBoardRequest.cs` + `…Handler.cs`
- Modify: `SpaceStroker.Infrastructure/Handlers/Games/RerollHouseEdgeBoard/RerollHouseEdgeBoardRequest.cs` + `…Handler.cs`
- Modify: `SpaceStroker.Api/Controllers/GamesController.cs`

**Interfaces:**
- Consumes: `IGameService.OpenSoloBoard/RerollSoloBoard` (Task 7).
- Produces: `POST /games/{gameTypeId}/boards` and `POST /games/rounds/{roundId}/reroll` accept an **optional** JSON body `{ "mode": "SUPER" }`.

- [ ] **Step 1: Widen the requests**

```csharp
/// <summary>Open a private board for a house-edge type (specs/hyper-roll-solo-rounds.md §6.1), in a
/// given mode (#193). A null/unknown Mode opens the type's default mode — never a refusal.</summary>
public record OpenHouseEdgeBoardRequest(Guid GameTypeId, string? Mode = null) : IRequest<ActiveGameDto>;
```

```csharp
/// <summary>The lever reroll: redraw an unbet solo board in place, optionally switching mode (#193).</summary>
public record RerollHouseEdgeBoardRequest(Guid RoundId, string? Mode = null) : IRequest<ActiveGameDto>;
```

- [ ] **Step 2: Pass it through**

`OpenHouseEdgeBoardHandler`: `var gameId = await _gameService.OpenSoloBoard(parameters!, request.Mode);`
`RerollHouseEdgeBoardHandler`: `rerolled = await _gameService.RerollSoloBoard(request.RoundId, request.Mode);`

Nothing else in either handler changes — the refusals stay exactly as vague as they are.

- [ ] **Step 3: Bind the body**

In `GamesController`:

```csharp
/// <summary>The optional mode on the two board routes (#193). A missing body keeps the old behaviour.</summary>
public record BoardModeBody(string? Mode);

[HttpPost("{gameTypeId:guid}/boards")]
public async Task<IActionResult> OpenHouseEdgeBoard(Guid gameTypeId, [FromBody] BoardModeBody? body = null)
    => await ProcessRequest(new OpenHouseEdgeBoardRequest(gameTypeId, body?.Mode));

[HttpPost("rounds/{roundId:guid}/reroll")]
public async Task<IActionResult> RerollHouseEdgeBoard(Guid roundId, [FromBody] BoardModeBody? body = null)
    => await ProcessRequest(new RerollHouseEdgeBoardRequest(roundId, body?.Mode));
```

- [ ] **Step 4: Build**

Run: `dotnet build src/SpaceStroker.Api`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Api src/SpaceStroker.Infrastructure
git commit -m "Accept a board mode when opening and rerolling a Hyper Roll board."
```

---

### Task 9: Publish the modes and the round's mode

**Files:**
- Modify: `SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs`, `ActiveGameDto.cs`, `GameVerificationDto.cs`, `GameHistoryDto.cs`
- Modify: `SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`, `Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs`
- Modify: `SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Modify: `SpaceStroker.Tests/UnitTests/Handlers/AdminGameTypeHandlerTests.cs` (cache key literal)
- Test: a new `SpaceStroker.Tests/UnitTests/Contracts/GameHistoryDtoTests.cs`

**Interfaces:**
- Produces: `GameParametersDto.Modes` (`IEnumerable<BoardModeDto>?`), `BoardModeDto { string Name; int WordCount; decimal RtpPercent; decimal TopMultiplier; }`, `ActiveGameDto.BoardMode`, `GameVerificationDto.BoardMode`, `GameHistoryDto.BoardMode`, cache key `ActiveParameters:v3`.

- [ ] **Step 1: Write the failing test that matters most**

```csharp
[Fact]
public void ClonePublic_CarriesTheBoardMode()
{
    var row = new GameHistoryDto { GameId = Guid.NewGuid(), GameTypeName = "Hyper Roll", BoardMode = "SUPER" };

    row.ClonePublic().BoardMode.ShouldBe("SUPER");
}
```

⚠️ This is the one that bites silently: `MemoryCacheService` stores the reference, so a `ClonePublic` that drops the field loses it on every cached row.

- [ ] **Step 2: Run and watch it fail**

Run: `scripts\run_tests.bat`
Expected: compile error — `BoardMode` does not exist on `GameHistoryDto`.

- [ ] **Step 3: Add the fields**

`GameHistoryDto`: add `public string? BoardMode { get; set; }` **and** copy it inside `ClonePublic()`.

`ActiveGameDto`: `public string? BoardMode { get; set; }` set from `game.BoardMode` in the constructor.

`GameVerificationDto`: `public string? BoardMode { get; set; }`, projected from the loaded game.

`GameParametersDto`:

```csharp
/// <summary>
/// #193 — the selectable boards, in dial order (lowest Position first); null for a non-house-edge type.
/// WordCount and TopMultiplier are derived from the mode's ladder so nothing is stored twice.
/// </summary>
public IEnumerable<BoardModeDto>? Modes { get; set; } = parameters.IsHouseEdge && parameters.BoardModes != null
    ? parameters.BoardModes.OrderBy(x => x.Position).Select(BoardModeDto.From).ToList()
    : null;
```

```csharp
public class BoardModeDto
{
    public string Name { get; set; }
    public int WordCount { get; set; }
    public decimal RtpPercent { get; set; }
    public decimal TopMultiplier { get; set; }

    public static BoardModeDto From(GameBoardMode mode)
    {
        var ladder = HouseEdgeRules.ParseLadder(mode.Ladder);
        return new BoardModeDto
        {
            Name = mode.Name,
            WordCount = ladder.Count,
            RtpPercent = mode.RtpPercent,
            TopMultiplier = ladder.Count == 0 ? 0m : ladder.Max(x => x.Multiplier),
        };
    }
}
```

Keep the scalar `RtpPercent` for older clients, now the best mode's:

```csharp
// The "up to" number an older SPA shows. With modes it is the best mode's RTP.
public decimal? RtpPercent { get; set; } = parameters.IsHouseEdge && parameters.BoardModes is { Count: > 0 }
    ? parameters.BoardModes.Max(x => x.RtpPercent)
    : parameters.RtpPercent;
```

- [ ] **Step 4: Project the mode**

`GameHistoryService` (~`:207`): add `BoardMode = game.BoardMode,` to the `new GameHistoryDto { … }`.
`GetGameVerificationHandler`: add `BoardMode = game.BoardMode` to its DTO construction.
Anywhere a `Game` is loaded for `ActiveGameDto`, `BoardMode` rides on the entity already — no extra include needed.

- [ ] **Step 5: Bump the cache key**

`CacheHelper.cs:18`: `=> new("ActiveParameters:v3", TimeSpan.FromHours(12));`
Update the literal in `AdminGameTypeHandlerTests.cs:58` to match.

⚠️ Required by `hyper-roll.md` §6.8: `GameParametersDto` changed shape, so the key must move or a mid-deploy old API revision rewrites the shared blob without `Modes`.

- [ ] **Step 6: Run the tests**

Run: `scripts\run_tests.bat`
Expected: green.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Publish the board modes and the round's mode."
```

---

### Task 10: Pin the locked odds

**Files:**
- Create: `SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeModeOddsTests.cs`
- Modify: any remaining test with a 2-part ladder literal

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write the pinning tests**

```csharp
public class HouseEdgeModeOddsTests
{
    // The LOCKED boards (specs/hyper-roll-modes.md §4.2). Changing a number here is changing the odds.
    public static TheoryData<string, decimal, string> Boards => new()
    {
        { "AVERAGE", 91.50m, "4:3:305011,5:4:228758,6:5:183007,7:6:152505,8:7:130719" },
        { "SUPER",   91.14m, "5:3:303797,6:4:227848,7:6:151899,8:8:113924,9:9:101266,9:9:101266" },
        { "HYPER",   91.84m, "6:4:229591,7:6:153061,8:8:114796,8:8:114796,9:9:102041,9:9:102041,10:10:91837,10:10:91837" },
    };

    [Theory]
    [MemberData(nameof(Boards))]
    public void EveryLaddersWeightsSumToOneMillion(string name, decimal rtp, string ladder)
        => HouseEdgeRules.ParseLadder(ladder).Sum(x => (long)x.Weight).ShouldBe(1_000_000L, name);

    [Theory]
    [MemberData(nameof(Boards))]
    public void EverySlotPaysBackItsModesPublishedRtp(string name, decimal rtp, string ladder)
    {
        foreach (var slot in HouseEdgeRules.ParseLadder(ladder))
        {
            var payback = slot.Weight / 1_000_000m * slot.Multiplier * 100m;
            payback.ShouldBe(rtp, 0.01m, $"{name} {slot.Length}-letter slot");
        }
    }

    [Fact]
    public void HyperIsStillTheBoardThatShipped()
    {
        // The multiset of (multiplier, weight) that shipped with #183 — slot ORDER is cosmetic, because
        // HouseEdgeBoard.Draw groups by tier and shuffles the whole board afterwards.
        var expected = new[] { (4m, 229591), (6m, 153061), (8m, 114796), (8m, 114796),
                               (9m, 102041), (9m, 102041), (10m, 91837), (10m, 91837) };

        HouseEdgeRules.ParseLadder(Boards.Cast<object[]>().Single(x => (string)x[0] == "HYPER")[2] as string)
            .Select(x => (x.Multiplier, x.Weight)).OrderBy(x => x.Multiplier).ThenBy(x => x.Weight)
            .ShouldBe(expected.OrderBy(x => x.Item1).ThenBy(x => x.Item2));
    }
}
```

- [ ] **Step 2: Run and confirm green**

Run: `scripts\run_tests.bat`
Expected: all PASS. If a payback assertion fails, **the config is wrong — do not relax the tolerance.**

- [ ] **Step 3: Fix any stragglers**

Grep the test project for old 2-part ladder literals and update them to triples:

```bash
grep -rn '"[0-9]\+:[0-9]\+[,"]' src/SpaceStroker.Tests --include=*.cs
```

- [ ] **Step 4: Run the whole unit suite**

Run: `scripts\run_tests.bat`
Expected: fully green.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Tests
git commit -m "Pin the three locked Hyper Roll boards."
```

---

### Task 11: The mandatory integration test

**Files:**
- Create: `SpaceStroker.Tests.Integration/…/HyperRollModeFlowTests.cs` (beside `HyperRollFlowTests`)

**Interfaces:**
- Consumes: the whole feature, through the real API + Consumers hosts against Testcontainers Postgres/Redis/Postgres-transport.

Level is **`Feature`**, so this is mandatory (`constraints.md` → "Integration tests are mandatory for every new feature").

- [ ] **Step 1: Write the happy path, one case per mode**

Model it on the existing `HyperRollFlowTests` happy path. For each of `AVERAGE` (5 keys), `SUPER` (6) and `HYPER` (8):

1. `POST /games/{typeId}/boards` with `{ "mode": "<name>" }`.
2. Assert the response's `boardChoices` count and that `boardMode` echoes the mode.
3. Place a paid bet on a known board word at the minimum stake.
4. Steer the round so that word wins (the existing fallback-draw steer the Hyper Roll suite uses).
5. Assert the credit equals `stake × that slot's multiplier` and the round's `BoardMode` persisted.

- [ ] **Step 2: Add the three complex-flow cases**

```
Reroll_SwitchesModeOnAnUnbetRound        → same round id, new key count, NEW ServerSeedHash, BoardMode updated
Reroll_IsRefusedOnceTheRoundHasABet      → 400 GameNotValidForBets, board rows unchanged
PreModeRound_ReadsBackAsHyperAndPaysOut  → a round seeded without a mode label behaves exactly as before
```

- [ ] **Step 3: Run the integration suite**

Run: `scripts\run_integration_tests.bat`
Expected: green. Docker must be running.

- [ ] **Step 4: Commit**

```bash
git add src/SpaceStroker.Tests.Integration
git commit -m "Cover the three Hyper Roll board modes end to end."
```

---

## Self-Review

**Spec coverage:** §4 ladders → Tasks 5 + 10. §5 data model → Task 2. §6 config → Task 5. §7.1 rules → Tasks 1 + 3. §7.2 draw → Task 4. §7.3–7.4 GameService → Task 7. §7.5 handlers/routes → Task 8. §7.6 seeding/self-heal → Task 6. §7.7 contracts + cache → Task 9. §13.1 unit tests → Tasks 1,3,4,5,6,7,9,10. §13.2 integration → Task 11. §8 frontend → the separate frontend plan. §12 rollout → an ops step, no code.

**Placeholders:** none — every code step carries the code.

**Type consistency:** `HouseEdgeSlot(int Length, decimal Multiplier, int Weight)` is used with that member order in Tasks 1, 4, 5, 9. `ValidateMode(GameParameters, GameBoardMode, IReadOnlyCollection<PresetChoice>)` is defined in Task 3 and called in Task 7. `OpenSoloBoard(GameParameters, string?)` / `RerollSoloBoard(Guid, string?)` defined in Task 7, called in Task 8.
