# House seed / Orbit start conflict — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a round holding only the house seed bet start when a funded Orbit player is waiting, and make a parked round heal on the next Consumers boot.

**Architecture:** Two changes in `SpaceStroker.Consumers`, no new types. (1) In `BetPlacedConsumer`, move the existing `ExpectedOrbitBets is null or 0` → `PlanPlacements` block from below the under-subscribed early return to above the player-count branch, so any bet landing in a round still accepting bets re-runs Orbit planning. (2) In `SpaceStroker.Consumers/Program.cs`, add a `ReplanOpenRounds(app)` startup pass that publishes `GameOpenedEvent` for every round in `Game.GameAcceptingBets`. No migration, no entity, no DTO, no frontend file.

**Tech Stack:** .NET 8, MassTransit, EF Core (Npgsql), xUnit + Moq + Shouldly, Serilog.

**Spec:** `monkify-meta/specs/house-seed-orbit-conflict.md` (authoritative). **Card:** [#172](https://trello.com/c/nnn5uWri). **Branch:** `feature/house-seed-orbit-conflict`. **Level:** `Minor change` ⇒ **no** integration test is required, **no** dedicated `features/<slug>.md`.

## Global Constraints

- Work **only** inside `E:\Monkify\.worktrees\house-seed-orbit-conflict\monkify-backend` and `…\monkify-meta`. The hubs stay on `main`; never `git checkout -b` / `git switch` in a hub.
- **No EF migration, no entity change, no DTO change, no controller change, no SignalR payload change, and zero `monkify-front` files.**
- Money and background work stay in `SpaceStroker.Consumers`; the API host is not touched.
- Unit tests run with `scripts\run_tests.bat` — **never** a bare `dotnet test` (it drags in the `ProductionGames` Monte Carlo suite).
- ⚠️ The startup pass publishes **`GameOpenedEvent`**. It must **never** publish `BetPlacedEvent`: on an open lottery round past `MinimumWaitPeriodForBets` and under-subscribed, `BetPlacedConsumer` refunds every bet and marks it `NotEnoughPlayersToStart`, hours before the scheduled draw.
- `ReplanOpenRounds` must never throw into host startup — the game loop, typer and payouts outrank a recovery pass.
- Commit messages end with the `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` trailer.

---

## File Structure

| File | Responsibility | Change |
| --- | --- | --- |
| `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` | Decides whether a round starts | Move one existing block up; nothing added or deleted |
| `src/SpaceStroker.Consumers/Program.cs` | Host wiring + startup passes | Add `ReplanOpenRounds(app)` + one `using` |
| `src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs` | `BetPlacedConsumer` behaviour | Correct 1 existing test, add 4 |
| `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs` | Planner behaviour | Add 1 test (house bet counts as an existing bettor) |
| `monkify-meta/features/games.md` §14 | KB — house seed bet | Record the accepted zero-entropy exposure |
| `monkify-meta/features/orbit.md` §2 | KB — Orbit engine | Same, from the Orbit side |

---

### Task 1: Correct the misleading existing test, then hoist the re-plan

The existing test `ProcessMessage_RoundBecomesViableWithNoGate_AsksThePlannerToPlace` seeds `GameWithBets(betCount: 2)` — an **already viable** round — while its docstring claims it covers the bet that *makes* a round viable. That mismatch is why the dead trigger went unnoticed. Correcting it first turns it into the failing test for this task.

**Files:**
- Modify: `src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs:528-542`
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs:50-134`

**Interfaces:**
- Consumes: `IOrbitBetPlacementService.PlanPlacements(Game, CancellationToken)`, `Game.ExpectedOrbitBets` (`int?`), `IGameService.GetBetsByGameId(Guid)`, the test helper `GameWithBets(int betCount, int orbitBets)` already in the file.
- Produces: nothing new — no signature changes anywhere.

- [ ] **Step 1: Correct the existing test so it actually covers an under-subscribed round**

In `BetPlacedHandlerTests.cs`, replace the body of `ProcessMessage_RoundBecomesViableWithNoGate_AsksThePlannerToPlace` (keep the name and rewrite the docstring so it stops describing a round it never built):

```csharp
        /// <summary>
        /// The second placement trigger. The planner publishes nothing for a round that cannot reach its
        /// minimum, so the bet that arrives into an UNDER-SUBSCRIBED round is what has to pick Orbit back
        /// up — and with the house seed bet on every round, that bet is usually the house's.
        ///
        /// ⚠️ This test used to seed betCount: 2 — an already-viable round — which is exactly why the
        /// dead trigger went unnoticed: the call it verified sat BELOW the under-subscribed return and
        /// was unreachable in the only state it was written for. See specs/house-seed-orbit-conflict.md.
        /// </summary>
        [Fact]
        public async Task ProcessMessage_RoundBecomesViableWithNoGate_AsksThePlannerToPlace()
        {
            var game = GameWithBets(betCount: 1, orbitBets: 0);

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(game.Bets.ToList());

            await Consumer().ProcessMessage(new BetPlacedEvent(game.Id));

            _placementMock.Verify(x => x.PlanPlacements(It.Is<Game>(g => g.Id == game.Id), It.IsAny<CancellationToken>()), Times.Once);
            _gameServiceMock.Verify(x => x.TryStartGame(It.IsAny<Game>()), Times.Never);
        }
```

- [ ] **Step 2: Run it to make sure it fails**

```bat
cd /d E:\Monkify\.worktrees\house-seed-orbit-conflict\monkify-backend
scripts\run_tests.bat
```

Expected: `ProcessMessage_RoundBecomesViableWithNoGate_AsksThePlannerToPlace` **FAILS** — Moq reports `PlanPlacements` was called `Times.Never` instead of `Times.Once`, because the consumer returns at the player-count check before reaching it. Every other test still passes.

- [ ] **Step 3: Hoist the re-plan block in `BetPlacedConsumer`**

In `BetPlacedConsumer.ProcessMessage`, **delete** the block currently at lines 115-134 (the comment beginning `// 2. Place, if a human bet is what just made this round viable.` through the closing brace of `if (game.ExpectedOrbitBets is null or 0) { … }`) and **insert** this immediately after `var gameBets = await gameService.GetBetsByGameId(game.Id);`:

```csharp
            // Ask the engine to (re)plan BEFORE the under-subscribed return below, not after it.
            //
            // ⚠️ This call used to live below that return, which made it unreachable in the ONLY state
            // it was written for. specs/orbit-auto-bet-engine.md decision 4 says the bet that makes an
            // unviable round viable is what re-runs planning — but an under-subscribed round returns
            // first, so the bet Orbit could have filled never re-planned. With the house seed bet on
            // every round that became permanent: the house bet lands, the count is 1, we return, and a
            // funded Orbit player sits out every round forever. See specs/house-seed-orbit-conflict.md.
            //
            // Guard unchanged — null (never planned) OR 0 (planned, and there was nothing to wait for).
            // It used to be null ONLY, which silently excluded the most ordinary case there is: a round
            // opens with no eligible Orbit players, so planning stamps 0; the player then enables Orbit
            // and deposits mid-round — and that 0 blocked this trigger forever.
            //
            // A POSITIVE count still blocks: placements are in flight and the gate below is armed, which
            // is exactly the state that guard exists to protect. Re-planning at 0 is cheap (the planner
            // returns immediately when there are no candidates) and a double fan-out is already harmless
            // — the BetDebit claim index admits exactly one placement per (player, round).
            //
            // PlanPlacements refuses to move a stake into a round that cannot reach its minimum, so
            // running it here can never strand a lone Orbit stake in a race that will not start.
            if (game.ExpectedOrbitBets is null or 0)
            {
                var placement = scope.ServiceProvider.GetRequiredService<IOrbitBetPlacementService>();
                await placement.PlanPlacements(game);
            }
```

Then renumber the comment above the gate check: `// 1. Hold, if Orbit's bets are what make this round viable…` becomes `// Hold, if Orbit's bets are what make this round viable…` (there is no longer a numbered pair), and delete the now-orphaned lead-in line `// The round can start. Two Orbit concerns come before that, in this order.`, replacing it with `// The round can start. One Orbit concern comes before that.`

- [ ] **Step 4: Run the tests to verify they pass**

```bat
scripts\run_tests.bat
```

Expected: PASS, whole suite green. In particular `ProcessMessage_GateAlreadyArmed_DoesNotReTriggerPlacement`, `ProcessMessage_RoundPlannedEmptyBeforeThePlayerEnrolled_AsksThePlannerAgain` and `ProcessMessage_NoGateArmed_StartsTheGameAsBefore` must still pass untouched — if one goes red, the hoist changed behaviour it was not supposed to.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend add src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend commit -m "Re-plan Orbit before the under-subscribed return, not after it."
```

---

### Task 2: Pin the new behaviour with the cases that were missing

Four unit tests that describe the scenarios the card is actually about. These are additions — Task 1's corrected test already proves the hoist works; these stop it regressing and cover the guard's edges.

**Files:**
- Modify: `src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs`

**Interfaces:**
- Consumes: the file's existing `GameWithBets(int betCount, int orbitBets)` helper, `_gameServiceMock` (`Mock<IGameService>`), `_placementMock` (`Mock<IOrbitBetPlacementService>`), `Consumer()`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the four failing tests**

Add to `BetPlacedHandlerTests.cs`, after the corrected test from Task 1:

```csharp
        /// <summary>
        /// The card's own scenario (#172): the round holds ONLY the house seed bet — an ordinary paid bet
        /// with its own PlayerId — and one funded Orbit player is waiting. The house bet is what has to
        /// re-run planning, because the Orbit planner already ran at t≈0, before the seed's randomised
        /// placement delay expired, and skipped at "0 existing + 1 candidate < 2".
        /// </summary>
        [Fact]
        public async Task ProcessMessage_RoundHoldsOnlyTheHouseSeedBet_AsksThePlannerToPlace()
        {
            var game = GameWithBets(betCount: 1, orbitBets: 0);
            game.ExpectedOrbitBets = null;

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(game.Bets.ToList());

            await Consumer().ProcessMessage(new BetPlacedEvent(game.Id));

            _placementMock.Verify(x => x.PlanPlacements(It.Is<Game>(g => g.Id == game.Id), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Stamped 0 means "planning ran, nothing outstanding" — not "never look again". An
        /// under-subscribed round must still re-plan on it, or a player who enrolled after the round
        /// opened is locked out of it.
        /// </summary>
        [Fact]
        public async Task ProcessMessage_UnderSubscribedAndPlannedEmpty_AsksThePlannerAgain()
        {
            var game = GameWithBets(betCount: 1, orbitBets: 0);
            game.ExpectedOrbitBets = 0;

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(game.Bets.ToList());

            await Consumer().ProcessMessage(new BetPlacedEvent(game.Id));

            _placementMock.Verify(x => x.PlanPlacements(It.Is<Game>(g => g.Id == game.Id), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A POSITIVE count means placements are in flight and the gate is armed. The hoist must not
        /// widen the guard: re-planning here would re-dispatch players whose stakes are still confirming.
        /// </summary>
        [Fact]
        public async Task ProcessMessage_UnderSubscribedWithPlacementsInFlight_DoesNotReplan()
        {
            var game = GameWithBets(betCount: 1, orbitBets: 0);
            game.ExpectedOrbitBets = 1;
            game.OrbitGateExpiresAt = DateTime.UtcNow.AddSeconds(45);

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(game.Bets.ToList());

            await Consumer().ProcessMessage(new BetPlacedEvent(game.Id));

            _placementMock.Verify(x => x.PlanPlacements(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// ⚠️ The hoist runs BEFORE the lottery refund branch, so it must not disturb it. An
        /// under-subscribed lottery past the betting window still refunds every bet, still lands on
        /// NotEnoughPlayersToStart, and still creates its successor — and the planner call it now also
        /// makes is a no-op, because IsOrbitEligible refuses lotteries outright.
        /// </summary>
        [Fact]
        public async Task ProcessMessage_UnderSubscribedLotteryPastTheWindow_StillRefundsAndReplaces()
        {
            var game = GameWithBets(betCount: 1, orbitBets: 0);
            game.Parameters.LotteryParameters = new LotteryParameters { Frequency = TimeSpan.FromHours(24) };
            game.CreatedDate = DateTime.UtcNow.AddMinutes(-10);

            _gameServiceMock.Setup(x => x.GetGameById(game.Id)).ReturnsAsync(game);
            _gameServiceMock.Setup(x => x.GetBetsByGameId(game.Id)).ReturnsAsync(game.Bets.ToList());

            await Consumer().ProcessMessage(new BetPlacedEvent(game.Id));

            _messageBusMock.Verify(x => x.PublishEventAsync(It.IsAny<RefundInvalidBetEvent>()), Times.Once);
            _gameServiceMock.Verify(x => x.UpdateGameStatus(It.IsAny<Game>(), GameStatus.NotEnoughPlayersToStart), Times.Once);
            _gameServiceMock.Verify(x => x.CreateGame(It.IsAny<GameParameters>(), It.IsAny<decimal>()), Times.Once);
        }
```

> **Before running:** confirm the mock field for the bus is named `_messageBusMock` and that `LotteryParameters`, `GameStatus`, `GameParameters` and `RefundInvalidBetEvent` are already imported in this file. If a name differs, use the file's actual name — do **not** add a new mock field or a new `using` for a type the file already reaches by another name.

- [ ] **Step 2: Run them to verify the first three pass and the lottery one is honest**

```bat
scripts\run_tests.bat
```

Expected: all four PASS against the Task 1 implementation. The first three would have failed before the hoist; the lottery one passes both before and after **by design** — it is a regression guard proving the hoist did not disturb the refund path, so a green result here is the point, not a weak test.

- [ ] **Step 3: Commit**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend add src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend commit -m "Cover the house-seed + Orbit round and the guard's edges."
```

---

### Task 3: Pin that the house seed bet counts as an existing bettor

Decision 1 of the spec lives in `OrbitBetPlacementService`, not in the consumer: the viability sum counts the house bet like any other. Nothing changes in the service — this test asserts the behaviour the card depends on so a future "exclude house bets" idea has to break a test to land.

**Files:**
- Modify: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs`

**Interfaces:**
- Consumes: the file's existing `NewContext()` in-memory context helper and `Service(context)` factory, and its existing seeding helpers for `Game` / `GameParameters` / `OrbitEnrollment` / `OrbitBalance`.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Add to `OrbitBetPlacementServiceTests.cs`, following the file's existing arrange style (read the neighbouring tests and reuse their seeding helpers verbatim rather than inventing new ones):

```csharp
    /// <summary>
    /// #172: the house seed bet is an ORDINARY bet with its own PlayerId, so it counts toward the
    /// viability sum exactly like a human's. One house bet + one funded Orbit candidate reaches the
    /// minimum of 2, so the planner arms the gate and dispatches — this is what lets a round run with no
    /// human in it. Excluding house bets here would need a way to tell them apart, which
    /// specs/house-seed-bet.md decision 2 forbids by design.
    /// </summary>
    [Fact]
    public async Task PlanPlacements_HouseSeedBetIsTheOnlyOtherBettor_ArmsTheGateAndDispatches()
    {
        using var context = NewContext();
        var game = SeedGame(context, minimumPlayers: 2);

        // An ordinary paid bet from a player who is NOT the Orbit candidate — the shape of a house bet.
        SeedBet(context, game, playerId: Guid.NewGuid(), fundedByOrbit: false);
        var candidate = SeedFundedOrbitPlayer(context, game.Parameters);

        await Service(context).PlanPlacements(game);

        var reloaded = context.Games.Single(x => x.Id == game.Id);
        reloaded.ExpectedOrbitBets.ShouldBe(1);
        _messageBus.Verify(x => x.PublishEventAsync(It.Is<PlaceOrbitBetEvent>(e => e.PlayerId == candidate)), Times.Once);
    }
```

- [ ] **Step 2: Run it**

```bat
scripts\run_tests.bat
```

Expected: PASS immediately — the service already behaves this way; the test exists to pin it. If it FAILS, the helper names above do not match the file's actual helpers: fix the test to use the real ones. **Do not change `OrbitBetPlacementService` to make it pass** — the service is explicitly out of scope (spec §6.4).

- [ ] **Step 3: Commit**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend add src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend commit -m "Pin that a house seed bet counts toward Orbit viability."
```

---

### Task 4: The startup recovery pass

**Files:**
- Modify: `src/SpaceStroker.Consumers/Program.cs` (usings block; the `ApplicationStarted` callback at ~line 249; a new function beside `OpenNewGames` at ~line 517)

**Interfaces:**
- Consumes: `SpaceStrokerDbContext.Games`, `Game.GameAcceptingBets` (`GameStatus[]`), `IMessageBus.PublishEventAsync<T>(T)`, `GameOpenedEvent(Guid GameId, Guid ParametersId)`.
- Produces: `void ReplanOpenRounds(WebApplication app)` — a local function, called only from the `ApplicationStarted` callback.

- [ ] **Step 1: Add the missing using**

`GameOpenedEvent` lives in `SpaceStroker.Infrastructure.Contracts.Games`, which `Program.cs` does **not** import (it reaches `GameTypeDeactivatedEvent` through `…Consumers.GameTypeDeactivated`). Add, keeping the block's existing ordering:

```csharp
using SpaceStroker.Infrastructure.Contracts.Games;
```

⚠️ The file starts with a UTF-8 BOM — do not strip it.

- [ ] **Step 2: Call the pass from the `ApplicationStarted` callback**

Change the callback at `Program.cs:249-256` to:

```csharp
app.Lifetime.ApplicationStarted.Register(() =>
{
    ScheduleRecurringJobs(app);
    CloseOngoingGames(app);
    CreateDefaultGameParametersAsync(app);
    CorrectScheduledLotteryDraws(app);
    ReplanOpenRounds(app);
    OpenNewGames(app);
});
```

Placement is load-bearing, both sides: **after** `CloseOngoingGames` (which closes `GameInProgressStatus` = `[GameStarting, InProgress]`, so only `WaitingBets` survives to be re-planned) and **before** `OpenNewGames` (whose `CreateGame` publishes its own `GameOpenedEvent`, so a round this boot creates would otherwise be published twice).

- [ ] **Step 3: Write the function**

Insert immediately **before** `void OpenNewGames(WebApplication app)` (~line 517):

```csharp
// A round that was already open when this host went down gets no further trigger on its own: the Orbit
// planner only fires on GameOpenedEvent, and BetPlacedConsumer only fires on a bet. So a race parked
// with one house seed bet on it stays parked — and because races only roll over when they RUN, that one
// round stops every later round of its type. Republishing GameOpenedEvent re-plans it, and the house bet
// has landed by now, so viability is finally computed with it counted.
//
// ⚠️ GameOpenedEvent, NEVER BetPlacedEvent. On an open lottery round past MinimumWaitPeriodForBets and
// under-subscribed, BetPlacedConsumer refunds every bet and marks it NotEnoughPlayersToStart — Space
// Jackpot's window is HOURS, so a restart would destroy the round long before its 22:00 UTC draw.
// GameOpenedEvent reaches the Orbit planner and the house seeder only, and neither can end a round.
//
// Runs AFTER CloseOngoingGames (so nothing in GameStarting survives to be re-planned) and BEFORE
// OpenNewGames (whose CreateGame publishes its own GameOpenedEvent for the rounds it creates).
//
// Also re-runs HouseSeedBetConsumer, which is idempotent per round — a round left unseeded by a restart
// during the placement delay is seeded on this boot, and a seeded one short-circuits before any transfer.
//
// ⚠️ Safe here precisely because these steps run on ApplicationStarted rather than inline: the
// transport's database is provisioned by a hosted service, so an inline publish on a cold boot dies on
// `database "spacestroker_messaging" does not exist`.
void ReplanOpenRounds(WebApplication app)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceStrokerDbContext>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var openRounds = dbContext.Games
            .Where(x => Game.GameAcceptingBets.Contains(x.Status))
            .Select(x => new { x.Id, x.ParametersId })
            .ToList();

        foreach (var round in openRounds)
            messageBus.PublishEventAsync(new GameOpenedEvent(round.Id, round.ParametersId)).GetAwaiter().GetResult();

        if (openRounds.Count > 0)
            Log.Information("Re-planned {Count} open round(s) at startup.", openRounds.Count);
    }
    catch (Exception ex)
    {
        // Never take the host down for a recovery pass: the game loop, the typer and every payout
        // outrank it. A failure here degrades to today's behaviour — the round waits for a bet.
        Log.Error(ex, "Could not re-plan the open rounds at startup.");
    }
}
```

- [ ] **Step 4: Build and run the unit suite**

```bat
cd /d E:\Monkify\.worktrees\house-seed-orbit-conflict\monkify-backend
dotnet build src\SpaceStroker.Consumers\SpaceStroker.Consumers.csproj
scripts\run_tests.bat
```

Expected: build succeeds (no `GameOpenedEvent` / `IMessageBus` / `Game` resolution errors), suite green.

- [ ] **Step 5: Commit**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend add src/SpaceStroker.Consumers/Program.cs
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend commit -m "Re-plan every open round when Consumers starts."
```

---

### Task 5: Prove the pass does not disturb the integration harness

The Testcontainers harness boots the real Consumers host through `WebApplicationFactory<Program>`, so `ReplanOpenRounds` executes at fixture boot. It must be a no-op there — the fixture starts on a fresh database, and the pass runs before `OpenNewGames`, so there are no open rounds yet. This task verifies that empirically rather than by argument, because a regression here would turn every integration suite red for an unrelated reason.

**Files:**
- No production file changes expected. Run-only.

- [ ] **Step 1: Run the integration suite**

```bat
cd /d E:\Monkify\.worktrees\house-seed-orbit-conflict\monkify-backend
scripts\run_integration_tests.bat
```

Expected: green, with the same pass/fail counts as `origin/main`. Docker must be running.

- [ ] **Step 2: If anything is red, diagnose before touching production code**

Compare against the baseline:

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend stash
# run scripts\run_integration_tests.bat again on the clean tree, record counts
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend stash pop
```

A failure present on both trees is pre-existing and **not** this card's to fix — report it. A failure only on the feature branch means the pass perturbed the harness: read the failing test's assumptions about round state before changing anything, and **stop and ask** rather than special-casing `ReplanOpenRounds` for tests.

- [ ] **Step 3: No commit** unless Step 2 required a change.

---

### Task 6: Knowledge-base corrections

Level is `Minor change`, so there is **no** dedicated `features/house-seed-orbit-conflict.md`. The two existing docs carry the correction. This satisfies acceptance criterion 14.

**Files:**
- Modify: `monkify-meta/features/games.md` §14 (in `.worktrees/house-seed-orbit-conflict/monkify-meta`)
- Modify: `monkify-meta/features/orbit.md` §2

**Interfaces:** none — documentation.

- [ ] **Step 1: Append to `features/games.md` §14, after the `FundedByOrbit` warning**

```markdown
⚠️ **A round may run on the house seed bet plus Orbit bets alone, with no human in it** — and such a
round carries **zero non-house entropy**, because both the house bet's `Seed` and every Orbit bet's
`Seed` are `OrbitChoiceGenerator.DeriveSeed(ServerSeed, playerId)`. This is a **deliberate, accepted**
exposure, not an oversight: [`../specs/orbit-auto-bet-engine.md`](../specs/orbit-auto-bet-engine.md)
decision 13 already accepted it for Orbit-only rounds, and #172 extended that acceptance to rounds the
house **bets in**. It sits alongside — and does not weaken — the AC 21 invariant that a type with
`MinimumNumberOfPlayers < 2` is never seeded at all. See
[`../specs/house-seed-orbit-conflict.md`](../specs/house-seed-orbit-conflict.md).
```

- [ ] **Step 2: Append to `features/orbit.md` §2, after the "Every eligible enrolled player bets every round" bullet**

```markdown
- ⚠️ **The house seed bet counts as an ordinary existing bettor in Orbit's viability check** (#172), so
  on a `MinimumNumberOfPlayers = 2` type one funded Orbit player is enough to start a round with no
  human in it. Two defects hid this: the planner runs at t≈0 while the house bet waits a randomised
  0–10s, and `BetPlacedConsumer`'s re-plan trigger sat below its under-subscribed early return — which
  also broke the plain 1-human + 1-Orbit case decision 4 promised. Both are fixed in
  [`../specs/house-seed-orbit-conflict.md`](../specs/house-seed-orbit-conflict.md), together with a
  Consumers startup pass that re-plans every open round.
```

- [ ] **Step 3: Commit**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-meta add features/games.md features/orbit.md docs/superpowers/plans/2026-08-16-house-seed-orbit-conflict.md
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-meta commit -m "Record the accepted zero-entropy house-vs-Orbit round."
```

---

### Task 7: Push both branches

- [ ] **Step 1: Verify both suites are green**

```bat
cd /d E:\Monkify\.worktrees\house-seed-orbit-conflict\monkify-backend
scripts\run_tests.bat
scripts\run_integration_tests.bat
```

- [ ] **Step 2: Push**

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend push origin feature/house-seed-orbit-conflict
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-meta push origin feature/house-seed-orbit-conflict
```

- [ ] **Step 3: Confirm the remote SHAs match local HEAD** (required before building the compare links)

```bash
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend rev-parse HEAD
git -C E:/Monkify/.worktrees/house-seed-orbit-conflict/monkify-backend ls-remote --heads origin feature/house-seed-orbit-conflict
```

---

## Self-Review

**Spec coverage.** §6.1 hoist → Task 1. §6.2 startup pass → Task 4. §6.3 (`GameOpenedEvent` not `BetPlacedEvent`) → Task 4 Step 3 comment + Task 2's lottery regression test. §6.4 "does not change" → enforced by Task 1 Step 4 and Task 3 Step 2's "do not change the service". §10.1 both backend files → Tasks 1 and 4. §10.2 KB → Task 6. §11 unit tests → Tasks 1–3. §11's note that `ReplanOpenRounds` is not directly unit-testable → Task 5 covers it by regression instead. AC 1/2/3 → Tasks 1–2. AC 4 → Task 2 test 3. AC 5 → Task 3. AC 6 → verified by code reading during refinement, no test (the claim index is pre-existing behaviour). AC 7/9/10/11 → Task 4's implementation + Step 13 manual QA in the spec. AC 8 → Task 2's lottery test (unit) + spec manual QA step 3. AC 12/13 → Tasks 1–4 touch no DTO/entity/front file. AC 14 → Task 6. AC 15 → Task 7 Step 1.

**Placeholder scan.** No TBD / "handle edge cases" / "similar to Task N" — every code step carries its code. Task 2 Step 1 and Task 3 Step 1 tell the implementer to match the test file's existing helper and mock names rather than inventing them, and say what to do if they differ; that is a real instruction, not a placeholder.

**Type consistency.** `PlanPlacements(Game, CancellationToken)`, `ExpectedOrbitBets` (`int?`), `OrbitGateExpiresAt` (`DateTime?`), `GameOpenedEvent(Guid GameId, Guid ParametersId)`, `Game.GameAcceptingBets` (`GameStatus[]`), `IMessageBus.PublishEventAsync<T>(T)` — all match the shipped signatures read during spec exploration. `ReplanOpenRounds` is named identically in Task 4 Steps 2 and 3.
