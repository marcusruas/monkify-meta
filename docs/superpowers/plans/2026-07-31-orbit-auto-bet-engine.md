# Orbit Auto-Bet Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the house place one real, provably-fair bet per round out of each enrolled player's custodial Orbit balance, credit winnings back into that balance, and never let a bot delay a paying human.

**Architecture:** A two-stage MassTransit fan-out. `GameService.CreateGame` starts publishing the already-existing `GameCreatedEvent` on the bus; an `OrbitPlacementPlannerConsumer` decides once whether the round is viable, arms a start gate on the `Game` row, and emits one `PlaceOrbitBetEvent` per eligible player; each `PlaceOrbitBetConsumer` claims its slot (a unique `BetDebit` ledger row doubling as the per-(player, round) lock), debits, signs an SPL transfer from the Orbit account, and replays the bet through the ordinary `RegisterBetRequest` MediatR path. Winnings and refunds route back to the Orbit account instead of the player's wallet.

**Tech Stack:** .NET 8, EF Core (Npgsql), MassTransit (Postgres SQL transport), Hangfire, MediatR, Solnet, xUnit + Shouldly + Moq, Testcontainers; Vue 3 + TypeScript (Vue CLI).

## Global Constraints

- **Base branch is `origin/release/orbit`, NOT `main`.** Work in `.worktrees/orbit-auto-bet-engine/<repo>` on `feature/orbit-auto-bet-engine`. Hubs stay on `main`. Never `git checkout -b` in a hub.
- **Authoritative design:** `monkify-meta/specs/orbit-auto-bet-engine.md`. Where this plan and the spec disagree, the spec wins — stop and reconcile.
- **EF mapping is inline** in `SpaceStrokerDbContext.OnModelCreating`. No `IEntityTypeConfiguration`.
- **Migrations** live in `SpaceStroker.Infrastructure/Migrations/` and are applied only by `SpaceStroker.Consumers`. **Never pass `--no-build`** to `dotnet ef migrations add` — it reads stale assemblies and emits an empty `Up`/`Down` with no error. Read the generated file before committing.
- **The DbContext is `NoTracking` by default** and `SaveChangesAsync` calls `ChangeTracker.Clear()`. Every mutation is `ExecuteUpdateAsync`, raw parameterized SQL, or an explicit `AsTracking()` re-query. A load-mutate-`SaveChanges` silently persists nothing.
- **`ExecuteUpdateAsync` does not run on the EF in-memory provider.** Any ledger write is provable only in `SpaceStroker.Tests.Integration`.
- **Unit tests run via `scripts/run_tests.bat`** — never a bare `dotnet test` (it drags in the slow `ProductionGames` Monte-Carlo suite). Integration tests via `scripts/run_integration_tests.bat` (Docker required).
- **Money movement lives in `SpaceStroker.Consumers`.** The API host stays stateless and never signs.
- **No new secret, no new endpoint, no new rate-limit rule.** `RateLimitSettingsTests`' exact-set assertion must stay untouched.
- **Orbit must be invisible to other players.** No public DTO field, no badge, no "waiting for auto-bets" state.
- **Frontend:** Prettier violations are ESLint **errors** (`printWidth: 100`, `singleQuote`, `endOfLine: lf`). Verify with `yarn lint` + `yarn build`; there is no test runner.

---

### Task 1: Data model — columns, EF mapping, migration

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/Entities/Bet.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs`
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitTransaction.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs`
- Create: `src/SpaceStroker.Infrastructure/Migrations/*_AddOrbitAutoBetEngine.cs` (generated)

**Interfaces:**
- Produces: `Bet.FundedByOrbit` (`bool`), `Game.ExpectedOrbitBets` (`int?`), `Game.OrbitGateExpiresAt` (`DateTime?`), `OrbitTransaction.GameId` (`Guid?`). Every later task consumes these.

- [ ] **Step 1: Add the three entity property blocks**

Copy the properties **with their doc comments verbatim** from spec §4.1 — the comments carry the reasoning (why the claim column exists, why the gate needs its own expiry) and are the only place it is recorded in code.

- [ ] **Step 2: Add the EF mapping**

In `SpaceStrokerDbContext.OnModelCreating`, inside the **existing** `Bet` block:

```csharp
builder.Property(x => x.FundedByOrbit).IsRequired().HasDefaultValue(false);
```

inside the **existing** `Game` block:

```csharp
builder.Property(x => x.ExpectedOrbitBets).IsRequired(false);
builder.Property(x => x.OrbitGateExpiresAt).IsRequired(false);
```

and inside the **existing** `OrbitTransaction` block, **replacing** the current non-unique pool index:

```csharp
builder.Property(x => x.GameId).IsRequired(false);

// Extends the pool index with GameId and makes it UNIQUE. It still serves the per-player pool
// lookups (leading columns), and additionally enforces one BetDebit per (player, round). Rows that
// are not BetDebit leave GameId NULL, and Postgres treats NULLs as distinct.
builder.HasIndex(x => new { x.OrbitAccount, x.MintAddress, x.PlayerId, x.GameId }).IsUnique();

builder.HasIndex(x => x.BetId);
```

⚠️ Delete the old `builder.HasIndex(x => new { x.OrbitAccount, x.MintAddress, x.PlayerId });` line — leaving both creates a redundant index.

- [ ] **Step 3: Generate the migration**

```bash
cd src/SpaceStroker.Infrastructure
dotnet ef migrations add AddOrbitAutoBetEngine --startup-project ../SpaceStroker.Consumers
```

- [ ] **Step 4: Read the generated migration**

Open `Migrations/*_AddOrbitAutoBetEngine.cs`. Expected `Up()`: 4 `AddColumn`, 1 `DropIndex` on `IX_OrbitTransactions_OrbitAccount_MintAddress_PlayerId`, 2 `CreateIndex` (the unique 4-column one and `IX_OrbitTransactions_BetId`). Expected `Down()`: the inverse.

⚠️ **If `Up()` is empty, the build was stale** — rebuild and regenerate; do not hand-edit. If the index change came out as anything other than drop-then-create, fix the model and regenerate.

- [ ] **Step 5: Verify it compiles and the existing suite is still green**

Run: `scripts/run_tests.bat`
Expected: PASS, same count as before this task (no behaviour changed yet).

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Domain src/SpaceStroker.Infrastructure
git commit -m "Add the Orbit auto-bet columns, mapping and migration."
```

---

### Task 2: Config — `OrbitBetGateTimeoutSeconds`

**Files:**
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json`
- Test: `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`

**Interfaces:**
- Produces: `OrbitSettings.OrbitBetGateTimeoutSeconds` (`int`, default 45). Consumed by Tasks 6 and 8.

- [ ] **Step 1: Write the failing test**

Append to `OrbitSettingsBindingTests.cs`:

```csharp
[Fact]
public void CommittedConfiguration_BindsTheOrbitBetGateTimeout()
{
    var settings = BindCommittedSettings();   // the helper the existing cases already use
    settings.Operator.Token.Orbit.OrbitBetGateTimeoutSeconds.ShouldBe(45);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — `OrbitBetGateTimeoutSeconds` does not exist.

- [ ] **Step 3: Add the property**

In `OrbitSettings.cs`, with the doc comment from spec §5:

```csharp
public int OrbitBetGateTimeoutSeconds { get; set; } = 45;
```

⚠️ A scalar with an initializer is safe. **Do not add an array here** without reading the `DepositOptions` comment — `ConfigurationBinder` appends to a pre-initialized array.

- [ ] **Step 4: Add the config entry**

In `spacestrokersettings.json`, inside `GeneralSettings.Operator.Token.Orbit`:

```json
"OrbitBetGateTimeoutSeconds": 45
```

- [ ] **Step 5: Run the test**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs src/SpaceStroker.Common/spacestrokersettings.json src/SpaceStroker.Tests
git commit -m "Add the Orbit bet-gate timeout setting."
```

---

### Task 3: `OrbitChoiceGenerator` — pure, provably-fair derivation

**Files:**
- Create: `src/SpaceStroker.Domain/Orbit/Services/OrbitChoiceGenerator.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitChoiceGeneratorTests.cs`

**Interfaces:**
- Consumes: `ProvablyFair.Keystream`, `ProvablyFair.NextIndex` (existing, `src/SpaceStroker.Domain/Games/Services/ProvablyFair.cs`).
- Produces:
  - `static string DeriveSeed(string serverSeed, Guid playerId)` → 64-char lowercase hex.
  - `static string DeriveChoice(string serverSeed, Guid playerId, int attempt, GameParameters parameters)`.

This task is pure functions with no I/O — the highest-value TDD target in the feature and the only part a player can independently verify.

- [ ] **Step 1: Write the failing tests**

```csharp
public class OrbitChoiceGeneratorTests
{
    private const string Seed = "a3f1c0de5b8e47129d6a0f4b7c2e91d8a3f1c0de5b8e47129d6a0f4b7c2e91d8";
    private static readonly Guid Player = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void DeriveSeed_IsDeterministicAndSixtyFourLowercaseHexChars()
    {
        var first = OrbitChoiceGenerator.DeriveSeed(Seed, Player);
        var second = OrbitChoiceGenerator.DeriveSeed(Seed, Player);

        first.ShouldBe(second);
        first.Length.ShouldBe(64);
        first.ShouldBe(first.ToLowerInvariant());
        first.ShouldAllBe(c => Uri.IsHexDigit(c));
    }

    [Fact]
    public void DeriveSeed_DiffersPerPlayer()
    {
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        OrbitChoiceGenerator.DeriveSeed(Seed, Player)
            .ShouldNotBe(OrbitChoiceGenerator.DeriveSeed(Seed, other));
    }

    [Fact]
    public void DeriveChoice_IsDeterministicForTheSameAttempt()
    {
        var parameters = FreeTextParameters(length: 6, acceptDuplicates: true);

        OrbitChoiceGenerator.DeriveChoice(Seed, Player, 0, parameters)
            .ShouldBe(OrbitChoiceGenerator.DeriveChoice(Seed, Player, 0, parameters));
    }

    [Fact]
    public void DeriveChoice_ChangesWithTheAttemptNumber()
    {
        var parameters = FreeTextParameters(length: 6, acceptDuplicates: true);

        OrbitChoiceGenerator.DeriveChoice(Seed, Player, 0, parameters)
            .ShouldNotBe(OrbitChoiceGenerator.DeriveChoice(Seed, Player, 1, parameters));
    }

    [Fact]
    public void DeriveChoice_HonoursLengthAndCharset()
    {
        var parameters = FreeTextParameters(length: 6, acceptDuplicates: true);
        var allowed = parameters.AllowedCharacters.StringValueOf();

        for (var attempt = 0; attempt < 25; attempt++)
        {
            var choice = OrbitChoiceGenerator.DeriveChoice(Seed, Player, attempt, parameters);
            choice.Length.ShouldBe(6);
            choice.ShouldAllBe(c => allowed.Contains(c));
        }
    }

    [Fact]
    public void DeriveChoice_NeverRepeatsACharacterWhenDuplicatesAreRejected()
    {
        var parameters = FreeTextParameters(length: 6, acceptDuplicates: false);

        for (var attempt = 0; attempt < 25; attempt++)
        {
            var choice = OrbitChoiceGenerator.DeriveChoice(Seed, Player, attempt, parameters);
            choice.Distinct().Count().ShouldBe(choice.Length);
        }
    }

    [Fact]
    public void DeriveChoice_AlwaysReturnsOneOfThePresetChoices()
    {
        var parameters = PresetParameters("alpha", "bravo", "charlie", "delta");

        for (var attempt = 0; attempt < 25; attempt++)
        {
            var choice = OrbitChoiceGenerator.DeriveChoice(Seed, Player, attempt, parameters);
            parameters.PresetChoices.Select(x => x.Choice).ShouldContain(choice);
        }
    }

    [Fact]
    public void DeriveChoice_SpreadsAcrossThePresetChoices()
    {
        var parameters = PresetParameters("alpha", "bravo", "charlie", "delta");

        var drawn = Enumerable.Range(0, 400)
            .Select(i => OrbitChoiceGenerator.DeriveChoice(Seed, Guid.NewGuid(), 0, parameters))
            .Distinct()
            .ToList();

        drawn.Count.ShouldBe(4);   // every option is reachable — a biased or constant index fails here
    }

    // Helpers build a GameParameters with the charset/length/preset shape under test.
    private static GameParameters FreeTextParameters(int length, bool acceptDuplicates) => new()
    {
        ChoiceRequiredLength = length,
        AcceptDuplicatedCharacters = acceptDuplicates,
        AllowedCharacters = GameCharacterType.Letters,
        PresetChoices = new List<PresetChoice>(),
    };

    private static GameParameters PresetParameters(params string[] choices) => new()
    {
        ChoiceRequiredLength = choices.Max(x => x.Length),
        AcceptDuplicatedCharacters = true,
        AllowedCharacters = GameCharacterType.Letters,
        PresetChoices = choices.Select(c => new PresetChoice { Choice = c }).ToList(),
    };
}
```

⚠️ Check `GameCharacterType`'s real member names and `PresetChoice`'s real property name against
`src/SpaceStroker.Domain/Games/` before running — adjust the helpers, not the assertions.

- [ ] **Step 2: Run them and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — `OrbitChoiceGenerator` does not exist.

- [ ] **Step 3: Implement the generator**

Create `src/SpaceStroker.Domain/Orbit/Services/OrbitChoiceGenerator.cs` per spec §6.8. Key points the tests pin:

- `DeriveSeed` = `HMAC-SHA256(key: serverSeed, msg: $"orbit-seed|{playerId:D}")`, hex, lowercase.
- `DeriveChoice` builds `ProvablyFair.Keystream(serverSeed, $"orbit|{playerId:D}|{attempt}")`.
- Preset: order `PresetChoices` by `StringComparer.Ordinal`, index with `ProvablyFair.NextIndex` (rejection sampling — a `%` would bias and fail the spread test).
- Free-text: draw `ChoiceRequiredLength` characters from `AllowedCharacters.StringValueOf()`; when `!AcceptDuplicatedCharacters`, remove each drawn character from the working pool before the next draw.

⚠️ Ordinal ordering is required for offline reproducibility — the same discipline `SpaceStrokerTyper.DecideFallbackWinner` already follows.

- [ ] **Step 4: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS, all 8 new cases.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Domain/Orbit/Services src/SpaceStroker.Tests/UnitTests/Domain/Orbit
git commit -m "Add the provably-fair Orbit choice generator."
```

---

### Task 4: Round-open trigger — bus event, early seed commit, gate scheduler

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs:414-446` (`CreateGame`), and near `:523` (`ScheduleLottery`)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/IGameService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Background/CreateGameTests.cs`

**Interfaces:**
- Produces: `GameCreatedEvent` on the bus; `IGameService.ScheduleOrbitGateExpiry(Guid gameId, DateTime expiresAt)` → `string` (Hangfire job id). Consumed by Tasks 6 and 8.

- [ ] **Step 1: Write the failing tests**

Append to `CreateGameTests.cs`:

```csharp
[Fact]
public async Task CreateGame_PublishesTheGameCreatedEventOnTheBus()
{
    var messageBus = new Mock<IMessageBus>();
    var service = BuildGameService(messageBus: messageBus);   // existing factory in this test class

    await service.CreateGame(ActiveRaceParameters());

    messageBus.Verify(x => x.PublishEventAsync(It.IsAny<GameCreatedEvent>()), Times.Once);
}

[Fact]
public async Task CreateGame_CommitsTheServerSeedBeforeAnyBetExists()
{
    var service = BuildGameService();

    await service.CreateGame(ActiveRaceParameters());

    var game = await Context.Games.AsNoTracking().SingleAsync();
    game.ServerSeed.ShouldNotBeNullOrWhiteSpace();
    game.ServerSeedHash.ShouldNotBeNullOrWhiteSpace();
    (await Context.GameBets.CountAsync()).ShouldBe(0);
}
```

⚠️ `EnsureServerSeedCommitted` uses `ExecuteSqlRawAsync`, which **does not run on the EF in-memory provider**. If `BuildGameService` is in-memory, this second test belongs in the integration suite (Task 14) instead — check the harness before writing it here, and move it rather than weakening the assertion.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — no publish, no seed.

- [ ] **Step 3: Publish the event and commit the seed**

In `CreateGame`, after `await _context.SaveChangesAsync();` (line 432) add:

```csharp
// The commitment must predate every bet — Orbit derives its choices from it at placement time,
// before any bet exists. Idempotent first-writer-wins, so the existing RegisterBetHandler and
// RunGameConsumer calls stay as harmless backstops.
await EnsureServerSeedCommitted(game.Id);
```

and after the existing SignalR send (line 440) add:

```csharp
// SignalR reaches browsers only; Consumers takes its work off the bus. Without this the auto-bet
// engine has no signal that a round opened.
await _messageBus.PublishEventAsync(gameCreatedEvent);
```

⚠️ Both stay **inside** the existing `try/catch` — a publish failure must not stop the round opening.

- [ ] **Step 4: Add `ScheduleOrbitGateExpiry`**

Beside `ScheduleLottery` in `GameService.cs`:

```csharp
public string ScheduleOrbitGateExpiry(Guid gameId, DateTime expiresAt)
{
    var delay = expiresAt - DateTime.UtcNow;
    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

    var jobId = BackgroundJob.Schedule(() => _messageBus.PublishEventAsync(new BetPlacedEvent(gameId)), delay);

    Log.Information("Scheduled the Orbit gate expiry. JobId={JobId}, GameId={GameId}, RunAt={RunAt}", jobId, gameId, expiresAt);

    return jobId;
}
```

Add it to `IGameService` (unlike `ScheduleLottery`, the planner needs it through the interface). A stale job needs no cancelling — `BetPlacedConsumer` already returns early on a round no longer in `GameAcceptingBets`.

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services/Games src/SpaceStroker.Tests
git commit -m "Publish GameCreatedEvent on the bus and commit the server seed at round creation."
```

---

### Task 5: `RegisterBetRequest.FundedByOrbit` + the three handler changes

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs:52-53, 63, 93, 171-185`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs`

**Interfaces:**
- Produces: `RegisterBetRequest.FundedByOrbit` (`bool`, `init`). Consumed by Task 7.

⚠️ **This is the security-critical task.** The flag must never reach `RegisterBetRequestBody`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task OrbitFundedBet_SkipsOnChainPaymentValidation()
{
    var settlement = new Mock<ITokenSettlementService>();
    var handler = BuildHandler(settlement: settlement);

    var request = new RegisterBetRequest(GameId, PaidBody()) { FundedByOrbit = true };
    await handler.HandleRequest(request, CancellationToken.None);

    settlement.Verify(x => x.ValidatePayment(It.IsAny<Bet>()), Times.Never);
}

[Fact]
public async Task OrbitFundedBet_StillRejectsAReusedPaymentSignature()
{
    var handler = BuildHandler();
    await SeedBetWithSignature(PaidBody().PaymentSignature!);

    var request = new RegisterBetRequest(GameId, PaidBody()) { FundedByOrbit = true };

    await Should.ThrowAsync<Exception>(() => handler.HandleRequest(request, CancellationToken.None));
    Messaging.Messages.ShouldContain(m => m.Contains(ErrorMessages.PaymentSignatureHasBeenUsed));
}

[Fact]
public async Task OrbitFundedBet_PersistsTheFundingMarker()
{
    var handler = BuildHandler();

    var request = new RegisterBetRequest(GameId, PaidBody()) { FundedByOrbit = true };
    await handler.HandleRequest(request, CancellationToken.None);

    (await Context.GameBets.AsNoTracking().SingleAsync()).FundedByOrbit.ShouldBeTrue();
}

[Fact]
public async Task OrdinaryBet_StillValidatesPaymentAndIsNotMarked()
{
    var settlement = new Mock<ITokenSettlementService>();
    settlement.Setup(x => x.ValidatePayment(It.IsAny<Bet>()))
              .ReturnsAsync(PaymentResult.Success("sig"));
    var handler = BuildHandler(settlement: settlement);

    await handler.HandleRequest(new RegisterBetRequest(GameId, PaidBody()), CancellationToken.None);

    settlement.Verify(x => x.ValidatePayment(It.IsAny<Bet>()), Times.Once);
    (await Context.GameBets.AsNoTracking().SingleAsync()).FundedByOrbit.ShouldBeFalse();
}

[Fact]
public void TheOrbitFlagIsNotPartOfThePublicRequestBody()
{
    // The body is model-bound from a PUBLIC, unauthenticated POST. If this ever fails, a caller can
    // skip payment validation and mint unfunded stakes into a real pot.
    typeof(RegisterBetRequestBody).GetProperties()
        .ShouldNotContain(p => p.Name.Contains("Orbit", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — `FundedByOrbit` does not exist on the request.

- [ ] **Step 3: Add the property to the request**

In `RegisterBetRequest.cs`, add the property **with the full doc comment from spec §6.5** (it is the only place the security reasoning is recorded):

```csharp
public bool FundedByOrbit { get; init; }
```

- [ ] **Step 4: Make the three handler changes**

`RegisterBetHandler.cs`:

```csharp
// beside _isFreeBet (line ~53)
private bool _fundedByOrbit;

// in HandleRequest, beside the _isFreeBet assignment (line ~63)
_fundedByOrbit = request.FundedByOrbit;

// on the bet construction (line ~93-98)
_bet.FundedByOrbit = _fundedByOrbit;
```

and in `ValidatePayment()` (line ~181), replace the settlement call with:

```csharp
// An Orbit stake is sent by the ORBIT ACCOUNT, not the player's wallet — ValidateBetPayment asserts
// sender == bet.GetWalletAddress() and would reject every Orbit bet. The placement consumer has
// already confirmed the transfer on-chain before reaching here. The reuse guard above still applies.
if (_fundedByOrbit)
    return;

var result = await _settlement.ValidatePayment(_bet);

if (!result.IsSuccess)
    Messaging.ReturnValidationFailureMessage(result.FailureMessage ?? ErrorMessages.WalletTemporarilyUnavailable);
```

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS, including the pre-existing `RegisterBetHandlerTests` cases unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet src/SpaceStroker.Tests
git commit -m "Let an Orbit-funded bet skip on-chain payment validation, off an internal-only flag."
```

---

### Task 6: `IOrbitBetPlacementService` — candidates, viability, arm-and-fan-out, failure

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitBetPlacementService.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs:55` (register `AddScoped`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs`

**Interfaces:**
- Consumes: `IOrbitAccountResolver`, `IGameService.ScheduleOrbitGateExpiry`, `IMessageBus`, `OrbitSettings.OrbitBetGateTimeoutSeconds`.
- Produces:
  - `Task<IReadOnlyList<Guid>> ResolveCandidates(GameParameters parameters, CancellationToken ct)`
  - `Task PlanPlacements(Game game, CancellationToken ct)` — viability + arm + fan out; publishes nothing when not viable.
  - `Task FailPlacement(Guid gameId, Guid playerId, string reason, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

Cover, with an in-memory context: Orbit unconfigured ⇒ no publish; `AllowOrbit` false ⇒ no publish; lottery ⇒ no publish; no candidates ⇒ no publish; candidates < minimum ⇒ **no publish and no gate**; candidates satisfy the minimum alone ⇒ gate armed **and** N publishes; existing human bets already satisfy the minimum ⇒ N publishes but **`ExpectedOrbitBets` stays null**; a self-excluded player and an under-funded player are excluded from candidates.

⚠️ `PlanPlacements` arms the gate with `ExecuteUpdateAsync`, which is a no-op in-memory. Assert the **publish behaviour** here and prove the gate write in the integration suite (Task 14). Do not weaken the design to make it unit-testable.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — the service does not exist.

- [ ] **Step 3: Implement the service**

`ResolveCandidates` is the query in spec §6.3 verbatim. `PlanPlacements` follows spec §6.2 steps 1-5 — orchestration only, each step its own private method per `conventions/backend.md`. `FailPlacement` is spec §6.6 verbatim (atomic floor-guarded decrement, `Log.Error`, republish `BetPlacedEvent`).

⚠️ **Order matters:** the gate write must complete before the first publish, or `BetPlacedConsumer` can start the round the instant the first Orbit bet lands.

- [ ] **Step 4: Register it**

```csharp
services.AddScoped<IOrbitBetPlacementService, OrbitBetPlacementService>();
```

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services/Orbit src/SpaceStroker.Infrastructure/DependencyInjection.cs src/SpaceStroker.Tests
git commit -m "Add the Orbit placement planner service."
```

---

### Task 7: `PlaceOrbitBetConsumer` — claim, debit, transfer, register

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetEvent.cs`
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs`

**Interfaces:**
- Consumes: `IOrbitLedgerService.TryDebit`, `IOrbitAccountResolver.ToDescriptor`, `ISolanaService.TransferTokensToWallet` / `GetSolBalance`, `OrbitChoiceGenerator`, `IMediator`, `IOrbitBetPlacementService.FailPlacement`.
- Produces: `public record PlaceOrbitBetEvent(Guid GameId, Guid PlayerId);`

- [ ] **Step 1: Write the failing tests**

Cover: round no longer accepting bets ⇒ no transfer; player self-excluded ⇒ no transfer; player already has a bet this round ⇒ no transfer; SOL below the minimum ⇒ `Error` + no transfer; all 10 choice attempts collide ⇒ no transfer + `FailPlacement`; transfer returns null ⇒ compensating `Refund` row + `ledger.Credit` + `FailPlacement`; happy path ⇒ `IMediator.Send` called once with `FundedByOrbit == true`.

Assert **"no transfer"** via `Mock<ISolanaService>.Verify(TransferTokensToWallet, Times.Never)` — that is the property that matters, since a signed transfer is money gone.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — the consumer does not exist.

- [ ] **Step 3: Implement the consumer**

Follow spec §6.4 steps 1-7 exactly, extending `BaseConsumer<PlaceOrbitBetEvent>` with the `IServiceScopeFactory` ctor every sibling consumer uses. The claim+debit transaction is spec §6.4 step 4 verbatim.

Three things that are easy to get wrong and that the tests above pin:

- A **lost claim** (`23505`) returns early and is **not** a `FailPlacement` — the winner will place the bet, so decrementing would start the round one bet short.
- The transfer's destination is `settings.Operator.Token.TokenOwnerPublicKey` (the game's token account owner) and the descriptor is `accounts.ToDescriptor(includePrivateKey: true)` — the **Orbit** descriptor, whose `SenderAccount` is the Orbit **ATA**.
- The transfer must be **confirmed** before `IMediator.Send`, because Task 5 removed the on-chain re-validation.

- [ ] **Step 4: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement src/SpaceStroker.Tests
git commit -m "Add the per-player Orbit bet placement consumer."
```

---

### Task 8: `OrbitPlacementPlannerConsumer` + MassTransit registration

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitPlacementPlanning/OrbitPlacementPlannerConsumer.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs:69-77` (`AddConsumer`) and `:121-126` (`ReceiveEndpoint`)
- Test: `src/SpaceStroker.Tests/UnitTests/Consumers/OrbitPlacementPlannerTests.cs`

**Interfaces:**
- Consumes: `IOrbitBetPlacementService.PlanPlacements`, `GameCreatedEvent`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task PlannerDelegatesToThePlacementService()
{
    var placement = new Mock<IOrbitBetPlacementService>();
    var consumer = BuildConsumer(placement);

    await consumer.ProcessMessage(new GameCreatedEvent(GameId, RaceParameters()));

    placement.Verify(x => x.PlanPlacements(It.Is<Game>(g => g.Id == GameId), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — the consumer does not exist.

- [ ] **Step 3: Implement the consumer**

Thin: resolve a scope, load the `Game` by `message.GameId`, delegate to `PlanPlacements`. All the decision logic lives in Task 6's service so it is unit-testable without a bus.

- [ ] **Step 4: Register both consumers**

In `SpaceStroker.Consumers/Program.cs`, following the shipped `nameof(TConsumer)` convention:

```csharp
x.AddConsumer<OrbitPlacementPlannerConsumer>();
x.AddConsumer<PlaceOrbitBetConsumer>();
```

```csharp
cfg.ReceiveEndpoint(nameof(OrbitPlacementPlannerConsumer), e => e.ConfigureConsumer<OrbitPlacementPlannerConsumer>(context));
cfg.ReceiveEndpoint(nameof(PlaceOrbitBetConsumer),        e => e.ConfigureConsumer<PlaceOrbitBetConsumer>(context));
```

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/OrbitPlacementPlanning src/SpaceStroker.Consumers/Program.cs src/SpaceStroker.Tests
git commit -m "Wire the Orbit placement planner onto the bus."
```

---

### Task 9: `BetPlacedConsumer` — the start gate and the second trigger

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs:90-98`
- Test: `src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: gate armed and under-filled and unexpired ⇒ `TryStartGame` **never** called; gate armed and filled ⇒ started; gate armed but expired ⇒ started regardless of the count; **no** gate ⇒ today's behaviour byte-for-byte; round becomes viable with no gate armed ⇒ `PlanPlacements` called once.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — the gate is not honoured.

- [ ] **Step 3: Insert the gate check**

Between the under-subscription block (ends line 90) and the `remainingWait` delay (line 94), add spec §6.9's block verbatim.

⚠️ Position is load-bearing: **after** the under-subscription branch so a doomed round keeps today's behaviour, and **before** the `Task.Delay` so a held round does not park a message-handling slot for the full window.

- [ ] **Step 4: Add the second placement trigger**

Immediately after the gate check, when the round is now viable and `game.ExpectedOrbitBets == null`, resolve `IOrbitBetPlacementService` and call `PlanPlacements`. The `== null` guard is what stops it firing on every subsequent bet.

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS, including every pre-existing `BetPlacedHandlerTests` case unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/BetPlaced src/SpaceStroker.Tests
git commit -m "Hold the round start until the expected Orbit bets land."
```

---

### Task 10: Orbit-bound settlement — `destinationWallet` + `IOrbitBetSettlementService`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Solana/TokenSettlementService.cs:40-53, 81-112`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitBetSettlementService.cs` + impl
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetSettlementServiceTests.cs`

**Interfaces:**
- Produces:
  - `ITokenSettlementService.Credit(Bet, decimal, CancellationToken, string? destinationWallet = null)`
  - `ITokenSettlementService.Refund(Bet, decimal, string reason, CancellationToken, string? destinationWallet = null)`
  - `IOrbitBetSettlementService.CreditWin(Bet bet, decimal amount, CancellationToken ct)` → `PaymentResult`
  - `IOrbitBetSettlementService.RefundToOrbit(Bet bet, decimal amount, string reason, CancellationToken ct)` → `PaymentResult`

- [ ] **Step 1: Write the failing tests**

Cover: `CreditWin` transfers to the **Orbit account** (never `bet.GetWalletAddress()`), writes a `TransactionLog` `Credit` row **and** an `OrbitTransaction` `WinCredit` row carrying `BetId`, and calls `ledger.Credit` once; a failed transfer writes **neither** ledger row; `RefundToOrbit` mirrors it with `Refund`; and — the regression guard — `settlement.Credit` with no `destinationWallet` still pays `bet.GetWalletAddress()`.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL — the service does not exist.

- [ ] **Step 3: Add the optional destination to the settlement service**

Add `string? destinationWallet = null` to both interface members and both implementations. In `Credit`:

```csharp
var destination = destinationWallet ?? bet.GetWalletAddress()!;

var signature = destinationWallet is null
    ? await _solana.TransferTokensForBet(bet, transferAmount, descriptor)
    : await _solana.TransferTokensToWallet(destinationWallet, transferAmount, descriptor);

if (string.IsNullOrWhiteSpace(signature))
    return PaymentResult.Failure(SettlementUnavailable);

await WriteLedger(bet, TransactionLogType.Credit, amount, transferAmount, signature, destination, null);
return PaymentResult.Success(signature);
```

`Refund` takes the same shape, keeping `relatedSignature: bet.Signature`.

⚠️ **Stop and reconsider if any existing settlement test needs changing.** These are the money path for every paid bet; the default branch must be byte-for-byte today's behaviour. If the shapes fight, add a sibling method instead of bending the shipped path (spec §15 item 2).

- [ ] **Step 4: Implement `IOrbitBetSettlementService`**

Per spec §6.10: call settlement with `destinationWallet: accounts.OrbitAccount`, then — on success, in one transaction — append the `OrbitTransaction` row and `ledger.Credit`. `CreditWin` additionally evaluates the threshold per spec §6.11 (`_cashOutReserver.Reserve(playerId, OrbitCashOutSource.Threshold, enforceLimits: false, ct)`).

⚠️ The `TransactionLog` `Credit` row is **not optional** — `BetDomainService.CalculateRewardForBet` subtracts `SumTransactionsForBet(bet, Credit)` for idempotency, so omitting it double-pays on retry.

- [ ] **Step 5: Register it and run the tests**

```csharp
services.AddScoped<IOrbitBetSettlementService, OrbitBetSettlementService>();
```

Run: `scripts/run_tests.bat`
Expected: PASS, with **zero** changes to the pre-existing settlement tests.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Services src/SpaceStroker.Tests
git commit -m "Route Orbit-funded wins and refunds back into the Orbit account."
```

---

### Task 11: `RewardGameConsumer` — the payout branch

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs:102`
- Test: `src/SpaceStroker.Tests/UnitTests/Background/RewardWinnersHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: an Orbit-funded winner goes through `CreditWin` and **not** `settlement.Credit`; a non-Orbit winner is unchanged; a mixed round pays each by its own route; a threshold crossing reserves a `Threshold` cash-out.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL.

- [ ] **Step 3: Add the branch**

Resolve `IOrbitBetSettlementService` beside `settlement` (line ~65), then replace line 102:

```csharp
var creditResult = winner.FundedByOrbit
    ? await orbitBets.CreditWin(winner, rewardResult.Value, CancellationToken.None)
    : await settlement.Credit(winner, rewardResult.Value, CancellationToken.None);
```

⚠️ Leave the blockhash pre-check (lines 71-83) alone — an Orbit payout is still a real on-chain transfer, just to a different destination.

- [ ] **Step 4: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/RewardGame src/SpaceStroker.Tests
git commit -m "Pay an Orbit-funded winner into the Orbit balance."
```

---

### Task 12: Refunds — event flag, consumer branch, sweep job

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetEvent.cs`
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs`
- Modify: `src/SpaceStroker.Infrastructure/Jobs/PendingRefundSweepJob.cs`
- Test: `.../Background/RefundInvalidBetConsumerTests.cs`, `.../RefundInvalidBetWithoutBetTests.cs`, `.../PendingRefundSweepJobTests.cs`

- [ ] **Step 1: Write the failing tests**

Cover: an Orbit-funded bet refunds to the Orbit account with an `OrbitTransaction` `Refund` row; a non-Orbit bet is unchanged; the **bet-less** path honours `FundedByOrbit` on the event and resolves `PlayerId` from `Wallet`; the sweep job carries `bet.FundedByOrbit` onto the event it publishes.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL.

- [ ] **Step 3: Extend the event**

```csharp
public record RefundInvalidBetEvent(
    Guid BetId, string Wallet, decimal Value, string Reason,
    Guid? GameId = null, string? PaymentSignature = null,
    bool FundedByOrbit = false);
```

A trailing optional parameter keeps every existing construction site compiling.

- [ ] **Step 4: Branch both consumer paths and the sweep**

Per spec §6.12. Bet-found: `bet.FundedByOrbit` ⇒ `RefundToOrbit`. Bet-less (`RefundWithoutBet`): the event flag is the only signal — resolve `PlayerId` from `Players.ExternalPlayerId == message.Wallet`. In `PendingRefundSweepJob`, pass `bet.FundedByOrbit` as the new argument.

- [ ] **Step 5: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Consumers/RefundInvalidBet src/SpaceStroker.Infrastructure/Jobs src/SpaceStroker.Tests
git commit -m "Return an Orbit-funded stake to the Orbit account on refund."
```

---

### Task 13: Owner-scoped DTO fields

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/BetDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/GameHistoryDto.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Contracts/BetDtoTests.cs`, `.../Handlers/GetGamesHistoryHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

The one that matters most:

```csharp
[Fact]
public void ABetDtoForAnotherPlayerNeverRevealsOrbit()
{
    var dto = new BetDto(OrbitFundedBetOfAnotherPlayer(), callerWallet: "someone-else");
    dto.ViaOrbit.ShouldBeFalse();
}
```

plus: the owner's own row **does** carry it; `GameHistoryRow.MyBetViaOrbit` is populated only for rounds the caller bet in.

- [ ] **Step 2: Run and watch them fail**

Run: `scripts/run_tests.bat`
Expected: FAIL.

- [ ] **Step 3: Add the fields, owner-scoped**

`BetDto.ViaOrbit` is set **only** when the row belongs to the caller; `GameHistoryDto.MyBetViaOrbit` joins `FundedByOrbit` into the existing `my*` block in `GameHistoryService`.

⚠️ AC23: an Orbit bet must be indistinguishable to everyone else. If the DTO shape makes owner-scoping awkward, **stop and ask** rather than exposing the flag broadly.

- [ ] **Step 4: Run the tests**

Run: `scripts/run_tests.bat`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStroker.Infrastructure/Contracts src/SpaceStroker.Infrastructure/Services/Games src/SpaceStroker.Tests
git commit -m "Expose the Orbit funding marker on the owner's own rows only."
```

---

### Task 14: Integration tests (MANDATORY — the card is `Feature`)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/OrbitAutoBetFlowTests.cs`
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` (Orbit enrolment seeder)

**Interfaces:**
- Consumes: `fixture.ConsumersSolanaService` (⚠️ **not** `fixture.SolanaService` — every transfer happens in the Consumers host, which resolves a different singleton), `SeedIsolatedTypeAsync`, `WaitForStatusAsync`, `WaitUntilAsync`.

This is the **only** place the claim's `23505`, the atomic debit and the gate write actually execute — none of them run on the EF in-memory provider.

- [ ] **Step 1: Add the fixture seeder**

`SeedOrbitEnrollmentAsync(string wallet, Guid parametersId, decimal balance)` — creates the `Player`, the `OrbitEnrollment` (enabled, pointed at the type) and an `OrbitBalance` in the configured pool, plus the matching `Deposit` ledger row so the balance invariant holds from the start.

- [ ] **Step 2: Write test 1 — the happy path (AC28a)**

Two funded wallets on an isolated `AllowOrbit` race type with `MinimumNumberOfPlayers = 2`; create a round; assert two bets land with `FundedByOrbit`, two `BetDebit` rows carrying `GameId`, and each balance debited exactly once; drive to `RewardForWinnersCompleted`; assert the winner's `WinCredit`, a `TransactionLog` `Credit` to the Orbit account, the ledger sum equal to the balance column, and **no** transfer to a personal wallet.

- [ ] **Step 3: Write test 2 — refund into Orbit (AC28b)**

Force a refund on an Orbit-funded bet; assert the stake returns to the Orbit account with a `Refund` row, the balance is restored **exactly once**, and the player's wallet received nothing.

- [ ] **Step 4: Write test 3 — the claim (AC7)**

Publish the same `PlaceOrbitBetEvent` twice; assert exactly one bet, one `BetDebit` row and one debit.

- [ ] **Step 5: Write test 4 — the gate (AC9/AC11/AC12)**

With `fixture.ConsumersSolanaService.NextTransferFails` set for one player, assert the round still starts, without that player, and **before** `OrbitBetGateTimeoutSeconds` elapses — that proves the decrement fired rather than the ceiling expiring.

- [ ] **Step 6: Run the integration suite**

Run: `scripts/run_integration_tests.bat`
Expected: PASS, all four new tests plus every pre-existing one.

⚠️ If a pre-existing game-flow test breaks, that is a **real finding** about the gate or the seed-commit change — fix the code, not the assertion, unless the assertion encoded the old commit timing.

- [ ] **Step 7: Commit**

```bash
git add src/SpaceStroker.Tests.Integration
git commit -m "Add the mandatory Orbit auto-bet integration tests."
```

---

### Task 15: Frontend — the two owner-only surfaces

**Files:**
- Modify: `src/types/index.ts` (`GameHistoryRow.myBetViaOrbit`, `AppliedBet.viaOrbit`)
- Modify: `src/hooks/useGame.ts` (`myBetThisRoundIsOrbit`, the `/Bets` push, `betOutput` copy)
- Modify: `src/components/game/GameConsole.vue` (the owner-only note, both layouts)
- Modify: `src/components/game/GameTerminal.vue` (`betPlayerLabel` → `'YOU · ORBIT'`)
- Modify: `src/pages/GameHistoryPage.vue` (the ORBIT chip, desktop + mobile)

⚠️ **Do not reach for `useOrbit`.** `useOrbit.load()` is deliberately never called on mount (a session costs a wallet prompt), so the game screen has no Orbit state. The mechanism is already present: an Orbit bet carries the player's real wallet, so `iHaveBetThisRound` is already true and `betLimitBlocks` already greys the panel. Only the copy is wrong.

- [ ] **Step 1: Add the two type fields**

```ts
// GameHistoryRow, inside the existing my* block
myBetViaOrbit?: boolean | null;

// AppliedBet — only ever true for the caller's own bets
viaOrbit?: boolean;
```

- [ ] **Step 2: Add the computed and carry the flag on live bets**

In `useGame.ts`, beside `iHaveBetThisRound`:

```ts
const myBetThisRoundIsOrbit = computed(
  () =>
    !!gameInstance.value?.bets.some(
      (b) => b.externalPlayerId === currentPlayerId.value && b.viaOrbit
    )
);
```

Add it to the return list, and in the `{gameId}/Bets` handler add `viaOrbit: newBet.viaOrbit ?? false` to the pushed row — without it a bet placed while the player watches loses the flag until reload.

- [ ] **Step 3: Make the refusal copy Orbit-aware**

Where `betOutput` is set to `'One bet per round in this game.'` and `'Waiting for another player before you can bet again.'`, use `'Orbit placed your bet for this round.'` when `myBetThisRoundIsOrbit`.

- [ ] **Step 4: Add the persistent note and the tags**

`GameConsole.vue`: render the note under the choice input when `myBetThisRoundIsOrbit`, in both the mobile branch and the desktop lever area, styled `vt323-regular text-[#7a8a9e]` like the other disabled affordances. `GameTerminal.vue`: `betPlayerLabel` returns `'YOU · ORBIT'` when the bet carries `viaOrbit`. `GameHistoryPage.vue`: append an ORBIT chip in the desktop `YOU` cell and the mobile card.

- [ ] **Step 5: Lint and build**

```bash
yarn lint
yarn build
```
Expected: both clean. ⚠️ Prettier violations are ESLint **errors** — run `yarn lint:fix` for reflow rather than hand-wrapping.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "Show the owner-only Orbit markers on the game screen and in history."
```

---

## Self-Review

**Spec coverage.** §4 → Task 1. §5 → Task 2. §6.1 → Task 4. §6.2/§6.3/§6.6 → Task 6. §6.4 → Task 7. §6.5 → Task 5. §6.7 → Task 4. §6.8 → Task 3. §6.9 → Task 9. §6.10/§6.11 → Tasks 10-11. §6.12 → Task 12. §6.13 → Tasks 6, 8, 10. §7 → Task 15. §10.1 rows 22-24 (DTOs) → Task 13. §11.1 → distributed across tasks. §11.2 → Task 14. No spec section is unassigned.

**Type consistency.** `FundedByOrbit` (C# `Bet` + `RegisterBetRequest` + `RefundInvalidBetEvent`), `viaOrbit` / `myBetViaOrbit` (TS) are used identically everywhere they appear. `PlanPlacements`, `ResolveCandidates`, `FailPlacement`, `CreditWin`, `RefundToOrbit`, `ScheduleOrbitGateExpiry`, `DeriveSeed`, `DeriveChoice` each keep one name across all tasks.

**Known in-memory limits, stated rather than worked around.** Task 4 step 1 (seed commit), Task 6 (gate write), Task 7 (claim + debit) all use `ExecuteUpdateAsync`/raw SQL and are proven in Task 14, not in unit tests. The unit tests assert the surrounding decisions instead.
