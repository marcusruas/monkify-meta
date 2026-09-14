# Hyper Roll — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Hyper Roll player-vs-house preset game type to `monkify-backend`: tiered word pool, per-round board, free SOL stake within bounds, weighted provably-fair draw, `stake × multiplier` payout from the treasury.

**Architecture:** A new `GameParameters.IsHouseEdge` flag gates every behaviour change so fixed-price and legacy preset types stay byte-identical. The pool lives in `PresetChoice` (new `Multiplier` + `Active`), each round persists its board in a new `GameBoardChoices` table, the typer skips natural matching and draws the winner with integer weights over the existing `"<prefix>|fallback"` HMAC, and `RewardGameConsumer` pays `stake × multiplier` behind a running treasury-balance check.

**Tech Stack:** .NET 8, EF Core (Npgsql), MassTransit, SignalR, xUnit + Shouldly + Moq + Bogus, Testcontainers.

**Spec:** `monkify-meta/specs/hyper-roll.md` (authoritative). Owner decisions on 2026-09-14: mockup **A6** chosen, spec **§3.1 approved**.

## Global Constraints

- Work only in `E:\Monkify\.worktrees\hyper-roll\monkify-backend` on `feature/hyper-roll`; never touch the hub checkout.
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`. Never hand-edit `SpaceStrokerDbContextModelSnapshot.cs`.
- `SpaceStrokerDbContext` is **NoTracking**: persist updates with `ExecuteUpdate` or `context.Update()`, never load-mutate-save.
- Unit tests: `scripts\run_tests.bat` (never a bare `dotnet test` over the solution). Integration tests: `scripts\run_integration_tests.bat` (Docker must be running).
- Never add an optional parameter to a method that Moq verifies in expression trees (`IGameService.UpdateGameStatus`) — add a sibling method instead.
- Multiplier cap `10.00`, floor `1.01`, RTP tolerance `0.25` percentage points, treasury reserve `0.001` SOL per transfer, words 6–8 characters.
- Weight formula: `100_000_000 / round(multiplier × 100)` (integer division).
- Bet refusals reuse the existing vague `BetValidationResult` messages; never add state-specific copy.
- Commits end with:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_016QNAKdJo28eauNmES75WZr
  ```
- Push after every commit: `git push origin feature/hyper-roll`.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs` | `IsHouseEdge`, `MaximumStake`, `RtpPercent`, `BoardLadder` |
| `src/SpaceStroker.Domain/Games/Entities/PresetChoice.cs` | `Multiplier`, `Active` |
| `src/SpaceStroker.Domain/Games/Entities/GameBoardChoice.cs` (new) | One word on one round's board |
| `src/SpaceStroker.Domain/Games/Entities/Game.cs` | `BoardChoices` navigation |
| `src/SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs` (new) | Constants, ladder parse/format, weights, RTP, config validation |
| `src/SpaceStroker.Domain/Games/Services/HouseEdgeBoard.cs` (new) | Draws a board from the pool per the ladder |
| `src/SpaceStroker.Domain/Games/Services/ProvablyFair.cs` | `WeightedFallbackIndex` |
| `src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` | House-edge bets/pool, no natural win, weighted fallback |
| `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs` | House-edge validation + reward |
| `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs` | `HouseEdgeSettings`, `HouseEdgeWordSettings` |
| `src/SpaceStroker.Common/Resources/ErrorMessages.resx` + `.Designer.cs` | `TyperStartedWithoutBoard`, `HouseEdgeBoardEntryMissing` |
| `src/SpaceStroker.Common/spacestrokersettings.json` | Remove Beast Mode, add Hyper Roll |
| `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | Mapping |
| `src/SpaceStroker.Infrastructure/Migrations/*_HyperRoll.cs` | Generated |
| `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs` (new) | Startup self-heal of words/ladder/RTP |
| `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` + `IGameService.cs` | Board include, board draw in `CreateGame`, `UpdateGameStatusWithPayouts` |
| `src/SpaceStroker.Infrastructure/Contracts/Games/GameStatusUpdatedEvent.cs` | `GameResultPayout`, `GameResult.Payouts` |
| `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` | Treasury check + payouts |
| `src/SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs` | House-edge fields |
| `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs` | `BoardChoices` |
| `src/SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs` | `IsHouseEdge`, `BoardChoices` |
| `src/SpaceStroker.Infrastructure/Handlers/Games/GetActiveGameForParameter/GetActiveGameForParameterHandler.cs` | Board include |
| `src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs` | Board + weights |
| `src/SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` | Skip house-edge types |
| `src/SpaceStroker.Consumers/Program.cs` | Seed mapping + reconciler call |
| Tests (see each task) | |

---

### Task 1: Entities and domain rules

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/PresetChoice.cs`
- Create: `src/SpaceStroker.Domain/Games/Entities/GameBoardChoice.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs`
- Create: `src/SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs`
- Create: `src/SpaceStroker.Domain/Games/Services/HouseEdgeBoard.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeBoardTests.cs`

**Interfaces:**
- Produces: `GameParameters.IsHouseEdge` (bool), `MaximumStake` (decimal?), `RtpPercent` (decimal?), `BoardLadder` (string?); `PresetChoice.Multiplier` (decimal?), `PresetChoice.Active` (bool, default true); `GameBoardChoice { Game Game; Guid GameId; string Choice; decimal Multiplier; int Position; }`; `Game.BoardChoices` (ICollection<GameBoardChoice>, never null); `HouseEdgeRules.{MinMultiplier, MaxMultiplier, RtpTolerancePercent, TreasuryFeeReservePerTransfer, WordMinLength, WordMaxLength, BoardSizeMin}`; `HouseEdgeRules.ParseLadder(string?) : IReadOnlyList<decimal>`; `HouseEdgeRules.FormatLadder(IEnumerable<decimal>) : string`; `HouseEdgeRules.IsValidMultiplier(decimal) : bool`; `HouseEdgeRules.WeightFor(decimal) : int`; `HouseEdgeRules.RtpPercentOf(IReadOnlyList<decimal>) : decimal`; `HouseEdgeRules.Validate(GameParameters, IReadOnlyCollection<PresetChoice>) : HouseEdgeValidation`; `record HouseEdgeValidation(bool IsValid, string? Reason)`; `HouseEdgeBoard.Draw(IReadOnlyCollection<PresetChoice>, IReadOnlyList<decimal>, Random) : List<GameBoardChoice>?`.

- [ ] **Step 1: Write the failing rules tests**

Create `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games
{
    public class HouseEdgeRulesTests
    {
        private static readonly decimal[] LaunchLadder = { 4m, 6m, 8m, 9m, 10m, 10m, 10m, 10m };

        private static GameParameters ValidParameters() => new()
        {
            Name = "Hyper Roll",
            Description = "test",
            AllowedCharacters = GameCharacterType.Letters,
            ChoiceRequiredLength = 8,
            RequiredAmount = 0.01m,
            MaximumStake = 0.2m,
            RtpPercent = 95m,
            BoardLadder = "4,6,8,9,10,10,10,10",
            IsHouseEdge = true,
            MinimumNumberOfPlayers = 1,
        };

        private static List<PresetChoice> ValidPool() => new()
        {
            new("banana") { Multiplier = 4m },
            new("rocket") { Multiplier = 6m },
            new("gorilla") { Multiplier = 8m },
            new("gravity") { Multiplier = 9m },
            new("asteroid") { Multiplier = 10m },
            new("blastoff") { Multiplier = 10m },
            new("galactic") { Multiplier = 10m },
            new("mandrill") { Multiplier = 10m },
        };

        [Fact]
        public void RtpPercentOf_LaunchLadder_IsJustUnder95()
        {
            decimal.Round(HouseEdgeRules.RtpPercentOf(LaunchLadder), 3).ShouldBe(94.987m);
        }

        [Fact]
        public void ParseLadder_And_FormatLadder_RoundTrip()
        {
            var ladder = HouseEdgeRules.ParseLadder("4,6,8,9,10,10,10,10");
            ladder.ShouldBe(LaunchLadder);
            HouseEdgeRules.FormatLadder(ladder).ShouldBe("4,6,8,9,10,10,10,10");
            HouseEdgeRules.FormatLadder(new[] { 1.5m, 7.25m }).ShouldBe("1.5,7.25");
        }

        [Fact]
        public void ParseLadder_Blank_IsEmpty()
        {
            HouseEdgeRules.ParseLadder(null).ShouldBeEmpty();
            HouseEdgeRules.ParseLadder("  ").ShouldBeEmpty();
        }

        [Theory]
        [InlineData(4, 250000)]
        [InlineData(6, 166666)]
        [InlineData(8, 125000)]
        [InlineData(9, 111111)]
        [InlineData(10, 100000)]
        public void WeightFor_UsesTheIntegerFormula(int multiplier, int expected)
        {
            HouseEdgeRules.WeightFor(multiplier).ShouldBe(expected);
        }

        [Theory]
        [InlineData("1.00")]
        [InlineData("10.01")]
        [InlineData("4.125")]
        public void WeightFor_RejectsAnInvalidMultiplier(string multiplier)
        {
            Should.Throw<ArgumentOutOfRangeException>(() => HouseEdgeRules.WeightFor(decimal.Parse(multiplier, System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void Validate_ValidConfig_IsValid()
        {
            var result = HouseEdgeRules.Validate(ValidParameters(), ValidPool());
            result.IsValid.ShouldBeTrue(result.Reason);
        }

        [Fact]
        public void Validate_NotHouseEdge_Fails()
        {
            var parameters = ValidParameters();
            parameters.IsHouseEdge = false;
            HouseEdgeRules.Validate(parameters, ValidPool()).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_RtpOutOfTolerance_Fails()
        {
            var parameters = ValidParameters();
            parameters.RtpPercent = 97m;
            var result = HouseEdgeRules.Validate(parameters, ValidPool());
            result.IsValid.ShouldBeFalse();
            result.Reason.ShouldContain("RTP");
        }

        [Theory]
        [InlineData("4,6,8,9,10,10,10,11")]
        [InlineData("1,6,8,9,10,10,10,10")]
        [InlineData("4.125,6,8,9,10,10,10,10")]
        public void Validate_LadderWithAnInvalidMultiplier_Fails(string ladder)
        {
            var parameters = ValidParameters();
            parameters.BoardLadder = ladder;
            HouseEdgeRules.Validate(parameters, ValidPool()).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_MinimumStakeBelowTheRentFloor_Fails()
        {
            var parameters = ValidParameters();
            parameters.RequiredAmount = 0.0008m;
            HouseEdgeRules.Validate(parameters, ValidPool()).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_MaximumBelowMinimum_Fails()
        {
            var parameters = ValidParameters();
            parameters.MaximumStake = 0.005m;
            HouseEdgeRules.Validate(parameters, ValidPool()).IsValid.ShouldBeFalse();
        }

        [Theory]
        [InlineData("rock")]
        [InlineData("asteroids")]
        [InlineData("rocket1")]
        [InlineData("Rocket")]
        public void Validate_BadWord_Fails(string word)
        {
            var pool = ValidPool();
            pool[1] = new PresetChoice(word) { Multiplier = 6m };
            HouseEdgeRules.Validate(ValidParameters(), pool).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_DuplicateWord_Fails()
        {
            var pool = ValidPool();
            pool.Add(new PresetChoice("banana") { Multiplier = 4m });
            HouseEdgeRules.Validate(ValidParameters(), pool).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_WordMultiplierNotInLadder_Fails()
        {
            var pool = ValidPool();
            pool.Add(new PresetChoice("comets") { Multiplier = 5m });
            HouseEdgeRules.Validate(ValidParameters(), pool).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_WordWithoutMultiplier_Fails()
        {
            var pool = ValidPool();
            pool.Add(new PresetChoice("comets"));
            HouseEdgeRules.Validate(ValidParameters(), pool).IsValid.ShouldBeFalse();
        }

        [Fact]
        public void Validate_PoolShortForASlot_Fails()
        {
            var pool = ValidPool().Where(x => x.Choice != "mandrill").ToList();
            var result = HouseEdgeRules.Validate(ValidParameters(), pool);
            result.IsValid.ShouldBeFalse();
            result.Reason.ShouldContain("10");
        }
    }
}
```

- [ ] **Step 2: Write the failing board tests**

Create `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeBoardTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Games
{
    public class HouseEdgeBoardTests
    {
        private static readonly decimal[] Ladder = { 4m, 6m, 8m, 9m, 10m, 10m, 10m, 10m };

        private static List<PresetChoice> Pool() => new()
        {
            new("banana") { Multiplier = 4m }, new("cosmos") { Multiplier = 4m },
            new("rocket") { Multiplier = 6m }, new("planet") { Multiplier = 6m },
            new("gorilla") { Multiplier = 8m }, new("nebulae") { Multiplier = 8m },
            new("gravity") { Multiplier = 9m }, new("capsule") { Multiplier = 9m },
            new("asteroid") { Multiplier = 10m }, new("blastoff") { Multiplier = 10m },
            new("galactic") { Multiplier = 10m }, new("mandrill") { Multiplier = 10m },
            new("starship") { Multiplier = 10m },
        };

        [Fact]
        public void Draw_FollowsTheLadderMultiset()
        {
            var board = HouseEdgeBoard.Draw(Pool(), Ladder, new Random(7));

            board.ShouldNotBeNull();
            board!.Select(x => x.Multiplier).OrderBy(x => x).ShouldBe(Ladder.OrderBy(x => x));
        }

        [Fact]
        public void Draw_EightDistinctWords_WithPositionsZeroToSeven()
        {
            var board = HouseEdgeBoard.Draw(Pool(), Ladder, new Random(11))!;

            board.Select(x => x.Choice).Distinct().Count().ShouldBe(8);
            board.Select(x => x.Position).OrderBy(x => x).ShouldBe(Enumerable.Range(0, 8));
        }

        [Fact]
        public void Draw_EveryWordCarriesItsPoolMultiplier()
        {
            var pool = Pool();
            var board = HouseEdgeBoard.Draw(pool, Ladder, new Random(3))!;

            foreach (var choice in board)
                pool.Single(x => x.Choice == choice.Choice).Multiplier.ShouldBe(choice.Multiplier);
        }

        [Fact]
        public void Draw_SkipsInactiveWords()
        {
            var pool = Pool();
            pool.Single(x => x.Choice == "starship").Active = false;
            pool.Single(x => x.Choice == "cosmos").Active = false;

            for (var seed = 0; seed < 50; seed++)
            {
                var board = HouseEdgeBoard.Draw(pool, Ladder, new Random(seed))!;
                board.ShouldNotContain(x => x.Choice == "starship" || x.Choice == "cosmos");
            }
        }

        [Fact]
        public void Draw_SameSeed_SameBoard()
        {
            var first = HouseEdgeBoard.Draw(Pool(), Ladder, new Random(42))!.Select(x => (x.Choice, x.Position)).ToList();
            var second = HouseEdgeBoard.Draw(Pool(), Ladder, new Random(42))!.Select(x => (x.Choice, x.Position)).ToList();
            first.ShouldBe(second);
        }

        [Fact]
        public void Draw_PoolShortForASlot_ReturnsNull()
        {
            var pool = Pool().Where(x => x.Multiplier != 10m).Take(8).ToList();
            HouseEdgeBoard.Draw(pool, Ladder, new Random(1)).ShouldBeNull();
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~HouseEdge"` from `E:\Monkify\.worktrees\hyper-roll\monkify-backend`
Expected: build FAILS — `HouseEdgeRules`, `HouseEdgeBoard`, `PresetChoice.Multiplier` do not exist.

- [ ] **Step 4: Add the entity members**

In `src/SpaceStroker.Domain/Games/Entities/GameParameters.cs`, add after the `ComingSoon` property:

```csharp
        // Player-vs-house preset type (#183 Hyper Roll). A round offers BoardLadder.Count words drawn from
        // the ACTIVE PresetChoices pool, the winner is ALWAYS the weighted fallback draw, and a winner is
        // paid stake × multiplier from the treasury with no commission. False = today's pari-mutuel
        // behaviour, which is what every other type — and every legacy preset type — keeps.
        public bool IsHouseEdge { get; set; }

        // House-edge only. RequiredAmount is the MINIMUM stake; this is the maximum. Create-only, exactly
        // like RequiredAmount: changing a bet bound under a live SPA refuses bets after their SOL moved.
        public decimal? MaximumStake { get; set; }

        // House-edge only. Target return-to-player as a percent (95 = 95%). Validated against BoardLadder.
        public decimal? RtpPercent { get; set; }

        // House-edge only. The board recipe: comma-separated multipliers, invariant culture, e.g.
        // "4,6,8,9,10,10,10,10". Its length is the board size. See HouseEdgeRules.
        public string? BoardLadder { get; set; }
```

Replace the body of `src/SpaceStroker.Domain/Games/Entities/PresetChoice.cs` class with:

```csharp
    public class PresetChoice : TableEntity
    {
        public PresetChoice() { }
        public PresetChoice(string choice)
        {
            Choice = choice;
        }

        public GameParameters Parameters { get; set; }
        public Guid ParametersId { get; set; }
        public string Choice { get; set; }

        // House-edge pools only (#183): the word's multiplier — its tier. Null on legacy pari-mutuel rows.
        public decimal? Multiplier { get; set; }

        // House-edge pools only: false = retired from the pool, never drawn onto a board again. Legacy rows
        // stay true, so a legacy preset type reads every row exactly as before.
        public bool Active { get; set; } = true;
    }
```

Create `src/SpaceStroker.Domain/Games/Entities/GameBoardChoice.cs`:

```csharp
using SpaceStroker.Common.Models;
using System;

namespace SpaceStroker.Domain.Games.Entities
{
    /// <summary>
    /// One word on one house-edge round's board (#183 Hyper Roll). Written once, in the same SaveChanges
    /// as its Game, and never rewritten — so a later pool or ladder edit can never change what a settled
    /// round offered, paid or published for verification.
    /// </summary>
    public class GameBoardChoice : TableEntity
    {
        public Game Game { get; set; }
        public Guid GameId { get; set; }
        public string Choice { get; set; }
        public decimal Multiplier { get; set; }

        // 0-based display order, shuffled at draw time: key 1 is Position 0. The weighted draw does NOT
        // use this order — it orders by Choice (ordinal) so any language reproduces it.
        public int Position { get; set; }
    }
}
```

In `src/SpaceStroker.Domain/Games/Entities/Game.cs`, add after `public ICollection<GameStatusLog> StatusLogs { get; set; }`:

```csharp
        // House-edge rounds only (#183): the board drawn when the round opened. Empty for every other type.
        // Initialised here so a Game loaded without the Include — or built in a test — is never null.
        public ICollection<GameBoardChoice> BoardChoices { get; set; } = new List<GameBoardChoice>();
```

- [ ] **Step 5: Implement `HouseEdgeRules`**

Create `src/SpaceStroker.Domain/Games/Services/HouseEdgeRules.cs`:

```csharp
using SpaceStroker.Common.Extensions;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpaceStroker.Domain.Games.Services
{
    public sealed record HouseEdgeValidation(bool IsValid, string? Reason)
    {
        public static readonly HouseEdgeValidation Ok = new(true, null);
        public static HouseEdgeValidation Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// The money rules of a player-vs-house preset type (#183 Hyper Roll). A word's draw probability is
    /// RTP ÷ multiplier, so for every word to carry the same expected return Σ(1/m) over a board must equal
    /// 1/RTP. The board LADDER fixes that sum by construction; this class checks a configuration keeps it.
    ///
    /// ⚠️ A mis-tiered pool or ladder is a MONEY bug, not a cosmetic one — a board whose Σ(1/m) is too low
    /// pays out more than it takes. GameService.CreateGame refuses to open a round when Validate fails.
    /// </summary>
    public static class HouseEdgeRules
    {
        public const decimal MinMultiplier = 1.01m;
        public const decimal MaxMultiplier = 10.00m;
        public const decimal RtpTolerancePercent = 0.25m;
        public const decimal TreasuryFeeReservePerTransfer = 0.001m;
        public const int WordMinLength = 6;
        public const int WordMaxLength = 8;
        public const int BoardSizeMin = 2;

        public static IReadOnlyList<decimal> ParseLadder(string? boardLadder)
        {
            if (string.IsNullOrWhiteSpace(boardLadder))
                return Array.Empty<decimal>();

            return boardLadder
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => decimal.Parse(x, NumberStyles.Number, CultureInfo.InvariantCulture))
                .ToList();
        }

        public static string FormatLadder(IEnumerable<decimal> ladder)
            => string.Join(",", ladder.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)));

        public static bool IsValidMultiplier(decimal multiplier)
            => multiplier >= MinMultiplier && multiplier <= MaxMultiplier && decimal.Round(multiplier, 2) == multiplier;

        /// <summary>
        /// Integer draw weight for a multiplier: 100,000,000 / round(m × 100). Integer so the weighted draw is
        /// exact and identical in C#, TypeScript and the offline verifier. KEEP-IN-SYNC with
        /// monkify-front/src/utils/provablyFair.ts weightForMultiplier and public/verify.html.
        /// </summary>
        public static int WeightFor(decimal multiplier)
        {
            if (!IsValidMultiplier(multiplier))
                throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "Multiplier must be between 1.01 and 10.00 with at most 2 decimals.");

            return 100_000_000 / (int)decimal.Round(multiplier * 100m);
        }

        public static decimal RtpPercentOf(IReadOnlyList<decimal> ladder)
            => 100m / ladder.Sum(m => 1m / m);

        public static HouseEdgeValidation Validate(GameParameters parameters, IReadOnlyCollection<PresetChoice> activePool)
        {
            if (!parameters.IsHouseEdge)
                return HouseEdgeValidation.Fail("The type is not a house-edge type.");

            if (parameters.MaximumStake is null || parameters.RtpPercent is null || string.IsNullOrWhiteSpace(parameters.BoardLadder))
                return HouseEdgeValidation.Fail("MaximumStake, RtpPercent and BoardLadder are all required.");

            IReadOnlyList<decimal> ladder;
            try
            {
                ladder = ParseLadder(parameters.BoardLadder);
            }
            catch (FormatException)
            {
                return HouseEdgeValidation.Fail($"BoardLadder '{parameters.BoardLadder}' is not a comma-separated list of numbers.");
            }

            if (ladder.Count < BoardSizeMin)
                return HouseEdgeValidation.Fail($"BoardLadder needs at least {BoardSizeMin} slots.");

            if (ladder.Any(m => !IsValidMultiplier(m)))
                return HouseEdgeValidation.Fail($"BoardLadder '{parameters.BoardLadder}' has a multiplier outside 1.01–10.00 or with more than 2 decimals.");

            var rtp = RtpPercentOf(ladder);
            if (Math.Abs(rtp - parameters.RtpPercent.Value) > RtpTolerancePercent)
                return HouseEdgeValidation.Fail($"BoardLadder RTP {rtp:0.###}% does not match RtpPercent {parameters.RtpPercent.Value}%.");

            var rentFloorSol = (decimal)NativeSol.RentExemptMinimumLamports / NativeSol.LamportsPerSol;
            if (parameters.RequiredAmount < rentFloorSol)
                return HouseEdgeValidation.Fail($"The minimum stake {parameters.RequiredAmount} is below the rent-exempt floor {rentFloorSol}.");

            if (parameters.MaximumStake.Value < parameters.RequiredAmount)
                return HouseEdgeValidation.Fail("MaximumStake is below the minimum stake (RequiredAmount).");

            var alphabet = parameters.AllowedCharacters.StringValueOf();
            var ladderValues = ladder.ToHashSet();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var word in activePool)
            {
                if (word.Choice.Length < WordMinLength || word.Choice.Length > WordMaxLength)
                    return HouseEdgeValidation.Fail($"Word '{word.Choice}' must be {WordMinLength}–{WordMaxLength} characters.");

                if (!word.Choice.All(alphabet.Contains))
                    return HouseEdgeValidation.Fail($"Word '{word.Choice}' has characters outside the type's alphabet.");

                if (!seen.Add(word.Choice))
                    return HouseEdgeValidation.Fail($"Word '{word.Choice}' is in the pool twice.");

                if (word.Multiplier is null)
                    return HouseEdgeValidation.Fail($"Word '{word.Choice}' has no multiplier.");

                if (!ladderValues.Contains(word.Multiplier.Value))
                    return HouseEdgeValidation.Fail($"Word '{word.Choice}' has multiplier {word.Multiplier.Value}, which is not on the ladder.");
            }

            foreach (var slot in ladder.GroupBy(m => m))
            {
                var available = activePool.Count(x => x.Multiplier == slot.Key);
                if (available < slot.Count())
                    return HouseEdgeValidation.Fail($"The pool has {available} active word(s) at {slot.Key}× but the ladder needs {slot.Count()}.");
            }

            return HouseEdgeValidation.Ok;
        }
    }
}
```

- [ ] **Step 6: Implement `HouseEdgeBoard`**

Create `src/SpaceStroker.Domain/Games/Services/HouseEdgeBoard.cs`:

```csharp
using SpaceStroker.Domain.Games.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceStroker.Domain.Games.Services
{
    /// <summary>
    /// Draws one house-edge round's board (#183). Ordinary randomness by design (spec decision 13): every
    /// word on a board has the same expected value, so choosing which words appear gives the house nothing
    /// to exploit. Only the WINNER draw is provably fair.
    /// </summary>
    public static class HouseEdgeBoard
    {
        /// <returns>The board (GameId unset), or null when a ladder slot has too few active words.</returns>
        public static List<GameBoardChoice>? Draw(IReadOnlyCollection<PresetChoice> activePool, IReadOnlyList<decimal> ladder, Random rng)
        {
            var board = new List<GameBoardChoice>(ladder.Count);

            foreach (var slot in ladder.GroupBy(m => m))
            {
                var candidates = activePool
                    .Where(x => x.Active && x.Multiplier == slot.Key)
                    .Select(x => x.Choice)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var needed = slot.Count();
                if (candidates.Count < needed)
                    return null;

                // Partial Fisher–Yates: the first `needed` entries become a uniform sample without repeats.
                for (var i = 0; i < needed; i++)
                {
                    var j = rng.Next(i, candidates.Count);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                    board.Add(new GameBoardChoice { Choice = candidates[i], Multiplier = slot.Key });
                }
            }

            for (var i = board.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (board[i], board[j]) = (board[j], board[i]);
            }

            for (var i = 0; i < board.Count; i++)
                board[i].Position = i;

            return board;
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~HouseEdge"`
Expected: PASS (all `HouseEdgeRulesTests` and `HouseEdgeBoardTests`).

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeRulesTests.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeBoardTests.cs
git commit -m "Add Hyper Roll entities, house-edge rules and board draw."
git push origin feature/hyper-roll
```

---

### Task 2: EF mapping and migration

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
- Create (generated): `src/SpaceStroker.Infrastructure/Migrations/<timestamp>_HyperRoll.cs` (+ `.Designer.cs`, snapshot update)

**Interfaces:**
- Consumes: Task 1 entity members.
- Produces: `SpaceStrokerDbContext.GameBoardChoices` DbSet; the `HyperRoll` migration.

- [ ] **Step 1: Add the DbSet**

In `SpaceStrokerDbContext.cs`, after `public DbSet<PresetChoice> PresetChoices { get; set; }` add:

```csharp
        public DbSet<GameBoardChoice> GameBoardChoices { get; set; }
```

- [ ] **Step 2: Map the new columns inline**

In the `modelBuilder.Entity<GameParameters>` builder, after `builder.Property(x => x.Active).IsRequired().HasDefaultValue(false);` add:

```csharp
                // #183 Hyper Roll. All four are only meaningful when IsHouseEdge is true.
                builder.Property(x => x.IsHouseEdge).IsRequired().HasDefaultValue(false);
                builder.Property(x => x.MaximumStake).HasPrecision(18, 9).IsRequired(false);
                builder.Property(x => x.RtpPercent).HasPrecision(5, 2).IsRequired(false);
                builder.Property(x => x.BoardLadder).HasMaxLength(64).IsRequired(false);
```

Replace the `modelBuilder.Entity<PresetChoice>` builder with:

```csharp
            modelBuilder.Entity<PresetChoice>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.Property(x => x.Choice).IsRequired().HasMaxLength(20);
                // #183 — house-edge pools only. No unique (ParametersId, Choice) index: an admin-created legacy
                // row in a dev database may already duplicate, which would fail the migration. Pool uniqueness is
                // enforced by HouseEdgeRules.Validate and the reconciler instead.
                builder.Property(x => x.Multiplier).HasPrecision(6, 2).IsRequired(false);
                builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);
            });

            modelBuilder.Entity<GameBoardChoice>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.Property(x => x.Choice).IsRequired().HasMaxLength(20);
                builder.Property(x => x.Multiplier).HasPrecision(6, 2).IsRequired();
                builder.Property(x => x.Position).IsRequired();
                builder.HasIndex(x => x.GameId);
            });
```

In the `modelBuilder.Entity<Game>` builder, after `builder.HasMany(x => x.StatusLogs).WithOne(x => x.Game).HasForeignKey(x => x.GameId);` add:

```csharp
                builder.HasMany(x => x.BoardChoices).WithOne(x => x.Game).HasForeignKey(x => x.GameId);
```

- [ ] **Step 3: Generate the migration**

Run from `src/SpaceStroker.Infrastructure`:

```bash
dotnet ef migrations add HyperRoll --startup-project ../SpaceStroker.Consumers/SpaceStroker.Consumers.csproj --context SpaceStrokerDbContext
```

Expected: `Done.` and three changed files under `Migrations/`. Open `<timestamp>_HyperRoll.cs` and confirm `Up` adds exactly: `GameParameters.IsHouseEdge` (bool, default false), `MaximumStake` (numeric(18,9) null), `RtpPercent` (numeric(5,2) null), `BoardLadder` (varchar(64) null); `PresetChoices.Multiplier` (numeric(6,2) null), `PresetChoices.Active` (bool, default true); table `GameBoardChoices` with FK to `Games` and index on `GameId`. Nothing else.

- [ ] **Step 4: Run the full unit suite**

Run: `scripts\run_tests.bat`
Expected: PASS (no behaviour changed yet).

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs src/SpaceStroker.Infrastructure/Migrations
git commit -m "Map the Hyper Roll columns and board table."
git push origin feature/hyper-roll
```

---

### Task 3: Weighted fallback draw

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Services/ProvablyFair.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/ProvablyFairTests.cs`

**Interfaces:**
- Consumes: `HouseEdgeRules.WeightFor` (Task 1).
- Produces: `ProvablyFair.WeightedFallbackIndex(string serverSeed, string prefix, IReadOnlyList<int> weights) : int`.

- [ ] **Step 1: Write the failing tests**

Append inside the `ProvablyFairTests` class:

```csharp
        // ---- V3: weighted house-edge fallback (#183 Hyper Roll) ----
        // KEEP-IN-SYNC: monkify-front/src/utils/provablyFair.ts and public/verify.html must reproduce this.
        private const string V3ServerSeed = "4f3c2a1b0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ffeeddccbbaa00";
        private const string V3Prefix = "11111111-1111-1111-1111-111111111111|seedA|seedB";
        // Board in ORDINAL order: asteroid 10, banana 4, capsule 9, eclipse 8, mandrill 10, rocket 6, starship 10, wormhole 10.
        private static readonly int[] V3Weights = { 100000, 250000, 111111, 125000, 100000, 166666, 100000, 100000 };
        private const int V3WeightedIndex = 5; // rocket

        [Fact]
        public void WeightedFallbackIndex_V3_MatchesTheFrozenVector()
        {
            ProvablyFair.WeightedFallbackIndex(V3ServerSeed, V3Prefix, V3Weights).ShouldBe(V3WeightedIndex);
        }

        [Fact]
        public void WeightedFallbackIndex_V3Weights_AreTheLadderFormula()
        {
            new[] { 10m, 4m, 9m, 8m, 10m, 6m, 10m, 10m }.Select(HouseEdgeRules.WeightFor).ShouldBe(V3Weights);
        }

        [Fact]
        public void WeightedFallbackIndex_Distribution_TracksTheWeights()
        {
            const int draws = 50_000;
            var counts = new int[V3Weights.Length];
            var total = V3Weights.Sum();

            for (var i = 0; i < draws; i++)
                counts[ProvablyFair.WeightedFallbackIndex(V3ServerSeed, V3Prefix + "|" + i, V3Weights)]++;

            for (var index = 0; index < V3Weights.Length; index++)
            {
                var expected = (double)V3Weights[index] / total;
                var observed = (double)counts[index] / draws;
                Math.Abs(observed - expected).ShouldBeLessThan(0.01, $"index {index}");
            }
        }

        [Fact]
        public void WeightedFallbackIndex_RejectsEmptyOrNonPositiveWeights()
        {
            Should.Throw<ArgumentOutOfRangeException>(() => ProvablyFair.WeightedFallbackIndex(V3ServerSeed, V3Prefix, Array.Empty<int>()));
            Should.Throw<ArgumentOutOfRangeException>(() => ProvablyFair.WeightedFallbackIndex(V3ServerSeed, V3Prefix, new[] { 1, 0 }));
        }
```

Also add `V3` to the class `<summary>` block, after the `V2:` lines:

```csharp
    /// V3 (weighted, #183): serverSeed = "4f3c…aa00"; prefix = "11111111-1111-1111-1111-111111111111|seedA|seedB";
    ///     weights = [100000,250000,111111,125000,100000,166666,100000,100000] (T = 1052777) →
    ///     first u32 2664242784 accepted, r = 716974 → WeightedFallbackIndex = 5.
```

Make sure the file has `using SpaceStroker.Domain.Games.Services;` (it does) and `using System.Linq;` (it does).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~ProvablyFairTests"`
Expected: build FAILS — `WeightedFallbackIndex` does not exist.

- [ ] **Step 3: Implement**

In `ProvablyFair.cs`, add after `FallbackIndex`:

```csharp
        /// <summary>
        /// Weighted fallback draw for house-edge rounds (#183 Hyper Roll). Deterministic and portable:
        /// T = Σ weights; limit = 2^32 − (2^32 mod T); block 0 is HMAC-SHA256(serverSeed, prefix + "|fallback")
        /// — the same bytes FallbackIndex uses — and block k ≥ 1 is HMAC(serverSeed, prefix + "|fallback|" + k).
        /// Each block is read as 8 big-endian unsigned 32-bit integers; the first u &lt; limit gives
        /// r = u mod T, and the winner is the first index whose running weight exceeds r.
        /// KEEP-IN-SYNC with monkify-front/src/utils/provablyFair.ts and public/verify.html.
        /// </summary>
        public static int WeightedFallbackIndex(string serverSeed, string prefix, IReadOnlyList<int> weights)
        {
            if (weights == null || weights.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(weights));

            long total = 0;
            foreach (var weight in weights)
            {
                if (weight <= 0)
                    throw new ArgumentOutOfRangeException(nameof(weights));
                total += weight;
            }

            const long twoPow32 = 4294967296L;
            var limit = twoPow32 - (twoPow32 % total);
            var key = Encoding.UTF8.GetBytes(serverSeed);

            for (long block = 0; ; block++)
            {
                var label = block == 0 ? prefix + "|fallback" : prefix + "|fallback|" + block;
                var bytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label));

                for (var i = 0; i + 3 < bytes.Length; i += 4)
                {
                    long u = ((long)bytes[i] << 24) | ((long)bytes[i + 1] << 16) | ((long)bytes[i + 2] << 8) | bytes[i + 3];
                    if (u >= limit)
                        continue;

                    var r = u % total;
                    for (var index = 0; index < weights.Count; index++)
                    {
                        if (r < weights[index])
                            return index;
                        r -= weights[index];
                    }
                }
            }
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~ProvablyFairTests"`
Expected: PASS (V1, V2 unchanged; V3 index 5).

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain/Games/Services/ProvablyFair.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/ProvablyFairTests.cs
git commit -m "Add the weighted house-edge fallback draw with a frozen vector."
git push origin feature/hyper-roll
```

---

### Task 4: Typer house-edge behaviour

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs`
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx`, `src/SpaceStroker.Common/Resources/ErrorMessages.Designer.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/SpaceStrokerTyperTests.cs`

**Interfaces:**
- Consumes: `Game.BoardChoices`, `GameParameters.IsHouseEdge` (Task 1); `ProvablyFair.WeightedFallbackIndex` (Task 3); `HouseEdgeRules.WeightFor` (Task 1).
- Produces: `SpaceStrokerTyper.IsHouseEdge` (bool); `ErrorMessages.TyperStartedWithoutBoard`; `ErrorMessages.HouseEdgeBoardEntryMissing` (used in Task 5).

- [ ] **Step 1: Add the resource strings**

In `ErrorMessages.resx`, add before `<data name="TyperStartedWithoutServerSeed"`:

```xml
  <data name="TyperStartedWithoutBoard" xml:space="preserve">
    <value>A house-edge game cannot run without its board. Game has ended</value>
  </data>
```

and before `<data name="OneBetPerPresetRound"`:

```xml
  <data name="HouseEdgeBoardEntryMissing" xml:space="preserve">
    <value>The bet's choice is not on this round's board.</value>
  </data>
```

In `ErrorMessages.Designer.cs`, add inside the class (alphabetical placement is not required):

```csharp
        /// <summary>
        ///   Consulta uma cadeia de caracteres localizada semelhante a A house-edge game cannot run without its board. Game has ended.
        /// </summary>
        public static string TyperStartedWithoutBoard {
            get {
                return ResourceManager.GetString("TyperStartedWithoutBoard", resourceCulture);
            }
        }

        /// <summary>
        ///   Consulta uma cadeia de caracteres localizada semelhante a The bet's choice is not on this round's board..
        /// </summary>
        public static string HouseEdgeBoardEntryMissing {
            get {
                return ResourceManager.GetString("HouseEdgeBoardEntryMissing", resourceCulture);
            }
        }
```

- [ ] **Step 2: Write the failing typer tests**

Append inside `SpaceStrokerTyperTests`:

```csharp
        // ---- House-edge (#183 Hyper Roll) ----

        private static readonly (string Choice, decimal Multiplier)[] HouseEdgeBoardWords =
        {
            ("rocket", 6m), ("asteroid", 10m), ("banana", 4m), ("wormhole", 10m),
            ("capsule", 9m), ("starship", 10m), ("eclipse", 8m), ("mandrill", 10m),
        };

        private Game HouseEdgeGame(params string[] betChoices)
        {
            var parameters = new GameParameters
            {
                Name = "Hyper Roll",
                Description = "test",
                AllowedCharacters = GameCharacterType.Letters,
                ChoiceRequiredLength = 8,
                AcceptDuplicatedCharacters = true,
                RequiredAmount = 0.01m,
                MaximumStake = 0.2m,
                RtpPercent = 95m,
                BoardLadder = "4,6,8,9,10,10,10,10",
                IsHouseEdge = true,
                // A pool bigger than the board — the typer must register only the board.
                PresetChoices = HouseEdgeBoardWords.Select(x => new PresetChoice(x.Choice) { Multiplier = x.Multiplier })
                    .Append(new PresetChoice("gravity") { Multiplier = 9m })
                    .ToList(),
            };

            var game = new Game(parameters, new List<Bet>());
            game.BoardChoices = HouseEdgeBoardWords
                .Select((x, i) => new GameBoardChoice { GameId = game.Id, Choice = x.Choice, Multiplier = x.Multiplier, Position = i })
                .ToList();

            foreach (var choice in betChoices)
                game.Bets.Add(new Bet(game.Id, Faker.Random.String2(64, "0123456789abcdef"), Guid.NewGuid(), choice, 0.05m));

            return WithSeed(game);
        }

        [Fact]
        public void HouseEdge_EmptyBoard_Throws()
        {
            var game = HouseEdgeGame("rocket");
            game.BoardChoices = new List<GameBoardChoice>();

            var exception = Should.Throw<ArgumentException>(() => new SpaceStrokerTyper(game, InstantPacing(1_000)));
            exception.Message.ShouldBe(ErrorMessages.TyperStartedWithoutBoard);
        }

        [Fact]
        public void HouseEdge_RegistersOnlyTheBoard_NotThePool()
        {
            var typer = new SpaceStrokerTyper(HouseEdgeGame("rocket"), InstantPacing(1_000));

            typer.IsHouseEdge.ShouldBeTrue();
            typer.Bets.Keys.ShouldBe(HouseEdgeBoardWords.Select(x => x.Choice), ignoreOrder: true);
            typer.Bets["rocket"].ShouldBe(1);
            typer.QueueLength.ShouldBe(8);
        }

        [Fact]
        public void HouseEdge_PoolIsTheBoardCharacters()
        {
            var typer = new SpaceStrokerTyper(HouseEdgeGame("rocket"), InstantPacing(1_000));
            var boardCharacters = HouseEdgeBoardWords.SelectMany(x => x.Choice).ToHashSet();

            boardCharacters.ShouldAllBe(c => typer.CharactersOnTyper.Contains(c));
        }

        [Fact]
        public async Task HouseEdge_RunsToTheCap_AndDecidesByTheWeightedDraw()
        {
            var game = HouseEdgeGame("rocket", "banana");
            var typer = new SpaceStrokerTyper(game, InstantPacing(2_000));

            while (!typer.HasWinners)
                await typer.GenerateNextCharacter(CancellationToken.None);

            typer.WinnerDecidedByFallback.ShouldBeTrue();
            typer.CharactersTypedCount.ShouldBe(typer.MaxCharacters);

            var ordered = HouseEdgeBoardWords.OrderBy(x => x.Choice, StringComparer.Ordinal).ToArray();
            var expectedIndex = ProvablyFair.WeightedFallbackIndex(
                game.ServerSeed,
                OraclePrefix(game),
                ordered.Select(x => HouseEdgeRules.WeightFor(x.Multiplier)).ToArray());

            typer.FirstChoiceTyped.ShouldBe(ordered[expectedIndex].Choice);
        }

        [Fact]
        public async Task HouseEdge_NeverDeclaresANaturalWin_EvenWhenABoardWordIsTyped()
        {
            // A two-character alphabet over "aaaaaa"-shaped words guarantees board words appear in the stream
            // within a few hundred characters; a natural match there would end the run early.
            var parameters = new GameParameters
            {
                Name = "Tiny", Description = "test", AllowedCharacters = GameCharacterType.Letters,
                ChoiceRequiredLength = 6, AcceptDuplicatedCharacters = true, IsHouseEdge = true,
                BoardLadder = "4,6", RtpPercent = 96m, MaximumStake = 1m, RequiredAmount = 0.01m,
            };
            var game = new Game(parameters, new List<Bet>());
            game.BoardChoices = new List<GameBoardChoice>
            {
                new() { GameId = game.Id, Choice = "aaaaaa", Multiplier = 4m, Position = 0 },
                new() { GameId = game.Id, Choice = "bbbbbb", Multiplier = 6m, Position = 1 },
            };
            game.Bets.Add(new Bet(game.Id, Faker.Random.String2(64, "0123456789abcdef"), Guid.NewGuid(), "aaaaaa", 0.05m));
            WithSeed(game);

            var typer = new SpaceStrokerTyper(game, InstantPacing(20_000));

            while (!typer.HasWinners)
                await typer.GenerateNextCharacter(CancellationToken.None);

            typer.CharactersTypedCount.ShouldBe(typer.MaxCharacters);
            typer.WinnerDecidedByFallback.ShouldBeTrue();
        }
```

Add `using System.Threading;` at the top if missing.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~SpaceStrokerTyperTests"`
Expected: build FAILS — `IsHouseEdge` does not exist on `SpaceStrokerTyper`.

- [ ] **Step 4: Implement the typer changes**

In `SpaceStrokerTyper.cs`:

1. In the constructor, after the `ServerSeed` check and before `GameId = game.Id;`, add:

```csharp
            IsHouseEdge = game.Parameters?.IsHouseEdge == true;
            if (IsHouseEdge && game.BoardChoices.IsNullOrEmpty())
                throw new ArgumentException(ErrorMessages.TyperStartedWithoutBoard);

            _boardMultipliers = IsHouseEdge
                ? game.BoardChoices.ToDictionary(x => x.Choice, x => x.Multiplier, StringComparer.Ordinal)
                : new Dictionary<string, decimal>(StringComparer.Ordinal);
```

2. Add the members next to `public bool IsLottery { get; }`:

```csharp
        /// <summary>#183 Hyper Roll: natural matching is off and the winner is always the weighted draw.</summary>
        public bool IsHouseEdge { get; }

        private readonly Dictionary<string, decimal> _boardMultipliers;
```

3. At the top of `CheckForWinners()`, add:

```csharp
            // A house-edge winner is ALWAYS the weighted draw (spec decision 11). With 6–8-letter words a
            // natural match could hand a win to a word the weighted draw did not pick, breaking the RTP.
            if (IsHouseEdge)
                return;
```

4. Replace the first three lines of `DecideFallbackWinner()` (the `orderedChoices`, `index`, `winningChoice` declarations) with:

```csharp
            string winningChoice;

            if (IsHouseEdge)
            {
                var board = _boardMultipliers.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
                var weights = board.Select(x => HouseEdgeRules.WeightFor(x.Value)).ToArray();
                winningChoice = board[ProvablyFair.WeightedFallbackIndex(_serverSeed, _prefix, weights)].Key;
            }
            else
            {
                var orderedChoices = Bets.Keys.OrderBy(choice => choice, StringComparer.Ordinal).ToArray();
                var index = ProvablyFair.FallbackIndex(_serverSeed, _prefix, orderedChoices.Length);
                winningChoice = orderedChoices[index];
            }
```

and change `NumberOfWinners = Bets[winningChoice];` to:

```csharp
            NumberOfWinners = Bets.TryGetValue(winningChoice, out var winnerCount) ? winnerCount : 0;
```

5. In `SetBets(Game game)`, replace `if (!game.Parameters.PresetChoices.IsNullOrEmpty())` block's opening line with a house-edge branch first:

```csharp
            if (IsHouseEdge)
            {
                foreach (var boardChoice in game.BoardChoices)
                {
                    if (boardChoice.Choice.Length > QueueLength)
                        QueueLength = boardChoice.Choice.Length;

                    Bets.TryAdd(boardChoice.Choice, 0);
                }
            }
            else if (!game.Parameters.PresetChoices.IsNullOrEmpty())
```

(keep the existing preset loop body under the `else if`).

6. In `SetCharactersOnTyper(Game game)`, replace `if (!game.Parameters.PresetChoices.IsNullOrEmpty())` with:

```csharp
            if (IsHouseEdge)
            {
                foreach (var character in game.BoardChoices.SelectMany(x => x.Choice))
                    availableCharacters.Add(character);
            }
            else if (!game.Parameters.PresetChoices.IsNullOrEmpty())
```

⚠️ `IsHouseEdge` and `_boardMultipliers` are assigned before `InitializeKeystream`, `SetBets`, `SetCharactersOnTyper` run, because they are set right after the server-seed check.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~SpaceStrokerTyperTests"`
Expected: PASS, including every pre-existing typer test.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs src/SpaceStroker.Common/Resources src/SpaceStroker.Tests/UnitTests/Domain/Games/SpaceStrokerTyperTests.cs
git commit -m "Run Hyper Roll rounds to the cap and pick the winner by weight."
git push origin feature/hyper-roll
```

---

### Task 5: Bet validation and reward math

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/BetDomainServiceTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `ErrorMessages.HouseEdgeBoardEntryMissing` (Task 4).
- Produces: house-edge branch of `BetDomainService.ChoiceIsValidForGame(Bet, Game, bool)` and `CalculateRewardForBet(Bet)`; `BetDomainService.Winners` ordered by `(CreatedDate, Id ordinal)` for house-edge games.

- [ ] **Step 1: Write the failing domain tests**

Append inside `BetDomainServiceTests`:

```csharp
        // ---- House-edge (#183 Hyper Roll) ----

        private static Game HouseEdgeGame()
        {
            var parameters = new GameParameters
            {
                Name = "Hyper Roll", Description = "test", AllowedCharacters = GameCharacterType.Letters,
                ChoiceRequiredLength = 8, AcceptDuplicatedCharacters = true, IsHouseEdge = true,
                RequiredAmount = 0.01m, MaximumStake = 0.2m, RtpPercent = 95m, BoardLadder = "4,6,8,9,10,10,10,10",
                PresetChoices = new List<PresetChoice> { new("gravity") { Multiplier = 9m } },
            };
            var game = new Game(parameters, new List<Bet>());
            game.BoardChoices = new List<GameBoardChoice>
            {
                new() { GameId = game.Id, Choice = "rocket", Multiplier = 6m, Position = 0 },
                new() { GameId = game.Id, Choice = "asteroid", Multiplier = 10m, Position = 1 },
                new() { GameId = game.Id, Choice = "banana", Multiplier = 4m, Position = 2 },
            };
            return game;
        }

        private static Bet HouseEdgeBet(Game game, string choice, decimal amount, BetStatus status = BetStatus.Made)
            => new(game.Id, new string('a', 64), Guid.NewGuid(), choice, amount) { Status = status };

        [Theory]
        [InlineData("0.009")]
        [InlineData("0.2000000001")]
        [InlineData("0.21")]
        [InlineData("0.0100000000001")]
        public void ChoiceIsValidForGame_HouseEdge_StakeOutOfBoundsOrTooFine_IsInvalidAmount(string amount)
        {
            var game = HouseEdgeGame();
            var bet = HouseEdgeBet(game, "rocket", decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

            BetDomainService.ChoiceIsValidForGame(bet, game).ShouldBe(BetValidationResult.InvalidAmount);
        }

        [Theory]
        [InlineData("0.01")]
        [InlineData("0.2")]
        [InlineData("0.123456789")]
        public void ChoiceIsValidForGame_HouseEdge_StakeInsideBounds_IsValid(string amount)
        {
            var game = HouseEdgeGame();
            var bet = HouseEdgeBet(game, "asteroid", decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

            BetDomainService.ChoiceIsValidForGame(bet, game).ShouldBe(BetValidationResult.Valid);
        }

        [Fact]
        public void ChoiceIsValidForGame_HouseEdge_PoolWordNotOnBoard_IsInvalidChoice()
        {
            var game = HouseEdgeGame();
            BetDomainService.ChoiceIsValidForGame(HouseEdgeBet(game, "gravity", 0.05m), game).ShouldBe(BetValidationResult.InvalidChoice);
        }

        [Fact]
        public void ChoiceIsValidForGame_HouseEdge_SixLetterBoardWord_IgnoresChoiceRequiredLength()
        {
            var game = HouseEdgeGame();
            BetDomainService.ChoiceIsValidForGame(HouseEdgeBet(game, "banana", 0.05m), game).ShouldBe(BetValidationResult.Valid);
        }

        [Fact]
        public void ChoiceIsValidForGame_HouseEdge_FreeBet_MustBeZero()
        {
            var game = HouseEdgeGame();
            BetDomainService.ChoiceIsValidForGame(HouseEdgeBet(game, "rocket", 0m), game, isFreeBet: true).ShouldBe(BetValidationResult.Valid);
            BetDomainService.ChoiceIsValidForGame(HouseEdgeBet(game, "rocket", 0.05m), game, isFreeBet: true).ShouldBe(BetValidationResult.InvalidAmount);
        }

        [Fact]
        public void CalculateRewardForBet_HouseEdge_PaysStakeTimesMultiplier_WithNoCommission()
        {
            var game = HouseEdgeGame();
            var winner = HouseEdgeBet(game, "asteroid", 0.037m, BetStatus.NeedsRewarding);
            game.Bets.Add(winner);
            game.Bets.Add(HouseEdgeBet(game, "rocket", 0.2m));

            var service = new BetDomainService(game, new TokenSettings { Decimals = 9, CommissionPercentage = 5m });
            var result = service.CalculateRewardForBet(winner);

            result.ErrorMessage.ShouldBeNull();
            result.Value.ShouldBe(0.37m);
            result.ValueInTokens.ShouldBe(370_000_000UL);
        }

        [Fact]
        public void CalculateRewardForBet_HouseEdge_PriorCredit_IsAlreadyRewarded()
        {
            var game = HouseEdgeGame();
            var winner = HouseEdgeBet(game, "rocket", 0.05m, BetStatus.NeedsRewarding);
            winner.TransactionLogs = new List<TransactionLog> { new() { Type = TransactionLogType.Credit, Amount = 0.3m } };
            game.Bets.Add(winner);

            var result = new BetDomainService(game, new TokenSettings { Decimals = 9 }).CalculateRewardForBet(winner);

            result.ErrorMessage.ShouldBe(ErrorMessages.BetHasAlreadyBeenRewarded);
        }

        [Fact]
        public void CalculateRewardForBet_HouseEdge_ChoiceMissingFromBoard_ReturnsError()
        {
            var game = HouseEdgeGame();
            var winner = HouseEdgeBet(game, "gravity", 0.05m, BetStatus.NeedsRewarding);
            game.Bets.Add(winner);

            var result = new BetDomainService(game, new TokenSettings { Decimals = 9 }).CalculateRewardForBet(winner);

            result.ErrorMessage.ShouldBe(ErrorMessages.HouseEdgeBoardEntryMissing);
        }

        [Fact]
        public void Winners_HouseEdge_AreOrderedByCreatedDateThenId()
        {
            var game = HouseEdgeGame();
            var later = HouseEdgeBet(game, "rocket", 0.05m, BetStatus.NeedsRewarding);
            later.CreatedDate = DateTime.UtcNow;
            var earlier = HouseEdgeBet(game, "rocket", 0.05m, BetStatus.NeedsRewarding);
            earlier.CreatedDate = later.CreatedDate.AddSeconds(-5);
            game.Bets.Add(later);
            game.Bets.Add(earlier);

            new BetDomainService(game, new TokenSettings { Decimals = 9 }).Winners.First().ShouldBe(earlier);
        }
```

- [ ] **Step 2: Write the failing handler test**

Append inside `RegisterBetHandlerTests`, next to `RegisterBet_PresetGame_SamePlayerDifferentChoice_ShouldRefund`:

```csharp
        [Fact]
        public async Task RegisterBet_HouseEdgeGame_SecondBetFromSamePlayer_ShouldRefund()
        {
            var requestBody = SolanaBody("rocket");
            var game = new Game { Status = GameStatus.WaitingBets };
            game.Bets.Add(BetTestFactory.CreateGameBet(game.Id, HmacSeed(), Faker.Random.String2(88), requestBody.Wallet, "banana", 2));
            game.Parameters = CreateGameParameters();
            game.Parameters.IsHouseEdge = true;
            game.Parameters.MaximumStake = 5m;
            game.Parameters.RtpPercent = 95m;
            game.Parameters.BoardLadder = "4,6";
            game.Parameters.PresetChoices = [new PresetChoice("banana") { Multiplier = 4m }, new PresetChoice("rocket") { Multiplier = 6m }];
            game.BoardChoices = new List<GameBoardChoice>
            {
                new() { GameId = game.Id, Choice = "banana", Multiplier = 4m, Position = 0 },
                new() { GameId = game.Id, Choice = "rocket", Multiplier = 6m, Position = 1 },
            };

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);

            using var context = new SpaceStrokerDbContext(ContextOptions);
            PersistGame(context, game);
            context.ChangeTracker.Clear();

            var request = new RegisterBetRequest(game.Id, requestBody);
            var expected = ErrorMessages.OneBetPerPresetRound + " " + ErrorMessages.RefundWarning;
            await ShouldReturnValidationFailure(CreateHandler(context).HandleRequest(request, CancellationToken), expected);

            _messageBusMock.Verify(x => x.PublishEventAsync(It.IsAny<RefundInvalidBetEvent>()), Times.Once);
            _gameServiceMock.Verify(x => x.InsertBet(It.IsAny<Bet>()), Times.Never);
        }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~BetDomainServiceTests|FullyQualifiedName~RegisterBetHandlerTests"`
Expected: the new domain tests FAIL (e.g. a 6-letter word returns `WrongChoiceLength`, reward returns a pot share with commission). The handler test may already pass — it pins behaviour that must not regress.

- [ ] **Step 4: Implement**

In `BetDomainService.cs`:

1. Add `using SpaceStroker.Domain.Games.ValueObjects;` if missing (it is present) — `NativeSol` lives there.

2. Replace the constructor's winner materialisation line
`Winners = game.Bets.Where(x => x.Status == BetStatus.NeedsRewarding).ToList();` with:

```csharp
            _isHouseEdge = game.Parameters?.IsHouseEdge == true;

            // House-edge payouts draw on the TREASURY with a running balance check, so the order winners are
            // paid in must be deterministic — earliest bet first. Pari-mutuel order is unchanged.
            Winners = _isHouseEdge
                ? game.Bets.Where(x => x.Status == BetStatus.NeedsRewarding)
                    .OrderBy(x => x.CreatedDate)
                    .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal)
                    .ToList()
                : game.Bets.Where(x => x.Status == BetStatus.NeedsRewarding).ToList();

            _boardMultipliers = (game.BoardChoices ?? new List<GameBoardChoice>())
                .ToDictionary(x => x.Choice, x => x.Multiplier, StringComparer.Ordinal);
```

3. Add fields beside `_commissionRate`:

```csharp
        private readonly bool _isHouseEdge;
        private readonly Dictionary<string, decimal> _boardMultipliers;
```

4. At the top of `ChoiceIsValidForGame`, right after `var parameters = game.Parameters;`, add:

```csharp
            if (parameters.IsHouseEdge)
                return HouseEdgeChoiceIsValid(bet, game, isFreeBet);
```

5. In `CalculateRewardForBet`, right after the `NeedsRewarding` status check, add:

```csharp
            if (_isHouseEdge)
                return CalculateHouseEdgeReward(bet);
```

6. Add the two private methods before `SumTransactionsForBet`:

```csharp
        // #183 Hyper Roll. Checked in this order so the vague results match the pari-mutuel path's order:
        // length/empty, then amount, then choice, then characters. ChoiceRequiredLength is NOT used — board
        // words are 6–8 characters and the board itself is the list of valid choices.
        private static BetValidationResult HouseEdgeChoiceIsValid(Bet bet, Game game, bool isFreeBet)
        {
            var parameters = game.Parameters;

            if (string.IsNullOrEmpty(bet.Choice))
                return BetValidationResult.WrongChoiceLength;

            if (isFreeBet)
            {
                if (bet.Amount != 0)
                    return BetValidationResult.InvalidAmount;
            }
            else if (bet.Amount < parameters.RequiredAmount
                || parameters.MaximumStake is null
                || bet.Amount > parameters.MaximumStake.Value
                || decimal.Round(bet.Amount, NativeSol.Decimals) != bet.Amount)
            {
                return BetValidationResult.InvalidAmount;
            }

            if (game.BoardChoices.IsNullOrEmpty() || !game.BoardChoices.Any(x => x.Choice == bet.Choice))
                return BetValidationResult.InvalidChoice;

            var acceptedCharacters = parameters.AllowedCharacters.StringValueOf().ToArray();
            if (!bet.Choice.All(character => acceptedCharacters.Contains(character)))
                return BetValidationResult.InvalidCharacters;

            return BetValidationResult.Valid;
        }

        // #183 Hyper Roll: stake × this round's multiplier for the word, from the treasury. No pot, no division,
        // and deliberately NO commission — the 5% edge is baked into the multipliers.
        private BetTransactionAmountResult CalculateHouseEdgeReward(Bet bet)
        {
            if (!_boardMultipliers.TryGetValue(bet.Choice, out var multiplier))
                return new BetTransactionAmountResult(ErrorMessages.HouseEdgeBoardEntryMissing);

            decimal reward = bet.Amount * multiplier;
            reward -= SumTransactionsForBet(bet, TransactionLogType.Credit);

            if (reward <= 0)
                return new BetTransactionAmountResult(ErrorMessages.BetHasAlreadyBeenRewarded);

            reward = Math.Round(reward, _settings.Decimals, MidpointRounding.ToZero);
            ulong rewardInTokens = (ulong)(reward * (decimal)Math.Pow(10, _settings.Decimals));

            return new BetTransactionAmountResult(reward, rewardInTokens);
        }
```

⚠️ The constructor's `SetPotAmount`, `_payableWinnerCount`, `_houseFundedBase`, `_commissionRate` lines stay; they are simply unused on the house-edge path.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~BetDomainServiceTests|FullyQualifiedName~RegisterBetHandlerTests"`
Expected: PASS, including the untouched `0.5m` commission fixtures.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs src/SpaceStroker.Tests/UnitTests/Domain/Games/BetDomainServiceTests.cs src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs
git commit -m "Validate Hyper Roll stakes against the board and pay stake times multiplier."
git push origin feature/hyper-roll
```

---

### Task 6: Configuration, seeding and self-heal

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OperatorSettings.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json`
- Create: `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`

**Interfaces:**
- Consumes: Task 1 (`HouseEdgeRules.FormatLadder`, `Validate`).
- Produces: `DefaultGameSettings.HouseEdge` (`HouseEdgeSettings?`); `HouseEdgeSettings { decimal RtpPercent; decimal MaximumStake; List<decimal> Ladder; List<HouseEdgeWordSettings> Words; }`; `HouseEdgeWordSettings { string Word; decimal Multiplier; }`; `HouseEdgeReconciler.Reconcile(SpaceStrokerDbContext, IReadOnlyList<DefaultGameSettings>) : int`.

- [ ] **Step 1: Write the failing binding tests**

In `OrbitSettingsBindingTests.cs`:

- In `DefaultGames_CarryTheSeededOrbitEligibility`, replace `[InlineData("Beast Mode", true)]` with `[InlineData("Hyper Roll", false)]`.
- In `DefaultGames_CarryTheSeededComingSoonFlag`, replace `[InlineData("Beast Mode", true)]` with `[InlineData("Hyper Roll", false)]`, and replace that test's `<summary>` first sentence with `/// Every committed type ships playable. Beast Mode was removed from config by #183 (Hyper Roll replaces it);`.
- Append:

```csharp
    /// <summary>
    /// #183 — the COMMITTED Hyper Roll config is a valid house-edge type: its ladder carries the configured
    /// RTP, every word fits the rules, and the pool can fill every slot. This is the guard against a
    /// mis-tiered word edit shipping a board that pays out more than it takes.
    /// </summary>
    [Fact]
    public void DefaultGames_HyperRoll_IsAValidHouseEdgeConfig()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "spacestrokersettings.json"), optional: false)
            .Build();

        var settings = new GeneralSettings();
        configuration.GetSection("GeneralSettings").Bind(settings);

        var hyperRoll = settings.Operator.DefaultGames.Single(x => x.Name == "Hyper Roll");
        hyperRoll.HouseEdge.ShouldNotBeNull();
        hyperRoll.PresetChoices.ShouldBeEmpty();
        hyperRoll.MinimumNumberOfPlayers.ShouldBe(1);
        hyperRoll.AllowOrbit.ShouldBeFalse();
        hyperRoll.ComingSoon.ShouldBeFalse();
        hyperRoll.RequiredAmount.ShouldBe(0.01m);
        hyperRoll.HouseEdge!.MaximumStake.ShouldBe(0.2m);
        hyperRoll.HouseEdge.RtpPercent.ShouldBe(95m);
        hyperRoll.HouseEdge.Ladder.ShouldBe(new[] { 4m, 6m, 8m, 9m, 10m, 10m, 10m, 10m });
        hyperRoll.HouseEdge.Words.Count.ShouldBe(36);

        var parameters = new GameParameters
        {
            Name = hyperRoll.Name,
            AllowedCharacters = hyperRoll.AllowedCharacters,
            RequiredAmount = hyperRoll.RequiredAmount,
            IsHouseEdge = true,
            MaximumStake = hyperRoll.HouseEdge.MaximumStake,
            RtpPercent = hyperRoll.HouseEdge.RtpPercent,
            BoardLadder = HouseEdgeRules.FormatLadder(hyperRoll.HouseEdge.Ladder),
        };
        var pool = hyperRoll.HouseEdge.Words.Select(x => new PresetChoice(x.Word) { Multiplier = x.Multiplier }).ToList();

        var validation = HouseEdgeRules.Validate(parameters, pool);
        validation.IsValid.ShouldBeTrue(validation.Reason);
    }
```

Add `using SpaceStroker.Domain.Games.Entities;` and `using SpaceStroker.Domain.Games.Services;` at the top if missing.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~OrbitSettingsBindingTests"`
Expected: build FAILS — `DefaultGameSettings.HouseEdge` does not exist.

- [ ] **Step 3: Add the settings classes**

In `OperatorSettings.cs`, inside `DefaultGameSettings` after `public TimeSpan? DrawTimeUtc { get; set; }` add:

```csharp
        // Non-null => a player-vs-house preset type (#183 Hyper Roll). PresetChoices must stay EMPTY: the pool
        // is HouseEdge.Words, each carrying its multiplier. RequiredAmount is the minimum stake.
        public HouseEdgeSettings? HouseEdge { get; set; }
```

At the end of the namespace (after the `DefaultGameSettings` class) add:

```csharp
    public class HouseEdgeSettings
    {
        public decimal RtpPercent { get; set; }
        public decimal MaximumStake { get; set; }
        public List<decimal> Ladder { get; set; } = new();
        public List<HouseEdgeWordSettings> Words { get; set; } = new();
    }

    public class HouseEdgeWordSettings
    {
        public string Word { get; set; }
        public decimal Multiplier { get; set; }
    }
```

- [ ] **Step 4: Replace Beast Mode with Hyper Roll in config**

In `src/SpaceStroker.Common/spacestrokersettings.json`, replace the whole Beast Mode object (from `{` before `"Name": "Beast Mode",` through its closing `},`) with:

```json
                {
                    "Name": "Hyper Roll",
                    "Description": "Pick 1 of 8 words and set your stake - land it and the house pays your multiplier, up to 10x.",
                    "AllowedCharacters": 2,
                    "ChoiceRequiredLength": 8,
                    "RequiredAmount": 0.01,
                    "MinimumNumberOfPlayers": 1,
                    "AcceptDuplicatedCharacters": true,
                    "AllowOrbit": false,
                    "ComingSoon": false,
                    "PresetChoices": [],
                    "HouseEdge": {
                        "RtpPercent": 95,
                        "MaximumStake": 0.2,
                        "Ladder": [ 4, 6, 8, 9, 10, 10, 10, 10 ],
                        "Words": [
                            { "Word": "banana", "Multiplier": 4 },
                            { "Word": "cosmos", "Multiplier": 4 },
                            { "Word": "hubble", "Multiplier": 4 },
                            { "Word": "bonobo", "Multiplier": 4 },
                            { "Word": "meteor", "Multiplier": 4 },
                            { "Word": "rocket", "Multiplier": 6 },
                            { "Word": "planet", "Multiplier": 6 },
                            { "Word": "comets", "Multiplier": 6 },
                            { "Word": "launch", "Multiplier": 6 },
                            { "Word": "orbits", "Multiplier": 6 },
                            { "Word": "gorilla", "Multiplier": 8 },
                            { "Word": "nebulae", "Multiplier": 8 },
                            { "Word": "stellar", "Multiplier": 8 },
                            { "Word": "shuttle", "Multiplier": 8 },
                            { "Word": "eclipse", "Multiplier": 8 },
                            { "Word": "gravity", "Multiplier": 9 },
                            { "Word": "capsule", "Multiplier": 9 },
                            { "Word": "jupiter", "Multiplier": 9 },
                            { "Word": "voyager", "Multiplier": 9 },
                            { "Word": "cyborgs", "Multiplier": 9 },
                            { "Word": "asteroid", "Multiplier": 10 },
                            { "Word": "blastoff", "Multiplier": 10 },
                            { "Word": "galactic", "Multiplier": 10 },
                            { "Word": "mandrill", "Multiplier": 10 },
                            { "Word": "starship", "Multiplier": 10 },
                            { "Word": "moonwalk", "Multiplier": 10 },
                            { "Word": "universe", "Multiplier": 10 },
                            { "Word": "spaceman", "Multiplier": 10 },
                            { "Word": "stardust", "Multiplier": 10 },
                            { "Word": "wormhole", "Multiplier": 10 },
                            { "Word": "redshift", "Multiplier": 10 },
                            { "Word": "nebulous", "Multiplier": 10 },
                            { "Word": "rocketry", "Multiplier": 10 },
                            { "Word": "kilonova", "Multiplier": 10 },
                            { "Word": "sunspots", "Multiplier": 10 },
                            { "Word": "starfall", "Multiplier": 10 }
                        ]
                    }
                },
```

- [ ] **Step 5: Run the binding tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~OrbitSettingsBindingTests"`
Expected: PASS.

- [ ] **Step 6: Implement the reconciler**

Create `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Infrastructure.Data.Context;

namespace SpaceStroker.Infrastructure.Services.Games
{
    /// <summary>
    /// The name-matched startup self-heal for a house-edge type's BOARD SHAPE (#183 Hyper Roll): the ladder,
    /// the RTP and the word pool. Seeding is create-only by name, so without this a word added to config
    /// would never reach a database that already has the row.
    ///
    /// ⚠️ It NEVER touches RequiredAmount or MaximumStake. Changing a bet bound while a stale SPA is quoting
    /// the old one refuses bets after their SOL moved — the same reason RequiredAmount has no self-heal.
    /// Board-shape edits are safe under a live round because every round persists its own board.
    ///
    /// A class, not an inline loop in Consumers/Program.cs, so the integration suite can drive it
    /// (ExecuteUpdate is a no-op on the in-memory provider) — the ComingSoonReconciler precedent.
    /// </summary>
    public static class HouseEdgeReconciler
    {
        /// <returns>The number of rows changed or inserted.</returns>
        public static int Reconcile(SpaceStrokerDbContext context, IReadOnlyList<DefaultGameSettings> defaults)
        {
            var changes = 0;

            foreach (var config in defaults.Where(x => x.HouseEdge != null))
            {
                var houseEdge = config.HouseEdge!;

                var parametersId = context.GameParameters
                    .AsNoTracking()
                    .Where(x => x.Name == config.Name)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefault();

                if (parametersId is null)
                    continue; // not seeded yet — the create pass builds it correctly

                var ladder = HouseEdgeRules.FormatLadder(houseEdge.Ladder);
                var rtp = houseEdge.RtpPercent;

                // NULL-aware on purpose: in SQL NULL <> '4,6' is NULL, not true, so a bare inequality would skip
                // exactly a row that predates the columns.
                changes += context.GameParameters
                    .Where(x => x.Id == parametersId
                             && (x.BoardLadder == null || x.BoardLadder != ladder || x.RtpPercent == null || x.RtpPercent != rtp))
                    .ExecuteUpdate(setters => setters
                        .SetProperty(x => x.BoardLadder, ladder)
                        .SetProperty(x => x.RtpPercent, rtp));

                var configured = houseEdge.Words.ToDictionary(x => x.Word, x => x.Multiplier, StringComparer.Ordinal);
                var existing = context.PresetChoices.AsNoTracking().Where(x => x.ParametersId == parametersId).ToList();

                foreach (var row in existing)
                {
                    if (configured.TryGetValue(row.Choice, out var multiplier))
                    {
                        if (row.Multiplier != multiplier || !row.Active)
                        {
                            var rowId = row.Id;
                            changes += context.PresetChoices
                                .Where(x => x.Id == rowId)
                                .ExecuteUpdate(setters => setters
                                    .SetProperty(x => x.Multiplier, multiplier)
                                    .SetProperty(x => x.Active, true));
                        }
                    }
                    else if (row.Active)
                    {
                        var rowId = row.Id;
                        changes += context.PresetChoices
                            .Where(x => x.Id == rowId)
                            .ExecuteUpdate(setters => setters.SetProperty(x => x.Active, false));
                    }
                }

                var existingWords = existing.Select(x => x.Choice).ToHashSet(StringComparer.Ordinal);
                var toInsert = houseEdge.Words
                    .Where(x => !existingWords.Contains(x.Word))
                    .Select(x => new PresetChoice(x.Word) { ParametersId = parametersId.Value, Multiplier = x.Multiplier, Active = true })
                    .ToList();

                if (toInsert.Count > 0)
                {
                    context.PresetChoices.AddRange(toInsert);
                    context.SaveChanges();
                    changes += toInsert.Count;
                }
            }

            return changes;
        }
    }
}
```

- [ ] **Step 7: Wire seeding in Consumers**

In `src/SpaceStroker.Consumers/Program.cs`:

1. In `ToGameParameters`, after `if (config.PresetChoices is { Count: > 0 }) parameters.PresetChoices = ...;` add:

```csharp
    // #183 Hyper Roll: a house-edge type's pool is HouseEdge.Words, each with its multiplier (tier).
    if (config.HouseEdge != null)
    {
        parameters.IsHouseEdge = true;
        parameters.MaximumStake = config.HouseEdge.MaximumStake;
        parameters.RtpPercent = config.HouseEdge.RtpPercent;
        parameters.BoardLadder = HouseEdgeRules.FormatLadder(config.HouseEdge.Ladder);
        parameters.PresetChoices = config.HouseEdge.Words
            .Select(x => new PresetChoice(x.Word) { Multiplier = x.Multiplier, Active = true })
            .ToList();
    }
```

2. Directly after the `if (comingSoon.Patched > 0) { ... }` block, add:

```csharp
        // #183 Hyper Roll board-shape self-heal (words, ladder, RTP). Never stake bounds. No cache drop needed
        // here: the unconditional ActiveParameters drop further down this block covers it.
        var houseEdgeChanges = HouseEdgeReconciler.Reconcile(context, defaults);
        if (houseEdgeChanges > 0)
            Log.Information("Reconciled {Count} house-edge pool/ladder change(s) from configuration.", houseEdgeChanges);
```

3. Replace the comment `// #168 — a fresh environment seeds Beast Mode already gated, and never opens a round for it.` with `// #168 — a type configured coming-soon is seeded already gated, and never opens a round.`

`Program.cs` already has `using SpaceStroker.Domain.Games.Services;` and `using SpaceStroker.Infrastructure.Services.Games;`.

- [ ] **Step 8: Build and run the unit suite**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SpaceStroker.Domain/Configs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeReconciler.cs src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs
git commit -m "Seed Hyper Roll from config and self-heal its word pool."
git push origin feature/hyper-roll
```

---

### Task 7: Game service — board loading, board draw, payouts contract

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameStatusUpdatedEvent.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/IGameService.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetActiveGameForParameter/GetActiveGameForParameterHandler.cs`

**Interfaces:**
- Consumes: Task 1 (`HouseEdgeBoard.Draw`, `HouseEdgeRules`), Task 2 (`GameBoardChoices`).
- Produces: `record GameResultPayout(string Wallet, decimal Amount)`; `GameResult.Payouts` (`IEnumerable<GameResultPayout>?`, 5th positional, default null); `IGameService.UpdateGameStatusWithPayouts(Game game, GameStatus status, IReadOnlyList<GameResultPayout>? payouts) : Task`; `GetGameById` returns the game with `BoardChoices` loaded; `CreateGame` persists a board for house-edge types.

This task's behaviour is proven end to end by the integration tests in Task 10 (the service needs a hub, a bus and Npgsql SQL); it has no unit test of its own.

- [ ] **Step 1: Extend the status contract**

In `GameStatusUpdatedEvent.cs`, change the `GameResult` record to:

```csharp
    public record GameResult(
        IEnumerable<string> Winners,
        int NumberOfWinners,
        string FirstChoiceTyped,
        decimal? NetRewardPerWinner = null,
        // #183 Hyper Roll only: what each PAID winner actually received. Payouts differ per player (stake ×
        // multiplier), so they can never ride NetRewardPerWinner, whose uniformity is unit-pinned. Each bet's
        // wallet, choice and amount are already public on {gameId}/Bets, so this reveals nothing new. Null
        // (omitted from the payload) everywhere else.
        IEnumerable<GameResultPayout>? Payouts = null);

    public record GameResultPayout(string Wallet, decimal Amount);
```

- [ ] **Step 2: Add the sibling interface method**

In `IGameService.cs`, after the `UpdateGameStatus` declaration add:

```csharp
        /// <summary>
        /// #183 Hyper Roll: the reward-completed transition with per-winner payouts. A SIBLING rather than an
        /// optional parameter on UpdateGameStatus, because Moq expression trees cannot omit optional arguments
        /// and existing verifications call UpdateGameStatus with four explicit arguments.
        /// </summary>
        Task UpdateGameStatusWithPayouts(Game game, GameStatus status, IReadOnlyList<GameResultPayout>? payouts);
```

- [ ] **Step 3: Refactor `UpdateGameStatus` into a shared core**

In `GameService.cs`:

1. Rename `public async Task UpdateGameStatus(Game game, GameStatus status, SpaceStrokerTyper? monkey = null, decimal? netRewardPerWinner = null)` to `private async Task UpdateGameStatusCore(Game game, GameStatus status, SpaceStrokerTyper? monkey, decimal? netRewardPerWinner, IReadOnlyList<GameResultPayout>? payouts)`.

2. Add above it:

```csharp
        public Task UpdateGameStatus(Game game, GameStatus status, SpaceStrokerTyper? monkey = null, decimal? netRewardPerWinner = null)
            => UpdateGameStatusCore(game, status, monkey, netRewardPerWinner, payouts: null);

        public Task UpdateGameStatusWithPayouts(Game game, GameStatus status, IReadOnlyList<GameResultPayout>? payouts)
            => UpdateGameStatusCore(game, status, monkey: null, netRewardPerWinner: null, payouts);
```

3. Inside the core, change `else if (netRewardPerWinner.HasValue && !string.IsNullOrEmpty(game.WinningChoice))` to:

```csharp
                    else if ((netRewardPerWinner.HasValue || payouts is { Count: > 0 }) && !string.IsNullOrEmpty(game.WinningChoice))
```

and change that arm's `result = new GameResult(winners, game.Bets.Count(x => x.Choice == game.WinningChoice), game.WinningChoice, netRewardPerWinner);` to:

```csharp
                        result = new GameResult(
                            winners,
                            game.Bets.Count(x => x.Choice == game.WinningChoice),
                            game.WinningChoice,
                            netRewardPerWinner,
                            payouts);
```

- [ ] **Step 4: Load the board in `GetGameById`**

Change the first query in `GetGameById` to:

```csharp
            var game = _context.Games
                .Include(x => x.BoardChoices)
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == gameId);
```

- [ ] **Step 5: Draw the board in `CreateGame`**

In `CreateGame`, right after `var game = new Game(parameters.Id) { CarriedExtra = carriedExtra };` add:

```csharp
                // #183 Hyper Roll: draw and persist this round's board in the SAME SaveChanges as the game. The
                // pool is read here rather than from `parameters`, because CreateGame has four call sites and not
                // all of them load PresetChoices.
                if (parameters.IsHouseEdge)
                {
                    var pool = await _context.PresetChoices
                        .AsNoTracking()
                        .Where(x => x.ParametersId == parameters.Id && x.Active)
                        .ToListAsync();

                    // ⚠️ A mis-tiered pool or ladder is a MONEY bug: refuse to open the round rather than offer a
                    // board whose Σ(1/m) pays out more than it takes.
                    var validation = HouseEdgeRules.Validate(parameters, pool);
                    if (!validation.IsValid)
                    {
                        Log.Error("House-edge game type {ParametersId} is misconfigured; no round opened: {Reason}", parameters.Id, validation.Reason);
                        return;
                    }

                    var board = HouseEdgeBoard.Draw(pool, HouseEdgeRules.ParseLadder(parameters.BoardLadder), Random.Shared);
                    if (board == null)
                    {
                        Log.Error("House-edge game type {ParametersId} cannot fill its board ladder {Ladder}; no round opened.", parameters.Id, parameters.BoardLadder);
                        return;
                    }

                    foreach (var boardChoice in board)
                        boardChoice.GameId = game.Id;

                    game.BoardChoices = board;
                }
```

- [ ] **Step 6: Load the board for the live instance**

In `GetActiveGameForParameterHandler.cs`, add `.Include(x => x.BoardChoices)` after `.Include(x => x.Bets).ThenInclude(b => b.Player)`.

- [ ] **Step 7: Build and run the unit suite**

Run: `scripts\run_tests.bat`
Expected: PASS (existing `UpdateGameStatus(..., null, null)` Moq verifications still compile).

- [ ] **Step 8: Commit**

```bash
git add src/SpaceStroker.Infrastructure
git commit -m "Draw and persist Hyper Roll boards and publish per-winner payouts."
git push origin feature/hyper-roll
```

---

### Task 8: Reward consumer — treasury check and payouts

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs`

**Interfaces:**
- Consumes: Task 5 (house-edge reward), Task 7 (`UpdateGameStatusWithPayouts`, `GameResultPayout`), `HouseEdgeRules.TreasuryFeeReservePerTransfer`, `ISolanaService.GetSolBalance(string) : Task<decimal?>`, `ITokenSettlementService.Credit(Bet, decimal, CancellationToken) : Task<PaymentResult>`.

- [ ] **Step 1: Write the failing consumer tests**

In `RewardGameConsumerTests.cs`:

1. Add fields and usings:

```csharp
using SpaceStroker.Infrastructure.Contracts.Games;
using SpaceStroker.Infrastructure.Services.Solana;
using Shouldly;
```

```csharp
    private readonly Mock<ISolanaService> _solana = new();
    private readonly Mock<ITokenSettlementService> _settlement = new();
```

2. In `ScopeFactory`, replace the `GeneralSettings` setup with:

```csharp
        provider.Setup(x => x.GetService(typeof(GeneralSettings))).Returns(new GeneralSettings
        {
            Operator = new OperatorSettings { Token = new TokenSettings { Decimals = 9, TokenOwnerPublicKey = "TreasuryPublicKey" } },
        });
        provider.Setup(x => x.GetService(typeof(ISolanaService))).Returns(_solana.Object);
        provider.Setup(x => x.GetService(typeof(ITokenSettlementService))).Returns(_settlement.Object);
```

3. Append the house-edge tests:

```csharp
    // ---- House-edge (#183 Hyper Roll) ----

    private Game SeedHouseEdgeRoundWithWinners(SpaceStrokerDbContext context, params decimal[] winningStakes)
    {
        var parameters = new GameParameters
        {
            Name = "Hyper Roll", Description = "house edge", RequiredAmount = 0.01m, MaximumStake = 5m,
            RtpPercent = 95m, BoardLadder = "4,6", IsHouseEdge = true, MinimumNumberOfPlayers = 1,
        };
        var game = new Game(parameters.Id) { Status = GameStatus.Ended, Parameters = parameters, WinningChoice = "rocket" };
        game.BoardChoices.Add(new GameBoardChoice { GameId = game.Id, Choice = "banana", Multiplier = 4m, Position = 0 });
        game.BoardChoices.Add(new GameBoardChoice { GameId = game.Id, Choice = "rocket", Multiplier = 6m, Position = 1 });

        var start = DateTime.UtcNow.AddMinutes(-1);
        for (var i = 0; i < winningStakes.Length; i++)
        {
            game.Bets.Add(new Bet(game.Id, HexSeed(), Guid.NewGuid(), "rocket", winningStakes[i])
            {
                Status = BetStatus.NeedsRewarding,
                Signature = $"sig-{i}",
                CreatedDate = start.AddSeconds(i),
            });
        }

        context.Add(parameters);
        context.Add(game);
        context.SaveChanges();
        return game;
    }

    private void SetupHappyPayments()
    {
        _solana.Setup(x => x.GetLatestBlockhashForTokenTransfer()).ReturnsAsync("BLOCKHASH");
        _settlement.Setup(x => x.Credit(It.IsAny<Bet>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Success("CREDIT_SIG"));
    }

    [Fact]
    public async Task HouseEdge_TreasuryCoversOnlyTheFirstWinner_SecondGoesToManualAnalysis()
    {
        using var context = NewContext();
        var game = SeedHouseEdgeRoundWithWinners(context, 1m, 1m); // each owes 6
        SetupHappyPayments();
        // 6 + 2 × 0.001 reserve fits; 12 does not.
        _solana.Setup(x => x.GetSolBalance("TreasuryPublicKey")).ReturnsAsync(6.5m);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _settlement.Verify(x => x.Credit(It.IsAny<Bet>(), 6m, It.IsAny<CancellationToken>()), Times.Once);
        _gameService.Verify(x => x.UpdateBetStatus(It.Is<Bet>(b => b.Signature == "sig-0"), BetStatus.Rewarded), Times.Once);
        _gameService.Verify(x => x.UpdateBetStatus(It.Is<Bet>(b => b.Signature == "sig-1"), BetStatus.NeedsManualAnalysis), Times.Once);
        _gameService.Verify(x => x.UpdateGameStatusWithPayouts(It.Is<Game>(g => g.Id == game.Id), GameStatus.ErrorWhenProcessingRewards, null), Times.Once);
    }

    [Fact]
    public async Task HouseEdge_UnknownTreasuryBalance_StillAttemptsEveryCredit()
    {
        using var context = NewContext();
        var game = SeedHouseEdgeRoundWithWinners(context, 1m, 0.5m);
        SetupHappyPayments();
        _solana.Setup(x => x.GetSolBalance("TreasuryPublicKey")).ReturnsAsync((decimal?)null);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _settlement.Verify(x => x.Credit(It.IsAny<Bet>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HouseEdge_CompletedRound_PublishesPayouts_AndNoNetRewardPerWinner()
    {
        using var context = NewContext();
        var game = SeedHouseEdgeRoundWithWinners(context, 0.25m);
        SetupHappyPayments();
        _solana.Setup(x => x.GetSolBalance("TreasuryPublicKey")).ReturnsAsync(100m);
        _gameService.Setup(x => x.UpdateBetStatus(It.IsAny<Bet>(), BetStatus.Rewarded))
            .Callback<Bet, BetStatus>((bet, status) => bet.Status = status)
            .Returns(Task.CompletedTask);

        IReadOnlyList<GameResultPayout>? published = null;
        _gameService.Setup(x => x.UpdateGameStatusWithPayouts(It.IsAny<Game>(), GameStatus.RewardForWinnersCompleted, It.IsAny<IReadOnlyList<GameResultPayout>?>()))
            .Callback<Game, GameStatus, IReadOnlyList<GameResultPayout>?>((_, _, payouts) => published = payouts)
            .Returns(Task.CompletedTask);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        published.ShouldNotBeNull();
        published!.Single().Amount.ShouldBe(1.5m);
        _gameService.Verify(x => x.UpdateGameStatus(It.IsAny<Game>(), It.IsAny<GameStatus>(), It.IsAny<SpaceStrokerTyper?>(), It.IsAny<decimal?>()), Times.Exactly(1));
    }
```

⚠️ The last `Times.Exactly(1)` counts the one `RewardForWinnersInProgress` transition, which still goes through `UpdateGameStatus`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~RewardGameConsumerTests"`
Expected: the three new tests FAIL (no treasury check, pari-mutuel status update used); the four existing no-winner tests still PASS.

- [ ] **Step 3: Implement**

In `RewardGameConsumer.cs`:

1. Add usings:

```csharp
using System.Collections.Generic;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Infrastructure.Contracts.Games;
```

2. In the game query, add `.Include(x => x.BoardChoices)` after `.Include(x => x.Parameters).ThenInclude(x => x.PresetChoices)`.

3. Directly after `await gameService.UpdateGameStatus(game, GameStatus.RewardForWinnersInProgress);` add:

```csharp
            // #183 Hyper Roll pays from the TREASURY's own balance, not from the round's stakes. Read it once
            // and keep a running figure so winners that can be covered are still paid when one cannot.
            // A failed read is UNKNOWN, not empty: the credit is attempted and fails on-chain the normal way.
            var isHouseEdge = game.Parameters?.IsHouseEdge == true;
            decimal? treasuryAvailable = null;
            var payouts = new List<GameResultPayout>();

            if (isHouseEdge && paidWinners.Any())
            {
                var balance = await solanaService.GetSolBalance(settings.Operator.Token.TokenOwnerPublicKey);
                treasuryAvailable = balance - HouseEdgeRules.TreasuryFeeReservePerTransfer * paidWinners.Count;
            }
```

4. Inside `if (string.IsNullOrWhiteSpace(rewardResult.ErrorMessage))`, before `winner.Game = game;`, add:

```csharp
                    if (treasuryAvailable is decimal available)
                    {
                        if (rewardResult.Value > available)
                        {
                            Log.Error("HOUSE EDGE — treasury cannot cover bet {BetId} in game {GameId}: owes {Amount}, available {Available}. Marked for manual analysis.",
                                winner.Id, game.Id, rewardResult.Value, available);
                            await gameService.UpdateBetStatus(winner, BetStatus.NeedsManualAnalysis);
                            continue;
                        }

                        treasuryAvailable = available - rewardResult.Value;
                    }
```

5. Replace `netRewardPerWinner ??= rewardResult.Value;` with:

```csharp
                        if (isHouseEdge)
                            payouts.Add(new GameResultPayout(winner.GetWalletAddress() ?? string.Empty, rewardResult.Value));
                        else
                            netRewardPerWinner ??= rewardResult.Value;
```

6. Replace the final `await gameService.UpdateGameStatus(game, newGameStatus, netRewardPerWinner: ...);` statement with:

```csharp
            if (isHouseEdge)
            {
                await gameService.UpdateGameStatusWithPayouts(
                    game, newGameStatus,
                    newGameStatus == GameStatus.RewardForWinnersCompleted ? payouts : null);
            }
            else
            {
                await gameService.UpdateGameStatus(
                    game, newGameStatus,
                    netRewardPerWinner: newGameStatus == GameStatus.RewardForWinnersCompleted ? netRewardPerWinner : null);
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~RewardGameConsumerTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs
git commit -m "Pay Hyper Roll winners from the treasury behind a running balance check."
git push origin feature/hyper-roll
```

---

### Task 9: Contracts, verification and house seed

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameVerificationDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetGameVerificationHandlerTests.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/HouseSeedBetServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 7.
- Produces (wire, camelCase): `GameParametersDto.{isHouseEdge, maximumStake, rtpPercent}` and `commissionPercentage = 0` for house-edge; `ActiveGameDto.boardChoices: [{ choice, multiplier }] | null` ordered by position; `GameVerificationDto.{isHouseEdge, boardChoices: [{ choice, multiplier, weight }]}` ordered by choice (ordinal).

- [ ] **Step 1: Write the failing tests**

Append inside `GetGameVerificationHandlerTests`:

```csharp
        [Fact]
        public async Task GetGameVerification_HouseEdge_PublishesTheBoardWithWeights_OrdinalOrdered()
        {
            var game = BuildGame(GameStatus.RewardForWinnersCompleted);
            game.Parameters.IsHouseEdge = true;
            game.Parameters.AllowedCharacters = GameCharacterType.Letters;
            game.Parameters.ChoiceRequiredLength = 8;
            game.Parameters.BoardLadder = "4,6,10";
            game.Parameters.RtpPercent = 95m;
            game.Parameters.MaximumStake = 1m;
            game.BoardChoices = new List<GameBoardChoice>
            {
                new() { GameId = game.Id, Choice = "rocket", Multiplier = 6m, Position = 0 },
                new() { GameId = game.Id, Choice = "asteroid", Multiplier = 10m, Position = 1 },
                new() { GameId = game.Id, Choice = "banana", Multiplier = 4m, Position = 2 },
            };
            game.WinningChoice = "banana";
            game.CharactersTyped = 1_000;
            game.MaxCharacters = 1_000;
            game.CharacterPool = "abceiknorst";
            var bets = new List<Bet> { new(game.Id, Faker.Random.Hexadecimal(64, ""), Guid.NewGuid(), "banana", 0.05m, 0) };

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(bets);

            using var context = new SpaceStrokerDbContext(ContextOptions);
            var dto = await CreateHandler(context).HandleRequest(new GetGameVerificationRequest(game.Id), CancellationToken);

            dto.IsHouseEdge.ShouldBeTrue();
            dto.BoardChoices.Select(x => x.Choice).ShouldBe(new[] { "asteroid", "banana", "rocket" });
            dto.BoardChoices.Select(x => x.Weight).ShouldBe(new[] { 100000, 250000, 166666 });
            dto.OrderedChoices.ShouldBe(new[] { "asteroid", "banana", "rocket" });
            dto.WinnerDecidedByFallback.ShouldBe(true);
        }
```

In `HouseSeedBetServiceTests.cs`:

1. Add `bool houseEdge = false` as the last parameter of `SeedGame(...)` and set `IsHouseEdge = houseEdge,` in its `GameParameters` initializer.
2. Append:

```csharp
    [Fact]
    public async Task DoesNothing_WhenTheTypeIsHouseEdge()
    {
        // #183 — the house is the COUNTERPARTY in a player-vs-house round, so it never seeds one. Returning
        // here also keeps a min-1 house-edge type out of the Warning below, which production logging keeps.
        using var context = NewContext();
        var game = SeedGame(context, minimumPlayers: 2, houseEdge: true);

        await Service(context).SeedRound(game.Id);

        VerifyNoTransfer();
        VerifyNoBet();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `scripts\run_tests.bat --filter "FullyQualifiedName~GetGameVerificationHandlerTests|FullyQualifiedName~HouseSeedBetServiceTests"`
Expected: build FAILS — `GameVerificationDto.IsHouseEdge` does not exist.

- [ ] **Step 3: Implement the DTOs**

`GameVerificationDto.cs` — add inside `GameVerificationDto` after `Bets`:

```csharp
        /// <summary>#183 Hyper Roll: the winner is the weighted draw over <see cref="BoardChoices"/>.</summary>
        public bool IsHouseEdge { get; set; }

        /// <summary>#183 Hyper Roll: the round's board, ordered by Choice (ordinal) — the draw's input order.</summary>
        public IEnumerable<VerificationBoardChoiceDto> BoardChoices { get; set; } = new List<VerificationBoardChoiceDto>();
```

and add the class at the end of the namespace:

```csharp
    public class VerificationBoardChoiceDto
    {
        public string Choice { get; set; }
        public decimal Multiplier { get; set; }
        public int Weight { get; set; }
    }
```

`ActiveGameDto.cs` — in the constructor after `CarriedExtra = game.CarriedExtra;` add:

```csharp
            // #183 Hyper Roll: this round's 8 words in key order (Position). Null for every other type.
            BoardChoices = game.BoardChoices.IsNullOrEmpty()
                ? null
                : game.BoardChoices
                    .OrderBy(x => x.Position)
                    .Select(x => new BoardChoiceDto { Choice = x.Choice, Multiplier = x.Multiplier })
                    .ToList();
```

add the property after `CarriedExtra`:

```csharp
        public IEnumerable<BoardChoiceDto>? BoardChoices { get; set; }
```

and the class at the end of the namespace:

```csharp
    public class BoardChoiceDto
    {
        public string Choice { get; set; }
        public decimal Multiplier { get; set; }
    }
```

`GameParametersDto.cs` — replace the `CommissionPercentage` line and `PresetChoices` line, and add three properties:

```csharp
        // House cut shown in the STATS badge. See specs/crypto-commission.md. Zero for a house-edge type —
        // Hyper Roll takes no cut; its edge is in the multipliers (#183).
        public decimal CommissionPercentage { get; set; } = parameters.IsHouseEdge ? 0m : token.CommissionPercentage;
        // #183 Hyper Roll: RequiredAmount is the MINIMUM stake for this type.
        public bool IsHouseEdge { get; set; } = parameters.IsHouseEdge;
        public decimal? MaximumStake { get; set; } = parameters.MaximumStake;
        public decimal? RtpPercent { get; set; } = parameters.RtpPercent;
```

```csharp
        public IEnumerable<string>? PresetChoices { get; set; } = !parameters.PresetChoices.IsNullOrEmpty() ? parameters.PresetChoices.Where(x => x.Active).Select(x => x.Choice) : new List<string>();
```

- [ ] **Step 4: Publish the board in the verification handler**

In `GetGameVerificationHandler.cs`, right after the `var dto = new GameVerificationDto { ... };` initializer, add:

```csharp
            // #183 Hyper Roll: the board and its integer weights are the draw's inputs. Published before any bet
            // exists, because the board is fixed when the round opens.
            dto.IsHouseEdge = game.Parameters.IsHouseEdge;
            if (dto.IsHouseEdge)
            {
                dto.BoardChoices = game.BoardChoices
                    .OrderBy(x => x.Choice, StringComparer.Ordinal)
                    .Select(x => new VerificationBoardChoiceDto { Choice = x.Choice, Multiplier = x.Multiplier, Weight = HouseEdgeRules.WeightFor(x.Multiplier) })
                    .ToList();
            }
```

- [ ] **Step 5: Skip house-edge types in the house seed**

In `HouseSeedBetService.SeedRound`, immediately before `if (game.Parameters.MinimumNumberOfPlayers < MinimumViablePlayers)`, add:

```csharp
            // #183 — the house is the counterparty in a player-vs-house round; it never seeds one. Information,
            // not Warning: production keeps Warning-and-above, and this fires for every Hyper Roll round.
            if (game.Parameters.IsHouseEdge)
            {
                Log.Information("HOUSE SEED — game type {ParametersId} is player-vs-house; the house never seeds it.", game.ParametersId);
                return;
            }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `scripts\run_tests.bat`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Infrastructure src/SpaceStroker.Tests/UnitTests/Handlers/GetGameVerificationHandlerTests.cs src/SpaceStroker.Tests/UnitTests/Services/HouseSeedBetServiceTests.cs
git commit -m "Publish the Hyper Roll board to players and verifiers; skip it in the house seed."
git push origin feature/hyper-roll
```

---

### Task 10: Integration tests

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationGameParameters.cs`
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs`
- Create: `src/SpaceStroker.Tests.Integration/HyperRollFlowTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/DefaultGameSeedingTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `IntegrationGameParameters.HyperRoll` (name const) + `HouseEdge(...)` builder; `IntegrationTestFixture.SeedHouseEdgeGameAsync(Guid parametersId) : Task<Guid>`; `IntegrationTestFixture.GetBoardAsync(Guid gameId) : Task<List<GameBoardChoice>>`.

- [ ] **Step 1: Add the integration game type**

In `IntegrationGameParameters.cs`:

1. Add the const beside the others:

```csharp
        // #183 Hyper Roll. MinimumNumberOfPlayers 8 so 8 players can cover the whole board before it starts,
        // which guarantees exactly one winner per test round.
        public const string HyperRoll = "8L Hyper Roll Test";
```

2. Append to `All()`: `HouseEdge(HyperRoll, minimumPlayers: 8),`

3. Add the builder (public, so a test can build an isolated variant):

```csharp
        public static GameParameters HouseEdge(string name, int minimumPlayers)
        {
            var parameters = Base(name, GameCharacterType.Letters, 8);
            parameters.MinimumNumberOfPlayers = minimumPlayers;
            parameters.RequiredAmount = 1m;
            parameters.MaximumStake = 5m;
            parameters.RtpPercent = 95m;
            parameters.BoardLadder = "4,6,8,9,10,10,10,10";
            parameters.IsHouseEdge = true;
            parameters.AllowOrbit = false;
            parameters.PresetChoices = new List<PresetChoice>
            {
                new("banana") { Multiplier = 4m }, new("cosmos") { Multiplier = 4m },
                new("rocket") { Multiplier = 6m }, new("planet") { Multiplier = 6m },
                new("gorilla") { Multiplier = 8m }, new("nebulae") { Multiplier = 8m },
                new("gravity") { Multiplier = 9m }, new("capsule") { Multiplier = 9m },
                new("asteroid") { Multiplier = 10m }, new("blastoff") { Multiplier = 10m },
                new("galactic") { Multiplier = 10m }, new("mandrill") { Multiplier = 10m },
                new("starship") { Multiplier = 10m },
            };
            return parameters;
        }
```

- [ ] **Step 2: Add the fixture helpers**

In `IntegrationTestFixture.cs`, after `SeedWaitingBetsGameAsync`, add:

```csharp
        /// <summary>
        /// #183 — open a house-edge round the way production does, through IGameService.CreateGame, so its
        /// board is drawn and persisted. SeedWaitingBetsGameAsync inserts a raw Game and would leave it boardless.
        /// </summary>
        public async Task<Guid> SeedHouseEdgeGameAsync(Guid parametersId)
        {
            using var scope = CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            var parameters = await db.GameParameters.AsNoTracking().FirstAsync(x => x.Id == parametersId);
            var before = DateTime.UtcNow.AddSeconds(-1);

            await gameService.CreateGame(parameters);

            return await db.Games.AsNoTracking()
                .Where(x => x.ParametersId == parametersId && x.CreatedDate >= before)
                .OrderByDescending(x => x.CreatedDate)
                .Select(x => x.Id)
                .FirstAsync();
        }

        public async Task<List<GameBoardChoice>> GetBoardAsync(Guid gameId)
        {
            using var scope = CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            return await db.GameBoardChoices.AsNoTracking().Where(x => x.GameId == gameId).OrderBy(x => x.Position).ToListAsync();
        }
```

Add `using SpaceStroker.Infrastructure.Services.Games;` if missing.

- [ ] **Step 3: Write the flow tests**

Create `src/SpaceStroker.Tests.Integration/HyperRollFlowTests.cs`:

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Handlers.Games.RegisterBet;
using SpaceStroker.Tests.Integration.Harness;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SpaceStroker.Tests.Integration
{
    /// <summary>
    /// #183 Hyper Roll end to end: a real API + Consumers against real Postgres/Redis. The chain is faked
    /// (FakeSolanaService), so these prove the flow and the ledger, not a SystemProgram instruction.
    /// </summary>
    [Collection(IntegrationCollection.Name)]
    public class HyperRollFlowTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);
        private readonly IntegrationTestFixture _fixture;

        public HyperRollFlowTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _fixture.LogSink.Clear();
        }

        private static RegisterBetRequestBody PaidBet(string choice, string wallet, decimal amount) => new()
        {
            Amount = amount,
            Choice = choice,
            Wallet = wallet,
            PaymentSignature = $"TEST_SIG_{Guid.NewGuid():N}",
            Seed = Guid.NewGuid().ToString("N"),
        };

        private static async Task<string> WaitForStatusFrameAsync(ConcurrentQueue<string> payloads, GameStatus status, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var payload in payloads.ToArray())
                {
                    using var document = JsonDocument.Parse(payload);
                    if (document.RootElement.TryGetProperty("status", out var value) && value.GetInt32() == (int)status)
                        return payload;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException($"No {status} status frame within {timeout.TotalSeconds:0}s.");
        }

        [Fact]
        public async Task HappyPath_EightPlayersCoverTheBoard_WeightedDrawPaysStakeTimesMultiplier()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);
            var gameId = await _fixture.SeedHouseEdgeGameAsync(parameters.Id);
            var board = await _fixture.GetBoardAsync(gameId);
            board.Count.ShouldBe(8);

            var connection = _fixture.CreateGamesHubConnection();
            var statusPayloads = new ConcurrentQueue<string>();
            connection.On<string>($"{gameId}/Status", json => statusPayloads.Enqueue(json));
            await connection.StartAsync();

            try
            {
                await connection.InvokeAsync("JoinGame", gameId.ToString());

                for (var i = 0; i < board.Count; i++)
                    await _fixture.PlaceBetAsync(gameId, PaidBet(board[i].Choice, $"hyper-wallet-{i}", 1m + i * 0.5m));

                var game = await _fixture.WaitForStatusAsync(gameId, GameStatus.RewardForWinnersCompleted, Timeout);

                var bets = await _fixture.GetBetsAsync(gameId);
                bets.Count.ShouldBe(8);
                var winner = bets.Single(b => b.Choice == game.WinningChoice);
                winner.Status.ShouldBe(BetStatus.Rewarded);
                bets.Where(b => b.Id != winner.Id).ShouldAllBe(b => b.Status != BetStatus.Rewarded);

                var multiplier = board.Single(x => x.Choice == game.WinningChoice).Multiplier;
                var expected = Math.Round(winner.Amount * multiplier, _fixture.TokenDecimals, MidpointRounding.ToZero);

                var logs = await _fixture.GetTransactionLogsAsync(bets.Select(b => b.Id));
                logs.Count(l => l.Type == TransactionLogType.Debit).ShouldBe(8);
                var credit = logs.Single(l => l.Type == TransactionLogType.Credit);
                credit.BetId.ShouldBe(winner.Id);
                credit.Amount.ShouldBe(expected);

                var verification = await _fixture.GetGameVerificationAsync(gameId);
                verification.IsHouseEdge.ShouldBeTrue();
                verification.WinnerDecidedByFallback.ShouldBe(true);
                var ordered = verification.BoardChoices.ToList();
                ordered.Count.ShouldBe(8);
                var index = ProvablyFair.WeightedFallbackIndex(
                    verification.ServerSeed!,
                    ProvablyFair.BuildPrefix(verification.GameId, verification.OrderedBetSeeds),
                    ordered.Select(x => HouseEdgeRules.WeightFor(x.Multiplier)).ToArray());
                ordered[index].Choice.ShouldBe(verification.WinningChoice);

                var frame = await WaitForStatusFrameAsync(statusPayloads, GameStatus.RewardForWinnersCompleted, Timeout);
                frame.ShouldNotContain("netRewardPerWinner");
                using var document = JsonDocument.Parse(frame);
                var payouts = document.RootElement.GetProperty("result").GetProperty("payouts").EnumerateArray().ToList();
                payouts.Count.ShouldBe(1);
                payouts[0].GetProperty("amount").GetDecimal().ShouldBe(expected);

                _fixture.LogSink.GetErrorMessages().ShouldBeEmpty();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task InsufficientTreasury_WinnerGoesToManualAnalysis_NoTransfer()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);
            var gameId = await _fixture.SeedHouseEdgeGameAsync(parameters.Id);
            var board = await _fixture.GetBoardAsync(gameId);

            _fixture.ConsumersSolanaService.NextSolBalance = 0m;
            try
            {
                for (var i = 0; i < board.Count; i++)
                    await _fixture.PlaceBetAsync(gameId, PaidBet(board[i].Choice, $"broke-wallet-{i}", 1m));

                var game = await _fixture.WaitForStatusAsync(gameId, GameStatus.ErrorWhenProcessingRewards, Timeout);

                var bets = await _fixture.GetBetsAsync(gameId);
                var winner = bets.Single(b => b.Choice == game.WinningChoice);
                winner.Status.ShouldBe(BetStatus.NeedsManualAnalysis);

                var logs = await _fixture.GetTransactionLogsAsync(bets.Select(b => b.Id));
                logs.ShouldNotContain(l => l.Type == TransactionLogType.Credit);

                _fixture.LogSink.GetErrorMessages().ShouldContain(m => m.Contains(winner.Id.ToString()));
            }
            finally
            {
                _fixture.ConsumersSolanaService.NextSolBalance = 1_000m;
            }
        }

        [Fact]
        public async Task SinglePlayer_RoundStartsAlone()
        {
            var parametersId = await _fixture.InsertGameParametersAsync(
                IntegrationGameParameters.HouseEdge($"Hyper Roll Solo {Guid.NewGuid():N}", minimumPlayers: 1));
            var gameId = await _fixture.SeedHouseEdgeGameAsync(parametersId);
            var board = await _fixture.GetBoardAsync(gameId);

            await _fixture.PlaceBetAsync(gameId, PaidBet(board[0].Choice, "solo-wallet", 1m));

            await _fixture.WaitForStatusAsync(gameId, GameStatus.RewardForWinnersCompleted, Timeout);
        }

        [Fact]
        public async Task OutOfBoundsStake_IsRefusedAndRefunded()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);
            var gameId = await _fixture.SeedHouseEdgeGameAsync(parameters.Id);
            var board = await _fixture.GetBoardAsync(gameId);
            var body = PaidBet(board[0].Choice, "greedy-wallet", 6m); // MaximumStake is 5

            var response = await _fixture.PlaceBetRawAsync(gameId, body);
            response.IsSuccessStatusCode.ShouldBeFalse();

            (await _fixture.GetBetsAsync(gameId)).ShouldBeEmpty();

            var deadline = DateTime.UtcNow.AddSeconds(30);
            TransactionLog? refund = null;
            while (refund is null && DateTime.UtcNow < deadline)
            {
                refund = await _fixture.GetCryptoRefundLogAsync(body.PaymentSignature!);
                if (refund is null)
                    await Task.Delay(250);
            }
            refund.ShouldNotBeNull();
        }
    }
}
```

- [ ] **Step 4: Update the seeding tests**

In `DefaultGameSeedingTests.cs`:

1. Change `DefaultTypes` to `["Cosmic Sprint", "Hyper Roll", "Space Jackpot"]`.

2. Replace the whole `ConsumersStartup_SeedsBeastModeAsComingSoon_AndLeavesTheOthersPlayable` test (with its `<summary>`) by:

```csharp
        /// <summary>
        /// #183 — the committed catalogue seeds Hyper Roll as a playable player-vs-house type with its whole
        /// word pool, and Beast Mode is no longer seeded at all on a fresh database.
        /// </summary>
        [Fact]
        public async Task ConsumersStartup_SeedsHyperRollWithItsPool_AndNoBeastMode()
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();

            var hyperRoll = await db.Set<GameParameters>().AsNoTracking()
                .Include(p => p.PresetChoices)
                .SingleAsync(p => p.Name == "Hyper Roll");

            hyperRoll.IsHouseEdge.ShouldBeTrue();
            hyperRoll.Active.ShouldBeTrue();
            hyperRoll.ComingSoon.ShouldBeFalse();
            hyperRoll.AllowOrbit.ShouldBeFalse();
            hyperRoll.MinimumNumberOfPlayers.ShouldBe(1);
            hyperRoll.RequiredAmount.ShouldBe(0.01m);
            hyperRoll.MaximumStake.ShouldBe(0.2m);
            hyperRoll.RtpPercent.ShouldBe(95m);
            hyperRoll.BoardLadder.ShouldBe("4,6,8,9,10,10,10,10");
            hyperRoll.PresetChoices.Count(x => x.Active).ShouldBe(36);
            hyperRoll.PresetChoices.ShouldAllBe(x => x.Multiplier != null);

            (await db.Set<GameParameters>().AsNoTracking().AnyAsync(p => p.Name == "Beast Mode")).ShouldBeFalse();
        }
```

3. Replace the body of `Reconcile_FlipsAnAlreadySeededRow_WithoutRewritingAnyOtherField` with a probe row (config no longer carries a coming-soon type):

```csharp
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();

            var probeName = $"Coming Soon Probe {Guid.NewGuid():N}";
            var probeId = await _fixture.InsertGameParametersAsync(new GameParameters
            {
                Name = probeName, Description = probeName, AllowedCharacters = GameCharacterType.Letters,
                ChoiceRequiredLength = 6, RequiredAmount = 2, MinimumNumberOfPlayers = 2,
                AcceptDuplicatedCharacters = true, Active = false, ComingSoon = false,
                PresetChoices = new List<PresetChoice> { new("rabbit"), new("monkey") },
            });
            var defaults = new List<DefaultGameSettings> { new() { Name = probeName, ComingSoon = true } };

            var before = await db.Set<GameParameters>().AsNoTracking().Include(p => p.PresetChoices).SingleAsync(p => p.Id == probeId);

            var result = ComingSoonReconciler.Reconcile(db, defaults);

            result.Patched.ShouldBe(1);
            result.NewlyGated.ShouldContain(probeId);

            var after = await db.Set<GameParameters>().AsNoTracking().Include(p => p.PresetChoices).SingleAsync(p => p.Id == probeId);

            after.ComingSoon.ShouldBeTrue();
            after.Active.ShouldBe(before.Active);
            after.Name.ShouldBe(before.Name);
            after.RequiredAmount.ShouldBe(before.RequiredAmount);
            after.MinimumNumberOfPlayers.ShouldBe(before.MinimumNumberOfPlayers);
            after.PresetChoices.Select(x => x.Choice).ShouldBe(before.PresetChoices.Select(x => x.Choice), ignoreOrder: true);

            var again = ComingSoonReconciler.Reconcile(db, defaults);
            again.Patched.ShouldBe(0);
            again.NewlyGated.ShouldBeEmpty();
```

4. Append the house-edge reconcile test:

```csharp
        [Fact]
        public async Task HouseEdgeReconcile_AddsNewWords_DeactivatesRemovedOnes_WithoutTouchingStakeBounds()
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();

            var probeName = $"House Edge Probe {Guid.NewGuid():N}";
            var probeId = await _fixture.InsertGameParametersAsync(new GameParameters
            {
                Name = probeName, Description = probeName, AllowedCharacters = GameCharacterType.Letters,
                ChoiceRequiredLength = 8, RequiredAmount = 0.01m, MaximumStake = 0.2m, RtpPercent = 95m,
                BoardLadder = "4,6", IsHouseEdge = true, MinimumNumberOfPlayers = 1, Active = false,
                PresetChoices = new List<PresetChoice>
                {
                    new("banana") { Multiplier = 4m },
                    new("rocket") { Multiplier = 6m },
                    new("stalest") { Multiplier = 6m },
                },
            });

            var defaults = new List<DefaultGameSettings>
            {
                new()
                {
                    Name = probeName,
                    HouseEdge = new HouseEdgeSettings
                    {
                        RtpPercent = 95m,
                        MaximumStake = 9m,
                        Ladder = new List<decimal> { 4m, 6m, 8m },
                        Words = new List<HouseEdgeWordSettings>
                        {
                            new() { Word = "banana", Multiplier = 4m },
                            new() { Word = "rocket", Multiplier = 8m },
                            new() { Word = "planet", Multiplier = 6m },
                        },
                    },
                },
            };

            HouseEdgeReconciler.Reconcile(db, defaults).ShouldBe(4); // ladder, rocket, stalest, planet

            var after = await db.Set<GameParameters>().AsNoTracking().Include(p => p.PresetChoices).SingleAsync(p => p.Id == probeId);
            after.BoardLadder.ShouldBe("4,6,8");
            after.MaximumStake.ShouldBe(0.2m);
            after.RequiredAmount.ShouldBe(0.01m);
            after.PresetChoices.Single(x => x.Choice == "rocket").Multiplier.ShouldBe(8m);
            after.PresetChoices.Single(x => x.Choice == "stalest").Active.ShouldBeFalse();
            after.PresetChoices.Single(x => x.Choice == "planet").Active.ShouldBeTrue();
            after.PresetChoices.Single(x => x.Choice == "banana").Multiplier.ShouldBe(4m);

            HouseEdgeReconciler.Reconcile(db, defaults).ShouldBe(0);
        }
```

Add usings at the top if missing: `using System.Collections.Generic;`, `using SpaceStroker.Domain.Games.ValueObjects;`.

- [ ] **Step 5: Run the integration suite**

Run: `scripts\run_integration_tests.bat` (Docker running)
Expected: PASS — the 4 `HyperRollFlowTests`, the updated `DefaultGameSeedingTests`, and every pre-existing suite (`GameFlowTests`, `SolSettlementFlowTests`, `LeaderboardsFlowTests`, `ProvablyFairFlowTests`, `ChoiceExclusivityFlowTests`, `ComingSoonGameFlowTests`) unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Tests.Integration
git commit -m "Add Hyper Roll end-to-end tests and retarget the seeding tests."
git push origin feature/hyper-roll
```

---

### Task 11: Full verification

- [ ] **Step 1: Unit suite**

Run: `scripts\run_tests.bat`
Expected: PASS, zero failures.

- [ ] **Step 2: Integration suite**

Run: `scripts\run_integration_tests.bat`
Expected: PASS, zero failures.

- [ ] **Step 3: Record the changed tests for the report**

Added: `HouseEdgeRulesTests` (all), `HouseEdgeBoardTests` (all), `HyperRollFlowTests` (4). Extended: `ProvablyFairTests` (+4), `SpaceStrokerTyperTests` (+5), `BetDomainServiceTests` (+10 cases), `RegisterBetHandlerTests` (+1), `RewardGameConsumerTests` (+3), `GetGameVerificationHandlerTests` (+1), `HouseSeedBetServiceTests` (+1), `OrbitSettingsBindingTests` (2 inline data changed, +1), `DefaultGameSeedingTests` (1 replaced, 1 rewritten, +1).

- [ ] **Step 4: Confirm the branch is pushed**

```bash
git status --short --branch
git log --oneline origin/main..feature/hyper-roll
```

Expected: clean tree, `## feature/hyper-roll...origin/feature/hyper-roll` with no ahead/behind marker.
