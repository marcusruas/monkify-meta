# Streamer Wallets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registered streamer wallets bet a play-money balance (3.7 SOL seeded once) on Hyper Roll and Monkey Duel VS HOUSE through the Orbit wager endpoint, with no on-chain movement, excluded from money aggregates.

**Architecture:** A new play ledger (`StreamerWallet` + `StreamerTransaction`) owned by `IStreamerLedger`; config → DB via a static startup reconciler; `RegisterBetHandler` gets a `StreamerPlay` branch that skips payment/coverage/TransactionLog and debits the ledger inside the bet-insert transaction; settlement/refund consumers route `EntryKind.Streamer` bets to the ledger; read paths filter `EntryKind != Streamer` for money figures. Front: `isStreamer` on Orbit state forces the Orbit source with no wallet fallback and hides deposit/cash-out/auto-bet/OPEN DUELS.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, MassTransit, xUnit + Shouldly + Moq (unit), Testcontainers (integration); Vue 3 + TS.

**Authoritative spec:** `monkify-meta/specs/streamer-wallets.md` (read it; section refs below are §N). Worktrees:
`E:\Monkify\.worktrees\streamer-wallets\monkify-backend` (code under `src\`), `...\monkify-front` (code under `src\`), `...\monkify-meta`.

## Global Constraints

- Work only in the worktrees above; never touch the hubs `E:\Monkify\monkify-*`. Never read any `backup/` folder.
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; no `IEntityTypeConfiguration`. The context is **NoTracking by default**: write with `Add`+`SaveChanges` or `ExecuteUpdateAsync`, never load-mutate-save.
- Migration via `dotnet ef migrations add AddStreamerWallets --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Consumers`; never hand-edit the snapshot.
- Unit tests: `scripts\run_tests.bat` (never full `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker).
- Committed config: mock/empty only. `InitialBalance` = `3.7`.
- `BetEntryKind.Streamer = 4`. Ledger types `Seed=1, TopUp=2, BetDebit=3, WinCredit=4, Refund=5`.
- No new error messages; reuse `GameNotValidForBets`, `HouseEdgeUnderMaintenance`, `OrbitInsufficientBalance` ("Not enough Orbit balance for that bet."), `OrbitRequestInvalid`, `JackpotEntryInvalid`, `SelfExclusionActive`, `FailedToRegisterBet`.
- Never `sed -i` across many .cs files (CRLF flip). Files are CRLF; keep them CRLF.
- Commit after each task in the backend worktree: `git -C <wt> add -A && git -C <wt> commit -m "<msg> (#201)."` with trailer `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Do not push (the controller pushes).

---

### Task 1: Domain, settings, EF mapping, migration

**Files:**
- Modify: `src/SpaceStroker.Domain/Bets/ValueObjects/BetEntryKind.cs`, `src/SpaceStroker.Domain/Bets/Services/BetExtensions.cs`
- Create: `src/SpaceStroker.Domain/Streamers/Entities/StreamerWallet.cs`, `StreamerTransaction.cs`, `src/SpaceStroker.Domain/Streamers/ValueObjects/StreamerTransactionType.cs`
- Create: `src/SpaceStroker.Domain/Configs/Entities/StreamerSettings.cs`; Modify `OperatorSettings.cs` (add `public StreamerSettings Streamers { get; set; } = new();`)
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (DbSets near the Orbit ones ~l.33; mapping after the OrbitTransaction block ~l.541)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` → under `GeneralSettings.Operator` add `"Streamers": { "InitialBalance": 3.7, "Wallets": [], "TopUps": [] }`
- Create migration `AddStreamerWallets`
- Test: `src/SpaceStroker.Tests/UnitTests/Api/StreamerSettingsBindingTests.cs` (model on `OrbitSettingsBindingTests.cs`)

**Produces:** `BetEntryKind.Streamer`, `BetExtensions.IsStreamerPlay(Bet)`, entities `StreamerWallet { Guid Id; string Wallet; bool Active; decimal Balance; CreatedDate; UpdatedDate }`, `StreamerTransaction { Guid Id; Guid StreamerWalletId; StreamerTransactionType Type; decimal Amount; string? TopUpId; Guid? PlacementRequestId; Guid? BetId }`, `StreamerSettings { decimal InitialBalance = 3.7m; List<StreamerWalletSettings> Wallets; List<StreamerTopUpSettings> TopUps }`, `StreamerWalletSettings { string Wallet; bool Active = true }`, `StreamerTopUpSettings { string Id; string Wallet; decimal Amount }`, DbSets `StreamerWallets`, `StreamerTransactions`.

- [ ] **Step 1:** Write `StreamerSettingsBindingTests` — binds the committed `spacestrokersettings.json` (copy the loading approach from `OrbitSettingsBindingTests`) and asserts `settings.Operator.Streamers.InitialBalance == 3.7m`, `Wallets` empty, `TopUps` empty. Run `scripts\run_tests.bat` → compile failure (types missing).
- [ ] **Step 2:** Add the enum value and helper:

```csharp
// BetEntryKind.cs — append
/// <summary>Play-money stake from a registered streamer wallet. Never on-chain, never in money aggregates.</summary>
Streamer = 4
```
```csharp
// BetExtensions.cs
public static bool IsStreamerPlay(Bet bet) => bet.EntryKind == BetEntryKind.Streamer;
```

- [ ] **Step 3:** Create the entities (extend `TableEntity` like `OrbitBalance` does — check its base class and copy the namespace style), the enum, and the settings classes exactly as in spec §4.2 / §5.
- [ ] **Step 4:** Add DbSets and the mapping block from spec §4.3 verbatim (table names `StreamerWallets`, `StreamerTransactions`; index names `IX_StreamerTransactions_Seed`, `IX_StreamerTransactions_TopUpId`, `IX_StreamerTransactions_PlacementRequestId_BetDebit`, `IX_StreamerTransactions_BetId_Type`).
- [ ] **Step 5:** Add the settings JSON block. Run `dotnet ef migrations add AddStreamerWallets --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Consumers` from the backend worktree root. Inspect the generated migration: only two `CreateTable`s + indexes + FK.
- [ ] **Step 6:** `scripts\run_tests.bat` → green. Commit "Streamer wallets: domain, settings, EF mapping and migration".

---

### Task 2: `IStreamerLedger`

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Streamers/IStreamerLedger.cs`, `StreamerLedger.cs`; register scoped in `src/SpaceStroker.Infrastructure/DependencyInjection.cs` next to the Orbit ledger registration.
- Test: `src/SpaceStroker.Tests/UnitTests/Services/StreamerLedgerTests.cs`

**Consumes:** Task 1 types. **Produces:**

```csharp
public interface IStreamerLedger
{
    Task<StreamerWallet?> FindActive(string? wallet, CancellationToken ct = default);
    Task<bool> IsActiveStreamer(string? wallet, CancellationToken ct = default);
    /// Must be called INSIDE the caller's open DB transaction. Conditional debit; false when the balance is short.
    Task<bool> TryDebit(Guid streamerWalletId, decimal amount, Guid? placementRequestId, Guid betId, CancellationToken ct = default);
    /// Own transaction; idempotent per bet (unique BetId+Type). Returns true when credited now or already credited.
    Task<bool> CreditWin(Bet bet, decimal amount, CancellationToken ct = default);
    Task<bool> Refund(Bet bet, CancellationToken ct = default);
    Task<Dictionary<Guid, decimal>> WonFor(IReadOnlyCollection<Guid> betIds, CancellationToken ct = default);
}
```

Behaviour:
- `FindActive`: `context.StreamerWallets.AsNoTracking().FirstOrDefaultAsync(x => x.Wallet == wallet && x.Active)`; null/blank wallet → null.
- `TryDebit`: `var rows = await context.StreamerWallets.Where(x => x.Id == id && x.Balance >= amount).ExecuteUpdateAsync(s => s.SetProperty(x => x.Balance, x => x.Balance - amount).SetProperty(x => x.UpdatedDate, DateTime.UtcNow))`; `rows == 0` → return false; else `context.StreamerTransactions.Add(new { Type = BetDebit, Amount = -amount, PlacementRequestId, BetId = betId })` + `SaveChangesAsync` → true. (The bet id is known before insert because `Bet.Id` is generated client-side — verify in `TableEntity`; if not, generate it before insert in Task 4.)
- `CreditWin` / `Refund`: resolve the streamer wallet by the bet's player wallet (`bet.Player?.ExternalPlayerId` — load `Player` if null via `context.Players.AsNoTracking()`), **regardless of `Active`** (a deactivated streamer's open round still settles, spec §3 10c). Open `BeginTransactionAsync`; if a row with `(BetId, Type)` exists → commit, return true; insert row (`Amount = +amount`), `ExecuteUpdate Balance += amount`, commit. Catch `DbUpdateException` whose inner `PostgresException.SqlState == "23505"` → return true. No wallet found → `Log.Error`, return false.
- `WonFor`: `WinCredit` rows grouped by BetId → sum.
- The EF InMemory provider does not run `ExecuteUpdate` or transactions faithfully; unit tests cover only `FindActive`, `IsActiveStreamer`, `WonFor`, and the "no wallet found" branch. The conditional debit, idempotency and invariant are proven in Task 9 (integration).

- [ ] **Step 1:** Write `StreamerLedgerTests` (in-memory context helper used by other service tests): `FindActive_ReturnsActiveOnly`, `FindActive_BlankWallet_ReturnsNull`, `WonFor_SumsWinCreditsPerBet`, `CreditWin_UnknownWallet_ReturnsFalse`. Run → fail.
- [ ] **Step 2:** Implement + register. Run `scripts\run_tests.bat` → green. Commit "Streamer wallets: play ledger service".

---

### Task 3: `StreamerWalletReconciler` + Consumers startup

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Streamers/StreamerWalletReconciler.cs` (static, modelled on `Services/Games/DuelWordPoolReconciler.cs`)
- Modify: `src/SpaceStroker.Consumers/Program.cs` — new local `ReconcileStreamerWallets(scope)` called right after `CreateDefaultReferralPartners` (~l.295), wrapped `try { var n = StreamerWalletReconciler.Reconcile(context, settings.Operator.Streamers); if (n > 0) Log.Information("Reconciled {Count} streamer wallet change(s) from configuration.", n); } catch (Exception ex) { Log.Error(ex, "Could not reconcile the streamer wallets from configuration at startup."); }`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/StreamerWalletReconcilerTests.cs`

**Produces:** `public static int Reconcile(SpaceStrokerDbContext context, StreamerSettings settings)` (sync like `DuelWordPoolReconciler`).

Logic (spec §6.1): normalise (trim; skip blank, `MOCK_` prefix, wallet length not 32–44 or non-base58 chars `[1-9A-HJ-NP-Za-km-z]` → `Log.Error("STREAMERS — skipping invalid wallet entry {Wallet}")`; dedupe ordinal). Activate/deactivate existing via `ExecuteUpdate` (`Active = configuredActiveSet.Contains(Wallet)`). Insert missing (`Balance = 0`, `Active = entry.Active`). Seed every wallet without a `Seed` row: transaction { insert `Seed` row Amount = `InitialBalance`; `ExecuteUpdate Balance += InitialBalance` }; `23505` → skip. Top-ups: for each entry with non-blank `Id`, known wallet, `Amount > 0`, no row with that `TopUpId` → transaction { insert `TopUp` row; `Balance +=` }; invalid → `Log.Error`. Warn when a newly inserted wallet's player has an `OrbitBalances` row with `Amount > 0`. Return number of inserts + flips + seeds + top-ups.

Unit tests (InMemory: inserts work, `ExecuteUpdate`/transactions don't — so the InMemory unit tests assert only inserts/skips; activation flips, seeding balance and idempotency are asserted in Task 9): `Reconcile_InsertsNewWallet_AndWritesOneSeedRow`, `Reconcile_SkipsMockAndInvalidWallets`, `Reconcile_SecondRun_WritesNoSecondSeedRow`, `Reconcile_TopUpForUnknownWallet_IsSkipped`. If `ExecuteUpdate` throws on InMemory, guard the tests by asserting rows only (check how `DuelWordPoolReconcilerTests` copes and mirror it).

- [ ] **Step 1:** Tests → fail. **Step 2:** implement + Program.cs call. **Step 3:** `run_tests.bat` green. Commit "Streamer wallets: config reconciler at Consumers startup".

---

### Task 4: `RegisterBetHandler` streamer branch, streamer-wallet guard, coverage exclusion

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetRequest.cs` — add init-only `public bool StreamerPlay { get; init; }`, `public Guid? StreamerWalletId { get; init; }`, `public Guid? PlacementRequestId { get; init; }` (same doc style as `FundedByOrbit`).
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` (inject `IStreamerLedger`)
- Modify: `src/SpaceStroker.Infrastructure/Services/Games/HouseEdgeCoverageService.cs` (l.108-111 and l.149-156: add `&& b.EntryKind != BetEntryKind.Streamer`)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (extend), `HouseEdgeCoverageServiceTests` (extend)

Apply the table in spec §6.5 exactly. Key points:
- `_isStreamerPlay = request.StreamerPlay`. If `StreamerPlay && (FundedByOrbit || StreamerWalletId == null)` → `Log.Error` + reject `FailedToRegisterBet` (no refund).
- Shape check l.135-139: for `_isStreamerPlay` require `amount > 0` and **no** signature (else reject `FailedToRegisterBet`).
- Bet build: `EntryKind = BetEntryKind.Streamer`, `ReferralCode = null`, no signature.
- Skip `ValidatePayment` (l.204) and the in-lock replay re-checks (l.615, l.880) when `_isStreamerPlay`.
- Streamer-wallet guard (after self-exclusion l.210-216) for `!_isStreamerPlay`: `if (await _streamers.IsActiveStreamer(walletOfRequest))` → `if (!_isFreeBet) await RefundInvalidBet(ErrorMessages.GameNotValidForBets); Messaging.ReturnValidationFailureMessage(ErrorMessages.GameNotValidForBets);` (mirror the auto-play block shape at l.239-246). Same guard in `RegisterHolderEntry` after the proof → `JackpotEntryInvalid`.
- Dispatch: `_isStreamerPlay` and not (`IsHouseEdge` || (`IsDuel` && round `DuelKind == House`)) → reject `GameNotValidForBets` (no refund — nothing moved).
- `RegisterHouseEdgeBet`: treat `_isStreamerPlay` like `_isFreeBet` for the coverage evaluate (l.625) and fail-closed (l.641-648). Maintenance unchanged.
- `RegisterDuelBet`: no treasury lock (l.889-890) and no coverage pricing/fail-closed in `DrawHouseSideUnderLock` (l.987-1000, l.913-922) for `_isStreamerPlay`.
- Debit inside the transaction: in both paths, after the bet row is added and **before** the commit, `if (_isStreamerPlay && !await _streamers.TryDebit(request.StreamerWalletId!.Value, _bet.Amount, request.PlacementRequestId, _bet.Id))` → roll back and reject `OrbitInsufficientBalance` (via the reject-only path). Find where `RegisterBet(deferNotification:true)` inserts and where the lock commits (`_coverageLock` / `_duelLock` commit at l.669 / l.937) — the debit must run on the same `DbContext` within that open transaction.
- Skip `WritePlacementLedgerRow` (l.263-264).
- `RefundInvalidBet` (l.1283): when `_isStreamerPlay` → release locks, reject only, no event (like the `Amount == 0` branch at l.1293).

Unit tests (`RegisterBetHandlerTests`, InMemory — mirror how existing tests mock `ITokenSettlementService`, coverage, locks): `StreamerPlay_HyperRoll_RegistersStreamerBet_WithoutValidatingPayment` (EntryKind Streamer, ReferralCode null, settlement `ValidatePayment` never called, no `TransactionLog` row, `IStreamerLedger.TryDebit` called once with the bet's amount), `StreamerPlay_HyperRoll_DoesNotEvaluateCoverage`, `StreamerPlay_DebitFails_RejectsWithInsufficientBalance_AndPublishesNoRefund`, `StreamerPlay_OnCosmicSprint_IsRejected`, `StreamerWallet_WalletSignedBet_IsRefundedWithGameNotValid` (ledger mock `IsActiveStreamer → true`; asserts `RefundInvalidBetEvent` published), `StreamerWallet_FreeBet_IsRejectedWithoutRefund`. Coverage test: `Exposure_IgnoresStreamerBets`.

- [ ] Steps: tests → fail → implement → `run_tests.bat` green (fix every existing test broken by the new constructor dependency by passing a `Mock<IStreamerLedger>` whose `IsActiveStreamer` returns false) → commit "Streamer wallets: play-money bet registration and streamer-wallet guard".

---

### Task 5: Orbit endpoints

**Files:**
- Modify: `Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` (`OrbitDto.IsStreamer`), `GetOrbitHandler.cs` (§6.3)
- Modify: `Handlers/Orbit/PlaceOrbitWager/PlaceOrbitWagerHandler.cs` (§6.4) — inject `IMediator` (check how other handlers send nested requests) and `IStreamerLedger`
- Modify: `Handlers/Orbit/GetOrbitWager/GetOrbitWagerHandler.cs` (streamer ledger read)
- Modify the deposit / cash-out / settings / Telegram link + verify handlers and `GetOrbitHistory` (§6.10): active streamer → `OrbitRequestInvalid` (history: empty page)
- Tests: extend `GetOrbitTests`/`GetOrbitHandlerTests`, `PlaceOrbitWagerTests`, `GetOrbitWagerTests`, `CreditOrbitDepositTests`, cash-out + settings handler tests

Streamer wager (§6.4) returns `OrbitWagerResultDto.Placed(requestId, betId)` synchronously and stores it via `_results.Set`; a `RegisterBetHandler` refusal comes back as a validation failure — catch the messaging/validation outcome the way the API surfaces it (inspect `Messaging.ReturnValidationFailureMessage` — it throws; catch that exception type, store `Refused(message)`, rethrow so the controller returns 400). Idempotency: stored result first, then `context.StreamerTransactions.AnyAsync(BetDebit && PlacementRequestId == id)` → Placed with its BetId.

Tests: `Streamer_HyperRoll_PlacesSynchronously` (mediator mock returns bet id; asserts `StreamerPlay`, `StreamerWalletId`, `PlacementRequestId` on the sent request; no `PlaceManualOrbitBetEvent` published), `Streamer_CosmicSprint_GameNotValid`, `Streamer_OpenRoomDuel_GameNotValid`, `Streamer_Maintenance_Refused`, `Streamer_InsufficientBalance_Refused`, `Streamer_SameRequestId_ReturnsStoredResult`, `GetOrbit_Streamer_ReturnsPlayBalance_AndNoEligibleTypes`, deposit/cash-out/settings refusals.

- [ ] Steps: tests → fail → implement → green → commit "Streamer wallets: Orbit endpoints serve the play balance".

---

### Task 6: Availability and open-duel quote

**Files:** `Handlers/Games/CheckBetAvailability/*` (body gains `[MaxLength(50)] public string? Wallet { get; set; }`), `Handlers/Games/OpenDuelRoom/*` (body gains the same), tests `CheckBetAvailabilityHandlerTests`, `OpenDuelRoomHandlerTests`. Behaviour per spec §6.6 (house-edge: maintenance → UnderMaintenance, else Open without coverage, board checks unchanged; duel OpenRoom → available false; duel House → maintenance + floor/ceiling, `maxStake = parameters Duel MaximumStake`; OpenDuelRoom House + streamer → skip coverage refusal, OpenRoom + streamer → `GameNotValidForBets`). Tests: one per branch. Commit "Streamer wallets: availability and duel-room quotes ignore treasury coverage".

---

### Task 7: Settlement and refunds

**Files:** `Consumers/RewardGame/RewardGameConsumer.cs` (§6.7), `Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` (§6.8), tests `RewardGameConsumerTests`, `RefundInvalidBetConsumerTests`.

- `paidWinners` (l.86) excludes `BetExtensions.IsStreamerPlay`. Treasury running balance only over `paidWinners`; when `paidWinners` is empty do not read the treasury.
- In the winner loop, before the cutover check: streamer winner → `CalculateRewardForBet`; `await streamerLedger.CreditWin(winner, reward)` → true: status `Rewarded`, house-edge `AddHouseEdgePayout(wallet, reward)`, else `netRewardPerWinner ??= reward`; false → `NeedsManualAnalysis`; `continue`.
- Refund: bet row exists and streamer → `streamerLedger.Refund(bet)` → `MarkRefunded`; false → leave status, `Log.Error`.

Tests: `StreamerHyperRollWinner_IsCreditedToPlayLedger_WithoutTransfer` (settlement `Credit` never called, ledger `CreditWin(stake × mult)`), `StreamerHouseDuelWinner_GetsTwoStakesLessCommission`, `StreamerOnlyRound_DoesNotReadTreasury`, `StreamerBetRefund_GoesToPlayLedger`. Commit "Streamer wallets: settle and refund play bets on the play ledger".

---

### Task 8: Read paths + payout SQL

**Files (backend):** `Services/Leaderboards/LeaderboardService.cs` l.419, l.431; `Services/Games/GlobalStatsService.cs` l.77, l.81, l.172; `Services/Games/GameStatsService.cs` l.62, l.66; `Services/Referrals/ReferralEarningsService.cs` l.158; `Handlers/Referrals/GetReferralPlayers/GetReferralPlayersHandler.cs` l.87-93; `Services/Games/GameHistoryService.cs` own columns l.259-285 (`MyPayout` for Streamer bets from `IStreamerLedger.WonFor`); `Services/Players/PlayerStatsService.cs` l.120-143 (streamer winnings from `WonFor`).
**File (meta):** `monkify-meta/deploy/sql/referral-payout-report.sql` add `AND "EntryKind" <> 4` mirroring `EarningBets`; `monkify-meta/deploy/secrets.example.json` appConfig placeholders + `_comment_streamers` (spec §5).

Tests (model on the existing holder-exclusion tests at `GlobalStatsServiceTests.cs:178`, `GameStatsServiceTests.cs:140`, `LeaderboardServiceTests.cs:382`, `LeaderboardHallOfFameTests.cs:207`, `PlayerStatsServiceTests.cs:167`, `GameHistoryServiceTests.cs:132`): streamer bet with Amount > 0 and a won round → absent from gamble race / PnL / hall of fame / own row; global `TotalVolumeWagered`/`TotalPaidOut`/`BiggestPot` unchanged but `TotalBetsPlaced`/`RoundsPlayed` incremented; per-type biggest pot/hottest choice unchanged; referral turnover unchanged; history `MyPayout` = ledger win; player stats `TotalWon` includes ledger win. Commit backend "Streamer wallets: keep play money out of leaderboards, stats and partner turnover"; commit meta "Streamer wallets: payout report filter and appConfig placeholders".

---

### Task 9: Integration tests — `StreamerWalletsFlowTests`

**File:** `src/SpaceStroker.Tests.Integration/StreamerWalletsFlowTests.cs` (`[Collection(IntegrationCollection.Name)]`). Use `OrbitWalletFlowTests` helpers as the model (`OpenSessionAsync`, `PostWagerAsync`, `WaitForWagerAsync`); drive the reconciler directly via `_fixture.CreateConsumersScope()` + `StreamerWalletReconciler.Reconcile(context, settings)` with a settings object holding the test wallet (do not change the shared fixture env). Clear both fake Solana services in the constructor. Cases from spec §11.2:

1. `HyperRoll_HappyPath_PlayMoneyWinIsCredited_NothingMovesOnChain` — reconcile → balance 3.7, one Seed row; open session; open board (`POST /games/{type}/boards`); force the streamer's word to win the weighted draw (see `HyperRollFlowTests` for how it makes the outcome deterministic); `POST /orbit/wagers` → Placed; wait `RewardForWinnersCompleted`; assert balance `3.7 − S + S × mult`, ledger Σ == balance, `EntryKind == Streamer`, no `Transfers` for the wallet on either fake, no `TransactionLogs` for the bet, `GET /leaderboards` has no row for the wallet, `GET /games/stats` money figures unchanged vs a snapshot taken before (call `_fixture.InvalidateGlobalCaches()` first), round in `GET /games/{type}/history`.
2. `HouseDuel_PlayMoney_SettlesToLedger` — open House duel room (with wallet), wager, wait completion; balance moved by `+S×(1−rate)` or `−S`; zero transfers.
3. `Refusals` — wager on Cosmic Sprint → 400 `GameNotValidForBets`, balance unchanged; `POST /orbit/deposits` → 400; wager above balance → 400 insufficient; wallet-signed Cosmic Sprint bet (`_fixture.PlaceBetAsync` with the streamer wallet) → refunded (a refund transfer recorded).
4. `Reconciler_IsIdempotent_AndDeactivates` — run twice → one Seed; add top-up, run twice → one TopUp and balance +amount; run with an empty list → `Active == false`, balance kept.

Run `scripts\run_integration_tests.bat` (filter to this class first with `dotnet test src/SpaceStroker.Tests.Integration --filter FullyQualifiedName~StreamerWalletsFlowTests`, then the full integration suite). Commit "Streamer wallets: integration tests".

---

### Task 10: Frontend

**Files (all under `monkify-front/src`):** `types/orbit.ts`, `hooks/useOrbit.ts`, `services/mainService.ts` + `types/index.ts` (availability/open-duel bodies gain `wallet?`), `hooks/useGame.ts`, `hooks/useDuel.ts`, `components/game/DuelConsole.vue`, `DuelTabs.vue`, `DuelBoard.vue`, `components/game/GameConsole.vue`, `components/orbit/OrbitQuickBody.vue`, `OrbitModal.vue`, `components/home/HomeOrbitCard.vue`. Behaviour exactly per spec §7 (line refs there). Key snippets:

```ts
// types/orbit.ts — OrbitState
/** True when the connected wallet is a registered streamer: `balance` is play money. */
isStreamer: boolean;
```
```ts
// useOrbit.ts
const isStreamer = computed(() => !!orbit.value?.isStreamer);
// export it
```
```ts
// useGame.ts nextBetSource — before the orbitUsable branch
if (orbitApi.isStreamer.value && orbitApi.sessionWallet.value === wallet) {
  return isStreamerPlayable.value ? 'orbit' : null;
}
// isStreamerPlayable = selectedGame.isHouseEdge || (selectedGame.isDuel && useDuel().tab.value === 'house')
```
In `runBet`: when streamer, skip fallback A (l.1746-1750) and B (l.1871-1902); an `'insufficient'` outcome → `onBetFail(ORBIT_INSUFFICIENT_BALANCE_MESSAGE)` and return; never call `triggerOrbitOutOfFunds`. The coverage watcher (l.823-842) passes null key for a streamer. `useDuel`: `orbitCanPay` true for a streamer on the house tab; `placeCreatorBet` / `takeRoom` never reach the wallet path for a streamer; OPEN DUELS tab hidden / `switchTab('board')` refused; `checkGameAvailability` + `openDuelRoom` send `wallet` when streamer. `GameConsole`: streamer on a non-houseEdge, non-duel game → `inputsLocked` true and `placeBet` refuses with the generic "The requested game was not found or is not receiving bets at the current moment." Orbit surfaces: hide deposit/cash-out/auto-bet/notifications/recover for a streamer (`visibleSections` → `['wallet']`).

Verify: copy `E:\Monkify\monkify-front\yarn.lock` into the front worktree if absent; junction the hub `node_modules` if absent (`cmd /c mklink /J node_modules E:\Monkify\monkify-front\node_modules`); `yarn lint` (ignore CRLF-only prettier false alarms on untouched files) and `yarn build` must pass. Keep edited files LF if they were LF (check `git diff` shows no whole-file churn). Commit in the front worktree "Streamer wallets: play balance in the Orbit pill, no wallet fallback, PVE-only console (#201)."

---

## Self-review notes

- Spec §6.1–§6.11 → Tasks 3, 2, 5, 5, 4, 6, 7, 7, 8, 5, (none needed). §7 → Task 10. §11.2 → Task 9. §5 deploy placeholders → Task 8.
- `TryDebit` signature takes `betId` (Task 2) and is called with `_bet.Id` (Task 4) — consistent.
- Things only provable on Postgres (ExecuteUpdate, unique-index idempotency, transactions) are explicitly deferred to Task 9.
