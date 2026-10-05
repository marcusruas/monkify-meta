# Orbit auto-bet for Monkey Duel & Hyper Roll — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Orbit's auto-bet play Hyper Roll and VS HOUSE Monkey Duel through a second, solo-round engine loop, with a player-set stake and (Hyper Roll) mode + multiplier.

**Architecture:** A new `OrbitSoloRoundDueEvent` → `OrbitSoloRoundConsumer` → `OrbitSoloAutoBetService.RunOnce(playerId)` tick opens the player's own round (`IGameService.OpenSoloBoard` / `OpenDuelRoom(House)`), picks the slot/word, and places through the unchanged `IOrbitStakePlacer`. Settlement (`RewardGameConsumer`) and refunds re-schedule the next tick after `SoloRoundGapSeconds` via Hangfire; a startup pass and a per-minute sweep keep it alive. The shared-round planner is fenced off solo types first.

**Tech Stack:** .NET 8, EF Core (Npgsql, NoTracking default), MassTransit (Postgres transport), Hangfire (Postgres, Consumers only), SignalR (`ActiveGamesHub`, Redis backplane), xUnit + Shouldly + Moq, Testcontainers.

**Spec:** `monkify-meta/specs/auto-bet-duel-hyper-roll.md` (authoritative). **Worktree:** `E:\Monkify\.worktrees\auto-bet-duel-hyper-roll\monkify-backend` on `feature/auto-bet-duel-hyper-roll`.

## Global Constraints

- Work only in the worktree above; never touch `E:\Monkify\monkify-backend` (the `main` hub).
- Unit tests: `cmd //c scripts\run_tests.bat` from the backend worktree root (single-class iteration: `cmd //c scripts\run_tests.bat --filter "FullyQualifiedName~<Class>"` — check the script forwards args; if not, `dotnet test src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~<Class>"`). NEVER run the full `dotnet test` (ProductionSessions Monte-Carlo suite).
- Integration tests: `cmd //c scripts\run_integration_tests.bat` (Docker running).
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`. Migrations via `dotnet ef migrations add` (never hand-edit the snapshot). Applied only by Consumers.
- DbContext is **NoTracking by default**: every write is `ExecuteUpdateAsync`, `.AsTracking()` load, or explicit `Update` — a load-mutate-SaveChanges on a NoTracking entity silently persists nothing.
- Money moves only in Consumers. The loop never writes Orbit ledger rows; only `OrbitStakePlacer` / `OrbitBetSettlementService` do.
- Settings refusals reuse `ErrorMessages.OrbitRequestInvalid` (vague). The waiting status never says why.
- Bulk `sed -i` over .cs flips CRLF→LF repo-wide; edit files individually with the Edit tool.
- Every new integration suite that seeds an `OrbitEnrollment` implements `IAsyncLifetime.DisposeAsync => _fixture.DisableAllOrbitEnrollmentsAsync()`.
- Commit after every task: `git -C E:\Monkify\.worktrees\auto-bet-duel-hyper-roll\monkify-backend commit` with a one-line message ending in the trailer `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Don't push (the controller pushes).

## Facts from the codebase (verified)

- `OrbitEnrollment` — `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs`; one row per player (`HasIndex(PlayerId).IsUnique()`).
- `GameParameters` has `IsHouseEdge`, `IsDuel`, `AllowOrbit`, `RequiredAmount` (min stake), **`MaximumStake` (decimal?, the ceiling for BOTH house-edge and duel types)**, `BoardModes` (`GameBoardMode { Name, RtpPercent, Ladder, Position }`).
- `HouseEdgeRules.ParseLadder(string?) : IReadOnlyList<HouseEdgeSlot>`; `HouseEdgeSlot(int Length, decimal Multiplier, int Weight)`.
- `GameBoardChoice { GameId, Choice, Multiplier, Weight, Position }` (rows of `Game.BoardChoices`).
- `IGameService.OpenSoloBoard(GameParameters, string? mode = null) : Task<Guid?>`; `IGameService.OpenDuelRoom(GameParameters, DuelKind) : Task<Guid?>`. Neither publishes `GameOpenedEvent`.
- Coverage: `IHouseEdgeCoverageService.EvaluateAsync(new HouseEdgeNewBet(Guid? GameId, string Choice, decimal Amount, decimal? Multiplier), ct) : HouseEdgeCoverageDecision { IsCoverable }`. Duel multiplier = `DuelHouseRules.PayoutMultiplier(settings.Operator.Token.CommissionPercentage)`. Maintenance = `settings.Games?.HouseEdgeEnabled ?? true`.
- Placement: `IOrbitStakePlacer.Place(new StakePlacement(gameId, player, stake, choice, seed, nonce, OrbitAutoPlaced, PlacementRequestId, Memo))` → `StakePlacementResult(Outcome)`; `IsLowOnFees()`. AUTO claims (PlacementRequestId null) dedupe per (player, game).
- Choice/seed: `OrbitChoiceGenerator.DeriveChoice(serverSeed, playerId, attempt, parameters)`, `OrbitChoiceGenerator.DeriveSeed(serverSeed, playerId)` (`src/SpaceStroker.Domain/Orbit/Services/OrbitChoiceGenerator.cs`).
- Scheduling pattern: `GameService.ScheduleOrbitGateExpiry` → `BackgroundJob.Schedule(() => _messageBus.PublishEventAsync(evt), delay)`.
- Private channel push: `OrbitNotificationConsumer` → `IHubContext<ActiveGamesHub>.Clients.Group(OrbitNotificationChannel.GroupFor(playerId)).SendAsync(settings.Games.OrbitNotificationEndpoint, payload)`. Endpoint names are `GameSettings` string properties set in `spacestrokersettings.json` (`"OrbitNotificationEndpoint": "OrbitNotification"`).
- Orbit options class: `OrbitSettings` (`src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`) at `GeneralSettings.Operator.Token.Orbit`; JSON block in `src/SpaceStroker.Common/spacestrokersettings.json` ~l.200.
- `AllowOrbit` is healed from config by name at Consumers startup (`CreateDefaultGameParametersAsync`, ExecuteUpdate) — flipping JSON is enough.
- Claims: `IOrbitChoiceClaimService.TryClaim(playerId, typeId, choice, ct)`, `Release(playerId, ct)` (drops every claim the player holds), `IsValidForType(choice, parameters)`.
- Latest migration: `20261001185217_HolderJackpot`.

---

### Task 1: Domain helpers, entity columns, config, migration

**Files:**
- Create: `src/SpaceStroker.Domain/Orbit/Services/OrbitSoloTypes.cs`
- Create: `src/SpaceStroker.Domain/Orbit/Services/OrbitSlotPicker.cs`
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs`
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` (add `OrbitAutoRoundEndpoint` beside `OrbitNotificationEndpoint`, l.~94)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (Orbit block: `"SoloRoundGapSeconds": 5`; Games block: `"OrbitAutoRoundEndpoint": "OrbitAutoRound"` beside `OrbitNotificationEndpoint`). Do NOT flip `AllowOrbit` yet (Task 6).
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (OrbitEnrollment block, ~l.408)
- Create: migration `OrbitSoloAutoBet` (generated)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitSlotPickerTests.cs`, `OrbitSoloTypesTests.cs` (place next to the existing Orbit domain tests — find `OrbitChoiceGeneratorTests` and mirror its folder/namespace)

**Interfaces — Produces:**
- `OrbitSoloTypes.IsSolo(GameParameters p) : bool` (`p.IsHouseEdge || p.IsDuel`)
- `OrbitSlotPicker.Pick(string serverSeed, Guid playerId, Guid gameId, IReadOnlyList<GameBoardChoice> rowsByPosition, decimal? multiplier) : GameBoardChoice`
- `OrbitEnrollment.{Stake, BoardMode, Multiplier, SoloRoundId, SoloLeaseUntil, NextSoloRoundAt, WaitingForHouseSince}`
- `OrbitSettings.SoloRoundGapSeconds` (int, default 5), `OrbitSettings.SoloLeaseSeconds` (int, default 60)
- `GameSettings.OrbitAutoRoundEndpoint` (string)

- [ ] **Step 1: Write failing tests**

```csharp
public class OrbitSlotPickerTests
{
    private static List<GameBoardChoice> Board(params decimal[] mults) =>
        mults.Select((m, i) => new GameBoardChoice { Choice = $"word{i}", Multiplier = m, Weight = 1, Position = i }).ToList();

    [Fact]
    public void SingleMatchingSlot_IsPicked()
    {
        var rows = Board(3, 4, 5, 6, 7);
        OrbitSlotPicker.Pick("seed", Guid.NewGuid(), Guid.NewGuid(), rows, 5m).Multiplier.ShouldBe(5m);
    }

    [Fact]
    public void DuplicatedMultiplier_PicksOnlyAmongMatches()
    {
        var rows = Board(4, 6, 8, 8, 9, 9, 10, 10);
        for (var i = 0; i < 50; i++)
            OrbitSlotPicker.Pick($"seed{i}", Guid.NewGuid(), Guid.NewGuid(), rows, 10m).Multiplier.ShouldBe(10m);
    }

    [Fact]
    public void DuplicatedMultiplier_ReachesBothSlots()
    {
        var rows = Board(4, 6, 8, 8, 9, 9, 10, 10);
        var picked = Enumerable.Range(0, 200)
            .Select(i => OrbitSlotPicker.Pick($"seed{i}", Guid.NewGuid(), Guid.NewGuid(), rows, 10m).Position)
            .Distinct().OrderBy(x => x).ToList();
        picked.ShouldBe(new List<int> { 6, 7 });
    }

    [Fact]
    public void Random_CanReachEverySlot()
    {
        var rows = Board(3, 4, 5, 6, 7);
        var picked = Enumerable.Range(0, 400)
            .Select(i => OrbitSlotPicker.Pick($"seed{i}", Guid.NewGuid(), Guid.NewGuid(), rows, null).Position)
            .Distinct().Count();
        picked.ShouldBe(5);
    }

    [Fact]
    public void MultiplierNotOnBoard_FallsBackToRandom()
    {
        var rows = Board(3, 4, 5, 6, 7);
        Should.NotThrow(() => OrbitSlotPicker.Pick("seed", Guid.NewGuid(), Guid.NewGuid(), rows, 10m));
    }

    [Fact]
    public void IsDeterministic_ForTheSameInputs()
    {
        var rows = Board(4, 6, 8, 8, 9, 9, 10, 10);
        var player = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var game = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var a = OrbitSlotPicker.Pick("known-seed", player, game, rows, null);
        var b = OrbitSlotPicker.Pick("known-seed", player, game, rows, null);
        a.Position.ShouldBe(b.Position);
        // Pin the vector once the implementation exists: replace with the observed Position.
    }
}
```

After the implementation passes, run `IsDeterministic_ForTheSameInputs` once, read the Position, and add `a.Position.ShouldBe(<observed>);` so the derivation is pinned (known-answer vector).

`OrbitSoloTypesTests`: `IsSolo` true for `IsHouseEdge`, true for `IsDuel`, false for a race.

- [ ] **Step 2: Run, verify FAIL** (types missing → compile error).

- [ ] **Step 3: Implement**

```csharp
namespace SpaceStroker.Domain.Orbit.Services
{
    /// <summary>
    /// A SOLO type has no shared round: every round belongs to one player (Hyper Roll) or one creator
    /// vs the house (Monkey Duel). Orbit plays these through its own loop, never through the shared-round
    /// planner — the planner would push other enrolled players into somebody else's round.
    /// </summary>
    public static class OrbitSoloTypes
    {
        public static bool IsSolo(GameParameters parameters) => parameters.IsHouseEdge || parameters.IsDuel;
    }
}
```

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SpaceStroker.Domain.Orbit.Services
{
    /// <summary>
    /// Picks the Hyper Roll board slot an Orbit auto-bet backs (specs/auto-bet-duel-hyper-roll.md §6.5).
    /// Candidates are the slots carrying the player's multiplier; RANDOM (null) — or a multiplier the
    /// board no longer has — means every slot. The pick among candidates is UNIFORM, derived from the
    /// round's committed server seed with 32-bit rejection sampling (no modulo bias), so it is
    /// reproducible offline once the seed is revealed.
    /// </summary>
    public static class OrbitSlotPicker
    {
        public static GameBoardChoice Pick(string serverSeed, Guid playerId, Guid gameId,
                                           IReadOnlyList<GameBoardChoice> rowsByPosition, decimal? multiplier)
        {
            if (rowsByPosition.Count == 0)
                throw new ArgumentException("A board has at least one slot.", nameof(rowsByPosition));

            var candidates = multiplier is { } wanted
                ? rowsByPosition.Where(x => x.Multiplier == wanted).ToList()
                : new List<GameBoardChoice>();

            if (candidates.Count == 0)
                candidates = rowsByPosition.ToList();

            if (candidates.Count == 1)
                return candidates[0];

            return candidates[UniformIndex(serverSeed, $"orbit-slot|{gameId:N}|{playerId:N}", candidates.Count)];
        }

        private static int UniformIndex(string serverSeed, string prefix, int count)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(serverSeed));
            var limit = uint.MaxValue - (uint.MaxValue % (uint)count);

            for (var counter = 0; ; counter++)
            {
                var block = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{prefix}|{counter}"));
                for (var offset = 0; offset + 4 <= block.Length; offset += 4)
                {
                    var value = BitConverter.ToUInt32(block, offset);
                    if (value < limit)
                        return (int)(value % (uint)count);
                }
            }
        }
    }
}
```

`OrbitEnrollment` — append:

```csharp
        // ---- Solo-type setup (Monkey Duel / Hyper Roll, specs/auto-bet-duel-hyper-roll.md §4) ----
        /// <summary>Stake per auto round on a solo type. Null ⇒ the type's RequiredAmount. Ignored on Cosmic Sprint.</summary>
        public decimal? Stake { get; set; }
        /// <summary>Hyper Roll board mode name. Null ⇒ the type's default (lowest Position) mode.</summary>
        public string? BoardMode { get; set; }
        /// <summary>Hyper Roll multiplier to back. Null ⇒ RANDOM.</summary>
        public decimal? Multiplier { get; set; }

        // ---- Solo loop bookkeeping. ⚠️ Written ONLY with ExecuteUpdate (NoTracking context). ----
        /// <summary>The auto round currently in flight, if any — the one-in-flight guard.</summary>
        public Guid? SoloRoundId { get; set; }
        /// <summary>Short lease taken BEFORE a round is opened, so a duplicate tick never opens a second one.</summary>
        public DateTime? SoloLeaseUntil { get; set; }
        /// <summary>Earliest time the next solo round may open (the gap).</summary>
        public DateTime? NextSoloRoundAt { get; set; }
        /// <summary>Set while the house refuses the bet (coverage / maintenance); cleared on a placed bet.</summary>
        public DateTime? WaitingForHouseSince { get; set; }
```

`OrbitSettings` — append `public int SoloRoundGapSeconds { get; set; } = 5;` and `public int SoloLeaseSeconds { get; set; } = 60;` with a one-line `///` each.

`GameSettings` — `public string OrbitAutoRoundEndpoint { get; set; }` next to `OrbitNotificationEndpoint`.

DbContext, inside `modelBuilder.Entity<OrbitEnrollment>(builder => { ... })`:

```csharp
                builder.Property(x => x.Stake).HasPrecision(18, 9);
                builder.Property(x => x.BoardMode).HasMaxLength(20);
                builder.Property(x => x.Multiplier).HasPrecision(9, 2);
                // The solo sweep's query: enabled enrolments with nothing in flight.
                builder.HasIndex(x => new { x.Enabled, x.SoloRoundId });
```

- [ ] **Step 4: Generate the migration**

```bash
cd E:/Monkify/.worktrees/auto-bet-duel-hyper-roll/monkify-backend/src
dotnet ef migrations add OrbitSoloAutoBet -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers
```

Expected: a migration adding 7 nullable columns to `OrbitEnrollments` + one index; nothing else. If it contains unrelated changes, stop and report.

- [ ] **Step 5: Run the two test classes → PASS; pin the known-answer vector.** Run the whole unit script once → green.
- [ ] **Step 6: Commit** — `Add Orbit solo-type helpers, slot picker and enrolment columns (#200).`

---

### Task 2: Fence the shared-round engine off solo types

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` (`PlanPlacements` l.101; `IsOrbitEligible` l.253)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` (l.~99)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` (after the accepting-bets check, l.~60)
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`TryReclaimWord` l.~289; `EvaluateZeroOut` l.221)
- Test: existing `OrbitBetPlacementServiceTests`, `BetPlacedConsumerTests`, `PlaceOrbitBetConsumerTests`, `OrbitBetSettlementServiceTests` (find them under `src/SpaceStroker.Tests/UnitTests`; follow their fixture style)

**Interfaces — Consumes:** `OrbitSoloTypes.IsSolo` (Task 1).

- [ ] **Step 1: Failing tests**
  1. `PlanPlacements` on a game whose `Parameters.IsHouseEdge = true` (and `AllowOrbit = true`) with an Enabled funded enrolment on that type → no `PlaceOrbitBetEvent` published, `ExpectedOrbitBets` untouched. Same for `IsDuel = true`.
  2. `BetPlacedConsumer` for a house-edge round with `ExpectedOrbitBets == null` → `IOrbitBetPlacementService.PlanPlacements` never invoked (mock verify `Times.Never`).
  3. `PlaceOrbitBetConsumer` with a house-edge round → `SkipPlacement` called, `placer.Place` never called.
  4. `EvaluateZeroOut` for an Enabled enrolment on a duel type with `Stake = 0.05` and settled balance `0.03` (≥ RequiredAmount 0.002) → treated as zero-out (Release called / ZeroOutNotifiedAt path), because the effective stake is 0.05.
  5. `TryReclaimWord` (via `EvaluateZeroOut` with balance ≥ stake) on a duel enrolment with a `Choice` → `claims.TryClaim` never called.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement**
  - `IsOrbitEligible(p)` → `p.AllowOrbit && !p.ComingSoon && p.LotteryParameters == null && !OrbitSoloTypes.IsSolo(p)`. This one change covers `PlanPlacements` (and `ResolveCandidates`' caller at l.43). Add a comment: solo types are played by `OrbitSoloAutoBetService`, never planned per shared round.
  - `BetPlacedConsumer`: wrap the re-plan: `if (!OrbitSoloTypes.IsSolo(game.Parameters) && (game.ExpectedOrbitBets is null || ...))`.
  - `PlaceOrbitBetConsumer`: after the `GameAcceptingBets` check: `if (OrbitSoloTypes.IsSolo(game.Parameters)) { await placement.SkipPlacement(message.GameId, message.PlayerId, "solo rounds are played by the solo loop"); return; }`.
  - `EvaluateZeroOut`: select `new { x.RequiredAmount, x.IsHouseEdge, x.IsDuel }`; `var stake = (isHouseEdge || isDuel) ? (enrollment.Stake ?? requiredAmount) : requiredAmount;` and compare the balance with `stake`.
  - `TryReclaimWord`: also return early when `OrbitSoloTypes.IsSolo(parameters)` (no standing claim on solo types, spec decision 11).

- [ ] **Step 4: Run the four test classes → PASS; full unit script → green.**
- [ ] **Step 5: Commit** — `Keep the shared Orbit planner and word claims off solo game types (#200).`

---

### Task 3: Settings — `PATCH /orbit/settings` (spec §6.2)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsHandler.cs`
- Test: existing `UpdateOrbitSettingsHandlerTests` (find under `UnitTests/Handlers/Orbit`)

**Interfaces — Produces:** body fields `Stake`/`ClearStake`, `BoardMode`/`ClearBoardMode`, `Multiplier`/`ClearMultiplier`; the handler publishes `OrbitSoloRoundDueEvent(playerId)` (record defined in Task 5 — **create it in this task** at `src/SpaceStroker.Infrastructure/Consumers/OrbitSoloRound/OrbitSoloRoundDueEvent.cs` as `public record OrbitSoloRoundDueEvent(Guid PlayerId);` so this task compiles; Task 5 reuses it).

- [ ] **Step 1: Failing tests** (one `[Fact]` each):
  1. Duel type, `Stake = 0.001` (< RequiredAmount 0.002) → `OrbitRequestInvalid`, nothing saved.
  2. Duel type, `Stake = 0.2` (> MaximumStake 0.1) → refused.
  3. Hyper Roll, `Stake = 0.0100000001` (10 dp) → refused.
  4. Cosmic Sprint type, `Stake = 0.01` → refused.
  5. Hyper Roll, `BoardMode = "super"` → stored as `"SUPER"` (canonical row name).
  6. Hyper Roll, `BoardMode = "MEGA"` → refused; duel with any `BoardMode` → refused.
  7. Hyper Roll AVERAGE, `Multiplier = 3` → stored; `Multiplier = 10` → refused (not on AVERAGE).
  8. Stored `BoardMode = AVERAGE, Multiplier = 7`, PATCH `BoardMode = HYPER` → stored `Multiplier = null`.
  9. Enabled on Hyper Roll, PATCH `Stake = 0.02` → refused; PATCH `GameParametersId = duel` → refused.
  10. Enabled on Hyper Roll, PATCH `{ Enabled = false, Stake = 0.02 }` → accepted, `Enabled = false`, `Stake = 0.02`.
  11. Enabled on Hyper Roll, PATCH `Choice = "rocket"` and `CashOutFloor` → accepted (word/rules stay editable).
  12. Paused on Cosmic Sprint holding a claim, PATCH `GameParametersId = duel` → `claims.Release(player)` called; `Stake/BoardMode/Multiplier` reset to null; `Choice` kept.
  13. Paused, PATCH `GameParametersId = duel, Choice = "rocket"`, then `Enabled = true` funded → `claims.TryClaim` **never** called.
  14. PATCH `Enabled = true` on a duel with no `Choice` and `AutoPickChoice = false` → `OrbitChoiceRequired` if that message exists, else `OrbitRequestInvalid` (grep `ErrorMessages` for the shipped "pick a word first" message the front maps; reuse it).
  15. PATCH `Enabled = true` on Hyper Roll → publishes `OrbitSoloRoundDueEvent(player.Id)`, **does not** publish `GameOpenedEvent`; `WaitingForHouseSince` and `NextSoloRoundAt` are null afterwards.
  16. PATCH `Enabled = false` on Hyper Roll with `WaitingForHouseSince` set → cleared; `SoloRoundId` untouched.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement.** Body additions (with `///` matching the file's style):

```csharp
        /// <summary>Stake per auto round on a solo type (Duel / Hyper Roll). Null = leave alone; ClearStake ⇒ the type minimum.</summary>
        public decimal? Stake { get; set; }
        public bool ClearStake { get; set; }
        /// <summary>Hyper Roll board mode (AVERAGE / SUPER / HYPER). ClearBoardMode ⇒ the type's default mode.</summary>
        public string? BoardMode { get; set; }
        public bool ClearBoardMode { get; set; }
        /// <summary>Hyper Roll multiplier to back. ClearMultiplier ⇒ RANDOM.</summary>
        public decimal? Multiplier { get; set; }
        public bool ClearMultiplier { get; set; }
```

Handler algorithm (insert after the enrolment is loaded/created and BEFORE the existing field assignments; keep the existing code otherwise):

```csharp
            // ---- 1. lock while enabled (spec §6.2.1) ----------------------------------------------
            // Pausing in the SAME request is allowed: Enabled=false is applied first, so "pause + switch" works.
            var willBeEnabled = body.Enabled ?? enrollment?.Enabled ?? false;
            var wasEnabled = enrollment?.Enabled ?? false;
            if (wasEnabled && willBeEnabled && ChangesSoloSetup(enrollment!, body))
                return Reject(ErrorMessages.OrbitRequestInvalid);
```

with

```csharp
        private static bool ChangesSoloSetup(OrbitEnrollment e, UpdateOrbitSettingsRequestBody b)
            => (b.GameParametersId is { } t && t != e.GameParametersId)
               || (b.ClearStake && e.Stake != null) || (b.Stake is { } s && s != e.Stake)
               || (b.ClearBoardMode && e.BoardMode != null)
               || (b.BoardMode is { } m && !string.Equals(m, e.BoardMode, StringComparison.OrdinalIgnoreCase))
               || (b.ClearMultiplier && e.Multiplier != null) || (b.Multiplier is { } x && x != e.Multiplier);
```

Then, after `GameParametersId` is applied, resolve the **target parameters** once, including `BoardModes`:

```csharp
            var typeChanged = body.GameParametersId is { } newType && newType != previousTypeId;
            var target = enrollment.GameParametersId is { } targetId
                ? await Context.GameParameters.Include(x => x.PresetChoices).Include(x => x.BoardModes)
                    .FirstOrDefaultAsync(x => x.Id == targetId, cancellationToken)
                : null;

            if (typeChanged)
            {
                // Values are per type (a Hyper Roll multiplier means nothing on a duel).
                enrollment.Stake = null; enrollment.BoardMode = null; enrollment.Multiplier = null;
                // Leaving a shared free-text type surrenders its standing word claim (decision 11).
                if (previousType is { } old && !OrbitSoloTypes.IsSolo(old) && old.PresetChoices.IsNullOrEmpty())
                    await _claims.Release(player.Id, cancellationToken);
            }
```

(`previousTypeId` = `enrollment?.GameParametersId` captured before assignment; `previousType` = the loaded old `GameParameters` with `PresetChoices`, null when none.)

Validation (refuse with `OrbitRequestInvalid`):

```csharp
            if (body.ClearStake) enrollment.Stake = null;
            else if (body.Stake is { } stake)
            {
                if (target == null || !OrbitSoloTypes.IsSolo(target) || target.MaximumStake is not { } ceiling
                    || stake <= 0 || decimal.Round(stake, 9) != stake
                    || stake < target.RequiredAmount || stake > ceiling)
                    return Reject(ErrorMessages.OrbitRequestInvalid);
                enrollment.Stake = stake;
            }

            if (body.ClearBoardMode) enrollment.BoardMode = null;
            else if (body.BoardMode is { } modeName)
            {
                var mode = target?.IsHouseEdge == true
                    ? target.BoardModes.FirstOrDefault(x => string.Equals(x.Name, modeName.Trim(), StringComparison.OrdinalIgnoreCase))
                    : null;
                if (mode == null) return Reject(ErrorMessages.OrbitRequestInvalid);
                enrollment.BoardMode = mode.Name;
            }

            var effectiveMode = target?.IsHouseEdge == true
                ? (target.BoardModes.FirstOrDefault(x => x.Name == enrollment.BoardMode)
                   ?? target.BoardModes.OrderBy(x => x.Position).FirstOrDefault())
                : null;
            var ladder = effectiveMode == null ? Array.Empty<HouseEdgeSlot>() : HouseEdgeRules.ParseLadder(effectiveMode.Ladder);

            if (body.ClearMultiplier) enrollment.Multiplier = null;
            else if (body.Multiplier is { } multiplier)
            {
                if (effectiveMode == null || !ladder.Any(x => x.Multiplier == multiplier))
                    return Reject(ErrorMessages.OrbitRequestInvalid);
                enrollment.Multiplier = multiplier;
            }

            // A mode switch can strand a stored multiplier: fall back to RANDOM rather than refusing.
            if (enrollment.Multiplier is { } stored && effectiveMode != null && !ladder.Any(x => x.Multiplier == stored))
                enrollment.Multiplier = null;
```

Guard both existing `TryClaim` call sites (l.~181 and l.~213) with `&& !OrbitSoloTypes.IsSolo(parameters)` — for a solo type the word is validated (`IsValidForType`) and stored but never claimed (and call `_claims.Release(player.Id)` instead in the l.~181 branch so a stale claim cannot survive). Hyper Roll ignores `Choice`: skip `IsValidForType` when `parameters.IsHouseEdge` (its `ChoiceRequiredLength` is 10, the duel word is 6).

On enable (`body.Enabled == true`) of a duel with neither `Choice` nor `AutoPickChoice` → reject with the existing "pick a word" message (find it in `ErrorMessages`/`OrbitMessages`; if none exists, `OrbitRequestInvalid`).

Bookkeeping on enable/disable of a solo type — after `SaveChangesAsync`, using ExecuteUpdate so it cannot be lost to tracking:

```csharp
            if (target != null && OrbitSoloTypes.IsSolo(target) && body.Enabled is { } flipped)
                await Context.OrbitEnrollments.Where(x => x.PlayerId == player.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.WaitingForHouseSince, (DateTime?)null)
                        .SetProperty(x => x.NextSoloRoundAt, x => flipped ? null : x.NextSoloRoundAt), cancellationToken);
```

`ConsiderTheCurrentRound`: load the type's `IsHouseEdge/IsDuel`; for a solo type publish `new OrbitSoloRoundDueEvent(playerId)` instead of `GameOpenedEvent` (add a `Guid playerId` parameter).

- [ ] **Step 4: Run tests → PASS; full unit script → green.**
- [ ] **Step 5: Commit** — `Validate and lock Orbit solo-type settings (stake, mode, multiplier) (#200).`

---

### Task 4: Read side — `GET /orbit` and `GET /games/rounds/{id}` for House duels

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetHouseEdgeRound/GetHouseEdgeRoundHandler.cs` (l.34)
- Test: `GetOrbitHandlerTests`, `GetHouseEdgeRoundHandlerTests` (existing; extend)

- [ ] **Step 1: Failing tests**
  1. Three eligible types (race, house-edge with 3 modes, duel) → DTOs carry `IsHouseEdge`, `IsDuel`, `MaximumStake` (null for the race), `Modes` only for house-edge, ordered by `Position`, each with `Slots` in ladder order.
  2. Enrolment `Stake=0.05, BoardMode="SUPER", Multiplier=9` → mapped.
  3. `WaitingForHouse` true only when `WaitingForHouseSince != null && Enabled`.
  4. `CurrentAutoRoundId` = `SoloRoundId` when that game's status is in `Game.ActiveGamesStatus`; null when it is settled or missing.
  5. `GetHouseEdgeRound` returns a House-duel round in an active status; an OpenRoom duel round and a race round → `GameNotValidForBets`.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement**

```csharp
    // GetOrbitRequest.cs
    public class OrbitBoardSlotDto { public decimal Multiplier { get; set; } public int Weight { get; set; } }
    public class OrbitBoardModeDto
    {
        public string Name { get; set; }
        public int WordCount { get; set; }
        public decimal RtpPercent { get; set; }
        public List<OrbitBoardSlotDto> Slots { get; set; } = new();
    }
    // OrbitEligibleGameTypeDto += IsHouseEdge, IsDuel, decimal? MaximumStake, List<OrbitBoardModeDto> Modes = new()
    // OrbitDto += decimal? Stake, string? BoardMode, decimal? Multiplier, bool WaitingForHouse, Guid? CurrentAutoRoundId
```

In the handler's projection add `x.IsHouseEdge, x.IsDuel, x.MaximumStake, Modes = x.BoardModes.OrderBy(m => m.Position).Select(m => new { m.Name, m.RtpPercent, m.Ladder }).ToList()` and map with `HouseEdgeRules.ParseLadder(m.Ladder)` → `WordCount = slots.Count`, `Slots = slots.Select(s => new OrbitBoardSlotDto { Multiplier = s.Multiplier, Weight = s.Weight })`. `MaximumStake` only when `IsHouseEdge || IsDuel`. Keep `.OrderBy(x => x.Name)`.

`CurrentAutoRoundId`: `enrollment?.SoloRoundId is { } rid && await Context.Games.AnyAsync(g => g.Id == rid && Game.ActiveGamesStatus.Contains(g.Status), ct) ? rid : null`.

`GetHouseEdgeRoundHandler` l.34: accept `round.Parameters?.IsHouseEdge == true || (round.Parameters?.IsDuel == true && round.DuelKind == DuelKind.House)`. Make sure the DTO it builds tolerates a duel round (no `BoardChoices`): read the handler, and if the mapping assumes board rows, map a duel the way `GET /games/{type}?wallet=` (`GetGameInstance` handler) maps one — reuse that mapper rather than duplicating it.

- [ ] **Step 4: Run → PASS; full unit script → green.**
- [ ] **Step 5: Commit** — `Expose Orbit solo setup, modes and the in-flight auto round; serve House duels by round id (#200).`

---

### Task 5: The solo loop — `OrbitSoloAutoBetService` + consumer + realtime

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitSoloAutoBetService.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSoloAutoBetService.cs`
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitSoloRound/OrbitSoloRoundConsumer.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (register `IOrbitSoloAutoBetService` scoped, beside the other Orbit services)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/Orbit/OrbitSoloAutoBetServiceTests.cs` (mirror the folder of `OrbitBetPlacementServiceTests`; reuse its in-memory/SQLite or Postgres-less DbContext helper — whatever that test class uses)

**Interfaces — Produces:**

```csharp
public interface IOrbitSoloAutoBetService
{
    /// <summary>One idempotent tick for one player (spec §6.4).</summary>
    Task RunOnce(Guid playerId, CancellationToken cancellationToken = default);
    /// <summary>Settlement / refund hook (spec §6.6). No-op unless the round is a solo round with an auto bet.</summary>
    Task OnAutoRoundSettled(Guid gameId, CancellationToken cancellationToken = default);
    /// <summary>Hangfire-scheduled OrbitSoloRoundDueEvent.</summary>
    void ScheduleNext(Guid playerId, TimeSpan delay);
}
```

Push payload record (put it in `OrbitSoloAutoBetService.cs` or its own file under `Services/Orbit`): `public sealed record OrbitAutoRoundPush(Guid GameTypeId, Guid? RoundId, bool WaitingForHouse);` — serialized camelCase by SignalR.

**Dependencies:** `SpaceStrokerDbContext`, `IGameService`, `IOrbitStakePlacer`, `IOrbitLedgerService` (`ResolveSettledBalance(playerId, ct)`), `IHouseEdgeCoverageService`, `IOrbitAccountResolver`, `IMessageBus`, `IHubContext<ActiveGamesHub>`, `GeneralSettings`. Wrap `BackgroundJob.Schedule` behind a small injectable seam so tests can verify scheduling without Hangfire: `public interface IOrbitSoloScheduler { void Schedule(Guid playerId, TimeSpan delay); }` with `HangfireOrbitSoloScheduler` (`BackgroundJob.Schedule(() => bus.PublishEventAsync(new OrbitSoloRoundDueEvent(playerId)), delay)`); `ScheduleNext` delegates to it.

- [ ] **Step 1: Failing tests** — one `[Fact]` per row; assert DB state + mock calls:
  1. Enrolment not Enabled → no `OpenSoloBoard`/`OpenDuelRoom`, no `Place`.
  2. Enabled on Cosmic Sprint (not solo) → no-op.
  3. `SoloRoundId` points at a `WaitingBets`/`InProgress` game → no-op.
  4. `SoloRoundId` points at a settled game → cleared, then proceeds (opens a board).
  5. `NextSoloRoundAt = now + 3s` → `Schedule(player, ~3s)` called, nothing opened.
  6. Lease already held (`SoloLeaseUntil = now + 30s`) → nothing opened.
  7. Settled balance 0.005 < stake 0.01 → nothing opened, no `Schedule`, lease cleared.
  8. `HouseEdgeEnabled = false` → `WaitingForHouseSince` set, `NextSoloRoundAt ≈ now + gap`, `Schedule(gap)`, a push with `WaitingForHouse = true`, **`Place` never called, `OpenSoloBoard` never called**.
  9. Coverage `IsCoverable = false` (Hyper Roll) → same as 8; verify `EvaluateAsync` received `Amount = stake` and `Multiplier = top multiplier of the effective mode`.
  10. Happy Hyper Roll: `OpenSoloBoard(params, "AVERAGE")` returns a game id whose `BoardChoices` are seeded in the test; `Place` receives `Stake = enrollment.Stake`, `Choice = OrbitSlotPicker.Pick(...)`'s word, `OrbitAutoPlaced = true`, `PlacementRequestId = null`, `Memo = $"orbit-solo:{roundId:N}"`; `SoloRoundId = roundId`; push `RoundId = roundId`; outcome `Placed` → `WaitingForHouseSince` cleared.
  11. Happy duel: `OpenDuelRoom(params, DuelKind.House)`; `Choice = enrollment.Choice`; coverage multiplier = `DuelHouseRules.PayoutMultiplier(commission)`.
  12. Duel with no `Choice` and `AutoPickChoice` → choice = `OrbitChoiceGenerator.DeriveChoice(serverSeed, playerId, 0, parameters)`.
  13. Outcome `InsufficientFunds` → `SoloRoundId` cleared, no `Schedule`.
  14. Outcome `TransferFailed` → cleared, `Schedule(gap)`.
  15. Outcome `RegistrationRefused` → cleared, waiting set, `Schedule(gap)`.
  16. Outcome `InDoubt` → `SoloRoundId` kept, no `Schedule`.
  17. `OnAutoRoundSettled(gameId)` for a solo game with an `OrbitAutoPlaced` bet whose player's `SoloRoundId == gameId` → cleared, `NextSoloRoundAt ≈ now + gap`, `Schedule(gap)`; for a race game or a non-matching `SoloRoundId` → nothing.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement `RunOnce`** following spec §6.4 exactly. Skeleton:

```csharp
public async Task RunOnce(Guid playerId, CancellationToken ct = default)
{
    if (!_accounts.IsConfigured) return;
    var orbit = _settings.Operator.Token.Orbit;
    var gap = TimeSpan.FromSeconds(Math.Max(0, orbit.SoloRoundGapSeconds));
    var now = DateTime.UtcNow;

    var enrollment = await _context.OrbitEnrollments.FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);
    if (enrollment is not { Enabled: true, GameParametersId: { } typeId }) return;

    var parameters = await _context.GameParameters.Include(x => x.BoardModes)
        .FirstOrDefaultAsync(x => x.Id == typeId, ct);
    var player = await _context.Players.FirstOrDefaultAsync(x => x.Id == playerId, ct);
    if (parameters == null || player == null || !OrbitSoloTypes.IsSolo(parameters)
        || !parameters.Active || !parameters.AllowOrbit || parameters.ComingSoon
        || player.IsSelfExcluded(now))
        return;

    // 2. in flight?
    if (enrollment.SoloRoundId is { } current)
    {
        var live = await _context.Games.AnyAsync(g => g.Id == current && Game.ActiveGamesStatus.Contains(g.Status), ct);
        if (live) return;
        await _context.OrbitEnrollments.Where(x => x.PlayerId == playerId && x.SoloRoundId == current)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SoloRoundId, (Guid?)null), ct);
    }

    // 3. gap
    if (enrollment.NextSoloRoundAt is { } next && next > now) { ScheduleNext(playerId, next - now); return; }

    // 4. lease (before any round exists, so a duplicate tick never orphans a board)
    var leaseUntil = now.AddSeconds(orbit.SoloLeaseSeconds);
    var leased = await _context.OrbitEnrollments
        .Where(x => x.PlayerId == playerId && x.Enabled && x.SoloRoundId == null
                    && (x.SoloLeaseUntil == null || x.SoloLeaseUntil < now))
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SoloLeaseUntil, (DateTime?)leaseUntil), ct);
    if (leased == 0) return;

    try
    {
        // 5. stake & funds
        var stake = enrollment.Stake ?? parameters.RequiredAmount;
        if (await _ledger.ResolveSettledBalance(playerId, ct) < stake) { await ReleaseLease(playerId, ct); return; }
        if (await _placer.IsLowOnFees()) { await ReleaseLease(playerId, ct); ScheduleNext(playerId, gap); return; }

        // 6. house pre-check — no money yet
        var mode = ResolveMode(parameters, enrollment.BoardMode);           // null for a duel
        var worstMultiplier = parameters.IsDuel
            ? DuelHouseRules.PayoutMultiplier(_settings.Operator.Token.CommissionPercentage)
            : HouseEdgeRules.ParseLadder(mode!.Ladder).Max(x => x.Multiplier);
        var maintenanceOn = !(_settings.Games?.HouseEdgeEnabled ?? true);
        if (maintenanceOn || !(await _coverage.EvaluateAsync(new HouseEdgeNewBet(null, "", stake, worstMultiplier), ct)).IsCoverable)
        { await EnterWaiting(playerId, typeId, gap, ct); return; }

        // 7. open
        var roundId = parameters.IsDuel
            ? await _gameService.OpenDuelRoom(parameters, DuelKind.House)
            : await _gameService.OpenSoloBoard(parameters, mode!.Name);
        if (roundId is not { } gameId) { await EnterWaiting(playerId, typeId, gap, ct); return; }

        // 8. record + announce
        await _context.OrbitEnrollments.Where(x => x.PlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SoloRoundId, (Guid?)gameId)
                                      .SetProperty(x => x.SoloLeaseUntil, (DateTime?)null), ct);
        await Push(playerId, new OrbitAutoRoundPush(typeId, gameId, false));

        // 9. choice
        var game = await _context.Games.Include(x => x.BoardChoices).FirstAsync(x => x.Id == gameId, ct);
        var choice = parameters.IsDuel
            ? (!string.IsNullOrWhiteSpace(enrollment.Choice) ? enrollment.Choice
               : enrollment.AutoPickChoice ? OrbitChoiceGenerator.DeriveChoice(game.ServerSeed!, playerId, 0, parameters) : null)
            : OrbitSlotPicker.Pick(game.ServerSeed!, playerId, gameId,
                game.BoardChoices.OrderBy(x => x.Position).ToList(), enrollment.Multiplier).Choice;
        if (choice == null) { await ClearRound(playerId, gameId, gap, ct); ScheduleNext(playerId, gap); return; }

        // 10. place (unchanged money path)
        var result = await _placer.Place(new StakePlacement(gameId, player, stake, choice,
            OrbitChoiceGenerator.DeriveSeed(game.ServerSeed!, playerId), 0,
            OrbitAutoPlaced: true, PlacementRequestId: null, Memo: $"orbit-solo:{gameId:N}"));

        // 11. outcome
        switch (result.Outcome)
        {
            case StakePlacementOutcome.Placed:
                await _context.OrbitEnrollments.Where(x => x.PlayerId == playerId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.WaitingForHouseSince, (DateTime?)null), ct);
                break;
            case StakePlacementOutcome.InsufficientFunds:
                await ClearRound(playerId, gameId, null, ct); break;
            case StakePlacementOutcome.AlreadyClaimed:
            case StakePlacementOutcome.TransferFailed:
                await ClearRound(playerId, gameId, gap, ct); ScheduleNext(playerId, gap); break;
            case StakePlacementOutcome.RegistrationRefused:
                await ClearRound(playerId, gameId, gap, ct); await EnterWaiting(playerId, typeId, gap, ct); break;
            case StakePlacementOutcome.InDoubt:
                break; // leave SoloRoundId; the reconciler + sweep resolve it
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Orbit solo tick failed for player {PlayerId}.", playerId);
        await ReleaseLease(playerId, CancellationToken.None);
        ScheduleNext(playerId, gap);
    }
}
```

Helpers: `ResolveMode` (enrolment mode by name, else lowest `Position`), `ReleaseLease` (ExecuteUpdate `SoloLeaseUntil = null`), `ClearRound(playerId, gameId, gap?)` (ExecuteUpdate `SoloRoundId = null WHERE SoloRoundId == gameId`, `SoloLeaseUntil = null`, and `NextSoloRoundAt = now + gap` when gap given), `EnterWaiting` (ExecuteUpdate `WaitingForHouseSince = COALESCE(existing, now)` — do it as `.SetProperty(x => x.WaitingForHouseSince, x => x.WaitingForHouseSince ?? now)` — plus `SoloLeaseUntil = null`, `NextSoloRoundAt = now + gap`; then `Push(new OrbitAutoRoundPush(typeId, null, true))` and `ScheduleNext(gap)`), `Push` (hub group send with `_settings.Games.OrbitAutoRoundEndpoint`, wrapped in try/catch → `Log.Warning`; a failed push must never fail the tick).

**`OnAutoRoundSettled(gameId)`**: load the game with `Parameters` and `Bets`; return unless `OrbitSoloTypes.IsSolo(game.Parameters)`; for each distinct `PlayerId` of an `OrbitAutoPlaced` bet: `ExecuteUpdate SoloRoundId = null, NextSoloRoundAt = now + gap WHERE PlayerId == p && SoloRoundId == gameId`; if 1 row → `ScheduleNext(p, gap)`.

**Consumer:**

```csharp
public class OrbitSoloRoundConsumer : BaseConsumer<OrbitSoloRoundDueEvent>
{
    public OrbitSoloRoundConsumer(IServiceScopeFactory f) : base(f) { }
    public override async Task ProcessMessage(OrbitSoloRoundDueEvent message)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOrbitSoloAutoBetService>().RunOnce(message.PlayerId);
    }
}
```

- [ ] **Step 4: Run → PASS; full unit script → green.**
- [ ] **Step 5: Commit** — `Add the Orbit solo-round auto-bet loop (#200).`

---

### Task 6: Triggers, wiring, config flip

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (after both `EvaluateOrbitFloors` calls, l.75 and l.244)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` (after a successful `RefundToOrbit` of a persisted bet, l.~76–90)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/OrbitDepositCredited/OrbitDepositCreditedConsumer.cs`
- Create: `src/SpaceStroker.Infrastructure/Jobs/OrbitSoloAutoBetSweepJob.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (AddConsumer + own ReceiveEndpoint like `PlaceOrbitBetConsumer` l.183; `AddScoped<OrbitSoloAutoBetSweepJob>()` near l.80; recurring job near l.321; startup pass beside the existing Orbit startup re-plan (search Program.cs for the open-rounds re-plan comment ~l.279))
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` — `"AllowOrbit": true` for "Hyper Roll" and "Monkey Duel (CoinFlip)"
- Test: `OrbitSettingsBindingTests` (update), new `OrbitSoloAutoBetSweepJobTests`, `OrbitDepositCreditedConsumerTests` (extend or create), `RewardGameConsumer` / `RefundInvalidBetConsumer` tests (extend where they exist)

- [ ] **Step 1: Failing tests**
  1. `OrbitSettingsBindingTests.DefaultGames_CarryTheSeededOrbitEligibility`: InlineData Hyper Roll → `true`, "Monkey Duel (CoinFlip)" → `true` (Cosmic Sprint true, Space Jackpot false unchanged). `DefaultGames_HyperRoll_IsAValidHouseEdgeConfig`: change `AllowOrbit.ShouldBeFalse()` → `ShouldBeTrue()`. New: `Orbit.SoloRoundGapSeconds` binds to 5; `Games.OrbitAutoRoundEndpoint` binds to `"OrbitAutoRound"`.
  2. Sweep job: publishes `OrbitSoloRoundDueEvent` for an Enabled duel enrolment with `SoloRoundId == null` and `NextSoloRoundAt` past; not for a future `NextSoloRoundAt`; publishes for a `SoloRoundId` whose game is settled or missing; not for a live one; never for Cosmic Sprint or disabled enrolments.
  3. Deposit consumer: Enabled on Hyper Roll → publishes `OrbitSoloRoundDueEvent`, does NOT call `PlanPlacements`.
  4. RewardGame: a settled Hyper Roll round with an `OrbitAutoPlaced` bet → `IOrbitSoloAutoBetService.OnAutoRoundSettled(gameId)` invoked (both the winners and the no-winners path).
  5. RefundInvalidBet: successful refund of an `OrbitAutoPlaced` bet on a solo round → `OnAutoRoundSettled` invoked.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement**
  - RewardGame: after each `EvaluateOrbitFloors(...)`: `await scope.ServiceProvider.GetRequiredService<IOrbitSoloAutoBetService>().OnAutoRoundSettled(game.Id);` in a try/catch logging Error (a scheduling failure must never undo settlement — the sweep recovers).
  - RefundInvalidBet: same call when `result.IsSuccess && bet.OrbitAutoPlaced && bet.GameId` round is solo (the service checks solo itself; just pass `bet.GameId`).
  - Deposit consumer: after loading the target type id, load `IsHouseEdge/IsDuel`; if solo → `await bus.PublishEventAsync(new OrbitSoloRoundDueEvent(message.PlayerId)); return;`.
  - Sweep job `Run()`: query `OrbitEnrollments` where `Enabled && GameParametersId != null` joined to solo `GameParameters`; select those with (`SoloRoundId == null && (NextSoloRoundAt == null || NextSoloRoundAt <= now)`) or (`SoloRoundId != null` and no game with that id in `Game.ActiveGamesStatus`); publish one `OrbitSoloRoundDueEvent` each. Log Information with the count.
  - Program.cs: `x.AddConsumer<OrbitSoloRoundConsumer>();` + `cfg.ReceiveEndpoint(nameof(OrbitSoloRoundConsumer), e => { /* copy PlaceOrbitBetConsumer's endpoint settings */ e.ConfigureConsumer<OrbitSoloRoundConsumer>(context); });` with a comment (own endpoint: a slow on-chain placement must not queue behind/ahead of other Orbit work). `builder.Services.AddScoped<OrbitSoloAutoBetSweepJob>();`, `recurringJobs.AddOrUpdate<OrbitSoloAutoBetSweepJob>("orbit-solo-autobet-sweep", job => job.Run(), Cron.Minutely());`. Register `IOrbitSoloScheduler` → `HangfireOrbitSoloScheduler` in DI where Hangfire is available (Consumers); in the API host register a no-op or skip (the API never resolves the service). Startup pass: after migrations + default-games seeding, publish `OrbitSoloRoundDueEvent` for every Enabled enrolment on a solo type (best-effort, try/catch).
  - Flip `AllowOrbit` for both types in `spacestrokersettings.json`.

- [ ] **Step 4: Run → PASS; full unit script → green.**
- [ ] **Step 5: Commit** — `Drive the Orbit solo loop from settlement, deposits, startup and a sweep; open Duel and Hyper Roll to Orbit (#200).`

---

### Task 7: Integration tests (mandatory — Feature)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/OrbitSoloAutoBetFlowTests.cs`
- Modify (if needed): `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` — a helper to set `Orbit.SoloRoundGapSeconds` (mirror how `SetHouseEdgeEnabled` / `SetRacePacing` mutate `GeneralSettings` at runtime) and a `PatchOrbitSettingsAsync(wallet, body)` helper if one doesn't exist (look at `OrbitSingleControlFlowTests` / `OrbitWalletFlowTests` for how they PATCH with `MintOrbitSession`).

Read first: `OrbitWalletFlowTests` (`HyperRoll_ManualOrbitWin_CreditsStakeTimesMultiplierToCustody`, `HouseDuel_OrbitFunded_WinLandsInOrbit`) — reuse their setup for treasury balance, HouseSeed, `SteerHouseDuelAsync`, `WaitForHouseEdgeRoundsToSettleAsync`, ledger assertions.

Class implements `IAsyncLifetime` with `DisposeAsync => _fixture.DisableAllOrbitEnrollmentsAsync()` and `InitializeAsync` calling it too. Set the gap to 1 s in a try/finally.

- [ ] **Step 1: Write the four tests** (spec §11.2):
  1. `HyperRoll_AutoBet_PlaysRoundsBackToBack` — seed an enrolment on Hyper Roll with 1 SOL (`SeedOrbitEnrollmentAsync` then PATCH `{ stake: 0.01, boardMode: "AVERAGE", multiplier: 3 }` while paused — the seed helper enables, so PATCH `{ enabled:false }` first or seed disabled), then PATCH `{ enabled: true }`. Poll (≤ 30 s) for a Hyper Roll game with an `OrbitAutoPlaced` bet by this player of `0.01` whose `Choice` is the 3× board word. Wait for settlement. Assert the ledger has `BetDebit −0.01` and, if won, `WinCredit +0.03`; balance == SUM(ledger). Poll for a **second** auto round with a later `CreatedDate`. PATCH `{ enabled: false }`; after the in-flight round settles, wait gap + 3 s and assert no newer auto round appears.
  2. `MonkeyDuel_AutoBet_DuelsTheHouse` — enrolment on the duel type, `Choice = "rocket"`, stake 0.01; assert a `DuelKind.House` round with the player's `OrbitAutoPlaced` bet on `rocket` and a house word; settle (steer if needed); win → `WinCredit = 0.01 × PayoutMultiplier` lands in the Orbit ledger; a second duel opens after the gap.
  3. `SoloAutoBet_HouseRefuses_WaitsWithoutMovingMoney` — `SetHouseEdgeEnabled(false)` in try/finally; enable Hyper Roll auto; wait 3 s; assert no new auto bet, ledger unchanged, `GET /orbit` → `waitingForHouse == true`, `enabled == true`; re-enable the switch; poll for a placed auto bet.
  4. `HumanHyperRollBet_DoesNotPullOrbitPlayersIn` — player A Enabled on Cosmic Sprint, player B Enabled on Hyper Roll (each funded); a third (human) opens a Hyper Roll board and places a free/paid bet as other Hyper Roll tests do; after settlement assert the human's round has exactly one bet (the human's).

- [ ] **Step 2: Run** `cmd //c scripts\run_integration_tests.bat --filter "FullyQualifiedName~OrbitSoloAutoBetFlowTests"` (or via the script's arg forwarding) → fix until green.
- [ ] **Step 3: Run the WHOLE integration script** → green (watch for Orbit churn flakiness in unrelated suites; if a suite unrelated to Orbit fails, re-run it once to separate flake from regression and report).
- [ ] **Step 4: Commit** — `Integration tests for the Orbit solo auto-bet loop (#200).`

---

## Self-review notes

- Spec §6.1 → Task 2 (+ Task 3 for `ConsiderTheCurrentRound`, Task 6 for the deposit consumer). §6.2 → Task 3. §6.3–6.5, 6.8 → Tasks 1, 5. §6.6–6.7 → Task 6. §6.9 + §7.3 backend dependency → Task 4. §6.10 → Task 2. §5 config → Tasks 1 and 6. §11 → every task + Task 7.
- Spec corrections applied here: the duel ceiling is `GameParameters.MaximumStake` (same column as Hyper Roll); the commission comes from `settings.Operator.Token.CommissionPercentage`; the endpoint name is a `GameSettings` property, not a constant.
