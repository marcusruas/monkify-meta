# Hyper Roll Solo Rounds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every Hyper Roll round belongs to one player: they open their own board, reroll it with the lever while unbet, and their bet runs their round immediately.

**Architecture:** Private rounds on the existing `Game` table. A new `OwnerPlayerId` is set by the one and only bet; unbet boards carry a `BoardExpiresAt` and are cleaned up by a job. New endpoints open, reroll and read a board; the bet handler enforces one bettor per round inside the existing global house-edge coverage lock. Automatic round creation is skipped for house-edge types. The front loads/opens/rerolls its own board, routes the lever, and moves Hyper Roll chat to a per-type channel.

**Tech Stack:** .NET 8, EF Core (Postgres; unit tests on the InMemory provider), MediatR handlers, MassTransit consumers, Hangfire, xUnit + Moq + Shouldly, Testcontainers integration suite; Vue 3 + TypeScript SPA with SignalR.

**Spec:** `monkify-meta/specs/hyper-roll-solo-rounds.md` (decision 35 in `specs/hyper-roll.md`). Read it before any task.

## Global Constraints

- Worktrees only: backend `C:\Users\kurok\Desktop\monkify\.worktrees\hyper-roll\monkify-backend`, front `...\monkify-front`, docs `...\monkify-meta`. Branch `feature/hyper-roll`. Never touch the hub checkouts.
- **Do not commit or push** unless the owner has approved commits for this run (CLAUDE.md). Where a task says "Checkpoint", run the listed verification and stop; commit only if approved.
- Every behaviour change is gated on `GameParameters.IsHouseEdge`; legacy types stay byte-identical.
- Every rule that decides whether a paid bet is accepted runs inside the house-edge coverage lock (`IHouseEdgeCoverageService.AcquireLockAsync`), and a refused paid bet goes through `RefundInvalidBet`.
- Unit tests use the EF **InMemory** provider: no raw SQL, `ExecuteUpdateAsync` or `ExecuteDeleteAsync` in code paths that unit tests exercise. Correctness under concurrency comes from the global advisory lock, which every writer here (bet claim, reroll, cleanup) takes.
- Vague refusals (`constraints.md` §6): reuse `ErrorMessages.GameNotValidForBets` / `ParameterNotFound` / `ParameterHasNoActiveGames`; the maintenance message is unchanged. No new player-facing error strings on the backend.
- EF config inline in `SpaceStrokerDbContext.OnModelCreating`; never edit an existing migration or hand-edit the snapshot.
- Backend unit tests: `scripts\run_tests.bat` (never a bare `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker must be running).
- Front: `yarn -s lint` must pass (use `npx eslint --fix <files>` for prettier).
- Values copied from the spec: board TTL **30** min (`HouseEdgeBoardTtlMinutes`), cleanup every **5** min (`HouseEdgeBoardCleanupIntervalMinutes`), `HouseEdgeTimeUntilGameStarts` **0**, rate limits `POST:/games/*/boards` **30/min**, `POST:/games/rounds/*/reroll` **60/min**, `GET:/games/rounds/*` **120/min**, localStorage key `hyperRoll:board:<gameTypeId>`.

## File Map

Backend (`src/`):
- `SpaceStroker.Domain/Configs/Entities/GameSettings.cs` — nullable start delay, TTL, cleanup interval.
- `SpaceStroker.Common/spacestrokersettings.json` — config values + rate-limit rules.
- `SpaceStroker.Domain/Games/Entities/Game.cs` — `OwnerPlayerId`, `Owner`, `BoardExpiresAt`, `HouseEdgeOpenStatus`.
- `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — FK + indexes; new migration.
- `SpaceStroker.Infrastructure/Services/Games/IGameService.cs`, `GameService.cs` — shared board draw, `OpenSoloBoard`, `RerollSoloBoard`.
- `SpaceStroker.Infrastructure/Handlers/Games/OpenHouseEdgeBoard/` (new), `RerollHouseEdgeBoard/` (new), `GetHouseEdgeRound/` (new).
- `SpaceStroker.Infrastructure/Handlers/Games/GetActiveGameForParameter/GetActiveGameForParameterHandler.cs` — house-edge by wallet.
- `SpaceStroker.Infrastructure/Handlers/Games/CheckBetAvailability/*` — `GameId`.
- `SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs`, `IHouseEdgeCoverageService.cs` — drop `FindOpenRoundAsync`.
- `SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` — one bettor, owner claim, board re-check, free bets locked.
- `SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs`, `Consumers/GameParametersCreated/GameParametersCreatedConsumer.cs`, `SpaceStroker.Consumers/Program.cs` — skip house-edge auto-open/replan; schedule cleanup.
- `SpaceStroker.Infrastructure/Jobs/HouseEdgeBoardCleanupJob.cs` (new).
- `SpaceStroker.Api/Controllers/GamesController.cs` — three routes.
- Tests: `SpaceStroker.Tests/UnitTests/...` and `SpaceStroker.Tests.Integration/...` as listed per task.

Front (`src/`): `types/index.ts`, `services/mainService.ts`, `hooks/useGameHub.ts`, `hooks/useGame.ts`, `hooks/useGameChat.ts`, `hooks/useLiveGames.ts`, `components/game/GameConsole.vue`, `components/game/GameTerminal.vue`, `components/game/GameStatsPanel.vue`.

---

### Task 1: Start delay, board TTL settings and rate limits

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/GameSettings.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (`GeneralSettings:Games` ~line 94, `IpRateLimiting` rules ~line 71)
- Create: `src/SpaceStroker.Tests/UnitTests/Configs/GameSettingsTests.cs`

**Interfaces:**
- Produces: `int? GameSettings.HouseEdgeTimeUntilGameStarts`, `int GameSettings.HouseEdgeBoardTtlMinutes` (default 30), `int GameSettings.HouseEdgeBoardCleanupIntervalMinutes` (default 5), `int GameSettings.SecondsUntilGameStarts(bool isHouseEdge)` (0 now means immediate).

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using SpaceStroker.Domain.Configs.Entities;
using Xunit;

namespace SpaceStroker.Tests.UnitTests.Configs
{
    public class GameSettingsTests
    {
        [Fact]
        public void HouseEdge_WithZeroDelay_StartsImmediately()
            => new GameSettings { TimeUntilGameStarts = 10, HouseEdgeTimeUntilGameStarts = 0 }
                .SecondsUntilGameStarts(isHouseEdge: true).ShouldBe(0);

        [Fact]
        public void HouseEdge_WithMissingDelay_FallsBackToTheSharedCountdown()
            => new GameSettings { TimeUntilGameStarts = 10, HouseEdgeTimeUntilGameStarts = null }
                .SecondsUntilGameStarts(isHouseEdge: true).ShouldBe(10);

        [Fact]
        public void LegacyType_IgnoresTheHouseEdgeDelay()
            => new GameSettings { TimeUntilGameStarts = 10, HouseEdgeTimeUntilGameStarts = 0 }
                .SecondsUntilGameStarts(isHouseEdge: false).ShouldBe(10);

        [Fact]
        public void BoardTtlAndCleanupInterval_HaveSafeDefaults()
        {
            var settings = new GameSettings();
            settings.HouseEdgeBoardTtlMinutes.ShouldBe(30);
            settings.HouseEdgeBoardCleanupIntervalMinutes.ShouldBe(5);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run (from `monkify-backend`): `cd src\SpaceStroker.Tests && dotnet test --filter "FullyQualifiedName~GameSettingsTests"`
Expected: build error — `HouseEdgeTimeUntilGameStarts` is `int` (cannot assign null) and the TTL properties do not exist.

- [ ] **Step 3: Implement**

In `GameSettings.cs` replace the `HouseEdgeTimeUntilGameStarts` property and `SecondsUntilGameStarts` with:

```csharp
        /// <summary>
        /// Start delay, in seconds, for a player-vs-house round (GameParameters.IsHouseEdge — Hyper Roll). Solo rounds
        /// (specs/hyper-roll-solo-rounds.md) set it to 0: nobody else can join, so the round starts as soon as its one
        /// bet lands. Null (missing from config) falls back to TimeUntilGameStarts, so an older config keeps working.
        /// </summary>
        public int? HouseEdgeTimeUntilGameStarts { get; set; }

        /// <summary>How long an unbet solo board lives without being read, rerolled or pre-checked (minutes).</summary>
        public int HouseEdgeBoardTtlMinutes { get; set; } = 30;

        /// <summary>How often HouseEdgeBoardCleanupJob deletes expired unbet boards (minutes).</summary>
        public int HouseEdgeBoardCleanupIntervalMinutes { get; set; } = 5;
```

and

```csharp
        public int SecondsUntilGameStarts(bool isHouseEdge)
        {
            if (isHouseEdge && HouseEdgeTimeUntilGameStarts is >= 0)
                return HouseEdgeTimeUntilGameStarts.Value;

            return TimeUntilGameStarts;
        }
```

Then `grep -rn "HouseEdgeTimeUntilGameStarts" src --include=*.cs` and fix any other reader that assumed `int` (compile errors will point to them).

In `spacestrokersettings.json` under `GeneralSettings:Games`: set `"HouseEdgeTimeUntilGameStarts": 0` and add `"HouseEdgeBoardTtlMinutes": 30`, `"HouseEdgeBoardCleanupIntervalMinutes": 5`. In `IpRateLimiting:GeneralRules`, next to the existing `*:/games/*/availability` rule and in the same shape, add:

```json
      { "Endpoint": "post:/games/*/boards", "Period": "1m", "Limit": 30 },
      { "Endpoint": "post:/games/rounds/*/reroll", "Period": "1m", "Limit": 60 },
      { "Endpoint": "get:/games/rounds/*", "Period": "1m", "Limit": 120 }
```

(match the existing rule's casing/verb format exactly — copy the availability rule and edit it).

- [ ] **Step 4: Run to verify they pass**

Run: `cd src\SpaceStroker.Tests && dotnet test --filter "FullyQualifiedName~GameSettingsTests|FullyQualifiedName~RateLimitSettingsTests"`
Expected: PASS (the rate-limit guard accepts the rules because none contains `/bets`).

- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 2: Round owner and board expiry on `Game` + migration

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (the `Game` builder, ~lines 129-172)
- Create (generated): `src/SpaceStroker.Infrastructure/Migrations/<timestamp>_HyperRollSoloRounds.cs` (+ Designer, snapshot update)

**Interfaces:**
- Produces: `Guid? Game.OwnerPlayerId`, `Player? Game.Owner`, `DateTime? Game.BoardExpiresAt`, `static GameStatus[] Game.HouseEdgeUnfinishedStatus = [GameStarting, InProgress, Ended, RewardForWinnersInProgress]`.

- [ ] **Step 1: Add the properties** (after `OrbitGateExpiresAt` in `Game.cs`):

```csharp
        /// <summary>
        /// House-edge solo rounds (specs/hyper-roll-solo-rounds.md): the one player this round belongs to. Null while
        /// the board is unbet; set in the same transaction as the round's first and only bet, never changed after.
        /// Always null for every other type.
        /// </summary>
        public Guid? OwnerPlayerId { get; set; }
        public Player? Owner { get; set; }

        /// <summary>
        /// When an unbet solo board may be deleted by HouseEdgeBoardCleanupJob. Refreshed by every read, reroll and
        /// availability pre-check; cleared when a bet binds the owner. Null for every other round.
        /// </summary>
        public DateTime? BoardExpiresAt { get; set; }
```

and next to the other status arrays:

```csharp
        public static GameStatus[] HouseEdgeUnfinishedStatus = [GameStarting, InProgress, Ended, RewardForWinnersInProgress];
```

- [ ] **Step 2: EF configuration** — inside `modelBuilder.Entity<Game>(builder => { ... })` add:

```csharp
                // Solo rounds: the owner is a player; a player row is never deleted while it owns a round.
                builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerPlayerId).OnDelete(DeleteBehavior.Restrict);
                // "My unfinished round" lookup (GET /games/{typeId}?wallet=).
                builder.HasIndex(x => new { x.ParametersId, x.OwnerPlayerId, x.Status });
                // HouseEdgeBoardCleanupJob only ever scans unbet boards.
                builder.HasIndex(x => x.BoardExpiresAt).HasFilter("\"BoardExpiresAt\" IS NOT NULL");
```

- [ ] **Step 3: Generate the migration** — follow the exact `dotnet ef migrations add` invocation in `monkify-meta/conventions/backend.md` (Migrations section), with the name `HyperRollSoloRounds`. Open the generated `Up()` and confirm it only adds `OwnerPlayerId` (uuid, nullable), `BoardExpiresAt` (timestamp, nullable), the FK and the two indexes — no drops, no data changes.

- [ ] **Step 4: Verify** — `dotnet build src/SpaceStroker.sln` succeeds and `scripts\run_tests.bat` passes.

- [ ] **Step 5: Checkpoint.**

---

### Task 3: Shared board draw, `OpenSoloBoard` and `RerollSoloBoard` in `GameService`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/IGameService.cs`, `GameService.cs` (`CreateGame` ~lines 502-614)
- Create: `src/SpaceStroker.Tests/UnitTests/Services/GameServiceSoloBoardTests.cs`

**Interfaces:**
- Consumes: `GameSettings.HouseEdgeBoardTtlMinutes` (Task 1), `Game.BoardExpiresAt`/`OwnerPlayerId` (Task 2).
- Produces:
  - `Task<Guid?> IGameService.OpenSoloBoard(GameParameters parameters)` — opens an unowned `WaitingBets` round with a drawn board, a committed seed and `BoardExpiresAt = now + TTL`; **no** hub broadcast, **no** `GameOpenedEvent`; null when the type cannot draw a board.
  - `Task<bool> IGameService.RerollSoloBoard(Guid gameId)` — caller MUST hold the coverage lock; true when the board was redrawn and the seed rotated.

- [ ] **Step 1: Write the failing tests** — build the service the way the existing `GameService` unit tests do (find one with `grep -rln "new GameService(" src/SpaceStroker.Tests` and copy its constructor arrangement: context, settings, hub mock, message bus mock). Seed a house-edge `GameParameters` with a valid ladder and pool using the same helper the existing house-edge unit tests use (`grep -rn "IsHouseEdge = true" src/SpaceStroker.Tests/UnitTests | head` → reuse that fixture/builder). Tests:

```csharp
        [Fact]
        public async Task OpenSoloBoard_OpensAnUnownedWaitingRound_WithABoardASeedAndAnExpiry_WithoutBroadcasting()
        {
            var (service, context, hubClients, bus) = BuildService();
            var parameters = await SeedHyperRollAsync(context);

            var gameId = await service.OpenSoloBoard(parameters);

            gameId.ShouldNotBeNull();
            var game = await context.Games.Include(x => x.BoardChoices).SingleAsync(x => x.Id == gameId);
            game.Status.ShouldBe(GameStatus.WaitingBets);
            game.OwnerPlayerId.ShouldBeNull();
            game.BoardChoices.Count.ShouldBe(8);
            game.BoardExpiresAt.ShouldNotBeNull();
            game.BoardExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(29));
            hubClients.Verify(x => x.All, Times.Never);
            bus.Verify(x => x.PublishEventAsync(It.IsAny<GameOpenedEvent>()), Times.Never);
        }

        [Fact]
        public async Task RerollSoloBoard_ReplacesTheBoard_RotatesTheSeed_AndRefreshesTheExpiry()
        {
            var (service, context, _, _) = BuildService();
            var parameters = await SeedHyperRollAsync(context);
            var gameId = (await service.OpenSoloBoard(parameters))!.Value;
            var before = await context.Games.AsNoTracking().Include(x => x.BoardChoices).SingleAsync(x => x.Id == gameId);
            context.ChangeTracker.Clear();

            (await service.RerollSoloBoard(gameId)).ShouldBeTrue();

            var after = await context.Games.AsNoTracking().Include(x => x.BoardChoices).SingleAsync(x => x.Id == gameId);
            after.BoardChoices.Count.ShouldBe(8);
            after.BoardChoices.Select(x => x.Id).ShouldNotBe(before.BoardChoices.Select(x => x.Id), ignoreOrder: true);
            after.ServerSeedHash.ShouldNotBe(before.ServerSeedHash);
            after.BoardExpiresAt.ShouldNotBeNull();
        }

        [Theory]
        [InlineData("owned")]
        [InlineData("bet")]
        [InlineData("started")]
        public async Task RerollSoloBoard_RefusesARoundThatIsNoLongerAnUnbetBoard(string state)
        {
            var (service, context, _, _) = BuildService();
            var parameters = await SeedHyperRollAsync(context);
            var gameId = (await service.OpenSoloBoard(parameters))!.Value;
            var game = await context.Games.SingleAsync(x => x.Id == gameId);
            if (state == "owned") game.OwnerPlayerId = Guid.NewGuid();
            if (state == "started") game.Status = GameStatus.GameStarting;
            if (state == "bet") context.GameBets.Add(new Bet(gameId, "seed", null, "rocket", 0.05m, 1));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            (await service.RerollSoloBoard(gameId)).ShouldBeFalse();
        }
```

(If `OwnerPlayerId = Guid.NewGuid()` trips the InMemory FK check, insert a `Player` first and use its id. Match the `Bet` constructor signature used in `RegisterBetHandler` line 114.)

- [ ] **Step 2: Run to verify they fail**

Run: `cd src\SpaceStroker.Tests && dotnet test --filter "FullyQualifiedName~GameServiceSoloBoardTests"`
Expected: build error — `OpenSoloBoard` / `RerollSoloBoard` do not exist.

- [ ] **Step 3: Implement** — in `GameService.cs`, extract the house-edge block of `CreateGame` (from `var stored = ...` through `game.BoardChoices = board;`) into:

```csharp
        /// <summary>
        /// Draws a validated house-edge board for a type, reading the type row and its active pool from the DATABASE
        /// (never the caller's object — see CreateGame). Returns null (logged) when the type is not house-edge-drawable.
        /// Shared by CreateGame, OpenSoloBoard and RerollSoloBoard so the three can never draw differently.
        /// </summary>
        private async Task<(bool IsHouseEdge, List<GameBoardChoice>? Board)> DrawHouseEdgeBoard(Guid parametersId, GameParameters fallback)
        {
            var stored = await _context.GameParameters
                .AsNoTracking()
                .Include(x => x.PresetChoices.Where(c => c.Active))
                .FirstOrDefaultAsync(x => x.Id == parametersId);

            var houseEdgeSource = stored ?? fallback;
            if (!houseEdgeSource.IsHouseEdge)
                return (false, null);

            var pool = stored?.PresetChoices?.ToList() ?? new List<PresetChoice>();

            var validation = HouseEdgeRules.Validate(houseEdgeSource, pool);
            if (!validation.IsValid)
            {
                Log.Error("House-edge game type {ParametersId} is misconfigured; no round opened: {Reason}", parametersId, validation.Reason);
                return (true, null);
            }

            var board = HouseEdgeBoard.Draw(pool, HouseEdgeRules.ParseLadder(houseEdgeSource.BoardLadder), Random.Shared);
            if (board == null)
            {
                Log.Error("House-edge game type {ParametersId} cannot fill its board ladder {Ladder}; no round opened.", parametersId, houseEdgeSource.BoardLadder);
                return (true, null);
            }

            var boardValidation = HouseEdgeRules.ValidateBoard(board);
            if (!boardValidation.IsValid)
            {
                Log.Error("House-edge game type {ParametersId} drew an invalid board; no round opened: {Reason}", parametersId, boardValidation.Reason);
                return (true, null);
            }

            return (true, board);
        }
```

Keep the existing explanatory comments from `CreateGame` on the moved lines. `CreateGame` becomes:

```csharp
                var (isHouseEdge, board) = await DrawHouseEdgeBoard(parameters.Id, parameters);
                if (isHouseEdge)
                {
                    if (board == null)
                        return;

                    foreach (var boardChoice in board)
                        boardChoice.GameId = game.Id;

                    game.BoardChoices = board;
                }
```

Add the two new members (and declare them on `IGameService` with the XML docs from the Interfaces block above):

```csharp
        public async Task<Guid?> OpenSoloBoard(GameParameters parameters)
        {
            var (isHouseEdge, board) = await DrawHouseEdgeBoard(parameters.Id, parameters);
            if (!isHouseEdge || board == null)
                return null;

            var game = new Game(parameters.Id)
            {
                BoardExpiresAt = DateTime.UtcNow.AddMinutes(_settings.Games.HouseEdgeBoardTtlMinutes),
            };
            foreach (var boardChoice in board)
                boardChoice.GameId = game.Id;
            game.BoardChoices = board;

            // Commit the seed in the same save as the board, so a solo board is never visible without its commitment.
            var serverSeed = ProvablyFair.GenerateServerSeed();
            game.ServerSeed = serverSeed;
            game.ServerSeedHash = ProvablyFair.HashServerSeed(serverSeed);

            await _context.Games.AddAsync(game);
            await _context.SaveChangesAsync();

            // ⚠️ No GameReceived broadcast and no GameOpenedEvent (spec T5): a solo board concerns one browser.
            return game.Id;
        }

        public async Task<bool> RerollSoloBoard(Guid gameId)
        {
            var game = await _context.Games.Include(x => x.Parameters).FirstOrDefaultAsync(x => x.Id == gameId);
            if (game == null || game.Parameters?.IsHouseEdge != true || game.OwnerPlayerId != null || game.Status != GameStatus.WaitingBets)
                return false;

            if (await _context.GameBets.AnyAsync(x => x.GameId == gameId))
                return false;

            var (_, board) = await DrawHouseEdgeBoard(game.ParametersId, game.Parameters);
            if (board == null)
                return false;

            // Tracked remove + add rather than ExecuteDelete: the caller holds the coverage lock inside a transaction
            // on this same context, and the unit tests run on the InMemory provider.
            var oldBoard = await _context.GameBoardChoices.Where(x => x.GameId == gameId).ToListAsync();
            _context.GameBoardChoices.RemoveRange(oldBoard);
            foreach (var boardChoice in board)
                boardChoice.GameId = gameId;
            await _context.GameBoardChoices.AddRangeAsync(board);

            // A new board gets a new commitment: the hash the player sees always matches the board they bet on.
            var serverSeed = ProvablyFair.GenerateServerSeed();
            game.ServerSeed = serverSeed;
            game.ServerSeedHash = ProvablyFair.HashServerSeed(serverSeed);
            game.BoardExpiresAt = DateTime.UtcNow.AddMinutes(_settings.Games.HouseEdgeBoardTtlMinutes);

            await _context.SaveChangesAsync();
            return true;
        }
```

(Use whatever the existing field names are for settings in `GameService` — `_settings` holds `GeneralSettings`, so `_settings.Games.HouseEdgeBoardTtlMinutes`.)

- [ ] **Step 4: Run to verify they pass** — the three new tests plus `CreateGameTests` and every existing house-edge `GameService` test: `dotnet test --filter "FullyQualifiedName~GameServiceSoloBoardTests|FullyQualifiedName~CreateGame"`. Expected: PASS.

- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 4: Open and read a board — `POST /games/{gameTypeId}/boards`, `GET /games/rounds/{roundId}`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/OpenHouseEdgeBoard/OpenHouseEdgeBoardRequest.cs`, `OpenHouseEdgeBoardHandler.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/GetHouseEdgeRound/GetHouseEdgeRoundRequest.cs`, `GetHouseEdgeRoundHandler.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs` (add `BoardExpiresAt`)
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs`
- Create: `src/SpaceStroker.Tests/UnitTests/Handlers/OpenHouseEdgeBoardHandlerTests.cs`, `GetHouseEdgeRoundHandlerTests.cs`

**Interfaces:**
- Consumes: `IGameService.OpenSoloBoard` (Task 3).
- Produces: `record OpenHouseEdgeBoardRequest(Guid GameTypeId) : IRequest<ActiveGameDto>`; `record GetHouseEdgeRoundRequest(Guid RoundId) : IRequest<ActiveGameDto>`; `ActiveGameDto.BoardExpiresAt` (`DateTime?`).

- [ ] **Step 1: Write the failing tests** (pattern: `CheckBetAvailabilityHandlerTests` — `UnitTestsClass`, `Mock<IGameService>`, `new SpaceStrokerDbContext(ContextOptions)`, `Messaging`):

```csharp
    public class OpenHouseEdgeBoardHandlerTests : UnitTestsClass
    {
        private readonly Mock<IGameService> _gameService = new();

        private GameParameters HyperRoll(bool active = true, bool comingSoon = false, bool houseEdge = true) => new()
        {
            Name = "Hyper Roll", Description = "house edge", AllowedCharacters = GameCharacterType.Letters,
            RequiredAmount = 0.01m, MaximumStake = 0.1m, MinimumNumberOfPlayers = 1,
            Active = active, ComingSoon = comingSoon, IsHouseEdge = houseEdge,
        };

        [Fact]
        public async Task OpensABoard_AndReturnsItsRound()
        {
            var parameters = HyperRoll();
            using var context = new SpaceStrokerDbContext(ContextOptions);
            context.GameParameters.Add(parameters);
            var game = new Game(parameters.Id) { Status = GameStatus.WaitingBets, ServerSeedHash = "hash" };
            context.Games.Add(game);
            await context.SaveChangesAsync();
            _gameService.Setup(x => x.GetGameParametersById(parameters.Id)).ReturnsAsync(parameters);
            _gameService.Setup(x => x.OpenSoloBoard(parameters)).ReturnsAsync(game.Id);

            var handler = new OpenHouseEdgeBoardHandler(context, Messaging, _gameService.Object);
            var dto = await handler.HandleRequest(new OpenHouseEdgeBoardRequest(parameters.Id), CancellationToken);

            dto.GameId.ShouldBe(game.Id);
            Messaging.HasValidationFailures().ShouldBeFalse();
        }

        [Theory]
        [InlineData(false, false, true)]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        public async Task RefusesInactiveComingSoonOrLegacyTypes_WithTheVagueNotFound(bool active, bool comingSoon, bool houseEdge)
        {
            var parameters = HyperRoll(active, comingSoon, houseEdge);
            _gameService.Setup(x => x.GetGameParametersById(parameters.Id)).ReturnsAsync(parameters);
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var handler = new OpenHouseEdgeBoardHandler(context, Messaging, _gameService.Object);

            await Should.ThrowAsync<Exception>(() => handler.HandleRequest(new OpenHouseEdgeBoardRequest(parameters.Id), CancellationToken));

            _gameService.Verify(x => x.OpenSoloBoard(It.IsAny<GameParameters>()), Times.Never);
        }
    }
```

(`Messaging.ReturnValidationFailureMessage` throws a `ControlledException`; if the existing tests assert that type, use `Should.ThrowAsync<ControlledException>` instead.)

`GetHouseEdgeRoundHandlerTests`: (a) an unowned `WaitingBets` house-edge round is returned and its `BoardExpiresAt` moves forward; (b) a round in `RewardForWinnersCompleted` is refused; (c) a legacy (non-house-edge) round is refused; (d) an owned `InProgress` round is returned and its `BoardExpiresAt` stays null.

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~OpenHouseEdgeBoardHandlerTests|FullyQualifiedName~GetHouseEdgeRoundHandlerTests"` → build errors.

- [ ] **Step 3: Implement**

`OpenHouseEdgeBoardRequest.cs`:

```csharp
using MediatR;
using SpaceStroker.Infrastructure.Contracts.Games;

namespace SpaceStroker.Infrastructure.Handlers.Games.OpenHouseEdgeBoard
{
    /// <summary>Open a private board for a house-edge type (specs/hyper-roll-solo-rounds.md §6.1).</summary>
    public record OpenHouseEdgeBoardRequest(Guid GameTypeId) : IRequest<ActiveGameDto>;
}
```

`OpenHouseEdgeBoardHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SpaceStroker.Common.Messaging;
using SpaceStroker.Common.Resources;
using SpaceStroker.Infrastructure.Contracts.Games;
using SpaceStroker.Infrastructure.Data.Context;
using SpaceStroker.Infrastructure.Services.Games;

namespace SpaceStroker.Infrastructure.Handlers.Games.OpenHouseEdgeBoard
{
    /// <summary>
    /// Opens a player's own Hyper Roll board: an unowned WaitingBets round that the first bet binds to a wallet.
    /// ⚠️ Maintenance does not block opening — a player may look; betting is what the switch refuses.
    /// </summary>
    public class OpenHouseEdgeBoardHandler : BaseRequestHandler<OpenHouseEdgeBoardRequest, ActiveGameDto>
    {
        private readonly IGameService _gameService;

        public OpenHouseEdgeBoardHandler(SpaceStrokerDbContext context, IMessaging messaging, IGameService gameService)
            : base(context, messaging)
        {
            _gameService = gameService;
        }

        public override async Task<ActiveGameDto> HandleRequest(OpenHouseEdgeBoardRequest request, CancellationToken cancellationToken)
        {
            var parameters = await _gameService.GetGameParametersById(request.GameTypeId);

            // The same vague "not found" every other game-type endpoint gives (constraints.md §6).
            if (parameters == null || !parameters.Active || parameters.ComingSoon || !parameters.IsHouseEdge)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterNotFound);

            var gameId = await _gameService.OpenSoloBoard(parameters!);
            if (gameId == null)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterHasNoActiveGames);

            var game = await LoadRound(Context, gameId!.Value, cancellationToken);
            return new ActiveGameDto(game!, null);
        }

        internal static Task<Domain.Games.Entities.Game?> LoadRound(SpaceStrokerDbContext context, Guid gameId, CancellationToken cancellationToken)
            => context.Games
                .Include(x => x.Bets).ThenInclude(b => b.Player)
                .Include(x => x.BoardChoices)
                .Include(x => x.Parameters).ThenInclude(p => p.LotteryParameters)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);
    }
}
```

`GetHouseEdgeRoundRequest.cs`: `public record GetHouseEdgeRoundRequest(Guid RoundId) : IRequest<ActiveGameDto>;` (namespace `...Handlers.Games.GetHouseEdgeRound`).

`GetHouseEdgeRoundHandler.cs`:

```csharp
    public class GetHouseEdgeRoundHandler : BaseRequestHandler<GetHouseEdgeRoundRequest, ActiveGameDto>
    {
        private readonly GeneralSettings _settings;

        public GetHouseEdgeRoundHandler(SpaceStrokerDbContext context, IMessaging messaging, GeneralSettings settings)
            : base(context, messaging)
        {
            _settings = settings;
        }

        public override async Task<ActiveGameDto> HandleRequest(GetHouseEdgeRoundRequest request, CancellationToken cancellationToken)
        {
            var round = await Context.Games
                .Include(x => x.Parameters)
                .FirstOrDefaultAsync(x => x.Id == request.RoundId, cancellationToken);

            if (round == null || round.Parameters?.IsHouseEdge != true || !Game.ActiveGamesStatus.Contains(round.Status))
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            // Reading an unbet board keeps it alive (spec T3).
            if (round!.OwnerPlayerId == null && round.Status == GameStatus.WaitingBets)
            {
                round.BoardExpiresAt = DateTime.UtcNow.AddMinutes(_settings.Games.HouseEdgeBoardTtlMinutes);
                await Context.SaveChangesAsync(cancellationToken);
            }

            Context.ChangeTracker.Clear();
            var game = await OpenHouseEdgeBoard.OpenHouseEdgeBoardHandler.LoadRound(Context, round.Id, cancellationToken);
            return new ActiveGameDto(game!, null);
        }
    }
```

`ActiveGameDto.cs`: add `public DateTime? BoardExpiresAt { get; set; }` and set it from `game.BoardExpiresAt` in the `Game` constructor next to `ServerSeedHash`. Fix the stale comment at ~line 43 ("null until the first bet") to "committed when the round (or solo board) opens".

`GamesController.cs` — add usings for the two handler namespaces and, **above** the existing `{gameTypeId:guid}` GET (so the literal `rounds` segment is unambiguous):

```csharp
        /// <summary>
        /// Open a private board for a player-vs-house type (Hyper Roll). The round belongs to nobody until its first and
        /// only bet binds it to that wallet. Rate-limited.
        /// </summary>
        [HttpPost("{gameTypeId:guid}/boards")]
        public async Task<IActionResult> OpenHouseEdgeBoard(Guid gameTypeId)
            => await ProcessRequest(new OpenHouseEdgeBoardRequest(gameTypeId));

        /// <summary>Read a house-edge round by id while it is still waiting, starting or in progress. Rate-limited.</summary>
        [HttpGet("rounds/{roundId:guid}")]
        public async Task<IActionResult> GetHouseEdgeRound(Guid roundId)
            => await ProcessRequest(new GetHouseEdgeRoundRequest(roundId));
```

MediatR discovers handlers by assembly scan (confirm with `grep -rn "RegisterServicesFromAssembly" src`); no manual registration needed.

- [ ] **Step 4: Run to verify they pass** — same filter. Expected: PASS.

- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 5: Reroll — `POST /games/rounds/{roundId}/reroll`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/RerollHouseEdgeBoard/RerollHouseEdgeBoardRequest.cs`, `RerollHouseEdgeBoardHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs`
- Create: `src/SpaceStroker.Tests/UnitTests/Handlers/RerollHouseEdgeBoardHandlerTests.cs`

**Interfaces:**
- Consumes: `IGameService.RerollSoloBoard` (Task 3), `IHouseEdgeCoverageService.AcquireLockAsync` → `IHouseEdgeCoverageLock` (`CommitAsync`, `DisposeAsync`), `OpenHouseEdgeBoardHandler.LoadRound` (Task 4).
- Produces: `record RerollHouseEdgeBoardRequest(Guid RoundId) : IRequest<ActiveGameDto>`.

- [ ] **Step 1: Write the failing tests** — mock `IHouseEdgeCoverageService` exactly as `RegisterBetHandlerTests` does (lines ~40-72: `_coverageMock.Setup(x => x.AcquireLockAsync(...)).ReturnsAsync(_coverageLockMock.Object)`):

```csharp
        [Fact]
        public async Task Reroll_TakesTheCoverageLock_RedrawsAndCommits()
        {
            var (handler, context, gameService, coverageLock, round) = await Arrange(rerollSucceeds: true);

            var dto = await handler.HandleRequest(new RerollHouseEdgeBoardRequest(round.Id), CancellationToken);

            dto.GameId.ShouldBe(round.Id);
            gameService.Verify(x => x.RerollSoloBoard(round.Id), Times.Once);
            coverageLock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            coverageLock.Verify(x => x.DisposeAsync(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task RefusedReroll_ReleasesTheLock_WithoutCommitting_AndReturnsTheVagueRefusal()
        {
            var (handler, _, _, coverageLock, round) = await Arrange(rerollSucceeds: false);

            await Should.ThrowAsync<Exception>(() => handler.HandleRequest(new RerollHouseEdgeBoardRequest(round.Id), CancellationToken));

            coverageLock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
            coverageLock.Verify(x => x.DisposeAsync(), Times.AtLeastOnce);
        }
```

`Arrange` seeds a house-edge `GameParameters` + `WaitingBets` round in the InMemory context, sets `gameService.Setup(x => x.RerollSoloBoard(round.Id)).ReturnsAsync(rerollSucceeds)`, and constructs `new RerollHouseEdgeBoardHandler(context, Messaging, gameService.Object, coverage.Object)`.

- [ ] **Step 2: Run to verify they fail** → build error.

- [ ] **Step 3: Implement**

```csharp
namespace SpaceStroker.Infrastructure.Handlers.Games.RerollHouseEdgeBoard
{
    public record RerollHouseEdgeBoardRequest(Guid RoundId) : IRequest<ActiveGameDto>;

    /// <summary>
    /// The lever reroll: redraw an unbet solo board in place (specs/hyper-roll-solo-rounds.md §6.2).
    ///
    /// ⚠️ Holds the SAME global coverage lock as a paid bet, so a reroll and a bet on one round can never interleave:
    /// the bet re-reads the board under that lock, and the reroll refuses a round that already has a bet.
    /// </summary>
    public class RerollHouseEdgeBoardHandler : BaseRequestHandler<RerollHouseEdgeBoardRequest, ActiveGameDto>
    {
        private readonly IGameService _gameService;
        private readonly IHouseEdgeCoverageService _coverage;

        public RerollHouseEdgeBoardHandler(SpaceStrokerDbContext context, IMessaging messaging, IGameService gameService,
            IHouseEdgeCoverageService coverage) : base(context, messaging)
        {
            _gameService = gameService;
            _coverage = coverage;
        }

        public override async Task<ActiveGameDto> HandleRequest(RerollHouseEdgeBoardRequest request, CancellationToken cancellationToken)
        {
            bool rerolled;
            await using (var coverageLock = await _coverage.AcquireLockAsync(cancellationToken))
            {
                rerolled = await _gameService.RerollSoloBoard(request.RoundId);
                if (rerolled)
                    await coverageLock.CommitAsync(cancellationToken);
            }

            // Vague on purpose: owned, bet, started, expired or unknown all read the same.
            if (!rerolled)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            Context.ChangeTracker.Clear();
            var game = await OpenHouseEdgeBoard.OpenHouseEdgeBoardHandler.LoadRound(Context, request.RoundId, cancellationToken);
            return new ActiveGameDto(game!, null);
        }
    }
}
```

(Split request and handler into the two files named above.) Controller:

```csharp
        /// <summary>Reroll an unbet Hyper Roll board in place: new words, new seed commitment. Rate-limited.</summary>
        [HttpPost("rounds/{roundId:guid}/reroll")]
        public async Task<IActionResult> RerollHouseEdgeBoard(Guid roundId)
            => await ProcessRequest(new RerollHouseEdgeBoardRequest(roundId));
```

- [ ] **Step 4: Run to verify they pass.** Expected: PASS.
- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 6: One bettor per round in `RegisterBetHandler`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` (`ValidateBetParameters` ~317-341, `RegisterHouseEdgeBet` ~448-524)
- Modify: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (house-edge section ~1209-1300)

**Interfaces:**
- Consumes: `Game.OwnerPlayerId`, `Game.BoardExpiresAt` (Task 2).
- Produces: behaviour only — a house-edge round accepts exactly one bet; the bet binds the owner and clears the expiry.

- [ ] **Step 1: Write the failing tests** — extend the existing `ArrangeHouseEdgeRound` helper (line ~1245) and add:

```csharp
        [Fact]
        public async Task HouseEdge_SecondBetOnARound_FromAnotherPlayer_IsRefunded()
        {
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var (game, inserted) = ArrangeHouseEdgeRound(context);
            var board = game.BoardChoices.First().Choice;
            await PlaceHouseEdgeBet(context, game, board, "wallet-a");     // accepted

            await Should.ThrowAsync<Exception>(() => PlaceHouseEdgeBet(context, game, board, "wallet-b"));

            context.GameBets.Count(x => x.GameId == game.Id).ShouldBe(1);
            AssertRefundPublished(ErrorMessages.GameNotValidForBets);
        }

        [Fact]
        public async Task HouseEdge_FirstBet_BindsTheOwner_AndClearsTheBoardExpiry()
        {
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var (game, _) = ArrangeHouseEdgeRound(context);
            game.BoardExpiresAt = DateTime.UtcNow.AddMinutes(30);
            await context.SaveChangesAsync();

            await PlaceHouseEdgeBet(context, game, game.BoardChoices.First().Choice, "owner-wallet");

            var stored = context.Games.AsNoTracking().Single(x => x.Id == game.Id);
            var player = context.Players.AsNoTracking().Single(x => x.ExternalPlayerId == "owner-wallet");
            stored.OwnerPlayerId.ShouldBe(player.Id);
            stored.BoardExpiresAt.ShouldBeNull();
        }

        [Fact]
        public async Task HouseEdge_ChoiceNoLongerOnTheBoardUnderTheLock_IsRefunded()
        {
            // Simulates a reroll that landed between validation and the lock: the lock's board read must win.
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var (game, _) = ArrangeHouseEdgeRound(context);
            var staleWord = game.BoardChoices.First().Choice;
            _coverageMock.Setup(x => x.AcquireLockAsync(It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    using var other = new SpaceStrokerDbContext(ContextOptions);
                    var row = other.GameBoardChoices.Single(x => x.GameId == game.Id && x.Choice == staleWord);
                    row.Choice = "zzzzzz";
                    other.SaveChanges();
                })
                .ReturnsAsync(_coverageLockMock.Object);

            await Should.ThrowAsync<Exception>(() => PlaceHouseEdgeBet(context, game, staleWord, "stale-wallet"));

            context.GameBets.Count(x => x.GameId == game.Id).ShouldBe(0);
        }

        [Fact]
        public async Task HouseEdge_FreeBet_AlsoTakesTheLock()
        {
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var (game, _) = ArrangeHouseEdgeRound(context, allowFreeBet: true);

            await PlaceHouseEdgeFreeBet(context, game, game.BoardChoices.First().Choice);

            _coverageMock.Verify(x => x.AcquireLockAsync(It.IsAny<CancellationToken>()), Times.Once);
            _coverageMock.Verify(x => x.EvaluateAsync(It.IsAny<HouseEdgeNewBet>(), It.IsAny<CancellationToken>()), Times.Never);
        }
```

`PlaceHouseEdgeBet` / `PlaceHouseEdgeFreeBet` / `AssertRefundPublished` wrap the handler construction and bet-body the existing house-edge tests already use (copy from `RegisterBet_HouseEdgeGame_SecondBetFromSamePlayer_ShouldRefund` at line ~1209). **Update** that existing test: its expectation is now `GameNotValidForBets` (the one-bettor rule), not `OneBetPerPresetRound`.

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~RegisterBetHandlerTests"` → the new tests fail (second bet accepted / owner null / free bet skips the lock).

- [ ] **Step 3: Implement**

In `ValidateBetParameters`, gate the per-round limits so house-edge skips them (it has its own, stricter rule under the lock):

```csharp
            if (!_isFreeBet && _bet.PlayerId != null && betGame?.Parameters?.IsHouseEdge != true)
```

Replace `RegisterHouseEdgeBet` with:

```csharp
        private async Task RegisterHouseEdgeBet(CancellationToken cancellationToken)
        {
            if (!(_settings.Games?.HouseEdgeEnabled ?? true))
                await RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance);

            try
            {
                HouseEdgeCoverageDecision? decision = null;
                try
                {
                    // Paid AND free bets take the lock now: the one-bettor claim below must serialize with every other
                    // claim, reroll and cleanup, whatever the stake (specs/hyper-roll-solo-rounds.md §6.5).
                    _coverageLock = await _houseEdgeCoverage.AcquireLockAsync(cancellationToken);

                    if (!_isFreeBet && await PaymentSignatureHasBeenUsed(_bet.Signature))
                    {
                        await ReleaseCoverageLockAsync();
                        Messaging.ReturnValidationFailureMessage(ErrorMessages.PaymentSignatureHasBeenUsed);
                    }

                    await ValidateSoloRoundUnderLock(cancellationToken);

                    // A free bet moves no money, so there is nothing for the treasury to cover.
                    if (!_isFreeBet)
                        decision = await _houseEdgeCoverage.EvaluateAsync(
                            new HouseEdgeNewBet(_bet.GameId, _bet.Choice, _bet.Amount), cancellationToken);
                }
                catch (Exception ex) when (ex is not ControlledException)
                {
                    Log.Error(ex, "HOUSE EDGE — bet {BetId} (signature {Signature}) on game {GameId} could not be evaluated; refusing and refunding.",
                        _bet.Id, _bet.Signature, _bet.GameId);
                    await RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance);
                    return;
                }

                if (decision is { IsCoverable: false })
                {
                    Log.Error("HOUSE EDGE — bet {BetId} on game {GameId} refused and refunded: treasury {Balance} cannot cover exposure {Exposure} + fee reserve {FeeReserve}.",
                        _bet.Id, _bet.GameId, decision.TreasuryBalance, decision.Exposure, decision.FeeReserve);
                    await RefundInvalidBet(ErrorMessages.HouseEdgeUnderMaintenance);
                }

                try
                {
                    await ClaimSoloRound(cancellationToken);
                    await RegisterBet(deferNotification: true);
                }
                catch (Exception ex) when (ex is not ControlledException)
                {
                    Log.Error(ex, "HOUSE EDGE — bet {BetId} (signature {Signature}) failed before its commit; refunding.", _bet.Id, _bet.Signature);
                    await RefundInvalidBet(ErrorMessages.FailedToRegisterBet);
                }

                try
                {
                    await _coverageLock!.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await HandleFailedHouseEdgeCommit(ex);
                }

                await ReleaseCoverageLockAsync();
                await _gameService.PublishBetNotification(_bet);
            }
            finally
            {
                await ReleaseCoverageLockAsync();
            }
        }

        /// <summary>
        /// Solo rounds, under the coverage lock: a round that already has ANY bet is taken, and the chosen word must
        /// be on the board as it is NOW (a reroll may have replaced it since ValidateBetParameters read it). Both refuse
        /// with the vague GameNotValidForBets; a paid bet is refunded.
        /// </summary>
        private async Task ValidateSoloRoundUnderLock(CancellationToken cancellationToken)
        {
            var taken = await _spacestrokerDbContext.GameBets.AnyAsync(x => x.GameId == _bet.GameId, cancellationToken);
            if (taken)
                await RefundInvalidBet(ErrorMessages.GameNotValidForBets);

            var onBoard = await _spacestrokerDbContext.GameBoardChoices
                .AsNoTracking()
                .AnyAsync(x => x.GameId == _bet.GameId && x.Choice == _bet.Choice, cancellationToken);
            if (!onBoard)
                await RefundInvalidBet(ErrorMessages.GameNotValidForBets);
        }

        /// <summary>
        /// Binds the round to the bettor and ends its board expiry, in the same transaction as the insert (a failed
        /// insert rolls the claim back with it). Safe as a tracked update because every claim, reroll and cleanup holds
        /// the global coverage lock. An anonymous free bet (no player) claims nothing but still occupies the round.
        /// </summary>
        private async Task ClaimSoloRound(CancellationToken cancellationToken)
        {
            var round = await _spacestrokerDbContext.Games.FirstAsync(x => x.Id == _bet.GameId, cancellationToken);
            if (round.OwnerPlayerId != null)
                await RefundInvalidBet(ErrorMessages.GameNotValidForBets);

            round.OwnerPlayerId = _bet.PlayerId;
            round.BoardExpiresAt = null;
            await _spacestrokerDbContext.SaveChangesAsync(cancellationToken);

            // Detach so InsertBet can attach its own (untracked) copy of the game without a duplicate-key conflict.
            _spacestrokerDbContext.Entry(round).State = EntityState.Detached;
        }
```

Update the XML doc above `RegisterHouseEdgeBet` to mention the one-bettor rule and that free bets take the lock.

- [ ] **Step 4: Run to verify they pass** — `dotnet test --filter "FullyQualifiedName~RegisterBetHandlerTests"`. Expected: PASS (all existing legacy tests unchanged).
- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 7: House-edge lookups — active round by wallet, availability by round

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/GetActiveGameForParameter/GetActiveGameForParameterHandler.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/CheckBetAvailability/CheckBetAvailabilityHandler.cs`, `CheckBetAvailabilityRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/IHouseEdgeCoverageService.cs`, `HouseEdgeCoverageService.cs` (remove `FindOpenRoundAsync`)
- Modify: `src/SpaceStroker.Tests/UnitTests/Handlers/CheckBetAvailabilityHandlerTests.cs`, `GetActiveGameForParameterHandlerTests.cs`, `src/SpaceStroker.Tests/UnitTests/Services/HouseEdgeCoverageServiceTests.cs`

**Interfaces:**
- Consumes: `Game.HouseEdgeUnfinishedStatus`, `Game.OwnerPlayerId`, `GameSettings.HouseEdgeBoardTtlMinutes`.
- Produces: `CheckBetAvailabilityRequestBody.GameId` (`Guid?`, `[Required]`).

- [ ] **Step 1: Write the failing tests**

`GetActiveGameForParameterHandlerTests` (add):
- `HouseEdge_WithWallet_ReturnsThatWalletsRunningRound` — seed a Player (`ExternalPlayerId = "w"`), an owned `InProgress` round for it, and an unowned `WaitingBets` round created later; request with `CallerWallet = "w"` returns the owned round.
- `HouseEdge_WithoutWallet_OrWithNoRunningRound_IsRefused` — both cases throw (`ParameterHasNoActiveGames`).
- `HouseEdge_CompletedRound_IsNotReturned` — owned `RewardForWinnersCompleted` round only → refused.
- Legacy types: the existing tests stay green unchanged.

`CheckBetAvailabilityHandlerTests` (rework): replace `_coverage.Setup(x => x.FindOpenRoundAsync(...))` with a seeded round in the InMemory context (house-edge, `WaitingBets`, unowned, no bets) and pass its id as `Body.GameId`. Replace `NoOpenRound_StillEvaluates_WithNoRound` with:

```csharp
        [Theory]
        [InlineData("missing")]
        [InlineData("owned")]
        [InlineData("bet")]
        public async Task RoundThatIsNotAnOpenUnbetBoard_IsRefused_WithoutEvaluating(string state)
        { /* arrange per state, expect Should.ThrowAsync, EvaluateAsync Times.Never */ }
```

and assert in `Covered_IsAvailable_AndPricesTheBetInTheOpenRound` that `b.GameId == <seeded round id>` and that the round's `BoardExpiresAt` was refreshed. Delete the `FindOpenRoundAsync` tests from `HouseEdgeCoverageServiceTests`.

- [ ] **Step 2: Run to verify they fail** → build errors (`GameId` missing) and assertion failures.

- [ ] **Step 3: Implement**

`GetActiveGameForParameterHandler` — after the parameters check:

```csharp
            if (gameParameters!.IsHouseEdge)
                return await HouseEdgeRoundForWallet(request, cancellationToken);
```

```csharp
        /// <summary>
        /// Solo rounds: the caller's newest round that is still running or paying — how a reload re-attaches to a paid
        /// bet. No wallet, or nothing running, is the same vague "no active game" as a legacy type with no round.
        /// </summary>
        private async Task<ActiveGameDto> HouseEdgeRoundForWallet(GetActiveGameForParameterRequest request, CancellationToken cancellationToken)
        {
            Game? result = null;
            if (!string.IsNullOrWhiteSpace(request.CallerWallet))
            {
                result = await Context.Games
                    .Include(x => x.Bets).ThenInclude(b => b.Player)
                    .Include(x => x.BoardChoices)
                    .Include(x => x.Parameters).ThenInclude(p => p.LotteryParameters)
                    .AsNoTracking()
                    .Where(x => x.ParametersId == request.ParameterId
                        && x.Owner != null && x.Owner.ExternalPlayerId == request.CallerWallet
                        && Game.HouseEdgeUnfinishedStatus.Contains(x.Status))
                    .OrderByDescending(x => x.CreatedDate)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (result is null)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.ParameterHasNoActiveGames);

            return new ActiveGameDto(result!, request.CallerWallet);
        }
```

`CheckBetAvailabilityRequest.cs` body: add

```csharp
        /// <summary>The player's own board (round id). Solo rounds: availability is always asked about ONE round.</summary>
        [Required(ErrorMessage = "Game is required.")]
        public Guid? GameId { get; set; }
```

`CheckBetAvailabilityHandler` — inject `GeneralSettings` is already there; replace the `FindOpenRoundAsync` lines with:

```csharp
            var round = await Context.Games.FirstOrDefaultAsync(x => x.Id == request.Body.GameId, cancellationToken);
            var isOpenBoard = round != null
                && round.ParametersId == parameters.Id
                && round.Status == GameStatus.WaitingBets
                && round.OwnerPlayerId == null
                && !await Context.GameBets.AnyAsync(x => x.GameId == round.Id, cancellationToken);

            // A board that is gone or taken is not a maintenance condition — the vague "not valid for bets" instead.
            if (!isOpenBoard)
                Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

            round!.BoardExpiresAt = DateTime.UtcNow.AddMinutes(_settings.Games.HouseEdgeBoardTtlMinutes);
            await Context.SaveChangesAsync(cancellationToken);

            var decision = await _coverage.EvaluateAsync(
                new HouseEdgeNewBet(round.Id, request.Body.Choice?.Trim() ?? string.Empty, amount), cancellationToken);
```

Keep the switch-off and free-bet early returns **before** this block (free bets: still check the round is an open board — move the free-bet `return BetAvailabilityDto.Open` to just before `EvaluateAsync`). Remove `FindOpenRoundAsync` from the interface and service. Update the handler's XML doc.

- [ ] **Step 4: Run to verify they pass** — `dotnet test --filter "FullyQualifiedName~GetActiveGameForParameterHandlerTests|FullyQualifiedName~CheckBetAvailabilityHandlerTests|FullyQualifiedName~HouseEdgeCoverageServiceTests"`. Expected: PASS.
- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 8: No automatic rounds for house-edge types

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs` (~50-61)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/GameParametersCreated/GameParametersCreatedConsumer.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (`ReplanOpenRounds` ~684, `OpenNewGames` ~711)
- Test: the existing consumer tests (`grep -rln "RunGameConsumer\|GameParametersCreatedConsumer" src/SpaceStroker.Tests`)

**Interfaces:** behaviour only.

- [ ] **Step 1: Write the failing tests**
- `RunGameConsumer` test: a house-edge game that ends → `IGameService.CreateGame` is **never** called; a legacy game still calls it once (existing test).
- `GameParametersCreatedConsumer` test: a new house-edge type → `CreateGame` never called.

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement**

`RunGameConsumer`:

```csharp
            // Solo rounds (specs/hyper-roll-solo-rounds.md): a house-edge player opens their own next board with the
            // lever or a reload, so no successor is created here.
            var parameters = game.Parameters?.IsHouseEdge == true
                ? null
                : await gameService.GetActiveParametersForRollover(game.ParametersId);
```

(`GetGameById` loads `game.Parameters`, see `GameService.cs:116-128`.)

`GameParametersCreatedConsumer`: extend the early-return condition with `|| parameters.IsHouseEdge` and add a comment line.

`Program.cs` `OpenNewGames`: after the `ComingSoon` skip, add

```csharp
        // Solo rounds: house-edge boards are opened by players (POST /games/{id}/boards), never at startup.
        if (parameter.Value.IsHouseEdge)
            continue;
```

`ReplanOpenRounds`: change the query to `.Where(x => Game.GameAcceptingBets.Contains(x.Status) && !x.Parameters.IsHouseEdge)`.

- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes.

---

### Task 9: `HouseEdgeBoardCleanupJob`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Jobs/HouseEdgeBoardCleanupJob.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (`ScheduleRecurringJobs`)
- Create: `src/SpaceStroker.Tests/UnitTests/Background/HouseEdgeBoardCleanupJobTests.cs`

**Interfaces:**
- Consumes: `IHouseEdgeCoverageService.AcquireLockAsync`, `Game.BoardExpiresAt`, `Game.OwnerPlayerId`.
- Produces: `Task<int> HouseEdgeBoardCleanupJob.Run()` — number of boards deleted.

- [ ] **Step 1: Write the failing tests**

```csharp
    public class HouseEdgeBoardCleanupJobTests : UnitTestsClass
    {
        [Fact]
        public async Task DeletesExpiredAndNeverExpiringUnbetBoards_ButNothingElse()
        {
            using var context = new SpaceStrokerDbContext(ContextOptions);
            var hyper = SeedType(context, houseEdge: true);
            var legacy = SeedType(context, houseEdge: false);
            var expired = SeedRound(context, hyper, expiresAt: DateTime.UtcNow.AddMinutes(-1));
            var legacyShared = SeedRound(context, hyper, expiresAt: null);                 // old shared round
            var fresh = SeedRound(context, hyper, expiresAt: DateTime.UtcNow.AddMinutes(10));
            var owned = SeedRound(context, hyper, expiresAt: null, owner: SeedPlayer(context));
            var withBet = SeedRound(context, hyper, expiresAt: DateTime.UtcNow.AddMinutes(-1), freeBet: true);
            var otherType = SeedRound(context, legacy, expiresAt: null);
            await context.SaveChangesAsync();

            var coverage = new Mock<IHouseEdgeCoverageService>();
            var coverageLock = new Mock<IHouseEdgeCoverageLock>();
            coverage.Setup(x => x.AcquireLockAsync(It.IsAny<CancellationToken>())).ReturnsAsync(coverageLock.Object);

            var deleted = await new HouseEdgeBoardCleanupJob(context, coverage.Object).Run();

            deleted.ShouldBe(2);
            var remaining = context.Games.AsNoTracking().Select(x => x.Id).ToList();
            remaining.ShouldNotContain(expired.Id);
            remaining.ShouldNotContain(legacyShared.Id);
            remaining.ShouldContain(fresh.Id);
            remaining.ShouldContain(owned.Id);
            remaining.ShouldContain(withBet.Id);
            remaining.ShouldContain(otherType.Id);
            context.GameBoardChoices.AsNoTracking().Any(x => x.GameId == expired.Id).ShouldBeFalse();
            coverageLock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
```

(`SeedRound` creates a `WaitingBets` round with 8 board rows; helpers local to the test class.)

- [ ] **Step 2: Run to verify they fail** → build error.

- [ ] **Step 3: Implement**

```csharp
namespace SpaceStroker.Infrastructure.Jobs
{
    /// <summary>
    /// Deletes abandoned Hyper Roll boards (specs/hyper-roll-solo-rounds.md §6.7): house-edge rounds that are still
    /// WaitingBets, unowned, have no bets, and whose BoardExpiresAt has passed — or is null (a shared round opened by
    /// pre-solo code). Owned rounds and any round with a bet are never touched.
    ///
    /// ⚠️ Runs under the global coverage lock, so it can never delete a board a bet is claiming at that instant.
    /// </summary>
    public class HouseEdgeBoardCleanupJob
    {
        private const int BatchSize = 500;
        private readonly SpaceStrokerDbContext _context;
        private readonly IHouseEdgeCoverageService _coverage;

        public HouseEdgeBoardCleanupJob(SpaceStrokerDbContext context, IHouseEdgeCoverageService coverage)
        {
            _context = context;
            _coverage = coverage;
        }

        public async Task<int> Run()
        {
            var now = DateTime.UtcNow;
            await using var coverageLock = await _coverage.AcquireLockAsync(CancellationToken.None);

            var expired = await _context.Games
                .Include(x => x.BoardChoices)
                .Where(x => x.Parameters.IsHouseEdge
                    && x.Status == GameStatus.WaitingBets
                    && x.OwnerPlayerId == null
                    && !x.Bets.Any()
                    && (x.BoardExpiresAt == null || x.BoardExpiresAt < now))
                .OrderBy(x => x.CreatedDate)
                .Take(BatchSize)
                .ToListAsync();

            foreach (var game in expired)
                _context.GameBoardChoices.RemoveRange(game.BoardChoices);
            _context.Games.RemoveRange(expired);

            await _context.SaveChangesAsync();
            await coverageLock.CommitAsync(CancellationToken.None);

            if (expired.Count > 0)
                Log.Information("Hyper Roll board cleanup removed {Count} abandoned board(s).", expired.Count);

            return expired.Count;
        }
    }
}
```

`Program.cs` `ScheduleRecurringJobs` — inject settings the same way other startup helpers read `GeneralSettings` (`scope.ServiceProvider.GetRequiredService<GeneralSettings>()`):

```csharp
    // Every few minutes: delete abandoned Hyper Roll boards (unbet, unowned, past their TTL).
    var cleanupMinutes = Math.Max(1, scope.ServiceProvider.GetRequiredService<GeneralSettings>().Games.HouseEdgeBoardCleanupIntervalMinutes);
    recurringJobs.AddOrUpdate<HouseEdgeBoardCleanupJob>("house-edge-board-cleanup", job => job.Run(), $"*/{cleanupMinutes} * * * *");
```

Confirm the job's DI registration follows how `PendingRefundSweepJob` is registered (`grep -rn "PendingRefundSweepJob" src/SpaceStroker.Consumers src/SpaceStroker.Infrastructure --include=*.cs`) and register `HouseEdgeBoardCleanupJob` the same way.

- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Checkpoint** — `scripts\run_tests.bat` passes; report the full list of changed/added unit tests.

---

### Task 10: Integration tests — solo rounds, and the shared-round tests rewritten

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs`
- Create: `src/SpaceStroker.Tests.Integration/HyperRollSoloRoundsTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/HyperRollFlowTests.cs`, `HyperRollMaintenanceFlowTests.cs`
- Check: `HyperRollMisconfigurationTests.cs` (uses `CreateGame`; should stay green)

**Interfaces:**
- Produces (fixture): `Task<(Guid GameId, List<BoardChoiceDto> Board, string ServerSeedHash)> OpenBoardAsync(Guid typeId)` (HTTP `POST /games/{typeId}/boards`); `Task<HttpResponseMessage> RerollBoardRawAsync(Guid gameId)`; `CheckBetAvailabilityRawAsync(Guid gameTypeId, Guid gameId, string choice, decimal amount, HttpClient? client = null)` (new `gameId` parameter); `string WinningChoiceFor(Guid gameId, string serverSeed, string betSeed, IEnumerable<GameBoardChoice> board)`.

- [ ] **Step 1: Fixture helpers**

`SeedHouseEdgeGameAsync` → call `gameService.OpenSoloBoard(parameters)` and return its id (so every existing caller gets a real solo board). Add:

```csharp
        public async Task<(Guid GameId, List<BoardChoiceDto> Board, string ServerSeedHash)> OpenBoardAsync(Guid gameTypeId)
        {
            var response = await CreatePlayerClient().PostAsync($"/games/{gameTypeId}/boards", null);
            response.EnsureSuccessStatusCode();
            var data = (await ReadEnvelopeAsync<SoloRound>(response)).Data!;
            return (data.GameId, data.BoardChoices!, data.ServerSeedHash!);
        }

        public Task<HttpResponseMessage> RerollBoardRawAsync(Guid gameId)
            => CreatePlayerClient().PostAsync($"/games/rounds/{gameId}/reroll", null);

        /// <summary>
        /// The word the weighted draw will pick for a one-bet solo round — lets a test bet a guaranteed winner after
        /// pinning the seed with SetServerSeedAsync. Same inputs the verifiers use: board by ordinal Choice, weights, and
        /// the prefix built from the round id and its single bet seed.
        /// </summary>
        public static string WinningChoiceFor(Guid gameId, string serverSeed, string betSeed, IEnumerable<GameBoardChoice> board)
        {
            var ordered = board.OrderBy(x => x.Choice, StringComparer.Ordinal).ToList();
            var index = ProvablyFair.WeightedFallbackIndex(
                serverSeed, ProvablyFair.BuildPrefix(gameId, new List<string> { betSeed }), ordered.Select(x => x.Weight).ToArray());
            return ordered[index].Choice;
        }

        private sealed record SoloRound(Guid GameId, List<BoardChoiceDto>? BoardChoices, string? ServerSeedHash);
```

(Match `BuildPrefix`'s real parameter types — the existing happy-path test calls it with `verification.GameId, verification.OrderedBetSeeds`.) Add `gameId` to `CheckBetAvailabilityRawAsync`'s body payload and update every caller.

- [ ] **Step 2: New `HyperRollSoloRoundsTests.cs`** (collection `IntegrationCollection.Name`, same ctor/`Timeout`/`PaidBet` helpers as `HyperRollFlowTests`):

```csharp
        [Fact]
        public async Task HappyPath_OpenRerollBet_RunsAloneImmediately_AndPaysStakeTimesMultiplier()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);

            var (gameId, board, hash) = await _fixture.OpenBoardAsync(parameters.Id);
            board.Count.ShouldBe(8);

            var reroll = await _fixture.RerollBoardRawAsync(gameId);
            reroll.EnsureSuccessStatusCode();
            var rerolled = await _fixture.GetBoardAsync(gameId);
            rerolled.Count.ShouldBe(8);
            (await _fixture.GetGameAsync(gameId))!.ServerSeedHash.ShouldNotBe(hash);

            // Pin the seed so the bet is a guaranteed winner.
            var serverSeed = new string('a', 64);
            await _fixture.SetServerSeedAsync(gameId, serverSeed, ProvablyFair.HashServerSeed(serverSeed));
            var body = PaidBet("placeholder", "solo-happy-wallet", 1m);
            body.Choice = IntegrationTestFixture.WinningChoiceFor(gameId, serverSeed, body.Seed!, rerolled);

            await _fixture.PlaceBetAsync(gameId, body);

            var owner = await _fixture.GetPlayerAsync("solo-happy-wallet");
            (await _fixture.GetGameAsync(gameId))!.OwnerPlayerId.ShouldBe(owner!.Id);

            var game = await _fixture.WaitForStatusAsync(gameId, GameStatus.RewardForWinnersCompleted, Timeout);
            game.WinningChoice.ShouldBe(body.Choice);

            var bet = (await _fixture.GetBetsAsync(gameId)).Single();
            // Started immediately: no 5 s countdown between the bet landing and the round's start.
            game.StartDate!.Value.ShouldBeLessThanOrEqualTo(bet.CreatedDate.AddSeconds(2));
            bet.Status.ShouldBe(BetStatus.Rewarded);
            var multiplier = rerolled.Single(x => x.Choice == body.Choice).Multiplier;
            var credit = (await _fixture.GetTransactionLogsAsync(new[] { bet.Id })).Single(l => l.Type == TransactionLogType.Credit);
            credit.Amount.ShouldBe(Math.Round(1m * multiplier, _fixture.TokenDecimals, MidpointRounding.ToZero));

            var verification = await _fixture.GetGameVerificationAsync(gameId);
            verification.WinningChoice.ShouldBe(body.Choice);

            // No successor round was opened for the type by the consumer.
            (await _fixture.GetSuccessorGameAsync(parameters.Id, gameId)).ShouldBeNull();
            _fixture.LogSink.GetErrorMessages().ShouldBeEmpty();
        }

        [Fact]
        public async Task TwoPlayersRaceOneRound_ExactlyOneIsAccepted_TheOtherIsRefunded()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);
            var (gameId, board, _) = await _fixture.OpenBoardAsync(parameters.Id);
            var first = PaidBet(board[0].Choice, "race-wallet-a", 1m);
            var second = PaidBet(board[1].Choice, "race-wallet-b", 1m);

            var responses = await Task.WhenAll(_fixture.PlaceBetRawAsync(gameId, first), _fixture.PlaceBetRawAsync(gameId, second));

            responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
            var bets = await _fixture.GetBetsAsync(gameId);
            bets.Count.ShouldBe(1);
            var loser = bets[0].Signature == first.PaymentSignature ? second : first;
            (await WaitForRefundAsync(loser.PaymentSignature!)).Amount.ShouldBe(1m);
        }

        [Fact]
        public async Task Cleanup_RemovesAnExpiredBoard_KeepsAFreshBoardAndAnOwnedRound()
        {
            var parameters = await _fixture.GetParameterAsync(IntegrationGameParameters.HyperRoll);
            var (expired, _, _) = await _fixture.OpenBoardAsync(parameters.Id);
            var (fresh, _, _) = await _fixture.OpenBoardAsync(parameters.Id);
            var (owned, ownedBoard, _) = await _fixture.OpenBoardAsync(parameters.Id);
            await _fixture.PlaceBetAsync(owned, PaidBet(ownedBoard[0].Choice, "cleanup-owner", 1m));
            await _fixture.SetBoardExpiresAtAsync(expired, DateTime.UtcNow.AddMinutes(-1));

            await _fixture.RunHouseEdgeBoardCleanupAsync();

            (await _fixture.GetGameAsync(expired)).ShouldBeNull();
            (await _fixture.GetGameAsync(fresh)).ShouldNotBeNull();
            (await _fixture.GetGameAsync(owned)).ShouldNotBeNull();
        }
```

Add fixture helpers `SetBoardExpiresAtAsync(Guid gameId, DateTime value)` (tracked update in a consumers scope, like `SetCarriedExtraAsync`) and `RunHouseEdgeBoardCleanupAsync()` (resolve `HouseEdgeBoardCleanupJob` in a consumers scope and `await Run()`, like `RunPendingRefundSweepAsync`). Copy `WaitForRefundAsync` from `HyperRollMaintenanceFlowTests` (or move it to the fixture and use it from both).

- [ ] **Step 3: Rewrite the shared-round tests** (each currently places many bets on ONE round, which solo rounds refuse):
- `HyperRollFlowTests.HappyPath_EightPlayersCoverTheBoard_...` → **delete**; move its published-weights assertions (board weights = ladder, sum 1,000,000, `GET` payload weights) into the new happy path, reading the board via `GET /games/rounds/{gameId}`.
- `HappyPath_NineAndTenLetterWords_...` → one solo round: pin the seed, bet the computed winning word, assert it is 9-10 letters, stored whole, paid and verified.
- `InsufficientTreasury_...` → one solo round, guaranteed-win bet of 1 SOL, `ConsumersSolanaService.NextSolBalance = 1m` (below any multiplier ≥ 2): expect `ErrorWhenProcessingRewards`, the bet `NeedsManualAnalysis`, no credit, and an error log naming the bet id.
- `SinglePlayer_RoundStartsAlone` → keep, now via `OpenBoardAsync`.
- `HyperRollMaintenanceFlowTests.OpenHyperRollAsync` → open with `OpenBoardAsync`; drop `minimumPlayers`.
- `HappyPath_CoveredBets_...` → one bet (guaranteed winner), round pays.
- `TwoConcurrentBetsOnTheSameWord_WhereOnlyOneFits_...` → rename `TwoConcurrentBetsOnTwoRounds_WhereOnlyOneFits_ExactlyOneIsAccepted`: open TWO boards, size the balance for one bet exactly as today, POST one bet to each round concurrently → one accepted, the other refunded with the maintenance message (this is the global-lock guarantee).
- Every `CheckBetAvailabilityRawAsync` call passes the round id.

- [ ] **Step 4: Run** — Docker running, then `scripts\run_integration_tests.bat --filter "FullyQualifiedName~HyperRoll"`. Expected: PASS. Then the full `scripts\run_integration_tests.bat`. Expected: PASS.

- [ ] **Step 5: Checkpoint.**

---

### Task 11: Front — types, services and a per-type chat channel

**Files:**
- Modify: `src/types/index.ts`, `src/services/mainService.ts`, `src/hooks/useGameHub.ts`

**Interfaces:**
- Produces:
  - `GameInstance.boardExpiresAt?: string | null`; `BodyGameAvailability.gameId: string`.
  - `mainService.openHouseEdgeBoard(gameTypeId: string): Promise<DefaultResponse<GameInstance>>`
  - `mainService.rerollHouseEdgeBoard(gameId: string): Promise<DefaultResponse<GameInstance>>`
  - `mainService.getHouseEdgeRound(gameId: string): Promise<DefaultResponse<GameInstance>>`
  - `useGameHub().attachToChat(channelId: string, onMessage: (payload: string) => void): Promise<void>`, `detachFromChat(): Promise<void>`

- [ ] **Step 1: Types** — add to `GameInstance`: `// Solo rounds: when an unbet Hyper Roll board expires (null once it has a bet).` `boardExpiresAt?: string | null;` and to `BodyGameAvailability`: `// Solo rounds: the player's own board (round id).` `gameId: string;`.

- [ ] **Step 2: Services** — next to `getGameInstance`:

```ts
// Solo rounds (specs/hyper-roll-solo-rounds.md): a Hyper Roll player's own board. Open → reroll while unbet →
// bet. The round id is kept by the browser; the first bet binds it to the wallet. All three are rate-limited.
const openHouseEdgeBoard = async (gameTypeId: string) => {
  const { data } = await mainAPI.post<DefaultResponse<GameInstance>>(`/games/${gameTypeId}/boards`);
  return data;
};

const rerollHouseEdgeBoard = async (gameId: string) => {
  const { data } = await mainAPI.post<DefaultResponse<GameInstance>>(`/games/rounds/${gameId}/reroll`);
  return data;
};

const getHouseEdgeRound = async (gameId: string) => {
  const { data } = await mainAPI.get<DefaultResponse<GameInstance>>(`/games/rounds/${gameId}`);
  return data;
};
```

and export them in the default object.

- [ ] **Step 3: Hub chat channel** — in `useGameHub.ts`, next to `currentGameId`/`boundGameEvents`:

```ts
// Hyper Roll chat is per GAME TYPE, not per round (rounds are private): a second group the tab joins alongside its
// round. Tracked separately so a round switch never drops the chat, and re-joined on reconnect like the round.
const currentChatChannelId = ref<string | null>(null);
let boundChatEvent: string | null = null;

const attachToChat = async (channelId: string, onMessage: (payload: string) => void): Promise<void> => {
  if (currentChatChannelId.value === channelId) return;
  if (currentChatChannelId.value) await detachFromChat();

  const live = await ensureConnected();
  if (!live) return;

  await live.invoke('JoinGame', channelId);
  boundChatEvent = `${channelId}/ChatMessage`;
  live.on(boundChatEvent, onMessage);
  currentChatChannelId.value = channelId;
};

const detachFromChat = async (): Promise<void> => {
  const live = connection.value;
  const channelId = currentChatChannelId.value;
  currentChatChannelId.value = null;
  if (!live || !channelId) return;
  if (boundChatEvent) live.off(boundChatEvent);
  boundChatEvent = null;
  try {
    await live.invoke('LeaveGame', channelId);
  } catch {
    // ignore — leaving a group on a dying socket is best-effort
  }
};
```

In the `onreconnected` handler, before the `if (!currentGameId.value) return;` line, add:

```ts
    if (currentChatChannelId.value) {
      try {
        await built.invoke('JoinGame', currentChatChannelId.value);
      } catch {
        // ignore — a further drop retries through this same handler
      }
    }
```

Export `attachToChat`/`detachFromChat` from the composable's return object.

- [ ] **Step 4: Verify** — `yarn -s lint` passes (run `npx eslint --fix src/types/index.ts src/services/mainService.ts src/hooks/useGameHub.ts` first).
- [ ] **Step 5: Checkpoint.**

---

### Task 12: Front — load, reroll and re-open boards in `useGame`

**Files:**
- Modify: `src/hooks/useGame.ts` (`getBets` ~562, `initalizeWebsocketConnections` ~597, `sendChatMessage` ~1326, `updateNewGame` ~1334, `bet()` availability ~920, rollover watch ~1456, return object)
- Modify: `src/hooks/useGameChat.ts` (`isReadOnly`)

**Interfaces:**
- Consumes: Task 11 services and hub methods.
- Produces (from `useGame()`): `rerollBoard(): Promise<boolean>`, `openFreshBoard(): Promise<void>`, `isRerolling: Ref<boolean>`.

- [ ] **Step 1: Board storage + loading** (module scope, near `stakeAmount`):

```ts
// Solo rounds: the browser keeps its own Hyper Roll board id, so a reload restores the same board instead of rerolling.
const isRerolling = ref(false);
const boardStorageKey = (gameTypeId: string) => `hyperRoll:board:${gameTypeId}`;
const readStoredBoard = (gameTypeId: string): string | null => {
  try {
    return localStorage.getItem(boardStorageKey(gameTypeId));
  } catch {
    return null;
  }
};
const storeBoard = (gameTypeId: string, gameId: string | null) => {
  try {
    if (gameId) localStorage.setItem(boardStorageKey(gameTypeId), gameId);
    else localStorage.removeItem(boardStorageKey(gameTypeId));
  } catch {
    // storage blocked — the next load simply opens a fresh board
  }
};
```

Inside `useGame()` before `getBets`:

```ts
  // Solo rounds, in order: my running round (reload mid-round, by wallet) → my stored unbet board → a fresh board.
  // A lookup that finds nothing is expected, so its error is marked handled (no interceptor toast).
  const loadHouseEdgeRound = async (gameTypeId: string): Promise<GameInstance> => {
    const wallet = currentPlayerId.value;
    if (wallet) {
      try {
        const { data } = await mainService.getGameInstance(gameTypeId, wallet);
        if (data) return data;
      } catch (err) {
        (err as { handled?: boolean }).handled = true;
      }
    }

    const stored = readStoredBoard(gameTypeId);
    if (stored) {
      try {
        const { data } = await mainService.getHouseEdgeRound(stored);
        if (data && data.status < GameStatus.Ended) return data;
      } catch (err) {
        (err as { handled?: boolean }).handled = true;
      }
    }

    const { data } = await mainService.openHouseEdgeBoard(gameTypeId);
    storeBoard(gameTypeId, data.gameId);
    return data;
  };
```

In `getBets`, replace the `getGameInstance` call with:

```ts
      const typeId = selectedGame.value.gameTypeId;
      const gameData = selectedGame.value.isHouseEdge
        ? await loadHouseEdgeRound(typeId)
        : (await mainService.getGameInstance(typeId, currentPlayerId.value)).data;
```

- [ ] **Step 2: Chat channel** — in `initalizeWebsocketConnections`, for house-edge do NOT pass `ChatMessage` in the round handlers; instead call `await hub.attachToChat(selectedGame.value.gameTypeId, onChatMessage)` (use the existing chat handler function); for every other type call `await hub.detachFromChat()`. In `sendChatMessage`:

```ts
    const channelId = selectedGame.value?.isHouseEdge ? selectedGame.value.gameTypeId : gameInstance.value?.gameId;
    if (!channelId) return;
    await hub.invoke('SendChatMessage', channelId, displayName, text);
```

In `updateNewGame`, only clear chat for legacy types: `if (!selectedGame.value?.isHouseEdge) chatMessages.value = [];` (keep the comment, add "Hyper Roll chat is per type and survives a new board"). In `useGameChat.ts`: `const isReadOnly = computed(() => !selectedGame.value?.isHouseEdge && (gameInstance.value?.status ?? GameStatus.WaitingBets) >= GameStatus.Ended);` (destructure `selectedGame` from `useGame()`).

- [ ] **Step 3: Reroll and fresh board**

```ts
  /** The lever with no word picked: redraw my unbet board in place. A board that is gone is replaced by a fresh one. */
  const rerollBoard = async (): Promise<boolean> => {
    const round = gameInstance.value;
    if (!round || !selectedGame.value?.isHouseEdge || isRerolling.value) return false;
    if (round.status !== GameStatus.WaitingBets || round.bets.length > 0) return false;

    isRerolling.value = true;
    try {
      const { data } = await mainService.rerollHouseEdgeBoard(round.gameId);
      gameInstance.value = {
        ...round,
        boardChoices: data.boardChoices,
        serverSeedHash: data.serverSeedHash,
        boardExpiresAt: data.boardExpiresAt,
      };
      userWord.value = '';
      return true;
    } catch (err) {
      (err as { handled?: boolean }).handled = true;
      await openFreshBoard();
      return true;
    } finally {
      isRerolling.value = false;
    }
  };

  /** A result is on screen (or my board expired): forget the stored board and load a new one. */
  const openFreshBoard = async () => {
    const typeId = selectedGame.value?.gameTypeId;
    if (typeId) storeBoard(typeId, null);
    await updateNewGame();
  };
```

- [ ] **Step 4: Expired-board guard before signing** — in `bet()`, at the start of the `if (isHouseEdge) {` block (before the maintenance check):

```ts
      // Solo rounds: re-read my board first (also keeps it alive). Gone or taken → a fresh board, no transfer.
      const roundId = gameInstance.value?.gameId ?? '';
      try {
        const { data: current } = await mainService.getHouseEdgeRound(roundId);
        if (current.status !== GameStatus.WaitingBets || current.bets.length > 0) throw new Error('board taken');
      } catch (err) {
        (err as { handled?: boolean }).handled = true;
        await openFreshBoard();
        betOutput.value = "YOUR BOARD EXPIRED. HERE'S A FRESH ONE.";
        if (onBetFail) onBetFail();
        return;
      }
```

and add `gameId: roundId,` to the `checkGameAvailability` body. Update the comment above that call (the availability route still takes the game TYPE id in the path; the body now names the round).

- [ ] **Step 5: No automatic rollover** — in the rollover status watcher: `if (status === GameStatus.Ended && !selectedGame.value?.isHouseEdge) armRollover();`. The manual lever (`triggerRollover`) keeps working unchanged. Return `rerollBoard`, `openFreshBoard`, `isRerolling` from `useGame()`.

- [ ] **Step 6: Verify** — `npx eslint --fix src/hooks/useGame.ts src/hooks/useGameChat.ts && yarn -s lint` passes; the dev server (`yarn serve --port <free port>`) compiles with no errors.

- [ ] **Step 7: Checkpoint.**

---

### Task 13: Front — lever routing, lever tag, terminal copy, lobby, stats

**Files:**
- Modify: `src/components/game/GameConsole.vue`, `src/components/game/GameTerminal.vue`, `src/hooks/useLiveGames.ts`, `src/components/game/GameStatsPanel.vue`

**Interfaces:**
- Consumes: `rerollBoard`, `openFreshBoard`, `isRerolling` (Task 12).

- [ ] **Step 1: Lever routing** in `GameConsole.vue` — destructure `rerollBoard`, `openFreshBoard`, `isRerolling` from `useGame()`, and add:

```ts
  // Solo rounds — the lever does exactly what its tag says: REROLL (no word), PLAY (word + stake), NEW BOARD (result).
  const pullLever = async () => {
    if (isHouseEdge.value) {
      const status = gameInstance.value?.status;
      if (status === GameStatus.RewardForWinnersCompleted || status === GameStatus.ErrorWhenProcessingRewards) {
        leverDownAnimation();
        await openFreshBoard();
        leverUpAnimation();
        return;
      }
      if (!userWord.value && status === GameStatus.WaitingBets && !iHaveBetThisRound.value) {
        if (processing.value || isRerolling.value) return;
        playAudio(buttonPress);
        leverDownAnimation();
        await rerollBoard();
        leverUpAnimation();
        return;
      }
      return placeBet();
    }
    return canRollOver.value ? fetchNewGame() : placeBet();
  };

  const leverTag = computed((): string | null => {
    if (!isHouseEdge.value) return null;
    const status = gameInstance.value?.status;
    if (status === GameStatus.RewardForWinnersCompleted || status === GameStatus.ErrorWhenProcessingRewards)
      return 'NEW BOARD';
    if (isRerolling.value || processing.value || status !== GameStatus.WaitingBets || iHaveBetThisRound.value)
      return '· · ·';
    if (userWord.value) return `PLAY ${displayStake(stakeAmount.value, selectedGame.value)}`;
    return 'REROLL';
  });
```

(import `displayStake` from `@/utils/houseEdge`). Replace both `@click="canRollOver ? fetchNewGame() : placeBet()"` with `@click="pullLever()"`.

- [ ] **Step 2: Lever tag markup** — desktop: wrap the lever `<img>` in `<div class="relative flex h-full flex-col items-center">` … and add under the image `<span v-if="leverTag" class="lever-tag">{{ leverTag }}</span>`. Mobile: inside the lever `<button>`, after the `<img>`, add `<span v-if="leverTag" class="lever-tag lever-tag--mobile">{{ leverTag }}</span>`. Styles (scoped):

```css
  /* Solo rounds: what the lever will do, as a small LCD tag on the lever plate. */
  .lever-tag {
    position: absolute;
    bottom: 4%;
    left: 50%;
    transform: translateX(-50%);
    padding: 2px 6px 1px;
    background: #0d1014;
    border: 2px solid #232843;
    color: #4ade80;
    font-family: 'Silkscreen', 'VT323', monospace;
    font-size: 10px;
    letter-spacing: 0.06em;
    line-height: 1.2;
    white-space: nowrap;
    pointer-events: none;
    text-shadow: 0 0 6px rgba(74, 222, 128, 0.5);
  }

  .lever-tag--mobile {
    bottom: 6px;
    font-size: 9px;
  }
```

Verify visually at 1440×900, 820×1180 and 390×844 that the tag sits on the plate and never covers the handle; adjust `bottom` only.

- [ ] **Step 3: Terminal copy** in `GameTerminal.vue` `houseEdgeLines`: nothing picked → `['> PICK A KEY [1-8] TO LOCK A WORD.', '> OR PULL THE LEVER TO REROLL THE BOARD.']`; own bet placed → `['> YOUR BET IS IN. EDSON IS WARMING UP...']`; remove the `THIS ROUND STARTS IN` countdown line; when `isRerolling` → `['> REROLLING THE BOARD...']` (checked first). Reel trigger: change the reel watcher source from the game id to the board's words:

```ts
  watch(
    () =>
      isHouseEdge.value
        ? (gameInstance.value?.boardChoices ?? []).map((entry) => entry.choice).join('|')
        : undefined,
```

keeping the existing body (it already guards empty boards).

- [ ] **Step 4: Lobby and stats** — `useLiveGames.ts`: `const playable = types.filter((game) => !game.comingSoon && !game.isHouseEdge);` with a comment ("solo rounds: a house-edge type has no shared round to snapshot — the card renders its idle variant"). `GameStatsPanel.vue`: for `selectedGame.isHouseEdge`, label the pot row `BIGGEST WIN` and bind `biggestPayoutText` instead of `potText` (find the row's label in the template and switch both by `isHouseEdge`).

- [ ] **Step 5: Verify** — `npx eslint --fix` on the four files, `yarn -s lint` passes.
- [ ] **Step 6: Checkpoint.**

---

### Task 14: End-to-end verification on the local stack + docs

**Files:**
- Modify: `monkify-meta/specs/hyper-roll-solo-rounds.md` (changelog row: implemented), `monkify-meta/specs/hyper-roll.md` (§7.2/§7.4/§9.1 notes pointing at the solo spec where they describe the shared lifecycle)

- [ ] **Step 1: Rebuild the backend containers from the worktree** (keeps the DB and its port remap): from `.worktrees/hyper-roll/monkify-backend`: `docker compose --project-name monkify-backend up -d --build --no-deps api consumers`. Confirm `GET http://localhost:6001/health` → `"Api running!"` and the Consumers log shows the `HyperRollSoloRounds` migration applied.
- [ ] **Step 2: Front** — `yarn serve --port <verified-free port>` in the front worktree; confirm the served bundle contains `rerollHouseEdgeBoard`.
- [ ] **Step 3: Browser checks (chrome-devtools)**, each at 1440×900 and 390×844:
  1. Open Hyper Roll with no wallet: a board renders with no prompt; reload → the same 8 words.
  2. Lever with no word: tag reads REROLL; the words reel to a new board; the fairness hash in the FAIR panel changes.
  3. FREE BET MODE on, pick a key: tag reads PLAY 0.01; pull → round runs with no countdown line; the terminal never shows another player's bet.
  4. After the result: the board stays; tag reads NEW BOARD; pull → fresh board with the reel.
  5. Two browser contexts (`isolatedContext`) on Hyper Roll: different boards; a chat message sent in one appears in the other.
  6. Lobby card for Hyper Roll shows no FILLING/LIVE pill; 0 console errors throughout.
- [ ] **Step 4: Docs** — add the changelog row `| <date> | Implemented on feature/hyper-roll (backend + front). |` to the solo spec; in `hyper-roll.md`, add a one-line pointer "*(Solo rounds: see decision 35 / hyper-roll-solo-rounds.md.)*" at the start of §9.1 and in §7.3's round-loading bullet.
- [ ] **Step 5: Final report** — list every changed/added unit and integration test (AGENTS rule 3), the lint/test commands run with their results, and ask the owner whether to commit and push both worktrees.
