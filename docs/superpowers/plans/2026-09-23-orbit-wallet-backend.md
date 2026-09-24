# Orbit Wallet — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a player place manual bets on any paid game from their Orbit custodial balance (session-only, no per-bet signature), with winnings and refunds back to the balance, while auto-bet keeps working and blocks manual play on the type it auto-plays.

**Architecture:** A session-gated API endpoint (`POST /orbit/wagers`) runs cheap pre-checks, records a `Pending` result in Redis and publishes `PlaceManualOrbitBetEvent`. A new Consumers consumer reuses the auto engine's claim → house-signed transfer → `RegisterBetHandler` path (extracted into `OrbitStakePlacer`). A new `Bet.OrbitAutoPlaced` flag separates "auto-bet" semantics from "paid from custody" (`FundedByOrbit`) everywhere.

**Tech Stack:** .NET 8, EF Core (Postgres, inline mapping), MediatR, MassTransit (Postgres transport), Redis (`ICacheService`), xUnit + Shouldly + Moq + in-memory EF, Testcontainers integration harness.

**Authoritative design:** `monkify-meta/specs/orbit-wallet.md` (read §3, §4, §6 before every task). Section references below (`§6.3`) point there.

## Global Constraints

- Work only in `E:\Monkify\.worktrees\orbit-wallet\monkify-backend` on `feature/orbit-wallet`. Never touch the hub `E:\Monkify\monkify-backend`.
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; migrations via `dotnet ef migrations add`; never hand-edit the snapshot.
- Money moves only in `SpaceStroker.Consumers` (API publishes, never signs).
- `SpaceStrokerDbContext` is **NoTracking by default** — use `ExecuteUpdate` or a tracked re-query to persist changes.
- Unit tests: run with `scripts\run_tests.bat` (never the full `dotnet test`; `ProductionSessions` is slow). Filtered runs: `dotnet test SpaceStroker.Tests --filter "FullyQualifiedName~<Class>"`.
- Integration tests: `scripts\run_integration_tests.bat` (Docker). Filter: `dotnet test SpaceStroker.Tests.Integration --filter "FullyQualifiedName~OrbitWalletFlowTests"`.
- Refusal copy: reuse existing `ErrorMessages`; the ONLY new clear messages are `OrbitInsufficientBalance` = "Not enough Orbit balance for that bet." and `OrbitAutoPlayActive` = "Orbit is auto-playing this game — pause it to bet manually."
- Never log session tokens, keys or signatures. Mock-only config values.
- Do NOT sed-bulk-edit `.cs` files (CRLF flip). Edit files individually.
- Commit after each task with a descriptive message ending in `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Do not push (the orchestrator pushes).

---

### Task 1: Data model — `Bet.OrbitAutoPlaced`, `OrbitTransaction.PlacementRequestId`, filtered claim index, migration

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Entities/Bet.cs` (next to `FundedByOrbit`)
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitTransaction.cs`
- Create: `src/SpaceStroker.Domain/Orbit/ValueObjects/OrbitBetSource.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (Bet block ~L200-280, OrbitTransaction block ~L399-432)
- Create: `src/SpaceStroker.Infrastructure/Migrations/<ts>_AddOrbitManualBets.cs` (generated) + snapshot (generated)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetRequest.cs`, `RegisterBetHandler.cs` (copy flag onto `_bet` next to `FundedByOrbit`, ~L119)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (FundedByOrbit block ~L1987)

**Interfaces — Produces:**
- `Bet.OrbitAutoPlaced : bool` (default false)
- `OrbitTransaction.PlacementRequestId : Guid?`
- `enum OrbitBetSource { Auto = 1, Manual = 2 }` in `SpaceStroker.Domain.Orbit.ValueObjects`
- `RegisterBetRequest.OrbitAutoPlaced { get; init; }` (bool, never bound from body)

- [ ] **Step 1: Failing test** — in `RegisterBetHandlerTests`, next to `RegisterBet_FundedByOrbit_PersistsTheFundingMarker`, add `RegisterBet_OrbitAutoPlaced_PersistsTheAutoMarker` (send `new RegisterBetRequest(gameId, body) { FundedByOrbit = true, OrbitAutoPlaced = true }`, assert the persisted bet has `OrbitAutoPlaced == true`) and `RegisterBet_FundedByOrbitManual_IsNotMarkedAuto` (`FundedByOrbit = true`, `OrbitAutoPlaced = false` → persisted `false`). Copy the arrange code of the neighbouring FundedByOrbit test.
- [ ] **Step 2: Run** `dotnet test SpaceStroker.Tests --filter "FullyQualifiedName~RegisterBetHandlerTests.RegisterBet_OrbitAutoPlaced"` → FAIL (property does not exist).
- [ ] **Step 3: Implement**

```csharp
// Bet.cs
/// True ONLY for a bet the Orbit ENGINE placed (PlaceOrbitBetConsumer). A manual bet paid from the
/// Orbit balance is FundedByOrbit = true, OrbitAutoPlaced = false. Everything that means "auto-bet"
/// (start gate, house-seed count, stop rules, win toast, $STROKE exclusion, BetDto.ViaOrbit) reads
/// THIS, never FundedByOrbit. See specs/orbit-wallet.md §4.1.
public bool OrbitAutoPlaced { get; set; }

// OrbitTransaction.cs
/// The client idempotency key of a MANUAL bet placement (specs/orbit-wallet.md §4.2). Null on every
/// other row. Set on the manual BetDebit claim and on its case-A compensation.
public Guid? PlacementRequestId { get; set; }

// OrbitBetSource.cs
namespace SpaceStroker.Domain.Orbit.ValueObjects
{
    public enum OrbitBetSource { Auto = 1, Manual = 2 }
}

// RegisterBetRequest.cs (next to FundedByOrbit)
/// Set only by PlaceOrbitBetConsumer (the engine). Never bound from the body.
public bool OrbitAutoPlaced { get; init; }

// RegisterBetHandler.cs — field next to _fundedByOrbit, assigned where _fundedByOrbit is, and copied onto _bet next to FundedByOrbit (~L119)
private bool _orbitAutoPlaced;
// ... _orbitAutoPlaced = request.OrbitAutoPlaced;
// ... OrbitAutoPlaced = _orbitAutoPlaced,
```

`SpaceStrokerDbContext`:

```csharp
// Bet block, next to FundedByOrbit
builder.Property(x => x.OrbitAutoPlaced).IsRequired().HasDefaultValue(false);

// OrbitTransaction block: REPLACE the existing unique (OrbitAccount, MintAddress, PlayerId, GameId) index
builder.HasIndex(x => new { x.OrbitAccount, x.MintAddress, x.PlayerId, x.GameId })
       .IsUnique()
       .HasFilter("\"PlacementRequestId\" IS NULL");
builder.HasIndex(x => x.PlacementRequestId)
       .IsUnique()
       .HasFilter("\"PlacementRequestId\" IS NOT NULL");
```

Keep the existing comment above the index and extend it: the auto claim is one-per-player-per-round; manual claims are one-per-request (Postgres NULLs are distinct, so the composite MUST be filtered rather than widened).

- [ ] **Step 4: Migration**

```bash
cd src/SpaceStroker.Infrastructure
dotnet ef migrations add AddOrbitManualBets --startup-project ../SpaceStroker.Consumers/SpaceStroker.Consumers.csproj --context SpaceStrokerDbContext
```

Then edit ONLY the generated migration's `Up` to add, after the `AddColumn` for `OrbitAutoPlaced`:

```csharp
// Every Orbit-funded bet before #194 was engine-placed. Without this a round open at deploy holds
// its start gate one bet short and historic auto wins relabel as Manual.
migrationBuilder.Sql("UPDATE \"GameBets\" SET \"OrbitAutoPlaced\" = true WHERE \"FundedByOrbit\" = true;");
```

Verify the table name `GameBets` against the generated `AddColumn` call. `Down` needs no inverse SQL.

- [ ] **Step 5: Run** the two new tests → PASS; then `scripts\run_tests.bat` → all green (the auto consumer still passes `FundedByOrbit` only; that is fixed in Task 3).
- [ ] **Step 6: Commit** `Add OrbitAutoPlaced and PlacementRequestId with a filtered Orbit claim index.`

---

### Task 2: Auto-only readers switch to `OrbitAutoPlaced`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` (~L151-161: `gameBets.Count(x => x.FundedByOrbit)`)
- Modify: `src/SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` (`CountOrbitPlayers`, ~L223)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (`EvaluateOrbitFloors` ~L296-323 — the distinct FundedByOrbit bettors)
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`CreditWin` L51-69)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/JackpotRewardService.cs` (~L110-126)
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/BetDto.cs` (L27-30)
- Test: `BetPlacedConsumerTests` (find via `grep -rl ExpectedOrbitBets SpaceStroker.Tests`), `HouseSeed*Tests`, `RewardGameConsumerTests`, `OrbitBetSettlementServiceTests`, `JackpotRewardServiceTests`, `BetDtoTests`

**Interfaces — Consumes:** `Bet.OrbitAutoPlaced` (Task 1).

- [ ] **Step 1: Failing tests** (one per reader; copy the arrange of the closest existing test in each file and flip the bet to `FundedByOrbit = true, OrbitAutoPlaced = false`):
  - Gate: a round with `ExpectedOrbitBets = 1` and one **manual** Orbit bet stays held (does not start). The existing auto tests must set `OrbitAutoPlaced = true` on their Orbit bets — update them.
  - HouseSeed: `CountOrbitPlayers` ignores a manual Orbit bet.
  - `OrbitBetSettlementServiceTests`: `CreditWin_ManualOrbitBet_CreditsButNeverEvaluatesThresholdOrNotifies` — enrolment threshold below the post-win balance; assert a `WinCredit` row exists, the reserver's `Reserve` is **never** called, and no `OrbitWinNotificationEvent` is published. Existing auto tests get `OrbitAutoPlaced = true`.
  - `RewardGameConsumerTests`: `AManualOnlyOrbitPlayer_IsNeverFloorEvaluated` — floor above the post-loss balance, only a manual Orbit bet → no floor cash-out.
  - `JackpotRewardServiceTests`: rename `PayWinners_OrbitFundedWinner_ShouldBeSkipped` → `PayWinners_OrbitAutoPlacedWinner_ShouldBeSkipped` (set `OrbitAutoPlaced = true`); add `PayWinners_ManualOrbitWinner_IsPaidToTheirWallet` (`FundedByOrbit = true`, `OrbitAutoPlaced = false` → paid at `bet.GetWalletAddress()`).
  - `BetDtoTests`: `ViaOrbit` true only for `OrbitAutoPlaced` (caller-scoped as today).
- [ ] **Step 2: Run** those classes → the new ones FAIL.
- [ ] **Step 3: Implement** — replace `FundedByOrbit` with `OrbitAutoPlaced` in exactly these readers:

```csharp
// BetPlacedConsumer — the start gate counts only bets the ENGINE placed (#194).
gameBets.Count(x => x.OrbitAutoPlaced) < game.ExpectedOrbitBets

// HouseSeedBetService.CountOrbitPlayers — same substitution in its predicate.

// RewardGameConsumer.EvaluateOrbitFloors — iterate distinct PlayerIds of bets with OrbitAutoPlaced.

// OrbitBetSettlementService.CreditWin
var result = await Settle(bet, amount, OrbitTransactionType.WinCredit, ...);   // unchanged
if (bet.OrbitAutoPlaced)
{
    // Stop rules and the private "Orbit won" toast are AUTO-BET concepts (#194 decisions 9, 11).
    await EvaluateThreshold(...);
    await PublishWinNotification(...);
}

// JackpotRewardService — replace `.Where(bet => !bet.FundedByOrbit)` with:
// Auto-placed Orbit wins belong to custody and the engine; a MANUAL Orbit winner is a player who
// chose this ticket and gets the promised bonus at their own wallet ($STROKE is never custodied).
.Where(bet => !bet.OrbitAutoPlaced)

// BetDto — ViaOrbit now means "the engine placed it" (drives the SPA's console lock, the once-per-round
// Orbit pill debit and the YOU · ORBIT label, all auto concepts).
```

Keep the existing structure of `CreditWin` (read it first); only gate the two calls. Do NOT change `RewardGameConsumer` L181 (`FundedByOrbit` → `CreditWin`), `RefundInvalidBetConsumer` or `ValidatePayment`.

- [ ] **Step 4: Run** the touched classes, then `scripts\run_tests.bat` → green.
- [ ] **Step 5: Commit** `Read OrbitAutoPlaced, not FundedByOrbit, wherever Orbit means auto-bet.`

---

### Task 3: Extract `OrbitStakePlacer` from `PlaceOrbitBetConsumer` + the exactly-once make-whole rule

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/IOrbitStakePlacer.cs`, `OrbitStakePlacer.cs`
- Modify: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` (L178-228 + helpers L271-477)
- Modify: DI registration where `IOrbitBetPlacementService` is registered (grep `AddScoped<IOrbitBetPlacementService`) — register `IOrbitStakePlacer` alongside, in BOTH hosts that register Orbit services.
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitStakePlacerTests.cs` (new); `Consumers/PlaceOrbitBetConsumerTests.cs` (must stay green)

**Interfaces — Produces:**

```csharp
namespace SpaceStroker.Infrastructure.Services.Orbit
{
    public enum StakePlacementOutcome { Placed, AlreadyClaimed, InsufficientFunds, TransferFailed, RegistrationRefused }

    public sealed record StakePlacementResult(StakePlacementOutcome Outcome, Guid? BetId = null, string? Message = null);

    /// <param name="PlacementRequestId">null ⇒ AUTO claim (one per player per round);
    /// set ⇒ MANUAL claim (one per request).</param>
    public sealed record StakePlacement(
        Guid GameId, Player Player, decimal Stake, string Choice, string Seed, long Nonce,
        bool OrbitAutoPlaced, Guid? PlacementRequestId, string Memo);

    public interface IOrbitStakePlacer
    {
        /// Solvency guard (pool-aware), unchanged from PlaceOrbitBetConsumer.IsLowOnFees.
        Task<bool> IsLowOnFees();
        /// claim+debit → transfer → register → stamp; makes the player whole exactly once on failure.
        Task<StakePlacementResult> Place(StakePlacement placement);
    }
}
```

- [ ] **Step 1: Failing tests** in `OrbitStakePlacerTests` (use in-memory EF + Moq for `ISolanaService`, `IMediator`, `IOrbitLedgerService` or the real ledger service as `PlaceOrbitBetConsumerTests` does — read that file first and mirror its fixture):
  1. `Place_Manual_WritesDebitWithRequestIdAndGameId_AndUsesTheMemo` — assert the `BetDebit` row has `PlacementRequestId`, `GameId`, `Amount == -stake`, and `TransferTokensWithMemo` was called with memo `orbit-manual:{id:N}`; `RegisterBetRequest` has `FundedByOrbit=true`, `OrbitAutoPlaced=false`, the given choice/seed/nonce, `Wallet = player.ExternalPlayerId`.
  2. `Place_SameRequestIdTwice_DebitsOnce` → second call returns `AlreadyClaimed`, one debit row, one transfer.
  3. `Place_InsufficientBalance_NoTransfer` → `InsufficientFunds`, no debit row persisted, transfer never called.
  4. `Place_TransferDidNotLand_LedgerOnlyRefundOnce` (case A) → exactly one `Refund` row with `PlacementRequestId` set and `GameId` null, balance restored; calling the compensation twice adds nothing.
  5. `Place_RegistrationRefused_WithRefundClaim_DoesNotCreditLedger` (case B1) → mediator throws after a `PaymentRefundClaim` for the signature was saved; assert NO ledger `Refund` row and no extra `RefundInvalidBetEvent` published.
  6. `Place_RegistrationRefused_WithoutRefundClaim_PublishesWithoutBetRefund` (case B2) → mediator throws, no claim row; assert `IMessageBus.PublishEventAsync(RefundInvalidBetEvent{ FundedByOrbit = true, PaymentSignature = signature, BetId = Guid.Empty })` once and NO ledger-only credit.
  7. `Place_Auto_KeepsTheExistingPerGameClaimAndMemo` → `PlacementRequestId == null`, memo `orbit:{gameId:N}:{playerId:N}`, `OrbitAutoPlaced = true` on the request.
- [ ] **Step 2: Run** → FAIL (type missing).
- [ ] **Step 3: Implement** by MOVING code out of `PlaceOrbitBetConsumer` (keep comments):
  - `ClaimAndDebit`: insert `OrbitTransaction { Type = BetDebit, Amount = -stake, GameId, PlacementRequestId, Status = Completed, OrbitAccount, MintAddress, PlayerId }`; 23505 → `ChangeTracker.Clear()` + rollback → `AlreadyClaimed` (works for both indexes); `TryDebit` false → rollback → `InsufficientFunds`.
  - `TransferStake(memo)`: unchanged except the memo is a parameter.
  - `StampClaim`: key by `PlacementRequestId` when set, else by `(pool, player, GameId, Type == BetDebit, PlacementRequestId == null)`.
  - `CompensateFailedTransfer` (case A only): dedupe by `PlacementRequestId` when set (existing `Refund` row with that id), else by the existing per-game `Reason`. The manual compensation row sets `PlacementRequestId` and `GameId = null`.
  - Post-transfer registration failure (case B):

```csharp
// §6.3 — the stake is in the TREASURY now. A ledger-only credit here would pay the player from money
// custody no longer holds. Whoever moves it back on-chain credits the ledger (RefundToOrbit).
var refundClaimed = await context.PaymentRefundClaims.AsNoTracking()
    .AnyAsync(x => x.PaymentSignature == signature);
if (!refundClaimed)
    await bus.PublishEventAsync(new RefundInvalidBetEvent(
        Guid.Empty, player.ExternalPlayerId, stake, reason, gameId, signature, FundedByOrbit: true));
return new StakePlacementResult(StakePlacementOutcome.RegistrationRefused, Message: reason);
```

   `reason` = the first validation message the handler left in `IMessaging` if available, else `ErrorMessages.FailedToRegisterBet` (check the name in `ErrorMessages.resx`; use the existing generic bet-failure message).
  - ⚠️ **Before switching the auto path to case B**, run `dotnet test SpaceStroker.Tests.Integration --filter "FullyQualifiedName~ARefundedOrbitStake_ReturnsToCustody_AndRestoresTheBalanceExactlyOnce"`. If it passes with the new rule, keep the rule for auto too. If the new rule breaks it, STOP and report to the orchestrator (do not silently diverge).
  - Verify `RefundInvalidBetConsumer.RefundWithoutBet` accepts `BetId == Guid.Empty` (it looks the bet up by signature first). If it requires a real id, report instead of guessing.
  - `PlaceOrbitBetConsumer` now: planning/eligibility/derived-choice logic stays; it calls `placer.IsLowOnFees()` and `placer.Place(new StakePlacement(game.Id, player, stake, choice, OrbitChoiceGenerator.DeriveSeed(game.ServerSeed, player.Id), 0, OrbitAutoPlaced: true, PlacementRequestId: null, Memo: $"orbit:{game.Id:N}:{player.Id:N}"))`, mapping outcomes to the SAME `SkipPlacement`/`FailPlacement` calls and messages as before (`AlreadyClaimed` → log + return without releasing the gate).
- [ ] **Step 4: Run** `OrbitStakePlacerTests`, `PlaceOrbitBetConsumerTests`, then `scripts\run_tests.bat` → green.
- [ ] **Step 5: Commit** `Extract OrbitStakePlacer and make a failed Orbit placement whole exactly once.`

---

### Task 4: Shared pre-check validator + `RegisterBetHandler` auto-play block + new error messages

**Files:**
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (+ `ErrorMessages.Designer.cs` — add the two properties by hand in the same shape as `OrbitNotEnabled`, since the designer is not regenerated by the build)
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitAutoPlay.cs` (static query helper)
- Create: `src/SpaceStroker.Infrastructure/Services/Games/BetPreChecks.cs` (+ interface `IBetPreChecks`)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` (after the ComingSoon check ~L168, before `ValidatePayment` L174)
- Test: `RegisterBetHandlerTests.cs`, new `SpaceStroker.Tests/UnitTests/Services/BetPreChecksTests.cs`

**Interfaces — Produces:**

```csharp
// ErrorMessages
OrbitInsufficientBalance = "Not enough Orbit balance for that bet."
OrbitAutoPlayActive      = "Orbit is auto-playing this game — pause it to bet manually."

// OrbitAutoPlay
public static class OrbitAutoPlay
{
    public static Task<bool> IsActive(SpaceStrokerDbContext context, Guid playerId, Guid gameParametersId, CancellationToken ct = default)
        => context.OrbitEnrollments.AsNoTracking()
            .AnyAsync(x => x.PlayerId == playerId && x.Enabled && x.GameParametersId == gameParametersId, ct);
}

// BetPreChecks — READ-ONLY replicas of RegisterBetHandler's refusals, returning the SAME message the
// handler would, or null when the bet would pass. Used by the manual Orbit path BEFORE any money moves.
public interface IBetPreChecks
{
    /// game must include Parameters (+PresetChoices, LotteryParameters, BoardChoices).
    Task<string?> Check(Game game, Guid playerId, string choice, decimal amount, CancellationToken ct);
}
```

`BetPreChecks.Check` order and messages (read `RegisterBetHandler.ValidateBetParameters` L287-402 and `BetDomainService` first; reuse `BetDomainService` methods, don't re-implement their logic):
1. game not in `Game.GameAcceptingBets` or `Parameters.ComingSoon` or `!Parameters.Active` → `ErrorMessages.GameNotValidForBets`
2. stake: house-edge → `BetDomainService.HouseEdgeChoiceIsValid` rule for amount (min/max/9dp) → its message (`InvalidAmount`); else `amount != RequiredAmount` → same `InvalidAmount` message
3. house-edge: `BoardExpiresAt < UtcNow + 10s` or round already owned/has a bet → the existing house-edge board-expired/changed message the handler returns in `ValidateSoloRoundUnderLock`
4. `BetDomainService.ChoiceIsValidForGame` failure → its message
5. free-text: choice taken in the round (`GameBets` `ExclusiveChoice`) or an `OrbitChoiceClaim` by another player on `(ParametersId, choice)` → `ErrorMessages.ChoiceAlreadyTaken`
6. per-round limits (not house-edge): `OneBetPerPresetRound` / `SecondBetNeedsAnotherPlayer`, same predicates as L322-346

Extract the predicates of 5-6 into private static methods on `RegisterBetHandler`'s side ONLY if it keeps both call sites identical; otherwise duplicate the two short predicates in `BetPreChecks` with a `// keep in sync with RegisterBetHandler.ValidateBetParameters` comment.

- [ ] **Step 1: Failing tests**
  - `RegisterBetHandlerTests`: `RegisterBet_WalletBet_OnATypeOrbitAutoPlays_IsRefundedWithAutoPlayMessage` (enrolment `Enabled`, same `GameParametersId`; assert `RefundInvalidBetEvent` published with `OrbitAutoPlayActive`, no bet row); `RegisterBet_WalletBet_WithPausedEnrolment_IsAccepted`; `RegisterBet_EngineBet_OnItsOwnEnrolledType_IsAccepted` (`FundedByOrbit=true, OrbitAutoPlaced=true`); `RegisterBet_ManualOrbitBet_OnATypeOrbitAutoPlays_IsRefused` (`FundedByOrbit=true, OrbitAutoPlaced=false`).
  - `BetPreChecksTests`: one test per rule 1-6 returning the expected message, plus `Check_ValidBet_ReturnsNull`, plus a house-edge amount above `MaximumStake` and an over-precise amount (`0.0020000001m`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** — in `RegisterBetHandler`, after the ComingSoon line:

```csharp
// #194 decision 4 — a game type Orbit is auto-playing for this player cannot also be played by hand.
// Applies to wallet bets AND manual Orbit bets, never to the engine's own bets. Placed WITH
// self-exclusion/ComingSoon, ahead of payment validation, so a transfer that already landed is refunded.
if (!_orbitAutoPlaced && player != null && betGame != null
    && await OrbitAutoPlay.IsActive(_spacestrokerDbContext, player.Id, betGame.ParametersId, cancellationToken))
    await RefundInvalidBet(ErrorMessages.OrbitAutoPlayActive);
```

(`RefundInvalidBet` throws for paid bets; for a free bet follow the self-exclusion pattern above it — return the validation message.) Register `IBetPreChecks` as scoped in the API host (where handlers' services are registered).
- [ ] **Step 4: Run** → PASS; `scripts\run_tests.bat` → green.
- [ ] **Step 5: Commit** `Refuse manual bets on a type Orbit auto-plays; add read-only bet pre-checks.`

---

### Task 5: Manual placement — event, consumer, result store, API endpoints, rate limits, deposit decoupling, history source

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Consumers/OrbitBetPlacement/PlaceManualOrbitBetEvent.cs`, `PlaceManualOrbitBetConsumer.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitWagerResultStore.cs` (+ `IOrbitWagerResultStore`)
- Modify: `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Orbit/PlaceOrbitWager/PlaceOrbitWagerRequest.cs`, `PlaceOrbitWagerHandler.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbitWager/GetOrbitWagerRequest.cs`, `GetOrbitWagerHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/OrbitController.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (~L92 `AddConsumer`, ~L167 receive endpoint)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (`IpRateLimiting.GeneralRules`, match existing entry syntax exactly)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` (delete L115-123 enrolment refusal)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbitHistory/GetOrbitHistoryHandler.cs` + `OrbitBetLogDto`
- Tests: new `Handlers/Orbit/PlaceOrbitWagerTests.cs`, `Handlers/Orbit/GetOrbitWagerTests.cs`, `Consumers/PlaceManualOrbitBetConsumerTests.cs`; modify `Handlers/Orbit/CreditOrbitDepositTests.cs`, `Api/RateLimitSettingsTests.cs`, the history DTO test (grep `OrbitBetLogDto` in tests)

**Interfaces — Consumes:** `IOrbitStakePlacer` (Task 3), `IBetPreChecks`, `OrbitAutoPlay`, error messages (Task 4), `OrbitBetSource` (Task 1).

**Produces:**

```csharp
public record PlaceManualOrbitBetEvent(Guid RequestId, Guid PlayerId, Guid GameId, string Choice,
                                       decimal Amount, string Seed, long Nonce);

public enum OrbitWagerStatus { Pending = 1, Placed = 2, Refused = 3, NotFound = 4 }
public sealed class OrbitWagerResultDto
{
    public Guid RequestId { get; set; }
    public OrbitWagerStatus Status { get; set; }
    public Guid? BetId { get; set; }
    public string? Message { get; set; }   // only existing vague copy or the two sanctioned clear messages
}

public interface IOrbitWagerResultStore
{
    void Set(Guid playerId, OrbitWagerResultDto result);
    bool TryGet(Guid playerId, Guid requestId, out OrbitWagerResultDto result);
}

// CacheHelper
public static CacheSettings GenerateOrbitWagerCacheSettings(Guid playerId, Guid requestId)
    => new($"OrbitWager:{playerId}:{requestId}", TimeSpan.FromMinutes(15));

public record PlaceOrbitWagerRequest(string Wallet, PlaceOrbitWagerRequestBody Body) : IRequest<OrbitWagerResultDto?>;
public class PlaceOrbitWagerRequestBody
{
    [Required] public Guid? RequestId { get; set; }
    [Required] public Guid? GameId { get; set; }
    [Required, MaxLength(20)] public string Choice { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
    [Required, MaxLength(64)] public string Seed { get; set; } = string.Empty;
    public long Nonce { get; set; }
}
public record GetOrbitWagerRequest(string Wallet, Guid RequestId) : IRequest<OrbitWagerResultDto?>;

// OrbitBetLogDto
public OrbitBetSource Source { get; set; }
```

`PlaceOrbitWagerHandler` (API, `BaseRequestHandler<PlaceOrbitWagerRequest, OrbitWagerResultDto?>`, mirror `RequestOrbitCashOutHandler`'s shape): steps exactly as spec §6.1 1-12 — configured → player by wallet (vague `OrbitRequestInvalid`) → `TryGet` existing result → return it → self-excluded (`SelfExclusionActive`) → load game with `Parameters.PresetChoices`, `Parameters.LotteryParameters`, `BoardChoices` → `OrbitAutoPlay.IsActive` → `OrbitAutoPlayActive` → `IBetPreChecks.Check` → `ledger.ResolveBalance(player.Id) < amount` → `OrbitInsufficientBalance` → `store.Set(Pending)` → `IMessageBus.PublishEventAsync(new PlaceManualOrbitBetEvent(...))` → return the Pending dto. Every refusal: `Messaging.ReturnValidationFailureMessage(msg); return null;` (→ 400 via `ProcessRequest`). Success returns 200 with the dto (the codebase's `ProcessRequest` pattern; the spec's "202" is realised as 200 + `status: Pending`).

`GetOrbitWagerHandler`: configured → player by wallet → store hit → return; miss → ledger fallback: `OrbitTransactions` for the player with `PlacementRequestId == id`: a `BetDebit` with `BetId != null` → Placed; a `Refund` row → Refused (generic message); else `NotFound`.

`PlaceManualOrbitBetConsumer : BaseConsumer<PlaceManualOrbitBetEvent>` (mirror `PlaceOrbitBetConsumer`'s scope/service resolution):

```csharp
// 1. configured; load game (same Include set as LoadGame) + player; missing → Refused(GameNotValidForBets / OrbitRequestInvalid)
// 2. player.IsSelfExcluded → Refused(SelfExclusionActive)
// 3. OrbitAutoPlay.IsActive → Refused(OrbitAutoPlayActive)
// 4. preChecks.Check(game, player.Id, choice, amount) != null → Refused(that message)
// 5. placer.IsLowOnFees() → Refused(ErrorMessages.GameNotValidForBets)   // vague on purpose
// 6. gameService.EnsureServerSeedCommitted(game.Id)
// 7. result = placer.Place(new StakePlacement(game.Id, player, amount, choice, seed, nonce,
//        OrbitAutoPlaced: false, PlacementRequestId: requestId, Memo: $"orbit-manual:{requestId:N}"))
// 8. Placed → store Placed{betId}; AlreadyClaimed → return (redelivery, leave the result alone);
//    InsufficientFunds → Refused(OrbitInsufficientBalance); TransferFailed → Refused(generic bet failure);
//    RegistrationRefused → Refused(result.Message)
// Never calls FailPlacement / SkipPlacement / ReleaseGateSlot / MarkPlanned.
```

`OrbitController`: add `[HttpOptions("wagers")]`, `[HttpOptions("wagers/{requestId}")]`, and:

```csharp
/// <summary>Places a MANUAL bet paid from the Orbit balance (#194). Session-only — no per-bet
/// signature (decision 2); the placement is asynchronous, poll GET /orbit/wagers/{requestId}.</summary>
[HttpPost("wagers")]
public async Task<IActionResult> PlaceWager([FromBody] PlaceOrbitWagerRequestBody body)
{
    if (RejectWithoutSession(out var wallet)) return NotFoundResult();
    return await ProcessRequest(new PlaceOrbitWagerRequest(wallet!, body));
}

[HttpGet("wagers/{requestId:guid}")]
public async Task<IActionResult> GetWager(Guid requestId)
{
    if (RejectWithoutSession(out var wallet)) return NotFoundResult();
    return await ProcessRequest(new GetOrbitWagerRequest(wallet!, requestId));
}
```

`Consumers/Program.cs`: `x.AddConsumer<PlaceManualOrbitBetConsumer>();` and its OWN `cfg.ReceiveEndpoint(nameof(PlaceManualOrbitBetConsumer), e => { ... e.ConfigureConsumer<PlaceManualOrbitBetConsumer>(context); });` copying the endpoint options of `PlaceOrbitBetConsumer`'s endpoint (concurrency/retry) verbatim. Register `IOrbitWagerResultStore` (scoped) in both hosts.

Rate limits (`spacestrokersettings.json`): add two rules for `/orbit/wagers` (POST, 30/1m) and `/orbit/wagers/*` (120/1m) using EXACTLY the syntax the existing `/orbit/*` rules use (they are `*:/orbit/...`; if per-verb or wildcard syntax is not used anywhere in the file, use `*:/orbit/wagers` 30/1m only and poll through the existing unlimited default — then state that in the report). Update `RateLimitSettingsTests.GeneralRules_AreExactlyTheQuotaProtectingPatterns`; `GeneralRules_NeverCoverTheBetPath` must stay green (no `/bets`).

Deposit: delete the `isEnrolled` block in `CreditOrbitDepositHandler` (L115-123) and its now-unused imports.

History: `GetOrbitHistoryHandler` projects `Source = bet.OrbitAutoPlaced ? OrbitBetSource.Auto : OrbitBetSource.Manual` (join `GameBets` on `BetId`, or read it from the already-joined bet if present; read the handler first).

- [ ] **Step 1: Failing tests**
  - `PlaceOrbitWagerTests` (extend `OrbitHandlerTestsBase`): not configured; no player → `OrbitRequestInvalid`; self-excluded; game not accepting; **auto-play active → `OrbitAutoPlayActive`**; paused enrolment accepted; pre-check message surfaces (mock `IBetPreChecks` returning `ChoiceAlreadyTaken`); **balance < amount → `OrbitInsufficientBalance`**; happy path → store `Set(Pending)` once and `PublishEventAsync(PlaceManualOrbitBetEvent)` once with the body's fields; **existing result for the requestId → returned, no publish**.
  - `GetOrbitWagerTests`: store hit returns it; another player's requestId → `NotFound` (store is keyed by player); store miss + ledger `BetDebit` with `BetId` → `Placed`; miss + `Refund` row → `Refused`; nothing → `NotFound`.
  - `PlaceManualOrbitBetConsumerTests`: happy path calls `placer.Place` with `OrbitAutoPlaced=false`, the requestId and memo `orbit-manual:{id:N}` and stores `Placed{betId}`; self-excluded / auto-play / pre-check / low SOL → `Refused` with the right message and `Place` never called; `AlreadyClaimed` → store untouched; never touches `ExpectedOrbitBets` (seed a game with `ExpectedOrbitBets = 1`, assert unchanged).
  - `CreditOrbitDepositTests`: replace the "no enrolment → refused" test with `Deposit_WithoutAnEnrolment_IsCredited`.
  - `RateLimitSettingsTests`: expected set includes the new rule(s).
  - History test: a manual bet row reports `Source == Manual`, an auto one `Auto`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** the new/changed classes → PASS; `scripts\run_tests.bat` → green; `dotnet build` both hosts.
- [ ] **Step 5: Commit** `Add manual Orbit wagers: endpoint, consumer, result store, deposit without enrolment.`

---

### Task 6: Hyper Roll payout destination check

**Files:**
- Read/maybe modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (`AddHouseEdgePayout`, house-edge branch ~L109-183)
- Test: `RewardGameConsumerTests.cs`

- [ ] **Step 1: Read** `AddHouseEdgePayout` and everything that consumes its output. Decide: is `winner.GetWalletAddress()` there a transfer DESTINATION or only a recorded/display field?
- [ ] **Step 2: Failing test** `HouseEdgeWinner_FundedByOrbit_IsCreditedToCustody` — a Hyper Roll round won by a `FundedByOrbit=true, OrbitAutoPlaced=false` bet: assert `IOrbitBetSettlementService.CreditWin` is called with `stake × multiplier` and the ordinary `settlement.Credit` is NOT. (If this already passes, keep it as a regression pin.)
- [ ] **Step 3:** If `AddHouseEdgePayout`'s wallet is a destination, route `FundedByOrbit` winners to the Orbit account there. If it is only recorded, add a one-line comment saying so and change nothing.
- [ ] **Step 4: Run** → PASS; `scripts\run_tests.bat` green.
- [ ] **Step 5: Commit** `Pin Hyper Roll Orbit-funded payouts to custody.`

---

### Task 7: Integration tests — `OrbitWalletFlowTests` (mandatory)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/OrbitWalletFlowTests.cs`
- Read first: `OrbitFlowTests.cs` (session + deposit helpers), `OrbitAutoBetFlowTests.cs` (round completion, forcing a winner, `ConsumersSolanaService`), `HyperRollSoloRoundsTests.cs` (board open + forced win), `Harness/IntegrationTestFixture.cs`, `Harness/TestDoubles.cs` (`FakeSolanaService`: `NextTransferFails`, `LastTransferDescriptor`, `RefundedWallets`)

Tests (each must assert end state from the DB and the fake, not only HTTP codes):
1. `ManualOrbitBet_HappyPath_WinCreditsTheBalance_AndNoAutoCashOut` — player with **no enrolment** creates a session, deposits, sets NO enrolment; a second wallet places a normal bet so the Cosmic Sprint round can start; `POST /orbit/wagers` (Orbit player's winning choice — steer the winner the way `OrbitAutoBetFlowTests` does); poll `GET /orbit/wagers/{id}` until `Placed`; wait for the round to settle; assert: balance = deposit − stake + net win, a `WinCredit` row with the bet's `BetId`, `SUM(OrbitTransaction.Amount) == OrbitBalance.Amount`, bet row `FundedByOrbit=true` & `OrbitAutoPlaced=false`, and — after PATCHing an enrolment threshold below the final balance BEFORE the round settles — no `CashOut` row.
2. `AutoPlayBlock_RefusesManualOrbitAndWalletBets_UntilPaused` — enrolment `Enabled` on Cosmic Sprint → `POST /orbit/wagers` 400 with `OrbitAutoPlayActive`, zero new `OrbitTransaction` rows; a wallet bet on the same type is refunded (fake `RefundedWallets` contains it); PATCH `enabled:false` → the wager is accepted.
3. `HyperRoll_ManualOrbitWin_CreditsStakeTimesMultiplierToCustody` — open a board, wager a variable stake (between min and max, e.g. 0.005), force the win as `HyperRollSoloRoundsTests` does; assert `WinCredit == stake × multiplier` and the Consumers fake's last transfer destination is the Orbit account.
4. `PreDebitRefusal_MovesNoMoney` — a word already taken in the round → 400 `ChoiceAlreadyTaken` (or `Refused` from the poll), zero `OrbitTransaction` rows added, no transfer on the Consumers fake.
5. `SameRequestIdTwice_PlacesOneBet` — POST twice with one requestId → one bet, one `BetDebit`.
6. `TransferFailure_RestoresTheBalanceExactlyOnce` — `ConsumersSolanaService.NextTransferFails = true` → poll `Refused`, balance restored, exactly one `Refund` row with the requestId.

- [ ] **Step 1:** write test 1, run it (`dotnet test SpaceStroker.Tests.Integration --filter "FullyQualifiedName~OrbitWalletFlowTests"`), fix until green; then 2-6 one at a time.
- [ ] **Step 2:** run the whole Orbit integration set: `--filter "FullyQualifiedName~Orbit|FullyQualifiedName~HyperRoll|FullyQualifiedName~JackpotReward|FullyQualifiedName~HouseSeed|FullyQualifiedName~RateLimit"` → green; then `scripts\run_integration_tests.bat` → green.
- [ ] **Step 3: Commit** `Add Orbit wallet integration tests.`

---

## Self-review notes

- Spec §6.1 → Task 5; §6.2 → Tasks 3+5; §6.3 → Task 3; §6.4 → Task 5; §6.5 → Task 4; §6.6 → Task 2; §6.7 → Task 6; §6.8/§6.10 → Task 5; §6.9 → Task 4; §4 → Task 1; §5 → Task 5; §11 → every task + Task 7.
- Types: `StakePlacement`, `StakePlacementResult`, `IOrbitStakePlacer`, `IBetPreChecks.Check`, `OrbitAutoPlay.IsActive`, `OrbitWagerResultDto`, `IOrbitWagerResultStore`, `PlaceManualOrbitBetEvent` are defined once and used with the same names.
