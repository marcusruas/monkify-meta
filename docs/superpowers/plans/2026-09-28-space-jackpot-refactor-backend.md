# Space Jackpot — Free Daily Draw for $STROKE Holders — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Space Jackpot stops taking 0.1 SOL tickets. A wallet holding ≥ N $STROKE (≈ $10, frozen per round) enters free by signing a message; balances are rechecked at the draw (below N → ticket voided); the pot is house-funded, sized from treasury headroom, frozen at round open and reserved against Hyper Roll / House duels; the 100k $STROKE winner bonus is deleted.

**Architecture:** New `Bet.EntryKind` (Paid/Free/Holder) + `BetStatus.Voided`; `Game` carries the frozen `HolderMinimum`, `HouseFundedPot`, `HolderEntriesPaused`. Consumers size N (Jupiter price → 1-2-5 ladder) and the pot (headroom under the shared treasury advisory lock) in an optional `IHolderJackpotRoundPlanner` called from `GameService.CreateGame`. The API's `RegisterBetHandler` gets a holder branch (WalletProof `jackpot-entry` + Token-2022 ATA balance read). `BetPlacedConsumer` rechecks balances before the lottery player count. Reserved pots enter `HouseEdgeCoverage` as exposure. Startup self-heals flip the default type to holder-only and refund the legacy paid round.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, MassTransit, Hangfire, Solnet, xUnit + Shouldly + Moq, Testcontainers.

**Spec:** `monkify-meta/specs/space-jackpot-refactor.md` (read it first — §4–§6, §11). **This plan overrides the spec where the code digest proved the spec wrong** — see "Spec corrections" below; record them in the spec at ship.

## Global Constraints

- Worktree `C:\Users\kurok\Desktop\monkify\.worktrees\space-jackpot-refactor\monkify-backend`, branch `feature/space-jackpot-refactor`. Never edit the hub `C:\Users\kurok\Desktop\monkify\monkify-backend`. **Do not commit** until the user approves the whole feature (monkify-implement-feature Step 9) — tasks end with a local verification, not a commit.
- Files are **CRLF**; C# files start with a UTF-8 BOM. Edit with the Edit/Write tools; never `sed -i`/`perl -pi` over `.cs`/`.resx` (flips line endings / double-encodes UTF-8).
- Unit tests (from `src/SpaceStroker.Tests`): `dotnet test --filter "FullyQualifiedName~UnitTests&FullyQualifiedName!~ProductionGames"` (= `scripts/run_tests.bat`). Narrow with `&FullyQualifiedName~<Class>` while iterating. Integration (from `src/SpaceStroker.Tests.Integration`, Docker running): `dotnet test --filter "FullyQualifiedName~<Class>"`; full suite = `scripts/run_integration_tests.bat`. Never a bare `dotnet test`.
- Migrations: from `monkify-backend`: `dotnet ef migrations add HolderJackpot --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Consumers` — never `--no-build`; never hand-edit the snapshot; read the generated file.
- EF config inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`.
- Money/pot/typer work in **Consumers**; the API only verifies the entry and reads a balance.
- Messages (exact text): `JackpotEntryInvalid` = `We could not verify that request. Please try again.`; `HolderBalanceUnavailable` = `Your entry could not be processed. Please try again.`; `HolderBelowMinimum` = `Hold at least {0} ${1} to enter today's draw.` formatted with `CultureInfo.InvariantCulture` and `N0` → `Hold at least 1,000,000 $STROKE to enter today's draw.`
- Entry message (LF-joined, byte-identical to the front):
  ```
  Space Stroker jackpot entry
  Wallet: {wallet}
  Round: {gameId}
  Word: {choice}
  Nonce: {nonce}
  ```
  `{gameId}` = `Guid.ToString()` (lowercase, hyphenated, "D" format).
- Nonce purpose string: `jackpot-entry`.
- Holder mint (public, committed): `Dts45D3WfhuJuwpK5nYkPM6Jyu5DqeEQzTw3rJkopump`, symbol `STROKE`, decimals `6`, program `TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb` (Token-2022). **Never** read $STROKE through `GetTokenBalance` (classic derivation → silently 0).
- Gate every holder behaviour on `LotteryParameters.HolderOnly` / `Game.IsHolderRound`, **never** on the type name.
- Mock-only secrets. Jupiter lite needs no key.
- Commit trailer (Step 10 only): `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Spec corrections (decided from the code; reversible, implementation-level)

| # | Spec says | Plan does | Why |
|---|---|---|---|
| C1 | `BetStatus.Voided = 7` | `Voided = 6` | Enum is implicit 0..5; 6 is next. The front only sees `voided: bool`. |
| C2 | `IsFreeBet(this Bet)` | Keep `public static bool IsFreeBet(Bet bet)` (not an extension) = `Amount == 0 && EntryKind != Holder`; add `IsHolderEntry(Bet)` | Every caller writes `BetExtensions.IsFreeBet(x)`. |
| C3 | "RefundInvalidBet already skips Amount == 0" | Its guard, and the refund loops in `BetPlacedConsumer` (L129) and `GameTypeDeactivatedConsumer` (L56), change from `IsFreeBet(bet)` to `bet.Amount == 0` | Otherwise a refused holder entry writes a refund claim with a null signature and appends `RefundWarning`. |
| C4 | Planner injected into `GameService` | `GameService` ctor gains optional `IHolderJackpotRoundPlanner? holderPlanner = null`; planner registered in **Consumers only**. No planner (API) on a holder type → round opens PAUSED at `StartingMinimum` + `Error` log | `GameService` is built by both hosts and by 24 unit-test `new GameService(` calls. |
| C5 | `HolderBalanceReader` uses `ExecuteRpcAsync` | Two new `ISolanaService` methods `GetToken2022Balance` / `GetToken2022Balances` implemented in `SolanaService` (where the private RPC/Polly plumbing lives); `HolderBalanceReader` wraps them with the config/`DevAssumedBalance` rules | `ExecuteRpcAsync` and policies are private. Also gives the integration fake a natural seam. |
| C6 | Holder branch "right after GetGameById", then `ValidateBetParameters` unchanged | Holder entries set `_isFreeBet = false`, `_isHolderEntry = true`; `ValidateBetParameters` learns the holder flag (own per-round rule, skip the free-bet type gate, `ChoiceIsValidForGame(..., isFreeBet: true)`) | `_isFreeBet` is true for amount 0, which skips the per-round rule and hits `!AllowFreeBet → InvalidAmount`. |
| C7 | Unique violation on the holder index → per-round message | `GameService.InsertBetWithoutNotification` also catches 23505 on `IX_GameBets_GameId_PlayerId_Holder` → throws new `HolderTicketAlreadyExistsException`; handler maps it to `OneBetPerPresetRound` | Today only the ExclusiveChoice constraint is caught. |
| C8 | One-ticket rule via `PerRoundLimitRefusal` | New `BetRoundRules.HolderTicketRefusal(context, gameId, playerId)` → `ErrorMessages.OneBetPerPresetRound` when any bet of that player exists in the round | `PerRoundLimitRefusal` allows a second bet once another player bet. |
| C9 | Recheck only in `BetPlacedConsumer`; typer skip voided | **Also** filter voided out of `RunGameConsumer` L71 (mark winners) and `GameService.UpdateGameStatusCore` L321-327 (GameResult winners) | Both match `Choice == FirstChoiceTyped` over all bets. |
| C10 | Reconciler sets `AllowFreeBet=false` | Exclude `HolderOnly` defaults from the existing `AllowFreeBet = !IsProduction()` patch (`Consumers/Program.cs:463-466`) | Otherwise the two flip-flop every non-prod boot. |
| C11 | Transition runs before `OpenNewGames` | Runs right after `CreateDefaultGameParametersAsync`, **before** `CorrectScheduledLotteryDraws` / `ReplanOpenRounds` | `ReplanOpenRounds` republishes `GameOpenedEvent` for the legacy round. Only `WaitingBets` legacy rounds survive `CloseOngoingGames`. |
| C12 | Integration test 7 "restarts Consumers" | Calls `HolderJackpotTransition.RunAsync` and `CreateGame` through `CreateConsumersScope()` twice | The fixture has no restart; `DefaultGameSeedingTests` drives reconcilers the same way. |
| C13 | Draw steered with `PublishRunGameAsync` | Holder flow tests publish `BetPlacedEvent` (new fixture helper) so the recheck runs | `RunGameEvent` bypasses `BetPlacedConsumer`. |
| C14 | "existing API rate limiter covers the route" | Add an explicit `get:/games/*/holder-eligibility` rule (30/min) | Rate limits are per-endpoint rules. |
| C15 | `/games/{typeId}/bets` | The route is `POST /games/{roundId}/bets` (round id) — unchanged | Code fact. |
| C16 | `IsConfigured` "trims" | New `HolderTokenSettings.IsConfigured` = mint & program non-blank after trim, `Decimals` 0..18 | Old one depended on the deleted `AmountPerWinner`. |
| C17 | Stats "contribute nothing" | Exclude holder **credits** (paid-out, winners-paid, biggest payout, player won/net/biggest win) and holder rounds from `BiggestPot`, as the spec lists. Participation counts (bets placed, rounds played, unique players) are **unchanged** — same as dev free bets today | Decision 13 says "as free bets today"; free bets count as participation. |

## Review Focus

1. **A second entry from the same wallet in the same round (sequential or concurrent)** — must be exactly one ticket; the concurrent case must not surface `FailedToRegisterBet`. Pinned in Task 7 (unit, sequential + exception mapping) and Task 12 (integration, `Task.WhenAll` of two entries).
2. **A refused holder entry must never look like money moved** — no `RefundWarning`, no `PaymentRefundClaim`, no `RefundInvalidBetEvent`. Pinned in Task 7 (`HolderRefusal_NeverAppendsRefundWarning_OrPublishesARefund`).
3. **Holder round opening when the treasury balance read fails / lock times out / mint unconfigured** — round still opens (PAUSED), never throws out of `CreateGame`, the type never ends with no live round. Pinned in Task 6.
4. **A voided ticket whose word is typed first** — must not be marked `NeedsRewarding`, not appear in the `GameResult` winners, not be paid; the provably-fair payload still re-verifies. Pinned in Task 9 (unit) and Task 12 (integration pass-the-bag).
5. **Redelivered draw / reward events** — recheck never revives a void; a second `RewardGameEvent` never double-pays the pot. Pinned in Task 9 (unit) and Task 12 (happy path redelivery).

---

### Task 1: Domain model, EF mapping, migration, and the "no money moved" guards

**Files:**
- Create: `src/SpaceStroker.Domain/Bets/ValueObjects/BetEntryKind.cs`
- Modify: `src/SpaceStroker.Domain/Bets/Entities/Bet.cs`, `src/SpaceStroker.Domain/Bets/ValueObjects/BetStatus.cs`, `src/SpaceStroker.Domain/Bets/Services/BetExtensions.cs`
- Modify: `src/SpaceStroker.Domain/Games/Entities/Game.cs` (after `HouseWord` ~L140), `src/SpaceStroker.Domain/Games/Entities/LotteryParameters.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (LotteryParameters L100-109, Game L160-229, Bet L251-335)
- Create: migration `HolderJackpot` (generated) + backfill SQL
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs:1127` (RefundInvalidBet guard), `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs:129`, `src/SpaceStroker.Infrastructure/Consumers/GameTypeDeactivated/GameTypeDeactivatedConsumer.cs:56` (find exact path with Grep)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/BetExtensionsTests.cs` (create if absent — Grep first), `.../UnitTests/Domain/Games/HolderEntityMappingTests.cs` (new)

**Interfaces — Produces:**
- `enum BetEntryKind { Paid = 1, Free = 2, Holder = 3 }` (namespace `SpaceStroker.Domain.Bets.ValueObjects`)
- `Bet.EntryKind : BetEntryKind` (default `Paid`)
- `BetStatus.Voided` (= 6)
- `BetExtensions.IsFreeBet(Bet)` = `bet.Amount == 0 && bet.EntryKind != BetEntryKind.Holder`; `BetExtensions.IsHolderEntry(Bet)` = `bet.EntryKind == BetEntryKind.Holder`
- `Game.HolderMinimum : decimal?`, `Game.HouseFundedPot : decimal?`, `Game.HolderEntriesPaused : bool`, `Game.IsHolderRound => HolderMinimum != null` (get-only, unmapped)
- `LotteryParameters.HolderOnly : bool`
- DB index name `IX_GameBets_GameId_PlayerId_Holder`

- [ ] **Step 1: failing tests.** `BetExtensionsTests`:
```csharp
[Fact] public void AZeroAmountDevBet_IsFree() =>
    BetExtensions.IsFreeBet(new Bet(BetStatus.Made, 0m) { EntryKind = BetEntryKind.Free }).ShouldBeTrue();
[Fact] public void AHolderTicket_IsNotFree_ButIsAHolderEntry()
{
    var bet = new Bet(BetStatus.Made, 0m) { EntryKind = BetEntryKind.Holder };
    BetExtensions.IsFreeBet(bet).ShouldBeFalse();
    BetExtensions.IsHolderEntry(bet).ShouldBeTrue();
}
[Fact] public void ALegacyZeroAmountBet_DefaultsToPaidKind_ButStaysFree() =>
    BetExtensions.IsFreeBet(new Bet(BetStatus.Made, 0m)).ShouldBeTrue();   // EntryKind defaults to Paid; Amount 0 still free
[Fact] public void VoidedIsTheNextStatus() => ((int)BetStatus.Voided).ShouldBe(6);
[Fact] public void AHolderRoundIsOneWithAMinimum()
{
    new Game { HolderMinimum = 1_000_000m }.IsHolderRound.ShouldBeTrue();
    new Game().IsHolderRound.ShouldBeFalse();
}
```
`HolderEntityMappingTests` (in-memory `SpaceStrokerDbContext` — copy the context construction from an existing mapping test, e.g. Grep `DuelEntityMappingTests`): round-trip a `Game` with `HolderMinimum = 2_000_000m, HouseFundedPot = 0.23m, HolderEntriesPaused = true`, a `Bet` with `EntryKind = Holder`, and a `LotteryParameters` with `HolderOnly = true`; assert the model has a unique index named `IX_GameBets_GameId_PlayerId_Holder` with filter `"EntryKind" = 3`:
```csharp
var index = context.Model.FindEntityType(typeof(Bet))!.GetIndexes()
    .Single(i => i.GetDatabaseName() == "IX_GameBets_GameId_PlayerId_Holder");
index.IsUnique.ShouldBeTrue();
index.GetFilter().ShouldBe("\"EntryKind\" = 3");
context.Model.FindEntityType(typeof(Game))!.FindProperty(nameof(Game.IsHolderRound)).ShouldBeNull();
```
- [ ] **Step 2:** run `--filter "...&(FullyQualifiedName~BetExtensionsTests|FullyQualifiedName~HolderEntityMappingTests)"` → compile FAIL.
- [ ] **Step 3: implement.**
  - `BetEntryKind.cs` per Interfaces, with a doc comment: Paid = a SOL stake; Free = dev free bet (pays nothing, prod-disabled); Holder = Space Jackpot holder ticket (free entry, house-funded pot, paid out).
  - `Bet.cs`: `public BetEntryKind EntryKind { get; set; } = BetEntryKind.Paid;`
  - `BetStatus.cs`: append `Voided` with a comment "terminal; set only by the holder draw recheck (balance below the round's minimum). Never paid, never refunded (Amount 0)."
  - `BetExtensions.cs`: replace `IsFreeBet`, add `IsHolderEntry`. Update the comment.
  - `Game.cs`: the three properties + `IsHolderRound` with doc comments citing spec §4.2.
  - `LotteryParameters.cs`: `public bool HolderOnly { get; set; }` (doc: gate holder behaviour on this, never the name).
  - DbContext: LotteryParameters `builder.Property(x => x.HolderOnly).IsRequired().HasDefaultValue(false);` Game: `builder.Property(x => x.HolderMinimum).HasPrecision(20, 6).IsRequired(false); builder.Property(x => x.HouseFundedPot).HasPrecision(18, 9).IsRequired(false); builder.Property(x => x.HolderEntriesPaused).IsRequired().HasDefaultValue(false); builder.Ignore(x => x.IsHolderRound);` Bet: `builder.Property(x => x.EntryKind).HasConversion<int>().IsRequired().HasDefaultValue(BetEntryKind.Paid).HasSentinel(BetEntryKind.Paid);` and
    ```csharp
    builder.HasIndex(x => new { x.GameId, x.PlayerId })
        .IsUnique()
        .HasFilter("\"EntryKind\" = 3")
        .HasDatabaseName("IX_GameBets_GameId_PlayerId_Holder");
    ```
    ⚠️ An existing non-unique index `(PlayerId, GameId)` has the reversed column order; EF allows both. If EF complains that `(GameId, PlayerId)` duplicates the `(GameId, PlayerId, Choice)` prefix, it does not — different column sets. Keep all existing indexes.
  - `RegisterBetHandler.RefundInvalidBet` L1127: `if (_bet.Amount == 0)` (comment: "nothing was paid — free or holder entry — so nothing to refund and no RefundWarning"). `BetPlacedConsumer` L129 and `GameTypeDeactivatedConsumer` L56: `if (bet.Amount == 0)` with the same log line. Leave `BetPlacedConsumer` L101 (player count) on `IsFreeBet` — holder tickets now count by PlayerId, which is what we want.
- [ ] **Step 4:** generate the migration (command in Global Constraints). Open it; in `Up`, **after** the `AddColumn` for `EntryKind`, add `migrationBuilder.Sql("UPDATE \"GameBets\" SET \"EntryKind\" = 2 WHERE \"Amount\" = 0;");` and make sure the index creation comes after it. `Down` is generated (drop index + columns). Build.
- [ ] **Step 5:** run the Step 2 filter → PASS. Run the full unit suite → PASS (existing free-bet tests still pass: legacy bets are `Paid` kind with Amount 0 → still free).

---

### Task 2: Settings — `HolderTokenSettings`, `HolderJackpotSettings`, default game, and deleting the $STROKE bonus

**Files:**
- Rename: `src/SpaceStroker.Domain/Configs/Entities/JackpotRewardSettings.cs` → `HolderTokenSettings.cs` (`git mv`)
- Modify: `src/SpaceStroker.Domain/Configs/Entities/TokenSettings.cs:72-73`
- Create: `src/SpaceStroker.Domain/Configs/Entities/HolderJackpotSettings.cs`; Modify `GameSettings.cs` (add property), `OperatorSettings.cs` (`DefaultGameSettings.HolderOnly`)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (L133-163 Games, L211-217 JackpotReward, L391-405 Space Jackpot default)
- Delete: `src/SpaceStroker.Infrastructure/Services/Games/JackpotRewardService.cs`, `IJackpotRewardService.cs`; DI lines `DependencyInjection.cs:71-73`
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (remove `bonusWinners` L125-131, L201, the try block L251-263)
- Modify: `src/SpaceStroker.Infrastructure/Services/Solana/ISolanaService.cs:107-140`, `SolanaService.cs:677-779` — delete `TransferJackpotReward`; **keep the body of `GetJackpotRewardBalance` in your clipboard** — Task 5 reuses it. Delete the method itself.
- Modify: `src/SpaceStroker.Tests.Integration/Harness/TestDoubles.cs` (L184-186, L321-350 jackpot region), `src/SpaceStroker.Tests.Integration/OrbitWalletFlowTests.cs:726` (comment only)
- Delete: `src/SpaceStroker.Tests/UnitTests/Services/JackpotRewardServiceTests.cs`, `src/SpaceStroker.Tests.Integration/JackpotRewardFlowTests.cs`
- Rename: `src/SpaceStroker.Tests/UnitTests/Api/JackpotRewardSettingsBindingTests.cs` → `HolderTokenSettingsBindingTests.cs`; Create `HolderJackpotSettingsBindingTests.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` `ToGameParameters` (L705-713) to copy `HolderOnly`

**Interfaces — Produces:**
```csharp
public class HolderTokenSettings   // SpaceStroker.Domain.Configs.Entities
{
    public string MintAddress { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Decimals { get; set; }
    public string TokenProgramId { get; set; } = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MintAddress?.Trim()) && !string.IsNullOrWhiteSpace(TokenProgramId?.Trim())
        && Decimals >= 0 && Decimals <= 18;
}
// TokenSettings: public HolderTokenSettings HolderToken { get; set; } = new();

public class HolderJackpotSettings  // GameSettings: public HolderJackpotSettings HolderJackpot { get; set; } = new();
{
    public decimal TargetUsd { get; set; } = 10m;
    public decimal StartingMinimum { get; set; } = 1_000_000m;
    public decimal MinimumFloor { get; set; } = 100_000m;
    public decimal MinimumCeiling { get; set; } = 100_000_000m;
    public decimal PotPercent { get; set; } = 0.5m;          // percent, 0 < x < 1
    public decimal ReserveFloor { get; set; } = 5m;          // SOL
    public decimal PotMinimum { get; set; } = 0.05m;         // SOL
    public decimal PotMaximum { get; set; } = 2m;            // SOL
    public string PriceApiUrl { get; set; } = "https://lite-api.jup.ag/price/v3";
    public int PriceTimeoutSeconds { get; set; } = 5;
    public int DrawRecheckAttempts { get; set; } = 3;        // retries AFTER the first try → 1 + N tries
    public decimal? DevAssumedBalance { get; set; }          // ignored in Production (spec S6)

    /// <summary>Null when valid; otherwise the first rule broken (logged at Error, holder rounds open PAUSED).</summary>
    public string? Validate()
    {
        if (TargetUsd <= 0) return "TargetUsd must be > 0";
        if (!(MinimumFloor > 0 && MinimumFloor <= StartingMinimum && StartingMinimum <= MinimumCeiling)) return "Require 0 < MinimumFloor <= StartingMinimum <= MinimumCeiling";
        if (!(PotPercent > 0 && PotPercent < 1)) return "PotPercent must be > 0 and < 1 (percent)";
        if (ReserveFloor < 0) return "ReserveFloor must be >= 0";
        if (!(PotMinimum > 0 && PotMinimum <= PotMaximum)) return "Require 0 < PotMinimum <= PotMaximum";
        if (PriceTimeoutSeconds <= 0 || DrawRecheckAttempts < 0) return "PriceTimeoutSeconds > 0 and DrawRecheckAttempts >= 0";
        return null;
    }
}
// DefaultGameSettings: public bool HolderOnly { get; set; }
```

- [ ] **Step 1: failing tests.** Rename the binding test and point it at `GeneralSettings:Operator:Token:HolderToken`; assert mint `Dts45D3WfhuJuwpK5nYkPM6Jyu5DqeEQzTw3rJkopump`, symbol `STROKE`, decimals 6, program `TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb`, `IsConfigured` true; `new HolderTokenSettings { MintAddress = "  " }.IsConfigured.ShouldBeFalse()`. Keep the file's existing config-loading helper. `HolderJackpotSettingsBindingTests`: bind `spacestrokersettings.json` → `GeneralSettings.Games.HolderJackpot` equals every default above, `Validate()` is null, and table cases: `PotPercent = 1` → not null; `PotPercent = 0` → not null; `StartingMinimum < MinimumFloor` → not null; `PotMinimum > PotMaximum` → not null. Also assert the "Space Jackpot" entry of `Operator:DefaultGames` binds `HolderOnly == true`, `RequiredAmount == 0`, `LotteryPot == 0`, `MinimumNumberOfPlayers == 2`, `ChoiceRequiredLength == 6`, `AllowOrbit == false`.
- [ ] **Step 2:** run the two binding classes → FAIL.
- [ ] **Step 3: implement** the classes above. JSON: rename `JackpotReward` → `HolderToken` and drop `AmountPerWinner`; add under `GeneralSettings:Games`:
```json
"HolderJackpot": {
  "TargetUsd": 10, "StartingMinimum": 1000000, "MinimumFloor": 100000, "MinimumCeiling": 100000000,
  "PotPercent": 0.5, "ReserveFloor": 5, "PotMinimum": 0.05, "PotMaximum": 2,
  "PriceApiUrl": "https://lite-api.jup.ag/price/v3", "PriceTimeoutSeconds": 5, "DrawRecheckAttempts": 3,
  "DevAssumedBalance": null
}
```
(match the file's indentation style). Space Jackpot default: `"RequiredAmount": 0`, add `"HolderOnly": true`. `ToGameParameters`: set `LotteryParameters.HolderOnly = config.HolderOnly`. Delete the bonus service, its DI, the consumer block, the two ISolanaService methods (+ SolanaService impls, + FakeSolanaService members and their `Clear()` lines), the two test files; fix the OrbitWalletFlowTests comment to cite `LotteryPotStackingFlowTests` only. Grep `JackpotReward` across `src` → only `Token2022Program` comments may remain; update those comments to say "holder token".
- [ ] **Step 4:** unit suite + `dotnet build` of the whole solution (the integration project must compile) → PASS.

---

### Task 3: Pure rules — N ladder, pot, carry

**Files:**
- Create: `src/SpaceStroker.Domain/Games/Services/HolderMinimumRules.cs`, `HolderJackpotPotRules.cs`
- Modify: `src/SpaceStroker.Domain/Games/Services/LotteryPotCarry.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/HolderMinimumRulesTests.cs`, `HolderJackpotPotRulesTests.cs` (new); `LotteryPotCarryTests.cs` (add)

**Interfaces — Produces:**
- `HolderMinimumRules.Compute(decimal? priceUsd, decimal? previousN, HolderJackpotSettings s) : (decimal N, bool UsedFallback)`
- `HolderMinimumRules.LadderUp(decimal raw) : decimal` (public for tests)
- `HolderJackpotPotRules.Compute(decimal? houseBalance, decimal exposure, decimal feeReserve, HolderJackpotSettings s) : decimal?` (null = PAUSED)
- `HolderJackpotPotRules.Floor2Sig(decimal value) : decimal`

- [ ] **Step 1: failing tests.**
```csharp
public class HolderMinimumRulesTests
{
    private static readonly HolderJackpotSettings S = new();   // defaults: target 10, start 1M, floor 100k, ceiling 100M

    [Theory]
    [InlineData("900000", "1000000")] [InlineData("1000000", "1000000")] [InlineData("1010000", "2000000")]
    [InlineData("2100000", "5000000")] [InlineData("5000000", "5000000")] [InlineData("0.3", "0.5")]
    [InlineData("7", "10")] [InlineData("123456789", "200000000")]
    public void LadderUp_IsTheSmallest125ValueAtOrAboveRaw(string raw, string expected) =>
        HolderMinimumRules.LadderUp(decimal.Parse(raw, CultureInfo.InvariantCulture)).ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));

    [Fact] public void Price_Of_0_00001_Gives_1M() => HolderMinimumRules.Compute(0.00001m, null, S).ShouldBe((1_000_000m, false));
    [Fact] public void DoubleCap() => HolderMinimumRules.Compute(10m / 5_000_000m, 1_000_000m, S).N.ShouldBe(2_000_000m);
    [Fact] public void HalfCap() => HolderMinimumRules.Compute(10m / 100_000m, 2_000_000m, S).N.ShouldBe(1_000_000m);
    [Fact] public void ClampMayLandOffTheLadder() => HolderMinimumRules.Compute(10m / 5_000_000m, 2_000_000m, S).N.ShouldBe(4_000_000m);
    [Fact] public void Floor() => HolderMinimumRules.Compute(10m, null, S).N.ShouldBe(100_000m);                  // raw 1 → floor
    [Fact] public void Ceiling() => HolderMinimumRules.Compute(0.0000000001m, null, S).N.ShouldBe(100_000_000m);
    [Theory] [InlineData(null)] [InlineData("0")] [InlineData("-1")]
    public void BadPrice_KeepsPrevious(string? price) =>
        HolderMinimumRules.Compute(price is null ? null : decimal.Parse(price), 2_000_000m, S).ShouldBe((2_000_000m, true));
    [Fact] public void BadPrice_NoPrevious_UsesStarting() => HolderMinimumRules.Compute(null, null, S).ShouldBe((1_000_000m, true));
}

public class HolderJackpotPotRulesTests
{
    private static readonly HolderJackpotSettings S = new();   // 0.5%, reserve 5, min 0.05, max 2

    [Fact] public void Formula() => HolderJackpotPotRules.Compute(100m, 0m, 0.001m, S).ShouldBe(0.47m);   // (100-0-0.001-5)*0.5% = 0.474995 → 0.47
    [Fact] public void ExposureAndFeesComeOff() => HolderJackpotPotRules.Compute(100m, 20m, 1m, S).ShouldBe(0.37m);  // 74*0.5% = 0.37
    [Theory] [InlineData("0.237", "0.23")] [InlineData("1.284", "1.2")] [InlineData("12.9", "12")] [InlineData("0.0999", "0.099")]
    public void Floor2Sig(string v, string e) => HolderJackpotPotRules.Floor2Sig(decimal.Parse(v, CultureInfo.InvariantCulture)).ShouldBe(decimal.Parse(e, CultureInfo.InvariantCulture));
    [Fact] public void ClampedToMax() => HolderJackpotPotRules.Compute(10_000m, 0m, 0m, S).ShouldBe(2m);
    [Fact] public void RaisedToMin_WhenHeadroomCoversIt() => HolderJackpotPotRules.Compute(6m, 0m, 0m, S).ShouldBe(0.05m);  // headroom 1, 0.5% = 0.005 → min 0.05 ≤ 1
    [Fact] public void Paused_WhenHeadroomBelowTheFloor() => HolderJackpotPotRules.Compute(5.04m, 0m, 0m, S).ShouldBeNull();   // headroom 0.04 < 0.05
    [Fact] public void Paused_WhenBalanceUnreadable() => HolderJackpotPotRules.Compute(null, 0m, 0m, S).ShouldBeNull();
    [Fact] public void Paused_WhenHeadroomNegative() => HolderJackpotPotRules.Compute(1m, 0m, 0m, S).ShouldBeNull();
}
```
`LotteryPotCarryTests`: for each of `NotEnoughPlayersToStart`, `GameEndedAbruptely`, `RewardForWinnersCompleted` (no winner), a game with `HolderMinimum = 1_000_000m, CarriedExtra = 3m` and a paid bet → `NextCarriedExtra` is `0m`.
- [ ] **Step 2:** run → compile FAIL.
- [ ] **Step 3: implement.**
```csharp
public static class HolderMinimumRules
{
    private static readonly decimal[] Steps = [1m, 2m, 5m];

    public static decimal LadderUp(decimal raw)
    {
        if (raw <= 0m) return 1m;
        var scale = 1m;
        while (scale > raw) scale /= 10m;           // exact decimal: 1, 0.1, 0.01 ...
        while (scale * 10m <= raw) scale *= 10m;    // now scale <= raw < 10*scale
        foreach (var step in Steps)
            if (step * scale >= raw) return step * scale;
        return 10m * scale;
    }

    public static (decimal N, bool UsedFallback) Compute(decimal? priceUsd, decimal? previousN, HolderJackpotSettings s)
    {
        if (priceUsd is not decimal price || price <= 0m)
            return (previousN ?? s.StartingMinimum, true);

        var n = LadderUp(s.TargetUsd / price);
        if (previousN is decimal prev)
            n = Math.Clamp(n, prev / 2m, prev * 2m);   // may land off the ladder (spec S5)
        n = Math.Clamp(n, s.MinimumFloor, s.MinimumCeiling);
        return (n, false);
    }
}

public static class HolderJackpotPotRules
{
    public static decimal? Compute(decimal? houseBalance, decimal exposure, decimal feeReserve, HolderJackpotSettings s)
    {
        if (houseBalance is not decimal balance) return null;                 // unreadable → fail closed
        var headroom = balance - exposure - feeReserve - s.ReserveFloor;
        var pot = Math.Clamp(Floor2Sig(headroom * s.PotPercent / 100m), s.PotMinimum, s.PotMaximum);
        return headroom < pot ? null : pot;
    }

    public static decimal Floor2Sig(decimal value)
    {
        if (value <= 0m) return 0m;
        var scale = 1m;
        while (value >= scale * 100m) scale *= 10m;
        while (value < scale * 10m) scale /= 10m;   // now 10*scale <= value < 100*scale
        return decimal.Floor(value / scale) * scale;
    }
}
```
⚠️ `Math.Clamp` throws if min > max; `Validate()` (Task 2) guarantees floor ≤ ceiling and the ×2/÷2 bounds are ordered because `prev > 0`. `LotteryPotCarry.NextCarriedExtra`: first line `if (ended.IsHolderRound) return 0m;` with a comment "holder pots never carry (card #196 decision 6; stacking is #191)". Normalize trailing zeros if a test compares `0.470m` to `0.47m` — Shouldly compares decimals by value, so `0.470m == 0.47m` passes.
- [ ] **Step 4:** run the three classes → PASS.

---

### Task 4: The reservation — reserved pots are house exposure

**Files:**
- Modify: `src/SpaceStroker.Domain/Games/Services/HouseEdgeCoverage.cs` (`Evaluate` L61-84)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs` (+ `IHouseEdgeCoverageService.cs`)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Games/HouseEdgeCoverageTests.cs`, `src/SpaceStroker.Tests/UnitTests/Services/HouseEdgeCoverageServiceTests.cs`

**Interfaces — Produces:**
- `HouseEdgeCoverage.Evaluate(decimal? treasuryBalance, IEnumerable<HouseEdgeRoundBook> unsettledRounds, HouseEdgeNewBet newBet, IReadOnlyCollection<decimal>? reservedPots = null)`
- `HouseEdgeCoverageService.ReservedPotRounds(IQueryable<Game> games) : IQueryable<Game>` (public static, for tests)
- `IHouseEdgeCoverageService.EvaluateHeadroomAsync(CancellationToken) : Task<HouseEdgeHeadroom>`; `public sealed record HouseEdgeHeadroom(decimal? Balance, decimal Exposure, decimal FeeReserve);` (put the record next to `HouseEdgeCoverageDecision` in the Domain file)

- [ ] **Step 1: failing tests.** `HouseEdgeCoverageTests`:
```csharp
[Fact]
public void ReservedPotsAreExposure_PlusOneFeeReserveEach()
{
    var d = HouseEdgeCoverage.Evaluate(100m, [], new HouseEdgeNewBet(null, "aaaaaa", 0m, 0m), reservedPots: [0.5m, 0.25m]);
    d.Exposure.ShouldBe(0.75m);
    d.FeeReserve.ShouldBe(HouseEdgeRules.TreasuryFeeReservePerTransfer * 3);   // own transfer + 2 pots
}

[Fact]
public void AHyperRollBetThatFitsWithoutThePot_IsRefusedWithIt()
{
    var bet = new HouseEdgeNewBet(null, "abc", 1m, 10m);                        // needs 10 SOL + fee
    HouseEdgeCoverage.Evaluate(10.5m, [], bet).IsCoverable.ShouldBeTrue();
    HouseEdgeCoverage.Evaluate(10.5m, [], bet, reservedPots: [1m]).IsCoverable.ShouldBeFalse();
}
```
`HouseEdgeCoverageServiceTests` (in-memory context, reuse the file's fixture/mocks): `ReservedPotRounds` returns a `WaitingBets` game with `HouseFundedPot = 0.3m` of a non-house-edge type; excludes it when the status is `NotEnoughPlayersToStart`, `RewardForWinnersCompleted`, `GameEndedAbruptely`, `ErrorWhenProcessingRewards`; excludes a paused holder round (`HouseFundedPot = null`). `EvaluateAsync` with a mocked `GetSolBalanceForRequest` = 10.5 and a reserved-pot round of 1 SOL refuses a 1 SOL bet at multiplier 10 (`new HouseEdgeNewBet(null, "abc", 1m, 10m)`). `EvaluateHeadroomAsync` returns `(10.5, 1, 0.002)` for that state (one own transfer + one pot).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: implement.** In `Evaluate`, after the rounds loop:
```csharp
// Space Jackpot holder rounds: a house-funded pot frozen and reserved at round open (specs/space-jackpot-refactor.md §6.3).
// It is owed in full to whoever wins, so it is exposure all day, plus its own payout transfer.
if (reservedPots != null)
    foreach (var pot in reservedPots)
    {
        exposure += pot;
        transfers += 1;
    }
```
In the service, factor the existing body of `EvaluateAsync` into a private `LoadAsync(Guid? newBetGameId, ct)` returning `(List<HouseEdgeRoundBook> books, List<decimal> reservedPots)`; reserved pots query:
```csharp
public static IQueryable<Game> ReservedPotRounds(IQueryable<Game> games)
{
    var unsettled = HouseEdgeCoverage.UnsettledStatuses;
    return games.Where(x => x.HouseFundedPot != null && unsettled.Contains(x.Status));
}
// in LoadAsync:
var reservedPots = await ReservedPotRounds(_context.Games.AsNoTracking()).Select(x => x.HouseFundedPot!.Value).ToListAsync(ct);
```
Keep "exposure first, balance second" ordering. `EvaluateAsync` passes `reservedPots` to `Evaluate` and adds `reserved {Reserved}` to its Warning log. `EvaluateHeadroomAsync`:
```csharp
public async Task<HouseEdgeHeadroom> EvaluateHeadroomAsync(CancellationToken ct)
{
    var (books, reservedPots) = await LoadAsync(null, ct);
    var balance = await _solana.GetSolBalanceForRequest(_settings.Operator.Token.TokenOwnerPublicKey);
    // A zero "bet" prices nothing; its one transfer stands for the new pot's own payout transfer.
    var d = HouseEdgeCoverage.Evaluate(balance, books, new HouseEdgeNewBet(null, string.Empty, 0m, 0m), reservedPots);
    return new HouseEdgeHeadroom(balance, d.Exposure, d.FeeReserve);
}
```
Update the `ExposureRounds` doc comment to mention reserved pots are loaded separately. No change to `CheckBetAvailabilityHandler` (it calls `EvaluateAsync`). Note: `RewardGameConsumer` does not use this service.
- [ ] **Step 4:** run both classes + the full unit suite (16 existing `Evaluate` calls compile via the default) → PASS.

---

### Task 5: Token-2022 balance reads and the Jupiter price client

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Solana/ISolanaService.cs`, `SolanaService.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Solana/IHolderBalanceReader.cs`, `HolderBalanceReader.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Pricing/IStrokePriceClient.cs`, `JupiterPriceClient.cs`
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (reader, both hosts), `src/SpaceStroker.Consumers/Program.cs` after L47 (price client + named HttpClient)
- Modify: `src/SpaceStroker.Tests.Integration/Harness/TestDoubles.cs` (implement the two new ISolanaService methods on `FakeSolanaService`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/HolderBalanceReaderTests.cs`, `JupiterPriceClientTests.cs`, `SolanaTokenAccountLayoutTests.cs` (new)

**Interfaces — Produces:**
```csharp
// ISolanaService
/// <summary>UI balance of a Token-2022 (or given-program) ATA. Account not found → 0. RPC failure → null.</summary>
Task<decimal?> GetToken2022Balance(string owner, HolderTokenSettings token);
/// <summary>Batched getMultipleAccounts (chunks of 100). Missing account → 0; failed chunk → null for its wallets.</summary>
Task<IReadOnlyDictionary<string, decimal?>> GetToken2022Balances(IReadOnlyCollection<string> owners, HolderTokenSettings token);

public static class SplTokenAccountLayout   // SpaceStroker.Infrastructure.Services.Solana
{
    public const int AmountOffset = 64;
    public static ulong? ReadAmount(byte[]? data) => data is { Length: >= AmountOffset + 8 } ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(AmountOffset, 8)) : null;
    public static decimal ToUi(ulong raw, int decimals) { var scale = 1m; for (var i = 0; i < decimals; i++) scale *= 10m; return raw / scale; }
}

public interface IHolderBalanceReader
{
    Task<decimal?> GetBalanceAsync(string wallet);
    Task<IReadOnlyDictionary<string, decimal?>> GetBalancesAsync(IReadOnlyCollection<string> wallets);
}

public interface IStrokePriceClient { Task<decimal?> GetUsdPriceAsync(string mint, CancellationToken ct = default); }
public static class JupiterPriceOptions { public const string HttpClientName = "JupiterPrice"; }
```

- [ ] **Step 1: failing tests.**
  - `SolanaTokenAccountLayoutTests`: a 165-byte array with `BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(64), 2_345_678_500_000)` → `ReadAmount` = that value, `ToUi(..., 6)` = `2_345_678.5m`; a 200-byte (Token-2022 with extensions) array reads the same; a 40-byte array → null.
  - `HolderBalanceReaderTests` (Moq `ISolanaService`, `IHostEnvironment`): (a) configured, Development, no dev balance → delegates to `GetToken2022Balance(wallet, settings.Operator.Token.HolderToken)` and returns its value; (b) `DevAssumedBalance = 5_000_000` in Development → returns 5,000,000 for single and batch without calling Solana; (c) the same in Production → ignores it and calls Solana; (d) `!HolderToken.IsConfigured` → `null` / every wallet null, Solana never called; (e) invalid base58 wallet (`"not-a-wallet"`) → null and Solana never called (validate with a try/catch around `new PublicKey(wallet)`, pattern `WalletSignature.IsValid`).
  - `JupiterPriceClientTests` (stub `HttpMessageHandler` + `IHttpClientFactory` mock returning `new HttpClient(stub)`): body `{"Dts45...pump":{"usdPrice":0.00001234,"blockId":1}}` → `0.00001234m`; 500 → null; `{}` → null; malformed JSON → null; handler that throws `TaskCanceledException` → null. Assert the request URI is `https://lite-api.jup.ag/price/v3?ids=<mint>`.
  - Token-2022 derivation: add to the existing `Token2022ProgramTests` one assertion that `SolanaService`'s derivation for `GetToken2022Balance` uses `Token2022Program.DeriveAssociatedTokenAccount(owner, mint, program)` — test it indirectly: in `HolderBalanceReaderTests` nothing to assert (the reader delegates). Instead put a comment in `SolanaService.GetToken2022Balance`: "⚠️ Token2022Program derivation — the classic AssociatedTokenAccountProgram reads a different address and silently returns 0 (jackpot-stroke-reward.md §2)".
- [ ] **Step 2:** run → compile FAIL.
- [ ] **Step 3: implement.**
  - `SolanaService.GetToken2022Balance`: the body of the deleted `GetJackpotRewardBalance` (Task 2), with `owner` instead of `signer.TokenOwnerPublicKey`, `token.MintAddress/TokenProgramId/Decimals`. Catch → null.
  - `SolanaService.GetToken2022Balances`: derive every ATA (skip & set null for owners whose derivation throws), chunk by 100, `ExecuteRpcAsync("getMultipleAccounts", _getMultipleAccountsPolicy, r => r is null || !r.WasSuccessful, c => c.GetMultipleAccountsAsync(chunkAtas, Commitment.Confirmed))`. Add `_getMultipleAccountsPolicy = BuildPolicy<ResponseValue<List<AccountInfo>>>("getMultipleAccounts", _settings.Polly.GetTokenAccountsRetryCount, r => r is null || !r.WasSuccessful, applyRateLimitMultiplier: false)` in the ctor next to `_getTokenBalancePolicy` (read `BuildPolicy`'s exact signature first). For each returned `AccountInfo`: null → `0m`; else `Convert.FromBase64String(info.Data[0])` → `SplTokenAccountLayout.ReadAmount` → null if unparsable, else `ToUi(raw, token.Decimals)`. A failed chunk → null for each of its owners + `Log.Error`.
  - `HolderBalanceReader(ISolanaService solana, GeneralSettings settings, IHostEnvironment env)`:
```csharp
private decimal? DevBalance => !_env.IsProduction() ? _settings.Games.HolderJackpot.DevAssumedBalance : null;
public async Task<decimal?> GetBalanceAsync(string wallet)
{
    var token = _settings.Operator.Token.HolderToken;
    if (!token.IsConfigured || !IsWallet(wallet)) return null;
    if (DevBalance is decimal dev) return dev;
    return await _solana.GetToken2022Balance(wallet, token);
}
public async Task<IReadOnlyDictionary<string, decimal?>> GetBalancesAsync(IReadOnlyCollection<string> wallets)
{
    var token = _settings.Operator.Token.HolderToken;
    var distinct = wallets.Distinct(StringComparer.Ordinal).ToList();
    if (!token.IsConfigured) return distinct.ToDictionary(w => w, _ => (decimal?)null, StringComparer.Ordinal);
    if (DevBalance is decimal dev) return distinct.ToDictionary(w => w, _ => (decimal?)dev, StringComparer.Ordinal);
    var valid = distinct.Where(IsWallet).ToList();
    var read = valid.Count == 0 ? new Dictionary<string, decimal?>() : await _solana.GetToken2022Balances(valid, token);
    return distinct.ToDictionary(w => w, w => read.TryGetValue(w, out var v) ? v : null, StringComparer.Ordinal);
}
```
  - `JupiterPriceClient(IHttpClientFactory factory, GeneralSettings settings)`: `GET {PriceApiUrl}?ids={Uri.EscapeDataString(mint)}` with `CancellationTokenSource(TimeSpan.FromSeconds(PriceTimeoutSeconds))` linked to `ct`; parse with `JsonDocument`; `root.TryGetProperty(mint, out var entry) && entry.TryGetProperty("usdPrice", out var p) && p.TryGetDecimal(out var price)` → price if `> 0`; anything else (incl. exceptions) → null + `Log.Warning`. Never throws.
  - DI: `AddDefaultServices`: `services.AddScoped<IHolderBalanceReader, HolderBalanceReader>();`. Consumers `Program.cs` (after `AddDefaultServices`): `builder.Services.AddHttpClient(JupiterPriceOptions.HttpClientName); builder.Services.AddScoped<IStrokePriceClient, JupiterPriceClient>();` (match that file's builder variable name).
  - `FakeSolanaService`: add `public ConcurrentDictionary<string, decimal?> HolderBalances { get; } = new(StringComparer.Ordinal); public decimal? DefaultHolderBalance { get; set; } = 0m; public bool HolderBalanceReadFails { get; set; } public int HolderBatchReads;` and implement both methods (fail → null for all; otherwise the dictionary value, else `DefaultHolderBalance`; batch increments `HolderBatchReads`). Reset all in `Clear()`. Add `public void SetHolderBalance(string wallet, decimal? balance) => HolderBalances[wallet] = balance;`.
- [ ] **Step 4:** run the new classes + full unit suite + solution build → PASS.

---

### Task 6: Opening a holder round — the planner

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Games/IHolderJackpotRoundPlanner.cs`, `HolderJackpotRoundPlanner.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` (ctor L41, `CreateGame` L894-970)
- Modify: `src/SpaceStroker.Consumers/Program.cs` (register the planner, Consumers only)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/HolderJackpotRoundPlannerTests.cs` (new), `src/SpaceStroker.Tests/UnitTests/Background/CreateGameTests.cs` (add)

**Interfaces:**
- Consumes: `HolderMinimumRules`, `HolderJackpotPotRules` (Task 3); `IHouseEdgeCoverageService.AcquireLockAsync` / `EvaluateHeadroomAsync` (Task 4); `IStrokePriceClient` (Task 5); `HolderJackpotSettings.Validate` (Task 2).
- Produces:
```csharp
public sealed record HolderRoundPlan(decimal HolderMinimum, decimal? HouseFundedPot, bool Paused, IHouseEdgeCoverageLock? Lock);
public interface IHolderJackpotRoundPlanner
{
    /// <summary>Never throws. When Lock is non-null the caller MUST insert the game inside it, then CommitAsync, then DisposeAsync.</summary>
    Task<HolderRoundPlan> PlanAsync(GameParameters parameters, CancellationToken ct);
}
// GameService ctor gains a LAST optional parameter: IHolderJackpotRoundPlanner? holderPlanner = null
```

- [ ] **Step 1: failing tests** (`HolderJackpotRoundPlannerTests`, in-memory context + Moq; seed a holder `GameParameters` with `LotteryParameters { HolderOnly = true }`):
  1. Price 0.00001, no previous round, headroom (100, 0, 0.001) → `HolderMinimum 1_000_000`, pot `0.47`, not paused, lock returned.
  2. Previous holder round of the type with `HolderMinimum 2_000_000` (newest by `CreatedDate`, include a non-holder older round that must be ignored), price null → N `2_000_000`, and an `Error` log containing `N kept at` (capture Serilog: reuse the unit project's log-capture helper if one exists — Grep `InMemorySink\|TestCorrelator\|LogEventSink`; else assert via a `Mock<ILogger>` is not possible with static `Log` — in that case add a `Serilog.Core.Logger` with a list sink in the test and set `Log.Logger`, restoring in `Dispose`).
  3. Headroom balance 5.01 → pot null, `Paused` true, `Error` log contains `opened PAUSED`.
  4. `HolderToken.MintAddress = ""` → `Paused` true (pot may still be computed; paused regardless).
  5. `AcquireLockAsync` throws twice → `Paused` true, pot null, `Lock` null, no throw; `AcquireLockAsync` called exactly 2 times.
  6. `Validate()` fails (PotPercent = 1) → `Paused` true, `HolderMinimum = StartingMinimum`, no lock taken.
  `CreateGameTests` additions (via `GameServiceTestFactory`, add an overload taking a planner): a holder type with a fake planner returning `(2_000_000, 0.3, false, lockMock)` → the saved game has `HolderMinimum 2_000_000`, `HouseFundedPot 0.3`, `HolderEntriesPaused false`, `CarriedExtra 0` even when called with `carriedExtra: 5m`; `lockMock.CommitAsync` and `DisposeAsync` called once. Holder type with **no** planner → game saved with `HolderMinimum = settings.Games.HolderJackpot.StartingMinimum`, `HolderEntriesPaused = true`, `HouseFundedPot = null`. A non-holder lottery → planner never called and fields null.
- [ ] **Step 2:** run → compile FAIL.
- [ ] **Step 3: implement.**
```csharp
public class HolderJackpotRoundPlanner : IHolderJackpotRoundPlanner
{
    // ctor: (SpaceStrokerDbContext context, IHouseEdgeCoverageService coverage, IStrokePriceClient prices, GeneralSettings settings)
    public async Task<HolderRoundPlan> PlanAsync(GameParameters parameters, CancellationToken ct)
    {
        var s = _settings.Games.HolderJackpot;
        var token = _settings.Operator.Token.HolderToken;

        if (s.Validate() is string invalid)
        {
            Log.Error("Holder jackpot settings invalid ({Reason}); round for {TypeId} opens PAUSED.", invalid, parameters.Id);
            return new HolderRoundPlan(s.StartingMinimum, null, true, null);
        }

        var previousN = await _context.Games.AsNoTracking()
            .Where(x => x.ParametersId == parameters.Id && x.HolderMinimum != null)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => x.HolderMinimum).FirstOrDefaultAsync(ct);

        var price = token.IsConfigured ? await _prices.GetUsdPriceAsync(token.MintAddress.Trim(), ct) : null;
        var (n, usedFallback) = HolderMinimumRules.Compute(price, previousN, s);
        if (usedFallback)
            Log.Error("Holder jackpot price fetch failed; N kept at {N} for type {TypeId}.", n, parameters.Id);

        IHouseEdgeCoverageLock? held = null;
        for (var attempt = 1; attempt <= 2 && held is null; attempt++)
        {
            try { held = await _coverage.AcquireLockAsync(ct); }
            catch (Exception ex) when (attempt == 1) { Log.Warning(ex, "Holder jackpot: treasury lock busy, retrying once."); }
            catch (Exception ex) { Log.Error(ex, "Holder jackpot: treasury lock unavailable; round for {TypeId} opens PAUSED.", parameters.Id); return new HolderRoundPlan(n, null, true, null); }
        }

        try
        {
            var h = await _coverage.EvaluateHeadroomAsync(ct);
            var pot = HolderJackpotPotRules.Compute(h.Balance, h.Exposure, h.FeeReserve, s);
            var paused = pot is null || !token.IsConfigured;
            if (paused)
                Log.Error("Holder jackpot round for type {TypeId} opened PAUSED: balance {Balance}, exposure {Exposure}, fee reserve {Fee}, reserve floor {Floor}, pot floor {PotMin}, mint configured {Configured}.",
                    parameters.Id, h.Balance, h.Exposure, h.FeeReserve, s.ReserveFloor, s.PotMinimum, token.IsConfigured);
            return new HolderRoundPlan(n, paused ? null : pot, paused, held);
        }
        catch (Exception ex)
        {
            await held!.DisposeAsync();
            Log.Error(ex, "Holder jackpot: headroom read failed; round for {TypeId} opens PAUSED.", parameters.Id);
            return new HolderRoundPlan(n, null, true, null);
        }
    }
}
```
  ⚠️ A paused round carries `HouseFundedPot = null` so it reserves nothing (spec §4.2). The log text must contain `opened PAUSED` (tests + integration grep it).
  `GameService.CreateGame`: after the lottery `StartDate` block and before `AddAsync`:
```csharp
HolderRoundPlan? holderPlan = null;
if (parameters.LotteryParameters?.HolderOnly == true)
{
    holderPlan = _holderPlanner != null
        ? await _holderPlanner.PlanAsync(parameters, CancellationToken.None)
        : NoPlannerPlan(parameters);   // API host: PAUSED at StartingMinimum + Log.Error("...no holder planner in this host...")
    game.HolderMinimum = holderPlan.HolderMinimum;
    game.HouseFundedPot = holderPlan.HouseFundedPot;
    game.HolderEntriesPaused = holderPlan.Paused;
    game.CarriedExtra = 0m;   // holder pots never carry (decision 6)
}
```
  then wrap `AddAsync` + `SaveChangesAsync` so that, when `holderPlan?.Lock` is set: `await lock.CommitAsync(CancellationToken.None)` after the save, and `await lock.DisposeAsync()` in a `finally` (dispose without commit rolls back if the save threw — the existing outer `catch` logs it). Everything after the save is unchanged. Register in Consumers: `AddScoped<IHolderJackpotRoundPlanner, HolderJackpotRoundPlanner>()`.
  ⚠️ `AcquireLockAsync` opens a transaction on the **scoped** DbContext that `GameService` also uses — they must be the same scope (they are: both scoped, resolved together). Do not create a new scope inside the planner.
- [ ] **Step 4:** run both classes + full unit suite → PASS.

---

### Task 7: Entry — the holder branch of `POST /games/{roundId}/bets`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs`, `RegisterBetRequestBody.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/JackpotEntryMessages.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/BetRoundRules.cs`, `GameService.cs` (`InsertBetWithoutNotification` L157-203); Create `HolderTicketAlreadyExistsException.cs` next to `ChoiceAlreadyTakenException.cs`
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs` (hand-maintained: append three properties in the existing Portuguese-comment format)
- Modify: `src/SpaceStroker.Infrastructure/Services/HouseSeed/HouseSeedBetService.cs` (after L121)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (+ ctor update L93-96), `src/SpaceStroker.Tests/UnitTests/Handlers/DuelHarness.cs:269-272` (ctor update), `.../Handlers/GetWalletNonceHandlerTests.cs`, `.../Services/HouseSeedBetServiceTests.cs`

**Interfaces:**
- Consumes: `IHolderBalanceReader` (Task 5), `Game.HolderMinimum/HolderEntriesPaused` (Task 1), `WalletProof.TryConsume(ICacheService, string purpose, string wallet, string? nonce, string? signature, Func<string,string,string> buildMessage)`.
- Produces:
  - `RegisterBetRequestBody.WalletProofNonce : string?` `[MaxLength(64)]`, `WalletProofSignature : string?` `[MaxLength(128)]`
  - `WalletNoncePurpose.JackpotEntry = "jackpot-entry"` (in `IsKnown`)
  - `JackpotEntryMessages.Build(string wallet, Guid gameId, string choice, string nonce) : string`
  - `BetRoundRules.HolderTicketRefusal(SpaceStrokerDbContext context, Guid gameId, Guid playerId, CancellationToken ct = default) : Task<string?>`
  - `ErrorMessages.JackpotEntryInvalid`, `HolderBalanceUnavailable`, `HolderBelowMinimum` (texts in Global Constraints; `HolderBelowMinimum` has `{0}` = N formatted `N0` invariant, `{1}` = symbol)
  - `RegisterBetHandler` ctor gains `ICacheService cache, IHolderBalanceReader holderBalances` (append at the end)

- [ ] **Step 1: failing tests** in `RegisterBetHandlerTests` (read the file's harness first; add a holder round builder: type with `LotteryParameters { HolderOnly = true, Frequency = 1 day }`, `RequiredAmount 0`, `AllowFreeBet false`, `ChoiceRequiredLength 6`; game `WaitingBets`, `HolderMinimum 1_000_000`, `HouseFundedPot 0.3`). Sign with a real keypair: `var account = new Solnet.Wallet.Account();` message via `JackpotEntryMessages.Build(account.PublicKey.Key, game.Id, "planet", nonce)`, signature `Convert.ToBase64String(account.Sign(Encoding.UTF8.GetBytes(message)))`, and seed the nonce into the memory cache at `CacheHelper.GenerateWalletNonceCacheSettings(WalletNoncePurpose.JackpotEntry, wallet)`. Balance via `Mock<IHolderBalanceReader>`. Cases:
  1. **Happy path** → a bet with `EntryKind Holder`, `Amount 0`, `Signature null`, `Status Made`; no `TransactionLog` row; `IMessageBus.PublishEventAsync<BetPlacedEvent>` never called; nonce removed from cache.
  2. Bad signature / unknown nonce / replayed nonce (submit the happy request twice) → all three messages equal `ErrorMessages.JackpotEntryInvalid`; `GetBalanceAsync` never called.
  3. Balance 999_999 → message exactly `Hold at least 1,000,000 $STROKE to enter today's draw.`
  4. Balance null → `ErrorMessages.HolderBalanceUnavailable`.
  5. `HolderEntriesPaused = true` → `ErrorMessages.GameNotValidForBets`.
  6. Second entry same wallet (fresh nonce, different word) → `ErrorMessages.OneBetPerPresetRound`; one ticket in the DB.
  7. `IGameService.InsertBet` throws `HolderTicketAlreadyExistsException` → `ErrorMessages.OneBetPerPresetRound` (not `FailedToRegisterBet`).
  8. Self-excluded player → `ErrorMessages.SelfExclusionActive`.
  9. Paid body (`Amount 0.1`, `PaymentSignature "sig"`) on the holder type → refused with `BetValidationResult.InvalidAmount.StringValueOf()` + refund published (`RefundInvalidBetEvent` with Amount 0.1). Dev free body (Amount 0, no proof fields) → `JackpotEntryInvalid`, no refund event.
  10. **`HolderRefusal_NeverAppendsRefundWarning_OrPublishesARefund`**: for cases 2-8 the message never contains `ErrorMessages.RefundWarning`, no `RefundInvalidBetEvent` published, no `PaymentRefundClaims` row.
  11. Signature over word `planet` submitted with choice `meteor` → `JackpotEntryInvalid`.
  12. Word shorter than 6 → the existing length message (from `ChoiceIsValidForGame`).
  `GetWalletNonceHandlerTests`: `jackpot-entry` → a nonce is issued. `HouseSeedBetServiceTests`: a holder lottery round → no seed bet, Information log.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: implement.**
  - `JackpotEntryMessages.Build` → `string.Join("\n", "Space Stroker jackpot entry", $"Wallet: {wallet}", $"Round: {gameId}", $"Word: {choice}", $"Nonce: {nonce}")`, doc comment "KEEP-IN-SYNC byte for byte with monkify-front `useHolderJackpot.buildJackpotEntryMessage`".
  - `HandleRequest`: right after `var betGame = await _gameService.GetGameById(request.GameId);` insert
```csharp
if (betGame?.Parameters?.LotteryParameters?.HolderOnly == true)
    return await RegisterHolderEntry(request, betGame, ct);
```
  - New private method (keep it in this file, near `RegisterDuelBet`):
```csharp
private async Task<BetDto> RegisterHolderEntry(RegisterBetRequest request, Game game, CancellationToken ct)
{
    var wallet = Body.Wallet?.Trim() ?? string.Empty;
    var amount = Body.Amount!.Value;
    var hasPayment = !string.IsNullOrWhiteSpace(Body.PaymentSignature);

    // 1. Shape. A paid bet (or anything carrying a payment) on a holder-only type is refused — refunded when money moved.
    if (amount != 0 || hasPayment || string.IsNullOrEmpty(wallet) || request.FundedByOrbit || request.OrbitClaimId != null)
    {
        if (amount > 0 && hasPayment && !request.FundedByOrbit)
        {
            _isFreeBet = false;
            var payer = await UpsertPlayerAsync(wallet, Body.DisplayName, null, ct);
            _bet = new Bet(request.GameId, Body.Seed!, payer?.Id, Body.Choice!, amount, Body.Nonce) { Signature = Body.PaymentSignature, Player = payer!, Game = game };
            await RefundInvalidBet(BetValidationResult.InvalidAmount.StringValueOf());
        }
        Messaging.ReturnValidationFailureMessage(amount == 0 && !hasPayment ? ErrorMessages.JackpotEntryInvalid : BetValidationResult.InvalidAmount.StringValueOf());
    }

    // 2. Round open for entries.
    if (game.HolderEntriesPaused || game.HolderMinimum is not decimal minimum || !Game.GameAcceptingBets.Contains(game.Status) || game.Parameters.ComingSoon)
        Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);

    // 3. Wallet proof BEFORE any RPC (vague on every failure — constraints §6).
    var choice = Body.Choice ?? string.Empty;
    if (!WalletProof.TryConsume(_cache, WalletNoncePurpose.JackpotEntry, wallet, Body.WalletProofNonce, Body.WalletProofSignature,
            (w, n) => JackpotEntryMessages.Build(w, game.Id, choice, n)))
        Messaging.ReturnValidationFailureMessage(ErrorMessages.JackpotEntryInvalid);

    // 4-5. Player, self-exclusion, Orbit auto-play (same gates and messages as paid bets).
    var player = await UpsertPlayerAsync(wallet, Body.DisplayName, Body.ReferralCode, ct);
    if (player is null) Messaging.ReturnValidationFailureMessage(ErrorMessages.JackpotEntryInvalid);
    if (player!.IsSelfExcluded(DateTime.UtcNow)) Messaging.ReturnValidationFailureMessage(ErrorMessages.SelfExclusionActive);
    if (await OrbitAutoPlay.IsActive(_spacestrokerDbContext, player.Id, game.ParametersId, ct))
        Messaging.ReturnValidationFailureMessage(ErrorMessages.OrbitAutoPlayActive);

    // 6-7. Balance vs the round's frozen N (the one specific message — public bound, constraints §6 exception).
    var balance = await _holderBalances.GetBalanceAsync(wallet);
    if (balance is null) Messaging.ReturnValidationFailureMessage(ErrorMessages.HolderBalanceUnavailable);
    if (balance < minimum)
        Messaging.ReturnValidationFailureMessage(string.Format(CultureInfo.InvariantCulture, ErrorMessages.HolderBelowMinimum,
            minimum.ToString("N0", CultureInfo.InvariantCulture), _settings.Operator.Token.HolderToken.Symbol));

    // 8-10. Build the ticket and run the shared choice rules; nothing was paid, so no refund path can fire (Amount 0).
    _isFreeBet = false;
    _isHolderEntry = true;
    _bet = new Bet(request.GameId, Body.Seed!, player.Id, choice, 0m, Body.Nonce)
    {
        EntryKind = BetEntryKind.Holder, Player = player, Game = game,
        ReferralCode = null,   // holder entries never earn referral commission (no house cut)
    };
    await ValidateBetParameters(request, game);
    _bet.ExclusiveChoice = game.Parameters.PresetChoices.IsNullOrEmpty() ? _bet.Choice : null;
    try { await RegisterBet(); }
    catch (HolderTicketAlreadyExistsException) { Messaging.ReturnValidationFailureMessage(ErrorMessages.OneBetPerPresetRound); }
    return new BetDto(_bet);   // no ledger row, no BetPlacedEvent (lottery), live notification already sent by InsertBet
}
```
  Adapt names to the file (e.g. how `Player`/`Game` navigation are set on `_bet` at L145-173, `IsNullOrEmpty` helper, the exact `UpsertPlayerAsync` return type). `ReturnValidationFailureMessage` throws, so control never continues past it.
  - `ValidateBetParameters`: per-round block (L366) becomes
```csharp
if (_isHolderEntry)
{
    var holderRefusal = await BetRoundRules.HolderTicketRefusal(_spacestrokerDbContext, request.GameId, _bet.PlayerId!.Value);
    if (holderRefusal != null) await RefundInvalidBet(holderRefusal);   // Amount 0 → plain message, no refund
}
else if (!_isFreeBet && _bet.PlayerId != null && betGame?.Parameters?.IsHouseEdge != true) { ...existing... }
```
    and `ChoiceIsValidForGame(_bet, betGame, _isFreeBet || _isHolderEntry)`. The free-bet type gate (L408) stays `_isFreeBet && ...` (false for holders).
  - `BetRoundRules.HolderTicketRefusal`: `await context.GameBets.AnyAsync(x => x.GameId == gameId && x.PlayerId == playerId, ct) ? ErrorMessages.OneBetPerPresetRound : null`.
  - `GameService.InsertBetWithoutNotification`: add before the generic catch
```csharp
catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg
    && pg.ConstraintName == "IX_GameBets_GameId_PlayerId_Holder")
{
    Log.Information("Holder ticket for player {PlayerId} in game {GameId} already exists (concurrent entry).", bet.PlayerId, bet.GameId);
    throw new HolderTicketAlreadyExistsException();
}
```
    and in `RegisterBet()` let `HolderTicketAlreadyExistsException` propagate (catch only `ChoiceAlreadyTakenException` as today).
  - `WalletNoncePurpose`: add the constant and to `IsKnown`.
  - `HouseSeedBetService.SeedRound` after L121: `if (game.Parameters.LotteryParameters?.HolderOnly == true) { Log.Information("House seed skipped for holder jackpot round {GameId}.", gameId); return; }`.
  - Update both handler constructions in tests (`RegisterBetHandlerTests.cs:93-96`, `DuelHarness.cs:269-272`) with a `MemoryCacheService` (or the mock the file already uses for `ICacheService`) and a `Mock<IHolderBalanceReader>().Object`.
- [ ] **Step 4:** run `RegisterBetHandlerTests`, `GetWalletNonceHandlerTests`, `HouseSeedBetServiceTests`, then the full unit suite → PASS.

---

### Task 8: Eligibility endpoint, DTO fields, rate limit

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Handlers/Games/GetHolderEligibility/GetHolderEligibilityRequest.cs`, `GetHolderEligibilityHandler.cs`; `src/SpaceStroker.Infrastructure/Contracts/Games/HolderEligibilityDto.cs`
- Modify: `src/SpaceStroker.Api/Controllers/GamesController.cs` (next to `GetActiveGameForParameter`, L134-138)
- Modify: `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs`
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs`, `BetDto.cs`, `GameParametersDto.cs` (`LotteryParametersDto` L231-238), `GameVerificationDto.cs` (`GameVerificationBetDto` L86), `src/SpaceStroker.Infrastructure/Handlers/Games/GetGameVerification/GetGameVerificationHandler.cs:69-73`
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (`IpRateLimiting.GeneralRules`)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetHolderEligibilityHandlerTests.cs` (new), `.../Contracts/` DTO tests (add to the existing active-game/bet DTO test file — Grep `new ActiveGameDto(`), the rate-limit settings test (Grep `RateLimitSettingsTests`)

**Interfaces — Produces:**
```csharp
public record GetHolderEligibilityRequest(Guid GameTypeId, string? Wallet) : IRequest<HolderEligibilityDto>;
public class HolderEligibilityDto { public string Wallet {get;set;} = ""; public string Symbol {get;set;} = ""; public decimal Minimum {get;set;}
    public decimal? Balance {get;set;} public bool Eligible {get;set;} public bool EntriesPaused {get;set;} }
// CacheHelper: public static CacheSettings GenerateHolderEligibilityCacheSettings(Guid gameId, string wallet) => new($"HolderEligibility:{gameId}:{wallet}", TimeSpan.FromSeconds(15));
// ActiveGameDto: bool HolderOnly, decimal? HolderMinimum, decimal? HouseFundedPot, bool EntriesPaused
// BetDto: bool Voided   (Status == BetStatus.Voided)
// LotteryParametersDto: bool HolderOnly
// GameVerificationBetDto: bool Voided
```
JSON (camelCase over the wire): `holderOnly`, `holderMinimum`, `houseFundedPot`, `entriesPaused`, `voided`.

- [ ] **Step 1: failing tests.** Handler (in-memory context, Moq reader, `MemoryCacheService`): eligible (balance 2,345,678.5 vs 1M → `eligible true`, `minimum 1000000`, `symbol "STROKE"`); below (`eligible false`); unreadable (null balance → `balance null`, `eligible false`, **not cached**: a second call reads again); non-holder type / unknown type / no live round → `ErrorMessages.ParameterNotFound`-style generic message (reuse whatever `GetActiveGameForParameterHandler` returns for "not found"; assert equal to it); bad base58 wallet → validation failure message (reuse `ErrorMessages`' existing invalid-wallet key if one exists — Grep `InvalidWallet`; else `JackpotEntryInvalid`); cached for 15 s (reader called once for two calls). DTO: an `ActiveGameDto` of a holder round maps the four fields; a voided `BetDto` has `Voided true`; `LotteryParametersDto.HolderOnly` maps. Verification handler: a voided bet appears with `voided: true`, and `orderedBetSeeds` still includes its seed. Rate limit test: the new rule exists, `get:/games/*/holder-eligibility`, and still no rule matches `/bets`.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: implement.** Handler ctor `(SpaceStrokerDbContext context, IMessaging messaging, IHolderBalanceReader reader, ICacheService cache, GeneralSettings settings)`. Load the live round: `Context.Games.AsNoTracking().Include(x => x.Parameters).ThenInclude(p => p.LotteryParameters).Where(x => x.ParametersId == id && Game.ActiveGamesStatus.Contains(x.Status) && x.Parameters.Active).OrderByDescending(x => x.CreatedDate).FirstOrDefaultAsync()`; not holder → generic not-found. Cache only when `Balance != null`. Controller:
```csharp
[HttpGet("{gameTypeId:guid}/holder-eligibility")]
public async Task<IActionResult> GetHolderEligibility(Guid gameTypeId, [FromQuery] string? wallet = null)
    => await ProcessRequest(new GetHolderEligibilityRequest(gameTypeId, wallet));
```
  Rate-limit rule (copy the shape of a neighbouring GET rule): `{ "Endpoint": "get:/games/*/holder-eligibility", "Period": "1m", "Limit": 30 }`. DTOs per Interfaces. Verification: `Voided = bet.Status == BetStatus.Voided` in the bet projection; typer-derived `orderedChoices` already excludes voided once Task 9 lands.
- [ ] **Step 4:** run the new/changed classes + full unit suite → PASS.

---

### Task 9: The draw — recheck, voids, typer, winners, pot, reward

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Consumers/BetPlaced/BetPlacedConsumer.cs` (between L50 and L100)
- Create: `src/SpaceStroker.Infrastructure/Services/Games/HolderDrawRecheck.cs`
- Modify: `src/SpaceStroker.Domain/Games/Services/SpaceStrokerTyper.cs` (`SetBets` L267 loop only; **not** `InitializeKeystream`)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RunGame/RunGameConsumer.cs:71`, `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs:321-327`
- Modify: `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs` (L55, `SetPotAmount` L215-234)
- Modify: `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` (`treasuryFunded` L109-110)
- Test: `src/SpaceStroker.Tests/UnitTests/Background/BetPlacedHandlerTests.cs`, `.../Domain/Games/SpaceStrokerTyperTests.cs`, `.../Domain/Games/BetDomainServiceTests.cs`, `.../Background/RewardWinnersHandlerTests.cs`, `.../Services/HolderDrawRecheckTests.cs` (new)

**Interfaces:**
- Consumes: `IHolderBalanceReader.GetBalancesAsync` (Task 5), `IGameService.UpdateBetStatus(Bet, BetStatus)`.
- Produces: `public class HolderDrawRecheck(IHolderBalanceReader reader, IGameService gameService, GeneralSettings settings)` with `Task<IReadOnlyList<Bet>> RunAsync(Game game, IReadOnlyList<Bet> bets, CancellationToken ct)` returning the **non-voided** bets; registered scoped in `AddDefaultServices`. `public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(20);` and an overridable backoff (`public Func<int, TimeSpan> Backoff { get; init; } = attempt => TimeSpan.FromMilliseconds(500 * attempt);`) so tests use zero.

- [ ] **Step 1: failing tests.**
  - `HolderDrawRecheckTests`: tickets A (2M), B (0.5M), C (unreadable on every try) with N = 1M → returns A and C; B `UpdateBetStatus(B, Voided)` once; an `Error` log contains C's wallet and the game id; reader called `1 + DrawRecheckAttempts` (=4) times for the batch containing C only when **every** result is null (re-read only the still-unread wallets on retries). Already-voided tickets (status Voided) are not re-read and not revived; non-holder bets are returned untouched. `DrawRecheckAttempts = 0` → one try.
  - `BetPlacedHandlerTests`: holder round, 2 tickets, one voided by the recheck → `NotEnoughPlayersToStart`, `CreateGame(parameters, 0m)` called (carry 0 via `LotteryPotCarry`), **no** `RefundInvalidBetEvent` published. Holder round, 3 tickets, one voided → proceeds to `TryStartGame`. Non-holder lottery → recheck never invoked.
  - `SpaceStrokerTyperTests`: a lottery game with bets `aaaaaa` (Made) and `bbbbbb` (Voided) → `typer.Bets.Keys` = `[aaaaaa]`; the keystream prefix equals the one computed with both seeds (compare against `ProvablyFair.BuildPrefix(game.Id, [seedA, seedB])` in bet order).
  - `BetDomainServiceTests`: holder game (`HolderMinimum` set, `HouseFundedPot 0.3`, `LotteryPot 4` on the type, `CarriedExtra 2`) with two `NeedsRewarding` holder winners → `PotAmount == 0.3m`, each `CalculateRewardForBet` = 0.15 net (commission 0 even at 2%), `IsFreeBet` false. One holder winner → 0.3.
  - `RewardWinnersHandlerTests`: holder winner paid via `settlement.Credit` (verify called with 0.3); a second run on the same bet (seed a prior Credit row of 0.3) → `BetHasAlreadyBeenRewarded` → status `Rewarded`, no second Credit; `treasuryFunded` path: `GetSolBalance` read once (holder round counts as treasury funded) and a treasury balance below the pot → `NeedsManualAnalysis`.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: implement.**
  - `HolderDrawRecheck.RunAsync`: if `!game.IsHolderRound` return `bets`. Candidates = `bets.Where(b => BetExtensions.IsHolderEntry(b) && b.Status == BetStatus.Made && b.GetWalletAddress() != null)`. Loop `attempt = 0..DrawRecheckAttempts` while unread remain and `stopwatch < TotalBudget`: `GetBalancesAsync(unreadWallets)`; record non-null answers; `await Task.Delay(Backoff(attempt+1))` between tries. Then for each candidate: known and `< game.HolderMinimum` → `await _gameService.UpdateBetStatus(bet, BetStatus.Voided)` + `Log.Information("Holder ticket {BetId} voided: balance {Balance} < {Minimum}", ...)`; unknown → keep + `Log.Error("Holder draw recheck: balance unreadable for wallet {Wallet} in game {GameId}; ticket kept (fail-open).", ...)`. Return `bets.Where(b => b.Status != BetStatus.Voided).ToList()` (use the updated in-memory status — set `bet.Status = BetStatus.Voided` if `UpdateBetStatus` does not mutate the instance; check).
  - `BetPlacedConsumer`: after `var gameBets = await gameService.GetBetsByGameId(game.Id);` add `if (game.IsHolderRound) gameBets = await scope.ServiceProvider.GetRequiredService<HolderDrawRecheck>().RunAsync(game, gameBets.ToList(), CancellationToken.None);` (adapt the variable type — if `gameBets` is `IEnumerable<Bet>`, materialize). Everything below uses the filtered list, so the count and the under-subscribed branch see only valid tickets. ⚠️ Only when the round is holder AND accepting bets (the idempotency gate above already returned otherwise).
  - `SpaceStrokerTyper.SetBets`: `foreach (var bet in game.Bets.Where(b => b.Status != BetStatus.Voided))` + comment "voided holder tickets cannot win; their seeds stay in the keystream prefix (InitializeKeystream) — spec §6.6".
  - `RunGameConsumer` L71 and `GameService` L321-327: add `&& x.Status != BetStatus.Voided` to the choice match.
  - `BetDomainService`: L55 `_houseFundedBase = game.HouseFundedPot ?? ((game.Parameters?.LotteryParameters?.LotteryPot ?? 0m) + game.CarriedExtra);`. `SetPotAmount`:
```csharp
PotAmount = game.Bets.Sum(x => x.Amount);   // holder tickets are 0
if (game.HouseFundedPot is decimal holderPot)
    PotAmount += holderPot;                  // frozen, house-funded (space-jackpot-refactor §6.7); ignores LotteryPot/CarriedExtra
else
{
    if (game.Parameters?.LotteryParameters != null) PotAmount += game.Parameters.LotteryParameters.LotteryPot;
    PotAmount += game.CarriedExtra;
}
if (game.HasDrawnHouseWord && game.Stake is decimal houseStake) PotAmount += houseStake;
```
  - `RewardGameConsumer` L110: `var treasuryFunded = isHouseEdge || (game.Parameters?.IsDuel == true && game.DuelKind == DuelKind.House) || game.IsHolderRound;` (comment: the holder pot is paid from the treasury). `paidWinners` already excludes only `IsFreeBet`, so holder winners are included.
- [ ] **Step 4:** run the five classes + full unit suite → PASS.

---

### Task 10: Stats and history

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GameHistoryService.cs` (projections L109-118, L159-168; `LoadBetsAsync` L192-196; `BuildRow` L201-223; records L269-288)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/GlobalStatsService.cs` (L75-80, L161-174), `GameStatsService.cs` (L58-85), `src/SpaceStroker.Infrastructure/Services/Players/PlayerStatsService.cs` (L47-50, L123-132)
- Test: `GameHistoryServiceTests`, `GlobalStatsServiceTests`, `GameStatsServiceTests`, `PlayerStatsServiceTests`, `LeaderboardServiceTests`, `LeaderboardHallOfFameTests` (all under `src/SpaceStroker.Tests/UnitTests/Services/`)

- [ ] **Step 1: failing tests.** Seed a completed holder round (`HouseFundedPot 0.3`) with two holder tickets, one winner with a `Credit` row of 0.3, next to a completed paid race with a 1.0 credit.
  - Global & per-type stats: `TotalPaidOut` 1.0, `WinnersPaid` 1, `BiggestSinglePayout` 1.0; `BiggestPot` ignores the holder round.
  - Player stats for the holder winner: `TotalWon` 0, `Net` 0, `BiggestSingleWin` 0 (participation counts unchanged — correction C17; assert `BetsPlaced` still 1 to pin the decision).
  - History: the holder round row has `Pot == 0.3`, `BetCount == 2`, `WinnerCount == 1`.
  - Leaderboards (race, PNL, hall of fame): the holder win contributes nothing (pins existing `Amount > 0` filters — no code change expected).
- [ ] **Step 2:** run → the stats/history tests FAIL, the leaderboard pins PASS.
- [ ] **Step 3: implement.**
  - Global/Game stats: define `var creditedBets = bets.Where(bet => bet.EntryKind != BetEntryKind.Holder);` and join `credits` on it instead of `bets`. `BiggestPot`: holder rounds have only `Amount 0` bets so the paid-bet grouping never sees them; add a comment saying so (no code change) — but the `+ lotteryBaseByType` add-on must not apply to holder types: `lotteryBaseByType` built from `LotteryParameters.LotteryPot` is 0 for Space Jackpot after the reconciler, so also no change; pin in the test.
  - PlayerStats: project `b.EntryKind` in L47-50; `betIds` for credits = `betsInCompleted.Where(b => b.EntryKind != BetEntryKind.Holder)`.
  - History: add `decimal? HouseFundedPot` to `PageGameRow` (both projections) and `BetEntryKind EntryKind` to `BetRow`; `BuildRow`:
```csharp
var paidOfRound = betsOfRound.Where(b => b.Amount > 0m).ToList();
var pot = game.HouseFundedPot ?? (paidOfRound.Sum(b => b.Amount)
    + (parameters.LotteryParameters != null ? parameters.LotteryParameters.LotteryPot + game.CarriedExtra : 0m));
var payable = betsOfRound.Where(b => b.Amount > 0m || b.EntryKind == BetEntryKind.Holder);
... WinnerCount = payable.Count(b => string.Equals(b.Choice, game.WinningChoice, StringComparison.OrdinalIgnoreCase)),
```
    ⚠️ Keep the original expression for non-holder rows byte-equivalent (paid sum + lottery base + carry).
- [ ] **Step 4:** run the six classes + full unit suite → PASS.

---

### Task 11: Startup self-heals — reconciler and the deploy transition

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Games/HolderLotteryReconciler.cs`, `HolderJackpotTransition.cs`
- Modify: `src/SpaceStroker.Consumers/Program.cs` (`AllowFreeBet` patch L463-466; call the reconciler after `DuelWordPoolReconciler` L579-588 and before the cache drop L614; `ApplicationStarted` L276-285 — transition right after `CreateDefaultGameParametersAsync`; `HolderJackpotSettings.Validate()` Error log at startup)
- Modify: `src/SpaceStroker.Api/Program.cs` (startup `Validate()` Error log only)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/HolderJackpotTransitionTests.cs` (new). `HolderLotteryReconciler` uses `ExecuteUpdate` (no-op on the in-memory provider) → covered in the integration suite (Task 12), like `ComingSoonReconciler`.

**Interfaces — Produces:**
```csharp
public static class HolderLotteryReconciler
{   /// <returns>number of rows patched</returns>
    public static int Reconcile(SpaceStrokerDbContext context, IReadOnlyList<DefaultGameSettings> defaults); }
public static class HolderJackpotTransition
{   public const string RefundReason = "Space Jackpot is now free for token holders.";
    /// <returns>number of legacy rounds closed</returns>
    public static Task<int> RunAsync(SpaceStrokerDbContext context, IMessageBus bus, CancellationToken ct = default); }
```

- [ ] **Step 1: failing tests** (`HolderJackpotTransitionTests`, in-memory; `Mock<IMessageBus>`): a holder-only type with a legacy `WaitingBets` round (`HolderMinimum null`, `CarriedExtra 1.5`) holding two paid `Made` bets (0.1 each, one `FundedByOrbit`) and one dev free bet → both paid bets `NeedsRefunding`, two `RefundInvalidBetEvent`s with `Value 0.1`, reason `RefundReason`, the game id, the bet signature and the `FundedByOrbit` flag; free bet untouched; round `GameEndedAbruptely`; a `Warning` log mentions `dropped carry 1.5`; returns 1. Second run → returns 0, no new events. A settled legacy round (`RewardForWinnersCompleted`) and a live holder round (`HolderMinimum` set) → untouched. A non-holder lottery type's live round → untouched.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: implement.**
  - `HolderLotteryReconciler` (pattern `ComingSoonReconciler`, `ExecuteUpdate` with change predicates): for each `config.HolderOnly` default: `context.LotteryParameters.Where(l => l.Parameters.Name == config.Name && (!l.HolderOnly || l.LotteryPot != 0m)).ExecuteUpdate(s => s.SetProperty(l => l.HolderOnly, true).SetProperty(l => l.LotteryPot, 0m))` and `context.GameParameters.Where(p => p.Name == config.Name && (p.RequiredAmount != 0m || p.AllowFreeBet)).ExecuteUpdate(s => s.SetProperty(p => p.RequiredAmount, 0m).SetProperty(p => p.AllowFreeBet, false))`. (Check the DbSet name for `LotteryParameters` in the context.)
  - `HolderJackpotTransition.RunAsync`: tracked query (the context is NoTracking by default → use `.AsTracking()`) of games `Parameters.LotteryParameters.HolderOnly && HolderMinimum == null && (Status == WaitingBets || Status == GameStarting)` including `Bets.Player`. For each paid `Made` bet (`Amount > 0`): `Status = NeedsRefunding` + a `BetStatusLog` (mirror `GameService.UpdateBetStatus` — read it), publish `new RefundInvalidBetEvent(bet.Id, bet.GetWalletAddress() ?? "", bet.Amount, RefundReason, game.Id, bet.Signature, bet.FundedByOrbit)`. Set the game `GameEndedAbruptely` (+ status log, `UpdatedDate`), `if (game.CarriedExtra > 0) Log.Warning("Holder jackpot transition: dropped carry {Carry} SOL from round {GameId}.", ...)`. `SaveChangesAsync` **before** publishing (so a crash between the two leaves `NeedsRefunding` for `PendingRefundSweepJob`, never a published refund with no status). Log Information per round.
  - `Program.cs`:
    - `AllowFreeBet` patch: exclude holder defaults — `var freeBetNames = defaults.Where(d => !d.HolderOnly).Select(d => d.Name).ToList();` and use it in that `Where`.
    - After `DuelWordPoolReconciler`: `try { var n = HolderLotteryReconciler.Reconcile(context, defaults); if (n > 0) Log.Information(...); } catch (Exception ex) { Log.Error(ex, "Holder lottery reconcile failed."); }`.
    - New local function `RunHolderJackpotTransition(app)` (try/catch + `Log.Error("... never take the host down")`, own scope, resolves `SpaceStrokerDbContext` + `IMessageBus`, `.GetAwaiter().GetResult()`), called in `ApplicationStarted` immediately after `CreateDefaultGameParametersAsync(app);`.
    - `OpenNewGames` needs no change: the legacy round is `GameEndedAbruptely`, so the type has no live round and `CreateGame` runs the planner; `LotteryPotCarry` returns the old carry but `CreateGame` forces 0 for holder types (Task 6).
    - Startup: `if (settings.Games.HolderJackpot.Validate() is string bad) Log.Error("GeneralSettings:Games:HolderJackpot invalid: {Reason}. Holder rounds will open PAUSED.", bad);` in both hosts.
- [ ] **Step 4:** run `HolderJackpotTransitionTests` + full unit suite → PASS.

---

### Task 12: Integration tests (Testcontainers)

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` (env vars, `TestWebAppFactory.ConfigureTestServices`, helpers)
- Create: `src/SpaceStroker.Tests.Integration/Harness/FakeStrokePriceClient.cs`
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationGameParameters.cs`
- Create: `src/SpaceStroker.Tests.Integration/HolderJackpotFlowTests.cs`
- Modify: `DefaultGameSeedingTests.cs`, and any of `GlobalStatsFlowTests`, `GamesHistoryFlowTests`, `LeaderboardsFlowTests` that assert Space Jackpot figures (run them to find out)

**Interfaces:**
- Consumes: everything above.
- Produces (fixture): `FakeStrokePriceClient StrokePrice` (singleton shared by both hosts; `SetPrice(decimal?)`, default `0.00001m`); `Task PublishBetPlacedAsync(Guid gameId)` (same shape as `PublishRunGameAsync`); `Task<Game> CreateHolderRoundAsync(string typeName)` (resolves `IGameService` from `CreateConsumersScope()`, loads parameters with `Include(LotteryParameters).Include(PresetChoices).AsNoTracking()`, closes any live round of that type to `NotEnoughPlayersToStart` first, calls `CreateGame`, returns the new `WaitingBets` round); `Task SetStartDateAsync(Guid gameId, DateTime startDate)`; `Task<BetDto-or-response> EnterHolderAsync(Guid gameId, Solnet.Wallet.Account wallet, string word)` (mints a `jackpot-entry` nonce via `MintWalletNonceAsync(wallet.PublicKey.Key, "jackpot-entry")`, signs `JackpotEntryMessages.Build`, posts `RegisterBetRequestBody { Amount = 0, Choice = word, Wallet = pubkey, PaymentSignature = "", Seed = Guid.NewGuid().ToString("N"), Nonce = ..., WalletProofNonce, WalletProofSignature }` via `PlaceBetRawAsync`, returns the raw response so tests can read messages with `ReadMessagesAsync`).

- [ ] **Step 1: harness.**
  - `IntegrationGameParameters`: `public const string HolderLottery = "6L Holder Jackpot";` and a builder: `Base(HolderLottery, <letters charset>, 6)` with `RequiredAmount = 0`, `AllowFreeBet = false`, `MinimumNumberOfPlayers = 2`, `LotteryParameters = new LotteryParameters { LotteryPot = 0, Frequency = TimeSpan.FromHours(1), HolderOnly = true }`; add to `All()`.
  - Fixture env: `GeneralSettings__Games__HolderJackpot__DevAssumedBalance` must stay unset (Development would honour it). Register fakes in `TestWebAppFactory.ConfigureTestServices` for **both** hosts: `services.RemoveAll<IStrokePriceClient>(); services.AddSingleton<IStrokePriceClient>(StrokePrice);`. The real `HolderBalanceReader` runs over `FakeSolanaService` (its `HolderBalances` / `HolderBalanceReadFails`), so set balances on **both** `SolanaService` (API) and `ConsumersSolanaService` via a helper `SetHolderBalance(string wallet, decimal? balance)` that writes both.
  - Tests that close a round must restore treasury (`SetTreasuryBalance(DefaultSolBalance)`) in `finally`.
- [ ] **Step 2: write `HolderJackpotFlowTests`** (`[Collection(IntegrationCollection.Name)]`, ctor clears `LogSink`, both Solana fakes, sets price 0.00001 and treasury 100). Draw steering: after entries, compute `prefix` from **all** ticket seeds in `(CreatedDate, Id ordinal)` order, `choiceCount` = distinct non-voided words, pick the target word's index in ordinal order, `FindSeedForFallbackIndex` (copy from the deleted `JackpotRewardFlowTests` — it is in git history: `git show HEAD:src/SpaceStroker.Tests.Integration/JackpotRewardFlowTests.cs`), `SetServerSeedAsync`, `SetMaxTyperCharacters(1)` in try/finally, `SetStartDateAsync(gameId, UtcNow + 2s)`, then `PublishBetPlacedAsync(gameId)` and `WaitForStatusAsync(..., RewardForWinnersCompleted, 120s)`. Tests:
  1. `TwoHoldersEnter_TheDrawPaysTheFrozenPot` — round N = 1,000,000, pot = `HolderJackpotPotRules.Compute(100, exposureAtOpen, feeAtOpen, defaults)` (assert `0.47` when no other exposure; if other suites leave exposure, assert pot equals the stored `HouseFundedPot` and > 0); two wallets at 2M enter `planet`/`meteor`; winner gets exactly one `Credit` log equal to the pot; no commission (credit == pot); loser `Made`; republish `PublishRewardGameAsync(gameId)` → still one Credit; `LogSink.GetErrorMessages()` empty. Also: `GetHolderEligibility` over HTTP for a winner wallet returns `eligible: true` before the draw.
  2. `PassTheBag_VoidsTheTicket_AndTheRoundReverifies` — three holders; before the draw `SetHolderBalance(wallet3, 10)`; steer so wallet3's word would be first among **all three** words — since the voided word is excluded from `orderedChoices`, steer to the index of a valid word and additionally assert wallet3's bet is `Voided`, not `NeedsRewarding`, has no Credit; `GetGameVerificationAsync(gameId)` has the voided bet flagged, `orderedBetSeeds.Length == 3`, `orderedChoices` excludes wallet3's word, and re-running `ProvablyFair` offline over the payload reproduces `winningChoice` (reuse the offline re-verify helper from `GameFlowTests` if present — Grep `BuildPrefix(` in the integration project).
  3. `UnderSubscribedAfterVoids_NoDraw_NoCarry_ReservationReleased` — two enter, one set below N → round `NotEnoughPlayersToStart`, no Credit, successor (`GetSuccessorGameAsync`) has `CarriedExtra 0`, `HolderMinimum` set, `HouseFundedPot` > 0; `EvaluateHouseEdgeCoverageAsync(new HouseEdgeNewBet(null, "x", 0m, 0m))` exposure excludes the old pot (equals the successor's pot plus other exposure).
  4. `DrawRecheckFailsOpen` — two enter; set `HolderBalanceReadFails = true` on the Consumers fake → draw completes, both tickets kept, an Error message contains each wallet.
  5. `EntryRefusals` — below N → message equals `Hold at least 1,000,000 $STROKE to enter today's draw.`; replayed nonce (resubmit the exact accepted body) → `We could not verify that request. Please try again.`; concurrent double entry: two different words, two nonces… **same wallet** fired with `Task.WhenAll` → exactly one ticket row for the wallet, and the other response carries `OneBetPerPresetRound` (not `FailedToRegisterBet`). Note: nonces are per `(purpose, wallet)` — minting a second nonce replaces the first; for the concurrent case mint once, sign two messages over the **same** nonce with different words; one request consumes the nonce, the other gets the vague message **or** the one-ticket message — assert exactly one row and that neither response is `FailedToRegisterBet`.
  6. `ReservedPot_BlocksAHyperRollStakeUntilTheRoundSettles` — treasury set so that a Hyper Roll bet at the max multiplier fits without the pot and not with it (compute from `EvaluateHouseEdgeCoverageAsync`); place the Hyper Roll bet (reuse the Hyper Roll bet helper from `HouseEdge*FlowTests`) → refused + refunded (`HouseEdgeUnderMaintenance`); settle/close the holder round (`NotEnoughPlayersToStart` via the draw with 0 tickets); same bet → accepted.
  7. `DeployTransition_RefundsLegacyPaidTickets_Idempotent` — insert a legacy `WaitingBets` round of the holder type directly (`new Game(parametersId)`, no `HolderMinimum`) with two paid bets (`PaymentSignature` valid per the fake, via `SeedWaitingBetsGameAsync`-style insertion); run `HolderJackpotTransition.RunAsync` in a Consumers scope; wait for 2 Refund ledger rows (`CountRefundLogsAsync`); round `GameEndedAbruptely`; `CreateHolderRoundAsync` → live holder round; run the transition again → no new Refund rows (wait 3 s, recount).
  8. `PausedRound_RefusesEntries_AndLogsAnError` — treasury 1 SOL → `CreateHolderRoundAsync` → `HolderEntriesPaused true`, `HouseFundedPot null`; entry → `GameNotValidForBets` text; an Error log contains `opened PAUSED`.
  `DefaultGameSeedingTests`: Space Jackpot `HolderOnly true`, `RequiredAmount 0`, `AllowFreeBet false` (the "all three AllowFreeBet true" assertion now excludes it); add `HolderLotteryReconciler_FlipsAnAlreadySeededPaidJackpot` (direct-reconciler pattern L144-183: set the row back to paid via SQL, run `Reconcile`, assert flipped; run again → 0 patched).
- [ ] **Step 3:** run `--filter "FullyQualifiedName~HolderJackpotFlowTests|FullyQualifiedName~DefaultGameSeedingTests"` → iterate to PASS (fix production code only where a test exposes a real bug; report each such fix).
- [ ] **Step 4:** run the full integration suite (`scripts/run_integration_tests.bat`) and the full unit suite → both green. Record the list of added/changed/deleted test classes and methods for the report.

---

## Final verification (before hand-off)

- [ ] `dotnet build` of the solution: 0 errors, no new warnings in touched files.
- [ ] `scripts/run_tests.bat` green; `scripts/run_integration_tests.bat` green.
- [ ] `git -C <worktree> grep -n "JackpotReward\|AmountPerWinner\|TransferJackpotReward"` → no hits outside comments that explain the removal.
- [ ] Local stack smoke (spec §14 Manual QA 1-6) — done together with the front plan.
