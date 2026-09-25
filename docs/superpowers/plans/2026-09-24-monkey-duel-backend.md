# Monkey Duel — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the `monkify-backend` half of Monkey Duel (#184) — a 1v1 race game type where two 6-letter words share one typer stream and the first word typed takes both stakes, in two per-round kinds: `House` (the house is the counterparty, funded from the HouseSeed wallet) and `OpenRoom` (a public board of player-created rooms).

**Architecture:** One `GameParameters` row flagged `IsDuel`; the duel **kind** is per round (`Game.DuelKind`), as is the **stake** (`Game.Stake`). A room *is* a `Game` in `WaitingBets` with `OwnerPlayerId` = creator and `ClaimedByPlayerId` = joiner/house — both bound by concurrency-safe conditional updates, which is what makes the join race safe. The type stays **free-text** to every shipped path (`PresetChoices` empty), so choice exclusivity, the unique `(GameId, ExclusiveChoice)` index and the typer all work unchanged; the curated house-word pool lives in its own `DuelWordPool` table. Payout reuses `BetDomainService.CalculateRewardForBet` untouched — pari-mutuel, commission on profit above own stake.

**Tech Stack:** .NET 8, EF Core + Npgsql (inline `OnModelCreating` config, migrations applied only by `SpaceStroker.Consumers`), MediatR, MassTransit over Postgres transport, SignalR (`ActiveGamesHub`), Solnet, xUnit + Shouldly + Moq (`SpaceStroker.Tests`), Testcontainers (`SpaceStroker.Tests.Integration`).

**Spec:** `monkify-meta/specs/monkey-duel.md` (read §3.1, §4, §5, §6, §9 before starting)

**Branch / worktree:** `feature/monkey-duel` → `C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-backend`. Never work in the hub.

---

## Global Constraints

- **Cut is 2%, never 5%.** `TokenSettings.CommissionPercentage = 2.0`, pinned by `CommissionRateTests.CommissionPercentage_IsTwoPercent`. Duel payout = `stake × (2 − rate)` = **1.98 × stake**, NOT `2 × stake × (1 − rate)`.
- **`IsHouseEdge` stays `false` on a duel type.** That flag selects `CalculateHouseEdgeReward` (treasury × multiplier, no commission). Reusing it pays `stake × multiplier` from the treasury.
- **A duel type's `PresetChoices` is ALWAYS EMPTY.** "Has `PresetChoices` rows" is the codebase-wide discriminator for "is a preset race" (§3.1 S1).
- **Never set `Bet.FundedByOrbit` on a house duel bet.** The HouseSeed wallet is both the transfer's sender and the bet's `Wallet`, so `ValidateBetPayment` passes untouched.
- **EF configuration is inline** in `SpaceStrokerDbContext.OnModelCreating`. No `IEntityTypeConfiguration`. Never hand-edit `SpaceStrokerDbContextModelSnapshot.cs`.
- **Migrations are applied only by `SpaceStroker.Consumers`.** Add under `SpaceStroker.Infrastructure/Migrations/`.
- **Mock-only secrets.** The HouseSeed keypair stays `MOCK_…` in every committed file.
- **Vague user-facing messaging** (`constraints.md` §6). Reuse the **existing** `ErrorMessages.GameNotValidForBets`, `ParameterNotFound`, `ParameterHasNoActiveGames`, `ChoiceAlreadyTaken`, `HouseEdgeUnderMaintenance`. **Do not add a new error message** — §10.1 row 31 says the intent is that none is needed.
- **Host split.** API stays stateless (open/list/cancel a room, availability, bet submission). The house counterparty bet, the round lifecycle, the typer and every payout stay in `SpaceStroker.Consumers`.
- **No rate-limit rule may contain `/bets`.** `RateLimitSettingsTests.GeneralRules_NeverCoverTheBetPath` fails loudly otherwise.
- **The pool ships EMPTY and the type ships `ComingSoon: true`** (decision 10). Flipping it on is a separate config change after the Monte Carlo.
- **Run unit tests with `scripts/run_tests.bat`**, never a bare `dotnet test` (the script excludes the `ProductionGames` Monte Carlo suite by design — a bare run "fails" ~156 tests and means nothing).
- ⚠️ **`scripts\run_tests.bat` takes NO `--filter`.** It hardcodes `--filter "FullyQualifiedName~UnitTests&FullyQualifiedName!~ProductionGames"` and forwards `%*`, so a second `--filter` makes `dotnet test` print its CLI help instead of running anything — a silent green. Where a task below writes `scripts\run_tests.bat --filter "X"`, run instead:
  `cd src\SpaceStroker.Tests && dotnet test --filter "X&FullyQualifiedName!~ProductionGames"`
  Full-suite verification uses the bare `scripts\run_tests.bat` with no arguments. (Found in Task 1.)
- **Commit after every task.** Push `feature/monkey-duel` to `origin` after the first commit.

## Review Focus

Five input classes the spec implies but no task's own happy path exercises. Each has its test folded into the task that owns the code.

1. **`MaximumStake` is null on a duel type** (misconfigured seed, or an admin-created row). The amount check must **fail closed** — `InvalidAmount`, never "no ceiling, anything goes". → Task 3.
2. **A `House` round opens while `DuelWordPool` is empty** (the shipped state until the Monte Carlo runs). `OpenDuelRoom` must return null and the handler answer vaguely — the last guard behind decision 10. → Task 7.
3. **The HouseSeed balance read returns null** (RPC down). Coverage must be **not coverable**, and the house tab unavailable — never "unknown, so allow". → Task 4 + Task 9.
4. **The creator bets in their own room.** `OwnerPlayerId <> @p` on the claim must refuse it; otherwise it is a self-duel that always pays them back minus the cut. → Task 10.
5. **A redelivered `DuelCreatorBetPlacedEvent`** (MassTransit at-least-once). The idempotency check must run **before signing**, or the house pays a second stake it can never recover. → Task 11.

---

## File Structure

### Created

| File | Responsibility |
| --- | --- |
| `SpaceStroker.Domain/Games/ValueObjects/DuelKind.cs` | The per-round kind enum (`House = 1`, `OpenRoom`). |
| `SpaceStroker.Domain/Games/Entities/DuelWordPool.cs` | One curated house-word row per duel game type. |
| `SpaceStroker.Domain/Games/Services/DuelHouseWordGenerator.cs` | Pure seed-derived house word draw (mirrors `OrbitChoiceGenerator`). |
| `SpaceStroker.Domain/Games/Services/DuelHouseCoverage.cs` | Pure HouseSeed coverage rule + clamped ceiling. |
| `SpaceStroker.Infrastructure/Services/Games/DuelHouseCoverageService.cs` (+ `IDuelHouseCoverageService.cs`) | Advisory-locked wrapper, distinct lock key from house-edge. |
| `SpaceStroker.Infrastructure/Services/Games/DuelWordPoolReconciler.cs` | Startup pool reconcile (insert missing, deactivate removed). |
| `SpaceStroker.Infrastructure/Services/Duel/DuelHouseBetService.cs` (+ interface) | The house counterparty bet (Consumers; moves money). |
| `SpaceStroker.Infrastructure/Consumers/DuelHouseBet/DuelHouseBetConsumer.cs` + `DuelCreatorBetPlacedEvent.cs` | Its own receive endpoint. |
| `SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/*` | `POST /games/{gameTypeId}/duels`. |
| `SpaceStroker.Infrastructure/Handlers/Games/GetDuelRooms/*` | `GET /games/{gameTypeId}/duels` (paged board). |
| `SpaceStroker.Infrastructure/Handlers/Games/CancelDuelRoom/*` | `POST /games/rounds/{roundId}/duel-cancel`. |
| `SpaceStroker.Infrastructure/Contracts/Games/DuelRoomDto.cs` | Board row + `DuelRoomEvent`. |
| `SpaceStroker.Infrastructure/Migrations/*_MonkeyDuel.cs` | Additive migration. |

### Modified

`GameParameters.cs`, `Game.cs`, `BetDomainService.cs`, `GameSettings.cs`, `OperatorSettings.cs`, `SpaceStrokerDbContext.cs`, `RegisterBetHandler.cs`, `CheckBetAvailabilityHandler.cs` (+ request body), `GetWalletNonceRequest.cs`, `GetActiveGameForParameterHandler.cs`, `GameService.cs` (+ `IGameService.cs`), `HouseSeedBetService.cs`, `BetPlacedConsumer.cs`, `RunGameConsumer.cs`, `ActiveGamesHub.cs`, `GameParametersDto.cs`, `ActiveGameDto.cs`, `BetAvailabilityDto.cs`, `GameVerificationDto.cs` (+ handler), `GamesController.cs`, `Consumers/Program.cs`, `spacestrokersettings.json`.

---

## Task 1: Data model — entities, EF mapping, migration

**Files:**
- Create: `src/SpaceStroker.Domain/Games/ValueObjects/DuelKind.cs`
- Create: `src/SpaceStroker.Domain/Games/Entities/DuelWordPool.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
- Create: `src/SpaceStroker.Infrastructure/Migrations/*_MonkeyDuel.cs` (generated)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelEntityMappingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DuelKind` enum; `DuelWordPool { Guid Id, Guid ParametersId, GameParameters Parameters, string Word, bool Active }`; `GameParameters.IsDuel : bool`, `GameParameters.DuelWordPool : ICollection<DuelWordPool>`; `Game.DuelKind : DuelKind?`, `Game.Stake : decimal?`, `Game.ClaimedByPlayerId : Guid?`, `Game.ClaimedBy : Player?`, `Game.DuelPoolSnapshot : string?`; `SpaceStrokerDbContext.DuelWordPool : DbSet<DuelWordPool>`.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelEntityMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games;

/// <summary>
/// #184 Monkey Duel: the new columns round-trip and the duel defaults are what every
/// non-duel row already reads as (IsDuel false, every Game duel column null).
/// </summary>
public class DuelEntityMappingTests
{
    private static SpaceStrokerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SpaceStrokerDbContext>()
            .UseInMemoryDatabase($"duel-mapping-{Guid.NewGuid():N}")
            .Options;
        return new SpaceStrokerDbContext(options);
    }

    [Fact]
    public void DuelWordPool_RoundTripsAgainstItsGameType()
    {
        using var context = NewContext();

        var parameters = new GameParameters(GameCharacterType.Letters, 6, true)
        {
            Name = "Monkey Duel", Description = "d", RequiredAmount = 0.002m,
            MinimumNumberOfPlayers = 2, Active = true,
            IsDuel = true, MaximumStake = 0.5m,
            DuelWordPool = new List<DuelWordPool> { new("banana"), new("cosmos") { Active = false } },
        };

        context.GameParameters.Add(parameters);
        context.SaveChanges();

        var stored = context.DuelWordPool.AsNoTracking()
            .Where(x => x.ParametersId == parameters.Id)
            .OrderBy(x => x.Word)
            .ToList();

        stored.Count.ShouldBe(2);
        stored[0].Word.ShouldBe("banana");
        stored[0].Active.ShouldBeTrue();
        stored[1].Word.ShouldBe("cosmos");
        stored[1].Active.ShouldBeFalse();
    }

    [Fact]
    public void Game_DuelColumns_RoundTrip()
    {
        using var context = NewContext();

        var parametersId = Guid.NewGuid();
        var claimedBy = Guid.NewGuid();
        var game = new Game(parametersId)
        {
            DuelKind = DuelKind.OpenRoom,
            Stake = 0.125m,
            ClaimedByPlayerId = claimedBy,
            DuelPoolSnapshot = "banana,cosmos,rocket",
        };

        context.Games.Add(game);
        context.SaveChanges();

        var stored = context.Games.AsNoTracking().Single(x => x.Id == game.Id);
        stored.DuelKind.ShouldBe(DuelKind.OpenRoom);
        stored.Stake.ShouldBe(0.125m);
        stored.ClaimedByPlayerId.ShouldBe(claimedBy);
        stored.DuelPoolSnapshot.ShouldBe("banana,cosmos,rocket");
    }

    [Fact]
    public void Game_DuelColumns_AreNullOnAnOrdinaryRound()
    {
        using var context = NewContext();

        var game = new Game(Guid.NewGuid());
        context.Games.Add(game);
        context.SaveChanges();

        var stored = context.Games.AsNoTracking().Single(x => x.Id == game.Id);
        stored.DuelKind.ShouldBeNull();
        stored.Stake.ShouldBeNull();
        stored.ClaimedByPlayerId.ShouldBeNull();
        stored.DuelPoolSnapshot.ShouldBeNull();
    }

    [Fact]
    public void GameParameters_IsDuel_DefaultsToFalse()
    {
        new GameParameters().IsDuel.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d C:\Users\kurok\Desktop\monkify\.worktrees\monkey-duel\monkify-backend && scripts\run_tests.bat --filter "FullyQualifiedName~DuelEntityMappingTests"`
Expected: FAIL — `DuelKind`, `DuelWordPool`, `IsDuel`, `Game.Stake` do not exist (compile errors).

- [ ] **Step 3: Create the `DuelKind` enum**

`src/SpaceStroker.Domain/Games/ValueObjects/DuelKind.cs`:

```csharp
using System.ComponentModel;

namespace SpaceStroker.Domain.Games.ValueObjects
{
    /// <summary>
    /// #184 Monkey Duel: which kind of duel a ROUND is. The game TYPE is one row (GameParameters.IsDuel);
    /// the kind is chosen when the round is opened and never changed.
    /// </summary>
    public enum DuelKind
    {
        [Description("The player duels the house, which bets from the HouseSeed wallet.")]
        House = 1,
        [Description("An open room another player may take.")]
        OpenRoom
    }
}
```

- [ ] **Step 4: Create the `DuelWordPool` entity**

`src/SpaceStroker.Domain/Games/Entities/DuelWordPool.cs`:

```csharp
using System;
using SpaceStroker.Common.Models;

namespace SpaceStroker.Domain.Games.Entities
{
    /// <summary>
    /// The curated readable-word pool a HOUSE duel draws its word from (#184). One row per word, per
    /// duel game type.
    ///
    /// ⚠️ A separate table from PresetChoice ON PURPOSE (specs/monkey-duel.md §3.1 S1): a duel type must
    /// look like a FREE-TEXT type to every shipped path — bet validation, choice exclusivity, the unique
    /// (GameId, ExclusiveChoice) index, the typer's competing-choice set and character pool, and the
    /// SPA's archetype resolver all branch on GameParameters.PresetChoices being empty.
    ///
    /// ⚠️ Active is how a word is retired. Because it is mutable, the pool a round actually drew from is
    /// snapshotted onto Game.DuelPoolSnapshot when the round opens — a later edit can never change what a
    /// settled round can prove.
    ///
    /// ⚠️ POOL SIZE IS AN OUTPUT OF THE MONTE CARLO (decision 10), not a guess. Do not seed a pool before
    /// DuelEdgeTests has run and shown the edge survives the best player counter-word.
    /// </summary>
    public class DuelWordPool : TableEntity
    {
        public DuelWordPool() { }
        public DuelWordPool(string word) { Word = word; }

        public GameParameters Parameters { get; set; }
        public Guid ParametersId { get; set; }
        public string Word { get; set; }
        public bool Active { get; set; } = true;
    }
}
```

- [ ] **Step 5: Add the `GameParameters` columns**

In `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs`, immediately after the `BoardLadder` property:

```csharp
        // #184 Monkey Duel: a 1v1 race type. Two bets, one typer stream, the first word typed takes the
        // 2-stake pot less the ordinary commission on profit above own stake.
        //
        // ⚠️ NOT IsHouseEdge. That flag selects the treasury × multiplier payout with NO commission
        // (BetDomainService.CalculateHouseEdgeReward). A duel is pari-mutuel: IsHouseEdge stays false so
        // CalculateRewardForBet's shipped path is reused unchanged.
        //
        // ⚠️ A duel type's PresetChoices is ALWAYS EMPTY. The curated house word pool lives in
        // DuelWordPool, because "has PresetChoices" is what the whole codebase reads as "is a preset race"
        // (see specs/monkey-duel.md §3.1 S1).
        //
        // RequiredAmount is the MINIMUM stake and MaximumStake the maximum (required when IsDuel), exactly
        // as for a house-edge type. Create-only, like every other bet bound. AllowOrbit is false.
        public bool IsDuel { get; set; }
```

And in the navigation block at the bottom, beside `PresetChoices`:

```csharp
        public ICollection<DuelWordPool> DuelWordPool { get; set; }
```

- [ ] **Step 6: Add the `Game` columns**

In `src/SpaceStroker.Domain/Games/Entities/Game.cs`, after the `BoardExpiresAt` property:

```csharp
        /// <summary>
        /// #184 Monkey Duel: which kind of duel this round is. Null for every non-duel round.
        /// Chosen when the round is opened and never changed — the type is one row, the kind is per round.
        /// </summary>
        public DuelKind? DuelKind { get; set; }

        /// <summary>
        /// #184: this round's OWN stake, in SOL. Null until the creator's (first) bet binds it, inside the
        /// same transaction that claims the round — then never changed. The joiner, or the house, must match
        /// it exactly. Null for every non-duel round, which reads its stake from
        /// GameParameters.RequiredAmount as before.
        /// </summary>
        public decimal? Stake { get; set; }

        /// <summary>
        /// #184: the second player (a joiner, or the house player on a house duel). Claimed by a
        /// concurrency-safe conditional update — first write wins — which is what makes the join race safe
        /// (AC 8). Null while the room is unmatched; a room with this set can no longer be cancelled.
        /// ⚠️ OwnerPlayerId is the CREATOR. Both columns live on this same entity.
        /// </summary>
        public Guid? ClaimedByPlayerId { get; set; }
        public Player? ClaimedBy { get; set; }

        /// <summary>
        /// #184: the ACTIVE DuelWordPool words this round could have drawn its house word from, ordinal
        /// ascending, comma-separated — snapshotted when the round opens, in the same SaveChanges as the
        /// seed. Published by the verification endpoint so the house word draw is reproducible offline even
        /// after the pool is edited. Null for a PVP room and for every non-duel round.
        /// </summary>
        public string? DuelPoolSnapshot { get; set; }
```

- [ ] **Step 7: Add the inline EF mapping**

In `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`, add the DbSet beside `PresetChoices`:

```csharp
        public DbSet<DuelWordPool> DuelWordPool { get; set; }
```

Inside the `modelBuilder.Entity<GameParameters>(...)` block, after the `BoardLadder` line:

```csharp
                // #184 Monkey Duel. Deliberately NOT indexed, for the reason ComingSoon documents above:
                // GameParameters holds a handful of rows.
                builder.Property(x => x.IsDuel).IsRequired().HasDefaultValue(false);
```

Inside the `modelBuilder.Entity<Game>(...)` block, after the `OrbitGateExpiresAt` line:

```csharp
                // #184 Monkey Duel. All four are null on every non-duel round.
                builder.Property(x => x.DuelKind).IsRequired(false);
                builder.Property(x => x.Stake).HasPrecision(18, 9).IsRequired(false);
                builder.Property(x => x.ClaimedByPlayerId).IsRequired(false);
                builder.Property(x => x.DuelPoolSnapshot).HasMaxLength(8000).IsRequired(false);
                builder.HasOne(x => x.ClaimedBy).WithMany().HasForeignKey(x => x.ClaimedByPlayerId)
                       .OnDelete(DeleteBehavior.Restrict);
```

and, with the other `HasIndex` calls in that block:

```csharp
                // The open-rooms board query: unmatched rooms of one type, newest first. Filtered, so it
                // costs nothing on the millions of non-duel rounds.
                builder.HasIndex(x => new { x.ParametersId, x.DuelKind, x.Status, x.ClaimedByPlayerId })
                       .HasFilter("\"DuelKind\" IS NOT NULL");
```

Add a new entity block after the `PresetChoice` block:

```csharp
            modelBuilder.Entity<DuelWordPool>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.Property(x => x.ParametersId).IsRequired();
                builder.Property(x => x.Word).IsRequired().HasMaxLength(20);
                builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);
                builder.HasOne(x => x.Parameters).WithMany(x => x.DuelWordPool).HasForeignKey(x => x.ParametersId);
                // Uniqueness is safe here (unlike PresetChoice): the table is new, so no existing row can
                // duplicate and fail the migration.
                builder.HasIndex(x => new { x.ParametersId, x.Word }).IsUnique();
            });
```

Add `using SpaceStroker.Domain.Games.ValueObjects;` if not already present.

- [ ] **Step 8: Run the test to verify it passes**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelEntityMappingTests"`
Expected: PASS (4 tests).

- [ ] **Step 9: Generate the migration**

```bash
cd src/SpaceStroker.Infrastructure
dotnet ef migrations add MonkeyDuel --startup-project ../SpaceStroker.Consumers
```

Then **read the generated `Up()`** and confirm it is additive only: one `bool` with default `false` on `GameParameters`; four nullable columns + one filtered index on `Games`; one new `DuelWordPool` table with its unique index. No `DropColumn`, no `AlterColumn` on an existing column, no data migration. If anything else appears, delete the migration, fix the model, and regenerate — never hand-edit the snapshot.

- [ ] **Step 10: Verify the whole suite still passes**

Run: `scripts\run_tests.bat`
Expected: PASS — no pre-existing test broken by the new columns.

- [ ] **Step 11: Commit and push**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Add the Monkey Duel data model: DuelKind, DuelWordPool, and the Game duel columns."
git push -u origin feature/monkey-duel
```

---

## Task 2: `DuelHouseWordGenerator` — the seed-derived house word

**Files:**
- Create: `src/SpaceStroker.Domain/Games/Services/DuelHouseWordGenerator.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseWordGeneratorTests.cs`

**Interfaces:**
- Consumes: `ProvablyFair.Keystream`, `ProvablyFair.NextIndex` (both existing).
- Produces: `static string DuelHouseWordGenerator.DeriveWord(string serverSeed, Guid gameId, int attempt, IReadOnlyList<string> pool)` and `static string DuelHouseWordGenerator.BuildPrefix(Guid gameId, int attempt)`.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseWordGeneratorTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.Services;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games;

/// <summary>
/// #184: the HOUSE duel's word is DRAWN from the committed ServerSeed, never chosen — so it is
/// verifiable offline and cannot be ground. Mirrors OrbitChoiceGeneratorTests.
/// </summary>
public class DuelHouseWordGeneratorTests
{
    private const string Seed = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
    private static readonly Guid GameId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string[] Pool = ["banana", "cosmos", "gorilla", "meteor", "rocket", "planet", "launch"];

    [Fact]
    public void DeriveWord_IsDeterministic()
    {
        var first = DuelHouseWordGenerator.DeriveWord(Seed, GameId, 0, Pool);
        var second = DuelHouseWordGenerator.DeriveWord(Seed, GameId, 0, Pool);

        second.ShouldBe(first);
        Pool.ShouldContain(first);
    }

    [Fact]
    public void DeriveWord_IgnoresThePoolsOwnOrder()
    {
        // EF yields rows in whatever order it likes; an offline verifier cannot reproduce that, so the
        // draw sorts ordinal first. Same discipline as OrbitChoiceGenerator.DerivePresetChoice.
        var shuffled = Pool.Reverse().ToArray();

        DuelHouseWordGenerator.DeriveWord(Seed, GameId, 0, shuffled)
            .ShouldBe(DuelHouseWordGenerator.DeriveWord(Seed, GameId, 0, Pool));
    }

    [Fact]
    public void DeriveWord_DiffersByAttempt()
    {
        // Not a guarantee for every seed, but the attempt MUST change the keystream label — assert the
        // prefix rather than hoping for a collision-free draw.
        DuelHouseWordGenerator.BuildPrefix(GameId, 1)
            .ShouldNotBe(DuelHouseWordGenerator.BuildPrefix(GameId, 0));
    }

    [Fact]
    public void DeriveWord_DiffersByGameId()
    {
        DuelHouseWordGenerator.BuildPrefix(Guid.NewGuid(), 0)
            .ShouldNotBe(DuelHouseWordGenerator.BuildPrefix(GameId, 0));
    }

    [Fact]
    public void BuildPrefix_IsDistinctFromTheTyperAndOrbitLabels()
    {
        // The typer's prefix is gameId + "|" + bet seeds; Orbit's is "orbit|playerId|attempt". A duel's
        // must line up with neither.
        var prefix = DuelHouseWordGenerator.BuildPrefix(GameId, 0);

        prefix.ShouldBe($"duel|{GameId:D}|0");
        prefix.ShouldNotStartWith(GameId.ToString("D"));
        prefix.ShouldNotStartWith("orbit|");
    }

    [Fact]
    public void DeriveWord_IsUniformOverALargeSample()
    {
        // Rejection sampling means no modulo bias. 7 words × 3000 draws ⇒ ~3000 each; allow ±25%.
        var counts = Pool.ToDictionary(x => x, _ => 0);

        for (var i = 0; i < 21_000; i++)
            counts[DuelHouseWordGenerator.DeriveWord(Seed, Guid.NewGuid(), 0, Pool)]++;

        foreach (var count in counts.Values)
        {
            count.ShouldBeGreaterThan(2_250);
            count.ShouldBeLessThan(3_750);
        }
    }

    [Fact]
    public void DeriveWord_ThrowsOnAnEmptyPool()
    {
        // The caller (OpenDuelRoom) refuses an empty pool long before this — decision 10's last guard.
        // Throwing rather than returning null keeps the failure loud if that guard is ever removed.
        Should.Throw<ArgumentException>(() => DuelHouseWordGenerator.DeriveWord(Seed, GameId, 0, []));
    }

    [Fact]
    public void DeriveWord_ThrowsWithoutACommittedSeed()
    {
        Should.Throw<ArgumentException>(() => DuelHouseWordGenerator.DeriveWord(" ", GameId, 0, Pool));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseWordGeneratorTests"`
Expected: FAIL — `DuelHouseWordGenerator` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SpaceStroker.Domain/Games/Services/DuelHouseWordGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceStroker.Domain.Games.Services
{
    /// <summary>
    /// Derives a HOUSE duel's word from the round's committed ServerSeed (#184).
    ///
    /// Why derive rather than pick: on a house duel the house is the counterparty, so "the house did not
    /// hand-pick a word that beats yours" has to be checkable rather than promised. Everything here is a
    /// pure function of (serverSeed, gameId, attempt, pool), so once the seed is revealed anyone can
    /// recompute the draw and compare.
    ///
    /// ⚠️ Reuses ProvablyFair.Keystream + NextIndex rather than a plain modulo: a modulo would bias the
    /// draw, which is exactly the kind of flaw a verifier would eventually notice. Same reasoning as
    /// OrbitChoiceGenerator.
    ///
    /// Deliberately pure — no EF, no config, no I/O — so it is fully unit-testable and so
    /// monkify-front/src/utils/provablyFair.ts and public/verify.html can mirror it byte-for-byte.
    /// KEEP-IN-SYNC with both.
    /// </summary>
    public static class DuelHouseWordGenerator
    {
        /// <summary>
        /// The keystream label. Distinct from the typer's prefix (gameId + bet seeds) and from Orbit's
        /// ("orbit|playerId|attempt"), so the draws can never line up; distinct per attempt so a retry is
        /// a genuinely new candidate rather than a re-roll a verifier cannot follow.
        /// </summary>
        public static string BuildPrefix(Guid gameId, int attempt) => $"duel|{gameId:D}|{attempt}";

        /// <summary>
        /// The house word for one draw attempt. The caller retries with attempt+1 when the derived word is
        /// already bet in this round (the creator took it), which keeps the whole sequence reproducible.
        ///
        /// ⚠️ Ordinal sort, NOT the collection's own order — EF yields rows in whatever order it likes and
        /// an offline verifier cannot reproduce that. Same discipline as
        /// OrbitChoiceGenerator.DerivePresetChoice and SpaceStrokerTyper.DecideFallbackWinner.
        /// </summary>
        public static string DeriveWord(string serverSeed, Guid gameId, int attempt, IReadOnlyList<string> pool)
        {
            if (string.IsNullOrWhiteSpace(serverSeed))
                throw new ArgumentException("A committed server seed is required.", nameof(serverSeed));

            if (pool == null || pool.Count == 0)
                throw new ArgumentException("The duel word pool is empty.", nameof(pool));

            var ordered = pool.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var keystream = new ProvablyFair.Keystream(serverSeed, BuildPrefix(gameId, attempt));

            return ordered[ProvablyFair.NextIndex(keystream, ordered.Count)];
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseWordGeneratorTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain/Games/Services/DuelHouseWordGenerator.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseWordGeneratorTests.cs
git commit -m "Derive the house duel word from the committed server seed."
git push origin feature/monkey-duel
```

---

## Task 3: `BetDomainService` — the per-round stake rule

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs` (`ChoiceIsValidForGame`)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Bets/BetDomainServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `Game.Stake` (Task 1), `GameParameters.IsDuel` (Task 1), `NativeSol.Decimals` (existing).
- Produces: no new signature — `ChoiceIsValidForGame(Bet, Game, bool)` gains a duel branch. **Payout maths is unchanged** (decision 4).

- [ ] **Step 1: Write the failing tests**

Append to `src/SpaceStroker.Tests/UnitTests/Domain/Bets/BetDomainServiceTests.cs` (inside the existing class; add `using SpaceStroker.Domain.Games.ValueObjects;` if missing):

```csharp
    // ---- #184 Monkey Duel -------------------------------------------------------------------

    /// <summary>A duel type is FREE-TEXT: PresetChoices stays empty (§3.1 S1), IsHouseEdge stays false.</summary>
    private static Game DuelRound(decimal? roundStake, decimal min = 0.002m, decimal? max = 0.5m)
    {
        var parameters = new GameParameters(GameCharacterType.Letters, 6, true)
        {
            Name = "Monkey Duel", Description = "d",
            RequiredAmount = min, MaximumStake = max,
            MinimumNumberOfPlayers = 2, Active = true,
            IsDuel = true, IsHouseEdge = false,
            PresetChoices = new List<PresetChoice>(),
        };

        return new Game(parameters, new List<Bet>())
        {
            DuelKind = DuelKind.OpenRoom,
            Stake = roundStake,
        };
    }

    private static Bet DuelBet(string choice, decimal amount)
        => new(Guid.NewGuid(), "seed", Guid.NewGuid(), choice, amount);

    [Theory]
    [InlineData(0.002)]   // exactly the floor
    [InlineData(0.05)]
    [InlineData(0.5)]     // exactly the ceiling
    public void Duel_CreatorBetInsideTheBounds_IsValid(decimal amount)
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("banana", amount), game)
            .ShouldBe(BetValidationResult.Valid);
    }

    [Theory]
    [InlineData(0.001)]        // below the floor
    [InlineData(0.6)]          // above the ceiling
    [InlineData(0.0000000001)] // finer than SOL precision (9 dp)
    public void Duel_CreatorBetOutsideTheBounds_IsInvalidAmount(decimal amount)
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("banana", amount), game)
            .ShouldBe(BetValidationResult.InvalidAmount);
    }

    /// <summary>Review Focus 1 — a misconfigured duel type with no ceiling must FAIL CLOSED.</summary>
    [Fact]
    public void Duel_CreatorBet_WithNoMaximumStake_IsInvalidAmount()
    {
        var game = DuelRound(roundStake: null, max: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("banana", 0.05m), game)
            .ShouldBe(BetValidationResult.InvalidAmount);
    }

    [Fact]
    public void Duel_JoinerBetMatchingTheRoundStake_IsValid()
    {
        var game = DuelRound(roundStake: 0.125m);

        BetDomainService.ChoiceIsValidForGame(DuelBet("cosmos", 0.125m), game)
            .ShouldBe(BetValidationResult.Valid);
    }

    /// <summary>AC 7 — the joiner must match the room's stake EXACTLY, in both directions.</summary>
    [Theory]
    [InlineData(0.124)]
    [InlineData(0.126)]
    public void Duel_JoinerBetNotMatchingTheRoundStake_IsInvalidAmount(decimal amount)
    {
        var game = DuelRound(roundStake: 0.125m);

        BetDomainService.ChoiceIsValidForGame(DuelBet("cosmos", amount), game)
            .ShouldBe(BetValidationResult.InvalidAmount);
    }

    /// <summary>
    /// The round's own stake wins over the type's bounds: a room posted at the floor still refuses a
    /// joiner who bets a DIFFERENT in-bounds amount.
    /// </summary>
    [Fact]
    public void Duel_JoinerBet_InBoundsButNotTheRoundStake_IsInvalidAmount()
    {
        var game = DuelRound(roundStake: 0.002m);

        BetDomainService.ChoiceIsValidForGame(DuelBet("cosmos", 0.5m), game)
            .ShouldBe(BetValidationResult.InvalidAmount);
    }

    [Fact]
    public void Duel_FreeBet_MustBeZero()
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("banana", 0m), game, isFreeBet: true)
            .ShouldBe(BetValidationResult.Valid);
        BetDomainService.ChoiceIsValidForGame(DuelBet("banana", 0.05m), game, isFreeBet: true)
            .ShouldBe(BetValidationResult.InvalidAmount);
    }

    /// <summary>A free bet on a round that already has a stake is still just "amount must be 0".</summary>
    [Fact]
    public void Duel_FreeJoinerBet_IsValidAgainstAStakedRoom()
    {
        var game = DuelRound(roundStake: 0.125m);

        BetDomainService.ChoiceIsValidForGame(DuelBet("cosmos", 0m), game, isFreeBet: true)
            .ShouldBe(BetValidationResult.Valid);
    }

    [Theory]
    [InlineData("banan")]     // 5
    [InlineData("bananas")]   // 7
    [InlineData("")]
    public void Duel_WordOfTheWrongLength_IsWrongChoiceLength(string choice)
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet(choice, 0.05m), game)
            .ShouldBe(BetValidationResult.WrongChoiceLength);
    }

    [Fact]
    public void Duel_WordOffTheAlphabet_IsInvalidCharacters()
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("ban4na", 0.05m), game)
            .ShouldBe(BetValidationResult.InvalidCharacters);
    }

    /// <summary>
    /// The vague-result ORDER is part of the contract (constraints.md §6): length → amount → choice →
    /// characters. A too-short word with a bad amount reports the LENGTH, exactly as the pari-mutuel
    /// path does.
    /// </summary>
    [Fact]
    public void Duel_LengthIsCheckedBeforeAmount()
    {
        var game = DuelRound(roundStake: null);

        BetDomainService.ChoiceIsValidForGame(DuelBet("ban", 999m), game)
            .ShouldBe(BetValidationResult.WrongChoiceLength);
    }

    /// <summary>
    /// AC 3 / §1.2 — the pot is 2 stakes and the cut applies ONLY to profit above the winner's own
    /// stake: stake × (2 − rate) = 1.98 × stake at 2%. NOT 2 × stake × (1 − rate) = 1.96 × stake, which
    /// is what the design artifact computes. Payout maths is REUSED UNCHANGED (decision 4).
    /// </summary>
    [Fact]
    public void Duel_Payout_IsStakeTimesTwoMinusTheRate()
    {
        const decimal stake = 0.5m;
        var settings = new TokenSettings { CommissionPercentage = 2m, Decimals = 9 };

        var winner = new Bet(BetStatus.NeedsRewarding, stake, "banana", "seed");
        var loser = new Bet(BetStatus.Made, stake, "cosmos", "seed");

        var parameters = new GameParameters(GameCharacterType.Letters, 6, true) { IsDuel = true };
        var game = new Game(parameters, new List<Bet> { winner, loser }) { DuelKind = DuelKind.House, Stake = stake };

        var result = new BetDomainService(game, settings).CalculateRewardForBet(winner);

        result.IsSuccess.ShouldBeTrue();
        result.Amount.ShouldBe(stake * (2m - 0.02m));   // 0.99
        result.Amount.ShouldNotBe(2m * stake * (1m - 0.02m)); // 0.98 — the artifact's wrong formula
    }
```

Reuse the file's existing helpers/usings where they already provide `TokenSettings`, `BetStatus`, etc. If the existing class already defines a helper with one of these names, suffix the new one with `Duel` rather than renaming the old.

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~BetDomainServiceTests"`
Expected: FAIL — the duel cases return `InvalidAmount` for every valid creator stake (today's rule demands `Amount == RequiredAmount`).

- [ ] **Step 3: Add the duel branch**

In `ChoiceIsValidForGame`, replace the two amount checks:

```csharp
            if (!isFreeBet && bet.Amount != game.Parameters.RequiredAmount)
                return BetValidationResult.InvalidAmount;

            if (isFreeBet && bet.Amount != 0)
                return BetValidationResult.InvalidAmount;
```

with:

```csharp
            // A free bet moved no money, whatever the type: the amount must be 0. Checked first so the
            // duel branch below never has to repeat it.
            if (isFreeBet)
            {
                if (bet.Amount != 0)
                    return BetValidationResult.InvalidAmount;
            }
            // #184 Monkey Duel: the stake is per ROUND, not per type.
            //  - the round already has a stake (a joiner, or the house): the amount must match it EXACTLY;
            //  - the round has none yet (the creator): the amount must sit within the type's floor/ceiling,
            //    at SOL precision.
            //
            // ⚠️ FAIL CLOSED on a null MaximumStake. It is REQUIRED when IsDuel is true, so a null one is a
            // misconfigured type — refusing is right, and "no ceiling means anything goes" would be a money
            // bug. Same discipline as HouseEdgeChoiceIsValid.
            else if (parameters.IsDuel)
            {
                if (game.Stake is decimal roundStake)
                {
                    if (bet.Amount != roundStake)
                        return BetValidationResult.InvalidAmount;
                }
                else if (bet.Amount < parameters.RequiredAmount
                      || parameters.MaximumStake is null
                      || bet.Amount > parameters.MaximumStake.Value
                      || decimal.Round(bet.Amount, NativeSol.Decimals) != bet.Amount)
                {
                    return BetValidationResult.InvalidAmount;
                }
            }
            else if (bet.Amount != game.Parameters.RequiredAmount)
            {
                return BetValidationResult.InvalidAmount;
            }
```

Everything else in the method is already correct for a duel **because the type is free-text**: `ChoiceRequiredLength = 6` is enforced above, `PresetChoices` is empty so the preset-list check is skipped, and the alphabet check applies. `CalculateRewardForBet` is **not touched**.

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~BetDomainServiceTests"`
Expected: PASS, including every pre-existing pari-mutuel and house-edge case.

- [ ] **Step 5: Run the full suite**

Run: `scripts\run_tests.bat`
Expected: PASS. `CommissionRateTests` in particular must stay green.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs src/SpaceStroker.Tests/UnitTests/Domain/Bets/BetDomainServiceTests.cs
git commit -m "Validate a duel bet against the round's own stake instead of the type's price."
git push origin feature/monkey-duel
```

---

## Task 4: `DuelHouseCoverage` — the HouseSeed coverage rule

**Files:**
- Create: `src/SpaceStroker.Domain/Games/Services/DuelHouseCoverage.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseCoverageTests.cs`

**Interfaces:**
- Consumes: `HouseEdgeRules.TreasuryFeeReservePerTransfer` (= `0.001m`, existing), `HouseEdgeCoverage.UnsettledStatuses` (existing, reused for the pending query in Task 9).
- Produces:
  - `sealed record DuelPendingHouseStake(Guid GameId, decimal Stake)`
  - `sealed record DuelCoverageDecision(bool IsCoverable, decimal? HouseSeedBalance, decimal MaxStake, decimal Spendable, decimal PendingTotal)`
  - `static DuelCoverageDecision DuelHouseCoverage.Evaluate(decimal? houseSeedBalance, decimal minimumSolBalance, decimal typeMaximumStake, IEnumerable<DuelPendingHouseStake> pending, decimal requestedStake)`

⚠️ This is **NOT** `HouseEdgeCoverage` (§3.1 S3). That rule prices *treasury* exposure at `stake × multiplier`; a duel treasury receives two stakes and pays out at most `1.98 × stake`, so it carries no exposure. The only real constraint is the **HouseSeed** wallet funding the second stake plus its fee floor.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseCoverageTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.Services;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games;

/// <summary>
/// #184 §6.5 — whether the HouseSeed wallet can fund one more house-duel counterparty stake, and what
/// ceiling the SPA may quote.
///
///     spendable = balance − minimumSolBalance − feeReserve × (pending + 1)
///     coverable(stake) ⇔ balance is not null AND spendable ≥ stake + Σ(pending stakes)
///     maxStake         = clamp(spendable − Σ(pending stakes), 0, typeMaximumStake)
///
/// ⚠️ NOT HouseEdgeCoverage (§3.1 S3): that prices TREASURY exposure at stake × multiplier. A duel
/// treasury takes two stakes and pays at most 1.98 × stake, so it carries no exposure.
/// </summary>
public class DuelHouseCoverageTests
{
    private const decimal Floor = 0.02m;      // HouseSeedSettings.MinimumSolBalance
    private const decimal Fee = 0.001m;       // HouseEdgeRules.TreasuryFeeReservePerTransfer
    private const decimal TypeMax = 0.5m;

    private static DuelCoverageDecision Evaluate(decimal? balance, decimal requested,
        params decimal[] pending)
        => DuelHouseCoverage.Evaluate(
            balance, Floor, TypeMax,
            pending.Select(x => new DuelPendingHouseStake(Guid.NewGuid(), x)),
            requested);

    [Fact]
    public void ComfortableBalance_CoversTheRequestAndQuotesTheTypeCeiling()
    {
        var decision = Evaluate(balance: 10m, requested: 0.5m);

        decision.IsCoverable.ShouldBeTrue();
        decision.MaxStake.ShouldBe(TypeMax);   // clamped by the type, not by the wallet
        decision.HouseSeedBalance.ShouldBe(10m);
    }

    [Fact]
    public void BalanceCoveringTheMinimumButNotTheRequest_RefusesAndClampsTheCeiling()
    {
        // spendable = 0.1 − 0.02 − 0.001 = 0.079
        var decision = Evaluate(balance: 0.1m, requested: 0.5m);

        decision.IsCoverable.ShouldBeFalse();
        decision.MaxStake.ShouldBe(0.079m);
        decision.MaxStake.ShouldBeLessThan(TypeMax);
    }

    [Fact]
    public void AStakeAtTheClampedCeiling_IsCoverable()
    {
        var ceiling = Evaluate(balance: 0.1m, requested: 0.5m).MaxStake;

        Evaluate(balance: 0.1m, requested: ceiling).IsCoverable.ShouldBeTrue();
    }

    [Fact]
    public void BalanceBelowTheFloor_IsNotCoverableAndQuotesZero()
    {
        var decision = Evaluate(balance: 0.01m, requested: 0.002m);

        decision.IsCoverable.ShouldBeFalse();
        decision.MaxStake.ShouldBe(0m);
    }

    /// <summary>Review Focus 3 — a failed RPC read is UNKNOWN, and unknown fails closed.</summary>
    [Fact]
    public void ANullBalance_IsNotCoverableAndQuotesZero()
    {
        var decision = Evaluate(balance: null, requested: 0.002m);

        decision.IsCoverable.ShouldBeFalse();
        decision.MaxStake.ShouldBe(0m);
        decision.HouseSeedBalance.ShouldBeNull();
    }

    [Fact]
    public void PendingHouseStakes_AreSubtractedFromTheCeiling()
    {
        // Two rounds already waiting on a house bet: 0.2 committed, 3 fee reserves (2 pending + this one).
        // spendable = 1 − 0.02 − 0.003 = 0.977; ceiling = 0.977 − 0.2 = 0.777 → clamped to 0.5.
        var withPending = Evaluate(balance: 1m, requested: 0.5m, 0.1m, 0.1m);
        withPending.IsCoverable.ShouldBeTrue();
        withPending.PendingTotal.ShouldBe(0.2m);

        // Squeeze it: 0.3 balance, 0.2 pending. spendable = 0.3 − 0.02 − 0.003 = 0.277;
        // ceiling = 0.277 − 0.2 = 0.077, so 0.1 is refused where it would pass with no pending.
        var tight = Evaluate(balance: 0.3m, requested: 0.1m, 0.1m, 0.1m);
        tight.IsCoverable.ShouldBeFalse();
        tight.MaxStake.ShouldBe(0.077m);
        Evaluate(balance: 0.3m, requested: 0.1m).IsCoverable.ShouldBeTrue();
    }

    [Fact]
    public void TheIncomingPlayerStakeIsNeverAddedToTheBalance()
    {
        // It goes to the TREASURY, not to HouseSeed, so it can never fund the house's own side.
        // A wallet that cannot cover 0.5 does not become able to because the player is paying 0.5.
        Evaluate(balance: 0.1m, requested: 0.5m).IsCoverable.ShouldBeFalse();
    }

    [Fact]
    public void MaxStake_IsNeverNegative()
    {
        Evaluate(balance: 0m, requested: 0.002m).MaxStake.ShouldBe(0m);
    }

    [Fact]
    public void AZeroStake_IsAlwaysCoverable_ItMovesNoMoney()
    {
        // The free-bet path (§3.1 S7): no transfer, no balance requirement.
        Evaluate(balance: 0m, requested: 0m).IsCoverable.ShouldBeTrue();
        Evaluate(balance: null, requested: 0m).IsCoverable.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseCoverageTests"`
Expected: FAIL — `DuelHouseCoverage` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SpaceStroker.Domain/Games/Services/DuelHouseCoverage.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceStroker.Domain.Games.Services
{
    /// <summary>A House duel round that has a stake but no house counterparty bet yet.</summary>
    public sealed record DuelPendingHouseStake(Guid GameId, decimal Stake);

    public sealed record DuelCoverageDecision(
        bool IsCoverable, decimal? HouseSeedBalance, decimal MaxStake, decimal Spendable, decimal PendingTotal);

    /// <summary>
    /// The HouseSeed coverage rule for Monkey Duel (#184 §6.5):
    ///
    ///     spendable = balance − minimumSolBalance − feeReserve × (pending + 1)
    ///     coverable(stake) ⇔ balance is not null AND spendable ≥ stake + Σ(pending stakes)
    ///     maxStake         = clamp(spendable − Σ(pending stakes), 0, typeMaximumStake)
    ///
    /// ⚠️ ONE function, called by BOTH the availability pre-check and the RegisterDuelBet backstop, so the
    /// app can never tell a player "go ahead" on a rule the server then refuses on — the same discipline
    /// HouseEdgeCoverage documents.
    ///
    /// ⚠️ NOT HouseEdgeCoverage, and not the treasury (§3.1 S3). That rule prices TREASURY exposure at
    /// stake × multiplier. A duel treasury RECEIVES two stakes and pays out at most 1.98 × stake, so it
    /// carries no exposure; RewardGameConsumer correctly runs its treasury pre-check for IsHouseEdge only.
    /// The only real constraint is the HouseSeed wallet funding the second stake plus its fee floor.
    ///
    /// ⚠️ The incoming PLAYER stake is deliberately NOT added to the balance: it goes to the treasury, not
    /// to HouseSeed, so it can never fund the house's own side.
    ///
    /// ⚠️ MinimumSolBalance is SUBTRACTED, not compared against: with native SOL the stake IS lamports out
    /// of the same balance that pays the fee, so a wallet holding exactly the floor would otherwise pass
    /// and then spend the floor on the bet. See specs/sol-settlement.md §6.6.
    ///
    /// ⚠️ A null balance read is UNKNOWN, and unknown is NOT coverable: fail closed.
    /// </summary>
    public static class DuelHouseCoverage
    {
        public static DuelCoverageDecision Evaluate(
            decimal? houseSeedBalance,
            decimal minimumSolBalance,
            decimal typeMaximumStake,
            IEnumerable<DuelPendingHouseStake> pending,
            decimal requestedStake)
        {
            var pendingStakes = pending?.ToList() ?? [];
            var pendingTotal = pendingStakes.Sum(x => x.Stake);

            if (houseSeedBalance is not decimal balance)
                return new DuelCoverageDecision(
                    // A free bet moves no money and needs no wallet, even when the read failed.
                    IsCoverable: requestedStake <= 0m,
                    HouseSeedBalance: null, MaxStake: 0m, Spendable: 0m, PendingTotal: pendingTotal);

            // One fee reserve per stake the house still owes, plus one for this request's own transfer.
            var feeReserve = HouseEdgeRules.TreasuryFeeReservePerTransfer * (pendingStakes.Count + 1);
            var spendable = balance - minimumSolBalance - feeReserve;

            var headroom = spendable - pendingTotal;
            var maxStake = Math.Clamp(headroom, 0m, typeMaximumStake);

            // A 0-amount (free) bet places a 0-amount house bet with no transfer at all (§3.1 S7).
            var isCoverable = requestedStake <= 0m || headroom >= requestedStake;

            return new DuelCoverageDecision(isCoverable, balance, maxStake, spendable, pendingTotal);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseCoverageTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain/Games/Services/DuelHouseCoverage.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/DuelHouseCoverageTests.cs
git commit -m "Add the HouseSeed coverage rule that clamps a duel's quoted ceiling."
git push origin feature/monkey-duel
```

---

## Task 5: The duel start countdown

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (`SecondsUntilGameStarts`)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json`
- Test: `src/SpaceStroker.Tests/UnitTests/Configs/GameSettingsTests.cs` (create if absent)

**Interfaces:**
- Consumes: `GameParameters.IsDuel` (Task 1).
- Produces: `GameSettings.DuelTimeUntilGameStarts : int?`; `GameSettings.SecondsUntilGameStarts(bool isHouseEdge, bool isDuel) : int`. ⚠️ **Signature change** — update the one caller, `GameService.SecondsUntilGameStarts(Game)`.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Configs/GameSettingsTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Configs.Entities;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Configs;

/// <summary>
/// The start countdown per round kind. Only a MISSING (null) value falls back to
/// TimeUntilGameStarts — a configured 0 means "start immediately", which is what Hyper Roll solo
/// rounds use and what a duel's VS splash budget is measured against.
/// </summary>
public class GameSettingsTests
{
    private static GameSettings Settings(int? houseEdge, int? duel) => new()
    {
        TimeUntilGameStarts = 10,
        HouseEdgeTimeUntilGameStarts = houseEdge,
        DuelTimeUntilGameStarts = duel,
    };

    [Fact]
    public void ADuelRound_UsesTheDuelCountdown()
    {
        Settings(houseEdge: 0, duel: 3)
            .SecondsUntilGameStarts(isHouseEdge: false, isDuel: true).ShouldBe(3);
    }

    [Fact]
    public void ADuelRound_WithAZeroCountdown_StartsImmediately()
    {
        Settings(houseEdge: 0, duel: 0)
            .SecondsUntilGameStarts(isHouseEdge: false, isDuel: true).ShouldBe(0);
    }

    [Fact]
    public void ADuelRound_WithNoConfiguredValue_FallsBackToTheOrdinaryCountdown()
    {
        Settings(houseEdge: 0, duel: null)
            .SecondsUntilGameStarts(isHouseEdge: false, isDuel: true).ShouldBe(10);
    }

    [Fact]
    public void AHouseEdgeRound_IsUnaffectedByTheDuelSetting()
    {
        Settings(houseEdge: 0, duel: 3)
            .SecondsUntilGameStarts(isHouseEdge: true, isDuel: false).ShouldBe(0);
    }

    [Fact]
    public void AnOrdinaryRound_KeepsTimeUntilGameStarts()
    {
        Settings(houseEdge: 0, duel: 3)
            .SecondsUntilGameStarts(isHouseEdge: false, isDuel: false).ShouldBe(10);
    }

    /// <summary>A negative value is "not configured" too — same semantics as the house-edge branch.</summary>
    [Fact]
    public void ANegativeDuelCountdown_FallsBack()
    {
        Settings(houseEdge: 0, duel: -1)
            .SecondsUntilGameStarts(isHouseEdge: false, isDuel: true).ShouldBe(10);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~GameSettingsTests"`
Expected: FAIL — `DuelTimeUntilGameStarts` does not exist and `SecondsUntilGameStarts` takes one argument.

- [ ] **Step 3: Add the setting and widen the method**

In `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs`, after `HouseEdgeTimeUntilGameStarts`:

```csharp
        /// <summary>
        /// Start delay, in seconds, for a duel round (#184) — the VS splash beat after both bets are in.
        /// Null (missing from config) falls back to TimeUntilGameStarts, so an older config keeps working.
        /// </summary>
        public int? DuelTimeUntilGameStarts { get; set; }
```

Replace `SecondsUntilGameStarts`:

```csharp
        /// <summary>
        /// The start countdown for a round of the given kind. A house-edge round uses
        /// HouseEdgeTimeUntilGameStarts and a duel round DuelTimeUntilGameStarts whenever it is set —
        /// including 0, which means the round starts immediately (Hyper Roll solo rounds). Only a MISSING
        /// (null) value — a config that predates the setting — falls back to TimeUntilGameStarts;
        /// everything else (a legacy round type) also keeps TimeUntilGameStarts.
        ///
        /// ⚠️ A round is never both: IsHouseEdge stays false on a duel type (specs/monkey-duel.md §3.1 S2).
        /// The house-edge branch is checked first anyway, so the two can never disagree.
        /// </summary>
        public int SecondsUntilGameStarts(bool isHouseEdge, bool isDuel)
        {
            if (isHouseEdge && HouseEdgeTimeUntilGameStarts is >= 0)
                return HouseEdgeTimeUntilGameStarts.Value;

            if (isDuel && DuelTimeUntilGameStarts is >= 0)
                return DuelTimeUntilGameStarts.Value;

            return TimeUntilGameStarts;
        }
```

- [ ] **Step 4: Update the one caller**

In `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs`, replace `SecondsUntilGameStarts(Game game)`:

```csharp
        /// <summary>
        /// The countdown for this round: Hyper Roll (IsHouseEdge) starts HouseEdgeTimeUntilGameStarts after
        /// its first bet, a duel (#184) starts DuelTimeUntilGameStarts after BOTH bets are in, and every
        /// other type keeps TimeUntilGameStarts. When the caller did not load Parameters, both flags are
        /// read from the database rather than guessed — guessing "not house-edge" would silently hand Hyper
        /// Roll the long countdown, and guessing "not a duel" would put a 10s wait in front of the VS splash.
        /// </summary>
        private async Task<int> SecondsUntilGameStarts(Game game)
        {
            var isHouseEdge = game.Parameters?.IsHouseEdge;
            var isDuel = game.Parameters?.IsDuel;

            if (isHouseEdge is null || isDuel is null)
            {
                // ONE query for both flags: two separate reads would double the cost on the start path.
                var flags = await _context.GameParameters
                    .AsNoTracking()
                    .Where(x => x.Id == game.ParametersId)
                    .Select(x => new { x.IsHouseEdge, x.IsDuel })
                    .FirstOrDefaultAsync();

                isHouseEdge ??= flags?.IsHouseEdge ?? false;
                isDuel ??= flags?.IsDuel ?? false;
            }

            return _settings.Games.SecondsUntilGameStarts(isHouseEdge.Value, isDuel.Value);
        }
```

- [ ] **Step 5: Add the config value**

In `src/SpaceStroker.Common/spacestrokersettings.json`, in `GeneralSettings:Games`, after `"HouseEdgeTimeUntilGameStarts": 0,`:

```jsonc
            "DuelTimeUntilGameStarts": 3,
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS, including every existing test that calls `SecondsUntilGameStarts` (fix any compile break by passing `isDuel: false`).

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs src/SpaceStroker.Infrastructure/Services/Games/GameService.cs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests/UnitTests/Configs/GameSettingsTests.cs
git commit -m "Give a duel round its own start countdown."
git push origin feature/monkey-duel
```

---

## Task 6: Config — the seeded (gated) duel type and its pool reconciler

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` (`DefaultGameSettings` + new `DuelSettings`)
- Create: `src/SpaceStroker.Infrastructure/Services/Games/DuelWordPoolReconciler.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (`ToGameParameters`, reconciler call)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (the seeded type)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/DuelWordPoolReconcilerTests.cs`

**Interfaces:**
- Consumes: `DuelWordPool`, `GameParameters.IsDuel` (Task 1).
- Produces: `DefaultGameSettings.Duel : DuelSettings?`; `DuelSettings { decimal MaximumStake, List<string> Words }`; `static int DuelWordPoolReconciler.Reconcile(SpaceStrokerDbContext, IReadOnlyList<DefaultGameSettings>)`.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Services/DuelWordPoolReconcilerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Services;

/// <summary>
/// #184 §5.3 — seeding is create-only by name, so the pool chosen AFTER the Monte Carlo would never
/// reach an environment that already has the type row. This reconciler is how it does. Modelled on
/// HouseEdgeReconciler, and a class rather than an inline loop so the integration suite can drive it.
/// </summary>
public class DuelWordPoolReconcilerTests
{
    private static SpaceStrokerDbContext NewContext()
        => new(new DbContextOptionsBuilder<SpaceStrokerDbContext>()
            .UseInMemoryDatabase($"duel-reconcile-{Guid.NewGuid():N}").Options);

    private static Guid SeedDuelType(SpaceStrokerDbContext context, params string[] words)
    {
        var parameters = new GameParameters(GameCharacterType.Letters, 6, true)
        {
            Name = "Monkey Duel", Description = "d", RequiredAmount = 0.002m,
            MinimumNumberOfPlayers = 2, Active = true, IsDuel = true, MaximumStake = 0.5m,
            DuelWordPool = words.Select(x => new DuelWordPool(x)).ToList(),
        };
        context.GameParameters.Add(parameters);
        context.SaveChanges();
        return parameters.Id;
    }

    private static List<DefaultGameSettings> Config(params string[] words) =>
    [
        new()
        {
            Name = "Monkey Duel", Description = "d", AllowedCharacters = GameCharacterType.Letters,
            ChoiceRequiredLength = 6, RequiredAmount = 0.002m, MinimumNumberOfPlayers = 2,
            AcceptDuplicatedCharacters = true, ComingSoon = true,
            Duel = new DuelSettings { MaximumStake = 0.5m, Words = words.ToList() },
        }
    ];

    [Fact]
    public void InsertsWordsThatAreInConfigButNotInTheDatabase()
    {
        using var context = NewContext();
        var parametersId = SeedDuelType(context, "banana");

        var changes = DuelWordPoolReconciler.Reconcile(context, Config("banana", "cosmos", "rocket"));

        changes.ShouldBe(2);
        context.DuelWordPool.Where(x => x.ParametersId == parametersId && x.Active)
            .Select(x => x.Word).OrderBy(x => x)
            .ShouldBe(["banana", "cosmos", "rocket"]);
    }

    [Fact]
    public void DeactivatesWordsThatLeftConfig_AndNeverDeletesThem()
    {
        // A settled round's DuelPoolSnapshot must stay reproducible, so a retired word is deactivated,
        // never removed.
        using var context = NewContext();
        var parametersId = SeedDuelType(context, "banana", "cosmos");

        DuelWordPoolReconciler.Reconcile(context, Config("banana"));

        var rows = context.DuelWordPool.AsNoTracking()
            .Where(x => x.ParametersId == parametersId).OrderBy(x => x.Word).ToList();
        rows.Count.ShouldBe(2);
        rows.Single(x => x.Word == "banana").Active.ShouldBeTrue();
        rows.Single(x => x.Word == "cosmos").Active.ShouldBeFalse();
    }

    [Fact]
    public void ReactivatesAWordThatComesBack()
    {
        using var context = NewContext();
        var parametersId = SeedDuelType(context, "banana", "cosmos");
        DuelWordPoolReconciler.Reconcile(context, Config("banana"));

        var changes = DuelWordPoolReconciler.Reconcile(context, Config("banana", "cosmos"));

        changes.ShouldBe(1);
        context.DuelWordPool.AsNoTracking()
            .Single(x => x.ParametersId == parametersId && x.Word == "cosmos").Active.ShouldBeTrue();
    }

    [Fact]
    public void IsIdempotent()
    {
        using var context = NewContext();
        SeedDuelType(context, "banana", "cosmos");

        DuelWordPoolReconciler.Reconcile(context, Config("banana", "cosmos"));
        DuelWordPoolReconciler.Reconcile(context, Config("banana", "cosmos")).ShouldBe(0);
    }

    [Fact]
    public void AnEmptyConfiguredPool_DeactivatesEverything_WhichIsHowTheTypeShips()
    {
        // Decision 10: the type ships ComingSoon with an EMPTY Words list until the Monte Carlo has run.
        using var context = NewContext();
        var parametersId = SeedDuelType(context, "banana", "cosmos");

        DuelWordPoolReconciler.Reconcile(context, Config());

        context.DuelWordPool.AsNoTracking()
            .Count(x => x.ParametersId == parametersId && x.Active).ShouldBe(0);
    }

    [Fact]
    public void SkipsATypeThatHasNotBeenSeededYet()
    {
        using var context = NewContext();

        Should.NotThrow(() => DuelWordPoolReconciler.Reconcile(context, Config("banana")));
        context.DuelWordPool.Count().ShouldBe(0);
    }

    [Fact]
    public void IgnoresNonDuelConfigEntries()
    {
        using var context = NewContext();
        SeedDuelType(context, "banana");

        var defaults = new List<DefaultGameSettings>
        {
            new() { Name = "Cosmic Sprint", Duel = null },
        };

        DuelWordPoolReconciler.Reconcile(context, defaults).ShouldBe(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelWordPoolReconcilerTests"`
Expected: FAIL — `DuelSettings`, `DefaultGameSettings.Duel` and `DuelWordPoolReconciler` do not exist.

- [ ] **Step 3: Add the config shape**

In `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs`, in `DefaultGameSettings` after the `HouseEdge` property:

```csharp
        // Non-null => a 1v1 duel type (#184 Monkey Duel). PresetChoices must stay EMPTY: the house word
        // pool is Duel.Words and lives in its own DuelWordPool table (specs/monkey-duel.md §3.1 S1).
        // RequiredAmount is the minimum stake and Duel.MaximumStake the maximum.
        public DuelSettings? Duel { get; set; }
```

and a new class beside `HouseEdgeSettings`:

```csharp
    public class DuelSettings
    {
        /// <summary>The per-round stake ceiling. REQUIRED for a duel type — BetDomainService fails closed without it.</summary>
        public decimal MaximumStake { get; set; }

        /// <summary>
        /// The curated house word pool.
        ///
        /// ⚠️ SHIPS EMPTY (decision 10). Its size is an OUTPUT of the ProductionGames Monte Carlo, not a
        /// guess — do not populate this before DuelEdgeTests has shown the house edge survives the best
        /// player counter-word. The type ships ComingSoon: true alongside it.
        /// </summary>
        public List<string> Words { get; set; } = new();
    }
```

- [ ] **Step 4: Write the reconciler**

`src/SpaceStroker.Infrastructure/Services/Games/DuelWordPoolReconciler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Infrastructure.Data.Context;

namespace SpaceStroker.Infrastructure.Services.Games
{
    /// <summary>
    /// The name-matched startup self-heal for a duel type's HOUSE WORD POOL (#184 §5.3). Seeding is
    /// create-only by name, so without this the pool chosen after the Monte Carlo would never reach a
    /// database that already has the type row — which is precisely the situation this feature ships in
    /// (the type is seeded with an EMPTY pool and ComingSoon: true).
    ///
    /// ⚠️ A word that leaves config is DEACTIVATED, never deleted. Game.DuelPoolSnapshot names the words a
    /// settled round could have drawn from, and AC 13 requires that draw to stay reproducible — deleting a
    /// row would not change the snapshot, but it would make the pool's own history unreadable.
    ///
    /// ⚠️ It NEVER touches RequiredAmount or MaximumStake, for the reason HouseEdgeReconciler documents:
    /// changing a bet bound while a stale SPA quotes the old one refuses bets after their SOL moved.
    ///
    /// A class, not an inline loop in Consumers/Program.cs, so the integration suite can drive it
    /// (ExecuteUpdate is a no-op on the in-memory provider) — the ComingSoonReconciler precedent.
    /// </summary>
    public static class DuelWordPoolReconciler
    {
        /// <returns>The number of rows changed or inserted.</returns>
        public static int Reconcile(SpaceStrokerDbContext context, IReadOnlyList<DefaultGameSettings> defaults)
        {
            var changes = 0;

            foreach (var config in defaults.Where(x => x.Duel != null))
            {
                var parametersId = context.GameParameters
                    .AsNoTracking()
                    .Where(x => x.Name == config.Name)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefault();

                if (parametersId is null)
                    continue; // not seeded yet — the create pass builds it correctly

                var configured = config.Duel!.Words
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToHashSet(StringComparer.Ordinal);

                var existing = context.DuelWordPool
                    .AsNoTracking()
                    .Where(x => x.ParametersId == parametersId)
                    .ToList();

                foreach (var row in existing)
                {
                    var shouldBeActive = configured.Contains(row.Word);
                    if (row.Active == shouldBeActive)
                        continue;

                    var rowId = row.Id;
                    changes += context.DuelWordPool
                        .Where(x => x.Id == rowId)
                        .ExecuteUpdate(setters => setters.SetProperty(x => x.Active, shouldBeActive));
                }

                var existingWords = existing.Select(x => x.Word).ToHashSet(StringComparer.Ordinal);
                var toInsert = configured
                    .Where(word => !existingWords.Contains(word))
                    .Select(word => new DuelWordPool(word) { ParametersId = parametersId.Value, Active = true })
                    .ToList();

                if (toInsert.Count > 0)
                {
                    context.DuelWordPool.AddRange(toInsert);
                    context.SaveChanges();
                    changes += toInsert.Count;
                }
            }

            return changes;
        }
    }
}
```

⚠️ `ExecuteUpdate` is a no-op on the in-memory provider used by the unit tests. Replace the two `ExecuteUpdate` calls with a tracked load-mutate-save **only if** the tests fail on it; the preferred fix is the one `HouseEdgeReconciler` uses — if the in-memory provider throws, mirror whatever `HouseEdgeReconcilerTests` does today (check that file first; if it drives the reconciler through Testcontainers only, move these assertions to the integration suite in Task 17 and keep the unit tests to the insert path).

- [ ] **Step 5: Wire `ToGameParameters` and the reconciler call**

In `src/SpaceStroker.Consumers/Program.cs`, in `ToGameParameters`, after the `config.HouseEdge` block:

```csharp
    // #184 Monkey Duel: the type is FREE-TEXT (PresetChoices stays empty — §3.1 S1); its house word pool
    // is its own table. MaximumStake is the per-round ceiling; AllowOrbit is false (decision 18) and
    // IsHouseEdge stays false (§3.1 S2).
    if (config.Duel != null)
    {
        parameters.IsDuel = true;
        parameters.MaximumStake = config.Duel.MaximumStake;
        parameters.DuelWordPool = config.Duel.Words.Select(x => new DuelWordPool(x)).ToList();
    }
```

And beside the `HouseEdgeReconciler.Reconcile` try/catch in `CreateDefaultGameParametersAsync`:

```csharp
        // #184 Monkey Duel house-word-pool self-heal. Runs BEFORE OpenNewGames, like the house-edge one,
        // and never touches a round's DuelPoolSnapshot.
        //
        // ⚠️ Log.Error, never throw: a reconcile failure must not skip the rest of this block — the cache
        // drop, referral partners, the lottery correction, ReplanOpenRounds and OpenNewGames all follow.
        try
        {
            var duelPoolChanges = DuelWordPoolReconciler.Reconcile(context, defaults);
            if (duelPoolChanges > 0)
                Log.Information("Reconciled {Count} duel word-pool change(s) from configuration.", duelPoolChanges);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not reconcile the duel word pools from configuration at startup.");
        }
```

- [ ] **Step 6: Add the seeded (gated) type**

In `src/SpaceStroker.Common/spacestrokersettings.json`, append to `GeneralSettings:Operator:DefaultGames`:

```jsonc
                {
                    "Name": "Monkey Duel",
                    "Description": "MOCK - 1v1: two words, one stream, winner takes both stakes.",
                    "AllowedCharacters": 2,
                    "ChoiceRequiredLength": 6,
                    "RequiredAmount": 0.002,
                    "MinimumNumberOfPlayers": 2,
                    "AcceptDuplicatedCharacters": true,
                    "AllowOrbit": false,
                    "ComingSoon": true,
                    "PresetChoices": [],
                    "Duel": {
                        "MaximumStake": 0.5,
                        "Words": []
                    }
                }
```

⚠️ `ComingSoon: true` and `"Words": []` are **load-bearing**, not placeholders: decision 10 forbids shipping the pool before the Monte Carlo, and a duel type with an empty pool must never take a house bet.

- [ ] **Step 7: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Domain/Configs src/SpaceStroker.Infrastructure/Services/Games/DuelWordPoolReconciler.cs src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests/UnitTests/Services/DuelWordPoolReconcilerTests.cs
git commit -m "Seed the gated Monkey Duel type and reconcile its word pool at startup."
git push origin feature/monkey-duel
```

---

## Task 7: Opening a room — `POST /games/{gameTypeId}/duels`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (+ `IGameService.cs`)
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/OpenDuelRoomRequest.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/OpenDuelRoomHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (rate-limit rule)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/OpenDuelRoomHandlerTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Api/RateLimitSettingsTests.cs` (extend)

**Interfaces:**
- Consumes: `DuelKind`, `Game.DuelKind/Stake/DuelPoolSnapshot`, `DuelWordPool` (Task 1); `IDuelHouseCoverageService` (Task 9 — **stub the interface now**, implement in Task 9).
- Produces:
  - `Task<Guid?> IGameService.OpenDuelRoom(GameParameters parameters, DuelKind kind)`
  - `record OpenDuelRoomRequest(Guid GameTypeId, OpenDuelRoomRequestBody Body) : IRequest<ActiveGameDto>`
  - `class OpenDuelRoomRequestBody { DuelKind? Kind { get; set; } }`

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Handlers/OpenDuelRoomHandlerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SpaceStroker.Common.Exceptions;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Handlers;

/// <summary>
/// #184 §6.1 — a duel round is opened by an explicit call, never at startup and never by rollover
/// (§3.1 S4). The seed is committed in the SAME SaveChanges as the row, so the commitment always
/// predates every bet and the house word cannot be ground.
/// </summary>
public class OpenDuelRoomHandlerTests
{
    private static SpaceStrokerDbContext NewContext()
        => new(new DbContextOptionsBuilder<SpaceStrokerDbContext>()
            .UseInMemoryDatabase($"open-duel-{Guid.NewGuid():N}").Options);

    private static GameParameters DuelType(bool comingSoon = false, params string[] pool)
        => new(GameCharacterType.Letters, 6, true)
        {
            Name = "Monkey Duel", Description = "d", RequiredAmount = 0.002m, MaximumStake = 0.5m,
            MinimumNumberOfPlayers = 2, Active = true, IsDuel = true, ComingSoon = comingSoon,
            DuelWordPool = pool.Select(x => new DuelWordPool(x)).ToList(),
        };

    [Fact]
    public async Task OpenDuelRoom_House_CommitsTheSeedAndSnapshotsTheActivePool()
    {
        using var context = NewContext();
        var parameters = DuelType(pool: ["rocket", "banana", "cosmos"]);
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        var service = GameServiceTestFactory.Create(context);
        var gameId = await service.OpenDuelRoom(parameters, DuelKind.House);

        gameId.ShouldNotBeNull();
        var round = context.Games.AsNoTracking().Single(x => x.Id == gameId!.Value);
        round.Status.ShouldBe(GameStatus.WaitingBets);
        round.DuelKind.ShouldBe(DuelKind.House);
        round.Stake.ShouldBeNull();
        round.OwnerPlayerId.ShouldBeNull();
        round.ClaimedByPlayerId.ShouldBeNull();
        round.BoardExpiresAt.ShouldBeNull();   // decision 15: no auto-expiry
        round.ServerSeed.ShouldNotBeNullOrWhiteSpace();
        round.ServerSeedHash.ShouldNotBeNullOrWhiteSpace();
        // Ordinal ascending, comma-joined — so an offline verifier reproduces the draw's input order.
        round.DuelPoolSnapshot.ShouldBe("banana,cosmos,rocket");
    }

    [Fact]
    public async Task OpenDuelRoom_House_SnapshotsOnlyTheACTIVEPool()
    {
        using var context = NewContext();
        var parameters = DuelType();
        parameters.DuelWordPool = [new("banana"), new("cosmos") { Active = false }, new("rocket")];
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        var gameId = await GameServiceTestFactory.Create(context).OpenDuelRoom(parameters, DuelKind.House);

        context.Games.AsNoTracking().Single(x => x.Id == gameId!.Value)
            .DuelPoolSnapshot.ShouldBe("banana,rocket");
    }

    /// <summary>Review Focus 2 — decision 10's LAST guard. The type ships with an empty pool.</summary>
    [Fact]
    public async Task OpenDuelRoom_House_WithAnEmptyPool_ReturnsNull()
    {
        using var context = NewContext();
        var parameters = DuelType();
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        (await GameServiceTestFactory.Create(context).OpenDuelRoom(parameters, DuelKind.House))
            .ShouldBeNull();
        context.Games.Count().ShouldBe(0);
    }

    [Fact]
    public async Task OpenDuelRoom_OpenRoom_NeedsNoPoolAndSnapshotsNothing()
    {
        // A PVP room never draws a house word, so the empty pool is irrelevant to it — which is what
        // keeps the OPEN DUELS board playable while the house tab is still gated.
        using var context = NewContext();
        var parameters = DuelType();
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        var gameId = await GameServiceTestFactory.Create(context).OpenDuelRoom(parameters, DuelKind.OpenRoom);

        gameId.ShouldNotBeNull();
        var round = context.Games.AsNoTracking().Single(x => x.Id == gameId!.Value);
        round.DuelKind.ShouldBe(DuelKind.OpenRoom);
        round.DuelPoolSnapshot.ShouldBeNull();
        round.ServerSeed.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenDuelRoom_RefusesANonDuelType()
    {
        using var context = NewContext();
        var parameters = DuelType();
        parameters.IsDuel = false;
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        (await GameServiceTestFactory.Create(context).OpenDuelRoom(parameters, DuelKind.OpenRoom))
            .ShouldBeNull();
    }

    [Fact]
    public async Task OpenDuelRoom_PublishesNoGameOpenedEvent()
    {
        // ⚠️ GameOpenedEvent drives the house SEEDER and the Orbit planner, and a duel wants neither
        // (decisions 17 + 18). A seeded house bet on a duel IS the second player, so every PVP room
        // would be taken the instant it opened.
        using var context = NewContext();
        var parameters = DuelType(pool: ["banana", "cosmos"]);
        context.GameParameters.Add(parameters);
        await context.SaveChangesAsync();

        var factory = GameServiceTestFactory.CreateWithSpies(context);
        await factory.Service.OpenDuelRoom(parameters, DuelKind.House);

        factory.PublishedEvents.ShouldBeEmpty();
        factory.HubBroadcasts.ShouldBeEmpty();
    }
}
```

⚠️ `GameServiceTestFactory` is a **new** test helper — the existing `GameService` unit tests build the service by hand. Before writing it, open the existing `src/SpaceStroker.Tests/UnitTests/Services/` tests that construct `GameService` and reuse their construction verbatim; add `GameServiceTestFactory` to `src/SpaceStroker.Tests/UnitTests/Services/GameServiceTestFactory.cs` as a thin wrapper exposing `Create(context)`, `CreateWithSpies(context)` (returning `{ Service, PublishedEvents, HubBroadcasts }` backed by Moq captures on `IMessageBus.PublishEventAsync` and `IHubContext<ActiveGamesHub>`), so the six tests above read as written.

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~OpenDuelRoomHandlerTests"`
Expected: FAIL — `OpenDuelRoom` does not exist.

- [ ] **Step 3: Implement `GameService.OpenDuelRoom`**

Add to `IGameService`:

```csharp
        /// <summary>
        /// #184 §6.1 — opens an unbet duel round of the given kind. Returns null when the type is not a
        /// duel, or when a House round is asked for while the active word pool is empty (decision 10's
        /// last guard). The caller answers vaguely.
        /// </summary>
        Task<Guid?> OpenDuelRoom(GameParameters parameters, DuelKind kind);
```

Implement in `GameService`, next to `OpenSoloBoard`:

```csharp
        public async Task<Guid?> OpenDuelRoom(GameParameters parameters, DuelKind kind)
        {
            if (parameters?.IsDuel != true)
                return null;

            // ⚠️ Read the pool from the DATABASE, never from the caller's object: callers can hand in a
            // GameParameters rebuilt from the ActiveParameters cache, and mid-deploy an older API revision
            // can rewrite that blob without the duel fields. Same discipline as DrawHouseEdgeBoard.
            string? poolSnapshot = null;
            if (kind == DuelKind.House)
            {
                var pool = await _context.DuelWordPool
                    .AsNoTracking()
                    .Where(x => x.ParametersId == parameters.Id && x.Active)
                    .Select(x => x.Word)
                    .ToListAsync();

                // ⚠️ DECISION 10'S LAST GUARD. A house duel with no pool has no word to draw, so the round
                // is refused here rather than failing later with the player's stake already on chain.
                if (pool.Count == 0)
                {
                    Log.Information("DUEL — game type {ParametersId} has an empty house word pool; no house round opened.", parameters.Id);
                    return null;
                }

                // Ordinal ascending so an offline verifier reproduces the draw's input order (§6.7).
                poolSnapshot = string.Join(',', pool.OrderBy(x => x, StringComparer.Ordinal));
            }

            var game = new Game(parameters.Id)
            {
                DuelKind = kind,
                Stake = null,
                OwnerPlayerId = null,
                BoardExpiresAt = null,          // decision 15: rooms never auto-expire
                DuelPoolSnapshot = poolSnapshot,
            };

            // Commit the seed in the SAME SaveChanges as the row, so the commitment always predates every
            // bet and the house word cannot be ground. Copied from OpenSoloBoard.
            var serverSeed = ProvablyFair.GenerateServerSeed();
            game.ServerSeed = serverSeed;
            game.ServerSeedHash = ProvablyFair.HashServerSeed(serverSeed);

            await _context.Games.AddAsync(game);
            await _context.SaveChangesAsync();

            // ⚠️ NO GameCreatedEvent broadcast and NO GameOpenedEvent (§6.1). GameOpenedEvent is what drives
            // the house SEEDER and the Orbit planner, and a duel wants neither (decisions 17 + 18). The
            // board hears about a room on the rooms channel once the CREATOR'S BET lands (§6.8), not when
            // the empty round opens — an unbet round is a shell, not a room.
            return game.Id;
        }
```

- [ ] **Step 4: Write the request and handler**

`src/SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/OpenDuelRoomRequest.cs`:

```csharp
using MediatR;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Contracts.Games;

namespace SpaceStroker.Infrastructure.Handlers.Games.OpenDuelRoom
{
    /// <summary>Open a duel round of a given kind (specs/monkey-duel.md §6.1).</summary>
    public record OpenDuelRoomRequest(Guid GameTypeId, OpenDuelRoomRequestBody Body) : IRequest<ActiveGameDto>;

    /// <summary><c>{ "kind": "House" | "OpenRoom" }</c>.</summary>
    public class OpenDuelRoomRequestBody
    {
        public DuelKind? Kind { get; set; }
    }
}
```

`src/SpaceStroker.Infrastructure/Handlers/Games/OpenDuelRoom/OpenDuelRoomHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Common.Messaging;
using SpaceStroker.Common.Resources;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Contracts.Games;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;

namespace SpaceStroker.Infrastructure.Handlers.Games.OpenDuelRoom
{
    /// <summary>
    /// Opens a duel round (#184 §6.1), modelled on OpenHouseEdgeBoardHandler. The round belongs to
    /// nobody until its creator's bet binds OwnerPlayerId and Stake.
    ///
    /// ⚠️ Every refusal is the SAME vague message every other game-type endpoint gives
    /// (constraints.md §6): an unknown, inactive, non-duel or coming-soon type, and a House round the
    /// HouseSeed wallet cannot back, are indistinguishable to the caller.
    /// </summary>
    public class OpenDuelRoomHandler : BaseRequestHandler<OpenDuelRoomRequest, ActiveGameDto>
    {
        private readonly IGameService _gameService;
        private readonly IDuelHouseCoverageService _coverage;

        public OpenDuelRoomHandler(SpaceStrokerDbContext context, IMessaging messaging,
            IGameService gameService, IDuelHouseCoverageService coverage) : base(context, messaging)
        {
            _gameService = gameService;
            _coverage = coverage;
        }

        public override async Task<ActiveGameDto> HandleRequest(OpenDuelRoomRequest request, CancellationToken cancellationToken)
        {
            var kind = request.Body?.Kind;

            if (kind is not (DuelKind.House or DuelKind.OpenRoom))
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterNotFound);

            var parameters = await _gameService.GetGameParametersById(request.GameTypeId);

            if (parameters == null || !parameters.Active || parameters.ComingSoon || !parameters.IsDuel)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterNotFound);

            // A House round the wallet cannot back is refused BEFORE the round exists, so the SPA lands on
            // the PVP board rather than opening a room nobody can play (AC 5).
            if (kind == DuelKind.House)
            {
                var decision = await _coverage.EvaluateAsync(parameters!, parameters!.RequiredAmount, cancellationToken);
                if (!decision.IsCoverable)
                    Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);
            }

            var gameId = await _gameService.OpenDuelRoom(parameters!, kind!.Value);

            if (gameId == null)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterHasNoActiveGames);

            var game = await LoadRound(Context, gameId!.Value, cancellationToken);
            return new ActiveGameDto(game!, null);
        }

        internal static Task<Domain.Games.Entities.Game?> LoadRound(SpaceStrokerDbContext context, Guid gameId, CancellationToken cancellationToken)
            => context.Games
                .Include(x => x.Bets).ThenInclude(b => b.Player)
                .Include(x => x.Owner)
                .Include(x => x.Parameters).ThenInclude(p => p.LotteryParameters)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);
    }
}
```

- [ ] **Step 5: Add the controller route**

In `src/SpaceStroker.Api/Controllers/GamesController.cs`, after `OpenHouseEdgeBoard`:

```csharp
        /// <summary>
        /// Open a Monkey Duel round (#184): <c>{ "kind": "House" | "OpenRoom" }</c>. The round belongs to
        /// nobody until the creator's bet binds its owner and its stake. Rate-limited.
        /// </summary>
        [HttpPost("{gameTypeId:guid}/duels")]
        public async Task<IActionResult> OpenDuelRoom(Guid gameTypeId, [FromBody] OpenDuelRoomRequestBody body)
            => await ProcessRequest(new OpenDuelRoomRequest(gameTypeId, body));
```

plus `using SpaceStroker.Infrastructure.Handlers.Games.OpenDuelRoom;`.

- [ ] **Step 6: Add the rate-limit rule and extend its test**

In `spacestrokersettings.json`, in `IpRateLimiting:GeneralRules`, beside `post:/games/*/boards`:

```jsonc
            {
                "Endpoint": "post:/games/*/duels",
                "Period": "1m",
                "Limit": 30
            },
```

⚠️ The path must **not** contain `/bets`. In `RateLimitSettingsTests`, add `"post:/games/*/duels"` to the sorted array in `GeneralRules_AreExactlyTheQuotaProtectingPatterns` and to the `/games` list in `GeneralRules_NeverCoverTheBetPath` (both keep alphabetical order: it sorts after `post:/games/*/boards`).

- [ ] **Step 7: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS. ⚠️ `IDuelHouseCoverageService` does not exist yet — create the interface file now (Task 9 writes the implementation) with just `Task<DuelCoverageDecision> EvaluateAsync(GameParameters parameters, decimal requestedStake, CancellationToken cancellationToken);` and register a temporary no-op implementation returning a coverable decision so the DI container resolves; Task 9 replaces it.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Api src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests
git commit -m "Open a duel round on request, committing its seed and pool snapshot together."
git push origin feature/monkey-duel
```

---

## Task 8: The open-rooms board — `GET /games/{gameTypeId}/duels`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Contracts/Games/DuelRoomDto.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/GetDuelRooms/GetDuelRoomsRequest.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/GetDuelRooms/GetDuelRoomsHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` + `RateLimitSettingsTests`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetDuelRoomsHandlerTests.cs`

**Interfaces:**
- Consumes: `Game.DuelKind/Stake/ClaimedByPlayerId/OwnerPlayerId` (Task 1).
- Produces:
  - `class DuelRoomDto { Guid GameId; string Word; decimal Stake; string? DisplayName; string? Wallet; DateTime CreatedDate; }`
  - `record DuelRoomEvent(string Change, DuelRoomDto? Room, Guid GameId)` — `Change` ∈ `"opened" | "taken" | "cancelled"` (used by Task 13)
  - `record GetDuelRoomsRequest(Guid GameTypeId, int PageNumber, int PageSize) : IRequest<PaginatedList<DuelRoomDto>>`

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Handlers/GetDuelRoomsHandlerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Handlers.Games.GetDuelRooms;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Handlers;

/// <summary>
/// #184 §6.2 — the public OPEN DUELS board: unmatched OpenRoom rounds of one type, newest first.
/// Words are VISIBLE by decision 11 (the joiner advantage is accepted, not mitigated).
/// </summary>
public class GetDuelRoomsHandlerTests
{
    private static SpaceStrokerDbContext NewContext()
        => new(new DbContextOptionsBuilder<SpaceStrokerDbContext>()
            .UseInMemoryDatabase($"duel-board-{Guid.NewGuid():N}").Options);

    private static Player NewPlayer(SpaceStrokerDbContext context, string wallet, string name)
    {
        var player = new Player { ExternalPlayerId = wallet, DisplayName = name, LastSeenAt = DateTime.UtcNow };
        context.Players.Add(player);
        return player;
    }

    /// <param name="ownerId">null ⇒ an OPENED-BUT-NEVER-BET shell, which is not a room.</param>
    private static Game Room(SpaceStrokerDbContext context, Guid parametersId, Guid? ownerId,
        string word, decimal stake, Guid? claimedBy = null, DuelKind kind = DuelKind.OpenRoom,
        GameStatus status = GameStatus.WaitingBets, DateTime? createdDate = null)
    {
        var game = new Game(parametersId)
        {
            DuelKind = kind, Stake = ownerId == null ? null : stake, Status = status,
            OwnerPlayerId = ownerId, ClaimedByPlayerId = claimedBy,
        };
        if (createdDate.HasValue)
            game.CreatedDate = createdDate.Value;

        context.Games.Add(game);
        if (ownerId != null)
            context.GameBets.Add(new Bet(game.Id, "seed", ownerId, word, stake));
        return game;
    }

    [Fact]
    public async Task ListsOnlyUnmatchedOpenRoomsOfThisType_NewestFirst()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        var otherTypeId = Guid.NewGuid();
        var alice = NewPlayer(context, "alice-wallet", "Alice");
        var bob = NewPlayer(context, "bob-wallet", "Bob");
        await context.SaveChangesAsync();

        var older = Room(context, typeId, alice.Id, "banana", 0.05m, createdDate: DateTime.UtcNow.AddMinutes(-5));
        var newer = Room(context, typeId, bob.Id, "cosmos", 0.10m, createdDate: DateTime.UtcNow);
        Room(context, typeId, alice.Id, "rocket", 0.05m, claimedBy: bob.Id);              // taken
        Room(context, typeId, null, "planet", 0.05m);                                      // never bet
        Room(context, typeId, alice.Id, "meteor", 0.05m, kind: DuelKind.House);            // house round
        Room(context, typeId, alice.Id, "launch", 0.05m, status: GameStatus.InProgress);   // running
        Room(context, otherTypeId, alice.Id, "gorill", 0.05m);                             // another type
        await context.SaveChangesAsync();

        var page = await new GetDuelRoomsHandler(context, TestMessaging.Create())
            .HandleRequest(new GetDuelRoomsRequest(typeId, 1, 30), default);

        page.Items.Select(x => x.GameId).ShouldBe([newer.Id, older.Id]);
        page.Items[0].Word.ShouldBe("cosmos");
        page.Items[0].Stake.ShouldBe(0.10m);
        page.Items[0].DisplayName.ShouldBe("Bob");
        page.Items[0].Wallet.ShouldBe("bob-wallet");
    }

    /// <summary>
    /// OwnerPlayerId IS NOT NULL is load-bearing: a round that was opened but never bet is an abandoned
    /// shell, not a room. Without it the board fills with empty rounds nobody can join.
    /// </summary>
    [Fact]
    public async Task ExcludesARoundThatWasOpenedButNeverBet()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        Room(context, typeId, null, "banana", 0.05m);
        await context.SaveChangesAsync();

        var page = await new GetDuelRoomsHandler(context, TestMessaging.Create())
            .HandleRequest(new GetDuelRoomsRequest(typeId, 1, 30), default);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task PageSizeIsCappedAtThirty()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        var alice = NewPlayer(context, "alice-wallet", "Alice");
        await context.SaveChangesAsync();

        for (var i = 0; i < 35; i++)
            Room(context, typeId, alice.Id, $"word{i:D2}", 0.05m, createdDate: DateTime.UtcNow.AddSeconds(-i));
        await context.SaveChangesAsync();

        var page = await new GetDuelRoomsHandler(context, TestMessaging.Create())
            .HandleRequest(new GetDuelRoomsRequest(typeId, 1, 500), default);

        page.Items.Count.ShouldBe(30);
        page.TotalCount.ShouldBe(35);
    }

    [Fact]
    public async Task AnEmptyBoardIsAnEmptyPage_NotAnError()
    {
        using var context = NewContext();

        var page = await new GetDuelRoomsHandler(context, TestMessaging.Create())
            .HandleRequest(new GetDuelRoomsRequest(Guid.NewGuid(), 1, 30), default);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }
}
```

⚠️ `TestMessaging.Create()` and the `PaginatedList<T>` shape: before writing this, open `src/SpaceStroker.Tests/UnitTests/Handlers/` for an existing paged-handler test (`GetGamesHistoryHandlerTests` is the closest) and copy its `IMessaging` construction and its `PaginatedList` property names (`Items` / `TotalCount` may be spelled differently) verbatim rather than assuming these.

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~GetDuelRoomsHandlerTests"`
Expected: FAIL — `GetDuelRoomsHandler` does not exist.

- [ ] **Step 3: Write the DTOs**

`src/SpaceStroker.Infrastructure/Contracts/Games/DuelRoomDto.cs`:

```csharp
using System;

namespace SpaceStroker.Infrastructure.Contracts.Games
{
    /// <summary>
    /// One room on the OPEN DUELS board (#184 §6.2) — who, what word, what stake.
    ///
    /// ⚠️ Word is the creator's bet choice and is deliberately PUBLIC (decision 11): the joiner picks
    /// second, seeing it. That advantage is ACCEPTED, not mitigated — the owner's call — and the
    /// ProductionGames Monte Carlo measures it so it is priced rather than unknown.
    /// </summary>
    public class DuelRoomDto
    {
        public Guid GameId { get; set; }
        public string Word { get; set; }
        public decimal Stake { get; set; }
        public string? DisplayName { get; set; }
        public string? Wallet { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    /// <summary>
    /// A change on a game type's rooms channel (#184 §6.8). Broadcast alongside the write that causes it,
    /// so the board never refetches and never polls (AC 10).
    /// </summary>
    /// <param name="Change">"opened" | "taken" | "cancelled".</param>
    /// <param name="Room">The row to add on "opened"; null on "taken" and "cancelled", which only remove.</param>
    public record DuelRoomEvent(string Change, DuelRoomDto? Room, Guid GameId);
}
```

- [ ] **Step 4: Write the request and handler**

`GetDuelRoomsRequest.cs`:

```csharp
using MediatR;
using SpaceStroker.Common.Models;
using SpaceStroker.Infrastructure.Contracts.Games;

namespace SpaceStroker.Infrastructure.Handlers.Games.GetDuelRooms
{
    /// <summary>The public OPEN DUELS board for a game type (specs/monkey-duel.md §6.2).</summary>
    public record GetDuelRoomsRequest(Guid GameTypeId, int PageNumber, int PageSize)
        : IRequest<PaginatedList<DuelRoomDto>>;
}
```

`GetDuelRoomsHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Common.Messaging;
using SpaceStroker.Common.Models;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Contracts.Games;
using SpaceStroker.Infrastructure.Data.Context;

namespace SpaceStroker.Infrastructure.Handlers.Games.GetDuelRooms
{
    /// <summary>
    /// The OPEN DUELS board (#184 §6.2): unmatched OpenRoom rounds of one type, newest first.
    ///
    /// ⚠️ OwnerPlayerId IS NOT NULL is load-bearing: a round that was opened but never bet is an
    /// abandoned shell, not a room — listing it would fill the board with rounds nobody can join.
    ///
    /// ⚠️ An unknown type returns an EMPTY page, not a refusal. The board is public and a missing type
    /// must not be distinguishable from a quiet one (constraints.md §6).
    /// </summary>
    public class GetDuelRoomsHandler : BaseRequestHandler<GetDuelRoomsRequest, PaginatedList<DuelRoomDto>>
    {
        /// <summary>Matches GET /games/{id}/history, which is the other public paged board.</summary>
        public const int MaxPageSize = 30;

        public GetDuelRoomsHandler(SpaceStrokerDbContext context, IMessaging messaging) : base(context, messaging) { }

        public override async Task<PaginatedList<DuelRoomDto>> HandleRequest(GetDuelRoomsRequest request, CancellationToken cancellationToken)
        {
            var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
            var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

            var rooms = Context.Games
                .AsNoTracking()
                .Where(x => x.ParametersId == request.GameTypeId
                         && x.DuelKind == DuelKind.OpenRoom
                         && x.Status == GameStatus.WaitingBets
                         && x.ClaimedByPlayerId == null
                         && x.OwnerPlayerId != null);

            var totalCount = await rooms.CountAsync(cancellationToken);

            var page = await rooms
                .OrderByDescending(x => x.CreatedDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new DuelRoomDto
                {
                    GameId = x.Id,
                    // The creator's bet is the ONLY bet on an unmatched room, so First is unambiguous.
                    Word = x.Bets.OrderBy(b => b.CreatedDate).Select(b => b.Choice).FirstOrDefault() ?? string.Empty,
                    Stake = x.Stake ?? 0m,
                    DisplayName = x.Owner!.DisplayName,
                    Wallet = x.Owner!.ExternalPlayerId,
                    CreatedDate = x.CreatedDate,
                })
                .ToListAsync(cancellationToken);

            return new PaginatedList<DuelRoomDto>(page, totalCount, pageNumber, pageSize);
        }
    }
}
```

⚠️ Match `PaginatedList<T>`'s real constructor — open `src/SpaceStroker.Common/Models/PaginatedList.cs` and copy its signature; `GetGamesHistoryHandler` shows the shipped call shape.

- [ ] **Step 5: Add the controller route + rate-limit rule**

Controller:

```csharp
        /// <summary>
        /// The public OPEN DUELS board for a duel type (#184): unmatched player-created rooms, newest
        /// first. pageSize is capped at 30. Rate-limited.
        /// </summary>
        [HttpGet("{gameTypeId:guid}/duels")]
        public async Task<IActionResult> GetDuelRooms(Guid gameTypeId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 30)
            => await ProcessRequest(new GetDuelRoomsRequest(gameTypeId, pageNumber, pageSize));
```

Config, beside the POST rule:

```jsonc
            {
                "Endpoint": "get:/games/*/duels",
                "Period": "1m",
                "Limit": 120
            },
```

Add `"get:/games/*/duels"` to both assertions in `RateLimitSettingsTests` (it sorts before `get:/games/rounds/*`).

- [ ] **Step 6: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Api src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests
git commit -m "Serve the open-duels board as a paged public listing."
git push origin feature/monkey-duel
```

---

## Task 9: `DuelHouseCoverageService` — the advisory-locked wrapper

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Games/IDuelHouseCoverageService.cs` (replace the Task 7 stub)
- Create: `src/SpaceStroker.Infrastructure/Services/Games/DuelHouseCoverageService.cs`
- Modify: `src/SpaceStroker.Api/Program.cs` and `src/SpaceStroker.Consumers/Program.cs` (DI registration — follow wherever `IHouseEdgeCoverageService` is registered)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/DuelHouseCoverageServiceTests.cs`

**Interfaces:**
- Consumes: `DuelHouseCoverage.Evaluate`, `DuelPendingHouseStake`, `DuelCoverageDecision` (Task 4); `IHouseSeedAccountResolver` (existing); `ISolanaService.GetSolBalanceForRequest` (existing).
- Produces:
  - `Task<DuelCoverageDecision> IDuelHouseCoverageService.EvaluateAsync(GameParameters parameters, decimal requestedStake, CancellationToken)`
  - `Task<IDuelCoverageLock> IDuelHouseCoverageService.AcquireLockAsync(CancellationToken)`
  - `interface IDuelCoverageLock : IAsyncDisposable { Task CommitAsync(CancellationToken); }`
  - `const long DuelHouseCoverageService.AdvisoryLockKey` — **must differ** from `HouseEdgeCoverageService.AdvisoryLockKey` (`0x4859504552454447L`), or Hyper Roll bets queue behind duel bets.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Services/DuelHouseCoverageServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;
using SpaceStroker.Infrastructure.Services.HouseSeed;
using SpaceStroker.Infrastructure.Services.Solana;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Services;

/// <summary>
/// #184 §6.5 — the service half: which rounds count as PENDING, and a FRESH (uncached, request-path)
/// HouseSeed balance read. The decision itself is DuelHouseCoverage.Evaluate, tested separately.
/// </summary>
public class DuelHouseCoverageServiceTests
{
    private const string HouseWallet = "MOCK_HOUSE_SEED_PUBLIC_KEY";

    private static SpaceStrokerDbContext NewContext()
        => new(new DbContextOptionsBuilder<SpaceStrokerDbContext>()
            .UseInMemoryDatabase($"duel-cov-svc-{Guid.NewGuid():N}").Options);

    private static (DuelHouseCoverageService Service, Mock<ISolanaService> Solana) Create(
        SpaceStrokerDbContext context, decimal? balance)
    {
        var solana = new Mock<ISolanaService>();
        solana.Setup(x => x.GetSolBalanceForRequest(HouseWallet)).ReturnsAsync(balance);

        var accounts = new Mock<IHouseSeedAccountResolver>();
        accounts.SetupGet(x => x.IsConfigured).Returns(true);
        accounts.SetupGet(x => x.PublicKey).Returns(HouseWallet);

        var settings = new GeneralSettings
        {
            Operator = new OperatorSettings
            {
                Token = new TokenSettings
                {
                    Decimals = 9,
                    HouseSeed = new HouseSeedSettings { PublicKey = HouseWallet, MinimumSolBalance = 0.02m },
                },
            },
        };

        return (new DuelHouseCoverageService(context, solana.Object, accounts.Object, settings), solana);
    }

    private static GameParameters DuelType(Guid id) => new(GameCharacterType.Letters, 6, true)
    {
        Id = id, Name = "Monkey Duel", Description = "d",
        RequiredAmount = 0.002m, MaximumStake = 0.5m, IsDuel = true, Active = true,
    };

    /// <summary>A House round with a stake and NO house bet yet — a stake the house has committed to match.</summary>
    private static void PendingHouseRound(SpaceStrokerDbContext context, Guid typeId, decimal stake)
    {
        var game = new Game(typeId) { DuelKind = DuelKind.House, Stake = stake, Status = GameStatus.WaitingBets };
        context.Games.Add(game);
        context.GameBets.Add(new Bet(game.Id, "seed", Guid.NewGuid(), "banana", stake));
    }

    [Fact]
    public async Task ReadsTheHouseSeedWallet_NotTheTreasury()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        var (service, solana) = Create(context, 10m);

        await service.EvaluateAsync(DuelType(typeId), 0.1m, default);

        // ⚠️ The REQUEST-PATH read (no cache, no Consumers retry policy) on the HOUSE SEED wallet.
        solana.Verify(x => x.GetSolBalanceForRequest(HouseWallet), Times.Once);
        solana.Verify(x => x.GetSolBalance(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CountsHouseRoundsAwaitingTheirCounterpartyBetAsPending()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        PendingHouseRound(context, typeId, 0.1m);
        PendingHouseRound(context, typeId, 0.2m);
        await context.SaveChangesAsync();

        var (service, _) = Create(context, 10m);
        var decision = await service.EvaluateAsync(DuelType(typeId), 0.1m, default);

        decision.PendingTotal.ShouldBe(0.3m);
    }

    [Fact]
    public async Task ARoundThatAlreadyHasItsHouseBetIsNotPending()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();
        var player = new Player { ExternalPlayerId = HouseWallet, DisplayName = "House", LastSeenAt = DateTime.UtcNow };
        context.Players.Add(player);

        var game = new Game(typeId) { DuelKind = DuelKind.House, Stake = 0.1m, Status = GameStatus.WaitingBets };
        context.Games.Add(game);
        context.GameBets.Add(new Bet(game.Id, "seed", Guid.NewGuid(), "banana", 0.1m));
        context.GameBets.Add(new Bet(game.Id, "seed", player.Id, "cosmos", 0.1m));   // the house side landed
        await context.SaveChangesAsync();

        var (service, _) = Create(context, 10m);
        (await service.EvaluateAsync(DuelType(typeId), 0.1m, default)).PendingTotal.ShouldBe(0m);
    }

    [Fact]
    public async Task OpenRoomsAndSettledRoundsAreNotPending()
    {
        using var context = NewContext();
        var typeId = Guid.NewGuid();

        // PVP: the house owes nothing.
        var pvp = new Game(typeId) { DuelKind = DuelKind.OpenRoom, Stake = 0.3m, Status = GameStatus.WaitingBets };
        context.Games.Add(pvp);
        // Settled: already paid, or never ran.
        var ended = new Game(typeId) { DuelKind = DuelKind.House, Stake = 0.3m, Status = GameStatus.RewardForWinnersCompleted };
        context.Games.Add(ended);
        await context.SaveChangesAsync();

        var (service, _) = Create(context, 10m);
        (await service.EvaluateAsync(DuelType(typeId), 0.1m, default)).PendingTotal.ShouldBe(0m);
    }

    /// <summary>Review Focus 3 — a thrown or null balance read fails closed.</summary>
    [Fact]
    public async Task ANullBalanceRead_IsNotCoverable()
    {
        using var context = NewContext();
        var (service, _) = Create(context, null);

        var decision = await service.EvaluateAsync(DuelType(Guid.NewGuid()), 0.1m, default);

        decision.IsCoverable.ShouldBeFalse();
        decision.MaxStake.ShouldBe(0m);
    }

    [Fact]
    public async Task AnUnconfiguredHouseSeedWallet_IsNotCoverable_AndReadsNothing()
    {
        using var context = NewContext();
        var solana = new Mock<ISolanaService>();
        var accounts = new Mock<IHouseSeedAccountResolver>();
        accounts.SetupGet(x => x.IsConfigured).Returns(false);

        var settings = new GeneralSettings
        {
            Operator = new OperatorSettings
            {
                Token = new TokenSettings { Decimals = 9, HouseSeed = new HouseSeedSettings { MinimumSolBalance = 0.02m } },
            },
        };

        var service = new DuelHouseCoverageService(context, solana.Object, accounts.Object, settings);
        var decision = await service.EvaluateAsync(DuelType(Guid.NewGuid()), 0.1m, default);

        decision.IsCoverable.ShouldBeFalse();
        solana.Verify(x => x.GetSolBalanceForRequest(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void TheAdvisoryLockKeyIsDistinctFromTheHouseEdgeOne()
    {
        // ⚠️ Hyper Roll bets must NOT queue behind duel bets — they are different wallets and different
        // rules, and sharing a key would serialise two unrelated money paths.
        DuelHouseCoverageService.AdvisoryLockKey.ShouldNotBe(HouseEdgeCoverageService.AdvisoryLockKey);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseCoverageServiceTests"`
Expected: FAIL — `DuelHouseCoverageService` does not exist.

- [ ] **Step 3: Write the interface**

`src/SpaceStroker.Infrastructure/Services/Games/IDuelHouseCoverageService.cs` (replacing the Task 7 stub):

```csharp
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;

namespace SpaceStroker.Infrastructure.Services.Games
{
    /// <summary>
    /// Whether the HouseSeed wallet can fund one more house-duel counterparty stake (#184 §6.5). The
    /// decision itself is <see cref="DuelHouseCoverage.Evaluate"/>; this service only feeds it — the
    /// pending house stakes from the database and a FRESH HouseSeed balance from the chain — so the
    /// availability pre-check and the RegisterDuelBet backstop can never disagree.
    /// </summary>
    public interface IDuelHouseCoverageService
    {
        Task<DuelCoverageDecision> EvaluateAsync(GameParameters parameters, decimal requestedStake, CancellationToken cancellationToken);

        /// <summary>
        /// Opens a transaction on the request's DbContext and takes the DUEL advisory lock. Everything
        /// between this and <see cref="IDuelCoverageLock.CommitAsync"/> — bind/claim → coverage → insert —
        /// is serialized across every API and Consumers instance. Disposing without committing rolls back.
        /// </summary>
        Task<IDuelCoverageLock> AcquireLockAsync(CancellationToken cancellationToken);
    }

    public interface IDuelCoverageLock : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 4: Write the implementation**

`src/SpaceStroker.Infrastructure/Services/Games/DuelHouseCoverageService.cs` — copy `HouseEdgeCoverageService`'s lock plumbing verbatim, changing only the key and the evaluation:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.HouseSeed;
using SpaceStroker.Infrastructure.Services.Solana;

namespace SpaceStroker.Infrastructure.Services.Games
{
    public class DuelHouseCoverageService : IDuelHouseCoverageService
    {
        /// <summary>
        /// The advisory-lock key for duel coverage ("MONKDUEL" as ASCII).
        ///
        /// ⚠️ DISTINCT from HouseEdgeCoverageService.AdvisoryLockKey, deliberately (§6.5): the two rules
        /// read different wallets and price different exposure, so sharing a key would make every Hyper
        /// Roll bet queue behind every duel bet for no benefit.
        ///
        /// ⚠️ ONE key for every duel round, not one per round: the pending-stake sum spans every open
        /// House round, so two creator bets on two different rounds must serialize too.
        /// </summary>
        public const long AdvisoryLockKey = 0x4D4F4E4B4455454CL;

        /// <summary>Same bound, and the same reasoning, as HouseEdgeCoverageService.LockTimeout.</summary>
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        private static readonly BetStatus[] SettledBetStatuses = [BetStatus.Refunded];

        private readonly SpaceStrokerDbContext _context;
        private readonly ISolanaService _solana;
        private readonly IHouseSeedAccountResolver _accounts;
        private readonly GeneralSettings _settings;

        public DuelHouseCoverageService(SpaceStrokerDbContext context, ISolanaService solana,
            IHouseSeedAccountResolver accounts, GeneralSettings settings)
        {
            _context = context;
            _solana = solana;
            _accounts = accounts;
            _settings = settings;
        }

        private HouseSeedSettings Config => _settings.Operator.Token.HouseSeed;

        public async Task<DuelCoverageDecision> EvaluateAsync(GameParameters parameters, decimal requestedStake, CancellationToken cancellationToken)
        {
            var ceiling = parameters.MaximumStake ?? 0m;

            // No wallet configured ⇒ the house cannot take the other side at all. Fail closed without
            // spending an RPC call.
            if (!_accounts.IsConfigured)
                return DuelHouseCoverage.Evaluate(null, Config.MinimumSolBalance, ceiling, [], requestedStake);

            // Pending FIRST, balance second: a round whose house bet lands in between leaves the stake
            // still counted against a balance that already spent it — conservative. The other order would
            // count a pre-transfer balance against post-transfer exposure.
            var pending = await PendingHouseStakes(_context.Games.AsNoTracking(), parameters.Id)
                .Select(x => new { x.Id, Stake = x.Stake!.Value })
                .ToListAsync(cancellationToken);

            // ⚠️ FRESH: straight to the RPC, no cache. The REQUEST-PATH read, not GetSolBalance — this runs
            // while the duel lock is held (and on the availability endpoint), so the Consumers retry policy
            // would serialize every duel bet behind seconds of backoff. Null = the read failed.
            var balance = await _solana.GetSolBalanceForRequest(_accounts.PublicKey);

            var decision = DuelHouseCoverage.Evaluate(
                balance, Config.MinimumSolBalance, ceiling,
                pending.Select(x => new DuelPendingHouseStake(x.Id, x.Stake)),
                requestedStake);

            if (!decision.IsCoverable)
            {
                Log.Warning("DUEL — the HouseSeed wallet cannot cover a {Amount} stake on type {ParametersId}: balance {Balance}, spendable {Spendable}, pending {Pending} over {Rounds} round(s).",
                    requestedStake, parameters.Id, balance, decision.Spendable, decision.PendingTotal, pending.Count);
            }

            return decision;
        }

        /// <summary>
        /// House duel rounds the house has committed to matching but has not yet transferred for: still
        /// accepting bets, carrying a stake, and holding no bet from the house wallet.
        ///
        /// ⚠️ "No house bet yet" is keyed on the house WALLET's player row, because that is what
        /// DuelHouseBetService writes — the house bet is ORDINARY in every other respect and carries no
        /// marker column (and must never carry FundedByOrbit).
        /// </summary>
        public static IQueryable<Game> PendingHouseStakes(IQueryable<Game> games, Guid parametersId)
            => games.Where(x => x.ParametersId == parametersId
                             && x.DuelKind == DuelKind.House
                             && Game.GameAcceptingBets.Contains(x.Status)
                             && x.Stake != null
                             && x.ClaimedByPlayerId == null);

        public async Task<IDuelCoverageLock> AcquireLockAsync(CancellationToken cancellationToken)
        {
            var owned = _context.Database.CurrentTransaction == null;
            var transaction = owned
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : _context.Database.CurrentTransaction!;

            try
            {
                foreach (var statement in LockStatements())
                    await _context.Database.ExecuteSqlInterpolatedAsync(statement, cancellationToken);
            }
            catch
            {
                if (owned)
                    await transaction.DisposeAsync();
                throw;
            }

            return new CoverageLock(owned ? transaction : null);
        }

        /// <summary>
        /// Cap the lock wait FIRST (transaction-local), then take the lock — the other order waits for the
        /// lock with no cap at all. Same shape as HouseEdgeCoverageService.LockStatements.
        /// </summary>
        public static IReadOnlyList<FormattableString> LockStatements()
        {
            var lockTimeout = $"{(int)LockTimeout.TotalMilliseconds}ms";
            return
            [
                $"SELECT set_config('lock_timeout', {lockTimeout}, true)",
                $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
            ];
        }

        private sealed class CoverageLock(IDbContextTransaction? transaction) : IDuelCoverageLock
        {
            public Task CommitAsync(CancellationToken cancellationToken)
                => transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

            public ValueTask DisposeAsync()
                => transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: Register it in DI**

Find every `AddScoped<IHouseEdgeCoverageService, HouseEdgeCoverageService>()` (API `Program.cs`, Consumers `Program.cs`, and any shared `ServiceCollectionExtensions`) and add the duel equivalent on the next line. Remove the Task 7 temporary stub registration.

- [ ] **Step 6: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services/Games src/SpaceStroker.Api/Program.cs src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Tests/UnitTests/Services/DuelHouseCoverageServiceTests.cs
git commit -m "Serialize duel coverage behind its own advisory lock."
git push origin feature/monkey-duel
```

---

## Task 10: `RegisterDuelBet` — bind, claim, cover, insert

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs`
- Create: `src/SpaceStroker.Infrastructure/Consumers/DuelHouseBet/DuelCreatorBetPlacedEvent.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `IDuelHouseCoverageService` (Task 9); `Game.Stake/ClaimedByPlayerId/DuelKind` (Task 1); `BetDomainService` duel branch (Task 3).
- Produces: `record DuelCreatorBetPlacedEvent(Guid GameId)`; `RegisterBetHandler` routes `IsDuel` rounds to a private `RegisterDuelBet(CancellationToken)`.

This is the highest-risk task in the plan: it decides money. Read `RegisterHouseEdgeBet` in full first — `RegisterDuelBet` mirrors its structure exactly (refusals decided inside the try blocks but refunded outside them, fail closed, one transaction).

- [ ] **Step 1: Write the failing tests**

Append to `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs`:

```csharp
    // ---- #184 Monkey Duel -------------------------------------------------------------------

    /// <summary>AC 6 — the creator's bet binds the round: it sets the owner AND the round's own stake.</summary>
    [Fact]
    public async Task DuelCreatorBet_BindsTheOwnerAndTheRoundStake()
    {
        var harness = DuelHarness.OpenRoom();

        await harness.PlaceAsync(wallet: "creator", choice: "banana", amount: 0.125m);

        var round = harness.Round();
        round.OwnerPlayerId.ShouldBe(harness.PlayerId("creator"));
        round.Stake.ShouldBe(0.125m);
        round.ClaimedByPlayerId.ShouldBeNull();
        harness.Bets().Count.ShouldBe(1);
    }

    /// <summary>The joiner claims the room — the conditional update is what makes the race safe.</summary>
    [Fact]
    public async Task DuelJoinerBet_ClaimsTheRoom()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);

        await harness.PlaceAsync("joiner", "cosmos", 0.125m);

        var round = harness.Round();
        round.OwnerPlayerId.ShouldBe(harness.PlayerId("creator"));
        round.ClaimedByPlayerId.ShouldBe(harness.PlayerId("joiner"));
        harness.Bets().Count.ShouldBe(2);
    }

    /// <summary>AC 8 — the SECOND joiner loses the claim (0 rows) and is refunded, vaguely.</summary>
    [Fact]
    public async Task DuelSecondJoiner_IsRefundedWithTheVagueAlreadyTakenMessage()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);
        await harness.PlaceAsync("joiner", "cosmos", 0.125m);

        var refusal = await harness.PlaceExpectingRefusalAsync("latecomer", "rocket", 0.125m);

        refusal.ShouldContain(ErrorMessages.ChoiceAlreadyTaken);
        harness.RefundedSignatures().ShouldContain(harness.LastSignature);
        harness.Bets().Count.ShouldBe(2);
        harness.Round().ClaimedByPlayerId.ShouldBe(harness.PlayerId("joiner"));
    }

    /// <summary>
    /// Review Focus 4 — the creator must not join their OWN room. Without `OwnerPlayerId &lt;&gt; @p` on the
    /// claim it is a self-duel that always pays them back minus the cut.
    /// </summary>
    [Fact]
    public async Task DuelCreatorCannotJoinTheirOwnRoom()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);

        var refusal = await harness.PlaceExpectingRefusalAsync("creator", "cosmos", 0.125m);

        refusal.ShouldContain(ErrorMessages.ChoiceAlreadyTaken);
        harness.RefundedSignatures().ShouldContain(harness.LastSignature);
        harness.Round().ClaimedByPlayerId.ShouldBeNull();
        harness.Bets().Count.ShouldBe(1);
    }

    /// <summary>AC 11 — the joiner copying the creator's word is refused by the EXISTING free-text rule.</summary>
    [Fact]
    public async Task DuelJoinerCopyingTheCreatorsWord_IsRefundedAsChoiceAlreadyTaken()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);

        var refusal = await harness.PlaceExpectingRefusalAsync("joiner", "banana", 0.125m);

        refusal.ShouldContain(ErrorMessages.ChoiceAlreadyTaken);
        harness.Round().ClaimedByPlayerId.ShouldBeNull();
    }

    /// <summary>AC 7 — the joiner's amount must match the room's stake; a mismatch is refunded.</summary>
    [Fact]
    public async Task DuelJoinerWithTheWrongAmount_IsRefundedAsInvalidAmount()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);

        var refusal = await harness.PlaceExpectingRefusalAsync("joiner", "cosmos", 0.2m);

        refusal.ShouldContain(BetValidationResult.InvalidAmount.StringValueOf());
        harness.RefundedSignatures().ShouldContain(harness.LastSignature);
        harness.Round().ClaimedByPlayerId.ShouldBeNull();
    }

    /// <summary>AC 4 — a creator stake above the CLAMPED ceiling is refused by the backstop and refunded.</summary>
    [Fact]
    public async Task DuelCreatorBetAboveTheClampedCeiling_IsRefunded()
    {
        var harness = DuelHarness.House(coverable: false);

        var refusal = await harness.PlaceExpectingRefusalAsync("creator", "banana", 0.5m);

        refusal.ShouldContain(ErrorMessages.HouseEdgeUnderMaintenance);
        harness.RefundedSignatures().ShouldContain(harness.LastSignature);
        harness.Round().OwnerPlayerId.ShouldBeNull();
        harness.Bets().ShouldBeEmpty();
    }

    /// <summary>Coverage is a HOUSE-round concern only: a PVP room needs no HouseSeed wallet at all.</summary>
    [Fact]
    public async Task DuelOpenRoomBet_IsNotCoverageChecked()
    {
        var harness = DuelHarness.OpenRoom(coverable: false);

        await harness.PlaceAsync("creator", "banana", 0.125m);

        harness.Round().Stake.ShouldBe(0.125m);
        harness.CoverageEvaluations.ShouldBe(0);
    }

    /// <summary>A free bet moves no money, so it skips coverage but still binds the round (§3.1 S7).</summary>
    [Fact]
    public async Task DuelFreeCreatorBet_BindsTheRoundWithoutACoverageCheck()
    {
        var harness = DuelHarness.House(coverable: false, allowFreeBets: true);

        await harness.PlaceFreeAsync("creator", "banana");

        harness.Round().Stake.ShouldBe(0m);
        harness.CoverageEvaluations.ShouldBe(0);
    }

    /// <summary>A creator bet on a HOUSE round asks Consumers for the counterparty bet.</summary>
    [Fact]
    public async Task DuelCreatorBetOnAHouseRound_PublishesDuelCreatorBetPlaced()
    {
        var harness = DuelHarness.House();

        await harness.PlaceAsync("creator", "banana", 0.125m);

        harness.PublishedEvents.OfType<DuelCreatorBetPlacedEvent>()
            .ShouldHaveSingleItem().GameId.ShouldBe(harness.GameId);
    }

    [Fact]
    public async Task DuelCreatorBetOnAPvpRoom_PublishesNoDuelCreatorBetPlaced()
    {
        var harness = DuelHarness.OpenRoom();

        await harness.PlaceAsync("creator", "banana", 0.125m);

        harness.PublishedEvents.OfType<DuelCreatorBetPlacedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task DuelJoinerBetPublishesNoDuelCreatorBetPlaced()
    {
        var harness = DuelHarness.House();
        await harness.PlaceAsync("creator", "banana", 0.125m);
        harness.PublishedEvents.Clear();

        await harness.PlaceAsync("house", "cosmos", 0.125m);

        harness.PublishedEvents.OfType<DuelCreatorBetPlacedEvent>().ShouldBeEmpty();
    }

    /// <summary>A bet on a round that already has both sides is refused — never a third bet in a duel.</summary>
    [Fact]
    public async Task DuelBetOnAFullRound_IsRefunded()
    {
        var harness = DuelHarness.OpenRoom();
        await harness.PlaceAsync("creator", "banana", 0.125m);
        await harness.PlaceAsync("joiner", "cosmos", 0.125m);

        await harness.PlaceExpectingRefusalAsync("third", "rocket", 0.125m);

        harness.Bets().Count.ShouldBe(2);
    }
```

⚠️ `DuelHarness` is a **new** test fixture. Build it in `src/SpaceStroker.Tests/UnitTests/Handlers/DuelHarness.cs` by copying the construction the existing `RegisterBetHandlerTests` already uses for house-edge bets (same in-memory `SpaceStrokerDbContext`, same Moq'd `IMessaging`, `IMessageBus`, `ITokenSettlementService`, `IReferralDirectoryProvider`, `IGameService`, `IServiceScopeFactory`), and add:

- `static DuelHarness OpenRoom(bool coverable = true)` / `static DuelHarness House(bool coverable = true, bool allowFreeBets = false)` — seeds a duel `GameParameters` (`IsDuel`, `RequiredAmount = 0.002m`, `MaximumStake = 0.5m`, `PresetChoices` empty) and one open round of that kind via `GameService.OpenDuelRoom`.
- `Task PlaceAsync(string wallet, string choice, decimal amount)` / `PlaceFreeAsync` / `Task<string> PlaceExpectingRefusalAsync(...)` (catches the `ControlledException` the messaging double throws and returns its message).
- `Guid GameId`, `string LastSignature`, `Game Round()`, `List<Bet> Bets()`, `Guid PlayerId(string wallet)`, `List<string> RefundedSignatures()` (from `PaymentRefundClaims`), `List<object> PublishedEvents`, `int CoverageEvaluations` (a counter on the `IDuelHouseCoverageService` mock).
- The `IDuelHouseCoverageService` mock returns `new DuelCoverageDecision(coverable, coverable ? 10m : 0.001m, coverable ? 0.5m : 0m, 0m, 0m)` and a no-op `IDuelCoverageLock`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~RegisterBetHandlerTests"`
Expected: FAIL — duel rounds fall through to `RegisterBet` and nothing binds `Stake` or `ClaimedByPlayerId`.

- [ ] **Step 3: Add the event**

`src/SpaceStroker.Infrastructure/Consumers/DuelHouseBet/DuelCreatorBetPlacedEvent.cs`:

```csharp
using System;

namespace SpaceStroker.Infrastructure.Consumers.DuelHouseBet
{
    /// <summary>
    /// A CREATOR's bet has committed on a House duel round (#184 §6.4) — Consumers must now place the
    /// house counterparty bet.
    ///
    /// ⚠️ Published only for DuelKind.House, and only for the creator's bet. A PVP room waits for a human,
    /// and the joiner's (or the house's own) bet is what starts the round.
    ///
    /// ⚠️ Deliberately NOT GameOpenedEvent: that one reaches the house SEEDER and the Orbit planner, and a
    /// duel must reach neither (decisions 17 + 18).
    /// </summary>
    public record DuelCreatorBetPlacedEvent(Guid GameId);
}
```

- [ ] **Step 4: Route duels and implement `RegisterDuelBet`**

In `HandleRequest`, replace:

```csharp
            if (betGame?.Parameters?.IsHouseEdge == true)
                await RegisterHouseEdgeBet(cancellationToken);
            else
                await RegisterBet();
```

with:

```csharp
            if (betGame?.Parameters?.IsHouseEdge == true)
                await RegisterHouseEdgeBet(cancellationToken);
            else if (betGame?.Parameters?.IsDuel == true)
                await RegisterDuelBet(cancellationToken);
            else
                await RegisterBet();
```

and, after the `BetPlacedEvent` publish at the end of `HandleRequest`:

```csharp
            // #184: a CREATOR's bet on a House round is what asks Consumers for the counterparty bet.
            // Published last, after the bet and its ledger row are committed, so the consumer can never
            // load a round whose creator bet is not there yet.
            if (_duelNeedsHouseCounterparty)
                await _messageBus.PublishEventAsync(new DuelCreatorBetPlacedEvent(request.GameId));
```

Add the fields beside the existing ones:

```csharp
        private readonly IDuelHouseCoverageService _duelCoverage;
        // The duel coverage lock (and its transaction) while a duel bet holds it. Released by
        // RefundInvalidBet before it writes anything, exactly as _coverageLock is.
        private IDuelCoverageLock? _duelLock;
        // Set when this bet BOUND a House round as its creator — the one case that needs a house bet.
        private bool _duelNeedsHouseCounterparty;
```

Inject `IDuelHouseCoverageService duelCoverage` into the constructor and assign it.

Add the method (place it directly after `RegisterHouseEdgeBet`):

```csharp
        /// <summary>
        /// #184 §6.3 — a duel bet, under the duel advisory lock. Parallel to RegisterHouseEdgeBet and
        /// holding the same discipline.
        ///
        /// Under the lock, in this order:
        ///   1. replay re-check (paid bets)
        ///   2. the round is still WaitingBets, is a duel, and is not already full
        ///   3. CREATOR (OwnerPlayerId IS NULL)  -> bind owner + stake
        ///      JOINER  (OwnerPlayerId IS NOT NULL, ClaimedByPlayerId IS NULL) -> claim the room
        ///      0 rows affected -> RefundInvalidBet(ChoiceAlreadyTaken)   [AC 8]
        ///   4. coverage — HOUSE rounds only, on the CREATOR's paid bet (§6.5)
        ///   5. insert the bet
        ///   6. commit, then publish the deferred bet notification
        ///
        /// ⚠️ "0 rows affected" on the claim IS the whole join race: whoever writes first plays, the loser
        /// is refunded on the shipped RefundInvalidBet path and sees the EXISTING, vague
        /// ChoiceAlreadyTaken — which already carries the refund notice the SPA renders. No new message.
        ///
        /// ⚠️ OwnerPlayerId &lt;&gt; @p on the claim stops a creator joining their own room, which would
        /// otherwise be a self-duel that always pays them back minus the cut.
        ///
        /// ⚠️ FAIL CLOSED, REFUNDED — the player has already paid, so nothing from here may end in a bare
        /// 500 that keeps the stake. Only a validation refusal (ControlledException) passes through.
        /// </summary>
        private async Task RegisterDuelBet(CancellationToken cancellationToken)
        {
            try
            {
                string? refusal = null;
                bool isCreator = false;
                DuelCoverageDecision? decision = null;

                try
                {
                    _duelLock = await _duelCoverage.AcquireLockAsync(cancellationToken);

                    // The reuse guard ran before the lock, so two requests carrying the SAME signature can
                    // both have passed it. Under the lock the first one's bet is committed and visible: the
                    // second is a replay, refused without a refund. A free bet has no signature.
                    if (!_isFreeBet && await PaymentSignatureHasBeenUsed(_bet.Signature))
                    {
                        await ReleaseDuelLockAsync();
                        Messaging.ReturnValidationFailureMessage(ErrorMessages.PaymentSignatureHasBeenUsed);
                    }

                    (refusal, isCreator) = await BindOrClaimDuelRound(cancellationToken);

                    // Coverage is a HOUSE-round concern, on the CREATOR's PAID bet only: a PVP room needs
                    // no house wallet, the house's own bet is the transfer this rule was protecting, and a
                    // free bet moves no money at all.
                    if (refusal == null && isCreator && !_isFreeBet
                        && _bet.Game!.DuelKind == DuelKind.House)
                    {
                        decision = await _duelCoverage.EvaluateAsync(
                            _bet.Game.Parameters, _bet.Amount, cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is not ControlledException && !_refundRequested)
                {
                    Log.Error(ex, "DUEL — bet {BetId} (signature {Signature}) on game {GameId} failed while taking the lock or binding the round; refusing and refunding.",
                        _bet.Id, _bet.Signature, _bet.GameId);
                    await RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance);
                    return;
                }

                if (refusal != null)
                    await RefundInvalidBet(refusal);

                // FAIL CLOSED: a paid creator bet on a House round goes on only with a decision that says
                // coverable — a missing decision is not one.
                if (isCreator && !_isFreeBet && _bet.Game!.DuelKind == DuelKind.House
                    && decision?.IsCoverable != true)
                {
                    Log.Error("DUEL — bet {BetId} on game {GameId} refused and refunded: the HouseSeed wallet {Balance} cannot cover the {Amount} counterparty stake (spendable {Spendable}, pending {Pending}; no decision: {NoDecision}).",
                        _bet.Id, _bet.GameId, decision?.HouseSeedBalance, _bet.Amount,
                        decision?.Spendable, decision?.PendingTotal, decision == null);
                    await RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance);
                }

                try
                {
                    await RegisterBet(deferNotification: true);
                }
                catch (Exception ex) when (ex is not ControlledException && !_refundRequested)
                {
                    // Nothing is committed yet: releasing the lock rolls the bind/claim AND the insert back.
                    Log.Error(ex, "DUEL — bet {BetId} (signature {Signature}) failed before its commit; refunding.", _bet.Id, _bet.Signature);
                    await RefundInvalidBet(ErrorMessages.FailedToRegisterBet);
                }

                try
                {
                    await _duelLock!.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await HandleFailedDuelCommit(ex);
                }

                await ReleaseDuelLockAsync();
                await _gameService.PublishBetNotification(_bet);

                // Only now, with the bind committed, is it true that this round needs a house bet.
                _duelNeedsHouseCounterparty = isCreator && _bet.Game!.DuelKind == DuelKind.House;
            }
            finally
            {
                await ReleaseDuelLockAsync();
            }
        }

        /// <summary>
        /// Binds the round to its creator (owner + stake) or claims it for the joiner, with a CONDITIONAL
        /// UPDATE inside the lock's transaction — a failed insert or commit rolls it back.
        ///
        /// ⚠️ Raw SQL, not a tracked update: "first write wins" has to be the DATABASE's decision, and a
        /// load-mutate-save would read a row that another request is about to change.
        /// </summary>
        /// <returns>(refusal message or null, whether this bet is the CREATOR's). The CALLER refunds.</returns>
        private async Task<(string? Refusal, bool IsCreator)> BindOrClaimDuelRound(CancellationToken cancellationToken)
        {
            var round = await _spacestrokerDbContext.Games
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == _bet.GameId, cancellationToken);

            if (round == null || round.DuelKind == null || round.Status != GameStatus.WaitingBets)
                return (ErrorMessages.GameNotValidForBets, false);

            // Already matched: a duel has exactly two sides.
            if (round.ClaimedByPlayerId != null)
                return (ErrorMessages.ChoiceAlreadyTaken, false);

            var isCreator = round.OwnerPlayerId == null;

            var affected = isCreator
                ? await _spacestrokerDbContext.Database.ExecuteSqlRawAsync(
                    @"UPDATE public.""Games""
                         SET ""OwnerPlayerId"" = @playerId,
                             ""Stake"" = @stake,
                             ""UpdatedDate"" = @updatedDate
                       WHERE ""Id"" = @id AND ""Status"" = @waitingBets AND ""OwnerPlayerId"" IS NULL",
                    new Npgsql.NpgsqlParameter("@playerId", (object?)_bet.PlayerId ?? DBNull.Value),
                    new Npgsql.NpgsqlParameter("@stake", _bet.Amount),
                    new Npgsql.NpgsqlParameter("@updatedDate", DateTime.UtcNow),
                    new Npgsql.NpgsqlParameter("@id", _bet.GameId),
                    new Npgsql.NpgsqlParameter("@waitingBets", (int)GameStatus.WaitingBets))
                : await _spacestrokerDbContext.Database.ExecuteSqlRawAsync(
                    @"UPDATE public.""Games""
                         SET ""ClaimedByPlayerId"" = @playerId,
                             ""UpdatedDate"" = @updatedDate
                       WHERE ""Id"" = @id AND ""Status"" = @waitingBets
                         AND ""ClaimedByPlayerId"" IS NULL AND ""OwnerPlayerId"" <> @playerId",
                    new Npgsql.NpgsqlParameter("@playerId", (object?)_bet.PlayerId ?? DBNull.Value),
                    new Npgsql.NpgsqlParameter("@updatedDate", DateTime.UtcNow),
                    new Npgsql.NpgsqlParameter("@id", _bet.GameId),
                    new Npgsql.NpgsqlParameter("@waitingBets", (int)GameStatus.WaitingBets));

            // ⚠️ THE JOIN RACE, IN ONE LINE (AC 8). Whoever wrote first plays; the loser takes the shipped
            // refund path with the EXISTING vague message, which already carries the refund notice.
            // It is also what refuses a creator joining their own room (OwnerPlayerId <> @playerId).
            if (affected == 0)
                return (ErrorMessages.ChoiceAlreadyTaken, isCreator);

            // The in-memory instance must agree with the row the rest of the handler reads.
            _bet.Game!.DuelKind = round.DuelKind;
            if (isCreator)
                _bet.Game.Stake = _bet.Amount;

            return (null, isCreator);
        }

        /// <summary>
        /// A commit that throws is AMBIGUOUS. Same shape, and same reasoning, as
        /// HandleFailedHouseEdgeCommit: look before refunding, from a FRESH scope.
        /// </summary>
        private async Task HandleFailedDuelCommit(Exception commitError)
        {
            await ReleaseDuelLockAsync();

            bool betExists;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fresh = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
                betExists = await fresh.GameBets.AsNoTracking().AnyAsync(x => x.Id == _bet.Id);
            }
            catch (Exception checkError)
            {
                Log.Error(checkError, "DUEL — commit for bet {BetId} (signature {Signature}) failed and the bet row could not be checked. NOT refunded automatically; needs manual analysis.",
                    _bet.Id, _bet.Signature);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(commitError).Throw();
                return;
            }

            if (betExists)
            {
                Log.Warning(commitError, "DUEL — commit for bet {BetId} reported a failure but the bet row exists; treating it as accepted.", _bet.Id);
                return;
            }

            Log.Error(commitError, "DUEL — commit for bet {BetId} failed; the bet was not stored and is being refunded.", _bet.Id);
            await RefundInvalidBet(ErrorMessages.FailedToRegisterBet);
        }

        /// <summary>Rolls back (if not committed) and releases the duel lock. Idempotent.</summary>
        private async Task ReleaseDuelLockAsync()
        {
            var held = _duelLock;
            _duelLock = null;

            if (held == null)
                return;

            try
            {
                await held.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DUEL — releasing the coverage lock for bet {BetId} threw.", _bet.Id);
            }
        }
```

Finally, in `RefundInvalidBet`, release the duel lock alongside the house-edge one — change the first line from `await ReleaseCoverageLockAsync();` to:

```csharp
            // Out of BOTH coverage transactions FIRST: the refund claim below must never be written inside
            // a transaction that is about to roll back.
            await ReleaseCoverageLockAsync();
            await ReleaseDuelLockAsync();
```

- [ ] **Step 5: Confirm the shipped free-text rules still apply**

No change needed, but verify by reading `ValidateBetParameters`: on a duel type `PresetChoices` is empty, so (a) `_bet.ExclusiveChoice = _bet.Choice` (the unique index refuses a joiner copying the creator's word — AC 11), (b) the preset one-bet-per-round rule is skipped, (c) the free-text exclusivity check runs, (d) the `OrbitChoiceClaim` query runs and never fires (`AllowOrbit = false`). `SecondBetNeedsAnotherPlayer` is harmless — the creator's second bet is already refused by `OwnerPlayerId <> @p`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~RegisterBetHandlerTests"`
Expected: PASS — all new duel tests plus every pre-existing pari-mutuel and house-edge test.

- [ ] **Step 7: Run the full suite**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Bind and claim a duel round under its own lock, first write wins."
git push origin feature/monkey-duel
```

---

## Task 11: `DuelHouseBetService` — the house counterparty bet

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Duel/IDuelHouseBetService.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Duel/DuelHouseBetService.cs`
- Create: `src/SpaceStroker.Infrastructure/Consumers/DuelHouseBet/DuelHouseBetConsumer.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (register consumer + its own receive endpoint + the service)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/DuelHouseBetServiceTests.cs`

**Interfaces:**
- Consumes: `DuelHouseWordGenerator.DeriveWord` (Task 2); `IHouseSeedAccountResolver`, `ISolanaService.TransferTokensWithMemo`/`GetSolBalance`, `OrbitChoiceGenerator.DeriveSeed`, `RegisterBetRequest` (all existing).
- Produces: `Task IDuelHouseBetService.PlaceCounterpartyBet(Guid gameId)`.

Model it **line-for-line on `HouseSeedBetService`** — that is the shipped answer to "the house places an ordinary paid bet".

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Services/DuelHouseBetServiceTests.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Handlers.Games.RegisterBet;
using SpaceStroker.Infrastructure.Services.Duel;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Services;

/// <summary>
/// #184 §6.4 — the house's side of a VS HOUSE duel: an ORDINARY paid bet from the HouseSeed wallet,
/// on a word DRAWN from the round's committed seed. Modelled on HouseSeedBetServiceTests.
/// </summary>
public class DuelHouseBetServiceTests
{
    private const string HouseWallet = "MOCK_HOUSE_SEED_PUBLIC_KEY";
    private const string Seed = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    [Fact]
    public async Task PlacesOneOrdinaryHouseBet_WithTheDuelMemo_AndNeverFundedByOrbit()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0.125m, creatorWord: "banana");

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        var body = harness.RegisteredBody.ShouldNotBeNull();
        body.Amount.ShouldBe(0.125m);                       // matches the ROUND's stake, exactly
        body.Wallet.ShouldBe(HouseWallet);
        body.PaymentSignature.ShouldNotBeNullOrWhiteSpace();
        // ⚠️ NO FundedByOrbit: the house wallet is BOTH the transfer's sender and the bet's Wallet, so
        // ValidateBetPayment passes untouched. Setting it would discard a real control and route the win
        // into player custody.
        harness.RegisteredRequest!.FundedByOrbit.ShouldBeFalse();
        // ⚠️ The memo is load-bearing: without it two rounds' identical transfers can share ONE
        // deterministic signature and the second is refused as a replay. Orbit lost a bet to exactly this.
        harness.TransferMemo.ShouldBe($"duel:{harness.GameId:N}");
        // The word is DRAWN, not chosen, and is reproducible offline from the revealed seed.
        body.Choice.ShouldBe(DuelHouseWordGenerator.DeriveWord(Seed, harness.GameId, 0, harness.Pool));
        body.Seed.ShouldBe(OrbitChoiceGenerator.DeriveSeed(Seed, harness.HousePlayerId));
        body.Nonce.ShouldBe(0);
    }

    [Fact]
    public async Task SkipsAWordTheCreatorAlreadyTook_AndUsesTheNextAttempt()
    {
        // Free-text exclusivity would refuse the house's bet AFTER the transfer, so the collision is
        // resolved BEFORE signing — by advancing the attempt, which keeps the draw reproducible.
        var attemptZero = DuelHouseWordGenerator.DeriveWord(Seed, DuelHouseBetHarness.FixedGameId, 0, DuelHouseBetHarness.FixedPool);
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0.125m, creatorWord: attemptZero, fixedGameId: true);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.RegisteredBody!.Choice.ShouldBe(
            DuelHouseWordGenerator.DeriveWord(Seed, harness.GameId, 1, harness.Pool));
        harness.RegisteredBody.Choice.ShouldNotBe(attemptZero);
    }

    /// <summary>Review Focus 5 — MassTransit is at-least-once. A redelivery must not sign twice.</summary>
    [Fact]
    public async Task ARedeliveredEvent_PlacesNoSecondBetAndSignsNoSecondTransfer()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0.125m, creatorWord: "banana");
        await harness.Service.PlaceCounterpartyBet(harness.GameId);
        harness.SimulateBetLanded();

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(1);
        harness.RegisterCount.ShouldBe(1);
    }

    [Fact]
    public async Task ABalanceBelowTheFloor_SignsNothingAndLeavesTheRoundUnmatched()
    {
        // Subtracting the stake FIRST matters with native SOL: a wallet holding exactly the floor would
        // otherwise pass and then spend the floor on the bet, leaving nothing for the fee.
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0.125m, creatorWord: "banana", balance: 0.1m);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(0);
        harness.Round().ClaimedByPlayerId.ShouldBeNull();
    }

    [Fact]
    public async Task ANullBalanceRead_SignsNothing()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0.125m, creatorWord: "banana", balance: null);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(0);
    }

    /// <summary>§3.1 S7 — outside production a FREE creator bet gets a FREE house bet: no money at all.</summary>
    [Fact]
    public async Task AFreeCreatorBet_GetsAZeroAmountHouseBetWithNoTransfer()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(stake: 0m, creatorWord: "banana", balance: 0m);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.RegisteredBody.ShouldNotBeNull();
        harness.RegisteredBody!.Amount.ShouldBe(0m);
        harness.RegisteredBody.PaymentSignature.ShouldBeNullOrWhiteSpace();
        harness.TransferCount.ShouldBe(0);
        harness.BalanceReads.ShouldBe(0);
    }

    [Fact]
    public async Task SkipsAPvpRoom()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(0.125m, "banana", kind: DuelKind.OpenRoom);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(0);
        harness.RegisterCount.ShouldBe(0);
    }

    [Fact]
    public async Task SkipsARoundThatIsNoLongerAcceptingBets()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(0.125m, "banana", status: GameStatus.InProgress);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(0);
    }

    [Fact]
    public async Task SkipsWhenTheHouseSeedWalletIsNotConfigured()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(0.125m, "banana", configured: false);

        await harness.Service.PlaceCounterpartyBet(harness.GameId);

        harness.TransferCount.ShouldBe(0);
    }

    /// <summary>A transfer that landed is NEVER re-sent: the stranded stake is logged for manual refund.</summary>
    [Fact]
    public async Task ARegistrationThatThrowsAfterTheTransfer_LogsAnOrphanPaymentAndDoesNotRetry()
    {
        var harness = DuelHouseBetHarness.WithCreatorBet(0.125m, "banana", registerThrows: true);

        await Should.NotThrowAsync(() => harness.Service.PlaceCounterpartyBet(harness.GameId));

        harness.TransferCount.ShouldBe(1);
        harness.LoggedOrphanPayment.ShouldBeTrue();
    }
}
```

⚠️ `DuelHouseBetHarness` is a **new** helper (`src/SpaceStroker.Tests/UnitTests/Services/DuelHouseBetHarness.cs`). Build it by copying `HouseSeedBetServiceTests`' Moq setup verbatim (`IHouseSeedAccountResolver`, `ISolanaService`, `IMediator`, `IGameService`, `GeneralSettings`, in-memory context) and exposing: `Service`, `GameId`, `HousePlayerId`, `Pool`, `RegisteredRequest`/`RegisteredBody` (captured from `IMediator.Send`), `TransferMemo`, `TransferCount`, `RegisterCount`, `BalanceReads`, `LoggedOrphanPayment` (assert via the Serilog in-memory sink the suite already uses, or a captured `Log.Error` double), `Round()`, `SimulateBetLanded()`, plus `static Guid FixedGameId` / `static string[] FixedPool` for the deterministic attempt-1 test.

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelHouseBetServiceTests"`
Expected: FAIL — `IDuelHouseBetService` does not exist.

- [ ] **Step 3: Write the interface**

`src/SpaceStroker.Infrastructure/Services/Duel/IDuelHouseBetService.cs`:

```csharp
namespace SpaceStroker.Infrastructure.Services.Duel
{
    /// <summary>
    /// Places the house's side of a VS HOUSE duel (#184 §6.4).
    ///
    /// Deliberately separate from DuelHouseBetConsumer, which owns nothing but the message plumbing —
    /// the HouseSeedBetService precedent, so the Testcontainers harness can drive the placement path
    /// directly without changing the host's environment.
    /// </summary>
    public interface IDuelHouseBetService
    {
        Task PlaceCounterpartyBet(Guid gameId);
    }
}
```

- [ ] **Step 4: Write the service**

`src/SpaceStroker.Infrastructure/Services/Duel/DuelHouseBetService.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Domain.Orbit.Services;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Handlers.Games.RegisterBet;
using SpaceStroker.Infrastructure.Services.HouseSeed;
using SpaceStroker.Infrastructure.Services.Solana;

namespace SpaceStroker.Infrastructure.Services.Duel
{
    /// <summary>
    /// The house's side of a VS HOUSE duel (#184 §6.4) — one ORDINARY paid bet from the HouseSeed wallet,
    /// on a word DRAWN from the round's committed seed.
    ///
    /// ⚠️ The bet is ORDINARY in every respect: no marker, no column, no payment-validation bypass, no
    /// payout redirect. That works because the house wallet is BOTH the transfer's sender AND the bet's
    /// own Wallet, so SolanaService.ValidateBetPayment passes untouched — exactly as HouseSeedBetService
    /// documents. Do NOT reach for Bet.FundedByOrbit here.
    ///
    /// ⚠️ It funds from HouseSeed, NOT the treasury (decision 7), deliberately: TokenSettings keeps them
    /// separate so a duel-maths bug cannot drain the account that pays every other game's winners.
    ///
    /// ⚠️ Placing this bet is what CLAIMS the round (RegisterDuelBet runs for it too, as the joiner), and
    /// its BetPlacedEvent is what starts the round. A failure leaves the round WaitingBets with one bet
    /// on it — the creator's stake is not lost: the 12h NeedsRefunding sweep or a cancel returns it.
    ///
    /// The order of operations is the design, and it is deliberately paranoid: every step after the
    /// transfer is money already gone. Runs in Consumers because it moves money (constraints.md §8).
    /// </summary>
    public class DuelHouseBetService : IDuelHouseBetService
    {
        public DuelHouseBetService(
            SpaceStrokerDbContext context,
            IHouseSeedAccountResolver accounts,
            ISolanaService solana,
            IMediator mediator,
            GeneralSettings settings)
        {
            _context = context;
            _accounts = accounts;
            _solana = solana;
            _mediator = mediator;
            _settings = settings;
        }

        private readonly SpaceStrokerDbContext _context;
        private readonly IHouseSeedAccountResolver _accounts;
        private readonly ISolanaService _solana;
        private readonly IMediator _mediator;
        private readonly GeneralSettings _settings;

        /// <summary>
        /// How many derived words may collide with the creator's before the round is left unmatched.
        /// Bounded because the alternative — spending a transfer on a word exclusivity will reject —
        /// leaves the stake in NeedsRefunding until the 12h sweep. Same constant the seeder uses.
        /// </summary>
        private const int MaxWordAttempts = 10;

        private HouseSeedSettings Config => _settings.Operator.Token.HouseSeed;

        public async Task PlaceCounterpartyBet(Guid gameId)
        {
            if (!_accounts.IsConfigured)
            {
                Log.Information("DUEL HOUSE — the HouseSeed wallet is not configured; skipping game {GameId}.", gameId);
                return;
            }

            var game = await LoadGame(gameId);

            if (game == null || game.DuelKind != DuelKind.House)
            {
                Log.Information("DUEL HOUSE — game {GameId} is not a house duel; skipping.", gameId);
                return;
            }

            if (!Game.GameAcceptingBets.Contains(game.Status) || game.ClaimedByPlayerId != null)
            {
                Log.Information("DUEL HOUSE — game {GameId} is no longer unmatched and accepting bets; skipping.", gameId);
                return;
            }

            if (game.Stake is not decimal stake)
            {
                Log.Information("DUEL HOUSE — game {GameId} has no bound stake yet; skipping.", gameId);
                return;
            }

            if (game.ServerSeed == null)
            {
                Log.Error("DUEL HOUSE — game {GameId} has no committed server seed; skipping.", gameId);
                return;
            }

            // The Player row must exist BEFORE anything is derived: the bet's client seed keys on a Guid
            // playerId, and it is also the idempotency key below.
            var housePlayer = await ResolveHousePlayer();

            // ⚠️ REVIEW FOCUS 5 — checked BEFORE signing. MassTransit is at-least-once, and a redelivery
            // that signs again spends a second stake the house can never recover. RegisterDuelBet would
            // refuse the duplicate, but only AFTER the transfer.
            if (await _context.GameBets.AnyAsync(x => x.GameId == game.Id && x.PlayerId == housePlayer.Id))
            {
                Log.Information("DUEL HOUSE — game {GameId} already has a house bet; skipping.", gameId);
                return;
            }

            var isFree = stake == 0m;

            // ⚠️ §3.1 S7 — outside production a FREE creator bet gets a FREE house bet: no transfer, no
            // balance read, no funded wallet needed. AllowFreeBet self-heals to !IsProduction(), so this
            // branch cannot reach real money; without it the house tab is untestable on the local stack.
            if (!isFree)
            {
                // ⚠️ THE STAKE IS SUBTRACTED FIRST. With native SOL the stake IS lamports out of the same
                // balance that pays the fee, so a wallet holding exactly the floor would pass a bare
                // "balance >= floor" and then spend the floor on the bet. See specs/sol-settlement.md §6.6.
                // Null is UNKNOWN, not empty: give up without spending.
                var sol = await _solana.GetSolBalance(_accounts.PublicKey);
                var stakeInSol = _accounts.ToDescriptor(includePrivateKey: false).IsNative ? stake : 0m;

                if (sol is not { } balance || balance - stakeInSol < Config.MinimumSolBalance)
                {
                    Log.Error("DUEL HOUSE — the HouseSeed wallet cannot cover the {Stake} counterparty stake plus its fee floor (balance {Balance}); leaving game {GameId} unmatched.",
                        stakeInSol, sol, gameId);
                    return;
                }
            }

            var word = await DeriveAvailableWord(game);

            if (word == null)
            {
                Log.Warning("DUEL HOUSE — no free word found in {Attempts} attempts for game {GameId}.", MaxWordAttempts, gameId);
                return;
            }

            await PlaceBet(game, housePlayer, word, stake, isFree);
        }

        private Task<Game?> LoadGame(Guid gameId)
            => _context.Games
                .Include(x => x.Parameters).ThenInclude(x => x.PresetChoices)
                .Include(x => x.Parameters).ThenInclude(x => x.LotteryParameters)
                .FirstOrDefaultAsync(x => x.Id == gameId);

        /// <summary>The house wallet's Player row — resolved or created. Verbatim from HouseSeedBetService.</summary>
        private async Task<Player> ResolveHousePlayer()
        {
            var wallet = _accounts.PublicKey;
            var existing = await _context.Players.FirstOrDefaultAsync(x => x.ExternalPlayerId == wallet);

            if (existing != null)
                return existing;

            var player = new Player
            {
                ExternalPlayerId = wallet,
                DisplayName = _accounts.DisplayName,
                LastSeenAt = DateTime.UtcNow,
            };

            await _context.Players.AddAsync(player);
            await _context.SaveChangesAsync();

            return player;
        }

        /// <summary>
        /// The first derived word that is not already bet in this round, or null when the attempts run out.
        ///
        /// ⚠️ The creator picked first, so attempt 0 can collide with their word — and free-text exclusivity
        /// would refuse the house's bet AFTER the transfer. Advancing the ATTEMPT (rather than re-rolling)
        /// keeps the whole sequence reproducible offline from the revealed seed.
        ///
        /// No OrbitChoiceClaim check: a duel type is AllowOrbit = false (decision 18), so no claim can
        /// exist for it — which is precisely why the draw stays reproducible with no extra persisted state.
        /// </summary>
        private async Task<string?> DeriveAvailableWord(Game game)
        {
            var pool = (game.DuelPoolSnapshot ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (pool.Count == 0)
            {
                Log.Error("DUEL HOUSE — game {GameId} has no pool snapshot; skipping.", game.Id);
                return null;
            }

            for (var attempt = 0; attempt < MaxWordAttempts; attempt++)
            {
                var candidate = DuelHouseWordGenerator.DeriveWord(game.ServerSeed!, game.Id, attempt, pool);

                var takenThisRound = await _context.GameBets
                    .AnyAsync(x => x.GameId == game.Id && x.Choice == candidate);

                if (!takenThisRound)
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Transfer (when paid), then register through the ORDINARY MediatR path — so exclusivity, the
        /// round claim, the TransactionLog and the bet-notification feed all behave exactly as for a human.
        ///
        /// ⚠️ Once a signature exists the transfer is NEVER re-sent. A reused signature is refused as
        /// PaymentSignatureHasBeenUsed, so the worst case is a rejection — never a double stake.
        /// </summary>
        private async Task PlaceBet(Game game, Player housePlayer, string word, decimal stake, bool isFree)
        {
            string? signature = null;

            if (!isFree)
            {
                var rawAmount = (ulong)(stake * (decimal)Math.Pow(10, _accounts.Decimals));

                // ⚠️ The memo is load-bearing, not annotation: two rounds' stakes are otherwise identical in
                // sender, receiver, amount and fee payer, and a Solana signature is deterministic over the
                // message — so they could share ONE signature and the second would be refused as a replay.
                // Orbit lost a player's bet to exactly this.
                signature = await _solana.TransferTokensWithMemo(
                    _settings.Operator.Token.TokenOwnerPublicKey,
                    new BetTransactionAmountResult(stake, rawAmount),
                    _accounts.ToDescriptor(includePrivateKey: true),
                    $"duel:{game.Id:N}");

                if (string.IsNullOrWhiteSpace(signature))
                {
                    // No money moved, so there is nothing to compensate.
                    Log.Error("DUEL HOUSE — the counterparty transfer did not land for game {GameId}.", game.Id);
                    return;
                }
            }

            var body = new RegisterBetRequestBody
            {
                Amount = stake,
                Choice = word,
                // Derived, not random: recomputable offline once the seed is revealed. Nonce 0 because
                // there is no client-side counter to advance.
                Seed = OrbitChoiceGenerator.DeriveSeed(game.ServerSeed!, housePlayer.Id),
                Nonce = 0,
                Wallet = _accounts.PublicKey,
                DisplayName = _accounts.DisplayName,
                PaymentSignature = signature,
            };

            try
            {
                await _mediator.Send(new RegisterBetRequest(game.Id, body));

                Log.Information("DUEL HOUSE — placed {Stake} on {Word} in game {GameId} (signature {Signature}).",
                    stake, word, game.Id, signature);
            }
            catch (Exception ex)
            {
                // A free bet moved nothing, so there is no orphan to reconcile.
                if (isFree)
                {
                    Log.Warning(ex, "DUEL HOUSE — the free counterparty bet for game {GameId} was refused.", game.Id);
                    return;
                }

                // The stake is already on chain and the transfer is never re-sent. Log everything the
                // stranded stake needs to be reconciled by hand via POST /admin/bets/refund.
                Log.Error(ex,
                    "DUEL HOUSE ORPHAN PAYMENT — signature={Signature} wallet={PublicKey} game={GameId} amount={Amount}",
                    signature, _accounts.PublicKey, game.Id, stake);
            }
        }
    }
}
```

- [ ] **Step 5: Write the consumer**

`src/SpaceStroker.Infrastructure/Consumers/DuelHouseBet/DuelHouseBetConsumer.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SpaceStroker.Common.Abstractions;
using SpaceStroker.Infrastructure.Services.Duel;

namespace SpaceStroker.Infrastructure.Consumers.DuelHouseBet
{
    /// <summary>
    /// Places the house's side of a VS HOUSE duel once the creator's bet has committed (#184 §6.4).
    ///
    /// Deliberately thin: every decision lives in IDuelHouseBetService, which is what keeps the placement
    /// path drivable by the Testcontainers harness.
    ///
    /// ⚠️ NO production gate, unlike HouseSeedBetConsumer. The house seed bet is a production-only
    /// product decision; a duel's counterparty IS the game — without it the VS HOUSE tab does nothing at
    /// all, and outside production it runs the free path (§3.1 S7) rather than moving money.
    ///
    /// ⚠️ Its OWN receive endpoint: it signs an on-chain transfer, and sharing an endpoint would serialise
    /// that behind (or in front of) unrelated consumers on the same round.
    /// </summary>
    public class DuelHouseBetConsumer : BaseConsumer<DuelCreatorBetPlacedEvent>
    {
        public DuelHouseBetConsumer(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory) { }

        public override async Task ProcessMessage(DuelCreatorBetPlacedEvent message)
        {
            using var scope = ServiceScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDuelHouseBetService>();

            await service.PlaceCounterpartyBet(message.GameId);
        }
    }
}
```

- [ ] **Step 6: Wire it in Consumers**

In `src/SpaceStroker.Consumers/Program.cs`: register the service beside `IHouseSeedBetService`, add `x.AddConsumer<DuelHouseBetConsumer>();` with the other `AddConsumer` calls, and add its endpoint beside `HouseSeedBetConsumer`'s:

```csharp
        // The house's side of a VS HOUSE duel (#184). ⚠️ Its OWN endpoint: it signs an on-chain transfer,
        // and must not queue behind — or in front of — anything else on the same round.
        cfg.ReceiveEndpoint(nameof(DuelHouseBetConsumer), e =>
        {
            e.ConfigureConsumer<DuelHouseBetConsumer>(context);
        });
```

⚠️ `IDuelHouseBetService` is registered **only in Consumers** (it moves money — `constraints.md` §8). The API must not resolve it.

- [ ] **Step 7: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Tests
git commit -m "Place the house's duel counterparty bet from the HouseSeed wallet."
git push origin feature/monkey-duel
```

---

## Task 12: Cancelling a room — `POST /games/rounds/{roundId}/duel-cancel`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/CancelDuelRoom/CancelDuelRoomRequest.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/CancelDuelRoom/CancelDuelRoomHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` + `RateLimitSettingsTests`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/CancelDuelRoomHandlerTests.cs`

**Interfaces:**
- Consumes: `WalletProof.TryConsume` (existing); `RefundInvalidBetEvent` (existing); `Game.DuelKind/ClaimedByPlayerId/OwnerPlayerId` (Task 1).
- Produces: `WalletNoncePurpose.DuelCancel = "duel-cancel"`; `record CancelDuelRoomRequest(Guid RoundId, CancelDuelRoomRequestBody Body) : IRequest<Unit>`; `class CancelDuelRoomRequestBody { string? Wallet; string? Nonce; string? Signature; }`.

**Decision recorded 2026-09-24:** the owner chose to **keep the wallet proof** (spec §3.1 S6's recommendation) over the unauthenticated `?wallet=` alternative, because without a signature "only the creator can cancel" is not enforceable — the creator's wallet is public on the board, so a script could cancel every room as it appears.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Handlers/CancelDuelRoomHandlerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SpaceStroker.Common.Resources;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Consumers.RefundBet;
using SpaceStroker.Infrastructure.Handlers.Games.CancelDuelRoom;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Handlers;

/// <summary>
/// #184 §6.6 / AC 9 — the creator cancels an unmatched room and gets their full stake back.
///
/// ⚠️ Every failure — bad proof, unknown round, not the owner, already claimed — returns the SAME vague
/// message (constraints.md §6). A caller must not be able to tell "not yours" from "already taken".
/// </summary>
public class CancelDuelRoomHandlerTests
{
    [Fact]
    public async Task ValidProofOnAnUnmatchedRoom_CancelsItAndPublishesTheRefund()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);

        await harness.CancelAsync(harness.OwnerWallet, validProof: true);

        var round = harness.Round();
        round.Status.ShouldBe(GameStatus.NotEnoughPlayersToStart);
        round.EndDate.ShouldNotBeNull();

        var refund = harness.PublishedEvents.OfType<RefundInvalidBetEvent>().ShouldHaveSingleItem();
        refund.BetId.ShouldBe(harness.CreatorBetId);
        refund.Wallet.ShouldBe(harness.OwnerWallet);
        refund.Value.ShouldBe(0.125m);
    }

    [Fact]
    public async Task ItLogsTheStatusTransition()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);

        await harness.CancelAsync(harness.OwnerWallet, validProof: true);

        harness.StatusLogs().ShouldContain(x =>
            x.PreviousStatus == GameStatus.WaitingBets && x.NewStatus == GameStatus.NotEnoughPlayersToStart);
    }

    [Fact]
    public async Task AClaimedRoomCannotBeCancelled()
    {
        var harness = CancelDuelHarness.ClaimedRoom(stake: 0.125m);

        var refusal = await harness.CancelExpectingRefusalAsync(harness.OwnerWallet, validProof: true);

        refusal.ShouldBe(ErrorMessages.GameNotValidForBets);
        harness.Round().Status.ShouldBe(GameStatus.WaitingBets);
        harness.PublishedEvents.OfType<RefundInvalidBetEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task SomebodyElseCannotCancelYourRoom()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);

        var refusal = await harness.CancelExpectingRefusalAsync("someone-else", validProof: true);

        refusal.ShouldBe(ErrorMessages.GameNotValidForBets);
        harness.Round().Status.ShouldBe(GameStatus.WaitingBets);
        harness.PublishedEvents.OfType<RefundInvalidBetEvent>().ShouldBeEmpty();
    }

    /// <summary>The proof is what makes "only the creator" true — the wallet alone is public.</summary>
    [Fact]
    public async Task ABadProofIsRefusedBeforeAnythingIsRead()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);

        var refusal = await harness.CancelExpectingRefusalAsync(harness.OwnerWallet, validProof: false);

        refusal.ShouldBe(ErrorMessages.GameNotValidForBets);
        harness.Round().Status.ShouldBe(GameStatus.WaitingBets);
        harness.PublishedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownRoundGivesTheSameVagueRefusal()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);

        var refusal = await harness.CancelExpectingRefusalAsync(
            harness.OwnerWallet, validProof: true, roundId: Guid.NewGuid());

        refusal.ShouldBe(ErrorMessages.GameNotValidForBets);
    }

    [Fact]
    public async Task ANonDuelRoundCannotBeCancelledThisWay()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m, duelKind: null);

        var refusal = await harness.CancelExpectingRefusalAsync(harness.OwnerWallet, validProof: true);

        refusal.ShouldBe(ErrorMessages.GameNotValidForBets);
    }

    /// <summary>
    /// A HOUSE round whose counterparty bet never landed is cancellable on exactly the same terms — the
    /// creator's stake must not sit locked until the 12h sweep (§6.4).
    /// </summary>
    [Fact]
    public async Task AnUnmatchedHouseRoundIsCancellableToo()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m, duelKind: DuelKind.House);

        await harness.CancelAsync(harness.OwnerWallet, validProof: true);

        harness.Round().Status.ShouldBe(GameStatus.NotEnoughPlayersToStart);
    }

    /// <summary>Cancelling twice must publish ONE refund: the second finds no WaitingBets row.</summary>
    [Fact]
    public async Task CancellingTwicePublishesOneRefund()
    {
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0.125m);
        await harness.CancelAsync(harness.OwnerWallet, validProof: true);

        await harness.CancelExpectingRefusalAsync(harness.OwnerWallet, validProof: true);

        harness.PublishedEvents.OfType<RefundInvalidBetEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task AFreeRoomIsCancelledWithoutARefundEvent()
    {
        // Nothing was ever paid, so there is nothing to give back.
        var harness = CancelDuelHarness.UnmatchedRoom(stake: 0m);

        await harness.CancelAsync(harness.OwnerWallet, validProof: true);

        harness.Round().Status.ShouldBe(GameStatus.NotEnoughPlayersToStart);
        harness.PublishedEvents.OfType<RefundInvalidBetEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void TheDuelCancelPurposeIsKnown()
    {
        WalletNoncePurpose.IsKnown(WalletNoncePurpose.DuelCancel).ShouldBeTrue();
        WalletNoncePurpose.DuelCancel.ShouldBe("duel-cancel");
    }
}
```

⚠️ `CancelDuelHarness` is a **new** helper (`src/SpaceStroker.Tests/UnitTests/Handlers/CancelDuelHarness.cs`). Copy the `ICacheService` + `IMessaging` + `IMessageBus` Moq setup from the existing `SetSelfExclusionHandlerTests` (the shipped `WalletProof.TryConsume` precedent), seed a duel round with a creator bet, and expose `OwnerWallet`, `CreatorBetId`, `Round()`, `StatusLogs()`, `PublishedEvents`, `CancelAsync(wallet, validProof, roundId = null)` and `CancelExpectingRefusalAsync(...)`. `validProof: true` primes the nonce in the cache double and stubs `WalletSignature.IsValid` (or injects a signature verifier double if the shipped code allows one — check `SetSelfExclusionHandlerTests` for how it fakes the signature).

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~CancelDuelRoomHandlerTests"`
Expected: FAIL — `WalletNoncePurpose.DuelCancel` and `CancelDuelRoomHandler` do not exist.

- [ ] **Step 3: Add the nonce purpose**

In `GetWalletNonceRequest.cs`, after the referral purposes:

```csharp
        // #184 Monkey Duel: cancelling an open room triggers a REFUND, and "money never moves on an
        // unproven request" is the rule every other refund path follows. The refund can only ever reach
        // the owner's own wallet, so the exposure without it would be griefing rather than theft — but
        // the creator's wallet is printed on the public board, so an unauthenticated `?wallet=` would let
        // anyone cancel any room and keep the OPEN DUELS board permanently empty.
        // Owner decision 2026-09-24: keep the proof.
        public const string DuelCancel = "duel-cancel";
```

and extend `IsKnown`:

```csharp
        public static bool IsKnown(string? purpose)
            => purpose == SelfExclusion || purpose == Preferences
            || purpose == OrbitSession || purpose == OrbitCashOut
            || purpose == ReferralSession || purpose == ReferralCashOut
            || purpose == DuelCancel;
```

- [ ] **Step 4: Write the request and handler**

`CancelDuelRoomRequest.cs`:

```csharp
using MediatR;

namespace SpaceStroker.Infrastructure.Handlers.Games.CancelDuelRoom
{
    /// <summary>Cancel an unmatched duel room and refund its creator (specs/monkey-duel.md §6.6).</summary>
    public record CancelDuelRoomRequest(Guid RoundId, CancelDuelRoomRequestBody Body) : IRequest<Unit>;

    /// <summary><c>{ wallet, nonce, signature }</c> — the shipped wallet-proof shape.</summary>
    public class CancelDuelRoomRequestBody
    {
        public string? Wallet { get; set; }
        public string? Nonce { get; set; }
        public string? Signature { get; set; }
    }
}
```

`CancelDuelRoomHandler.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Common.Messaging;
using SpaceStroker.Common.Resources;
using SpaceStroker.Domain.Bets.Services;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Consumers.RefundBet;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Handlers.Players.GetWalletNonce;
using SpaceStroker.Infrastructure.Services.Cache;
using SpaceStroker.Infrastructure.Services.Games;
using SpaceStroker.Infrastructure.Services.Messaging;
using SpaceStroker.Infrastructure.Services.Wallets;

namespace SpaceStroker.Infrastructure.Handlers.Games.CancelDuelRoom
{
    /// <summary>
    /// #184 §6.6 — the creator withdraws an unmatched room and gets their full stake back (AC 9).
    ///
    /// ⚠️ EVERY failure returns the SAME vague GameNotValidForBets (constraints.md §6): a bad proof, an
    /// unknown round, a round that is not yours, and one that was already claimed must be
    /// indistinguishable.
    ///
    /// ⚠️ The conditional UPDATE is the SAME shape as the join claim, which is what resolves "the creator
    /// cancels while a join is in flight": whoever writes first wins, and the loser is refunded.
    ///
    /// ⚠️ NotEnoughPlayersToStart is the right terminal status: it already means "the round never ran and
    /// the stakes went back", it is NOT in HouseEdgeCoverage.UnsettledStatuses, and the 12h refund sweep
    /// does not re-select a bet that has already been refunded.
    ///
    /// ⚠️ NO auto-expiry anywhere (decision 15). Dead rooms accumulate; if the board gets noisy, expiry is
    /// a follow-up card.
    /// </summary>
    public class CancelDuelRoomHandler : BaseRequestHandler<CancelDuelRoomRequest, Unit>
    {
        private readonly ICacheService _cache;
        private readonly IMessageBus _messageBus;
        private readonly IGameService _gameService;

        public CancelDuelRoomHandler(SpaceStrokerDbContext context, IMessaging messaging,
            ICacheService cache, IMessageBus messageBus, IGameService gameService) : base(context, messaging)
        {
            _cache = cache;
            _messageBus = messageBus;
            _gameService = gameService;
        }

        public override async Task<Unit> HandleRequest(CancelDuelRoomRequest request, CancellationToken cancellationToken)
        {
            var wallet = request.Body?.Wallet ?? string.Empty;

            if (!WalletProof.TryConsume(_cache, WalletNoncePurpose.DuelCancel, wallet,
                    request.Body?.Nonce, request.Body?.Signature, BuildMessage))
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            var ownerId = await Context.Players
                .AsNoTracking()
                .Where(x => x.ExternalPlayerId == wallet)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (ownerId is null)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            var now = DateTime.UtcNow;

            // ⚠️ Raw SQL, not load-mutate-save: "first write wins" has to be the DATABASE's decision, so a
            // join landing at the same moment cannot both claim the room AND have it cancelled under it.
            var affected = await Context.Database.ExecuteSqlRawAsync(
                @"UPDATE public.""Games""
                     SET ""Status"" = @cancelled,
                         ""EndDate"" = @now,
                         ""UpdatedDate"" = @now
                   WHERE ""Id"" = @id AND ""Status"" = @waitingBets
                     AND ""DuelKind"" IS NOT NULL
                     AND ""ClaimedByPlayerId"" IS NULL
                     AND ""OwnerPlayerId"" = @owner",
                [
                    new Npgsql.NpgsqlParameter("@cancelled", (int)GameStatus.NotEnoughPlayersToStart),
                    new Npgsql.NpgsqlParameter("@now", now),
                    new Npgsql.NpgsqlParameter("@id", request.RoundId),
                    new Npgsql.NpgsqlParameter("@waitingBets", (int)GameStatus.WaitingBets),
                    new Npgsql.NpgsqlParameter("@owner", ownerId!.Value),
                ], cancellationToken);

            if (affected == 0)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            await Context.GameStatusLogs.AddAsync(
                new GameStatusLog(request.RoundId, GameStatus.WaitingBets, GameStatus.NotEnoughPlayersToStart),
                cancellationToken);
            await Context.SaveChangesAsync(cancellationToken);

            // The creator's is the only bet on an unmatched room.
            var creatorBet = await Context.GameBets
                .AsNoTracking()
                .Where(x => x.GameId == request.RoundId)
                .OrderBy(x => x.CreatedDate)
                .FirstOrDefaultAsync(cancellationToken);

            // A free room moved no money, so there is nothing to give back.
            if (creatorBet != null && !BetExtensions.IsFreeBet(creatorBet))
            {
                await _messageBus.PublishEventAsync(new RefundInvalidBetEvent(
                    creatorBet.Id, wallet, creatorBet.Amount, "Duel cancelled by its creator.",
                    request.RoundId, creatorBet.Signature));
            }

            await _gameService.BroadcastDuelRoomCancelled(request.RoundId, cancellationToken);

            return Unit.Value;
        }

        /// <summary>
        /// The canonical message the wallet signs. Purpose-scoped and nonce-bound, so a signature captured
        /// here can never be replayed on a cash-out.
        /// </summary>
        public static string BuildMessage(string wallet, string nonce)
            => $"SpaceStroker duel cancel\nWallet: {wallet}\nNonce: {nonce}";
    }
}
```

⚠️ `BroadcastDuelRoomCancelled` is added in **Task 13**. Add the `IGameService` method signature there; until then, stub it as a no-op so this task compiles, and Task 13 fills it in.

⚠️ Match `BuildMessage`'s shape to the shipped ones — open `OrbitCashOutHandler` (or whichever handler uses `orbit-cashout`) and mirror its message format exactly so the SPA's signing helper needs no new branch.

- [ ] **Step 5: Add the controller route + rate-limit rule**

Controller:

```csharp
        /// <summary>
        /// Cancel an unmatched Monkey Duel room and refund its creator (#184). Body:
        /// <c>{ wallet, nonce, signature }</c> — a wallet proof, because this moves money. Rate-limited.
        /// </summary>
        [HttpPost("rounds/{roundId:guid}/duel-cancel")]
        public async Task<IActionResult> CancelDuelRoom(Guid roundId, [FromBody] CancelDuelRoomRequestBody body)
            => await ProcessRequest(new CancelDuelRoomRequest(roundId, body));
```

Config:

```jsonc
            {
                "Endpoint": "post:/games/rounds/*/duel-cancel",
                "Period": "1m",
                "Limit": 30
            },
```

Add `"post:/games/rounds/*/duel-cancel"` to both `RateLimitSettingsTests` assertions (it sorts after `post:/games/rounds/*/reroll`).

- [ ] **Step 6: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Api src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests
git commit -m "Let a creator cancel an unmatched duel room against a wallet proof."
git push origin feature/monkey-duel
```

---

## Task 13: The per-type rooms channel

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Hubs/ActiveGamesHub.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (+ `IGameService.cs`)
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` + `spacestrokersettings.json`
- Test: `src/SpaceStroker.Tests/UnitTests/Hubs/ActiveGamesHubDuelTests.cs`

**Interfaces:**
- Consumes: `DuelRoomDto`, `DuelRoomEvent` (Task 8).
- Produces:
  - `ActiveGamesHub.DuelBoardGroupPrefix = "duels:"`, `static string DuelBoardGroupFor(Guid gameTypeId)`
  - `Task ActiveGamesHub.JoinDuelBoard(string gameTypeId)` / `Task LeaveDuelBoard(string gameTypeId)`
  - `GameSettings.DuelBoardEndpoint` (`"{0}/DuelBoard"`)
  - `Task IGameService.BroadcastDuelRoomOpened(Guid gameId, CancellationToken)` / `BroadcastDuelRoomTaken(...)` / `BroadcastDuelRoomCancelled(...)`

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Hubs/ActiveGamesHubDuelTests.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Moq;
using Shouldly;
using SpaceStroker.Infrastructure.Hubs;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Hubs;

/// <summary>
/// #184 §6.8 / AC 10 — the OPEN DUELS board listens on a per-GAME-TYPE group, so it survives every room
/// opening and closing and the client joins ONCE. One connection per tab
/// (specs/unified-websocket-connection.md), no polling.
/// </summary>
public class ActiveGamesHubDuelTests
{
    [Fact]
    public void TheGroupIsKeyedOnTheGameType_AndCannotCollideWithARoundsGroup()
    {
        var typeId = Guid.NewGuid();

        // A round's group name is the bare gameId; the prefix is what keeps the two apart.
        ActiveGamesHub.DuelBoardGroupFor(typeId).ShouldBe($"duels:{typeId}");
        ActiveGamesHub.DuelBoardGroupFor(typeId).ShouldNotBe(typeId.ToString());
        ActiveGamesHub.DuelBoardGroupFor(typeId).ShouldNotBe(ActiveGamesHub.ChatGroupFor(typeId));
    }

    [Fact]
    public async Task JoinDuelBoard_JoinsTheTypesGroup()
    {
        var harness = HubDuelHarness.WithActiveType(out var typeId);

        await harness.Hub.JoinDuelBoard(typeId.ToString());

        harness.Groups.Verify(x => x.AddToGroupAsync(
            harness.ConnectionId, ActiveGamesHub.DuelBoardGroupFor(typeId), default), Times.Once);
    }

    /// <summary>An unknown or inactive type joins NOTHING and says nothing (constraints.md §6).</summary>
    [Fact]
    public async Task JoinDuelBoard_OnAnUnknownType_JoinsNothingAndThrowsNothing()
    {
        var harness = HubDuelHarness.WithActiveType(out _);

        await Should.NotThrowAsync(() => harness.Hub.JoinDuelBoard(Guid.NewGuid().ToString()));

        harness.Groups.Verify(x => x.AddToGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task JoinDuelBoard_OnAMalformedId_JoinsNothing()
    {
        var harness = HubDuelHarness.WithActiveType(out _);

        await Should.NotThrowAsync(() => harness.Hub.JoinDuelBoard("not-a-guid"));

        harness.Groups.Verify(x => x.AddToGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>Leaving is harmless, so it is NOT validated — the JoinChat/LeaveChat precedent.</summary>
    [Fact]
    public async Task LeaveDuelBoard_IsNotValidated()
    {
        var harness = HubDuelHarness.WithActiveType(out _);
        var strangerType = Guid.NewGuid();

        await harness.Hub.LeaveDuelBoard(strangerType.ToString());

        harness.Groups.Verify(x => x.RemoveFromGroupAsync(
            harness.ConnectionId, ActiveGamesHub.DuelBoardGroupFor(strangerType), default), Times.Once);
    }

    [Fact]
    public async Task LeaveDuelBoard_OnAMalformedId_DoesNothing()
    {
        var harness = HubDuelHarness.WithActiveType(out _);

        await Should.NotThrowAsync(() => harness.Hub.LeaveDuelBoard("not-a-guid"));

        harness.Groups.Verify(x => x.RemoveFromGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
```

⚠️ `HubDuelHarness` is a **new** helper. Copy the hub construction from the existing `src/SpaceStroker.Tests/UnitTests/Hubs/` tests (the chat tests already build `ActiveGamesHub` with a mocked `IGroupManager`, `HubCallerContext` and `IGameService.GetAllActiveGameParameters`); expose `Hub`, `Groups`, `ConnectionId`, and `WithActiveType(out Guid typeId)` seeding one active type in the catalogue double.

- [ ] **Step 2: Run test to verify it fails**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~ActiveGamesHubDuelTests"`
Expected: FAIL — `DuelBoardGroupFor`, `JoinDuelBoard` and `LeaveDuelBoard` do not exist.

- [ ] **Step 3: Add the hub methods**

In `ActiveGamesHub.cs`, beside `ChatGroupPrefix`:

```csharp
        /// <summary>
        /// A game type's OPEN DUELS board (#184 §6.8). Prefixed so it can never collide with a round's
        /// (bare gameId) group — nor with the chat room's.
        /// </summary>
        public const string DuelBoardGroupPrefix = "duels:";

        public static string DuelBoardGroupFor(Guid gameTypeId) => $"{DuelBoardGroupPrefix}{gameTypeId}";
```

and beside `JoinChat` / `LeaveChat`:

```csharp
        /// <summary>
        /// Joins a GAME TYPE's open-duels board. Keyed on the type, not a round, so the board survives
        /// every room opening and closing and the client joins ONCE — keeping the one-connection-per-tab
        /// model from specs/unified-websocket-connection.md. No polling.
        ///
        /// An unknown or inactive type joins nothing and says nothing (constraints.md §6), exactly as
        /// JoinChat does.
        /// </summary>
        public async Task JoinDuelBoard(string gameTypeId)
        {
            var typeId = await ResolveActiveGameType(gameTypeId);

            if (typeId is null)
                return;

            await Groups.AddToGroupAsync(Context.ConnectionId, DuelBoardGroupFor(typeId.Value));
        }

        /// <summary>Leaves it (a type switch). Not validated: leaving is harmless.</summary>
        public Task LeaveDuelBoard(string gameTypeId)
            => Guid.TryParse(gameTypeId, out var typeId)
                ? Groups.RemoveFromGroupAsync(Context.ConnectionId, DuelBoardGroupFor(typeId))
                : Task.CompletedTask;
```

- [ ] **Step 4: Add the endpoint setting**

In `GameSettings.cs`, beside `GameChatEndpoint`:

```csharp
        /// <summary>
        /// The SignalR event the open-duels board listens on (#184). A "{0}/…" format like the game
        /// streams, because the board is per GAME TYPE and the type id has to be in the name.
        /// </summary>
        public string DuelBoardEndpoint { get; set; }
```

In `spacestrokersettings.json`, beside `"GameChatEndpoint"`:

```jsonc
            "DuelBoardEndpoint": "{0}/DuelBoard",
```

- [ ] **Step 5: Add the three broadcasts**

In `IGameService`:

```csharp
        /// <summary>#184 §6.8 — a creator's bet has just turned a round into a listable room.</summary>
        Task BroadcastDuelRoomOpened(Guid gameId, CancellationToken cancellationToken = default);

        /// <summary>#184 §6.8 — a joiner (or the house) has claimed a room; it leaves the board.</summary>
        Task BroadcastDuelRoomTaken(Guid gameId, CancellationToken cancellationToken = default);

        /// <summary>#184 §6.8 — the creator cancelled an unmatched room; it leaves the board.</summary>
        Task BroadcastDuelRoomCancelled(Guid gameId, CancellationToken cancellationToken = default);
```

In `GameService`:

```csharp
        public Task BroadcastDuelRoomOpened(Guid gameId, CancellationToken cancellationToken = default)
            => BroadcastDuelRoomChange(gameId, "opened", includeRoom: true, cancellationToken);

        public Task BroadcastDuelRoomTaken(Guid gameId, CancellationToken cancellationToken = default)
            => BroadcastDuelRoomChange(gameId, "taken", includeRoom: false, cancellationToken);

        public Task BroadcastDuelRoomCancelled(Guid gameId, CancellationToken cancellationToken = default)
            => BroadcastDuelRoomChange(gameId, "cancelled", includeRoom: false, cancellationToken);

        /// <summary>
        /// Pushes one room change to the game type's board group (#184 §6.8 / AC 10), so the board never
        /// refetches and never polls.
        ///
        /// ⚠️ A HOUSE round never appears on the board and never broadcasts: it concerns one browser.
        ///
        /// ⚠️ Never throws. A dead Redis backplane must not roll back the bet, the claim or the cancel that
        /// caused it — the board recovers on its next load, the money must not.
        /// </summary>
        private async Task BroadcastDuelRoomChange(Guid gameId, string change, bool includeRoom, CancellationToken cancellationToken)
        {
            try
            {
                var round = await _context.Games
                    .AsNoTracking()
                    .Where(x => x.Id == gameId)
                    .Select(x => new
                    {
                        x.Id,
                        x.ParametersId,
                        x.DuelKind,
                        x.Stake,
                        x.CreatedDate,
                        Word = x.Bets.OrderBy(b => b.CreatedDate).Select(b => b.Choice).FirstOrDefault(),
                        DisplayName = x.Owner!.DisplayName,
                        Wallet = x.Owner!.ExternalPlayerId,
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (round == null || round.DuelKind != DuelKind.OpenRoom)
                    return;

                DuelRoomDto? room = includeRoom
                    ? new DuelRoomDto
                    {
                        GameId = round.Id,
                        Word = round.Word ?? string.Empty,
                        Stake = round.Stake ?? 0m,
                        DisplayName = round.DisplayName,
                        Wallet = round.Wallet,
                        CreatedDate = round.CreatedDate,
                    }
                    : null;

                var payload = new DuelRoomEvent(change, room, round.Id).AsJson();
                var endpoint = string.Format(_settings.Games.DuelBoardEndpoint, round.ParametersId.ToString());

                await _activeGamesHub.Clients
                    .Group(ActiveGamesHub.DuelBoardGroupFor(round.ParametersId))
                    .SendAsync(endpoint, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DUEL — could not broadcast the '{Change}' room change for game {GameId}.", change, gameId);
            }
        }
```

- [ ] **Step 6: Call the broadcasts from the writes that cause them**

In `RegisterBetHandler.RegisterDuelBet` (Task 10), after `await _gameService.PublishBetNotification(_bet);`:

```csharp
                // #184 §6.8 — tell the board, alongside the write that caused it. A room becomes LISTABLE
                // when its CREATOR's bet lands (an unbet round is a shell, not a room), and it leaves the
                // board when a joiner claims it. A House round broadcasts neither (the service filters).
                if (isCreator)
                    await _gameService.BroadcastDuelRoomOpened(_bet.GameId, cancellationToken);
                else
                    await _gameService.BroadcastDuelRoomTaken(_bet.GameId, cancellationToken);
```

`CancelDuelRoomHandler` already calls `BroadcastDuelRoomCancelled` (Task 12) — remove the temporary stub.

- [ ] **Step 7: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests
git commit -m "Push duel room changes to a per-type board channel."
git push origin feature/monkey-duel
```

---

## Task 14: Lifecycle — start, no rollover, never seeded, never auto-opened

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs`
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (`OpenNewGames`, `ReplanOpenRounds`)
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/BetPlacedConsumerTests.cs` (extend)
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/RunGameConsumerTests.cs` (extend)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/HouseSeedBetServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `GameParameters.IsDuel` (Task 1), `GameSettings.SecondsUntilGameStarts(isHouseEdge, isDuel)` (Task 5).
- Produces: no new signatures.

- [ ] **Step 1: Write the failing tests**

Append to `BetPlacedConsumerTests.cs`:

```csharp
    /// <summary>
    /// AC 2 — a duel starts as soon as BOTH bets are in. MinimumWaitPeriodForBets is the window a
    /// shared rolling round holds open for more players; a duel has exactly two sides, so waiting it out
    /// would put 15 dead seconds in front of every VS splash.
    /// </summary>
    [Fact]
    public async Task ADuelRoundWithBothBets_StartsWithoutWaitingOutTheBettingWindow()
    {
        var harness = BetPlacedHarness.DuelRound(betCount: 2, minimumWaitPeriodForBets: 60);

        var elapsed = await harness.ProcessAndTimeAsync();

        harness.StartedGames.ShouldContain(harness.GameId);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Decision 15 — an unmatched room past the window simply STAYS OPEN. It is not under-subscribed,
    /// it is waiting, and refunding it would cancel a room the creator never asked to cancel.
    /// </summary>
    [Fact]
    public async Task ADuelRoomWithOneBetPastTheWindow_StaysWaitingBetsAndIsNotRefunded()
    {
        var harness = BetPlacedHarness.DuelRound(betCount: 1, minimumWaitPeriodForBets: 0);

        await harness.ProcessAsync();

        harness.StartedGames.ShouldBeEmpty();
        harness.Round().Status.ShouldBe(GameStatus.WaitingBets);
        harness.PublishedEvents.OfType<RefundInvalidBetEvent>().ShouldBeEmpty();
    }

    /// <summary>MinimumNumberOfPlayers = 2 is correct for BOTH kinds: the house player counts as one.</summary>
    [Fact]
    public async Task ADuelRoundStillRequiresTwoDistinctPlayers()
    {
        var harness = BetPlacedHarness.DuelRound(betCount: 1, minimumWaitPeriodForBets: 60);

        await harness.ProcessAsync();

        harness.StartedGames.ShouldBeEmpty();
    }
```

Append to `RunGameConsumerTests.cs`:

```csharp
    /// <summary>
    /// A duel creates NO successor round — same as house-edge. Rounds are opened by players
    /// (POST /games/{id}/duels), and a rolled-over duel would be an ownerless shell on the board.
    /// </summary>
    [Fact]
    public async Task ADuelRoundCreatesNoSuccessor()
    {
        var harness = RunGameHarness.DuelRound();

        await harness.ProcessAsync();

        harness.CreatedGames.ShouldBeEmpty();
    }
```

Append to `HouseSeedBetServiceTests.cs`:

```csharp
    /// <summary>
    /// AC 12 / decision 17 — the house seed bet NEVER touches a duel type. Necessary, not cosmetic: on a
    /// duel a seeded house bet IS the second player, so every PVP room would be taken by the house the
    /// instant it opened and the board would never show a human room.
    /// </summary>
    [Fact]
    public async Task ADuelTypeIsNeverSeeded()
    {
        var harness = HouseSeedHarness.ForDuelType();

        await harness.Service.SeedRound(harness.GameId);

        harness.TransferCount.ShouldBe(0);
        harness.RegisterCount.ShouldBe(0);
    }
```

⚠️ Reuse the harness each of those three test files already has; add only the `DuelRound()` / `ForDuelType()` factory methods (a duel type is `IsDuel = true`, `IsHouseEdge = false`, `MinimumNumberOfPlayers = 2`, `PresetChoices` empty, plus `Game.DuelKind` set on the round).

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~BetPlacedConsumerTests|FullyQualifiedName~RunGameConsumerTests|FullyQualifiedName~HouseSeedBetServiceTests"`
Expected: FAIL — the duel round waits out the window, creates a successor, and gets seeded.

- [ ] **Step 3: Skip the wait-out hold for duels**

In `BetPlacedConsumer`, change:

```csharp
            if (!game.Parameters.IsHouseEdge)
```

to:

```csharp
            // A house-edge round (Hyper Roll) skips this hold: it is player-vs-house, needs one player, and
            // its countdown begins on the first valid bet.
            //
            // #184 — a DUEL skips it for a different reason: it has exactly TWO sides, so once both bets
            // are in there is nobody left to wait for. Its countdown is DuelTimeUntilGameStarts (the VS
            // splash beat), stamped by TryStartGame. The house never seeds a duel and AllowOrbit is off, so
            // nothing else relies on this window for them.
            if (!game.Parameters.IsHouseEdge && !game.Parameters.IsDuel)
```

⚠️ **No other change here.** `requiredPlayers` stays `MinimumNumberOfPlayers` (2) for a duel — the house player is a distinct `PlayerId`, so `playerCount` counts it. The under-subscribed branch already `return`s for a non-lottery round, which is exactly "the room stays open".

- [ ] **Step 4: No rollover for duels**

In `RunGameConsumer`, change:

```csharp
            var parameters = game.Parameters?.IsHouseEdge == true
                ? null
                : await gameService.GetActiveParametersForRollover(game.ParametersId);
```

to:

```csharp
            // #184 — a duel creates no successor either: its rounds are opened by players
            // (POST /games/{id}/duels), and a rolled-over duel would be an ownerless shell on the board.
            var parameters = game.Parameters?.IsHouseEdge == true || game.Parameters?.IsDuel == true
                ? null
                : await gameService.GetActiveParametersForRollover(game.ParametersId);
```

- [ ] **Step 5: Never seed a duel**

In `HouseSeedBetService.SeedRound`, after the `IsHouseEdge` guard:

```csharp
            // #184 / decision 17 — a duel's second bet IS the counterparty, placed by DuelHouseBetService
            // against the ROUND's own stake. Seeding here would take every PVP room the instant it opened,
            // so the board would never show a human room. Information, not Warning: production keeps
            // Warning-and-above and this fires for every duel round.
            if (game.Parameters.IsDuel)
            {
                Log.Information("HOUSE SEED — game type {ParametersId} is a duel; the house never seeds it.", game.ParametersId);
                return;
            }
```

- [ ] **Step 6: Never auto-open or re-plan a duel**

In `Consumers/Program.cs` → `OpenNewGames`, after the `IsHouseEdge` skip:

```csharp
        // #184: duel rounds are opened by players (POST /games/{id}/duels), never at startup — exactly
        // like house-edge boards.
        if (parameter.Value.IsDuel)
            continue;
```

In `ReplanOpenRounds`, widen the filter:

```csharp
        // ⚠️ Duels are excluded alongside house-edge rounds: republishing GameOpenedEvent for a duel would
        // reach the house SEEDER and the Orbit planner, neither of which should ever see one
        // (decisions 17 + 18).
        var openRounds = dbContext.Games
            .Where(x => Game.GameAcceptingBets.Contains(x.Status)
                     && !x.Parameters.IsHouseEdge && !x.Parameters.IsDuel)
            .Select(x => new { x.Id, x.ParametersId })
            .ToList();
```

- [ ] **Step 7: Confirm the untouched paths**

Read and confirm — no edit expected:
- `HouseEdgeBoardCleanupJob`: its query is `IsHouseEdge`-filtered **and** duels set no `BoardExpiresAt`, so it never sees a duel room. Verify both.
- `SpaceStrokerTyper`: a duel is a free-text race with two bets — `SetBets` races the two words, `SetCharactersOnTyper` narrows the pool to the union of their characters, `DecideFallbackWinner` uses the unweighted `FallbackIndex`. No change.
- `RewardGameConsumer`: `isHouseEdge` is false, so no treasury pre-check runs and the pari-mutuel payout applies (AC 3). No change.
- Lottery scheduling: a duel type has no `LotteryParameters`. No change.

- [ ] **Step 8: Run the full suite**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Tests
git commit -m "Start a duel on its second bet, and keep it out of rollover, seeding and startup."
git push origin feature/monkey-duel
```

---

## Task 15: Contracts — DTOs, availability, verification

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/BetAvailabilityDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs` + its handler
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/CheckBetAvailability/CheckBetAvailabilityHandler.cs` + request body
- Test: `src/SpaceStroker.Tests/UnitTests/Contracts/DuelContractTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/CheckBetAvailabilityHandlerTests.cs` (extend)

**Interfaces:**
- Produces: `GameParametersDto.IsDuel`; `ActiveGameDto.DuelKind/Stake/OwnerWallet/IsFull`; `BetAvailabilityDto.MaxStake`; `GameVerificationDto.IsDuel/DuelKind/DuelPoolSnapshot/HouseWord`; `CheckBetAvailabilityRequestBody.GameId` becomes genuinely optional for duels.

- [ ] **Step 1: Write the failing tests**

Create `src/SpaceStroker.Tests/UnitTests/Contracts/DuelContractTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Contracts.Games;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Contracts;

/// <summary>#184 §6.10 — what the SPA is told about a duel type and a duel round.</summary>
public class DuelContractTests
{
    private static TokenSettings Token() => new() { CommissionPercentage = 2m, Decimals = 9, Symbol = "SOL" };

    private static GameParameters DuelType() => new(GameCharacterType.Letters, 6, true)
    {
        Name = "Monkey Duel", Description = "d", RequiredAmount = 0.002m, MaximumStake = 0.5m,
        MinimumNumberOfPlayers = 2, Active = true, IsDuel = true, AllowOrbit = false,
        PresetChoices = new List<PresetChoice>(),
    };

    [Fact]
    public void GameParametersDto_PublishesIsDuel_AndKeepsPresetChoicesEmpty()
    {
        var dto = new GameParametersDto(DuelType(), Token());

        dto.IsDuel.ShouldBeTrue();
        dto.IsHouseEdge.ShouldBeFalse();
        dto.MaximumStake.ShouldBe(0.5m);
        // ⚠️ §3.1 S1: the SPA's archetype resolver reads a non-empty presetChoices as "preset race".
        dto.PresetChoices.ShouldBeEmpty();
    }

    /// <summary>
    /// AC 15 — the duel DOES take a cut, so commissionPercentage must be the REAL rate. The
    /// `IsHouseEdge ? 0m` branch must not be widened, or the UI prints a 0% cut on a game that takes one.
    /// </summary>
    [Fact]
    public void GameParametersDto_PublishesTheRealCommissionRateForADuel()
    {
        new GameParametersDto(DuelType(), Token()).CommissionPercentage.ShouldBe(2m);
    }

    [Fact]
    public void ActiveGameDto_PublishesTheRoundsKindStakeOwnerAndFullness()
    {
        var owner = new Player { ExternalPlayerId = "creator-wallet", DisplayName = "Creator", LastSeenAt = DateTime.UtcNow };
        var parameters = DuelType();
        var game = new Game(parameters.Id)
        {
            Parameters = parameters,
            DuelKind = DuelKind.OpenRoom,
            Stake = 0.125m,
            OwnerPlayerId = owner.Id,
            Owner = owner,
            ClaimedByPlayerId = Guid.NewGuid(),
        };

        var dto = new ActiveGameDto(game);

        dto.DuelKind.ShouldBe(DuelKind.OpenRoom);
        dto.Stake.ShouldBe(0.125m);
        dto.OwnerWallet.ShouldBe("creator-wallet");
        dto.IsFull.ShouldBeTrue();
        dto.BoardChoices.ShouldBeNull();   // a duel has no board
    }

    [Fact]
    public void ActiveGameDto_OnAnUnmatchedRoom_IsNotFull()
    {
        var parameters = DuelType();
        var game = new Game(parameters.Id) { Parameters = parameters, DuelKind = DuelKind.House, Stake = 0.125m };

        new ActiveGameDto(game).IsFull.ShouldBeFalse();
    }

    [Fact]
    public void ActiveGameDto_OnANonDuelRound_LeavesTheDuelFieldsNull()
    {
        var game = new Game(Guid.NewGuid());

        var dto = new ActiveGameDto(game);

        dto.DuelKind.ShouldBeNull();
        dto.Stake.ShouldBeNull();
        dto.OwnerWallet.ShouldBeNull();
        dto.IsFull.ShouldBeFalse();
    }

    [Fact]
    public void BetAvailabilityDto_Open_QuotesNoClamp()
    {
        // null maxStake means "no clamp" — every non-duel type.
        BetAvailabilityDto.Open.MaxStake.ShouldBeNull();
        BetAvailabilityDto.Open.Available.ShouldBeTrue();
    }

    [Fact]
    public void BetAvailabilityDto_CanQuoteAClampedCeiling()
    {
        var dto = BetAvailabilityDto.OpenWithMaxStake(0.079m);

        dto.Available.ShouldBeTrue();
        dto.MaxStake.ShouldBe(0.079m);
    }

    [Fact]
    public void BetAvailabilityDto_UnderMaintenance_CanStillQuoteACeiling()
    {
        // AC 4: the tab is refused for THIS stake but the SPA still needs the clamp to re-quote.
        var dto = BetAvailabilityDto.UnavailableWithMaxStake(0.079m);

        dto.Available.ShouldBeFalse();
        dto.MaxStake.ShouldBe(0.079m);
        dto.Message.ShouldBe(ErrorMessages.HouseEdgeUnderMaintenance);
    }
}
```

Append to `CheckBetAvailabilityHandlerTests.cs`:

```csharp
    /// <summary>§6.11 — a duel quotes the CLAMPED ceiling, and does so with no round (mount time).</summary>
    [Fact]
    public async Task ADuelHouseTab_AnswersWithTheClampedCeiling()
    {
        var harness = AvailabilityHarness.DuelType(maxStake: 0.079m, coverable: true);

        var dto = await harness.CheckAsync(gameId: harness.HouseRoundId, choice: "banana", amount: 0.05m);

        dto.Available.ShouldBeTrue();
        dto.MaxStake.ShouldBe(0.079m);
    }

    [Fact]
    public async Task ADuelTypeAnswersWithNoGameId_SoTheSpaCanAskOnMount()
    {
        // ⚠️ Hyper Roll REQUIRES a round here (availability is about one board). A duel asks for the
        // ceiling BEFORE a round exists, so a null GameId must not be "not an open board".
        var harness = AvailabilityHarness.DuelType(maxStake: 0.5m, coverable: true);

        var dto = await harness.CheckAsync(gameId: null, choice: "banana", amount: 0m);

        dto.Available.ShouldBeTrue();
        dto.MaxStake.ShouldBe(0.5m);
    }

    [Fact]
    public async Task ADuelPvpRoom_IsAlwaysAvailableAtTheTypeCeiling()
    {
        // The house wallet is irrelevant to a PVP room: the counterparty is another player.
        var harness = AvailabilityHarness.DuelType(maxStake: 0.5m, coverable: false);

        var dto = await harness.CheckAsync(gameId: harness.OpenRoomId, choice: "banana", amount: 0.05m);

        dto.Available.ShouldBeTrue();
        dto.MaxStake.ShouldBe(0.5m);
    }

    /// <summary>AC 5 — below the coverage floor the house tab is unavailable, vaguely.</summary>
    [Fact]
    public async Task ADuelHouseTabBelowTheFloor_IsUnavailable()
    {
        var harness = AvailabilityHarness.DuelType(maxStake: 0m, coverable: false);

        var dto = await harness.CheckAsync(gameId: harness.HouseRoundId, choice: "banana", amount: 0.05m);

        dto.Available.ShouldBeFalse();
        dto.Message.ShouldBe(ErrorMessages.HouseEdgeUnderMaintenance);
        dto.MaxStake.ShouldBe(0m);
    }

    [Fact]
    public async Task AComingSoonDuelType_IsUnavailable()
    {
        var harness = AvailabilityHarness.DuelType(maxStake: 0.5m, coverable: true, comingSoon: true);

        (await harness.CheckAsync(gameId: null, choice: "banana", amount: 0m)).Available.ShouldBeFalse();
    }

    [Fact]
    public async Task ADuelFreeBetIsAvailableWithoutTouchingTheWallet()
    {
        var harness = AvailabilityHarness.DuelType(maxStake: 0.5m, coverable: false);

        (await harness.CheckAsync(gameId: harness.HouseRoundId, choice: "banana", amount: 0m))
            .Available.ShouldBeTrue();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~DuelContractTests|FullyQualifiedName~CheckBetAvailabilityHandlerTests"`
Expected: FAIL — the new DTO members do not exist.

- [ ] **Step 3: Extend `GameParametersDto`**

After the `IsHouseEdge` line:

```csharp
        // #184 Monkey Duel: RequiredAmount is the MINIMUM stake and MaximumStake the maximum, both
        // per-round. A duel type is FREE-TEXT — PresetChoices stays the empty list, which is what the
        // SPA's archetype resolver reads (§3.1 S1).
        public bool IsDuel { get; set; } = parameters.IsDuel;
```

⚠️ **Do not touch** the `CommissionPercentage` line. `parameters.IsHouseEdge ? 0m : token.CommissionPercentage` is already correct: a duel is not house-edge, so it publishes the real 2%.

- [ ] **Step 4: Extend `ActiveGameDto`**

In the constructor, after `BoardChoices`:

```csharp
            // #184 Monkey Duel. Null / false on every non-duel round.
            DuelKind = game.DuelKind;
            Stake = game.Stake;
            OwnerWallet = game.Owner?.ExternalPlayerId;
            // Both sides are in — the SPA stops offering "take this room" and shows the VS card.
            IsFull = game.ClaimedByPlayerId != null;
```

and the properties:

```csharp
        // #184: which kind of duel this round is; null for every other type.
        public DuelKind? DuelKind { get; set; }
        // #184: this round's OWN stake. The joiner (or the house) must match it exactly.
        public decimal? Stake { get; set; }
        // #184: the creator's wallet, so the SPA can render your own rooms in your corner colour.
        public string? OwnerWallet { get; set; }
        // #184: both sides are in. A full room is off the board and cannot be cancelled.
        public bool IsFull { get; set; }
```

⚠️ `OwnerWallet` needs `Owner` included — Task 7's `LoadRound` and `GetActiveGameForParameterHandler` (Task 16) both add `.Include(x => x.Owner)`.

- [ ] **Step 5: Extend `BetAvailabilityDto`**

```csharp
        public static BetAvailabilityDto Open => new() { Available = true };

        public static BetAvailabilityDto UnderMaintenance => new() { Available = false, Message = ErrorMessages.HouseEdgeUnderMaintenance };

        // #184: available, and here is the ceiling the SPA may quote (the type's, or the HouseSeed
        // wallet's clamp — whichever is lower).
        public static BetAvailabilityDto OpenWithMaxStake(decimal maxStake)
            => new() { Available = true, MaxStake = maxStake };

        // #184 / AC 4: refused for THIS stake, but the SPA still needs the clamp so it can re-quote
        // instead of leaving the player guessing.
        public static BetAvailabilityDto UnavailableWithMaxStake(decimal maxStake)
            => new() { Available = false, Message = ErrorMessages.HouseEdgeUnderMaintenance, MaxStake = maxStake };

        public bool Available { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        // #184: the maximum stake the SPA may offer. NULL means "no clamp" — every non-duel type, which
        // is why it is omitted from their payload entirely.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? MaxStake { get; init; }
```

- [ ] **Step 6: Add the availability duel branch**

In `CheckBetAvailabilityHandler.HandleRequest`, **before** the `if (!parameters!.IsHouseEdge)` line:

```csharp
            // #184 §6.11 — the duel branch, BEFORE the house-edge one. Read-only: no lock, no reservation.
            // The answer can go stale, which is exactly why RegisterDuelBet re-checks under the lock and
            // refunds (AC 4).
            if (parameters!.IsDuel)
                return await DuelAvailability(parameters, request, cancellationToken);
```

and the method:

```csharp
        /// <summary>
        /// §6.11. A duel asks TWO different questions with one endpoint: "what ceiling may I quote?"
        /// (on mount, with no round yet) and "can this stake go on?" (before the wallet signs).
        ///
        /// ⚠️ GameId is genuinely OPTIONAL here, unlike Hyper Roll where availability is always about one
        /// board: the SPA needs the ceiling before a round exists.
        ///
        /// ⚠️ A PVP room needs no HouseSeed wallet at all — its counterparty is another player.
        /// </summary>
        private async Task<BetAvailabilityDto> DuelAvailability(
            GameParameters parameters, CheckBetAvailabilityRequest request, CancellationToken cancellationToken)
        {
            if (parameters.ComingSoon)
                return BetAvailabilityDto.UnderMaintenance;

            var amount = request.Body.Amount ?? 0m;

            if (amount < 0)
                Messaging.ReturnValidationFailureMessage(BetValidationResult.InvalidAmount.StringValueOf());

            var typeCeiling = parameters.MaximumStake ?? 0m;

            // Which kind are we being asked about? No round id ⇒ the SPA is asking on mount, which it only
            // does for the house tab's ceiling; a round that is an OpenRoom is a PVP question.
            var kind = request.Body.GameId is Guid roundId
                ? await Context.Games.AsNoTracking()
                    .Where(x => x.Id == roundId && x.ParametersId == parameters.Id)
                    .Select(x => x.DuelKind)
                    .FirstOrDefaultAsync(cancellationToken)
                : DuelKind.House;

            if (kind == DuelKind.OpenRoom)
                return BetAvailabilityDto.OpenWithMaxStake(typeCeiling);

            // A free bet moves no money — the backstop does not check coverage for it either (§3.1 S7).
            if (BetExtensions.IsFreeBetRequest(amount))
                return BetAvailabilityDto.OpenWithMaxStake(typeCeiling);

            var decision = await _duelCoverage.EvaluateAsync(parameters, amount, cancellationToken);

            return decision.IsCoverable
                ? BetAvailabilityDto.OpenWithMaxStake(decision.MaxStake)
                : BetAvailabilityDto.UnavailableWithMaxStake(decision.MaxStake);
        }
```

Inject `IDuelHouseCoverageService _duelCoverage` into the handler's constructor. Also loosen the request body's doc comment:

```csharp
        /// <summary>
        /// The player's own board (round id). Solo rounds: availability is always asked about ONE round.
        ///
        /// ⚠️ Deliberately NOT [Required]: a legacy (non-house-edge) caller may omit it entirely, and a
        /// DUEL (#184) omits it on purpose — the SPA asks for the house tab's ceiling on mount, before a
        /// round exists. A house-edge type with no GameId is still treated as "not an open board".
        /// </summary>
        public Guid? GameId { get; set; }
```

- [ ] **Step 7: Extend `GameVerificationDto` and its handler**

Properties, after `BoardChoices`:

```csharp
        /// <summary>#184 Monkey Duel: this round was a 1v1 race.</summary>
        public bool IsDuel { get; set; }

        /// <summary>#184: House or OpenRoom; null for every non-duel round.</summary>
        public DuelKind? DuelKind { get; set; }

        /// <summary>
        /// #184: the ordinal-sorted pool this HOUSE round could have drawn its word from, as it was when
        /// the round opened. Published so the draw stays reproducible even after the pool is edited.
        /// Null for a PVP room.
        /// </summary>
        public string? DuelPoolSnapshot { get; set; }

        /// <summary>
        /// #184: the word the house actually drew — the bet placed by the HouseSeed wallet. Null for a PVP
        /// room. Verify it by taking attempts 0, 1, 2 … of
        /// DuelHouseWordGenerator.DeriveWord(serverSeed, gameId, attempt, duelPoolSnapshot) and returning
        /// the first that differs from the creator's word.
        /// </summary>
        public string? HouseWord { get; set; }
```

In `GetGameVerificationHandler`, populate them: `IsDuel = game.Parameters?.IsDuel == true`, `DuelKind = game.DuelKind`, `DuelPoolSnapshot = game.DuelPoolSnapshot`, and `HouseWord` = the choice of the bet whose player's `ExternalPlayerId` equals the configured HouseSeed public key, **only when** `game.DuelKind == DuelKind.House`. `OrderedBetSeeds` / `OrderedChoices` already publish everything the typer used — no change there.

- [ ] **Step 8: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Publish the duel fields on the game, round, availability and verification payloads."
git push origin feature/monkey-duel
```

---

## Task 16: Reattaching to your own duel round

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetActiveGameForParameter/GetActiveGameForParameterHandler.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetActiveGameForParameterHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `Game.DuelKind/ClaimedByPlayerId` (Task 1), `ActiveGameDto` duel fields (Task 15).
- Produces: no new signature — a duel branch beside the house-edge one.

- [ ] **Step 1: Write the failing test**

Append to `GetActiveGameForParameterHandlerTests.cs`:

```csharp
    /// <summary>
    /// #184 §7.4 — a reload re-attaches to YOUR round, exactly as Hyper Roll solo rounds do. A duel type
    /// has many concurrent live rounds, so "the type's newest round" would hand you a stranger's duel.
    /// </summary>
    [Fact]
    public async Task ADuelTypeReturnsTheCallersOwnUnfinishedRound()
    {
        var harness = ActiveGameHarness.DuelType();
        var mine = harness.SeedRound(ownerWallet: "me", claimed: true, status: GameStatus.InProgress);
        harness.SeedRound(ownerWallet: "someone-else", claimed: true, status: GameStatus.InProgress);

        var dto = await harness.GetAsync(wallet: "me");

        dto.GameId.ShouldBe(mine);
    }

    /// <summary>A round you JOINED is yours too — you are the claimer, not the owner.</summary>
    [Fact]
    public async Task ADuelTypeReturnsARoundTheCallerJOINED()
    {
        var harness = ActiveGameHarness.DuelType();
        var joined = harness.SeedRound(ownerWallet: "creator", claimedByWallet: "me", status: GameStatus.InProgress);

        (await harness.GetAsync(wallet: "me")).GameId.ShouldBe(joined);
    }

    [Fact]
    public async Task ADuelTypeReturnsYourOwnUNMATCHEDRoom()
    {
        // Your posted room is what the machine shows you while you wait for a taker.
        var harness = ActiveGameHarness.DuelType();
        var mine = harness.SeedRound(ownerWallet: "me", claimed: false, status: GameStatus.WaitingBets);

        (await harness.GetAsync(wallet: "me")).GameId.ShouldBe(mine);
    }

    [Fact]
    public async Task ADuelTypeWithNoWalletIsTheSameVagueNoActiveGame()
    {
        var harness = ActiveGameHarness.DuelType();
        harness.SeedRound(ownerWallet: "someone-else", claimed: true, status: GameStatus.InProgress);

        var refusal = await harness.GetExpectingRefusalAsync(wallet: null);

        refusal.ShouldBe(ErrorMessages.ParameterHasNoActiveGames);
    }

    [Fact]
    public async Task ADuelTypeWithNothingOfYoursIsTheSameVagueNoActiveGame()
    {
        var harness = ActiveGameHarness.DuelType();

        (await harness.GetExpectingRefusalAsync(wallet: "me")).ShouldBe(ErrorMessages.ParameterHasNoActiveGames);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~GetActiveGameForParameterHandlerTests"`
Expected: FAIL — the handler returns the type's newest round regardless of the caller.

- [ ] **Step 3: Add the duel branch**

After the house-edge branch:

```csharp
            // #184: a duel type has MANY concurrent live rounds, so "the type's newest" would hand the
            // caller a stranger's duel. Same shape as HouseEdgeRoundForWallet, widened to the round the
            // caller JOINED as well as the one they created.
            if (gameParameters.IsDuel)
                return await DuelRoundForWallet(request, cancellationToken);
```

and the method:

```csharp
        /// <summary>
        /// #184 — the caller's newest duel round that is still waiting, running or paying: how a reload
        /// re-attaches to a paid bet, and how a creator sees the room they posted.
        ///
        /// ⚠️ Owner OR claimer. The creator posted it; the joiner took it. Both are "my round".
        ///
        /// No wallet, or nothing of theirs running, is the same vague "no active game" a legacy type with
        /// no round gives (constraints.md §6).
        /// </summary>
        private async Task<ActiveGameDto> DuelRoundForWallet(GetActiveGameForParameterRequest request, CancellationToken cancellationToken)
        {
            Game? result = null;

            if (!string.IsNullOrWhiteSpace(request.CallerWallet))
            {
                result = await Context.Games
                    .Include(x => x.Bets).ThenInclude(b => b.Player)
                    .Include(x => x.Owner)
                    .Include(x => x.ClaimedBy)
                    .Include(x => x.Parameters).ThenInclude(p => p.LotteryParameters)
                    .AsNoTracking()
                    .Where(x => x.ParametersId == request.ParameterId
                        && x.DuelKind != null
                        && ((x.Owner != null && x.Owner.ExternalPlayerId == request.CallerWallet)
                         || (x.ClaimedBy != null && x.ClaimedBy.ExternalPlayerId == request.CallerWallet))
                        && Game.HouseEdgeUnfinishedStatus.Contains(x.Status))
                    .OrderByDescending(x => x.CreatedDate)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (result is null)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterHasNoActiveGames);

            return new ActiveGameDto(result!, request.CallerWallet);
        }
```

⚠️ `Game.HouseEdgeUnfinishedStatus` is `[GameStarting, InProgress, Ended, RewardForWinnersInProgress]` — it does **not** include `WaitingBets`, but a creator waiting for a taker must still see their room. Add `WaitingBets` to the duel query explicitly rather than editing that shared array:

```csharp
                        && (x.Status == GameStatus.WaitingBets || Game.HouseEdgeUnfinishedStatus.Contains(x.Status))
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests
git commit -m "Re-attach a duel player to their own round on reload."
git push origin feature/monkey-duel
```

---

## Task 17: Integration tests (Testcontainers) — MANDATORY

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationGameParameters.cs`
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs`
- Create: `src/SpaceStroker.Tests.Integration/MonkeyDuelFlowTests.cs`
- Create: `src/SpaceStroker.Tests.Integration/MonkeyDuelJoinRaceTests.cs`
- Create: `src/SpaceStroker.Tests.Integration/MonkeyDuelCancelTests.cs`
- Create: `src/SpaceStroker.Tests.Integration/MonkeyDuelCoverageTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/HouseSeedBetFlowTests.cs`, `HubBetNotificationTests.cs`, `DefaultGameSeedingTests.cs`

`constraints.md` §3 makes this mandatory: a **new feature** ships with at least one happy-path integration test, **plus** a dedicated test per particularly complex flow. The **join race** and the **cancel refund** are named as complex flows in the spec (§9.2). Docker must be running.

- [ ] **Step 1: Extend the harness**

In `IntegrationGameParameters.cs`:

```csharp
        // #184 Monkey Duel. MinimumNumberOfPlayers 2 — a duel has exactly two sides.
        public const string MonkeyDuel = "6L Monkey Duel Test";

        /// <summary>
        /// A small, fixed house word pool — enough to exercise the draw and its collision retry. The REAL
        /// pool size is an output of the ProductionGames Monte Carlo (decision 10) and is not chosen here.
        /// </summary>
        public static readonly string[] DuelWords =
            ["banana", "cosmos", "meteor", "planet", "rocket", "launch", "orbits"];

        public static GameParameters Duel(string name)
        {
            var parameters = Base(name, GameCharacterType.Letters, 6);
            parameters.MinimumNumberOfPlayers = 2;
            parameters.RequiredAmount = 1m;
            parameters.MaximumStake = 5m;
            parameters.IsDuel = true;
            // ⚠️ IsHouseEdge stays FALSE (§3.1 S2) and PresetChoices stays EMPTY (§3.1 S1) — the type must
            // look free-text to every shipped path.
            parameters.IsHouseEdge = false;
            parameters.AllowOrbit = false;
            parameters.PresetChoices = new List<PresetChoice>();
            parameters.DuelWordPool = DuelWords.Select(x => new DuelWordPool(x)).ToList();
            return parameters;
        }
```

and add `Duel(MonkeyDuel)` to `All()`.

In `IntegrationTestFixture.cs`, add helpers modelled on `OpenBoardAsync` / `RerollBoardRawAsync`:

```csharp
        /// <summary>POST /games/{typeId}/duels — opens a round of the given kind.</summary>
        public async Task<(Guid GameId, string ServerSeedHash)> OpenDuelRoomAsync(Guid gameTypeId, DuelKind kind, HttpClient? client = null)

        /// <summary>GET /games/{typeId}/duels — the public board.</summary>
        public async Task<List<DuelRoomDto>> GetDuelRoomsAsync(Guid gameTypeId)

        /// <summary>POST /games/rounds/{roundId}/duel-cancel with a valid wallet proof.</summary>
        public async Task<HttpResponseMessage> CancelDuelRoomRawAsync(Guid roundId, string wallet)

        /// <summary>Mints and caches a duel-cancel nonce, then signs it the way MintOrbitSession does.</summary>
        public (string Nonce, string Signature) MintDuelCancelProof(string wallet)

        /// <summary>Drives DuelHouseBetService directly, the way the suite drives the Orbit placement path.</summary>
        public async Task PlaceDuelHouseBetAsync(Guid gameId)

        /// <summary>Sets the FakeSolanaService's HouseSeed SOL balance (null = a failed read).</summary>
        public void SetHouseSeedBalance(decimal? balance)
```

⚠️ Copy each one's construction from the nearest shipped helper rather than inventing it: `OpenBoardAsync` for the open/raw-POST shape, `MintOrbitSession` + the self-exclusion tests for the wallet-proof shape, `PublishPlaceOrbitBetAsync` for driving a Consumers service directly, and `TestDoubles.cs`'s `FakeSolanaService` for the balance knob (it may already expose a settable SOL balance — check before adding one).

- [ ] **Step 2: Write `MonkeyDuelFlowTests` — the mandatory happy path**

```csharp
[Collection(IntegrationCollection.Name)]
public class MonkeyDuelFlowTests
{
    [Fact]
    public async Task HouseDuel_HappyPath_RunsAndPaysTheWinnerTheTwoStakePotLessTheCut()
```

Asserts, in order (AC 1–3, 13):
1. `OpenDuelRoomAsync(typeId, DuelKind.House)` returns a round whose `ServerSeedHash` is already set — **the commitment predates both bets**, and `DuelPoolSnapshot` equals the ordinal-sorted pool.
2. The creator's paid bet binds `OwnerPlayerId` and `Stake`.
3. `PlaceDuelHouseBetAsync(gameId)` places exactly **one** house bet: amount == the round's stake, `FundedByOrbit == false`, and its choice equals `DuelHouseWordGenerator.DeriveWord(seed, gameId, attempt, poolSnapshot)` for the **first attempt whose word differs from the creator's** — computed in the test, not read from the row.
4. The round reaches `GameStarting` **within `DuelTimeUntilGameStarts + slack`, without waiting out `MinimumWaitPeriodForBets`** — pin `MinimumWaitPeriodForBets` high in the fixture and assert the elapsed time, exactly as `HyperRollSoloRoundsTests` does.
5. The round runs and ends; the winner is credited `stake × (2 − rate)` — assert the **exact** figure (`1.98 × stake` at 2%), not `2 × stake × (1 − rate)`.
6. **Exactly one** `Credit` `TransactionLog` row for the winning bet.
7. `GetSuccessorGameAsync(parametersId, gameId)` is **null** — no rollover.
8. `GetGameVerificationAsync(gameId)` publishes `isDuel`, `duelKind`, `duelPoolSnapshot` and `houseWord`, and the revealed `serverSeed` reproduces the same house word offline.

```csharp
    [Fact]
    public async Task PvpDuel_HappyPath_ListsTheRoomThenRunsWhenSomebodyTakesIt()
```

Asserts (AC 6, 10): the creator's room appears on `GET /games/{type}/duels` with its word and stake; a second wallet's matching bet sets `ClaimedByPlayerId`; the room leaves the board; the round starts and ends; no successor round.

- [ ] **Step 3: Write `MonkeyDuelJoinRaceTests` — complex flow 1**

```csharp
    [Fact]
    public async Task TwoConcurrentJoiners_ExactlyOneClaimsTheRoom_TheOtherIsRefunded()
```

Fire both `POST /games/{roomId}/bets` with `Task.WhenAll` (distinct words, matching stakes). Assert (AC 8): exactly one 201 and one refusal; `ClaimedByPlayerId` is the winner's; the loser's payment signature has a `Refund` `TransactionLog` row (`WaitForRefundAsync`); the loser's message is the **existing vague** `ChoiceAlreadyTaken` plus the refund notice; the round holds exactly **two** bets.

```csharp
    [Fact]
    public async Task AJoinerWhoseAmountDoesNotMatchTheRoomStake_IsRefunded()      // AC 7

    [Fact]
    public async Task AJoinerWhoCopiesTheCreatorsWord_IsRefundedAsChoiceAlreadyTaken()  // AC 11

    [Fact]
    public async Task TheCreatorCannotJoinTheirOwnRoom()                            // Review Focus 4
```

- [ ] **Step 4: Write `MonkeyDuelCancelTests` — complex flow 2**

```csharp
    [Fact]
    public async Task TheCreatorCancelsAnUnmatchedRoom_AndIsFullyRefunded()
```

Asserts (AC 9): the room leaves `GET /games/{type}/duels`; the round's status is `NotEnoughPlayersToStart` with an `EndDate`; a `Refund` `TransactionLog` row exists for the creator's payment for the **full** stake; the status-log row records `WaitingBets → NotEnoughPlayersToStart`.

```csharp
    [Fact]
    public async Task AClaimedRoomCannotBeCancelled_AndTheDuelStillRuns()

    [Fact]
    public async Task CancellingTwiceRefundsOnce()
```

- [ ] **Step 5: Write `MonkeyDuelCoverageTests`**

```csharp
    [Fact]
    public async Task AHouseSeedWalletCoveringPartOfTheRange_ClampsTheQuoteAndRefusesAboveIt()  // AC 4
```

Set the HouseSeed balance so the clamp lands mid-range; assert `POST /availability` quotes the clamped `maxStake`, and a bet **above** it is refused by the `RegisterDuelBet` backstop **and refunded** (not accepted).

```csharp
    [Fact]
    public async Task AHouseSeedWalletBelowTheMinimum_MakesTheHouseTabUnavailable()  // AC 5

    [Fact]
    public async Task AFailedBalanceRead_FailsClosed()                              // Review Focus 3
```

- [ ] **Step 6: Extend the three shipped suites**

- `HouseSeedBetFlowTests`: a duel round is opened → the seeder **never** places a bet on it (AC 12).
- `HubBetNotificationTests`: on one connection joined to `{gameTypeId}/DuelBoard`, a room opened / taken / cancelled each arrive, **with no refetch** (AC 10).
- `DefaultGameSeedingTests`: the seeded duel type has `IsDuel == true`, `IsHouseEdge == false`, `AllowOrbit == false`, `ComingSoon == true`, `PresetChoices` **empty**, and its configured words reconciled into `DuelWordPool`.

- [ ] **Step 7: Run the integration suite**

Run: `scripts\run_integration_tests.bat`
Expected: PASS. Docker must be running; the first run pulls images.

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Tests.Integration
git commit -m "Cover the duel happy path, the join race and the cancel refund end to end."
git push origin feature/monkey-duel
```

---

## Task 18: The Monte Carlo launch gate

**Files:**
- Create: `src/SpaceStroker.Tests/UnitTests/Domain/ProductionGames/DuelEdgeTests.cs`

**Interfaces:**
- Consumes: `SpaceStrokerTyper`, `DuelHouseWordGenerator`, `GameSettings` (all existing).
- Produces: a report, not a shipped behavior. **The pool is NOT chosen in this task** — the type stays `ComingSoon: true` with an empty `Words` list until the owner reads the run's output and decides.

⚠️ The namespace is what matters: `run_production_session_tests.bat` filters on `FullyQualifiedName~ProductionGames`, so a class in `SpaceStroker.Tests.UnitTests.Domain.ProductionGames` is picked up for free — **and** excluded from `run_tests.bat`, which is what keeps the fast suite fast.

⚠️ This is a **new kind of simulation**. The existing `ProductionGameTests` measures typer **pacing** (mean/p95 duration, fallback share) — it does not measure win rates. This is not a new `[InlineData]` on `ShouldSelectWinnerInTime`.

- [ ] **Step 1: Write the simulation**

`src/SpaceStroker.Tests/UnitTests/Domain/ProductionGames/DuelEdgeTests.cs` — four `[Fact]`s, each running a large sample of **real typer runs** at the type's real settings (6-letter words, `Letters`, `AcceptDuplicatedCharacters = true`) and writing a table to `ITestOutputHelper`:

1. `HouseDuel_AgainstANaivePlayerWord_ReportsTheHouseShareOfThePot` — expected ≈ the commission on profit, **~1.00% of the pot**. Assert the measured share is within a wide band of that (it is a sanity check on the harness, not the gate).
2. `HouseDuel_AgainstTheBestPlayerCounterWord_ReportsTheEdge` — search candidate player words against the pool's distribution and report the **best counter-word's win rate** and the house's resulting edge. The player picks first knowing the pool, so on the ~57% of rounds that finish **naturally** they can optimise word order (Penney's game). **This is the gate**: report it, do not assert a threshold the owner has not set.
3. `PvpDuel_ReportsTheJoinerAdvantage` — the joiner's win rate when they answer a known creator word with the best reply. The design artifact estimates ~53% overall (~6 in 11 natural finishes). Measured so decision 11's **accepted** asymmetry is priced rather than unknown.
4. `PoolSizeSweep_ReportsTheEdgeAtEachCandidateSize` — 1–3 repeated at several candidate pool sizes. **The chosen size is the OUTPUT.**

Each test writes a markdown table (sample size, pool size, best counter-word, its win rate, natural-finish share, fallback share, house edge) so the run can be pasted onto the Trello card.

- [ ] **Step 2: Verify it is excluded from the fast suite**

Run: `scripts\run_tests.bat`
Expected: PASS, and `DuelEdgeTests` **does not appear** in the output (the filter excludes `ProductionGames`).

- [ ] **Step 3: Run it once and capture the output**

Run: `scripts\run_production_session_tests.bat --filter "FullyQualifiedName~DuelEdgeTests"`
Expected: PASS, with the four tables in the output. This is a long run — expect minutes, not seconds.

⚠️ **Do not populate the pool from this run without the owner's sign-off.** AC 14 says the pool ships only if the house duel edge stays positive against the best player counter-word; that is the owner's call on the numbers, and it is a separate config change after this card (§13).

- [ ] **Step 4: Commit**

```bash
git add src/SpaceStroker.Tests/UnitTests/Domain/ProductionGames/DuelEdgeTests.cs
git commit -m "Measure the duel edge and the PVP joiner advantage as a launch gate."
git push origin feature/monkey-duel
```

---

## Final verification (before hand-off)

- [ ] `scripts\run_tests.bat` — green, and **record exactly which unit tests were added or changed** (`constraints.md` §3 requires reporting them).
- [ ] `scripts\run_integration_tests.bat` — green (Docker running).
- [ ] `git log --oneline origin/main..feature/monkey-duel` in the backend worktree — every task committed and pushed.
- [ ] Grep the diff for accidental real secrets: `git diff origin/main -- "*.json" | grep -iE "privatekey|secret|password"` — every hit must be a `MOCK_` value.
- [ ] Confirm the seeded type is still `"ComingSoon": true` with `"Words": []`.
- [ ] Confirm no new key was added to `ErrorMessages.resx` (the intent is that none is needed — §10.1 row 31).

---

## Self-review notes

**Spec coverage.** Every row of §10.1's 31-file checklist maps to a task: files 1–4 → Task 1; 5 → Task 2; 6 → Task 4; 7 → Task 3; 8 → Task 5; 9 → Task 6; 10–11 → Task 1; 12 → Task 7; 13 → Task 8; 14 → Task 12; 15 → Task 10; 16 → Task 15; 17 → Task 12; 18 → Tasks 7 + 13; 19 → Task 9; 20 → Task 6; 21–22 → Task 11; 23–24 → Task 14; 25 → Task 14; 26 → Task 13; 27 → Tasks 8 + 15; 28 → Tasks 7, 8, 12; 29 → Tasks 6 + 14; 30 → Tasks 5, 6, 7, 8, 12, 13; 31 → deliberately **not** touched. §9.1's unit-test table maps to Tasks 2, 3, 4, 10, 11, 12, 14, 15; §9.2's integration table to Task 17; §9.3 to Task 18. §6.9's "no change" rows are verified, not edited, in Task 14 Step 7.

**Known gaps handed to the executor.** Four test helpers do not exist yet and each task names the shipped file to copy them from (`GameServiceTestFactory`, `DuelHarness`, `DuelHouseBetHarness`, `CancelDuelHarness`, `HubDuelHarness`, plus the availability/active-game harnesses). Two API shapes must be matched against the real code rather than assumed: `PaginatedList<T>`'s constructor (Task 8) and the wallet-proof message format (Task 12). One provider limitation may need a fallback: `ExecuteUpdate` is a no-op on the in-memory provider (Task 6).

**Ordering.** Tasks 1–6 are independent of each other after Task 1. Task 7 needs a stub of Task 9's interface. Task 10 needs 1, 3, 9. Task 11 needs 1, 2, 10. Task 12 needs 1 and a stub of Task 13's broadcast. Tasks 13–16 need 1, 8, 10, 12. Task 17 needs everything. Task 18 needs only Task 2.

