# Orbit Stop Loss Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Orbit's three settlement stop rules decide on a balance that includes stakes still riding in an open round, and make a round that ends with no winner evaluate those rules at all.

**Architecture:** One new read on `IOrbitLedgerService` — `ResolveSettledBalance` = the raw ledger balance **plus** the stakes of the player's `BetDebit` rows whose round is still in `Game.ActiveGamesStatus`. The three evaluators in `OrbitBetSettlementService` switch to it as **decision input only** (cash-out amounts keep coming from the raw ledger inside `OrbitCashOutReserver`). Separately, `RewardGameConsumer`'s no-winners early return gains the same stop-rule evaluation the normal path already does. No entity, no EF mapping, no migration, no config, no frontend.

**Tech Stack:** .NET 8, EF Core (Postgres in prod, EF in-memory in the unit suite), MassTransit (Postgres SQL transport), xUnit + Shouldly + Moq.

**Spec:** `monkify-meta/specs/orbit-stop-loss-trigger.md` — authoritative. Read §0 and §3.1 before writing code.
**Card:** [Orbit Stop Loss trigger #154](https://trello.com/c/kCzYE9Bi) — Level **`Minor change`**.

## Global Constraints

- **Worktree only.** All backend work happens in `E:\Monkify\.worktrees\orbit-stop-loss-trigger\monkify-backend`. The hub `E:\Monkify\monkify-backend` stays on `main` and is never switched or committed to.
- **Branch:** `feature/orbit-stop-loss-trigger`. Push after each commit. **Never force-push. Never commit on `main`. Never skip hooks.**
- **No schema.** This plan adds no entity, no property, no `OnModelCreating` change and **no migration**. If you find yourself running `dotnet ef migrations add`, you have gone off-plan — stop.
- **No config.** No new `GeneralSettings` / `OrbitSettings` key. The archived attempt added `RidingBetStaleAfterMinutes`; it is **rejected** (spec §3.1). Do not add it.
- **No frontend.** `monkify-front` is not touched and has no worktree for this card.
- **Level `Minor change` ⇒ no new integration test** is required (`constraints.md` §3). The existing `SpaceStroker.Tests.Integration` suite must still pass unchanged.
- **Unit test command:** `scripts\run_tests.bat` from the backend worktree root. **Never run a bare `dotnet test`** over the whole solution — it pulls in the slow `ProductionGames` Monte Carlo suite. For a single red/green loop, run `dotnet test --filter "FullyQualifiedName~<TestClass>"` from `src\SpaceStroker.Tests`.
- **Debits are stored negative.** `OrbitTransaction.Amount` is signed (`OrbitTransaction.cs` — "SIGNED: credits positive, debits negative"). Adding riding stakes back is therefore a **subtraction**.
- **Report which unit tests changed** at the end (`constraints.md` §3).

---

## File Structure

| File | Responsibility after this plan |
| --- | --- |
| `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs` | Declares `ResolveSettledBalance` alongside `ResolveBalance`, with the decide-only warning. |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs` | Implements it: `ResolveBalance` first, then one `SumAsync` over open-round `BetDebit` rows. |
| `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` | `EvaluateFloor`, `EvaluateZeroOut`, `EvaluateThreshold` decide on the settled balance. |
| `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` | Evaluates the stop rules on the no-winners exit as well as the normal one. |
| `src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs` | + 7 cases for `ResolveSettledBalance`. |
| `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs` | 11 existing mock setups retargeted + 6 new cases. |
| `src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs` | **New.** The no-winners evaluation path. |

Three tasks. Task 1 is self-contained (new method, nobody calls it yet). Task 2 switches the callers — it is the task that changes behaviour and the one that breaks existing tests. Task 3 is independent of both and could be reviewed on its own.

---

### Task 1: `ResolveSettledBalance` on the ledger service

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs` (add after `ResolveBalance`, which ends at line 116)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs`

**Interfaces:**
- Consumes: nothing new. `OrbitLedgerService` already has `_context` (`SpaceStrokerDbContext`) and `_accounts` (`IOrbitAccountResolver`) as fields — **do not add a constructor parameter.**
- Produces: `Task<decimal> IOrbitLedgerService.ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default)`. Task 2 consumes exactly this signature.

- [ ] **Step 1: Write the failing tests**

Append to `src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs`, immediately before the closing `}` of the class. Add these usings at the top of the file (the existing usings do not cover `Game`/`GameStatus`):

```csharp
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.ValueObjects;
```

Then the helpers and cases:

```csharp
    // ---- ResolveSettledBalance ----

    /// <summary>
    /// A round in one of the ActiveGamesStatus states. Only Id and Status matter to the query, but
    /// ParametersId is a non-nullable FK so it has to be set for the in-memory provider to accept it.
    /// </summary>
    private static Game GameAt(GameStatus status)
        => new() { ParametersId = Guid.NewGuid(), Status = status };

    private static OrbitTransaction BetDebitOn(Guid playerId, Guid gameId, decimal amount)
        => new()
        {
            OrbitAccount = Account,
            MintAddress = Mint,
            PlayerId = playerId,
            // Debits are stored NEGATIVE.
            Amount = -amount,
            Type = OrbitTransactionType.BetDebit,
            Status = OrbitTransactionStatus.Completed,
            GameId = gameId,
        };

    [Theory]
    [InlineData(GameStatus.WaitingBets)]
    [InlineData(GameStatus.GameStarting)]
    [InlineData(GameStatus.InProgress)]
    public async Task ResolveSettledBalance_AddsBackAStakeRidingInAnOpenRound(GameStatus openStatus)
    {
        var playerId = Guid.NewGuid();
        var game = GameAt(openStatus);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(BetDebitOn(playerId, game.Id, 1m));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 9m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var service = new OrbitLedgerService(context, Accounts());

        (await service.ResolveBalance(playerId)).ShouldBe(9m);
        (await service.ResolveSettledBalance(playerId)).ShouldBe(10m);
    }

    /// <summary>
    /// ⚠️ THE CASE THE WHOLE CARD TURNS ON. At its own settlement a round is already
    /// RewardForWinnersInProgress (RewardGameConsumer.cs:86), or RewardForWinnersCompleted on the
    /// no-winners path. Neither is in ActiveGamesStatus, so that round's own resolved stake must NOT be
    /// added back — adding it would inflate the balance at exactly the moment the floor should fire,
    /// which is what the archived deny-list implementation did (spec §3.1).
    /// </summary>
    [Theory]
    [InlineData(GameStatus.RewardForWinnersInProgress)]
    [InlineData(GameStatus.RewardForWinnersCompleted)]
    public async Task ResolveSettledBalance_ExcludesTheRoundBeingSettled(GameStatus settlingStatus)
    {
        var playerId = Guid.NewGuid();
        var game = GameAt(settlingStatus);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(BetDebitOn(playerId, game.Id, 1m));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 9m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(9m);
    }

    [Theory]
    [InlineData(GameStatus.Ended)]
    [InlineData(GameStatus.ErrorWhenProcessingRewards)]
    [InlineData(GameStatus.GameEndedAbruptely)]
    [InlineData(GameStatus.NotEnoughPlayersToStart)]
    public async Task ResolveSettledBalance_ExcludesTerminalAndAbortedRounds(GameStatus terminalStatus)
    {
        var playerId = Guid.NewGuid();
        var game = GameAt(terminalStatus);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(BetDebitOn(playerId, game.Id, 1m));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 9m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(9m);
    }

    /// <summary>
    /// Only BetDebit rows carry a GameId — it IS the placement claim. Nothing else may ever be added
    /// back, or a deposit would count twice.
    /// </summary>
    [Fact]
    public async Task ResolveSettledBalance_IgnoresRowsThatAreNotBetDebits()
    {
        var playerId = Guid.NewGuid();
        var game = GameAt(GameStatus.WaitingBets);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(Row(playerId, 4m, OrbitTransactionType.WinCredit));
            seed.OrbitTransactions.Add(Row(playerId, 2m, OrbitTransactionType.Refund));
            seed.OrbitTransactions.Add(Row(playerId, -3m, OrbitTransactionType.CashOut));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 13m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(13m);
    }

    [Fact]
    public async Task ResolveSettledBalance_IgnoresAnotherPlayersRidingStake()
    {
        var playerId = Guid.NewGuid();
        var otherPlayerId = Guid.NewGuid();
        var game = GameAt(GameStatus.WaitingBets);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(BetDebitOn(otherPlayerId, game.Id, 5m));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 10m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(10m);
    }

    /// <summary>Pool isolation, same reason as ResolveBalance_IgnoresRowsBelongingToAnotherPool.</summary>
    [Fact]
    public async Task ResolveSettledBalance_IgnoresARidingStakeFromAnotherPool()
    {
        var playerId = Guid.NewGuid();
        var game = GameAt(GameStatus.WaitingBets);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(game);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(new OrbitTransaction
            {
                OrbitAccount = "A_SUPERSEDED_ORBIT_ACCOUNT",
                MintAddress = Mint,
                PlayerId = playerId,
                Amount = -7m,
                Type = OrbitTransactionType.BetDebit,
                Status = OrbitTransactionStatus.Completed,
                GameId = game.Id,
            });
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 10m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(10m);
    }

    [Fact]
    public async Task ResolveSettledBalance_EqualsTheLedger_WhenNothingIsRiding()
    {
        var playerId = Guid.NewGuid();

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.OrbitTransactions.Add(Row(playerId, 6m, OrbitTransactionType.Deposit));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 6m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);
        var service = new OrbitLedgerService(context, Accounts());

        (await service.ResolveSettledBalance(playerId)).ShouldBe(await service.ResolveBalance(playerId));
    }

    /// <summary>Two open rounds at once (a mid-round re-plan) both count.</summary>
    [Fact]
    public async Task ResolveSettledBalance_AddsBackEveryOpenRoundsStake()
    {
        var playerId = Guid.NewGuid();
        var first = GameAt(GameStatus.WaitingBets);
        var second = GameAt(GameStatus.InProgress);

        using (var seed = new SpaceStrokerDbContext(ContextOptions))
        {
            seed.Games.Add(first);
            seed.Games.Add(second);
            seed.OrbitTransactions.Add(Row(playerId, 10m, OrbitTransactionType.Deposit));
            seed.OrbitTransactions.Add(BetDebitOn(playerId, first.Id, 1m));
            seed.OrbitTransactions.Add(BetDebitOn(playerId, second.Id, 2m));
            seed.OrbitBalances.Add(new OrbitBalance { OrbitAccount = Account, MintAddress = Mint, PlayerId = playerId, Amount = 7m });
            await seed.SaveChangesAsync();
        }

        using var context = new SpaceStrokerDbContext(ContextOptions);

        (await new OrbitLedgerService(context, Accounts()).ResolveSettledBalance(playerId)).ShouldBe(10m);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run, from `E:\Monkify\.worktrees\orbit-stop-loss-trigger\monkify-backend\src\SpaceStroker.Tests`:

```
dotnet test --filter "FullyQualifiedName~OrbitLedgerServiceTests"
```

Expected: **compile error** — `'IOrbitLedgerService' does not contain a definition for 'ResolveSettledBalance'`. That is the correct red for this step; the method does not exist yet.

- [ ] **Step 3: Declare it on the interface**

In `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs`, add after the `ResolveBalance` declaration (line 14):

```csharp
        /// <summary>
        /// The balance to DECIDE on: the ledger balance plus the stakes of Orbit bets that are still
        /// riding — i.e. whose round has not run yet.
        ///
        /// A stake leaves the balance the instant the bet is placed, and the successor round opens
        /// BEFORE the previous one settles (RunGameConsumer creates it at line 50-61, then publishes
        /// RewardGameEvent at line 66). Every settlement-time stop rule that reads the raw ledger
        /// therefore reads it one stake too low, and cashes a player out while their bet is riding.
        ///
        /// ⚠️ DECIDE ONLY — never size money with this. A cash-out must be sized from ResolveBalance,
        /// which is what OrbitCashOutReserver.Reserve already does: the riding stake is committed to a
        /// live round and is not available to send home.
        /// </summary>
        Task<decimal> ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it**

In `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs`, add these usings at the top:

```csharp
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Orbit.ValueObjects;
```

Then insert this method immediately after `ResolveBalance` ends (after line 116, before `TryDebit`):

```csharp
        /// <inheritdoc />
        public async Task<decimal> ResolveSettledBalance(Guid playerId, CancellationToken cancellationToken = default)
        {
            // ⚠️ THE LEDGER IS READ FIRST, AND THE ORDER IS DELIBERATE. A placement can land between the
            // two reads either way, and the two orders fail in OPPOSITE directions:
            //   ledger-then-riding  → the new debit is in `riding` but not in `ledger`, so the settled
            //                         balance comes out one stake HIGH and the floor is DEFERRED a round.
            //   riding-then-ledger  → the new debit is in `ledger` but not in `riding`, so it comes out
            //                         one stake LOW and the floor fires EARLY — the exact bug this fixes.
            // Deferring self-corrects at the next settlement; firing early sends a player's money home
            // while their bet is still riding and cannot be undone. So: ledger first.
            //
            // Reuse ResolveBalance rather than re-summing the ledger — it owns the REPEATABLE READ
            // snapshot and the guarded self-heal, and duplicating that read is how the column-drift bug
            // it was written to fix comes back.
            var ledger = await ResolveBalance(playerId, cancellationToken);

            var account = _accounts.OrbitAccount;
            var mint = _accounts.MintAddress;

            // ⚠️ RIDING IS DECIDED BY GAME STATUS, NEVER BET STATUS. A losing bet is never transitioned —
            // it stays BetStatus.Made forever — so a bet-status test would treat every past loss as still
            // riding, grow the settled balance without bound, and stop the floor ever firing again.
            //
            // ⚠️ ALLOW-LIST (ActiveGamesStatus), never a deny-list of terminal statuses. At its own
            // settlement the round is already RewardForWinnersInProgress (RewardGameConsumer.cs:86), or
            // RewardForWinnersCompleted on the no-winners path — neither is in ActiveGamesStatus, so the
            // round being settled excludes itself for free. A deny-list adds that stake back and
            // suppresses the very trip this exists to make correct. See spec §3.1.
            //
            // Only BetDebit rows carry a GameId (it IS the placement claim), so a deposit, cash-out, win
            // or refund can never be picked up here. Reading the claim rather than the Bet also counts a
            // stake that has been debited but whose bet has not been registered yet.
            var riding = await _context.OrbitTransactions
                .Where(x => x.OrbitAccount == account
                         && x.MintAddress == mint
                         && x.PlayerId == playerId
                         && x.Type == OrbitTransactionType.BetDebit
                         && x.GameId != null
                         && _context.Games.Any(g => g.Id == x.GameId && Game.ActiveGamesStatus.Contains(g.Status)))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

            // Debits are stored NEGATIVE, so subtracting is what adds the riding stakes back.
            return ledger - riding;
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test --filter "FullyQualifiedName~OrbitLedgerServiceTests"
```

Expected: PASS, all cases. If `ResolveSettledBalance_AddsBackAStakeRidingInAnOpenRound` fails with `11` instead of `10`, the sign is wrong — you added instead of subtracting, or seeded the debit positive.

- [ ] **Step 6: Run the full fast suite**

From `E:\Monkify\.worktrees\orbit-stop-loss-trigger\monkify-backend`:

```
scripts\run_tests.bat
```

Expected: PASS. Nothing calls the new method yet, so no existing test can have moved.

- [ ] **Step 7: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitLedgerService.cs src/SpaceStroker.Infrastructure/Services/Orbit/OrbitLedgerService.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitLedgerServiceTests.cs
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Add ResolveSettledBalance to the Orbit ledger service."
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend push origin feature/orbit-stop-loss-trigger
```

---

### Task 2: The three stop rules decide on the settled balance

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (lines 172, 215, 340 — plus the comments at 158-164, 183-197, 232-239, 313-331)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`

**Interfaces:**
- Consumes: `IOrbitLedgerService.ResolveSettledBalance(Guid, CancellationToken)` from Task 1.
- Produces: no new public surface. `EvaluateFloor`, `EvaluateZeroOut` and `EvaluateThreshold` keep their existing signatures.

> ⚠️ **This task breaks 11 existing tests on purpose.** `OrbitBetSettlementServiceTests` stubs `_ledger.Setup(x => x.ResolveBalance(...))` at lines **126, 138, 222, 234, 246, 259, 271, 357, 370, 475, 500**. Once the evaluators call `ResolveSettledBalance`, an unstubbed Moq method returns `0m`, so those tests go red with confusing assertions rather than a compile error. Retargeting them is part of this task, not a follow-up.

- [ ] **Step 1: Write the failing tests**

Append to `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`, before the closing `}` of the class:

```csharp
    // ---- the settled balance is what the stop rules decide on ----

    /// <summary>
    /// ⚠️ THE DEFECT THIS CARD EXISTS FOR. The raw ledger says the player is exactly at their floor,
    /// but only because the SUCCESSOR round's stake has already been debited — the round that opens
    /// before the previous one settles. That bet is still riding, so this is not a loss yet and the
    /// floor must not fire.
    /// </summary>
    [Fact]
    public async Task EvaluateFloor_DoesNotCashOut_WhenOnlyARidingStakeTookTheBalanceToTheFloor()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 3m);
        _ledger.Setup(x => x.ResolveBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(3m);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(4m);

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(It.IsAny<Guid>(), It.IsAny<OrbitCashOutSource>(), It.IsAny<bool>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The floor decides on the settled balance, but the RESERVER still sizes from the raw ledger — it
    /// is passed no amount, so OrbitCashOutReserver reads ResolveBalance itself. The riding stake is
    /// committed to a live round and must never be paid out.
    /// </summary>
    [Fact]
    public async Task EvaluateFloor_CashesOut_WhenTheSettledBalanceIsAtTheFloor_AndPassesNoAmount()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context, cashOutFloor: 3m);
        _ledger.Setup(x => x.ResolveBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(2m);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(3m);

        await Service(context).EvaluateFloor(playerId);

        _cashOut.Verify(x => x.Reserve(playerId, OrbitCashOutSource.Floor, false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The take-profit reads the same polluted number from CreditWin: the win landed, but the next
    /// round's stake has already left, so the raw ledger sits below the threshold and the player's
    /// profit is not taken. The settled balance is what actually crossed it.
    /// </summary>
    [Fact]
    public async Task CreditWin_FiresTheThreshold_OnTheSettledBalance_WhenTheRawLedgerIsAStakeShort()
    {
        using var context = NewContext();
        var (bet, playerId) = Seed(context, cashOutAtThreshold: 12m);
        _ledger.Setup(x => x.ResolveBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(11m);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(12m);

        await Service(context).CreditWin(bet, 7m);

        _cashOut.Verify(x => x.Reserve(playerId, OrbitCashOutSource.Threshold, false, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The zero-out's other half: while a stake is riding the player is not out of funds, so Orbit must
    /// not announce that it stopped — and must NOT surrender the reserved word. Releasing it here is the
    /// per-round claim churn the comment in EvaluateZeroOut says it exists to prevent.
    /// </summary>
    [Fact]
    public async Task EvaluateZeroOut_KeepsTheWordAndSaysNothing_WhileAStakeIsRiding()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context);
        SeedBetDebit(context, playerId);
        _ledger.Setup(x => x.ResolveBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(5m);

        await Service(context).EvaluateZeroOut(playerId);

        _claims.Verify(x => x.Release(playerId, It.IsAny<CancellationToken>()), Times.Never);
        context.OrbitEnrollments.Single(x => x.PlayerId == playerId).ZeroOutNotifiedAt.ShouldBeNull();
    }

    /// <summary>With nothing riding the settled balance IS the ledger, so the zero-out behaves exactly as before.</summary>
    [Fact]
    public async Task EvaluateZeroOut_StillFires_WhenTheSettledBalanceIsGenuinelyUnderTheStake()
    {
        using var context = NewContext();
        var (_, playerId) = Seed(context);
        SeedBetDebit(context, playerId);
        _ledger.Setup(x => x.ResolveBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        _ledger.Setup(x => x.ResolveSettledBalance(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        await Service(context).EvaluateZeroOut(playerId);

        _claims.Verify(x => x.Release(playerId, It.IsAny<CancellationToken>()), Times.Once);
    }
```

Helpers these reuse, all already in the file — do not redefine them:

- `Seed(SpaceStrokerDbContext context, decimal? cashOutAtThreshold = null, decimal? cashOutFloor = null, string? choice = null)` returns `(Bet Bet, Guid PlayerId)` (line 419).
- `SeedBetDebit(context, playerId)` (line 397) writes the `BetDebit` row that `EvaluateZeroOut`'s `hasBet` guard requires **and already calls `TargetTheSeededType` itself** — do not call `TargetTheSeededType` as well.
- `Service(context)` (line 414) builds the `OrbitBetSettlementService` with all nine mocks.
- `NewContext()` (line 417).

- [ ] **Step 2: Run the tests to verify they fail**

From `src\SpaceStroker.Tests`:

```
dotnet test --filter "FullyQualifiedName~OrbitBetSettlementServiceTests"
```

Expected: the 5 new tests FAIL (the evaluators still read `ResolveBalance`, so the settled stub is ignored). The 11 pre-existing tests still PASS at this point.

- [ ] **Step 3: Switch the three evaluators**

In `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs`, make exactly three call-site changes:

```csharp
// line 172, inside EvaluateThreshold
var balance = await _ledger.ResolveSettledBalance(playerId, cancellationToken);

// line 215, inside EvaluateZeroOut
var balance = await _ledger.ResolveSettledBalance(playerId, cancellationToken);

// line 340, inside EvaluateFloor
var balance = await _ledger.ResolveSettledBalance(playerId, cancellationToken);
```

Leave every guard as it is — `if (balance < threshold) return;`, `if (balance >= requiredAmount)`, `if (balance > floor) return;`, `if (balance <= 0) return;`. They now evaluate the settled number, which is the whole change.

- [ ] **Step 4: Correct the comments that justify the raw read**

These are load-bearing docs — the next reader will otherwise re-derive the bug. Update, in the same file:

- The `EvaluateThreshold` summary (lines 158-164): add that it decides on the settled balance, and that the reserved amount can therefore be **one stake below the threshold the player set**, because that stake is committed to a live round.
- The `EvaluateZeroOut` summary (lines 183-197): it currently says "Evaluated HERE — at settlement, on a RESOLVED bet". Say that settlement alone was not enough, because the successor round's stake has already left the ledger by then, and that the riding stakes are added back for the decision.
- The claim-release comment (lines 232-239): note that this is the read that was releasing the word every round for a player holding roughly one stake, and that the settled balance is what makes its stated invariant true.
- The `EvaluateFloor` summary (lines 313-331): same correction as the zero-out.

- [ ] **Step 5: Retarget the 11 existing mock setups**

In `OrbitBetSettlementServiceTests.cs`, change `ResolveBalance` → `ResolveSettledBalance` in the `_ledger.Setup(...)` lines at **126, 138, 222, 234, 246, 259, 271, 357, 370, 475, 500** (line numbers before your edits; find them by the `_ledger.Setup(x => x.ResolveBalance` pattern). Do **not** touch the new tests added in Step 1 — they deliberately stub *both*.

Sanity check afterwards:

```
grep -n "ResolveBalance(" src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs
```

Expected: only the **5 new** tests still stub `ResolveBalance`, and each of those also stubs `ResolveSettledBalance`.

- [ ] **Step 6: Run the tests to verify they pass**

```
dotnet test --filter "FullyQualifiedName~OrbitBetSettlementServiceTests"
```

Expected: PASS, all of them — the 5 new and the 11 retargeted.

- [ ] **Step 7: Run the full fast suite**

```
scripts\run_tests.bat
```

Expected: PASS. If something outside `OrbitBetSettlementServiceTests` went red, another test doubles `IOrbitLedgerService` and reaches an evaluator — fix it the same way (stub `ResolveSettledBalance`), and add it to the changed-test list for the report.

- [ ] **Step 8: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Decide the three Orbit stop rules on the settled balance."
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend push origin feature/orbit-stop-loss-trigger
```

---

### Task 3: A round with no winner evaluates the stop rules

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`
- Create: `src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs`

**Interfaces:**
- Consumes: `IOrbitBetSettlementService.EvaluateFloor(Guid, CancellationToken)` and `.EvaluateZeroOut(Guid, CancellationToken)` — both already exist and are unchanged by Tasks 1-2.
- Produces: no new surface. `EvaluateOrbitFloors` stays `private static`.

- [ ] **Step 1: Write the failing test**

Create `src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using SpaceStroker.Domain.Bets.Entities;
using SpaceStroker.Domain.Bets.ValueObjects;
using SpaceStroker.Domain.Configs.Entities;
using SpaceStroker.Domain.Games.Entities;
using SpaceStroker.Domain.Games.Services;
using SpaceStroker.Domain.Games.ValueObjects;
using SpaceStroker.Infrastructure.Consumers.RewardGame;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;
using SpaceStroker.Infrastructure.Services.Orbit;
using SpaceStroker.Tests.UnitTests.Shared;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Consumers;

/// <summary>
/// The no-winner round.
///
/// ⚠️ The property under test is that a round nobody won still evaluates the Orbit stop rules. Before
/// this, RewardGameConsumer returned at its "no winners to be rewarded" branch BEFORE reaching
/// EvaluateOrbitFloors — so a preset round whose typed choice had no backers, where every Orbit player
/// lost their whole stake, was exactly the round the cash-out floor never saw.
///
/// Only the no-winners path is exercised here. The paid path moves real money through Solana and
/// belongs to SpaceStroker.Tests.Integration.
/// </summary>
public class RewardGameConsumerTests : UnitTestsClass
{
    private readonly Mock<IGameService> _gameService = new();
    private readonly Mock<IOrbitBetSettlementService> _orbitSettlement = new();

    private SpaceStrokerDbContext NewContext() => new(ContextOptions);

    private RewardGameConsumer Consumer(SpaceStrokerDbContext context) => new(ScopeFactory(context));

    private IServiceScopeFactory ScopeFactory(SpaceStrokerDbContext context)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(x => x.GetService(typeof(GeneralSettings))).Returns(new GeneralSettings());
        provider.Setup(x => x.GetService(typeof(SpaceStrokerDbContext))).Returns(context);
        provider.Setup(x => x.GetService(typeof(IGameService))).Returns(_gameService.Object);
        provider.Setup(x => x.GetService(typeof(IOrbitBetSettlementService))).Returns(_orbitSettlement.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(x => x.ServiceProvider).Returns(provider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(x => x.CreateScope()).Returns(scope.Object);

        return factory.Object;
    }

    /// <summary>
    /// A round where nobody backed the typed choice. Every bet stays Made — NeedsRewarding is only
    /// stamped on matches by RunGameConsumer — which is what sends the consumer down the early return.
    /// </summary>
    private Game SeedRoundWithNoWinners(SpaceStrokerDbContext context, params Guid[] orbitPlayerIds)
    {
        var parameters = new GameParameters { Name = "Beast Mode", RequiredAmount = 1m };
        var game = new Game(parameters.Id) { Status = GameStatus.Ended, Parameters = parameters };

        foreach (var playerId in orbitPlayerIds)
            game.Bets.Add(new Bet(1m)
            {
                GameId = game.Id,
                PlayerId = playerId,
                Amount = 1m,
                Choice = "jaguar",
                Status = BetStatus.Made,
                FundedByOrbit = true,
            });

        context.GameParameters.Add(parameters);
        context.Games.Add(game);
        context.SaveChanges();

        return game;
    }

    [Fact]
    public async Task ARoundNobodyWon_StillEvaluatesTheOrbitStopRules_ForEveryOrbitBettor()
    {
        using var context = NewContext();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var game = SeedRoundWithNoWinners(context, first, second);

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _orbitSettlement.Verify(x => x.EvaluateFloor(first, It.IsAny<CancellationToken>()), Times.Once);
        _orbitSettlement.Verify(x => x.EvaluateZeroOut(first, It.IsAny<CancellationToken>()), Times.Once);
        _orbitSettlement.Verify(x => x.EvaluateFloor(second, It.IsAny<CancellationToken>()), Times.Once);
        _orbitSettlement.Verify(x => x.EvaluateZeroOut(second, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>The round must still reach its terminal status — the evaluation is added, not swapped in.</summary>
    [Fact]
    public async Task ARoundNobodyWon_StillCompletes()
    {
        using var context = NewContext();
        var game = SeedRoundWithNoWinners(context, Guid.NewGuid());

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _gameService.Verify(x => x.UpdateGameStatus(
            It.Is<Game>(g => g.Id == game.Id), GameStatus.RewardForWinnersCompleted, null), Times.Once);
    }

    [Fact]
    public async Task ARoundNobodyWon_EvaluatesNothing_WhenNoBetWasFundedByOrbit()
    {
        using var context = NewContext();
        var game = SeedRoundWithNoWinners(context);
        context.Bets.Add(new Bet(1m)
        {
            GameId = game.Id,
            PlayerId = Guid.NewGuid(),
            Amount = 1m,
            Choice = "falcon",
            Status = BetStatus.Made,
            FundedByOrbit = false,
        });
        await context.SaveChangesAsync();

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _orbitSettlement.Verify(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _orbitSettlement.Verify(x => x.EvaluateZeroOut(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A round with no bets at all returns before anything — unchanged behaviour.</summary>
    [Fact]
    public async Task ARoundWithNoBets_EvaluatesNothing()
    {
        using var context = NewContext();
        var parameters = new GameParameters { Name = "Beast Mode", RequiredAmount = 1m };
        var game = new Game(parameters.Id) { Status = GameStatus.Ended, Parameters = parameters };
        context.GameParameters.Add(parameters);
        context.Games.Add(game);
        await context.SaveChangesAsync();

        await Consumer(context).ProcessMessage(new RewardGameEvent(game.Id));

        _orbitSettlement.Verify(x => x.EvaluateFloor(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _gameService.Verify(x => x.UpdateGameStatus(It.IsAny<Game>(), It.IsAny<GameStatus>(), It.IsAny<SpaceStrokerTyper?>()), Times.Never);
    }
}
```

Signatures this leans on, all verified against the current tree:

- `IGameService.UpdateGameStatus(Game game, GameStatus status, SpaceStrokerTyper? monkey = null)` (`IGameService.cs:28`). The consumer calls it with two arguments, so the third is `null` at the call site — which is why `Verify(..., GameStatus.RewardForWinnersCompleted, null)` matches. `SpaceStrokerTyper` lives in `SpaceStroker.Domain.Games.Services`; add that using.
- `Bet(decimal amount)` (`Bet.cs:45`) — the same constructor the existing settlement tests use. `GameId`, `PlayerId`, `Amount`, `Choice`, `Status` and `FundedByOrbit` are all settable properties.
- `GameParameters.Name` and `.RequiredAmount` exist and are enough for this seed; `Game(Guid parametersId)` is the constructor to use.

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test --filter "FullyQualifiedName~RewardGameConsumerTests"
```

Expected: `ARoundNobodyWon_StillEvaluatesTheOrbitStopRules_ForEveryOrbitBettor` FAILS — "Expected invocation on the mock once, but was 0 times" — because the consumer returns at line 57-62 before reaching the evaluation. `ARoundNobodyWon_StillCompletes` should already PASS.

- [ ] **Step 3: Move the Orbit resolve above the early return, and evaluate**

In `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`:

1. Move the `orbitSettlement` resolve (currently line 66) up next to the other three resolves at the top of `ProcessMessage` (lines 29-31), so it exists before the no-winners return:

```csharp
            var settings = scope.ServiceProvider.GetRequiredService<GeneralSettings>();
            var dbContext = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            // Resolved up here, not beside the Solana services below: the no-winners return needs it, and
            // that return happens before any payout service is ever touched.
            var orbitSettlement = scope.ServiceProvider.GetRequiredService<IOrbitBetSettlementService>();
```

   Then delete the old `var orbitSettlement = ...` line from the block at 64-66, leaving `solanaService` and `settlement` where they are.

2. Extend the no-winners branch (lines 57-62):

```csharp
            if (!game.Bets.Any(x => x.Status == BetStatus.NeedsRewarding))
            {
                Log.Warning("Game {0} has no winners to be rewarded.", message.GameId);
                await gameService.UpdateGameStatus(game, GameStatus.RewardForWinnersCompleted);

                // ⚠️ A round nobody won is a round every Orbit bettor LOST OUTRIGHT — precisely the
                // round the cash-out floor exists for. This return used to happen before
                // EvaluateOrbitFloors, so a preset type whose typed choice had no backers never
                // evaluated a stop rule at all.
                await EvaluateOrbitFloors(game, orbitSettlement);
                return;
            }
```

3. Leave the other three exits exactly as they are: `game == null` (45-49), `game.Bets.IsNullOrEmpty()` (51-55), and the blockhash failure (76-83) — on that last one the payouts never ran, so the floor must not decide on that state. The `ErrorWhenProcessingRewards` path at 130-134 already evaluates; do not touch it.

4. Update the `EvaluateOrbitFloors` doc-comment (137-147) to say it decides on the settled balance and that it runs on the no-winners path too.

- [ ] **Step 4: Run the test to verify it passes**

```
dotnet test --filter "FullyQualifiedName~RewardGameConsumerTests"
```

Expected: PASS, all four.

- [ ] **Step 5: Run the full fast suite**

```
scripts\run_tests.bat
```

Expected: PASS.

- [ ] **Step 6: Run the integration suite (Docker must be up)**

From the backend worktree root:

```
scripts\run_integration_tests.bat
```

Expected: PASS. Watch `OrbitAutoBetFlowTests` and `OrbitFeedbackFlowTests` in particular — the latter's `ASettledOrbitRound_LeavesTheBalanceAndTheLedgerAgreeing` asserts on the raw balance column across a settlement, which is the pass this change runs inside. No new integration test is added: the card's Level is `Minor change` (`constraints.md` §3).

- [ ] **Step 7: Commit**

```bash
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend add src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs src/SpaceStroker.Tests/UnitTests/Consumers/RewardGameConsumerTests.cs
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend commit -m "Evaluate the Orbit stop rules on a round nobody won."
git -C E:/Monkify/.worktrees/orbit-stop-loss-trigger/monkify-backend push origin feature/orbit-stop-loss-trigger
```

---

## Acceptance criteria coverage

| AC | Covered by |
| --- | --- |
| 1 — riding stake added back | Task 1 `ResolveSettledBalance_AddsBackAStakeRidingInAnOpenRound` |
| 2 — round being settled excluded | Task 1 `ResolveSettledBalance_ExcludesTheRoundBeingSettled` |
| 3 — no cash-out when only a riding stake crossed the floor | Task 2 `EvaluateFloor_DoesNotCashOut_WhenOnlyARidingStakeTookTheBalanceToTheFloor` |
| 4 — cash-out when the settled balance is at/below the floor | Task 2 `EvaluateFloor_CashesOut_WhenTheSettledBalanceIsAtTheFloor_AndPassesNoAmount` |
| 5 — amount sized from the raw ledger | Task 2, same test (asserts `Reserve` is passed `null` for the amount) |
| 6 — word kept, nothing announced while riding | Task 2 `EvaluateZeroOut_KeepsTheWordAndSaysNothing_WhileAStakeIsRiding` |
| 7 — zero-out still fires when genuinely dry | Task 2 `EvaluateZeroOut_StillFires_WhenTheSettledBalanceIsGenuinelyUnderTheStake` |
| 8 — threshold fires on the settled balance | Task 2 `CreditWin_FiresTheThreshold_OnTheSettledBalance_WhenTheRawLedgerIsAStakeShort` |
| 9 — no-winner round evaluates | Task 3 `ARoundNobodyWon_StillEvaluatesTheOrbitStopRules_ForEveryOrbitBettor` + `ARoundNobodyWon_StillCompletes` |
| 10 — other early returns unchanged | Task 3 `ARoundWithNoBets_EvaluatesNothing`; blockhash path untouched (Task 3 Step 3.3) |
| 11 — `Enabled` untouched on a floor trip | No code writes it; guarded by the unchanged `EvaluateFloor` body and reviewed in Task 2 |
| 12 — stuck round defers, never trips | Task 1 `ResolveSettledBalance_AddsBackAStakeRidingInAnOpenRound` (`WaitingBets` case) — a round that never leaves `WaitingBets` keeps counting |
| 13 — redelivery is idempotent | Existing behaviour: `EvaluateFloor` returns on `balance <= 0`, zero-out guarded by `ZeroOutNotifiedAt`. No new code; verified by the suite staying green |
| 14 — suite green, tests reported | Task 3 Steps 5-6 + the final report |

## Out of scope (do not implement)

- #153's orphaned placement claim; the missing stale-round timeout for races.
- Reordering the rollover in `RunGameConsumer`.
- A staleness cutoff / `RidingBetStaleAfterMinutes`; a planner eligibility gate; any Hangfire sweep.
- Writing `OrbitEnrollment.Enabled` on a floor trip.
- Any frontend change, any new notification, any `GET /orbit` field.
