# Orbit bet count & stake steps (#208) — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Orbit auto-bet a per-run **bet limit** on every auto-bet type (Cosmic Sprint, Monkey Duel VS HOUSE, Hyper Roll) and **on-win / on-loss stake steps** on the two solo types, with the run pausing itself and announcing `OrbitRunFinished` when the Nth bet settles.

**Architecture:** Seven additive `OrbitEnrollment` columns hold the run setup and run state. The stake placer takes a run **slot** with a conditional `ExecuteUpdate` inside its existing claim+debit transaction (the hard "never more than N" guarantee) and gives it back on a provably-unplaced stake. A new `OrbitRunService` (settlement side) writes the next stake and pauses a finished run, called by `RewardGameConsumer` **before** the stop rules and by `RefundInvalidBetConsumer`. The solo loop stakes `OrbitRunStake.Effective` (clamped) and refuses a full run before it opens a round; the shared planner filters full runs out. All new conditional SQL lives behind `IOrbitRunStore` so the unit suite can run against an in-memory fake (`InMemoryOrbitRunStore`) and the SQL is proven on Postgres.

**Tech Stack:** .NET 8, EF Core (Npgsql, NoTracking default), MassTransit, Hangfire, SignalR (`ActiveGamesHub`), xUnit + Shouldly + Moq + Bogus (in-memory EF), Testcontainers.

**Spec:** `monkify-meta/specs/new-options-for-orbit.md` (authoritative; this plan adapts it to the real code, see "Spec deviations"). **Worktree:** `E:\Monkify\.worktrees\new-options-for-orbit\monkify-backend` on `feature/new-options-for-orbit`. All paths below are relative to that worktree unless absolute.

---

## Spec deviations (read first)

1. **The outcome enum is `StakePlacementOutcome`** (in `IOrbitStakePlacer.cs`), not `StakeOutcome`. The new member is `StakePlacementOutcome.RunLimitReached`.
2. **The counter SQL sits behind a new `IOrbitRunStore`** (`OrbitRunStore` on Postgres, `InMemoryOrbitRunStore` test fake), not inline in `OrbitStakePlacer` / `OrbitRunService`. Reason: the EF in-memory provider cannot run `ExecuteUpdate`, and every existing `OrbitStakePlacer` unit test would otherwise throw. Same pattern as `IOrbitSoloLoopStore` / `IOrbitStakeClaimStore`. The SQL is the spec's SQL, word for word.
3. **The placer gives a slot back through `IOrbitRunStore.ReleaseSlot`, not `IOrbitRunService.ReleaseSlot`.** Keeps the money path free of the hub/claims dependencies of the run service. `IOrbitRunService.ReleaseSlot` exists and delegates to the store (used by `RefundInvalidBetConsumer`).
4. **`OrbitStakePlacer` gains a constructor parameter (`IOrbitRunStore runs`, last).** Five existing `new OrbitStakePlacer(...)` call sites in tests must be updated (Task 2 lists them).
5. **`claimedAt` for the placer's releases is the claim row's `CreatedDate`,** which `TableEntity`'s constructor stamps BEFORE the counter increment runs. `ClaimAndDebit` now returns it as a third tuple element. (Spec intent unchanged.)
6. **"At the limit ⇒ no debit row, balance untouched" cannot be asserted in the unit suite** (in-memory EF ignores the transaction rollback; the ledger is mocked). The unit test asserts no transfer / no register / no credit / counter unchanged; the rollback is proven against Postgres in `OrbitRunStoreSqlTests.Placer_AtTheLimit_RefusesTheClaim_AndRollsTheDebitBack` (Task 9).
7. **"ReleaseSlot never below 0 / never across runs" is a SQL predicate,** so it is tested in `OrbitRunStoreSqlTests` (Task 9), not `OrbitRunServiceTests` (an in-memory test would only test the fake). `OrbitRunServiceTests` covers the delegation.
8. **DI goes in `SpaceStroker.Infrastructure/DependencyInjection.cs`** (shared by both hosts), next to `IOrbitSoloAutoBetService` — not in Consumers `Program.cs`. The API never invokes the run service; registering it there keeps `ValidateOnBuild` green, exactly as the solo loop does.
9. **`ResolveCandidates` filters at the source** (`.Where(enrollment => enrollment.Enabled && ... && (enrollment.BetLimit == null || enrollment.RunBetsPlaced < enrollment.BetLimit))`) instead of projecting the two columns through both joins. Same SQL effect, fewer edits.
10. **`EvaluateZeroOut` already returns early for `!Enabled`** (`OrbitBetSettlementService.cs:227`), so no guard is added. Only the effective-stake expression changes (to `OrbitRunStake.Effective`, which needs the full `GameParameters` row, so the 3-column projection becomes an `AsNoTracking` entity load). A test pins the "finished run says nothing" behaviour.
11. **Solo loop `RunLimitReached`:** the lease was already dropped by `SetRound` before placement (`OrbitSoloAutoBetService.cs:246`), so "release the lease" needs no extra call; the round is handed back with `_store.ClearRound(playerId, roundId, null, ct)`.
12. **`ApplySettledRound` also requires `enrollment.GameParametersId == game.ParametersId`** before writing a step (spec silent). A bet on a type the enrolment has since left must not set the new type's next stake. The `RunStartedAt <= bet.CreatedDate` guard already covers this after a restart; this covers the paused-switch window.
13. **`RewardGameConsumer` resolves `IOrbitRunService` lazily inside try/catch, only when the round has an `OrbitAutoPlaced` bet** (same shape as `ReleaseOrbitSoloLoop`), so `Background/RewardWinnersHandlerTests`' service-provider mock needs no change and an unresolvable service never fails settlement.
14. **Run reset on START forces `IsModified` on `RunBetsPlaced` / `RunNextStake`.** A late `SetNextStake` / `ReleaseSlot` from the previous run can `ExecuteUpdate` those columns between the handler's load and its save; EF writes only properties it sees as changed, so without this a stale next stake could leak into the new run.
15. **`EvaluateRunEnd` keeps going after a failed claim release.** `IOrbitChoiceClaimService.Release` failing is logged at Error, and the push is still attempted (the pause has already committed). The push itself is wrapped like `OrbitSoloAutoBetService.Push` (Warning).
16. **The Cosmic Sprint integration test seeds the enrolment PAUSED and PATCHes `{ betLimit: 2 }` then `{ enabled: true }` before opening the first round** (so the planner sees an enabled, reset run). `OrbitAutoBetFlowTests` has no PATCH/session helpers, so the test adds local ones (the file deliberately duplicates helpers rather than sharing them).
17. **`AssertLedgerForAsync` in `OrbitSoloAutoBetFlowTests` gains an optional `decimal? stake = null`,** because it hard-codes `-Stake` for the debit and the stepped bets stake more.

---

## Global Constraints

- Work only in `E:\Monkify\.worktrees\new-options-for-orbit\monkify-backend`; never touch `E:\Monkify\monkify-backend` (the `main` hub).
- **Unit tests:** whole suite = `cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend && cmd //c "scripts\run_tests.bat"`. `run_tests.bat` already passes its own `--filter` (it excludes `ProductionGames`), so do NOT add a second `--filter` to it. Single class = `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~<ClassName>"`. NEVER run a bare `dotnet test` on the solution (the `ProductionSessions` Monte-Carlo suite).
- **Integration tests:** whole suite = `cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend && cmd //c "scripts\run_integration_tests.bat"` (Docker running). Single class = `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests.Integration --filter "FullyQualifiedName~<ClassName>"`.
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`. Migration **generated** with `dotnet ef migrations add AddOrbitRunLimits -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers` from `src`. Never hand-write or hand-edit the snapshot. Additive only. Applied only by Consumers.
- The DbContext is **NoTracking by default**: every consumer/service write is a conditional `ExecuteUpdateAsync` (behind `IOrbitRunStore`). The one tracked write is `UpdateOrbitSettingsHandler`, which already loads `.AsTracking()`.
- **Orbit is live with real money.** This card writes NO Orbit ledger row of its own; `OrbitBalance.Amount == SUM(OrbitTransaction.Amount)` must keep holding in every integration test.
- Every new settings refusal is `ErrorMessages.OrbitRequestInvalid` (vague).
- Bulk `sed -i` over `.cs` flips CRLF to LF repo-wide: edit with the Edit tool only. After generating the migration, `git status` must show only the files this task touched.
- Commit after every task: `git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add <files>` then `git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "<message>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"`. **Do not push** (the controller pushes).
- **Exact values from the spec:** bet limit `1..10_000` (`OrbitRunRules.MaxBetLimit = 10_000`), `0`/empty = no limit; raise % `0..1000` (`MaxIncreasePercent = 1000m`), at most 2 decimals (`IncreasePercentDecimals = 2`); next stake truncated to 9 dp (`NativeSol.Decimals`, `MidpointRounding.ToZero`) and clamped to `[RequiredAmount, MaximumStake]`; config `"OrbitRunFinishedEndpoint": "OrbitRunFinished"`; push payload `OrbitRunFinishedPush(Guid GameTypeId, string GameTypeName, int BetsPlaced)` (camelCase JSON `{ gameTypeId, gameTypeName, betsPlaced }`); no Telegram.
- **Frontend contract (exact names, camelCase JSON):**
  - `GET /orbit` adds `betLimit` (int|null), `stakeStepsEnabled` (bool), `onWinIncreasePercent` (decimal|null), `onLossIncreasePercent` (decimal|null), `runBetsPlaced` (int), `runFinished` (bool), `nextStake` (decimal|null).
  - `PATCH /orbit/settings` adds `betLimit`, `clearBetLimit`, `stakeStepsEnabled`, `onWinIncreasePercent`, `clearOnWinIncrease`, `onLossIncreasePercent`, `clearOnLossIncrease`.
  - Realtime event `"OrbitRunFinished"` (`GameSettings.OrbitRunFinishedEndpoint`) with `{ gameTypeId, gameTypeName, betsPlaced }`.

## Facts from the codebase (verified on the worktree, = main)

- `OrbitEnrollment` — `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs`; solo setup block ends at l.127 (`WaitingForHouseSince`).
- `GameParameters` (`SpaceStroker.Domain.Games.Entities`): `RequiredAmount` (decimal), `MaximumStake` (decimal?), `IsHouseEdge`, `IsDuel`, `Name`. `OrbitSoloTypes.IsSolo(p) => p.IsHouseEdge || p.IsDuel`.
- `NativeSol.Decimals = 9` (`SpaceStroker.Domain.Games.ValueObjects`).
- `BetStatus` (`SpaceStroker.Domain.Bets.ValueObjects`): `Made, NeedsRewarding, Rewarded, NeedsRefunding, Refunded, NeedsManualAnalysis, Voided`. No Lost status: a lost bet stays `Made`.
- `HouseEdgeCoverage.UnsettledStatuses` (`SpaceStroker.Domain.Games.Services`) = `WaitingBets, GameStarting, InProgress, Ended, RewardForWinnersInProgress`.
- `OrbitStakePlacer.ClaimAndDebit` l.182-221 returns `(StakePlacementOutcome, Guid ClaimId)`; `Place` l.70-172; `TransferFailed` returned from `MakeWholeUnsentStake` at l.98 and l.115; `RegistrationRefused` at l.160.
- `OrbitSoloAutoBetService.RunOnce` l.70-336: stake at l.131, `StakeIsWithinBounds` l.132-137, lease l.156, `SetRound` (drops the lease) l.246, `Place` l.287-289, outcome switch l.293-327; `Push` l.494-513 (pattern for the new push).
- `RewardGameConsumer`: no-winners exit `EvaluateOrbitFloors` l.77 / `ReleaseOrbitSoloLoop` l.78; normal exit l.267 / l.268.
- `RefundInvalidBetConsumer`: `OrbitAutoPlaced` hook l.111-121.
- `UpdateOrbitSettingsHandler`: lock l.98-100, `ApplySoloSetup` call l.161, enable block l.176-213, `SaveChangesAsync` l.336, `ChangesSoloSetup` l.356-361.
- `GetOrbitHandler`: DTO built l.131-189, `StreamerView` l.197-229.
- `OrbitBetPlacementService.ResolveCandidates` l.41-104 (enrolment source `Where` at l.68).
- `PlaceOrbitBetConsumer` outcome switch l.177-207.
- `OrbitBetSettlementService.EvaluateZeroOut` l.222-289 (projection l.230-242).
- `GameSettings.OrbitAutoRoundEndpoint` l.97; `spacestrokersettings.json` l.166.
- DbContext `OrbitEnrollment` block l.412-442 (`Multiplier` mapping l.421).
- Latest migration `20261007033746_AddStreamerWallets`.
- `AsJson()` (`SpaceStroker.Domain.Configs.ValueObjects`) serializes camelCase.
- Test bases: `UnitTestsClass` (in-memory DB per instance, transaction warnings ignored), `OrbitHandlerTestsBase`, `InMemoryOrbitStakeClaimStore` (pattern for the new fake).
- Placer constructions to update: `UnitTests/Services/OrbitStakePlacerTests.cs:665`, `UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs:529`, `UnitTests/Jobs/OrbitStakeReconciliationJobTests.cs:742`, `SpaceStroker.Tests.Integration/OrbitStakeCompensationTests.cs:83`, `SpaceStroker.Tests.Integration/OrbitStakeReconciliationTests.cs:383`.

## File structure

| File | Responsibility | Task |
| --- | --- | --- |
| `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs` | 7 new properties | 1 |
| `src/SpaceStroker.Domain/Orbit/Services/OrbitRunStake.cs` *(new)* | `Effective`, `Next`, `Clamp` (pure) | 1 |
| `src/SpaceStroker.Domain/Orbit/Services/OrbitRunRules.cs` *(new)* | `ReachedLimit`, `IsValidIncreasePercent`, bounds | 1 |
| `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` | `OrbitRunFinishedEndpoint` | 1 |
| `src/SpaceStroker.Common/spacestrokersettings.json` | `"OrbitRunFinishedEndpoint": "OrbitRunFinished"` | 1 |
| `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` | inline mapping | 1 |
| `src/SpaceStroker.Infrastructure/Migrations/<ts>_AddOrbitRunLimits*.cs` + snapshot *(generated)* | additive migration | 1 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunStore.cs` + `OrbitRunStore.cs` *(new)* | the four conditional `ExecuteUpdate`s | 2 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitStakePlacer.cs` | `RunLimitReached` | 2 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitStakePlacer.cs` | slot take in `ClaimAndDebit`, slot give-back | 2 |
| `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` | `RunLimitReached` ⇒ skip | 2 |
| `src/SpaceStroker.Infrastructure/DependencyInjection.cs` | register store (2) and service (6) | 2, 6 |
| `src/SpaceStroker.Tests/UnitTests/Shared/InMemoryOrbitRunStore.cs` *(new)* | in-memory fake | 2 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` | limit filter | 3 |
| `.../Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsRequest.cs` + `Handler.cs` | PATCH fields, validation, lock, reset | 4 |
| `.../Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` + `GetOrbitHandler.cs` | DTO fields | 5 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunService.cs` + `OrbitRunService.cs` *(new)* | steps, run end, push, release | 6 |
| `.../Consumers/RewardGame/RewardGameConsumer.cs`, `.../Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`, `.../Services/Orbit/OrbitBetSettlementService.cs` | settlement wiring, zero-out stake | 7 |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSoloAutoBetService.cs` | effective stake, limit refusal, outcome | 8 |
| `src/SpaceStroker.Tests.Integration/OrbitRunStoreSqlTests.cs` *(new)* | the SQL on Postgres | 9 |
| `src/SpaceStroker.Tests.Integration/OrbitSoloAutoBetFlowTests.cs`, `OrbitAutoBetFlowTests.cs` | mandatory flows | 10 |

---

### Task 1: Domain helpers, entity columns, config, EF mapping, migration

**Files:**
- Create: `src/SpaceStroker.Domain/Orbit/Services/OrbitRunStake.cs`
- Create: `src/SpaceStroker.Domain/Orbit/Services/OrbitRunRules.cs`
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs` (after l.127)
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs` (after l.97)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (after l.166)
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (after l.421)
- Create (generated): `src/SpaceStroker.Infrastructure/Migrations/<timestamp>_AddOrbitRunLimits.cs`, `.Designer.cs`; modified `SpaceStrokerDbContextModelSnapshot.cs`
- Test: create `src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunStakeTests.cs`, `src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunRulesTests.cs`; modify `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`

**Interfaces — Produces:**
- `OrbitEnrollment.{ int? BetLimit; bool StakeStepsEnabled; decimal? OnWinIncreasePercent; decimal? OnLossIncreasePercent; DateTime? RunStartedAt; int RunBetsPlaced; decimal? RunNextStake }`
- `OrbitRunStake.Effective(OrbitEnrollment enrollment, GameParameters parameters) : decimal`
- `OrbitRunStake.Next(decimal settledStake, bool won, OrbitEnrollment enrollment, GameParameters parameters) : decimal`
- `OrbitRunStake.Clamp(decimal stake, GameParameters parameters) : decimal`
- `OrbitRunRules.MaxBetLimit (int 10_000)`, `MaxIncreasePercent (decimal 1000m)`, `IncreasePercentDecimals (int 2)`, `ReachedLimit(OrbitEnrollment) : bool`, `IsValidIncreasePercent(decimal) : bool`
- `GameSettings.OrbitRunFinishedEndpoint : string`

- [ ] **Step 1: Write the failing tests**

`src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunStakeTests.cs`:

```csharp
using Shouldly;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Domain.Orbit.Services;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Orbit;

/// <summary>
/// The stake a run's NEXT auto round uses (specs/new-options-for-orbit.md §6.1). Pure: the solo loop, zero-out,
/// GET /orbit and the settlement step all read the same two functions, so they cannot disagree about the number.
/// </summary>
public class OrbitRunStakeTests
{
    private static GameParameters Solo(decimal required = 0.01m, decimal? max = 1m)
        => new() { IsHouseEdge = true, RequiredAmount = required, MaximumStake = max };

    private static GameParameters Sprint() => new() { RequiredAmount = 1m };

    // ---- Effective ----

    [Fact]
    public void Effective_OnCosmicSprint_IsAlwaysTheRequiredAmount()
        => OrbitRunStake.Effective(new OrbitEnrollment { Stake = 0.5m, RunNextStake = 3m }, Sprint()).ShouldBe(1m);

    [Fact]
    public void Effective_OnASoloType_WithNothingSet_IsTheRequiredAmount()
        => OrbitRunStake.Effective(new OrbitEnrollment(), Solo()).ShouldBe(0.01m);

    [Fact]
    public void Effective_OnASoloType_WithNoStep_IsTheBaseStake()
        => OrbitRunStake.Effective(new OrbitEnrollment { Stake = 0.05m }, Solo()).ShouldBe(0.05m);

    [Fact]
    public void Effective_OnADuel_PrefersTheSteppedNextStake()
        => OrbitRunStake.Effective(new OrbitEnrollment { Stake = 0.05m, RunNextStake = 0.2m },
            new GameParameters { IsDuel = true, RequiredAmount = 0.01m, MaximumStake = 1m }).ShouldBe(0.2m);

    [Fact]
    public void Effective_IsClampedToTheMaximumStake()
        => OrbitRunStake.Effective(new OrbitEnrollment { Stake = 0.05m, RunNextStake = 5m }, Solo(max: 1m)).ShouldBe(1m);

    [Fact]
    public void Effective_IsClampedUpToTheRequiredAmount()
        => OrbitRunStake.Effective(new OrbitEnrollment { Stake = 0.001m }, Solo(required: 0.01m)).ShouldBe(0.01m);

    [Fact]
    public void Effective_WithNoCeiling_HasNoUpperClamp()
        => OrbitRunStake.Effective(new OrbitEnrollment { RunNextStake = 5m }, Solo(max: null)).ShouldBe(5m);

    // ---- Next ----

    [Fact]
    public void Next_TwoLossesAtPlus100_DoubleFromTheBase()
    {
        var enrollment = new OrbitEnrollment { Stake = 0.010m, OnLossIncreasePercent = 100m };
        var parameters = Solo();

        var second = OrbitRunStake.Next(0.010m, won: false, enrollment, parameters);
        var third = OrbitRunStake.Next(second, won: false, enrollment, parameters);

        second.ShouldBe(0.020m);
        third.ShouldBe(0.040m);
    }

    [Fact]
    public void Next_AWinWithBackToBase_ReturnsTheBaseStake()
        => OrbitRunStake.Next(0.04m, won: true, new OrbitEnrollment { Stake = 0.01m, OnLossIncreasePercent = 100m }, Solo())
            .ShouldBe(0.01m);

    [Fact]
    public void Next_BackToBase_WithNoStoredStake_IsTheRequiredAmount()
        => OrbitRunStake.Next(0.04m, won: true, new OrbitEnrollment(), Solo(required: 0.01m)).ShouldBe(0.01m);

    [Fact]
    public void Next_ZeroPercent_BehavesAsBackToBase()
        => OrbitRunStake.Next(0.04m, won: false, new OrbitEnrollment { Stake = 0.01m, OnLossIncreasePercent = 0m }, Solo())
            .ShouldBe(0.01m);

    [Fact]
    public void Next_RaisesTheSettledStake_NotTheBase()
        => OrbitRunStake.Next(0.03m, won: true, new OrbitEnrollment { Stake = 0.01m, OnWinIncreasePercent = 50m }, Solo())
            .ShouldBe(0.045m);

    [Fact]
    public void Next_TruncatesToLamports_NeverRoundsUp()
        // 0.000000003 × 1.9 = 0.0000000057 → 0.000000005 (rounding would give 0.000000006).
        => OrbitRunStake.Next(0.000000003m, won: false, new OrbitEnrollment { OnLossIncreasePercent = 90m },
            Solo(required: 0.000000001m, max: null)).ShouldBe(0.000000005m);

    [Fact]
    public void Next_ARaisePastTheMaximum_IsClampedToTheMaximum()
        => OrbitRunStake.Next(0.04m, won: false, new OrbitEnrollment { Stake = 0.01m, OnLossIncreasePercent = 100m }, Solo(max: 0.05m))
            .ShouldBe(0.05m);

    [Fact]
    public void Next_TwoDecimalPercent_IsApplied()
        => OrbitRunStake.Next(0.01m, won: false, new OrbitEnrollment { Stake = 0.01m, OnLossIncreasePercent = 12.5m }, Solo())
            .ShouldBe(0.01125m);
}
```

`src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunRulesTests.cs`:

```csharp
using System.Globalization;
using Shouldly;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Domain.Orbit.Services;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Domain.Orbit;

public class OrbitRunRulesTests
{
    [Theory]
    [InlineData(null, 99, false)] // no limit never ends a run
    [InlineData(0, 99, false)]    // 0 is "no limit" too (defensive: the PATCH stores it as null)
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    public void ReachedLimit(int? limit, int placed, bool expected)
        => OrbitRunRules.ReachedLimit(new OrbitEnrollment { BetLimit = limit, RunBetsPlaced = placed }).ShouldBe(expected);

    [Theory]
    [InlineData("0", true)]
    [InlineData("1000", true)]
    [InlineData("12.34", true)]
    [InlineData("1000.01", false)]
    [InlineData("-1", false)]
    [InlineData("12.345", false)]
    public void IsValidIncreasePercent(string raw, bool expected)
        => OrbitRunRules.IsValidIncreasePercent(decimal.Parse(raw, CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void Bounds_AreTheSpecsNumbers()
    {
        OrbitRunRules.MaxBetLimit.ShouldBe(10_000);
        OrbitRunRules.MaxIncreasePercent.ShouldBe(1000m);
        OrbitRunRules.IncreasePercentDecimals.ShouldBe(2);
    }
}
```

`OrbitSettingsBindingTests.cs` — add next to `OrbitAutoRoundEndpoint_BindsToItsHubMethodName` (l.190-201):

```csharp
    [Fact]
    public void OrbitRunFinishedEndpoint_BindsToItsHubMethodName()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "spacestrokersettings.json"), optional: false)
            .Build();

        var settings = new GeneralSettings();
        configuration.GetSection("GeneralSettings").Bind(settings);

        settings.Games.OrbitRunFinishedEndpoint.ShouldBe("OrbitRunFinished");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&(FullyQualifiedName~OrbitRunStakeTests|FullyQualifiedName~OrbitRunRulesTests|FullyQualifiedName~OrbitSettingsBindingTests)"`
Expected: build FAILS (`OrbitRunStake`, `OrbitRunRules`, `BetLimit`, `OrbitRunFinishedEndpoint` do not exist).

- [ ] **Step 3: Implement**

`OrbitEnrollment.cs` — insert after l.127 (`public DateTime? WaitingForHouseSince { get; set; }`), inside the class:

```csharp

        // ---- Run limits & stake steps (specs/new-options-for-orbit.md §4) ----
        /// <summary>Auto bets per run (one START). Null = no limit. Any auto-bet type.</summary>
        public int? BetLimit { get; set; }
        /// <summary>Apply the on-win / on-loss steps. Solo types only; ignored on Cosmic Sprint.</summary>
        public bool StakeStepsEnabled { get; set; }
        /// <summary>After a WIN: null = back to base, else raise the previous stake by this % (0–1000, 2 dp).</summary>
        public decimal? OnWinIncreasePercent { get; set; }
        /// <summary>After a LOSS: null = back to base, else raise the previous stake by this % (0–1000, 2 dp).</summary>
        public decimal? OnLossIncreasePercent { get; set; }

        // ---- Run state. WARNING: written with ExecuteUpdate (IOrbitRunStore) outside UpdateOrbitSettingsHandler. ----
        /// <summary>When the current run began (the last paused → enabled). Stale-run guard for decrements/steps.</summary>
        public DateTime? RunStartedAt { get; set; }
        /// <summary>Auto bets claimed in the current run, net of refunds / failed placements.</summary>
        public int RunBetsPlaced { get; set; }
        /// <summary>The stake the next solo auto round uses. Null = base. Never read on Cosmic Sprint.</summary>
        public decimal? RunNextStake { get; set; }
```

`src/SpaceStroker.Domain/Orbit/Services/OrbitRunStake.cs`:

```csharp
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Domain.Orbit.Entities;

namespace SpaceStroker.Domain.Orbit.Services
{
    /// <summary>
    /// The stake of a run's next auto round (specs/new-options-for-orbit.md §6.1).
    ///
    /// ⚠️ Always CLAMPED to [RequiredAmount, MaximumStake]. The solo loop's StakeIsWithinBounds silently idles the loop
    /// (log only, no reschedule, no push) for a stake above MaximumStake — a raw compounded stake would hit exactly that.
    /// A null ceiling is not clamped here; the loop's bounds check still fails it closed.
    /// </summary>
    public static class OrbitRunStake
    {
        /// <summary>Cosmic Sprint (non-solo): always RequiredAmount. Solo: RunNextStake ?? Stake ?? RequiredAmount, clamped.</summary>
        public static decimal Effective(OrbitEnrollment enrollment, GameParameters parameters)
            => !OrbitSoloTypes.IsSolo(parameters)
                ? parameters.RequiredAmount
                : Clamp(enrollment.RunNextStake ?? enrollment.Stake ?? parameters.RequiredAmount, parameters);

        /// <summary>
        /// After a settled solo auto bet: back to base (null or 0 %) or raise the bet's OWN stake by the %. Truncated to
        /// lamports (never rounded up), then clamped.
        /// </summary>
        public static decimal Next(decimal settledStake, bool won, OrbitEnrollment enrollment, GameParameters parameters)
        {
            var basis = enrollment.Stake ?? parameters.RequiredAmount;
            var percent = won ? enrollment.OnWinIncreasePercent : enrollment.OnLossIncreasePercent;

            if (percent is null or 0m)
                return Clamp(basis, parameters);

            var raised = settledStake * (1m + percent.Value / 100m);
            return Clamp(decimal.Round(raised, NativeSol.Decimals, MidpointRounding.ToZero), parameters);
        }

        public static decimal Clamp(decimal stake, GameParameters parameters)
            => parameters.MaximumStake is { } max
                ? Math.Min(Math.Max(stake, parameters.RequiredAmount), max)
                : Math.Max(stake, parameters.RequiredAmount);
    }
}
```

`src/SpaceStroker.Domain/Orbit/Services/OrbitRunRules.cs`:

```csharp
using SpaceStroker.Domain.Orbit.Entities;

namespace SpaceStroker.Domain.Orbit.Services
{
    /// <summary>The bet-count half of a run (specs/new-options-for-orbit.md §5, §6.1).</summary>
    public static class OrbitRunRules
    {
        /// <summary>Sanity cap on a run's bet limit; above it the PATCH is refused.</summary>
        public const int MaxBetLimit = 10_000;
        public const decimal MaxIncreasePercent = 1000m;
        public const int IncreasePercentDecimals = 2;

        /// <summary>Null or 0 limit never ends a run (today's behaviour).</summary>
        public static bool ReachedLimit(OrbitEnrollment enrollment)
            => enrollment.BetLimit is > 0 && enrollment.RunBetsPlaced >= enrollment.BetLimit;

        /// <summary>0–1000, at most 2 decimals. No negative % (no decrease steps).</summary>
        public static bool IsValidIncreasePercent(decimal percent)
            => percent >= 0m && percent <= MaxIncreasePercent
               && decimal.Round(percent, IncreasePercentDecimals) == percent;
    }
}
```

`GameSettings.cs` — insert after l.97 (`public string OrbitAutoRoundEndpoint { get; set; }`):

```csharp

        /// <summary>
        /// Private-channel endpoint for "your Orbit run placed its N bets and paused" (specs/new-options-for-orbit.md §6.7).
        /// Same group as OrbitNotificationEndpoint. In-app only — never sent to Telegram.
        /// </summary>
        public string OrbitRunFinishedEndpoint { get; set; }
```

`spacestrokersettings.json` — after l.166 (`"OrbitAutoRoundEndpoint": "OrbitAutoRound",`):

```json
            "OrbitRunFinishedEndpoint": "OrbitRunFinished",
```

`SpaceStrokerDbContext.cs` — inside `modelBuilder.Entity<OrbitEnrollment>`, after l.421 (`builder.Property(x => x.Multiplier).HasPrecision(9, 2);`):

```csharp
                // Run limits & stake steps (specs/new-options-for-orbit.md §4.2). BetLimit / RunStartedAt need no mapping.
                builder.Property(x => x.StakeStepsEnabled).IsRequired().HasDefaultValue(false);
                builder.Property(x => x.OnWinIncreasePercent).HasPrecision(6, 2);
                builder.Property(x => x.OnLossIncreasePercent).HasPrecision(6, 2);
                builder.Property(x => x.RunBetsPlaced).IsRequired().HasDefaultValue(0);
                builder.Property(x => x.RunNextStake).HasPrecision(18, 9);
```

- [ ] **Step 4: Generate the migration**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src
dotnet ef migrations add AddOrbitRunLimits -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend status --short
```

Expected: `<timestamp>_AddOrbitRunLimits.cs` adds exactly 7 columns to `OrbitEnrollments` — `BetLimit integer NULL`, `StakeStepsEnabled boolean NOT NULL DEFAULT FALSE`, `OnWinIncreasePercent numeric(6,2) NULL`, `OnLossIncreasePercent numeric(6,2) NULL`, `RunStartedAt timestamp with time zone NULL`, `RunBetsPlaced integer NOT NULL DEFAULT 0`, `RunNextStake numeric(18,9) NULL` — no index, no other table; `Down` drops the same 7. The timestamp orders after `20261007033746_AddStreamerWallets`. `git status` lists only the files of this task. If the migration contains anything else, stop and report.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&(FullyQualifiedName~OrbitRunStakeTests|FullyQualifiedName~OrbitRunRulesTests|FullyQualifiedName~OrbitSettingsBindingTests)"`
Expected: PASS. Then the whole unit script: `cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend && cmd //c "scripts\run_tests.bat"` → green.

- [ ] **Step 6: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs src/SpaceStroker.Domain/Orbit/Services/OrbitRunStake.cs src/SpaceStroker.Domain/Orbit/Services/OrbitRunRules.cs src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs src/SpaceStroker.Infrastructure/Migrations src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunStakeTests.cs src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitRunRulesTests.cs src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: enrolment columns, OrbitRunStake/OrbitRunRules, OrbitRunFinished endpoint and the AddOrbitRunLimits migration (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: The run slot — `IOrbitRunStore`, the claim's conditional increment, give-backs, `RunLimitReached`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunStore.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunStore.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitStakePlacer.cs` (enum, l.5-24)
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitStakePlacer.cs` (ctor l.44-68, `Place` l.70-172, `ClaimAndDebit` l.182-221)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` (switch l.177-207)
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (after l.91 `IOrbitStakeClaimStore`)
- Create: `src/SpaceStroker.Tests/UnitTests/Shared/InMemoryOrbitRunStore.cs`
- Modify (constructor call sites): `src/SpaceStroker.Tests/UnitTests/Services/OrbitStakePlacerTests.cs:654-668`, `src/SpaceStroker.Tests/UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs:529-532`, `src/SpaceStroker.Tests/UnitTests/Jobs/OrbitStakeReconciliationJobTests.cs:742-743`, `src/SpaceStroker.Tests.Integration/OrbitStakeCompensationTests.cs:83-93`, `src/SpaceStroker.Tests.Integration/OrbitStakeReconciliationTests.cs:383-390`
- Test: `OrbitStakePlacerTests.cs`, `PlaceOrbitBetConsumerTests.cs`

**Interfaces — Consumes:** `OrbitEnrollment.{BetLimit, RunBetsPlaced, RunStartedAt, RunNextStake, Enabled}` (Task 1).
**Interfaces — Produces:**

```csharp
public interface IOrbitRunStore
{
    Task<bool> TryTakeSlot(Guid playerId, CancellationToken cancellationToken = default);
    Task<bool> ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default);
    Task<bool> SetNextStake(Guid playerId, decimal nextStake, DateTime betCreatedAt, CancellationToken cancellationToken = default);
    Task<bool> PauseFinishedRun(Guid playerId, DateTime? runStartedAt, DateTime now, CancellationToken cancellationToken = default);
}
```
- `StakePlacementOutcome.RunLimitReached`
- `OrbitStakePlacer(..., IOrbitStakeClaimStore claims, IOrbitRunStore runs)`
- Test fake `InMemoryOrbitRunStore(SpaceStrokerDbContext)` with `Func<Guid, Task>? BeforePause`.

- [ ] **Step 1: Create the store interface, the fake and the enum member (so the tests compile), then write the failing tests**

`src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunStore.cs`:

```csharp
namespace SpaceStroker.Infrastructure.Services.Orbit
{
    /// <summary>
    /// Every run-state write on <c>OrbitEnrollment</c> (specs/new-options-for-orbit.md §6.3, §6.6). Each is ONE conditional
    /// <c>ExecuteUpdate</c>: the context is NoTracking by default, and "may this bet count" has to be the database's decision.
    ///
    /// Behind an interface because EF's in-memory provider cannot execute ExecuteUpdate — the callers are unit-tested
    /// against InMemoryOrbitRunStore, the SQL itself in OrbitRunStoreSqlTests. None of these touch money.
    /// </summary>
    public interface IOrbitRunStore
    {
        /// <summary>
        /// <c>RunBetsPlaced + 1 WHERE PlayerId AND Enabled AND (BetLimit IS NULL OR RunBetsPlaced &lt; BetLimit)</c>.
        /// False = the run is full or Orbit is paused: the caller must not place. Runs inside the placer's claim
        /// transaction, so a rollback undoes it.
        /// </summary>
        Task<bool> TryTakeSlot(Guid playerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>RunBetsPlaced - 1 WHERE PlayerId AND RunBetsPlaced &gt; 0 AND RunStartedAt &lt;= claimedAt</c>: never below
        /// zero, and never into a run that started after the slot was taken.
        /// </summary>
        Task<bool> ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default);

        /// <summary><c>RunNextStake = nextStake WHERE PlayerId AND RunStartedAt &lt;= betCreatedAt</c> (current run only).</summary>
        Task<bool> SetNextStake(Guid playerId, decimal nextStake, DateTime betCreatedAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>Enabled = false, WaitingForHouseSince = NULL, UpdatedDate = now WHERE PlayerId AND Enabled AND
        /// RunStartedAt = runStartedAt</c> — a stale evaluation never pauses a run the player just restarted.
        /// </summary>
        Task<bool> PauseFinishedRun(Guid playerId, DateTime? runStartedAt, DateTime now, CancellationToken cancellationToken = default);
    }
}
```

`src/SpaceStroker.Tests/UnitTests/Shared/InMemoryOrbitRunStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Orbit;

namespace SpaceStroker.Tests.UnitTests.Shared;

/// <summary>
/// <see cref="OrbitRunStore"/>'s conditional semantics over the in-memory provider, where ExecuteUpdate does not run:
/// the same WHERE, as a tracked load-mutate-save. Not atomic and not rolled back by a transaction — the SQL and the
/// rollback are proven against Postgres in OrbitRunStoreSqlTests; this lets the unit suite exercise the logic around it.
/// </summary>
public sealed class InMemoryOrbitRunStore : IOrbitRunStore
{
    private readonly SpaceStrokerDbContext _context;

    public InMemoryOrbitRunStore(SpaceStrokerDbContext context)
    {
        _context = context;
    }

    /// <summary>Runs before every PauseFinishedRun — how a test slips a restart in between the read and the pause.</summary>
    public Func<Guid, Task>? BeforePause { get; set; }

    public Task<bool> TryTakeSlot(Guid playerId, CancellationToken cancellationToken = default)
        => Update(playerId, x => x.Enabled && (x.BetLimit == null || x.RunBetsPlaced < x.BetLimit), x => x.RunBetsPlaced++);

    public Task<bool> ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default)
        => Update(playerId, x => x.RunBetsPlaced > 0 && x.RunStartedAt <= claimedAt, x => x.RunBetsPlaced--);

    public Task<bool> SetNextStake(Guid playerId, decimal nextStake, DateTime betCreatedAt, CancellationToken cancellationToken = default)
        => Update(playerId, x => x.RunStartedAt <= betCreatedAt, x => x.RunNextStake = nextStake);

    public async Task<bool> PauseFinishedRun(Guid playerId, DateTime? runStartedAt, DateTime now, CancellationToken cancellationToken = default)
    {
        if (BeforePause is { } before)
            await before(playerId);

        return await Update(playerId, x => x.Enabled && x.RunStartedAt == runStartedAt, x =>
        {
            x.Enabled = false;
            x.WaitingForHouseSince = null;
            x.UpdatedDate = now;
        });
    }

    private async Task<bool> Update(Guid playerId, Func<OrbitEnrollment, bool> where, Action<OrbitEnrollment> apply)
    {
        var row = await _context.OrbitEnrollments.AsTracking().FirstOrDefaultAsync(x => x.PlayerId == playerId);
        var won = row != null && where(row);

        if (won)
        {
            apply(row!);
            await _context.SaveChangesAsync();
        }

        if (row != null)
            _context.Entry(row).State = EntityState.Detached;

        return won;
    }
}
```

`IOrbitStakePlacer.cs` — add the member after `InDoubt` (l.23):

```csharp
        /// <summary>
        /// The run's bet limit is reached, or Orbit was paused since the caller looked (specs/new-options-for-orbit.md §6.3).
        /// Like InsufficientFunds: the claim rolled back — no debit, no transfer, no bet. AUTO placements only.
        /// </summary>
        RunLimitReached,
```

Now the failing tests. In `OrbitStakePlacerTests.cs`:

(a) Replace the fixture `Seed` (l.688-702) so every player has an ENABLED, unlimited enrolment (auto claims now need one), with an optional `configure`:

```csharp
    private (Guid GameId, Player Player) Seed(SpaceStrokerDbContext context, Action<OrbitEnrollment>? configure = null)
    {
        var player = new Player
        {
            ExternalPlayerId = Faker.Random.String2(40),
            LastSeenAt = DateTime.UtcNow,
        };

        // An auto claim takes a run slot (specs/new-options-for-orbit.md §6.3), which needs an ENABLED enrolment —
        // exactly as a live auto bettor has. No limit, run started an hour ago.
        var enrollment = new OrbitEnrollment
        {
            PlayerId = player.Id,
            Enabled = true,
            RunStartedAt = DateTime.UtcNow.AddHours(-1),
        };
        configure?.Invoke(enrollment);

        context.Add(player);
        context.Add(enrollment);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return (Guid.NewGuid(), player);
    }
```

(b) Replace `Placer(...)` (l.654-668) — pass the fake:

```csharp
    private OrbitStakePlacer Placer(SpaceStrokerDbContext context)
    {
        var settings = new GeneralSettings();
        settings.Operator.Token.MintAddress = Mint;
        settings.Operator.Token.TokenOwnerPublicKey = "GameTokenOwner";
        settings.Operator.Token.Decimals = 6;
        settings.Operator.Token.Orbit.MinimumSolBalance = 0.02m;

        _currentContext = context;
        _claims = new InMemoryOrbitStakeClaimStore(context);

        return new OrbitStakePlacer(context, _ledger.Object, _accounts.Object, _solana.Object, _mediator.Object,
            _bus.Object, Messaging, settings, _claims, new InMemoryOrbitRunStore(context));
    }
```

(c) Add these tests before `// ---- fixture ----`:

```csharp
    // ---- the run's bet limit (specs/new-options-for-orbit.md §6.3) ----

    private static OrbitEnrollment Enrollment(SpaceStrokerDbContext context, Guid playerId)
        => context.OrbitEnrollments.AsNoTracking().Single(x => x.PlayerId == playerId);

    [Fact]
    public async Task Place_Auto_TakesOneRunSlot()
    {
        using var context = NewContext();
        var (gameId, player) = Seed(context);
        RegistersTheBet(context);

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.Placed);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(1);
    }

    /// <summary>
    /// Never more than N: the claim refuses the (N+1)th auto bet. Nothing is sent or registered and the counter stays.
    /// The BetDebit row and the TryDebit are undone by the transaction ROLLBACK, which in-memory EF ignores — that half
    /// is proven on Postgres by OrbitRunStoreSqlTests.Placer_AtTheLimit_RefusesTheClaim_AndRollsTheDebitBack.
    /// </summary>
    [Fact]
    public async Task Place_Auto_AtTheLimit_IsRunLimitReached_AndMovesNothing()
    {
        using var context = NewContext();
        var (gameId, player) = Seed(context, e => { e.BetLimit = 2; e.RunBetsPlaced = 2; });
        RegistersTheBet(context);

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.RunLimitReached);
        _transferCount.ShouldBe(0);
        _requests.ShouldBeEmpty();
        _ledger.Verify(x => x.Credit(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(2);
    }

    /// <summary>The claim carries Enabled: a placement racing a pause (manual or run-end) is refused here.</summary>
    [Fact]
    public async Task Place_Auto_ForAPausedEnrolment_IsRunLimitReached()
    {
        using var context = NewContext();
        var (gameId, player) = Seed(context, e => e.Enabled = false);
        RegistersTheBet(context);

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.RunLimitReached);
        _transferCount.ShouldBe(0);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(0);
    }

    /// <summary>Manual Orbit wagers never count toward a run, even at the limit.</summary>
    [Fact]
    public async Task Place_Manual_NeverTouchesTheRunCounter()
    {
        using var context = NewContext();
        var (gameId, player) = Seed(context, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });
        RegistersTheBet(context);

        var result = await Placer(context).Place(Manual(gameId, player, Guid.NewGuid()));

        result.Outcome.ShouldBe(StakePlacementOutcome.Placed);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(1);
    }

    [Fact]
    public async Task Place_Auto_TransferDidNotLand_GivesTheSlotBack()
    {
        _nextSend = StakeTransferStatus.NotSent;

        using var context = NewContext();
        var (gameId, player) = Seed(context);

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.TransferFailed);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(0);
    }

    [Fact]
    public async Task Place_Auto_RegistrationRefused_GivesTheSlotBack()
    {
        using var context = NewContext();
        var (gameId, player) = Seed(context);
        _mediator.Setup(x => x.Send(It.IsAny<RegisterBetRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.RegistrationRefused);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(0);
    }

    /// <summary>InDoubt keeps its slot: the stake may be live.</summary>
    [Fact]
    public async Task Place_Auto_InDoubt_KeepsTheSlot()
    {
        _nextSend = StakeTransferStatus.Unknown;

        using var context = NewContext();
        var (gameId, player) = Seed(context);
        RegistersTheBet(context);

        var result = await Placer(context).Place(Auto(gameId, player));

        result.Outcome.ShouldBe(StakePlacementOutcome.InDoubt);
        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(1);
    }

    /// <summary>A slot taken in an EARLIER run is never given back into the current one.</summary>
    [Fact]
    public async Task Place_Auto_TransferDidNotLand_AfterARestart_DoesNotDecrementTheNewRun()
    {
        _nextSend = StakeTransferStatus.NotSent;

        using var context = NewContext();
        // The run "started" in the future relative to the claim: the claim belongs to the previous run.
        var (gameId, player) = Seed(context, e => { e.RunStartedAt = DateTime.UtcNow.AddMinutes(5); e.RunBetsPlaced = 1; });

        await Placer(context).Place(Auto(gameId, player));

        Enrollment(context, player.Id).RunBetsPlaced.ShouldBe(2, "the claim took a slot; its give-back is refused across runs.");
    }
```

In `PlaceOrbitBetConsumerTests.cs`:

(a) In `ScopeFactory` (l.529-532) add the fake as the last constructor argument:

```csharp
        provider.Setup(x => x.GetService(typeof(IOrbitStakePlacer))).Returns(new OrbitStakePlacer(
            context, _ledger.Object, _accounts.Object, _solana.Object, _mediator.Object,
            new Mock<SpaceStroker.Infrastructure.Services.Messaging.IMessageBus>().Object, Messaging, settings,
            new InMemoryOrbitStakeClaimStore(context), new InMemoryOrbitRunStore(context)));
```

(b) Add tests (above `private void VerifyNothingReplanned()`):

```csharp
    // ---- run limits (specs/new-options-for-orbit.md §6.4) ----

    private void VerifyNoStakeSent()
        => _solana.Verify(x => x.TransferStakeWithMemo(It.IsAny<string>(), It.IsAny<BetTransactionAmountResult>(),
                It.IsAny<SolanaTokenDescriptor>(), It.IsAny<string>(), It.IsAny<Func<string, long, Task<bool>>>()),
            Times.Never);

    /// <summary>
    /// A full run is an EXPECTED skip — released from the gate like any skip, and never re-planned into (a failed
    /// placement must not be re-planned, features/orbit.md §2).
    /// </summary>
    [Fact]
    public async Task RunLimitReached_IsAnExpectedSkip_AndIsNeverReplanned()
    {
        _ledger.Setup(x => x.TryDebit(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var context = NewContext();
        var (game, player) = Seed(context);
        context.Add(new OrbitEnrollment
        {
            PlayerId = player.Id, Enabled = true, GameParametersId = game.Parameters.Id, AutoPickChoice = true,
            BetLimit = 2, RunBetsPlaced = 2,
        });
        context.SaveChanges();

        await Consumer(context).ProcessMessage(new PlaceOrbitBetEvent(game.Id, player.Id));

        VerifyNoStakeSent();
        VerifySkipped(game.Id, player.Id);
        VerifyNothingReplanned();
    }

    /// <summary>The consumer never re-checks Enabled; the claim does — a placement racing a pause is skipped.</summary>
    [Fact]
    public async Task APlacementRacingAPause_IsRefusedAtTheClaim()
    {
        _ledger.Setup(x => x.TryDebit(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var context = NewContext();
        var (game, player) = Seed(context);
        Enroll(context, player.Id, game.Parameters.Id, enabled: false);

        await Consumer(context).ProcessMessage(new PlaceOrbitBetEvent(game.Id, player.Id));

        VerifyNoStakeSent();
        VerifySkipped(game.Id, player.Id);
    }
```

In `OrbitStakeReconciliationJobTests.cs` l.742-743, pass the fake last:

```csharp
        var placer = new OrbitStakePlacer(context, _ledger.Object, _accounts.Object, _solana.Object, new Mock<IMediator>().Object,
            _bus.Object, Messaging, settings, claims, new InMemoryOrbitRunStore(context));
```

In `SpaceStroker.Tests.Integration/OrbitStakeCompensationTests.cs` `Placer(...)` (l.80-93) add the last argument `services.GetRequiredService<IOrbitRunStore>()`; in `OrbitStakeReconciliationTests.cs` `PlaceWithAStall` (l.383-390) add `services.GetRequiredService<IOrbitRunStore>()` after `services.GetRequiredService<IOrbitStakeClaimStore>()`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&(FullyQualifiedName~OrbitStakePlacerTests|FullyQualifiedName~PlaceOrbitBetConsumerTests|FullyQualifiedName~OrbitStakeReconciliationJobTests)"`
Expected: build FAILS — `OrbitStakePlacer` has no constructor taking 10 arguments.

- [ ] **Step 3: Implement**

`src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Infrastructure.Data.Context;

namespace SpaceStroker.Infrastructure.Services.Orbit
{
    /// <summary>
    /// <see cref="IOrbitRunStore"/> on Postgres. Every write is ONE conditional ExecuteUpdate (NoTracking context — a
    /// load-mutate-SaveChanges would persist nothing). TryTakeSlot runs inside the placer's claim transaction: a row lock
    /// on the enrolment serialises concurrent claims, and READ COMMITTED re-evaluates the WHERE after the lock, so two
    /// placements can never both take the last slot. Proven by OrbitRunStoreSqlTests.
    /// </summary>
    public class OrbitRunStore : IOrbitRunStore
    {
        private readonly SpaceStrokerDbContext _context;

        public OrbitRunStore(SpaceStrokerDbContext context)
        {
            _context = context;
        }

        public async Task<bool> TryTakeSlot(Guid playerId, CancellationToken cancellationToken = default)
            => await _context.OrbitEnrollments
                .Where(x => x.PlayerId == playerId && x.Enabled && (x.BetLimit == null || x.RunBetsPlaced < x.BetLimit))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunBetsPlaced, x => x.RunBetsPlaced + 1), cancellationToken) == 1;

        public async Task<bool> ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default)
            => await _context.OrbitEnrollments
                .Where(x => x.PlayerId == playerId && x.RunBetsPlaced > 0 && x.RunStartedAt <= claimedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunBetsPlaced, x => x.RunBetsPlaced - 1), cancellationToken) == 1;

        public async Task<bool> SetNextStake(Guid playerId, decimal nextStake, DateTime betCreatedAt, CancellationToken cancellationToken = default)
            => await _context.OrbitEnrollments
                .Where(x => x.PlayerId == playerId && x.RunStartedAt <= betCreatedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunNextStake, (decimal?)nextStake), cancellationToken) == 1;

        public async Task<bool> PauseFinishedRun(Guid playerId, DateTime? runStartedAt, DateTime now, CancellationToken cancellationToken = default)
            => await _context.OrbitEnrollments
                .Where(x => x.PlayerId == playerId && x.Enabled && x.RunStartedAt == runStartedAt)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Enabled, false)
                    .SetProperty(x => x.WaitingForHouseSince, (DateTime?)null)
                    .SetProperty(x => x.UpdatedDate, (DateTime?)now), cancellationToken) == 1;
    }
}
```

`OrbitStakePlacer.cs`:

1. Fields/ctor (l.44-68): add `private readonly IOrbitRunStore _runs;`, a trailing ctor parameter `IOrbitRunStore runs`, and `_runs = runs;`.

2. `Place` — replace l.72-75:

```csharp
            var (claim, claimId, claimedAt) = await ClaimAndDebit(placement);

            if (claim != StakePlacementOutcome.Placed)
                return new StakePlacementResult(claim);
```

Replace l.98 (`return await MakeWholeUnsentStake(unsent);` inside `catch (Exception ex) when (!transferStarted)`) with:

```csharp
                var unsentResult = await MakeWholeUnsentStake(unsent);
                await ReleaseRunSlot(placement, claimedAt);
                return unsentResult;
```

Replace l.115 (`return await MakeWholeUnsentStake(unsent);` in `case StakeTransferStatus.NotSent:`) with:

```csharp
                    var notSent = await MakeWholeUnsentStake(unsent);
                    await ReleaseRunSlot(placement, claimedAt);
                    return notSent;
```

Before l.160 (`return new StakePlacementResult(StakePlacementOutcome.RegistrationRefused, Message: reason);`) insert:

```csharp
                // The bet does not exist, so it does not count toward the run (the stake itself comes back through the
                // reconciler, never from here).
                await ReleaseRunSlot(placement, claimedAt);
```

3. `ClaimAndDebit` — replace its signature and the three returns + the commit (l.182-221):

```csharp
        private async Task<(StakePlacementOutcome Outcome, Guid ClaimId, DateTime ClaimedAt)> ClaimAndDebit(StakePlacement placement)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var claim = new OrbitTransaction
            {
                OrbitAccount = _accounts.OrbitAccount,
                MintAddress = _accounts.MintAddress,
                PlayerId = placement.Player.Id,
                GameId = placement.GameId,
                PlacementRequestId = placement.PlacementRequestId,
                Type = OrbitTransactionType.BetDebit,
                // Signed: a stake leaving custody is negative, so the ledger sums to what remains.
                Amount = -placement.Stake,
                Status = OrbitTransactionStatus.Completed,
                // Open: the stake reconciler owns it if this placement never gets to decide (§6.3b).
                ClaimState = OrbitClaimState.Open,
            };

            try
            {
                await _context.OrbitTransactions.AddAsync(claim);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.ChangeTracker.Clear();
                await transaction.RollbackAsync();
                return (StakePlacementOutcome.AlreadyClaimed, Guid.Empty, default);
            }

            if (!await _ledger.TryDebit(placement.Player.Id, placement.Stake))
            {
                await transaction.RollbackAsync();
                return (StakePlacementOutcome.InsufficientFunds, Guid.Empty, default);
            }

            // ⚠️ THE RUN'S BET LIMIT (specs/new-options-for-orbit.md §6.3) — the hard "never more than N" guarantee. A
            // conditional increment in THIS transaction, LAST, so every earlier refusal leaves the counter untouched and
            // this refusal rolls the debit back. It also carries Enabled: PlaceOrbitBetConsumer never re-checks it, so a
            // placement racing a pause (manual or run-end) is refused here. AUTO placements only — a manual wager never
            // counts toward a run.
            if (placement.OrbitAutoPlaced && !await _runs.TryTakeSlot(placement.Player.Id))
            {
                await transaction.RollbackAsync();
                return (StakePlacementOutcome.RunLimitReached, Guid.Empty, default);
            }

            await transaction.CommitAsync();
            // CreatedDate is stamped by TableEntity's constructor, i.e. BEFORE the slot was taken: a run restarted after
            // this instant never has this slot given back into it.
            return (StakePlacementOutcome.Placed, claim.Id, claim.CreatedDate);
        }
```

4. Add the helper after `IsUniqueViolation`:

```csharp
        /// <summary>
        /// Gives back the run slot of an AUTO stake that provably never became a bet (TransferFailed, RegistrationRefused).
        /// InDoubt / AlreadyClaimed keep theirs — the stake may be live. Never throws: the money decision is already made.
        /// </summary>
        private async Task ReleaseRunSlot(StakePlacement placement, DateTime claimedAt)
        {
            if (!placement.OrbitAutoPlaced)
                return;

            try
            {
                await _runs.ReleaseSlot(placement.Player.Id, claimedAt);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not give back the Orbit run slot of player {PlayerId} for game {GameId}; the run counts one bet too many until its next START.",
                    placement.Player.Id, placement.GameId);
            }
        }
```

`PlaceOrbitBetConsumer.cs` — add a case to the switch (after `case StakePlacementOutcome.InsufficientFunds:` block, l.185-187):

```csharp
                case StakePlacementOutcome.RunLimitReached:
                    // Nothing was debited or sent (the claim rolled back). The run is full — or the player paused since
                    // the planner's snapshot — so this is an EXPECTED skip, and it is never re-planned into
                    // (specs/new-options-for-orbit.md §6.4).
                    await placement.SkipPlacement(message.GameId, message.PlayerId, "the player's Orbit run is full or paused");
                    return;
```

`DependencyInjection.cs` — after l.91 (`services.AddScoped<IOrbitStakeClaimStore, OrbitStakeClaimStore>();`):

```csharp
            // Run limits (#208, specs/new-options-for-orbit.md §6.3): the conditional run-slot / next-stake / run-end
            // writes. The placer takes a slot inside its claim transaction, so this shares the scoped DbContext.
            services.AddScoped<IOrbitRunStore, OrbitRunStore>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&(FullyQualifiedName~OrbitStakePlacerTests|FullyQualifiedName~PlaceOrbitBetConsumerTests|FullyQualifiedName~OrbitStakeReconciliationJobTests)"`
Expected: PASS (all pre-existing placer tests too — they now seed an enabled enrolment). Build the integration project: `dotnet build E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests.Integration` → succeeds. Whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunStore.cs src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunStore.cs src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitStakePlacer.cs src/SpaceStroker.Infrastructure/Services/Orbit/OrbitStakePlacer.cs src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs src/SpaceStroker.Infrastructure/DependencyInjection.cs src/SpaceStroker.Tests/UnitTests/Shared/InMemoryOrbitRunStore.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitStakePlacerTests.cs src/SpaceStroker.Tests/UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs src/SpaceStroker.Tests/UnitTests/Jobs/OrbitStakeReconciliationJobTests.cs src/SpaceStroker.Tests.Integration/OrbitStakeCompensationTests.cs src/SpaceStroker.Tests.Integration/OrbitStakeReconciliationTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: the stake claim takes a run slot (never more than N, refused when paused) and gives it back for a stake that never became a bet (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Shared planner — a full run is not a candidate

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` (l.68)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs` (`SeedEnrolledPlayers` l.532-573, new test)

**Interfaces — Consumes:** `OrbitEnrollment.BetLimit`, `RunBetsPlaced` (Task 1).

- [ ] **Step 1: Write the failing test**

Extend `SeedEnrolledPlayers` with two optional parameters and set them on the enrolment:

```csharp
    private List<Guid> SeedEnrolledPlayers(
        SpaceStrokerDbContext context,
        Guid parametersId,
        int count,
        decimal balance,
        bool enabled = true,
        DateTime? selfExcludedAt = null,
        DateTime? selfExcludedUntil = null,
        decimal? cashOutFloor = null,
        int? betLimit = null,
        int runBetsPlaced = 0)
```

and in its `new OrbitEnrollment { ... }` add `BetLimit = betLimit, RunBetsPlaced = runBetsPlaced,`.

Add the test after `ExcludesAPermanentlySelfExcludedPlayer`:

```csharp
    /// <summary>
    /// specs/new-options-for-orbit.md §6.4: a player whose run is full would be refused at the claim, so they are not a
    /// candidate — otherwise the start gate (ArmGate) would wait on a bet that is never coming.
    /// </summary>
    [Fact]
    public async Task ExcludesAPlayerWhoseRunReachedItsBetLimit()
    {
        using var context = NewContext();
        var game = SeedGame(context, minimumPlayers: 1);

        var full = SeedEnrolledPlayers(context, game.Parameters.Id, count: 1, balance: 10m, betLimit: 2, runBetsPlaced: 2)[0];
        var underway = SeedEnrolledPlayers(context, game.Parameters.Id, count: 1, balance: 10m, betLimit: 2, runBetsPlaced: 1)[0];
        var unlimited = SeedEnrolledPlayers(context, game.Parameters.Id, count: 1, balance: 10m, runBetsPlaced: 50)[0];

        var candidates = await Service(context).ResolveCandidates(game.Parameters, game.Id);

        candidates.ShouldNotContain(full);
        candidates.OrderBy(x => x).ShouldBe(new[] { underway, unlimited }.OrderBy(x => x));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~OrbitBetPlacementServiceTests"`
Expected: `ExcludesAPlayerWhoseRunReachedItsBetLimit` FAILS (`full` is a candidate).

- [ ] **Step 3: Implement** — replace l.68 in `ResolveCandidates`:

```csharp
                .Where(enrollment => enrollment.Enabled && enrollment.GameParametersId == parameters.Id
                                     // Run limits (specs/new-options-for-orbit.md §6.4): a full run would be refused at the
                                     // claim, so it must not hold the start gate open for a bet that never comes.
                                     && (enrollment.BetLimit == null || enrollment.RunBetsPlaced < enrollment.BetLimit))
```

- [ ] **Step 4: Run to verify it passes** — same command → PASS; whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: the shared planner leaves a full run out of its candidates (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Settings — `PATCH /orbit/settings` (validation, lock, run reset on START)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsRequest.cs` (after l.73)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsHandler.cs` (l.99, after l.162, enable block l.195, after l.361)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/UpdateOrbitSettingsTests.cs`

**Interfaces — Consumes:** `OrbitRunRules.MaxBetLimit`, `OrbitRunRules.IsValidIncreasePercent` (Task 1).
**Interfaces — Produces:** body fields `BetLimit` (int?), `ClearBetLimit` (bool), `StakeStepsEnabled` (bool?), `OnWinIncreasePercent` (decimal?), `ClearOnWinIncrease` (bool), `OnLossIncreasePercent` (decimal?), `ClearOnLossIncrease` (bool) — JSON `betLimit`, `clearBetLimit`, `stakeStepsEnabled`, `onWinIncreasePercent`, `clearOnWinIncrease`, `onLossIncreasePercent`, `clearOnLossIncrease`.

- [ ] **Step 1: Add the body fields (so the tests compile), then write the failing tests**

`UpdateOrbitSettingsRequest.cs` — after l.73 (`public bool ClearMultiplier { get; set; }`):

```csharp

        // ---- Run limits & stake steps (specs/new-options-for-orbit.md §6.2) ----
        // LOCKED while Orbit is enabled, like the solo setup above; pausing in the same request lifts the lock.

        /// <summary>Auto bets per run, 1–10,000. 0 or ClearBetLimit ⇒ no limit. Any auto-bet type.</summary>
        public int? BetLimit { get; set; }
        public bool ClearBetLimit { get; set; }

        /// <summary>Apply the on-win / on-loss steps. Setting true is refused unless the target type is solo.</summary>
        public bool? StakeStepsEnabled { get; set; }

        /// <summary>After a WIN raise the previous stake by this % (0–1000, ≤ 2 dp). ClearOnWinIncrease ⇒ back to base.</summary>
        public decimal? OnWinIncreasePercent { get; set; }
        public bool ClearOnWinIncrease { get; set; }

        /// <summary>After a LOSS raise the previous stake by this % (0–1000, ≤ 2 dp). ClearOnLossIncrease ⇒ back to base.</summary>
        public decimal? OnLossIncreasePercent { get; set; }
        public bool ClearOnLossIncrease { get; set; }
```

Tests — add `using System.Globalization;` at the top of `UpdateOrbitSettingsTests.cs`, then append before the closing brace of the class:

```csharp
    // ---- Run limits & stake steps (specs/new-options-for-orbit.md §6.2) ----------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public async Task BetLimit_AcceptsOneToTenThousand_OnAnyType(int limit)
    {
        var player = await SeedPlayer();
        var sprint = await SeedFreeTextType();
        await SeedEnrollment(player.Id, enabled: false, sprint.Id);

        await Patch(new UpdateOrbitSettingsRequestBody { BetLimit = limit });

        (await ReadEnrollment(player.Id)).BetLimit.ShouldBe(limit);
    }

    [Fact]
    public async Task BetLimit_ZeroOrTheClearFlag_MeansNoLimit()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id, configure: x => x.BetLimit = 20);

        await Patch(new UpdateOrbitSettingsRequestBody { BetLimit = 0 });
        (await ReadEnrollment(player.Id)).BetLimit.ShouldBeNull();

        await Patch(new UpdateOrbitSettingsRequestBody { BetLimit = 5 });
        await Patch(new UpdateOrbitSettingsRequestBody { ClearBetLimit = true, BetLimit = 7 });
        (await ReadEnrollment(player.Id)).BetLimit.ShouldBeNull("clear wins over a value sent alongside it.");
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(-1)]
    public async Task BetLimit_OutOfRange_IsRefused(int limit)
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id);

        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { BetLimit = limit });

        (await ReadEnrollment(player.Id)).BetLimit.ShouldBeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("12.34")]
    public async Task IncreasePercent_InBounds_IsStored(string raw)
    {
        var percent = decimal.Parse(raw, CultureInfo.InvariantCulture);
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id);

        await Patch(new UpdateOrbitSettingsRequestBody
            { StakeStepsEnabled = true, OnWinIncreasePercent = percent, OnLossIncreasePercent = percent });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.StakeStepsEnabled.ShouldBeTrue();
        enrollment.OnWinIncreasePercent.ShouldBe(percent);
        enrollment.OnLossIncreasePercent.ShouldBe(percent);
    }

    [Theory]
    [InlineData("1000.01")]
    [InlineData("-1")]
    [InlineData("12.345")]
    public async Task IncreasePercent_OutOfBoundsOrTooFine_IsRefused(string raw)
    {
        var percent = decimal.Parse(raw, CultureInfo.InvariantCulture);
        var player = await SeedPlayer();
        var duel = await SeedDuelType();
        await SeedEnrollment(player.Id, enabled: false, duel.Id);

        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnWinIncreasePercent = percent });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnLossIncreasePercent = percent });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.OnWinIncreasePercent.ShouldBeNull();
        enrollment.OnLossIncreasePercent.ShouldBeNull();
    }

    [Fact]
    public async Task BackToBase_IsStoredAsANullPercent()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id, configure: x =>
        {
            x.OnWinIncreasePercent = 50m;
            x.OnLossIncreasePercent = 100m;
        });

        await Patch(new UpdateOrbitSettingsRequestBody { ClearOnWinIncrease = true, ClearOnLossIncrease = true, OnLossIncreasePercent = 5m });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.OnWinIncreasePercent.ShouldBeNull();
        enrollment.OnLossIncreasePercent.ShouldBeNull("clear wins over a value sent alongside it.");
    }

    /// <summary>Cosmic Sprint has a fixed price: SETTING a step is refused; turning them off or clearing is harmless.</summary>
    [Fact]
    public async Task StakeSteps_OnCosmicSprint_AreRefused_ButClearingIsNot()
    {
        var player = await SeedPlayer();
        var sprint = await SeedFreeTextType();
        await SeedEnrollment(player.Id, enabled: false, sprint.Id);

        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { StakeStepsEnabled = true });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnWinIncreasePercent = 50m });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnLossIncreasePercent = 50m });

        await Patch(new UpdateOrbitSettingsRequestBody { StakeStepsEnabled = false, ClearOnWinIncrease = true, ClearOnLossIncrease = true });

        (await ReadEnrollment(player.Id)).StakeStepsEnabled.ShouldBeFalse();
    }

    /// <summary>Decision 11: the bet limit, CHANGE STAKE and both steps are locked while Orbit runs.</summary>
    [Fact]
    public async Task RunSetup_IsLockedWhileEnabled()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: true, hyper.Id);

        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { BetLimit = 5 });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { StakeStepsEnabled = true });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnWinIncreasePercent = 50m });
        await ShouldBeRefused(new UpdateOrbitSettingsRequestBody { OnLossIncreasePercent = 50m });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.BetLimit.ShouldBeNull();
        enrollment.StakeStepsEnabled.ShouldBeFalse();
    }

    /// <summary>Re-sending the stored values (or a no-op clear) while running is not a change: an echoing client is not refused.</summary>
    [Fact]
    public async Task RunSetup_ResendingTheStoredValuesWhileEnabled_IsAccepted()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: true, hyper.Id, configure: x =>
        {
            x.BetLimit = 5;
            x.StakeStepsEnabled = true;
            x.OnLossIncreasePercent = 100m;
        });

        await Patch(new UpdateOrbitSettingsRequestBody
        {
            BetLimit = 5, StakeStepsEnabled = true, OnLossIncreasePercent = 100m, ClearOnWinIncrease = true, CashOutFloor = 2m,
        });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.BetLimit.ShouldBe(5);
        enrollment.CashOutFloor.ShouldBe(2m, "the cash-out rules stay editable while running.");
    }

    [Fact]
    public async Task RunSetup_PausingInTheSameRequest_LiftsTheLock()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: true, hyper.Id);

        await Patch(new UpdateOrbitSettingsRequestBody
            { Enabled = false, BetLimit = 5, StakeStepsEnabled = true, OnLossIncreasePercent = 100m });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.Enabled.ShouldBeFalse();
        enrollment.BetLimit.ShouldBe(5);
        enrollment.StakeStepsEnabled.ShouldBeTrue();
        enrollment.OnLossIncreasePercent.ShouldBe(100m);
    }

    /// <summary>Decision 4: a START (paused → enabled) begins a fresh run — counter 0, next stake back to base.</summary>
    [Fact]
    public async Task Enabling_StartsAFreshRun()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id, configure: x =>
        {
            x.BetLimit = 3;
            x.RunBetsPlaced = 3;
            x.RunNextStake = 0.04m;
            x.RunStartedAt = DateTime.UtcNow.AddHours(-2);
        });

        await Patch(new UpdateOrbitSettingsRequestBody { Enabled = true }, balance: 5m);

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.Enabled.ShouldBeTrue();
        enrollment.RunBetsPlaced.ShouldBe(0);
        enrollment.RunNextStake.ShouldBeNull();
        enrollment.RunStartedAt.ShouldNotBeNull();
        (DateTime.UtcNow - enrollment.RunStartedAt!.Value).ShouldBeLessThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Enabling_CosmicSprint_AlsoStartsAFreshRun()
    {
        var player = await SeedPlayer();
        var sprint = await SeedFreeTextType();
        await SeedEnrollment(player.Id, enabled: false, sprint.Id, configure: x => { x.BetLimit = 2; x.RunBetsPlaced = 2; });

        await Patch(new UpdateOrbitSettingsRequestBody { Enabled = true });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.RunBetsPlaced.ShouldBe(0);
        enrollment.RunStartedAt.ShouldNotBeNull();
    }

    /// <summary>A repeat "Enabled = true" while running is not a START: the run carries on untouched.</summary>
    [Fact]
    public async Task RepeatEnable_ResetsNothing()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        await SeedEnrollment(player.Id, enabled: true, hyper.Id, configure: x =>
        {
            x.RunBetsPlaced = 2;
            x.RunNextStake = 0.02m;
            x.RunStartedAt = startedAt;
        });

        await Patch(new UpdateOrbitSettingsRequestBody { Enabled = true }, balance: 5m);

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.RunBetsPlaced.ShouldBe(2);
        enrollment.RunNextStake.ShouldBe(0.02m);
        enrollment.RunStartedAt.ShouldBe(startedAt);
    }

    /// <summary>Decision 14: a type change keeps the bet limit and the step settings (steps are ignored on Cosmic Sprint).</summary>
    [Fact]
    public async Task TypeChange_KeepsTheRunSettings()
    {
        var player = await SeedPlayer();
        var hyper = await SeedHyperRollType();
        var duel = await SeedDuelType();
        await SeedEnrollment(player.Id, enabled: false, hyper.Id, configure: x =>
        {
            x.BetLimit = 5;
            x.StakeStepsEnabled = true;
            x.OnLossIncreasePercent = 100m;
        });

        await Patch(new UpdateOrbitSettingsRequestBody { GameParametersId = duel.Id });

        var enrollment = await ReadEnrollment(player.Id);
        enrollment.GameParametersId.ShouldBe(duel.Id);
        enrollment.BetLimit.ShouldBe(5);
        enrollment.StakeStepsEnabled.ShouldBeTrue();
        enrollment.OnLossIncreasePercent.ShouldBe(100m);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~UpdateOrbitSettingsTests"`
Expected: the new tests FAIL (fields never applied, nothing refused, no reset). `TypeChange_KeepsTheRunSettings` and `RepeatEnable_ResetsNothing` may already pass — they pin behaviour that must not regress.

- [ ] **Step 3: Implement** in `UpdateOrbitSettingsHandler.cs`

1. Lock — replace l.99:

```csharp
            if (enrollment is { Enabled: true } && willBeEnabled
                && (ChangesSoloSetup(enrollment, body) || ChangesRunSetup(enrollment, body)))
```

2. Apply — right after l.161-162 (`if (!ApplySoloSetup(enrollment, body, target)) return Reject(...)`):

```csharp

            // ---- run limits & stake steps (specs/new-options-for-orbit.md §6.2) -------------------------
            // A type change does NOT reset these (decision 14): the limit is game-agnostic, steps are ignored on Sprint.
            if (!ApplyRunSetup(enrollment, body, target))
                return Reject(ErrorMessages.OrbitRequestInvalid);
```

3. Run reset — inside `if (body.Enabled is { } enabled)`, right after l.195 (`enrollment.Enabled = enabled;`):

```csharp

                // A START (paused → enabled, any type) begins a fresh run (decision 4): counter 0, next stake back to
                // base. A repeat enable while running resets nothing.
                if (enabled && !wasEnabled)
                {
                    enrollment.RunStartedAt = DateTime.UtcNow;
                    enrollment.RunBetsPlaced = 0;
                    enrollment.RunNextStake = null;

                    // ⚠️ Written even when the loaded value already matches: the previous run's settlement (SetNextStake)
                    // or a refund (ReleaseSlot) can ExecuteUpdate these columns between this load and the save below, and
                    // EF writes only a property it sees as changed.
                    var entry = Context.Entry(enrollment);
                    if (entry.State != EntityState.Added)
                    {
                        entry.Property(x => x.RunBetsPlaced).IsModified = true;
                        entry.Property(x => x.RunNextStake).IsModified = true;
                    }
                }
```

4. Helpers — after `ChangesSoloSetup` (l.361):

```csharp

        /// <summary>
        /// Whether this patch would move any run field locked while Orbit runs (bet limit, CHANGE STAKE, both steps).
        /// Re-sending the stored value — or clearing what is already clear — is not a change. BetLimit 0 means "clear".
        /// </summary>
        private static bool ChangesRunSetup(OrbitEnrollment e, UpdateOrbitSettingsRequestBody b)
            => (b.ClearBetLimit || b.BetLimit == 0 ? e.BetLimit != null : b.BetLimit is { } limit && limit != e.BetLimit)
               || (b.StakeStepsEnabled is { } steps && steps != e.StakeStepsEnabled)
               || (b.ClearOnWinIncrease ? e.OnWinIncreasePercent != null : b.OnWinIncreasePercent is { } win && win != e.OnWinIncreasePercent)
               || (b.ClearOnLossIncrease ? e.OnLossIncreasePercent != null : b.OnLossIncreasePercent is { } loss && loss != e.OnLossIncreasePercent);

        /// <summary>
        /// Applies the bet limit and the stake steps (spec §6.2). False = refuse the whole patch with OrbitRequestInvalid.
        /// Clear wins over a value sent alongside it. SETTING a step (or turning CHANGE STAKE on) needs a solo target type;
        /// turning it off / clearing is always allowed.
        /// </summary>
        private static bool ApplyRunSetup(OrbitEnrollment enrollment, UpdateOrbitSettingsRequestBody body, GameParameters? target)
        {
            if (body.ClearBetLimit || body.BetLimit == 0)
                enrollment.BetLimit = null;
            else if (body.BetLimit is { } limit)
            {
                if (limit < 1 || limit > OrbitRunRules.MaxBetLimit)
                    return false;

                enrollment.BetLimit = limit;
            }

            var solo = target != null && OrbitSoloTypes.IsSolo(target);

            if (body.StakeStepsEnabled is { } steps)
            {
                if (steps && !solo)
                    return false;

                enrollment.StakeStepsEnabled = steps;
            }

            if (body.ClearOnWinIncrease)
                enrollment.OnWinIncreasePercent = null;
            else if (body.OnWinIncreasePercent is { } win)
            {
                if (!solo || !OrbitRunRules.IsValidIncreasePercent(win))
                    return false;

                enrollment.OnWinIncreasePercent = win;
            }

            if (body.ClearOnLossIncrease)
                enrollment.OnLossIncreasePercent = null;
            else if (body.OnLossIncreasePercent is { } loss)
            {
                if (!solo || !OrbitRunRules.IsValidIncreasePercent(loss))
                    return false;

                enrollment.OnLossIncreasePercent = loss;
            }

            return true;
        }
```

(`OrbitSoloTypes`, `GameParameters` and `Microsoft.EntityFrameworkCore` are already imported; `OrbitRunRules` lives in `SpaceStroker.Domain.Orbit.Services`, already imported.)

- [ ] **Step 4: Run to verify they pass** — same command → PASS; whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsRequest.cs src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsHandler.cs src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/UpdateOrbitSettingsTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: PATCH /orbit/settings validates, locks and stores the bet limit and stake steps; START begins a fresh run (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Read side — `GET /orbit`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` (after l.134)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs` (l.188, l.228, new helper)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/GetOrbitSoloSetupTests.cs`

**Interfaces — Consumes:** `OrbitRunRules.ReachedLimit`, `OrbitRunStake.Effective` (Task 1).
**Interfaces — Produces:** `OrbitDto.{ int? BetLimit; bool StakeStepsEnabled; decimal? OnWinIncreasePercent; decimal? OnLossIncreasePercent; int RunBetsPlaced; bool RunFinished; decimal? NextStake }`.

- [ ] **Step 1: Add the DTO fields (so the tests compile), then write the failing tests**

`GetOrbitRequest.cs` — after l.134 (`public bool AutoStakeInFlight { get; set; }`):

```csharp

        // ---- Run limits & stake steps (specs/new-options-for-orbit.md §6.8) ----
        /// <summary>Auto bets per run. Null = no limit.</summary>
        public int? BetLimit { get; set; }
        public bool StakeStepsEnabled { get; set; }
        /// <summary>Null = back to base.</summary>
        public decimal? OnWinIncreasePercent { get; set; }
        /// <summary>Null = back to base.</summary>
        public decimal? OnLossIncreasePercent { get; set; }
        /// <summary>Auto bets claimed in the CURRENT run. OrbitBetsPlaced stays the all-time count.</summary>
        public int RunBetsPlaced { get; set; }
        /// <summary>Paused AND the run reached its limit — lets the client say "Finished N/N" after a reload.</summary>
        public bool RunFinished { get; set; }
        /// <summary>The (clamped) stake the next solo auto round uses; null on Cosmic Sprint or with no type.</summary>
        public decimal? NextStake { get; set; }
```

Tests — append to `GetOrbitSoloSetupTests` (before `private async Task SeedEnrollment`):

```csharp
    // ---- run limits & stake steps (specs/new-options-for-orbit.md §6.8) ----

    [Fact]
    public async Task Enrollment_MapsTheRunSettingsAndCounter()
    {
        var player = await SeedPlayer();
        await SeedEnrollment(player.Id, e =>
        {
            e.BetLimit = 20;
            e.StakeStepsEnabled = true;
            e.OnWinIncreasePercent = null;
            e.OnLossIncreasePercent = 100m;
            e.RunBetsPlaced = 3;
        });

        var dto = await Read();

        dto.BetLimit.ShouldBe(20);
        dto.StakeStepsEnabled.ShouldBeTrue();
        dto.OnWinIncreasePercent.ShouldBeNull();
        dto.OnLossIncreasePercent.ShouldBe(100m);
        dto.RunBetsPlaced.ShouldBe(3);
        dto.RunFinished.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, 3, 3, true)]
    [InlineData(true, 3, 3, false)]     // still enabled: the last bet is riding
    [InlineData(false, 3, 2, false)]    // paused by hand mid-run
    [InlineData(false, null, 7, false)] // no limit
    public async Task RunFinished_OnlyWhenPausedAtTheLimit(bool enabled, int? limit, int placed, bool expected)
    {
        var player = await SeedPlayer();
        await SeedEnrollment(player.Id, e =>
        {
            e.Enabled = enabled;
            e.BetLimit = limit;
            e.RunBetsPlaced = placed;
        });

        (await Read()).RunFinished.ShouldBe(expected);
    }

    [Fact]
    public async Task NextStake_OnASoloType_IsTheClampedSteppedStake()
    {
        var player = await SeedPlayer();
        var hyper = await Seed("Hyper Roll", houseEdge: true, max: 0.1m);
        await SeedEnrollment(player.Id, e =>
        {
            e.GameParametersId = hyper.Id;
            e.Stake = 0.02m;
            e.RunNextStake = 0.5m;
        });

        (await Read()).NextStake.ShouldBe(0.1m);
    }

    [Fact]
    public async Task NextStake_OnASoloType_WithNoStep_IsTheBaseStake()
    {
        var player = await SeedPlayer();
        var duel = await Seed("Monkey Duel", duel: true, max: 0.5m);
        await SeedEnrollment(player.Id, e =>
        {
            e.GameParametersId = duel.Id;
            e.Stake = 0.02m;
        });

        (await Read()).NextStake.ShouldBe(0.02m);
    }

    [Fact]
    public async Task NextStake_OnCosmicSprint_IsNull()
    {
        var player = await SeedPlayer();
        var sprint = await Seed("Cosmic Sprint");
        await SeedEnrollment(player.Id, e =>
        {
            e.GameParametersId = sprint.Id;
            e.Stake = 0.02m;
            e.RunNextStake = 0.04m;
        });

        (await Read()).NextStake.ShouldBeNull();
    }

    [Fact]
    public async Task StreamerView_HasTheRunDefaults()
    {
        var player = await SeedPlayer();
        await SeedEnrollment(player.Id, e => { e.BetLimit = 3; e.RunBetsPlaced = 3; e.StakeStepsEnabled = true; });
        MakeStreamer();

        var dto = await Read();

        dto.IsStreamer.ShouldBeTrue();
        dto.BetLimit.ShouldBeNull();
        dto.StakeStepsEnabled.ShouldBeFalse();
        dto.OnWinIncreasePercent.ShouldBeNull();
        dto.OnLossIncreasePercent.ShouldBeNull();
        dto.RunBetsPlaced.ShouldBe(0);
        dto.RunFinished.ShouldBeFalse();
        dto.NextStake.ShouldBeNull();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~GetOrbitSoloSetupTests"`
Expected: the mapping / `RunFinished` (true case) / `NextStake` tests FAIL (fields stay at defaults). `StreamerView_HasTheRunDefaults` and the default-false theory rows pass already.

- [ ] **Step 3: Implement** in `GetOrbitHandler.cs`

Add `using SpaceStroker.Domain.Orbit.Services;` to the usings.

After l.188 (the `AutoStakeInFlight = ...` initializer, before the closing `};` at l.189), add:

```csharp
                BetLimit = enrollment?.BetLimit,
                StakeStepsEnabled = enrollment?.StakeStepsEnabled ?? false,
                OnWinIncreasePercent = enrollment?.OnWinIncreasePercent,
                OnLossIncreasePercent = enrollment?.OnLossIncreasePercent,
                RunBetsPlaced = enrollment?.RunBetsPlaced ?? 0,
                RunFinished = enrollment is { Enabled: false } && OrbitRunRules.ReachedLimit(enrollment),
                NextStake = await ResolveNextStake(enrollment, cancellationToken),
```

In `StreamerView`, after `AutoStakeInFlight = false,` (l.228):

```csharp
                // #208's run fields: no limit, no steps, no run — the SPA never shows auto-bet for a streamer.
                BetLimit = null,
                StakeStepsEnabled = false,
                OnWinIncreasePercent = null,
                OnLossIncreasePercent = null,
                RunBetsPlaced = 0,
                RunFinished = false,
                NextStake = null,
```

New helper (after `StreamerView`):

```csharp
        /// <summary>
        /// The stake the next SOLO auto round uses — the same OrbitRunStake.Effective the loop stakes and zero-out compares
        /// with, so the screen and the money cannot disagree. Null on Cosmic Sprint (fixed price) or with no type.
        /// </summary>
        private async Task<decimal?> ResolveNextStake(Domain.Orbit.Entities.OrbitEnrollment? enrollment, CancellationToken cancellationToken)
        {
            if (enrollment?.GameParametersId is not { } typeId)
                return null;

            var parameters = await Context.GameParameters.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == typeId, cancellationToken);

            return parameters != null && OrbitSoloTypes.IsSolo(parameters)
                ? OrbitRunStake.Effective(enrollment, parameters)
                : null;
        }
```

- [ ] **Step 4: Run to verify they pass** — same command → PASS; whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/GetOrbitSoloSetupTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: GET /orbit returns the run settings, the run counter, runFinished and the next stake (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: `OrbitRunService` — steps, run end, `OrbitRunFinished` push

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunService.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunService.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (next to the store registration from Task 2)
- Test: create `src/SpaceStroker.Tests/UnitTests/Services/OrbitRunServiceTests.cs`

**Interfaces — Consumes:** `IOrbitRunStore` (Task 2), `OrbitRunStake.Next`, `OrbitRunRules.ReachedLimit` (Task 1), `GameSettings.OrbitRunFinishedEndpoint` (Task 1), `IOrbitChoiceClaimService.Release(Guid, CancellationToken)`, `OrbitNotificationChannel.GroupFor(Guid)`.
**Interfaces — Produces:**

```csharp
public interface IOrbitRunService
{
    Task ApplySettledRound(Guid gameId, CancellationToken cancellationToken = default);
    Task EvaluateRunEnd(Guid playerId, Guid? settlingGameId = null, CancellationToken cancellationToken = default);
    Task ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default);
}
public sealed record OrbitRunFinishedPush(Guid GameTypeId, string GameTypeName, int BetsPlaced);
```

- [ ] **Step 1: Create the interface (so the tests compile), then write the failing tests**

`src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunService.cs`:

```csharp
namespace SpaceStroker.Infrastructure.Services.Orbit
{
    /// <summary>
    /// The settlement side of a run (specs/new-options-for-orbit.md §6.6): the next stake after a settled solo auto bet,
    /// and pausing a run that placed its N bets. Consumers only (settlement / refunds / the solo loop). Every method
    /// swallows and logs its own failures — a run rule must never fail a settlement or a refund that already moved money.
    /// </summary>
    public interface IOrbitRunService
    {
        /// <summary>
        /// For each player with an OrbitAutoPlaced bet in the round: the stake step (solo types, CHANGE STAKE on, the bet
        /// Rewarded or still Made), then <see cref="EvaluateRunEnd"/> with this round excluded. Called BEFORE the stop rules.
        /// </summary>
        Task ApplySettledRound(Guid gameId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pauses the player's run (and announces it) when it is enabled, at its limit, and has no OrbitAutoPlaced,
        /// non-Refunded bet in an unsettled round other than <paramref name="settlingGameId"/>.
        /// </summary>
        Task EvaluateRunEnd(Guid playerId, Guid? settlingGameId = null, CancellationToken cancellationToken = default);

        /// <summary>Gives back a run slot taken at <paramref name="claimedAt"/> (never across runs, never below zero).</summary>
        Task ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default);
    }

    /// <summary>The private-channel payload of a finished run, serialized camelCase: { gameTypeId, gameTypeName, betsPlaced }.</summary>
    public sealed record OrbitRunFinishedPush(Guid GameTypeId, string GameTypeName, int BetsPlaced);
}
```

`src/SpaceStroker.Tests/UnitTests/Services/OrbitRunServiceTests.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Hubs;
using SpaceStroker.Infrastructure.Services.Orbit;
using SpaceStroker.Tests.UnitTests.Shared;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Services;

/// <summary>
/// The settlement side of a run (specs/new-options-for-orbit.md §6.6–§6.7). Runs over InMemoryOrbitRunStore (the
/// conditional writes' semantics without ExecuteUpdate); the SQL itself is proven in OrbitRunStoreSqlTests.
/// </summary>
public class OrbitRunServiceTests : UnitTestsClass
{
    private const string Endpoint = "OrbitRunFinished";
    private const string TypeName = "Hyper Roll";

    private readonly Mock<IOrbitChoiceClaimService> _claims = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IHubContext<ActiveGamesHub>> _hub = new();
    private readonly GeneralSettings _settings = new();
    private Func<Guid, Task>? _beforePause;

    public OrbitRunServiceTests()
    {
        _clients.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupProxy.Object);
        _hub.SetupGet(x => x.Clients).Returns(_clients.Object);
        _settings.Games = new GameSettings { OrbitRunFinishedEndpoint = Endpoint };
    }

    // ---------------------------------------------------------------- steps

    [Fact]
    public async Task ALoss_RaisesTheNextStakeByThePercent_AndTwoLossesDouble()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.010m));
        Read(playerId).RunNextStake.ShouldBe(0.020m);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.020m));
        Read(playerId).RunNextStake.ShouldBe(0.040m);
    }

    [Fact]
    public async Task AWin_WithBackToBase_ReturnsToTheBaseStake()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => e.RunNextStake = 0.04m);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Rewarded, 0.04m));

        Read(playerId).RunNextStake.ShouldBe(0.01m);
    }

    [Fact]
    public async Task ARefundedBet_LeavesTheNextStakeAsItWas()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => e.RunNextStake = 0.04m);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Refunded, 0.04m));

        Read(playerId).RunNextStake.ShouldBe(0.04m);
    }

    [Fact]
    public async Task WithChangeStakeOff_NoStepIsWritten()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => e.StakeStepsEnabled = false);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m));

        Read(playerId).RunNextStake.ShouldBeNull();
    }

    [Fact]
    public async Task OnCosmicSprint_NoStepIsWritten()
    {
        var typeId = SeedType(solo: false);
        var playerId = SeedEnrollment(typeId);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 1m));

        Read(playerId).RunNextStake.ShouldBeNull();
    }

    [Fact]
    public async Task ABetFromAnEarlierRun_NeverStepsTheCurrentRun()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => e.RunStartedAt = DateTime.UtcNow);

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m,
            betCreated: DateTime.UtcNow.AddHours(-1)));

        Read(playerId).RunNextStake.ShouldBeNull();
    }

    /// <summary>Manual Orbit bets never count and never move the progression — not even at the limit.</summary>
    [Fact]
    public async Task AManualBet_NeitherStepsNorEndsTheRun()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m,
            autoPlaced: false));

        var enrollment = Read(playerId);
        enrollment.RunNextStake.ShouldBeNull();
        enrollment.Enabled.ShouldBeTrue();
        VerifyNoPush();
    }

    // ---------------------------------------------------------------- run end

    /// <summary>
    /// ⚠️ REGRESSION: the round being settled is still RewardForWinnersInProgress (an UnsettledStatus) when the reward path
    /// calls this. Without excluding it, the Nth bet would count as "in flight" and the run would never pause.
    /// </summary>
    [Fact]
    public async Task TheNthBetSettling_PausesTheRun_EvenWhileItsRoundIsStillRewarding()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });
        var gameId = SeedRound(typeId, GameStatus.RewardForWinnersInProgress, playerId, BetStatus.Made, 0.01m);

        await Service().ApplySettledRound(gameId);

        Read(playerId).Enabled.ShouldBeFalse();
        _claims.Verify(x => x.Release(playerId, It.IsAny<CancellationToken>()), Times.Once);

        var push = SinglePush(playerId);
        push["gameTypeId"]!.Value<string>().ShouldBe(typeId.ToString());
        push["gameTypeName"]!.Value<string>().ShouldBe(TypeName);
        push["betsPlaced"]!.Value<int>().ShouldBe(1);
    }

    [Fact]
    public async Task DoesNotPause_WhileAnotherAutoBetIsStillInFlight()
    {
        var typeId = SeedType(solo: false);
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 2; e.RunBetsPlaced = 2; });
        var settling = SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 1m);
        SeedRound(typeId, GameStatus.InProgress, playerId, BetStatus.Made, 1m); // the successor round's bet

        await Service().ApplySettledRound(settling);

        Read(playerId).Enabled.ShouldBeTrue();
        VerifyNoPush();
        _claims.Verify(x => x.Release(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARefundedBetInAnOpenRound_IsNotInFlight()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 2; e.RunBetsPlaced = 2; });
        var settling = SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m);
        SeedRound(typeId, GameStatus.WaitingBets, playerId, BetStatus.Refunded, 0.01m);

        await Service().ApplySettledRound(settling);

        Read(playerId).Enabled.ShouldBeFalse();
    }

    /// <summary>The refund path calls EvaluateRunEnd with no settling round; the refunded bet is excluded by its status.</summary>
    [Fact]
    public async Task EvaluateRunEnd_FromARefund_PausesWhenNothingIsLeftInFlight()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });
        SeedRound(typeId, GameStatus.WaitingBets, playerId, BetStatus.Refunded, 0.01m);

        await Service().EvaluateRunEnd(playerId);

        Read(playerId).Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(3, 2)]
    public async Task DoesNotPause_BelowTheLimitOrWithNoLimit(int? limit, int placed)
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = limit; e.RunBetsPlaced = placed; });

        await Service().ApplySettledRound(SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m));

        Read(playerId).Enabled.ShouldBeTrue();
        VerifyNoPush();
    }

    /// <summary>A stale evaluation never pauses a run the player restarted between the read and the pause.</summary>
    [Fact]
    public async Task OnlyPausesTheRunItEvaluated()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });
        var gameId = SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m);
        _beforePause = async id =>
        {
            using var context = NewContext();
            var row = context.OrbitEnrollments.AsTracking().Single(x => x.PlayerId == id);
            row.RunStartedAt = DateTime.UtcNow.AddSeconds(5);
            row.RunBetsPlaced = 0;
            await context.SaveChangesAsync();
        };

        await Service().ApplySettledRound(gameId);

        Read(playerId).Enabled.ShouldBeTrue();
        VerifyNoPush();
        _claims.Verify(x => x.Release(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailingClaimRelease_IsLoggedAndSwallowed_AndTheRunStillPausesAndAnnounces()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => { e.BetLimit = 1; e.RunBetsPlaced = 1; });
        var gameId = SeedRound(typeId, GameStatus.RewardForWinnersCompleted, playerId, BetStatus.Made, 0.01m);
        _claims.Setup(x => x.Release(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var logs = await CaptureLogs(() => Should.NotThrowAsync(() => Service().ApplySettledRound(gameId)));

        logs.ShouldContain(x => x.Level == LogEventLevel.Error);
        Read(playerId).Enabled.ShouldBeFalse();
        SinglePush(playerId);
    }

    [Fact]
    public async Task AMissingRound_IsANoOp()
        => await Should.NotThrowAsync(() => Service().ApplySettledRound(Guid.NewGuid()));

    // ---------------------------------------------------------------- release

    [Fact]
    public async Task ReleaseSlot_GivesOneSlotBack()
    {
        var typeId = SeedType();
        var playerId = SeedEnrollment(typeId, e => e.RunBetsPlaced = 2);

        await Service().ReleaseSlot(playerId, DateTime.UtcNow);

        Read(playerId).RunBetsPlaced.ShouldBe(1);
    }

    // ---------------------------------------------------------------- fixture

    private OrbitRunService Service()
    {
        var context = NewContext();
        return new OrbitRunService(context, new InMemoryOrbitRunStore(context) { BeforePause = _beforePause }, _claims.Object,
            _hub.Object, _settings);
    }

    private SpaceStrokerDbContext NewContext() => new(ContextOptions);

    private Guid SeedType(bool solo = true)
    {
        using var context = NewContext();
        var parameters = new GameParameters
        {
            Name = TypeName, Description = "d", RequiredAmount = solo ? 0.01m : 1m, MaximumStake = solo ? 1m : null,
            IsHouseEdge = solo, MinimumNumberOfPlayers = 1, AllowOrbit = true, Active = true,
        };
        context.Add(parameters);
        context.SaveChanges();
        return parameters.Id;
    }

    /// <summary>Enabled, CHANGE STAKE on, after a loss +100 %, after a win back to base, base 0.01, run started an hour ago.</summary>
    private Guid SeedEnrollment(Guid typeId, Action<OrbitEnrollment>? configure = null)
    {
        using var context = NewContext();
        var player = new Player { ExternalPlayerId = Faker.Random.String2(40), LastSeenAt = DateTime.UtcNow };
        var enrollment = new OrbitEnrollment
        {
            PlayerId = player.Id,
            Enabled = true,
            GameParametersId = typeId,
            Stake = 0.01m,
            StakeStepsEnabled = true,
            OnLossIncreasePercent = 100m,
            RunStartedAt = DateTime.UtcNow.AddHours(-1),
        };
        configure?.Invoke(enrollment);
        context.Add(player);
        context.Add(enrollment);
        context.SaveChanges();
        return player.Id;
    }

    private Guid SeedRound(Guid typeId, GameStatus status, Guid playerId, BetStatus betStatus, decimal amount,
        bool autoPlaced = true, DateTime? betCreated = null)
    {
        using var context = NewContext();
        var game = new Game(typeId) { Status = status, ServerSeedHash = "hash" };
        context.Add(game);
        context.Add(new Bet(game.Id, Faker.Random.String2(64, "0123456789abcdef"), playerId, "banana", amount)
        {
            Status = betStatus,
            FundedByOrbit = true,
            OrbitAutoPlaced = autoPlaced,
            CreatedDate = betCreated ?? DateTime.UtcNow,
        });
        context.SaveChanges();
        return game.Id;
    }

    private OrbitEnrollment Read(Guid playerId)
    {
        using var context = NewContext();
        return context.OrbitEnrollments.AsNoTracking().Single(x => x.PlayerId == playerId);
    }

    private JObject SinglePush(Guid playerId)
    {
        _clients.Verify(x => x.Group(OrbitNotificationChannel.GroupFor(playerId)), Times.Once);
        var sends = _groupProxy.Invocations.Where(i => i.Method.Name == nameof(IClientProxy.SendCoreAsync)).ToList();
        sends.Count.ShouldBe(1);
        sends[0].Arguments[0].ShouldBe(Endpoint);
        var args = (object?[])sends[0].Arguments[1];
        return JObject.Parse(args.Single().ShouldBeOfType<string>());
    }

    private void VerifyNoPush()
        => _groupProxy.Invocations.Where(i => i.Method.Name == nameof(IClientProxy.SendCoreAsync)).ShouldBeEmpty();

    private static async Task<List<LogEvent>> CaptureLogs(Func<Task> act)
    {
        var sink = new CapturingSink();
        var original = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            await act();
        }
        finally
        {
            Log.Logger = original;
        }

        lock (sink.Events)
            return sink.Events.ToList();
    }

    private sealed class CapturingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent)
        {
            lock (Events)
                Events.Add(logEvent);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~OrbitRunServiceTests"`
Expected: build FAILS (`OrbitRunService` does not exist).

- [ ] **Step 3: Implement**

`src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunService.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Configs.ValueObjects;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Domain.Orbit.Services;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Hubs;

namespace SpaceStroker.Infrastructure.Services.Orbit
{
    /// <summary>
    /// Run limits & stake steps on the settlement side (specs/new-options-for-orbit.md §6.6–§6.7).
    ///
    /// ⚠️ Writes NO ledger row and moves no money. Every enrolment write is a conditional ExecuteUpdate behind
    /// <see cref="IOrbitRunStore"/>. Every failure is logged and swallowed: settlement and refunds already moved the money,
    /// and the solo loop's limit check (OrbitSoloAutoBetService) is the retry path for a missed run end.
    /// </summary>
    public class OrbitRunService : IOrbitRunService
    {
        private readonly SpaceStrokerDbContext _context;
        private readonly IOrbitRunStore _store;
        private readonly IOrbitChoiceClaimService _claims;
        private readonly IHubContext<ActiveGamesHub> _hub;
        private readonly GeneralSettings _settings;

        public OrbitRunService(SpaceStrokerDbContext context, IOrbitRunStore store, IOrbitChoiceClaimService claims,
            IHubContext<ActiveGamesHub> hub, GeneralSettings settings)
        {
            _context = context;
            _store = store;
            _claims = claims;
            _hub = hub;
            _settings = settings;
        }

        public async Task ApplySettledRound(Guid gameId, CancellationToken cancellationToken = default)
        {
            Game? game;

            try
            {
                game = await _context.Games.AsNoTracking()
                    .Include(x => x.Parameters)
                    .Include(x => x.Bets)
                    .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not load round {GameId} to apply the Orbit run rules.", gameId);
                return;
            }

            if (game?.Parameters == null)
                return;

            var autoBetsByPlayer = (game.Bets ?? new List<Bet>())
                .Where(x => x.OrbitAutoPlaced && x.PlayerId != null)
                .GroupBy(x => x.PlayerId!.Value)
                .ToList();

            foreach (var playerBets in autoBetsByPlayer)
            {
                var playerId = playerBets.Key;

                try
                {
                    if (OrbitSoloTypes.IsSolo(game.Parameters))
                        await ApplyStep(playerId, playerBets.OrderByDescending(x => x.CreatedDate).First(), game, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not apply the Orbit stake step for player {PlayerId} after round {GameId}.", playerId, gameId);
                }

                // ⚠️ The settling round is EXCLUDED from the in-flight check: it is still RewardForWinnersInProgress (or
                // otherwise unsettled) when the reward path calls this, so counting it would never pause on the Nth bet.
                await EvaluateRunEnd(playerId, gameId, cancellationToken);
            }
        }

        public async Task EvaluateRunEnd(Guid playerId, Guid? settlingGameId = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var enrollment = await _context.OrbitEnrollments.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PlayerId == playerId, cancellationToken);

                if (enrollment is not { Enabled: true } || !OrbitRunRules.ReachedLimit(enrollment))
                    return;

                // The OrbitDto.AutoStakeInFlight predicate, minus the settling round and minus refunded bets (a refund
                // whose round is still open).
                var excludedGameId = settlingGameId ?? Guid.Empty;
                var inFlight = await _context.GameBets.AsNoTracking().AnyAsync(b => b.PlayerId == playerId
                    && b.OrbitAutoPlaced
                    && b.Status != BetStatus.Refunded
                    && b.GameId != excludedGameId
                    && HouseEdgeCoverage.UnsettledStatuses.Contains(b.Game.Status), cancellationToken);

                if (inFlight)
                    return;

                // Conditional on THIS run: a stale evaluation never pauses a run the player just restarted.
                if (!await _store.PauseFinishedRun(playerId, enrollment.RunStartedAt, DateTime.UtcNow, cancellationToken))
                    return;

                Log.Information("Orbit run of player {PlayerId} placed its {Bets} bets; paused.", playerId, enrollment.RunBetsPlaced);

                // A pause gives the free-text word claim back, as the settings pause does (UpdateOrbitSettingsHandler).
                try
                {
                    await _claims.Release(playerId, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not release the Orbit word claim of player {PlayerId} after their run finished.", playerId);
                }

                await PushRunFinished(playerId, enrollment);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not evaluate the end of the Orbit run of player {PlayerId}; the solo loop / next settlement retries.", playerId);
            }
        }

        public async Task ReleaseSlot(Guid playerId, DateTime claimedAt, CancellationToken cancellationToken = default)
        {
            try
            {
                await _store.ReleaseSlot(playerId, claimedAt, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not give back an Orbit run slot of player {PlayerId}; the run counts one bet too many until its next START.", playerId);
            }
        }

        /// <summary>
        /// Win = Rewarded, loss = still Made on a settled round (there is no Lost status). Anything else (Refunded,
        /// NeedsRefunding, NeedsRewarding, NeedsManualAnalysis) leaves the next stake as it is.
        /// </summary>
        private async Task ApplyStep(Guid playerId, Bet bet, Game game, CancellationToken cancellationToken)
        {
            bool won;
            if (bet.Status == BetStatus.Rewarded)
                won = true;
            else if (bet.Status == BetStatus.Made)
                won = false;
            else
                return;

            var enrollment = await _context.OrbitEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlayerId == playerId, cancellationToken);

            // A bet on a type the enrolment has since left must not set the new type's next stake.
            if (enrollment is not { StakeStepsEnabled: true } || enrollment.GameParametersId != game.ParametersId)
                return;

            var next = OrbitRunStake.Next(bet.Amount, won, enrollment, game.Parameters);

            // Conditional on RunStartedAt <= the bet: a bet of an earlier run never steps the current one.
            await _store.SetNextStake(playerId, next, bet.CreatedDate, cancellationToken);
        }

        /// <summary>Private-channel push (§6.7), exactly like OrbitSoloAutoBetService.Push. Never allowed to fail the caller.</summary>
        private async Task PushRunFinished(Guid playerId, OrbitEnrollment enrollment)
        {
            try
            {
                var endpoint = _settings.Games?.OrbitRunFinishedEndpoint;
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    Log.Warning("GameSettings.OrbitRunFinishedEndpoint is not configured; the Orbit run-finished push was skipped.");
                    return;
                }

                var typeName = enrollment.GameParametersId is { } typeId
                    ? await _context.GameParameters.AsNoTracking().Where(x => x.Id == typeId).Select(x => x.Name).FirstOrDefaultAsync()
                    : null;

                var push = new OrbitRunFinishedPush(enrollment.GameParametersId ?? Guid.Empty, typeName ?? string.Empty, enrollment.RunBetsPlaced);

                await _hub.Clients
                    .Group(OrbitNotificationChannel.GroupFor(playerId))
                    .SendAsync(endpoint, push.AsJson());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "The Orbit run-finished push for player {PlayerId} failed.", playerId);
            }
        }
    }
}
```

`DependencyInjection.cs` — next to the `IOrbitRunStore` registration (Task 2):

```csharp
            // The settlement side of a run (#208 §6.6): next stake, run-end pause, the OrbitRunFinished push. Only
            // Consumers invoke it; registered in both hosts like IOrbitSoloAutoBetService (ValidateOnBuild).
            services.AddScoped<IOrbitRunService, OrbitRunService>();
```

- [ ] **Step 4: Run to verify they pass** — same command → PASS; whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitRunService.cs src/SpaceStroker.Infrastructure/Services/Orbit/OrbitRunService.cs src/SpaceStroker.Infrastructure/DependencyInjection.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitRunServiceTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: OrbitRunService writes the next stake after a settled solo bet and pauses + announces a run that placed its N bets (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Settlement wiring — reward (steps before the stop rules), refunds (slot back), zero-out (next stake)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (l.77, l.267, new helper)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` (l.111-121)
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (l.230-242)
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs`, `src/SpaceStroker.Tests/UnitTests/Background/RefundInvalidBetConsumerTests.cs`, `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`

**Interfaces — Consumes:** `IOrbitRunService.ApplySettledRound / EvaluateRunEnd / ReleaseSlot` (Task 6), `OrbitRunStake.Effective` (Task 1).

- [ ] **Step 1: Write the failing tests**

`RewardGameConsumerTests.cs`:
- Add the field `private readonly Mock<IOrbitRunService> _runs = new();` (next to `_soloLoop`, l.41) and register it in `ScopeFactory` after l.65: `provider.Setup(x => x.GetService(typeof(IOrbitRunService))).Returns(_runs.Object);`
- Append (before the class's closing brace):

```csharp
    // ---- Run limits & stake steps (specs/new-options-for-orbit.md §6.6) ----

    /// <summary>⚠️ The step must land BEFORE zero-out, which compares the balance with the NEXT stake.</summary>
    [Fact]
    public async Task SoloAutoRound_NobodyWon_AppliesTheRunRulesBeforeTheStopRules()
    {
        using var context = NewContext();
        var game = SeedAutoPlacedHyperRoll(context, won: false);
        var order = new List<string>();
        _runs.Setup(x => x.ApplySettledRound(game.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("run")).Returns(Task.CompletedTask);
        _orbitSettlement.Setup(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("floor")).Returns(Task.CompletedTask);
        _soloLoop.Setup(x => x.OnAutoRoundSettled(game.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("loop")).Returns(Task.CompletedTask);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        order.ShouldBe(new[] { "run", "floor", "loop" });
    }

    [Fact]
    public async Task SoloAutoRound_WithAWinner_AppliesTheRunRulesBeforeTheStopRules()
    {
        using var context = NewContext();
        var game = SeedAutoPlacedHyperRoll(context, won: true);
        SetupHappyPayments();
        _orbitSettlement.Setup(x => x.CreditWin(It.IsAny<Bet>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Success("ORBIT_CREDIT_SIG"));
        _solana.Setup(x => x.GetSolBalance("TreasuryPublicKey")).ReturnsAsync(100m);
        var order = new List<string>();
        _runs.Setup(x => x.ApplySettledRound(game.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("run")).Returns(Task.CompletedTask);
        _orbitSettlement.Setup(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("floor")).Returns(Task.CompletedTask);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        order.ShouldBe(new[] { "run", "floor" });
    }

    /// <summary>Cosmic Sprint rounds count too: the run end is evaluated for every auto-bet type.</summary>
    [Fact]
    public async Task ASharedRaceRoundWithAutoBets_AppliesTheRunRules()
    {
        using var context = NewContext();
        var game = SeedRoundWithNoWinners(context, Guid.NewGuid());

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _runs.Verify(x => x.ApplySettledRound(game.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARoundWithoutAutoBets_NeverAppliesTheRunRules()
    {
        using var context = NewContext();
        var game = SeedRoundWithNoWinners(context);
        context.Add(new Bet(game.Id, HexSeed(), Guid.NewGuid(), "falcon", 1m)
        {
            Status = BetStatus.Made,
            FundedByOrbit = true,
            OrbitAutoPlaced = false,
        });
        await context.SaveChangesAsync();

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _runs.Verify(x => x.ApplySettledRound(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailingRunRulesHook_NeverFailsTheSettlement()
    {
        using var context = NewContext();
        var game = SeedAutoPlacedHyperRoll(context, won: false);
        _runs.Setup(x => x.ApplySettledRound(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Should.NotThrowAsync(() => Consumer(context).ProcessMessage(new RewardGameEvent(game.Id)));

        _gameService.Verify(x => x.UpdateGameStatus(
            It.Is<Game>(g => g.Id == game.Id), GameStatus.RewardForWinnersCompleted, null, null), Times.Once);
        _orbitSettlement.Verify(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _soloLoop.Verify(x => x.OnAutoRoundSettled(game.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
```

`RefundInvalidBetConsumerTests.cs`:
- Add the field `private readonly Mock<IOrbitRunService> _runsMock = new();` (next to `_soloLoopMock`, l.33) and in the constructor after l.68: `_serviceProviderMock.Setup(x => x.GetService(typeof(IOrbitRunService))).Returns(_runsMock.Object);`
- Append (before `public void Dispose()`):

```csharp
        // ---- Run limits (specs/new-options-for-orbit.md §6.3) ----

        /// <summary>A refunded auto bet does not count: its slot comes back, the run end is re-evaluated, then the loop.</summary>
        [Fact]
        public async Task ARefundedOrbitAutoBet_GivesTheRunSlotBack_AndReEvaluatesTheRun_BeforeReleasingTheLoop()
        {
            var bet = SeedOrbitAutoBetOnADuel();
            SetupOrbitRefund(success: true);
            var playerId = bet.PlayerId!.Value;
            var order = new List<string>();
            _runsMock.Setup(x => x.ReleaseSlot(playerId, bet.CreatedDate, It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("release")).Returns(Task.CompletedTask);
            _runsMock.Setup(x => x.EvaluateRunEnd(playerId, null, It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("run-end")).Returns(Task.CompletedTask);
            _soloLoopMock.Setup(x => x.OnAutoRoundSettled(bet.GameId, It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("loop")).Returns(Task.CompletedTask);

            await new RefundInvalidBetConsumer(_serviceScopeFactoryMock.Object)
                .ProcessMessage(new RefundInvalidBetEvent(bet.Id, "player1", 2, "Duel given up"));

            order.ShouldBe(new[] { "release", "run-end", "loop" });
        }

        [Fact]
        public async Task AManualOrbitBetRefund_NeverTouchesTheRun()
        {
            var bet = SeedOrbitAutoBetOnADuel(autoPlaced: false);
            SetupOrbitRefund(success: true);

            await new RefundInvalidBetConsumer(_serviceScopeFactoryMock.Object)
                .ProcessMessage(new RefundInvalidBetEvent(bet.Id, "player1", 2, "Duel given up"));

            _runsMock.Verify(x => x.ReleaseSlot(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AFailedOrbitRefund_KeepsTheRunSlot()
        {
            var bet = SeedOrbitAutoBetOnADuel();
            SetupOrbitRefund(success: false);

            await new RefundInvalidBetConsumer(_serviceScopeFactoryMock.Object)
                .ProcessMessage(new RefundInvalidBetEvent(bet.Id, "player1", 2, "Duel given up"));

            _runsMock.Verify(x => x.ReleaseSlot(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AFailingRunHook_NeverFailsTheRefund_NorSkipsTheLoop()
        {
            var bet = SeedOrbitAutoBetOnADuel();
            SetupOrbitRefund(success: true);
            _runsMock.Setup(x => x.ReleaseSlot(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("db down"));

            await Should.NotThrowAsync(() => new RefundInvalidBetConsumer(_serviceScopeFactoryMock.Object)
                .ProcessMessage(new RefundInvalidBetEvent(bet.Id, "player1", 2, "Duel given up")));

            _context.GameBets.Single(x => x.Id == bet.Id).Status.ShouldBe(BetStatus.Refunded);
            _soloLoopMock.Verify(x => x.OnAutoRoundSettled(bet.GameId, It.IsAny<CancellationToken>()), Times.Once);
        }
```

`OrbitBetSettlementServiceTests.cs` — append after `EvaluateZeroOut_SoloType_NeverReclaimsAWord`:

```csharp
    /// <summary>
    /// specs/new-options-for-orbit.md §6.6: zero-out compares the balance with the NEXT (stepped) stake. Base 0.01 is
    /// payable from 0.03, but the next stake after a loss is 0.05 — so this IS a zero-out.
    /// </summary>
    [Fact]
    public async Task EvaluateZeroOut_SoloType_ComparesWithTheSteppedNextStake()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, choice: "abcd", isDuel: true, requiredAmount: 0.002m, stake: 0.01m);
        TargetTheSeededType(context, playerId);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0.03m);

        // Already-notified so the method stops before SetZeroOutFlag (ExecuteUpdateAsync, not runnable in-memory).
        var enrollment = ReadEnrollment(context, playerId);
        enrollment.StakeStepsEnabled = true;
        enrollment.RunNextStake = 0.05m;
        enrollment.ZeroOutNotifiedAt = DateTime.UtcNow.AddMinutes(-5);
        context.SaveChanges();

        await Service(context).EvaluateZeroOut(playerId);

        _claims.Verify(x => x.Release(playerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A run that just finished is PAUSED, not out of funds: no claim release, no zero-out flag, no Telegram.</summary>
    [Fact]
    public async Task EvaluateZeroOut_SaysNothing_ForARunThatJustFinished()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context);
        SeedBetDebit(context, playerId);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        var enrollment = ReadEnrollment(context, playerId);
        enrollment.Enabled = false;
        enrollment.BetLimit = 3;
        enrollment.RunBetsPlaced = 3;
        context.SaveChanges();

        await Service(context).EvaluateZeroOut(playerId);

        ReadEnrollment(context, playerId).ZeroOutNotifiedAt.ShouldBeNull();
        _claims.Verify(x => x.Release(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _telegram.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&(FullyQualifiedName~RewardGameConsumerTests|FullyQualifiedName~RefundInvalidBetConsumerTests|FullyQualifiedName~OrbitBetSettlementServiceTests)"`
Expected: the reward-order / run-rules / refund-slot tests FAIL (never called); `EvaluateZeroOut_SoloType_ComparesWithTheSteppedNextStake` FAILS (it takes the "recovered" branch against the 0.01 base and hits `ClearZeroOutFlag`'s ExecuteUpdate). `EvaluateZeroOut_SaysNothing_ForARunThatJustFinished`, `ARoundWithoutAutoBets_...`, `AManualOrbitBetRefund_...` and `AFailedOrbitRefund_...` already pass (the existing `!Enabled` guard / no call) — they pin behaviour.

- [ ] **Step 3: Implement**

`RewardGameConsumer.cs`:

1. No-winners exit — insert before l.77 (`await EvaluateOrbitFloors(game, orbitSettlement);`):

```csharp
                // Run limits (specs/new-options-for-orbit.md §6.6): the next stake and the run end FIRST — zero-out below
                // compares the balance with the NEXT stake.
                await ApplyOrbitRunRules(game, scope.ServiceProvider);
```

2. Normal exit — insert before l.267 (`await EvaluateOrbitFloors(game, orbitSettlement);`):

```csharp
            // Bet statuses are written; the run rules go BEFORE the stop rules (zero-out compares with the next stake).
            await ApplyOrbitRunRules(game, scope.ServiceProvider);
```

3. New helper, next to `ReleaseOrbitSoloLoop`:

```csharp
        /// <summary>
        /// Run limits & stake steps (specs/new-options-for-orbit.md §6.6): the next stake and the run-end pause for every
        /// Orbit AUTO bettor in the round, any auto-bet type. ⚠️ Runs BEFORE EvaluateOrbitFloors (zero-out compares the
        /// balance with the NEXT stake). Rounds with no auto bet pay nothing extra. Never allowed to fail settlement — the
        /// solo loop's limit check is the retry path for a missed run end.
        /// </summary>
        private static async Task ApplyOrbitRunRules(Game game, IServiceProvider services)
        {
            if (!game.Bets.Any(bet => bet.OrbitAutoPlaced))
                return;

            try
            {
                await services.GetRequiredService<IOrbitRunService>().ApplySettledRound(game.Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not apply the Orbit run rules after round {GameId} settled.", game.Id);
            }
        }
```

`RefundInvalidBetConsumer.cs` — replace the `if (bet.OrbitAutoPlaced) { ... }` block (l.111-121) with:

```csharp
                if (bet.OrbitAutoPlaced)
                {
                    // Run limits (specs/new-options-for-orbit.md §6.3): a refunded auto bet does not count toward the run.
                    // Its slot comes back (only into the run that took it — the bet's CreatedDate), then the run end is
                    // re-evaluated with this bet now Refunded. Never allowed to fail the refund that already moved money.
                    if (bet.PlayerId is { } autoPlayerId)
                    {
                        try
                        {
                            var runs = scope.ServiceProvider.GetRequiredService<IOrbitRunService>();
                            await runs.ReleaseSlot(autoPlayerId, bet.CreatedDate);
                            await runs.EvaluateRunEnd(autoPlayerId);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Could not give back the Orbit run slot of refunded bet {BetId}; the run counts it until its next START.", bet.Id);
                        }
                    }

                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<IOrbitSoloAutoBetService>().OnAutoRoundSettled(bet.GameId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Could not release the Orbit solo loop after refunding bet {BetId} in round {GameId}; the sweep will.", bet.Id, bet.GameId);
                    }
                }
```

(Keep the existing comment above the block.)

`OrbitBetSettlementService.cs` — replace l.230-242 (the `typeRow` projection through `requiredAmount`) with:

```csharp
            var typeRow = await _context.GameParameters.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == enrollment.GameParametersId, cancellationToken);

            if (typeRow is null)
                return;

            // The stake the NEXT auto round uses: Cosmic Sprint's RequiredAmount, or a solo enrolment's stepped / base
            // stake, clamped (specs/new-options-for-orbit.md §6.6). The same number the solo loop stakes.
            var requiredAmount = OrbitRunStake.Effective(enrollment, typeRow);
```

(The `!Enabled` early return at l.227 already keeps a just-finished run from being announced as "out of funds".)

- [ ] **Step 4: Run to verify they pass** — same command → PASS; whole unit script → green (including `Background/RewardWinnersHandlerTests`, whose provider has no `IOrbitRunService`: the lazy, guarded resolve keeps it green).

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs src/SpaceStroker.Tests/UnitTests/Background/RefundInvalidBetConsumerTests.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: settlement applies the step and run end before the stop rules, refunds give the slot back, zero-out judges the next stake (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Solo loop — effective stake, limit refusal before any round, `RunLimitReached`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSoloAutoBetService.cs` (ctor l.35-65, l.129-137, switch l.293-327)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitSoloAutoBetServiceTests.cs`

**Interfaces — Consumes:** `IOrbitRunService.EvaluateRunEnd` (Task 6), `OrbitRunStake.Effective`, `OrbitRunRules.ReachedLimit` (Task 1), `StakePlacementOutcome.RunLimitReached` (Task 2).
**Interfaces — Produces:** `OrbitSoloAutoBetService(..., IOrbitBetSettlementService settlement, IOrbitRunService runs)`.

- [ ] **Step 1: Write the failing tests**

In `OrbitSoloAutoBetServiceTests.cs`:

(a) Field: `private readonly Mock<IOrbitRunService> _runs = new();` (with the other mocks, l.43-54).

(b) `Service()` (l.1024-1026) passes it last:

```csharp
    private OrbitSoloAutoBetService Service()
        => new(NewContext(), _gameService.Object, _placer.Object, _ledger.Object, _coverage.Object, _accounts.Object,
            _hub.Object, _settings, _store.Object, _scheduler.Object, _claims.Object, _settlement.Object, _runs.Object);
```

(c) `SeedEnrollment` (l.1170-1200) — add a trailing `Action<OrbitEnrollment>? configure = null` parameter, and replace its `context.Add(new OrbitEnrollment { ... });` with:

```csharp
        var enrollment = new OrbitEnrollment
        {
            PlayerId = player.Id,
            Enabled = enabled,
            GameParametersId = typeId,
            Stake = stake,
            BoardMode = boardMode,
            Multiplier = multiplier,
            Choice = choice,
            AutoPickChoice = autoPick,
            SoloRoundId = soloRoundId,
            NextSoloRoundAt = nextSoloRoundAt,
            SoloLeaseUntil = soloLeaseUntil,
            CashOutFloor = cashOutFloor,
        };
        configure?.Invoke(enrollment);
        context.Add(enrollment);
```

(d) New tests (add a section before `// ---- fixture` / the helpers):

```csharp
    // ---------------------------------------------------------------- run limits & stake steps (specs/new-options-for-orbit.md §6.5)

    [Fact]
    public async Task UsesTheSteppedStake_ForCoverageAndPlacement()
    {
        var typeId = SeedHyperRoll();
        var playerId = SeedEnrollment(typeId, configure: e => { e.StakeStepsEnabled = true; e.RunNextStake = 0.04m; });
        SetupOpenSoloBoard(SeedBoard(typeId, (2m, "able"), (4m, "bold")));

        await Service().RunOnce(playerId);

        _coverage.Verify(x => x.EvaluateAsync(It.Is<HouseEdgeNewBet>(b => b.Amount == 0.04m), It.IsAny<CancellationToken>()), Times.Once);
        _placer.Verify(x => x.Place(It.Is<StakePlacement>(p => p.Stake == 0.04m)), Times.Once);
    }

    /// <summary>
    /// ⚠️ A compounded stake above MaximumStake is CLAMPED and play continues — StakeIsWithinBounds would otherwise idle
    /// the loop silently (no reschedule, no push).
    /// </summary>
    [Fact]
    public async Task ClampsASteppedStakeAboveTheMaximum_AndKeepsPlaying()
    {
        var typeId = SeedHyperRoll(maximumStake: 1m);
        var playerId = SeedEnrollment(typeId, configure: e => { e.StakeStepsEnabled = true; e.RunNextStake = 5m; });
        SetupOpenSoloBoard(SeedBoard(typeId, (2m, "able"), (4m, "bold")));

        await Service().RunOnce(playerId);

        _placer.Verify(x => x.Place(It.Is<StakePlacement>(p => p.Stake == 1m)), Times.Once);
    }

    /// <summary>Balance short of the NEXT stake ⇒ the shipped NEEDS MONEY path; the run (counter, next stake) is untouched.</summary>
    [Fact]
    public async Task NeedsMoneyForTheSteppedStake_OpensNothing_AndKeepsTheRun()
    {
        var typeId = SeedHyperRoll();
        var playerId = SeedEnrollment(typeId, configure: e =>
        {
            e.StakeStepsEnabled = true;
            e.RunNextStake = 0.04m;
            e.BetLimit = 5;
            e.RunBetsPlaced = 2;
        });
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0.03m);

        await Service().RunOnce(playerId);

        VerifyNothingOpened();
        _scheduler.Verify(x => x.Schedule(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
        _runs.VerifyNoOtherCalls();
    }

    /// <summary>A full run is refused BEFORE the lease and before any round exists — no unbet board/duel shell.</summary>
    [Fact]
    public async Task LimitReached_TakesNoLease_OpensNothing_AndEvaluatesTheRunEnd()
    {
        var typeId = SeedHyperRoll();
        var playerId = SeedEnrollment(typeId, configure: e => { e.BetLimit = 3; e.RunBetsPlaced = 3; });

        await Service().RunOnce(playerId);

        VerifyNothingOpened();
        _store.Verify(x => x.TryLease(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _runs.Verify(x => x.EvaluateRunEnd(playerId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>The claim refused the slot (a race with the static check): hand the untouched round back, no reschedule.</summary>
    [Fact]
    public async Task RunLimitReachedAtTheClaim_HandsTheRoundBack_WithoutRescheduling()
    {
        var (playerId, boardId) = SeedHappyHyperRoll(StakePlacementOutcome.RunLimitReached);

        await Service().RunOnce(playerId);

        _store.Verify(x => x.ClearRound(playerId, boardId, null, It.IsAny<CancellationToken>()), Times.Once);
        _scheduler.Verify(x => x.Schedule(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
        _runs.Verify(x => x.EvaluateRunEnd(playerId, null, It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests --filter "FullyQualifiedName~UnitTests&FullyQualifiedName~OrbitSoloAutoBetServiceTests"`
Expected: build FAILS (no 13-argument constructor).

- [ ] **Step 3: Implement** in `OrbitSoloAutoBetService.cs`

1. Ctor: add field `private readonly IOrbitRunService _runs;`, trailing parameter `IOrbitRunService runs`, and `_runs = runs;`.

2. Replace l.129-131 (the two-line "Static refusals ..." comment and `var stake = enrollment.Stake ?? parameters.RequiredAmount;`) with the block below — it re-states that comment, so do not keep the old copy:

```csharp
            // ---- run limit (specs/new-options-for-orbit.md §6.5) ----
            // A static refusal BEFORE the lease: refusing only at the claim would leave an opened, unbet board/duel shell.
            // The claim's conditional increment (OrbitStakePlacer) stays the hard guarantee. No reschedule — the run is over;
            // EvaluateRunEnd pauses it, and this is the retry path when settlement's call was missed.
            if (OrbitRunRules.ReachedLimit(enrollment))
            {
                Log.Information("Orbit solo loop for player {PlayerId}: the run reached its {Limit}-bet limit; not playing.",
                    playerId, enrollment.BetLimit);
                await _runs.EvaluateRunEnd(playerId, null, cancellationToken);
                return;
            }

            // Static refusals that no amount of waiting fixes — checked BEFORE the lease, so they write nothing and
            // leave no shell. A settings change (or deposit) re-triggers the loop.
            // The EFFECTIVE stake: base, or the run's stepped next stake, CLAMPED to [RequiredAmount, MaximumStake] — so
            // StakeIsWithinBounds only trips on a null ceiling (fail closed) or a config change, never on a compounded stake.
            var stake = OrbitRunStake.Effective(enrollment, parameters);
```

(The funds check l.170-178, the coverage pre-check l.226 and the placer call l.287-289 already use the local `stake`; NEEDS MONEY keeps the run state because nothing here writes it.)

3. Outcome switch — add after the `case StakePlacementOutcome.InsufficientFunds:` block (l.300-303):

```csharp
                    case StakePlacementOutcome.RunLimitReached:
                        // The claim refused the slot (the run filled, or the player paused, since the static check). The
                        // placer touched nothing; SetRound already dropped the lease. Hand the round back (its unbet shell
                        // is removed by the board cleanup), no reschedule, and let the run end be evaluated.
                        await _store.ClearRound(playerId, roundId, null, cancellationToken);
                        await _runs.EvaluateRunEnd(playerId, null, cancellationToken);
                        break;
```

(`SpaceStroker.Domain.Orbit.Services` is already imported.)

- [ ] **Step 4: Run to verify they pass** — same command → PASS (all pre-existing loop tests too: `OrbitRunStake.Effective` with no `RunNextStake` equals `Stake ?? RequiredAmount` clamped, and their seeded stakes are in bounds); whole unit script → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSoloAutoBetService.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitSoloAutoBetServiceTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: the solo loop stakes the clamped stepped stake, refuses a full run before opening a round, and hands a refused round back (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Integration — the run SQL on Postgres (`OrbitRunStoreSqlTests`)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/OrbitRunStoreSqlTests.cs`

**Interfaces — Consumes:** `IOrbitRunStore` (Task 2), `IOrbitStakePlacer`, fixture helpers `InsertGameParametersAsync`, `SeedOrbitEnrollmentAsync`, `GetOrbitEnrollmentAsync`, `GetOrbitLedgerAsync`, `GetOrbitBalanceColumnAsync`, `CreateConsumersScope`, `DisableAllOrbitEnrollmentsAsync`; `IntegrationGameParameters.HouseEdge`.

- [ ] **Step 1: Write the tests**

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Domain.Orbit.ValueObjects;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Orbit;
using SpaceStroker.Tests.Integration.Harness;
using Xunit;

namespace SpaceStroker.Tests.Integration
{
    /// <summary>
    /// The run-limit SQL (<see cref="OrbitRunStore"/>, specs/new-options-for-orbit.md §6.3/§6.6) on REAL Postgres. Every
    /// method is one conditional ExecuteUpdate whose WHERE is the guarantee; the unit suite's in-memory fake cannot run
    /// ExecuteUpdate or roll a transaction back, so these are the only tests of the statements Npgsql actually sends —
    /// including the placer's rollback when the claim refuses the slot.
    ///
    /// ⚠️ Isolation from the live loop: enrolments sit on a solo-shaped type with AllowOrbit = FALSE (and 0 balance where
    /// the placer is not involved), so neither the solo sweep nor a tick ever touches their rows.
    /// </summary>
    [Collection(IntegrationCollection.Name)]
    public class OrbitRunStoreSqlTests : IAsyncLifetime
    {
        private readonly IntegrationTestFixture _fixture;

        public OrbitRunStoreSqlTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        public Task InitializeAsync() => _fixture.DisableAllOrbitEnrollmentsAsync();

        public Task DisposeAsync() => _fixture.DisableAllOrbitEnrollmentsAsync();

        [Fact]
        public async Task TryTakeSlot_CountsUpToTheLimit_AndNeverPast()
        {
            var playerId = await SeedAsync(betLimit: 2);

            (await Store(s => s.TryTakeSlot(playerId))).ShouldBeTrue();
            (await Store(s => s.TryTakeSlot(playerId))).ShouldBeTrue();
            (await Store(s => s.TryTakeSlot(playerId))).ShouldBeFalse("the third claim of a 2-bet run is refused.");

            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(2);
        }

        [Fact]
        public async Task TryTakeSlot_WithNoLimit_AlwaysCounts_ButNeverForAPausedRun()
        {
            var playerId = await SeedAsync(betLimit: null);

            for (var i = 0; i < 3; i++)
                (await Store(s => s.TryTakeSlot(playerId))).ShouldBeTrue();

            await SetEnabledAsync(playerId, false);
            (await Store(s => s.TryTakeSlot(playerId))).ShouldBeFalse("a paused enrolment takes no slot.");

            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(3);
        }

        /// <summary>The row lock + READ COMMITTED re-check: ten racing claims on a 3-bet run take exactly three slots.</summary>
        [Fact]
        public async Task TryTakeSlot_ConcurrentClaims_NeverExceedTheLimit()
        {
            var playerId = await SeedAsync(betLimit: 3);

            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Store(s => s.TryTakeSlot(playerId))));

            results.Count(x => x).ShouldBe(3);
            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(3);
        }

        [Fact]
        public async Task ReleaseSlot_NeverBelowZero_AndNeverAcrossRuns()
        {
            var runStart = Ms(DateTime.UtcNow.AddMinutes(-10));
            var playerId = await SeedAsync(betLimit: 5, runStartedAt: runStart, runBetsPlaced: 1);

            (await Store(s => s.ReleaseSlot(playerId, runStart.AddMinutes(-1))))
                .ShouldBeFalse("a slot taken before this run began belongs to an earlier run.");
            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(1);

            (await Store(s => s.ReleaseSlot(playerId, runStart.AddMinutes(1)))).ShouldBeTrue();
            (await Store(s => s.ReleaseSlot(playerId, runStart.AddMinutes(2)))).ShouldBeFalse("never below zero.");

            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(0);
        }

        [Fact]
        public async Task SetNextStake_OnlyForABetOfTheCurrentRun()
        {
            var runStart = Ms(DateTime.UtcNow.AddMinutes(-10));
            var playerId = await SeedAsync(betLimit: null, runStartedAt: runStart);

            (await Store(s => s.SetNextStake(playerId, 0.02m, runStart.AddMinutes(-1)))).ShouldBeFalse();
            (await EnrollmentAsync(playerId)).RunNextStake.ShouldBeNull();

            (await Store(s => s.SetNextStake(playerId, 0.02m, runStart.AddSeconds(1)))).ShouldBeTrue();
            (await EnrollmentAsync(playerId)).RunNextStake.ShouldBe(0.02m);
        }

        [Fact]
        public async Task PauseFinishedRun_PausesOnlyTheRunItWasAskedAbout()
        {
            var runStart = Ms(DateTime.UtcNow.AddMinutes(-10));
            var playerId = await SeedAsync(betLimit: 2, runStartedAt: runStart, runBetsPlaced: 2);

            (await Store(s => s.PauseFinishedRun(playerId, runStart.AddSeconds(1), DateTime.UtcNow)))
                .ShouldBeFalse("a stale evaluation never pauses a restarted run.");
            (await EnrollmentAsync(playerId)).Enabled.ShouldBeTrue();

            (await Store(s => s.PauseFinishedRun(playerId, runStart, DateTime.UtcNow))).ShouldBeTrue();
            var stored = await EnrollmentAsync(playerId);
            stored.Enabled.ShouldBeFalse();
            stored.WaitingForHouseSince.ShouldBeNull();

            (await Store(s => s.PauseFinishedRun(playerId, runStart, DateTime.UtcNow))).ShouldBeFalse("already paused.");
        }

        /// <summary>
        /// The unit suite cannot prove this half (in-memory EF ignores rollbacks): a refused slot rolls back the BetDebit
        /// claim AND the TryDebit, so the ledger and the balance column are exactly as before and the invariant holds.
        /// </summary>
        [Fact]
        public async Task Placer_AtTheLimit_RefusesTheClaim_AndRollsTheDebitBack()
        {
            var typeId = await IsolatedTypeAsync();
            var playerId = await _fixture.SeedOrbitEnrollmentAsync(NewWallet(), typeId, 1m, enabled: true);
            await SetRunAsync(playerId, betLimit: 1, runStartedAt: Ms(DateTime.UtcNow.AddMinutes(-1)), runBetsPlaced: 1);
            var gameId = await SeedRoundAsync(typeId);

            StakePlacementResult result;
            using (var scope = _fixture.CreateConsumersScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
                var player = await db.Players.AsNoTracking().SingleAsync(x => x.Id == playerId);

                result = await scope.ServiceProvider.GetRequiredService<IOrbitStakePlacer>().Place(new StakePlacement(
                    gameId, player, 0.5m, "banana", new string('c', 64), 0,
                    OrbitAutoPlaced: true, PlacementRequestId: null, Memo: $"orbit:{gameId:N}:{playerId:N}"));
            }

            result.Outcome.ShouldBe(StakePlacementOutcome.RunLimitReached);

            var ledger = await _fixture.GetOrbitLedgerAsync(playerId);
            ledger.ShouldNotContain(x => x.Type == OrbitTransactionType.BetDebit, "the claim row rolled back with the refused slot.");
            (await _fixture.GetOrbitBalanceColumnAsync(playerId)).ShouldBe(1m, "TryDebit rolled back too.");
            (await _fixture.GetOrbitBalanceColumnAsync(playerId)).ShouldBe(ledger.Sum(x => x.Amount));
            (await EnrollmentAsync(playerId)).RunBetsPlaced.ShouldBe(1);
        }

        // ---- helpers ----

        private static string NewWallet() => new Wallet(WordCount.TwentyFour, WordList.English).GetAccount(0).PublicKey.Key;

        /// <summary>Whole milliseconds, UTC — a value Postgres stores and returns unchanged.</summary>
        private static DateTime Ms(DateTime value)
            => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        /// <summary>A solo-shaped type the live loop ignores (AllowOrbit false), so nothing races the test's writes.</summary>
        private Task<Guid> IsolatedTypeAsync()
        {
            var parameters = IntegrationGameParameters.HouseEdge($"Orbit Run Store {Guid.NewGuid():N}", minimumPlayers: 8);
            parameters.AllowOrbit = false;
            return _fixture.InsertGameParametersAsync(parameters);
        }

        /// <summary>An ENABLED, unfunded enrolment on an isolated type, with the given run state.</summary>
        private async Task<Guid> SeedAsync(int? betLimit, DateTime? runStartedAt = null, int runBetsPlaced = 0)
        {
            var playerId = await _fixture.SeedOrbitEnrollmentAsync(NewWallet(), await IsolatedTypeAsync(), 0m, enabled: true);
            await SetRunAsync(playerId, betLimit, runStartedAt ?? Ms(DateTime.UtcNow.AddMinutes(-1)), runBetsPlaced);
            return playerId;
        }

        private async Task SetRunAsync(Guid playerId, int? betLimit, DateTime runStartedAt, int runBetsPlaced)
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            await db.OrbitEnrollments.Where(x => x.PlayerId == playerId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.BetLimit, betLimit)
                    .SetProperty(x => x.RunStartedAt, (DateTime?)runStartedAt)
                    .SetProperty(x => x.RunBetsPlaced, runBetsPlaced));
        }

        private async Task SetEnabledAsync(Guid playerId, bool enabled)
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            await db.OrbitEnrollments.Where(x => x.PlayerId == playerId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, enabled));
        }

        private async Task<Guid> SeedRoundAsync(Guid typeId)
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            var game = new Game(typeId);
            await db.Games.AddAsync(game);
            await db.SaveChangesAsync();
            return game.Id;
        }

        private async Task<OrbitEnrollment> EnrollmentAsync(Guid playerId)
            => (await _fixture.GetOrbitEnrollmentAsync(playerId))!;

        private async Task<T> Store<T>(Func<IOrbitRunStore, Task<T>> call)
        {
            using var scope = _fixture.CreateConsumersScope();
            return await call(scope.ServiceProvider.GetRequiredService<IOrbitRunStore>());
        }
    }
}
```

- [ ] **Step 2: Run** (Docker running)

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests.Integration --filter "FullyQualifiedName~OrbitRunStoreSqlTests"`
Expected: PASS (Tasks 1–2 implemented the SQL; the migration from Task 1 is applied by the Consumers host the fixture boots). If any fails, fix the store/placer (not the test) and re-run.

- [ ] **Step 3: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Tests.Integration/OrbitRunStoreSqlTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: prove the run-slot / next-stake / run-end SQL and the placer's rollback on Postgres (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Integration — the mandatory flows (Hyper Roll happy path, Cosmic Sprint "never more than N")

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/OrbitSoloAutoBetFlowTests.cs` (usings, `OrbitView` l.415-420, `AssertLedgerForAsync` l.480, new test)
- Modify: `src/SpaceStroker.Tests.Integration/OrbitAutoBetFlowTests.cs` (usings, helpers, new test)

**Interfaces — Consumes:** everything above, through the real hosts; `OrbitRunStake.Next` (Task 1) to compute the expected stakes from the actual outcomes.

- [ ] **Step 1: Write the tests**

`OrbitSoloAutoBetFlowTests.cs`:

(a) Usings — add `using SpaceStroker.Domain.Bets.ValueObjects;` and `using SpaceStroker.Domain.Orbit.Services;`.

(b) `OrbitView` — add the run fields:

```csharp
        private sealed class OrbitView
        {
            public bool Enabled { get; set; }
            public bool WaitingForHouse { get; set; }
            public Guid? CurrentAutoRoundId { get; set; }
            public int? BetLimit { get; set; }
            public int RunBetsPlaced { get; set; }
            public bool RunFinished { get; set; }
            public decimal? NextStake { get; set; }
        }
```

(c) `AssertLedgerForAsync` — add an optional stake (stepped bets stake more than the base):

```csharp
        private async Task AssertLedgerForAsync(Guid playerId, Guid gameId, Bet bet, bool won, decimal grossPayout, decimal? stake = null)
        {
            var ledger = await _fixture.GetOrbitLedgerAsync(playerId);

            var debit = ledger.Single(x => x.Type == OrbitTransactionType.BetDebit && x.GameId == gameId);
            debit.Amount.ShouldBe(-(stake ?? Stake));
```

(the rest of the method is unchanged).

(d) The test (after `HyperRoll_AutoBet_PlaysRoundsBackToBack`):

```csharp
        /// <summary>
        /// #208 — THE MANDATORY HAPPY PATH (specs/new-options-for-orbit.md §11.2.1, AC1/AC7/AC8/AC14): Hyper Roll, base
        /// 0.01, PLAY 3 BETS, CHANGE STAKE on, AFTER A LOSS raise 100 %, AFTER A WIN back to base. Exactly three auto bets,
        /// each stake following the previous bet's ACTUAL outcome, then Orbit pauses itself with the money still in Orbit
        /// (no cash-out) and GET /orbit reports the finished run.
        /// </summary>
        [Fact]
        public async Task HyperRoll_AutoBet_BetLimitWithLossSteps_PausesAfterN()
        {
            var typeId = await _fixture.InsertGameParametersAsync(HyperRollType());
            // 1 SOL: far above base × (1 + 2 + 4) = 0.07, and no threshold/floor is set.
            var (playerId, client) = await SeedPausedAsync(typeId, 1m);

            await PatchOkAsync(client, new
            {
                stake = Stake, boardMode = "AVERAGE", multiplier = 3m,
                betLimit = 3, stakeStepsEnabled = true, onLossIncreasePercent = 100m,
            });
            await PatchOkAsync(client, new { enabled = true });

            // 1. The run ends by itself: Orbit pauses once the 3rd bet settles.
            var paused = await _fixture.WaitUntilAsync(async () => !(await _fixture.GetOrbitEnrollmentAsync(playerId))!.Enabled,
                TimeSpan.FromSeconds(420));
            paused.ShouldBeTrue("Orbit never paused after its 3-bet run.");

            var rounds = await GetAutoRoundsAsync(playerId, typeId);
            rounds.Count.ShouldBe(3, "exactly three auto bets in the run.");
            foreach (var (_, game) in rounds)
                await _fixture.WaitForStatusAsync(game.Id, GameStatus.RewardForWinnersCompleted, RoundPatience);
            rounds = await GetAutoRoundsAsync(playerId, typeId); // re-read the settled bet statuses

            // 2. Each stake follows the steps, computed from the ACTUAL outcomes: loss ⇒ ×2, win ⇒ back to base.
            var rules = new OrbitEnrollment { Stake = Stake, StakeStepsEnabled = true, OnLossIncreasePercent = 100m };
            var parameters = new GameParameters { IsHouseEdge = true, RequiredAmount = Stake, MaximumStake = 1m };

            rounds[0].Bet.Amount.ShouldBe(Stake, "the first bet of a run is the base stake.");
            for (var i = 1; i < rounds.Count; i++)
            {
                var previous = rounds[i - 1].Bet;
                var won = previous.Status == BetStatus.Rewarded;
                rounds[i].Bet.Amount.ShouldBe(OrbitRunStake.Next(previous.Amount, won, rules, parameters),
                    $"bet {i + 1} follows bet {i}'s {(won ? "win" : "loss")}.");
            }

            // 3. No 4th bet — not after the gap, not after a sweep window.
            await Task.Delay(TimeSpan.FromSeconds(Gap + 65));
            (await GetAutoRoundsAsync(playerId, typeId)).Count.ShouldBe(3, "a finished run places nothing more.");
            (await GamesOfTypeAsync(typeId)).Count.ShouldBe(3, "and opens no unbet board either.");

            // 4. GET /orbit reports the finished run.
            var view = await GetOrbitAsync(client);
            view.Enabled.ShouldBeFalse();
            view.RunFinished.ShouldBeTrue();
            view.RunBetsPlaced.ShouldBe(3);
            view.BetLimit.ShouldBe(3);

            // 5. The money: every round's ledger, deposit − Σ stakes + Σ credits, no cash-out, the invariant.
            foreach (var (bet, game) in rounds)
                await AssertLedgerForAsync(playerId, game.Id, bet, bet.Status == BetStatus.Rewarded, bet.Amount * 3m, bet.Amount);

            var ledger = await _fixture.GetOrbitLedgerAsync(playerId);
            ledger.ShouldNotContain(x => x.Type == OrbitTransactionType.CashOut, "reaching the count pauses; it never cashes out.");
            var credits = ledger.Where(x => x.Type == OrbitTransactionType.WinCredit).Sum(x => x.Amount);
            var balance = await _fixture.GetOrbitBalanceColumnAsync(playerId);
            balance.ShouldBe(1m - rounds.Sum(x => x.Bet.Amount) + credits);
            balance.ShouldBe(ledger.Sum(x => x.Amount));
        }
```

`OrbitAutoBetFlowTests.cs`:

(a) Usings — add `using Microsoft.EntityFrameworkCore;`, `using SpaceStroker.Domain.Bets.Entities;`, `using SpaceStroker.Infrastructure.Data.Context;`.

(b) Helpers (after `OpenRoundAsync`; deliberately local, as this file's header explains):

```csharp
        // ---- run limits (specs/new-options-for-orbit.md §11.2.2) ----

        private static async Task PatchOkAsync(HttpClient client, object body)
        {
            var response = await client.PatchAsJsonAsync("/orbit/settings", body);
            response.IsSuccessStatusCode.ShouldBeTrue($"PATCH /orbit/settings refused: {await response.Content.ReadAsStringAsync()}");
        }

        private sealed class RunView
        {
            public bool Enabled { get; set; }
            public int RunBetsPlaced { get; set; }
            public bool RunFinished { get; set; }
        }

        private static async Task<RunView> GetOrbitAsync(HttpClient client)
        {
            var response = await client.GetAsync("/orbit");
            response.IsSuccessStatusCode.ShouldBeTrue($"GET /orbit failed: {await response.Content.ReadAsStringAsync()}");
            return (await IntegrationTestFixture.ReadEnvelopeAsync<RunView>(response)).Data!;
        }

        private async Task<List<Bet>> AutoBetsAsync(Guid playerId)
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            return await db.GameBets.AsNoTracking().Where(b => b.PlayerId == playerId && b.OrbitAutoPlaced).ToListAsync();
        }

        private async Task<int> RoundsOfTypeAsync(Guid parametersId)
        {
            using var scope = _fixture.CreateConsumersScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            return await db.Games.AsNoTracking().CountAsync(g => g.ParametersId == parametersId);
        }
```

(c) The test:

```csharp
        /// <summary>
        /// #208 — THE COMPLEX FLOW (specs/new-options-for-orbit.md §11.2.2, AC2): Cosmic Sprint opens the SUCCESSOR round —
        /// and the planner signs its stake — before the previous round settles, so a run of 2 is exactly where an (N+1)th
        /// bet could slip in. The claim's conditional increment must refuse it, and Orbit pauses once the 2nd bet settles.
        /// One funded player on a minimum-1 race: rounds roll over on their own.
        /// </summary>
        [Fact]
        public async Task CosmicSprint_AutoBet_BetLimit_NeverPlacesMoreThanN()
        {
            var parametersId = await SeedOrbitTypeAsync(minimumPlayers: 1);
            var wallet = NewWallet();
            var playerId = await _fixture.SeedOrbitEnrollmentAsync(wallet, parametersId, balance: 10m, enabled: false);

            using var client = _fixture.CreatePlayerClient();
            client.DefaultRequestHeaders.Add("X-Orbit-Session", _fixture.MintOrbitSession(wallet));

            // Paused → limit → START, so the run is reset exactly as a player's would be.
            await PatchOkAsync(client, new { betLimit = 2 });
            await PatchOkAsync(client, new { enabled = true });

            await OpenRoundAsync(parametersId);

            var paused = await _fixture.WaitUntilAsync(async () => !(await _fixture.GetOrbitEnrollmentAsync(playerId))!.Enabled,
                TimeSpan.FromSeconds(180));
            paused.ShouldBeTrue("Orbit never paused after its 2-bet run.");

            // A third round opened (the successor of the 2nd) and stays unbet: the run is full.
            (await _fixture.WaitUntilAsync(async () => await RoundsOfTypeAsync(parametersId) >= 3, Patience))
                .ShouldBeTrue("successor rounds keep opening after the run is full.");
            await Task.Delay(TimeSpan.FromSeconds(10));

            var autoBets = await AutoBetsAsync(playerId);
            autoBets.Count.ShouldBe(2, "never more than N bets in the run, even with successor rounds opening early.");
            autoBets.Select(b => b.GameId).Distinct().Count().ShouldBe(2, "one bet per round.");
            foreach (var bet in autoBets)
                await _fixture.WaitForStatusAsync(bet.GameId, GameStatus.RewardForWinnersCompleted, Patience);

            var view = await GetOrbitAsync(client);
            view.Enabled.ShouldBeFalse();
            view.RunFinished.ShouldBeTrue();
            view.RunBetsPlaced.ShouldBe(2);

            var ledger = await _fixture.GetOrbitLedgerAsync(playerId);
            ledger.Count(x => x.Type == OrbitTransactionType.BetDebit).ShouldBe(2, "no (N+1)th stake ever left custody.");
            ledger.ShouldNotContain(x => x.Type == OrbitTransactionType.CashOut);
            (await _fixture.GetOrbitBalanceColumnAsync(playerId)).ShouldBe(ledger.Sum(x => x.Amount));
        }
```

- [ ] **Step 2: Run the two tests** (Docker running)

Run: `dotnet test E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend/src/SpaceStroker.Tests.Integration --filter "FullyQualifiedName~HyperRoll_AutoBet_BetLimitWithLossSteps_PausesAfterN|FullyQualifiedName~CosmicSprint_AutoBet_BetLimit_NeverPlacesMoreThanN"`
Expected: PASS. If a test fails, debug the production code (superpowers:systematic-debugging), never weaken an assertion about the count, the stakes or the ledger.

- [ ] **Step 3: Run the WHOLE integration script**

Run: `cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend && cmd //c "scripts\run_integration_tests.bat"`
Expected: green. Watch the Orbit churn flakiness noted in memory (typer cap, Orbit enrolment churn): if a suite unrelated to Orbit fails, re-run that suite once to separate flake from regression, and report it either way.

- [ ] **Step 4: Run the whole unit script once more** — `cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend && cmd //c "scripts\run_tests.bat"` → green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend add src/SpaceStroker.Tests.Integration/OrbitSoloAutoBetFlowTests.cs src/SpaceStroker.Tests.Integration/OrbitAutoBetFlowTests.cs
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-backend commit -m "Orbit run limits: integration tests for the Hyper Roll stepped 3-bet run and Cosmic Sprint's never-more-than-N (#208)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Changed / added tests (for the end-of-work report)

- **New unit classes:** `Domain/Orbit/OrbitRunStakeTests`, `Domain/Orbit/OrbitRunRulesTests`, `Services/OrbitRunServiceTests`; new shared fake `Shared/InMemoryOrbitRunStore`.
- **Extended unit classes:** `Api/OrbitSettingsBindingTests`, `Services/OrbitStakePlacerTests` (fixture now seeds an enabled enrolment; 8 new tests), `Consumers/PlaceOrbitBetConsumerTests` (+2), `Services/OrbitBetPlacementServiceTests` (+1, `SeedEnrolledPlayers` params), `Handlers/Orbit/UpdateOrbitSettingsTests` (+14), `Handlers/Orbit/GetOrbitSoloSetupTests` (+6), `Services/OrbitSoloAutoBetServiceTests` (+5, `Service()`/`SeedEnrollment` helpers), `Services/OrbitBetSettlementServiceTests` (+2), `Consumers/RewardGameConsumerTests` (+5), `Background/RefundInvalidBetConsumerTests` (+4).
- **Constructor-only updates:** `Jobs/OrbitStakeReconciliationJobTests`, integration `OrbitStakeCompensationTests`, `OrbitStakeReconciliationTests`.
- **New integration class:** `OrbitRunStoreSqlTests` (7). **Extended:** `OrbitSoloAutoBetFlowTests` (+1, `OrbitView`, `AssertLedgerForAsync` stake param), `OrbitAutoBetFlowTests` (+1, local helpers).

## Self-review (spec coverage)

- §4.1–4.3 → Task 1. §5 (endpoint + constants) → Task 1. §6.1 → Task 1. §6.2 → Task 4. §6.3 (claim increment, `RunLimitReached`, give-backs from placer and refund) → Tasks 2, 7. §6.4 → Tasks 2 (consumer mapping), 3 (planner). §6.5 → Task 8. §6.6 (`ApplySettledRound`, `EvaluateRunEnd` with the settling-round and refunded-bet exclusions, conditional pause, claim release, error swallowing, call order) → Tasks 6, 7. §6.7 → Task 6. §6.8 → Task 5. §6.9 (unchanged paths) → asserted by the manual-wager tests (Tasks 2, 6) and the unchanged ledger checks (Tasks 9, 10). §10.1 rows 1–21 → Tasks 1–8. §11.1 → every task's tests (deviations 6–7 move two SQL cases to Task 9). §11.2 → Task 10 (both), plus Task 9.
- Type consistency checked: `IOrbitRunStore.{TryTakeSlot, ReleaseSlot, SetNextStake, PauseFinishedRun}`, `IOrbitRunService.{ApplySettledRound, EvaluateRunEnd(Guid, Guid? = null, CancellationToken = default), ReleaseSlot}`, `StakePlacementOutcome.RunLimitReached`, `OrbitRunFinishedPush(GameTypeId, GameTypeName, BetsPlaced)`, `OrbitRunStake.{Effective, Next, Clamp}`, `OrbitRunRules.{ReachedLimit, IsValidIncreasePercent, MaxBetLimit, MaxIncreasePercent, IncreasePercentDecimals}` are used with the same names and signatures in every task.
