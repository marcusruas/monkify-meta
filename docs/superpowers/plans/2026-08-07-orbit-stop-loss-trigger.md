# Orbit Stop Loss Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Orbit's cash-out floor (the "stop loss") evaluate against a *settled* balance and stop the player entering new rounds instead of cashing them out while a bet is still riding.

**Architecture:** One new read — `ResolveSettledBalance` = ledger balance + the stakes of still-riding Orbit bets — becomes the comparison number for all three settlement-time stop rules. The floor additionally defers its cash-out while anything is riding, and the placement planner gains one *derived* eligibility condition (`settled > floor`) that **is** the tripped state, so nothing is stored and nothing needs clearing. A 5-minute Hangfire sweep is the only remaining trigger once a tripped player stops betting. The frontend gains one derived `'stop-loss'` status.

**Tech Stack:** .NET 8, EF Core (Postgres; in-memory for unit tests), MassTransit, Hangfire, xUnit + Shouldly + Moq, Vue 3 + TypeScript.

**Spec:** [`../../../specs/orbit-stop-loss-trigger.md`](../../../specs/orbit-stop-loss-trigger.md) — authoritative. **Trello:** [#154](https://trello.com/c/kCzYE9Bi).

## Global Constraints

- **Worktrees only.** All work happens in `E:\Monkify\.worktrees\orbit-stop-loss-trigger\<repo>`. The hubs (`E:\Monkify\<repo>`) stay on `main` — never `git checkout -b` / `git switch` there.
- **Branch:** `feature/orbit-stop-loss-trigger` in all three repos. Push after every commit. Never force-push, never skip hooks, never amend a pushed commit.
- **Unit tests:** run with `scripts/run_tests.bat` from the backend worktree. **Never** bare `dotnet test` — it pulls in the slow `ProductionGames` Monte Carlo suite.
- **Level = `Minor change`** ⇒ **no new integration test** is required and **no** new `features/*.md` doc. Unit tests are still mandatory.
- **No EF migration.** No entity change, no `SpaceStrokerDbContext.OnModelCreating` edit. If you think you need one, stop — the design is wrong.
- **No `ExecuteUpdateAsync` in new code.** Everything added here must be exercisable on the EF in-memory provider, which is why the design avoids it. `ExecuteUpdateAsync` throws in-memory and would leave the new logic with zero unit coverage.
- **The settled balance is a DECISION INPUT ONLY.** Never pass it as a cash-out amount. `OrbitCashOutReserver` keeps sizing from `ResolveBalance`.
- **Mock secrets only** in any committed config.
- **Host split:** the sweep is background work — `SpaceStroker.Consumers` only, never `SpaceStroker.Api`.
- ⚠️ **Base drift (spec §0):** `OrbitEnrollment.Stopped` **does not exist** on this base — `Enabled` is the single play/pause switch. **Nothing in this plan writes `Enabled`.** The frontend `OrbitStatus` union is `'signed-out' | 'off' | 'unfunded' | 'running'`.

---

## File Structure

**`monkify-backend`**

| File | Responsibility |
| --- | --- |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSettledBalance.cs` *(create)* | The `(Ledger, RidingStake, RidingCount)` value object + `Settled` / `HasRidingBets`. |
| `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs` *(modify)* | Declares `ResolveSettledBalance`. |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs` *(modify)* | Implements it; gains a `GeneralSettings` dependency for the staleness window. |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` *(modify)* | `EvaluateFloor` (settled + deferral), `EvaluateThreshold` (settled), `EvaluateZeroOut` (settled). |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` *(modify)* | The stop-loss gate in `ResolveCandidates`; gains an `IOrbitLedgerService` dependency. |
| `src/SpaceStroker.Infrastructure/Jobs/OrbitStopRuleSweepJob.cs` *(create)* | The 5-minute re-drive. |
| `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs` *(modify)* | `RidingBetStaleAfterMinutes` (default 60). |
| `src/SpaceStroker.Consumers/Program.cs` *(modify)* | DI registration + recurring-job schedule. |

**`monkify-front`**

| File | Responsibility |
| --- | --- |
| `src/types/orbit.ts` *(modify)* | `'stop-loss'` added to `OrbitStatus`. |
| `src/hooks/useOrbit.ts` *(modify)* | `isStoppedByFloor` derivation + its place in `orbitStatus`. |
| `src/components/orbit/OrbitStatusTag.vue` *(modify)* | The `STOP LOSS` label + red dot. |
| `src/pages/OrbitPage.vue` *(modify)* | One explanatory line under the floor field. |

**Tests (`monkify-backend/src/SpaceStroker.Tests`)**

| File | Responsibility |
| --- | --- |
| `UnitTests/Services/OrbitLedgerServiceTests.cs` *(modify)* | `ResolveSettledBalance` cases. |
| `UnitTests/Services/OrbitBetSettlementServiceTests.cs` *(modify)* | Floor deferral, saving win, threshold, zero-out. |
| `UnitTests/Services/OrbitBetPlacementServiceTests.cs` *(modify)* | The planner gate. |
| `UnitTests/Jobs/OrbitStopRuleSweepJobTests.cs` *(create)* | The sweep. |

---

## Task 1: `OrbitSettledBalance` + `ResolveSettledBalance`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSettledBalance.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs`
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `OrbitSettledBalance(decimal Ledger, decimal RidingStake, int RidingCount)` with `decimal Settled` and `bool HasRidingBets`; `Task<OrbitSettledBalance> IOrbitLedgerService.ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default)`; `int OrbitSettings.RidingBetStaleAfterMinutes`. Tasks 2–5 all depend on these exact names.

- [ ] **Step 1: Add the config value**

In `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`, after `CashOutSweepStaleMinutes`:

```csharp
        /// <summary>
        /// How long an unresolved Orbit bet may keep counting as "riding" before the stop rules stop
        /// waiting for it.
        ///
        /// A bet is normally riding for seconds. This bound exists for the pathological case: a round
        /// can sit in WaitingBets for hours looking perfectly healthy (Trello #153), and without a bound
        /// the stop loss would wait on it forever — holding money the player explicitly asked to have
        /// sent home.
        ///
        /// ⚠️ Accepted consequence: if such a round eventually DOES settle, its win credit or refund
        /// lands in a balance that has already been emptied. That is strictly better than never cashing
        /// the player out at all.
        ///
        /// 0 disables the ageing (every unresolved bet counts as riding forever) — never do that in a
        /// deployed environment.
        /// </summary>
        public int RidingBetStaleAfterMinutes { get; set; } = 60;
```

- [ ] **Step 2: Create the value object**

Create `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitSettledBalance.cs`:

```csharp
namespace SpaceStroker.Infrastructure.Services.Orbit
{
    /// <summary>
    /// A player's Orbit position split into money they HAVE and money that is currently STAKED.
    ///
    /// The split exists because the two numbers answer different questions and the stop rules need
    /// both: <see cref="Settled"/> decides WHETHER a rule has been crossed, and <see cref="Ledger"/> is
    /// the only number that may ever size a transfer — a stake riding in a live round is not ours to
    /// send.
    /// </summary>
    /// <param name="Ledger">The authoritative ledger balance (what ResolveBalance returns).</param>
    /// <param name="RidingStake">The summed stake of every Orbit bet still riding.</param>
    /// <param name="RidingCount">
    /// How many bets that is. Carried separately rather than inferred from RidingStake > 0, so a
    /// zero-amount bet could never read as "nothing riding".
    /// </param>
    public readonly record struct OrbitSettledBalance(decimal Ledger, decimal RidingStake, int RidingCount)
    {
        /// <summary>What the player is really worth right now — the stop rules' comparison number.</summary>
        public decimal Settled => Ledger + RidingStake;

        public bool HasRidingBets => RidingCount > 0;
    }
}
```

- [ ] **Step 3: Declare it on the interface**

In `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs`, after `ResolveBalance`:

```csharp
        /// <summary>
        /// The player's balance SPLIT into settled money and money currently riding in an unresolved
        /// round.
        ///
        /// ⚠️ Exists because a stake leaves the balance the instant a bet is placed, and the round that
        /// stake is riding in opens BEFORE the previous round settles (RunGameConsumer creates the
        /// successor round before publishing RewardGameEvent). So every settlement-time stop rule that
        /// reads the raw ledger reads it one stake too low. Compare against Settled; transfer only
        /// Ledger.
        /// </summary>
        Task<OrbitSettledBalance> ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Write the failing tests**

Append to `src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs`. Add these usings at the top of the file if absent: `using SpaceStroker.Domain.Bets.Entities;`, `using SpaceStroker.Domain.Bets.ValueObjects;`, `using SpaceStroker.Domain.Configs.Entities;`, `using SpaceStroker.Domain.Games.Entities;`, `using SpaceStroker.Domain.Games.ValueObjects;`.

```csharp
    private static GeneralSettings LedgerSettings(int staleAfterMinutes = 60) => new()
    {
        Operator = new OperatorSettings
        {
            Token = new TokenSettings
            {
                Orbit = new OrbitSettings { RidingBetStaleAfterMinutes = staleAfterMinutes },
            },
        },
    };

    /// <summary>
    /// A bet in a game at <paramref name="status"/>, placed <paramref name="minutesAgo"/> ago.
    /// The GAME's status is what decides "riding" — see the ...IgnoresALosingBet... test for why.
    /// </summary>
    private static Bet OrbitBet(Guid playerId, decimal amount, GameStatus status,
                                bool fundedByOrbit = true, int minutesAgo = 0)
    {
        var game = new Game { Id = Guid.NewGuid(), Status = status };

        return new Bet
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Game = game,
            PlayerId = playerId,
            Amount = amount,
            FundedByOrbit = fundedByOrbit,
            Choice = "abc",
            Seed = "seed",
            Status = BetStatus.Made,
            CreatedDate = DateTime.UtcNow.AddMinutes(-minutesAgo),
        };
    }

    [Fact]
    public async Task ResolveSettledBalance_AddsTheStakeOfARidingBet()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.InProgress));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings()).ResolveSettledBalance(playerId);

        result.Ledger.ShouldBe(5m);
        result.RidingStake.ShouldBe(1m);
        result.RidingCount.ShouldBe(1);
        result.Settled.ShouldBe(6m);
        result.HasRidingBets.ShouldBeTrue();
    }

    [Fact]
    public async Task ResolveSettledBalance_IgnoresASettledRound()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.RewardForWinnersCompleted));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings()).ResolveSettledBalance(playerId);

        result.RidingCount.ShouldBe(0);
        result.Settled.ShouldBe(5m);
        result.HasRidingBets.ShouldBeFalse();
    }

    /// <summary>
    /// ⚠️ THE REGRESSION THAT WOULD BREAK THE FLOOR FOREVER. A LOSING bet is never transitioned — it
    /// stays BetStatus.Made for all time. Keying "riding" on bet status would therefore treat every
    /// past loss as still riding, the settled balance would grow without bound, and the floor would
    /// never fire again for anyone who has ever lost.
    /// </summary>
    [Fact]
    public async Task ResolveSettledBalance_IgnoresALosingBetThatStaysMade()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });

            // Status stays Made — the round is over and this bet simply lost.
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.RewardForWinnersCompleted));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings()).ResolveSettledBalance(playerId);

        result.RidingCount.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveSettledBalance_IgnoresABetOlderThanTheStalenessWindow()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });

            // A jammed round: still WaitingBets, but placed 90 minutes ago.
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.WaitingBets, minutesAgo: 90));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings(staleAfterMinutes: 60))
            .ResolveSettledBalance(playerId);

        result.RidingCount.ShouldBe(0);
        result.Settled.ShouldBe(5m);
    }

    [Fact]
    public async Task ResolveSettledBalance_IgnoresBetsNotFundedByOrbitAndBetsOfAnotherPlayer()
    {
        var playerId = Guid.NewGuid();
        var otherPlayerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.InProgress, fundedByOrbit: false));
            seed.GameBets.Add(OrbitBet(otherPlayerId, 1m, GameStatus.InProgress));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings()).ResolveSettledBalance(playerId);

        result.RidingCount.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveSettledBalance_IgnoresAnErroredOrAbruptlyEndedGame()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 5m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 5m });
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.ErrorWhenProcessingRewards));
            seed.GameBets.Add(OrbitBet(playerId, 1m, GameStatus.GameEndedAbruptely));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var result = await new OrbitLedgerService(context, Accounts(), LedgerSettings()).ResolveSettledBalance(playerId);

        result.RidingCount.ShouldBe(0);
    }
```

- [ ] **Step 5: Run the tests to verify they fail**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: **compile failure** — `OrbitLedgerService` has no 3-argument constructor and `IOrbitLedgerService` has no `ResolveSettledBalance`.

- [ ] **Step 6: Implement it**

In `OrbitLedgerService.cs` — add usings `using SpaceStroker.Domain.Configs.Entities;` and `using SpaceStroker.Domain.Games.ValueObjects;`, then widen the constructor:

```csharp
        public OrbitLedgerService(SpaceStrokerDbContext context, IOrbitAccountResolver accounts, GeneralSettings settings)
        {
            _context = context;
            _accounts = accounts;
            _settings = settings;
        }

        private readonly SpaceStrokerDbContext _context;
        private readonly IOrbitAccountResolver _accounts;
        private readonly GeneralSettings _settings;
```

Then add, directly after `ResolveBalance`:

```csharp
        /// <inheritdoc />
        public async Task<OrbitSettledBalance> ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default)
        {
            // Reuse ResolveBalance rather than re-summing: it owns the REPEATABLE READ snapshot and the
            // guarded self-heal. Duplicating that read is exactly how the drift bug it was written to
            // fix comes back.
            var ledger = await ResolveBalance(playerId, cancellationToken);

            var staleAfterMinutes = _settings.Operator.Token.Orbit.RidingBetStaleAfterMinutes;
            var staleBefore = DateTime.UtcNow.AddMinutes(-staleAfterMinutes);

            // ⚠️ The terminal statuses are a DENY-LIST of three, deliberately, not `< Completed`.
            // GameStatus is an ordered enum in which ErrorWhenProcessingRewards and GameEndedAbruptely
            // sort AFTER RewardForWinnersCompleted, so a comparison would read them as settled only by
            // accident of ordering — and inserting a new member anywhere would silently change what
            // this means.
            //
            // ⚠️ Riding is decided by GAME status, never BET status: a losing bet stays
            // BetStatus.Made forever, so a bet-status test would treat every past loss as riding.
            var riding = await _context.GameBets
                .Where(bet => bet.PlayerId == playerId
                           && bet.FundedByOrbit
                           && (staleAfterMinutes <= 0 || bet.CreatedDate > staleBefore)
                           && bet.Game.Status != GameStatus.RewardForWinnersCompleted
                           && bet.Game.Status != GameStatus.ErrorWhenProcessingRewards
                           && bet.Game.Status != GameStatus.GameEndedAbruptely)
                .Select(bet => bet.Amount)
                .ToListAsync(cancellationToken);

            return new OrbitSettledBalance(ledger, riding.Sum(), riding.Count);
        }
```

- [ ] **Step 7: Fix the other construction sites**

`OrbitLedgerService` is DI-resolved everywhere in production code, so no production call site changes. Search for direct constructions in tests and fix each to pass a `GeneralSettings`:

```bash
grep -rn "new OrbitLedgerService(" --include=*.cs src/
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: PASS, whole suite green.

- [ ] **Step 9: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Add ResolveSettledBalance to the Orbit ledger service."
```

---

## Task 2: `EvaluateFloor` — settled comparison + deferral while riding

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`EvaluateFloor`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`

**Interfaces:**
- Consumes: `IOrbitLedgerService.ResolveSettledBalance` and `OrbitSettledBalance` (Task 1).
- Produces: no new public surface — `EvaluateFloor`'s signature is unchanged.

- [ ] **Step 1: Write the failing tests**

Append to `OrbitBetSettlementServiceTests.cs`. The existing `_ledger` field is a `Mock<IOrbitLedgerService>`.

```csharp
    /// <summary>The card's headline bug: the next round's stake is already out, so the RAW ledger
    /// reads below the floor while the player never actually reached it.</summary>
    [Fact]
    public async Task EvaluateFloor_DoesNothing_WhenOnlyARidingStakePutTheLedgerUnderTheFloor()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 2m);

        // Ledger 1 (a stake is already out for the next round), riding 1 → settled 2… but the floor is
        // 2 and "strictly above is still playing", so settled 2 is NOT above 2.
        // Use 1.5 riding so settled = 2.5 > 2.
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(1m, 1.5m, 1));

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(It.IsAny<Guid>(), It.IsAny<OrbitCashOutSource>(), It.IsAny<bool>(),
                                       It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateFloor_DefersTheCashOut_WhileABetIsRiding()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 2m);

        // Settled 2 (== floor, so crossed) but a bet is still riding — nothing may move.
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(1m, 1m, 1));

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(It.IsAny<Guid>(), It.IsAny<OrbitCashOutSource>(), It.IsAny<bool>(),
                                       It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateFloor_CashesOut_OnceNothingIsRiding()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 2m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(2m, 0m, 0));

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(playerId, OrbitCashOutSource.Floor, false, null, It.IsAny<CancellationToken>()),
                        Times.Once);
    }

    /// <summary>The stop is PROVISIONAL: a riding win that lifts the settled balance back over the
    /// floor cancels it, and there is no stored state to clear because the trip was never stored.</summary>
    [Fact]
    public async Task EvaluateFloor_DoesNothing_WhenTheRidingWinLiftedItBackAboveTheFloor()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 2m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(5m, 0m, 0));

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(It.IsAny<Guid>(), It.IsAny<OrbitCashOutSource>(), It.IsAny<bool>(),
                                       It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateFloor_DoesNothing_WhenTheLedgerIsEmptyEvenThoughTheFloorIsCrossed()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 2m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(0m, 0m, 0));

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(It.IsAny<Guid>(), It.IsAny<OrbitCashOutSource>(), It.IsAny<bool>(),
                                       It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Then **update the five existing `EvaluateFloor_*` tests** (`CashesOutWhatIsLeft_WhenTheBalanceFallsToIt`, `CashesOutWhenTheBalanceFallsBelowIt`, `LeavesThePlayerAlone_WhileTheyAreStillAboveIt`, `DoesNothing_WhenTheBalanceIsEmpty`, `DoesNothing_WhenNoFloorIsSet`) so their `_ledger` setup is `ResolveSettledBalance` returning `new OrbitSettledBalance(<the old balance>, 0m, 0)` instead of `ResolveBalance`. Their assertions do not change — behaviour with nothing riding is identical.

- [ ] **Step 2: Run to verify they fail**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: the new deferral tests FAIL (`Reserve` called once, expected never) because `EvaluateFloor` still reads `ResolveBalance`.

- [ ] **Step 3: Implement**

Replace the body of `EvaluateFloor` in `OrbitBetSettlementService.cs`:

```csharp
        /// <inheritdoc />
        public async Task EvaluateFloor(Guid playerId, CancellationToken cancellationToken = default)
        {
            var enrollment = await _context.OrbitEnrollments.FirstOrDefaultAsync(x => x.PlayerId == playerId, cancellationToken);

            if (enrollment?.CashOutFloor is not { } floor)
                return;

            var position = await _ledger.ResolveSettledBalance(playerId, cancellationToken);

            // Strictly ABOVE the floor is still playing. Semantics unchanged — but the number is now the
            // SETTLED one, so the NEXT round's stake (already debited by the time settlement runs, because
            // RunGameConsumer creates the successor round before publishing RewardGameEvent) no longer
            // trips a player who never actually reached their floor.
            if (position.Settled > floor)
                return;

            // ⚠️ TRIPPED, BUT NOT YET PAYABLE. A stake riding in a live round is not ours to send —
            // cashing out here is precisely what sent a player's money home mid-bet. The planner will not
            // enter them into anything new, so the wait is bounded by the round they are already in; and
            // if that round never resolves, the staleness window drops it out of `riding` and the
            // stop-rule sweep finishes the job.
            if (position.HasRidingBets)
            {
                Log.Information(
                    "Orbit floor {Floor} crossed for player {PlayerId} (settled {Settled}), but {Count} bet(s) are still riding — deferring the cash-out.",
                    floor, playerId, position.Settled, position.RidingCount);
                return;
            }

            // Nothing to send. A zero cash-out row would claim money moved when none did.
            if (position.Ledger <= 0)
                return;

            Log.Information("Orbit balance {Balance} fell to the cash-out floor {Floor} for player {PlayerId}.",
                position.Ledger, floor, playerId);

            // ⚠️ No requestedAmount: the reserver sizes from ResolveBalance itself, so the SETTLED number
            // can never become a transfer amount.
            await _cashOutReserver.Reserve(playerId, OrbitCashOutSource.Floor, enforceLimits: false, cancellationToken: cancellationToken);
        }
```

- [ ] **Step 4: Run to verify they pass**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Defer the Orbit floor cash-out while a bet is still riding."
```

---

## Task 3: `EvaluateThreshold` and `EvaluateZeroOut` — settled comparison

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`EvaluateThreshold` ~line 165, `EvaluateZeroOut` ~line 199)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`

**Interfaces:**
- Consumes: `ResolveSettledBalance` (Task 1).
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

```csharp
    /// <summary>Fires at the number the player set. The raw ledger is one stake low at settlement, so
    /// the take-profit used to fire ~a stake LATE.</summary>
    [Fact]
    public async Task EvaluateThreshold_FiresAtTheSettledBalance_WhenAStakeIsRiding()
    {
        using var context = NewContext();
        var (bet, playerId) = Seed(context, cashOutAtThreshold: 10m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(9m, 1m, 1));

        await Service(context).CreditWin(bet, 5m);

        // Immediate — NOT deferred. The threshold's promise is "as soon as a win takes it here".
        _cashOut.Verify(x => x.Reserve(playerId, OrbitCashOutSource.Threshold, false, null, It.IsAny<CancellationToken>()),
                        Times.Once);
    }

    [Fact]
    public async Task EvaluateZeroOut_DoesNotNotify_WhileTheSettledBalanceStillCoversTheStake()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context);

        // Ledger 0 because the stake is riding; settled 1 still covers the 1-unit stake.
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(0m, 1m, 1));

        await Service(context).EvaluateZeroOut(playerId);

        _messageBus.Verify(x => x.PublishEventAsync(It.IsAny<OrbitWinNotificationEvent>()), Times.Never);

        using var check = NewContext();
        check.OrbitEnrollments.First(x => x.PlayerId == playerId).ZeroOutNotifiedAt.ShouldBeNull();
    }
```

⚠️ The `Seed` helper must give the game type a `RequiredAmount` of `1m` for the zero-out test to mean anything — check it does, and set it if not.

- [ ] **Step 2: Run to verify they fail**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: FAIL — both still read `ResolveBalance`, which the mock no longer stubs (returns `0m`).

- [ ] **Step 3: Implement — `EvaluateThreshold`**

Replace lines 172-178 of `OrbitBetSettlementService.cs`:

```csharp
            // ⚠️ SETTLED, not the raw ledger. At settlement the next round's stake is already debited,
            // so the raw number is one stake low and this used to fire ~a stake LATE.
            //
            // ⚠️ Accepted consequence: this can now fire when the LEDGER is below the threshold, because
            // a riding stake made up the difference — and what goes home is the ledger balance, which is
            // less than the number on screen. That is the honest reading of "cash out on a win at X" (the
            // player's total position reached X) and it is what makes the rule fire at the number they
            // set. The riding bet, when it settles, credits into an emptied balance and may re-fire this.
            // Deliberately NOT deferred like the floor: this rule's shipped promise is "as soon as a win
            // takes it here".
            var position = await _ledger.ResolveSettledBalance(playerId, cancellationToken);

            if (position.Settled < threshold)
                return;

            Log.Information("Orbit balance {Balance} reached the cash-out threshold {Threshold} for player {PlayerId}.",
                position.Settled, threshold, playerId);
```

- [ ] **Step 4: Implement — `EvaluateZeroOut`**

Replace line 215 of `OrbitBetSettlementService.cs`:

```csharp
            // ⚠️ SETTLED: a stake riding in a live round left the ledger, and announcing "Orbit has
            // stopped betting" over money that is currently in play is simply false. Worse, ZeroOutNotifiedAt
            // is a once-per-episode flag, so a false fire SUPPRESSES the real one.
            var balance = (await _ledger.ResolveSettledBalance(playerId, cancellationToken)).Settled;
```

**Change nothing else in `EvaluateZeroOut`** — the word-claim `Release` / `TryReclaimWord` calls stay exactly where they are. They ask the same "can this balance pay for a round" question and get the corrected number for free.

- [ ] **Step 5: Run to verify they pass**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: PASS. Any pre-existing test that stubbed `ResolveBalance` for these two paths must be re-stubbed to `ResolveSettledBalance`.

- [ ] **Step 6: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Read the settled balance in the Orbit threshold and zero-out rules."
```

---

## Task 4: The planner gate — bar a tripped player from new rounds

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` (`ResolveCandidates`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs`

**Interfaces:**
- Consumes: `IOrbitLedgerService.ResolveSettledBalance` (Task 1).
- Produces: `OrbitBetPlacementService` constructor gains a 6th parameter, `IOrbitLedgerService ledger`, appended **last**: `(SpaceStrokerDbContext, IOrbitAccountResolver, IMessageBus, IGameService, GeneralSettings, IOrbitLedgerService)`.

- [ ] **Step 1: Write the failing tests**

Add to `OrbitBetPlacementServiceTests.cs` (add `private readonly Mock<IOrbitLedgerService> _ledger = new();` to the fields and pass `_ledger.Object` in the service factory):

```csharp
    /// <summary>THE STOP half of "stop first, then cash out". Without this the deferred cash-out in
    /// EvaluateFloor is unbounded — the player keeps entering rounds forever.</summary>
    [Fact]
    public async Task ResolveCandidates_SkipsAPlayerWhoseSettledBalanceReachedTheirFloor()
    {
        using var context = NewContext();
        var (parameters, playerId) = SeedEligiblePlayer(context, balance: 10m, cashOutFloor: 10m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(10m, 0m, 0));

        var candidates = await Service(context).ResolveCandidates(parameters);

        candidates.ShouldNotContain(playerId);
    }

    [Fact]
    public async Task ResolveCandidates_KeepsAPlayerWhoseSettledBalanceIsAboveTheirFloor()
    {
        using var context = NewContext();
        var (parameters, playerId) = SeedEligiblePlayer(context, balance: 10m, cashOutFloor: 5m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(10m, 0m, 0));

        var candidates = await Service(context).ResolveCandidates(parameters);

        candidates.ShouldContain(playerId);
    }

    /// <summary>A riding win that lifted the settled balance back over the floor makes the player
    /// eligible again — the whole point of deriving the trip instead of storing it.</summary>
    [Fact]
    public async Task ResolveCandidates_KeepsAPlayerWhoseRidingWinLiftedThemBackAboveTheFloor()
    {
        using var context = NewContext();
        var (parameters, playerId) = SeedEligiblePlayer(context, balance: 10m, cashOutFloor: 5m);

        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new OrbitSettledBalance(4m, 3m, 1));   // settled 7 > floor 5

        var candidates = await Service(context).ResolveCandidates(parameters);

        candidates.ShouldContain(playerId);
    }

    [Fact]
    public async Task ResolveCandidates_KeepsAPlayerWithNoFloorSet_WithoutReadingTheLedger()
    {
        using var context = NewContext();
        var (parameters, playerId) = SeedEligiblePlayer(context, balance: 10m, cashOutFloor: null);

        var candidates = await Service(context).ResolveCandidates(parameters);

        candidates.ShouldContain(playerId);
        _ledger.Verify(x => x.ResolveSettledBalance(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

⚠️ `SeedEligiblePlayer` may not exist under that name — reuse whatever the file's existing seeding helper is, extending it with a `cashOutFloor` parameter. Do **not** invent a second seeding helper.

- [ ] **Step 2: Run to verify they fail**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: compile failure (no 6-arg constructor), then the skip test fails.

- [ ] **Step 3: Implement**

In `OrbitBetPlacementService.cs`, append the dependency:

```csharp
        public OrbitBetPlacementService(
            SpaceStrokerDbContext context,
            IOrbitAccountResolver accounts,
            IMessageBus messageBus,
            IGameService gameService,
            GeneralSettings settings,
            IOrbitLedgerService ledger)
        {
            _context = context;
            _accounts = accounts;
            _messageBus = messageBus;
            _gameService = gameService;
            _settings = settings;
            _ledger = ledger;
        }

        private readonly IOrbitLedgerService _ledger;
```

Replace the final line of `ResolveCandidates` (`return candidates.Where(id => !alreadyBet.Contains(id)).ToList();`) with:

```csharp
            var eligible = candidates.Where(id => !alreadyBet.Contains(id)).ToList();

            return eligible.Count == 0 ? eligible : await ExcludeTrippedStopLosses(eligible, cancellationToken);
        }

        /// <summary>
        /// ⚠️ THE STOP-LOSS GATE — the "stop" half of "stop first, then cash out".
        ///
        /// A player whose SETTLED balance has reached their cash-out floor does not enter a new round.
        /// That is what makes the floor a stop loss rather than a delayed withdrawal, and it is what
        /// bounds the deferral in OrbitBetSettlementService.EvaluateFloor: without it, the player keeps
        /// betting, something is always riding, and the cash-out never fires.
        ///
        /// It is DERIVED, so there is no flag to set and none to clear. A riding win that lifts the
        /// settled balance back over the floor simply makes them eligible again on the next plan, and
        /// the player's own Enabled switch is never written by anything automatic.
        ///
        /// The per-player read only runs for candidates who actually HAVE a floor — a small subset of an
        /// already-small list, and usually none at all.
        /// </summary>
        private async Task<IReadOnlyList<Guid>> ExcludeTrippedStopLosses(
            List<Guid> eligible, CancellationToken cancellationToken)
        {
            var floors = await _context.OrbitEnrollments
                .Where(x => eligible.Contains(x.PlayerId) && x.CashOutFloor != null)
                .Select(x => new { x.PlayerId, Floor = x.CashOutFloor!.Value })
                .ToListAsync(cancellationToken);

            if (floors.Count == 0)
                return eligible;

            var barred = new List<Guid>();

            foreach (var entry in floors)
            {
                var position = await _ledger.ResolveSettledBalance(entry.PlayerId, cancellationToken);

                if (position.Settled <= entry.Floor)
                    barred.Add(entry.PlayerId);
            }

            if (barred.Count == 0)
                return eligible;

            Log.Information("Orbit skipped {Count} player(s) whose cash-out floor has been reached.", barred.Count);

            return eligible.Where(id => !barred.Contains(id)).ToList();
        }
```

⚠️ `PlanPlacements` already derives both the `MinimumNumberOfPlayers` viability check and the gate count from `ResolveCandidates`' result, so a barred player is correctly absent from both. **Do not add the filter anywhere else.**

- [ ] **Step 4: Run to verify they pass**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Bar a player whose Orbit stop loss has tripped from entering new rounds."
```

---

## Task 5: `OrbitStopRuleSweepJob`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Jobs/OrbitStopRuleSweepJob.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (DI ~line 62, schedule ~line 248)
- Test: `src/SpaceStroker.Tests/UnitTests/Jobs/OrbitStopRuleSweepJobTests.cs` *(create)*

**Interfaces:**
- Consumes: `IOrbitBetSettlementService.EvaluateFloor` (unchanged signature).
- Produces: `OrbitStopRuleSweepJob(SpaceStrokerDbContext context, IOrbitBetSettlementService settlement)` with `Task Run()`.

- [ ] **Step 1: Write the failing tests**

Create `src/SpaceStroker.Tests/UnitTests/Jobs/OrbitStopRuleSweepJobTests.cs`:

```csharp
using Moq;
using Shouldly;
using SpaceStroker.Domain.Orbit.Entities;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Jobs;
using SpaceStroker.Infrastructure.Services.Orbit;
using SpaceStroker.Tests.UnitTests.Shared;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Jobs;

/// <summary>
/// ⚠️ This job is not belt-and-braces — it is the ONLY remaining trigger for a tripped player.
/// EvaluateFloor is otherwise called only from settlement, and settlement only runs for a round the
/// player bet in; a tripped player is barred from new rounds, so they leave that path forever.
/// </summary>
public class OrbitStopRuleSweepJobTests : UnitTestsClass
{
    private readonly Mock<IOrbitBetSettlementService> _settlement = new();

    private static OrbitEnrollment Enrollment(Guid playerId, bool enabled, decimal? floor) => new()
    {
        PlayerId = playerId,
        Enabled = enabled,
        CashOutFloor = floor,
        GameParametersId = Guid.NewGuid(),
    };

    [Fact]
    public async Task Run_EvaluatesEveryEnabledPlayerWithAFloor()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitEnrollments.Add(Enrollment(first, enabled: true, floor: 2m));
            seed.OrbitEnrollments.Add(Enrollment(second, enabled: true, floor: 5m));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        await new OrbitStopRuleSweepJob(context, _settlement.Object).Run();

        _settlement.Verify(x => x.EvaluateFloor(first, It.IsAny<CancellationToken>()), Times.Once);
        _settlement.Verify(x => x.EvaluateFloor(second, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_SkipsPlayersWithNoFloorAndDisabledEnrolments()
    {
        var noFloor = Guid.NewGuid();
        var disabled = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitEnrollments.Add(Enrollment(noFloor, enabled: true, floor: null));
            seed.OrbitEnrollments.Add(Enrollment(disabled, enabled: false, floor: 2m));
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        await new OrbitStopRuleSweepJob(context, _settlement.Object).Run();

        _settlement.Verify(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>One player's failure must not stop the rest — this job is the only thing that pays them.</summary>
    [Fact]
    public async Task Run_KeepsGoing_WhenOnePlayerThrows()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitEnrollments.Add(Enrollment(failing, enabled: true, floor: 2m));
            seed.OrbitEnrollments.Add(Enrollment(healthy, enabled: true, floor: 2m));
            await seed.SaveChangesAsync();
        }

        _settlement.Setup(x => x.EvaluateFloor(failing, It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("boom"));

        using var context = new SpaceStrokerDbContext(ContextOptions);
        await Should.NotThrowAsync(() => new OrbitStopRuleSweepJob(context, _settlement.Object).Run());

        _settlement.Verify(x => x.EvaluateFloor(healthy, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: compile failure — `OrbitStopRuleSweepJob` does not exist.

- [ ] **Step 3: Implement the job**

Create `src/SpaceStroker.Infrastructure/Jobs/OrbitStopRuleSweepJob.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Orbit;

namespace SpaceStroker.Infrastructure.Jobs
{
    /// <summary>
    /// Re-drives the Orbit cash-out floor for players nothing else will ever re-check.
    ///
    /// ⚠️ This is NOT belt-and-braces; it is the ONLY remaining trigger for a tripped player.
    /// EvaluateFloor is called from settlement, and settlement only happens for a round the player bet
    /// in — but a player whose floor has tripped is barred from new rounds by
    /// OrbitBetPlacementService. So the moment their last riding bet resolves, they leave the settlement
    /// path forever. Without this job, a floor that trips while a bet is riding in a round that then
    /// jams never pays out at all.
    ///
    /// Every guard lives in EvaluateFloor, so this is a thin driver — and idempotent for free, because
    /// OrbitCashOutReserver debits the balance inside its own transaction, so a second pass finds
    /// nothing to cash out.
    /// </summary>
    public class OrbitStopRuleSweepJob
    {
        public OrbitStopRuleSweepJob(SpaceStrokerDbContext context, IOrbitBetSettlementService settlement)
        {
            _context = context;
            _settlement = settlement;
        }

        private readonly SpaceStrokerDbContext _context;
        private readonly IOrbitBetSettlementService _settlement;

        public async Task Run()
        {
            // Enabled + a floor set is the whole candidate set. Anything narrower would duplicate a guard
            // EvaluateFloor already owns, and the two copies would drift.
            var players = await _context.OrbitEnrollments
                .Where(x => x.Enabled && x.CashOutFloor != null)
                .Select(x => x.PlayerId)
                .ToListAsync();

            foreach (var playerId in players)
            {
                try
                {
                    await _settlement.EvaluateFloor(playerId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not evaluate the Orbit cash-out floor for player {PlayerId}.", playerId);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Wire it into Consumers**

In `src/SpaceStroker.Consumers/Program.cs`, beside line 62-63:

```csharp
builder.Services.AddScoped<OrbitStopRuleSweepJob>();
```

and in `ScheduleRecurringJobs`, after the reconciliation registration:

```csharp
    // Every 5 minutes: a tripped stop loss is barred from new rounds, so settlement will NEVER re-check
    // it — this is the only thing that finishes the cash-out once the last riding bet resolves.
    recurringJobs.AddOrUpdate<OrbitStopRuleSweepJob>("orbit-stop-rule-sweep", job => job.Run(), "*/5 * * * *");
```

⚠️ **Consumers only** — the API must not gain a recurring job.

- [ ] **Step 5: Run to verify they pass**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: PASS, whole suite green.

- [ ] **Step 6: Commit and push the backend**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Add the Orbit stop-rule sweep job."
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend push origin feature/orbit-stop-loss-trigger
```

---

## Task 6: Frontend — the `stop-loss` status

**Files:**
- Modify: `src/types/orbit.ts:255`
- Modify: `src/hooks/useOrbit.ts` (beside `isPausedForFunds` ~line 639; `orbitStatus` ~line 793; the return block ~line 1059)
- Modify: `src/components/orbit/OrbitStatusTag.vue`
- Modify: `src/pages/OrbitPage.vue`

**Interfaces:**
- Consumes: nothing from the backend tasks — every input is already on the `GET /orbit` payload.
- Produces: `OrbitStatus` gains `'stop-loss'`; `useOrbit()` returns `isStoppedByFloor: ComputedRef<boolean>`.

- [ ] **Step 1: Widen the type**

`src/types/orbit.ts` line 255:

```ts
export type OrbitStatus = 'signed-out' | 'off' | 'stop-loss' | 'unfunded' | 'running';
```

- [ ] **Step 2: Derive the state**

In `src/hooks/useOrbit.ts`, after `needsFirstDeposit`:

```ts
  /**
   * The stop loss has been reached, so Orbit is not entering new rounds — whether or not the money has
   * been sent home yet.
   *
   * ⚠️ `orbitBetsPlaced > 0` is load-bearing, exactly as it is in `isPausedForFunds`. A brand-new
   * enrolment sits at 0.00, so `balance <= floor` is trivially true for any floor — and telling someone
   * their stop loss triggered before Orbit ever placed a bet is both wrong and alarming.
   *
   * Derived, never pushed: the backend's own gate is the same comparison over the same numbers, and
   * both are already on the /orbit payload.
   */
  const isStoppedByFloor = computed(() => {
    const state = orbit.value;
    if (!state?.enabled || state.cashOutFloor === null) return false;
    if (state.orbitBetsPlaced <= 0) return false;
    return state.balance <= state.cashOutFloor;
  });
```

Update `orbitStatus`:

```ts
  const orbitStatus = computed<OrbitStatus>(() => {
    if (!hasSession.value) return 'signed-out';
    if (!orbit.value?.enabled) return 'off';
    // ⚠️ STOP-LOSS outranks UNFUNDED. A floor cash-out empties the balance BY DESIGN, so the "needs
    // money" reading is technically true and completely misleading — it invites a deposit into an Orbit
    // that is deliberately stopped.
    if (isStoppedByFloor.value) return 'stop-loss';
    // ⚠️ UNFUNDED before RUNNING: calling an Orbit that cannot pay for a round "running" told a
    // player with a 0.00 balance that it was playing for them, which is simply false.
    if (needsFirstDeposit.value || isPausedForFunds.value) return 'unfunded';
    return 'running';
  });
```

Add `isStoppedByFloor,` to the returned object.

- [ ] **Step 3: Label it**

In `src/components/orbit/OrbitStatusTag.vue`, add to the `label` map:

```ts
          'stop-loss': 'STOP LOSS',
```

and to `dotClass`, **before** the parked-pause/violet branch:

```ts
    // Red, deliberately NOT the violet a parked pause wears. The violet says "this is a state, not a
    // problem"; a stop loss firing is the loss the player asked to be protected from, and it reads as a
    // warning. `text-*` alongside `bg-*` — the dot's glow is `currentColor`.
    if (orbitStatus.value === 'stop-loss')
      return 'bg-[color:oklch(0.8_0.16_25)] text-[color:oklch(0.8_0.16_25)]';
```

⚠️ Verify `isParkedPause` is unaffected: it requires `orbitStatus === 'off'`, and `stop-loss` requires `enabled`, so they cannot collide. Do not "fix" it.

- [ ] **Step 4: Explain it on the page**

In `src/pages/OrbitPage.vue`, in the floor block, add a branch **above** the existing `v-else-if="floorAtRisk"` copy:

```html
                <p
                  v-else-if="orbitStatus === 'stop-loss'"
                  class="dialog-copy !text-[18px] !text-[color:oklch(0.8_0.16_25)]"
                >
                  Your stop loss triggered. Orbit is not entering new rounds. Raise or clear the floor,
                  or add funds, to start it again.
                </p>
```

`orbitStatus` is already destructured in this file's setup — confirm, and add it if not.

- [ ] **Step 5: Verify the frontend builds**

```bash
cp E:/Monkify/monkify-front/yarn.lock E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-front/yarn.lock
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-front
yarn install --frozen-lockfile
yarn lint
yarn build
```

Expected: both succeed. ⚠️ `yarn build` fails on CRLF (`prettier` `endOfLine: lf`) — if it complains about line endings, fix the endings, do not disable the rule.

- [ ] **Step 6: Commit and push the frontend**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-front add -A
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-front commit -m "Show a STOP LOSS state on /orbit when the cash-out floor has tripped."
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-front push origin feature/orbit-stop-loss-trigger
```

---

## Task 7: Final verification

- [ ] **Step 1: Full unit suite**

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_tests.bat
```

Expected: all green. Record exactly which test files were added/changed — `constraints.md` §3 requires reporting them.

- [ ] **Step 2: Integration suite (regression check only)**

Level is `Minor change`, so **no new** integration test is required — but the existing Orbit suites must not regress:

```bash
cd E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend
scripts/run_integration_tests.bat
```

Expected: green, in particular `OrbitAutoBetFlowTests`, `OrbitFeedbackFlowTests`, `OrbitNotificationsFlowTests`, `OrbitFlowTests`.

- [ ] **Step 3: Confirm no migration was added**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend diff --stat origin/main...feature/orbit-stop-loss-trigger -- "*/Migrations/*"
```

Expected: **empty output**. Any migration here means the design was implemented wrong.

- [ ] **Step 4: Push everything and build the compare links**

```bash
for r in monkify-backend monkify-front; do
  git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/$r push origin feature/orbit-stop-loss-trigger
  git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/$r remote get-url origin
  git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/$r diff --stat origin/main...feature/orbit-stop-loss-trigger
done
```

---

## Self-Review

**Spec coverage:**

| Spec § | Task |
| --- | --- |
| §4.1 `OrbitSettledBalance` | 1 |
| §5 `RidingBetStaleAfterMinutes` | 1 |
| §6.1 `ResolveSettledBalance` | 1 |
| §6.2 `EvaluateFloor` | 2 |
| §6.3 `EvaluateZeroOut` | 3 |
| §6.4 `EvaluateThreshold` | 3 |
| §6.5 planner gate | 4 |
| §6.6 sweep job + wiring | 5 |
| §7.1–7.5 frontend | 6 |
| §11 unit tests | 1–5 |
| §12 AC 1–12 | see below |

**Acceptance criteria → task:** AC1 → 2; AC2 → 2 + 4; AC3 → 2; AC4 → 2 + 4; AC5 → 2; AC6 → 1 + 5; AC7 → 5; AC8 → 4 (nothing writes `Enabled`); AC9 → 3; AC10 → 3; AC11 → 6; AC12 → 7.

**Type consistency:** `OrbitSettledBalance(Ledger, RidingStake, RidingCount)` / `.Settled` / `.HasRidingBets` and `ResolveSettledBalance` are used identically in Tasks 1–5. `isStoppedByFloor` and `'stop-loss'` are used identically across Task 6's four files.

**Placeholders:** none — every code step carries its actual content. Three steps flag "verify the existing helper's name" (Task 3 Step 1, Task 4 Step 1, Task 6 Step 4) rather than inventing a helper; that is a deliberate instruction to reuse, not a TBD.
