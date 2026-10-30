# Token integration (#212) — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Profile icons a player unlocks by burning $STROKE from their own wallet (verified on-chain, stored permanently), plus two read endpoints (`$STROKE` balance, icon prices) for the front.

**Architecture:** A new `PlayerIconPurchases` table is the first persisted icon inventory. `PlayerIconRules` takes the wallet's purchased keys as an extra input; a new scoped `IPlayerIconPurchases` loads them. A pure static `SolanaService.DescribeIconBurn(JObject, wallet, token)` decides "is this a burn of the holder mint by this wallet" from the raw `getTransaction` JSON; `VerifyIconBurnAsync` wraps it with the existing RPC path. `PurchasePlayerIconHandler` follows `ReportDonationHandler` (verify without a session, idempotent by signature).

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, Solnet 6.1.0, Newtonsoft JObject, xUnit + Shouldly + Moq, Testcontainers.

**Spec:** `monkify-meta/specs/token-integration.md` (authoritative; §4–§6, §10.1, §11). Worktree `C:\Users\kurok\Desktop\monkify\.worktrees\token-integration\monkify-backend` on `feature/token-integration`. All paths below are relative to its `src\`.

## Global Constraints

- Work ONLY in the worktree; never touch the hub `C:\Users\kurok\Desktop\monkify\monkify-backend`.
- Unit tests: `scripts\run_tests.bat` (never a bare `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker).
- EF mapping inline in `SpaceStrokerDbContext.OnModelCreating`; migration via `dotnet ef migrations add AddPlayerIconPurchases -p SpaceStroker.Infrastructure -s SpaceStroker.Consumers`; never hand-edit the snapshot.
- Config values are placeholders: every burn price is `10000`.
- Refusal copy exactly: `IconUnlockRefused` = `That icon could not be unlocked.` · `IconBurnPending` = `Your burn could not be confirmed yet. Please try again.` No other message on the purchase path. Real reasons go to the log only.
- Memo text exactly `spacestroker-icon:{iconKey}`. Memo v2 program `MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr`. ComputeBudget program `ComputeBudget111111111111111111111111111111`. BurnChecked tag `15`.
- Never read the holder balance with classic `GetTokenBalance`; use `IHolderBalanceReader` (Token-2022).
- DbContext is NoTracking by default: insert with `Add` + `SaveChangesAsync`.
- Don't run `sed -i` over .cs files (CRLF flips). Use Edit.
- Unique-index race: `catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })` (pattern `RegisterReferralPartnerHandler.cs:134`). EF InMemory never throws it, so the race branch is covered by the integration suite's concurrent replay, not a unit test.

## Review Focus

1. **Wallet with no `Player` row** (burned before ever betting): purchase must still unlock, profile + preferences save must still see the key. Ownership is keyed by wallet, never by `Player.Id`. → Task 3 test `UnknownWallet_WithPurchase_ProfileListsBurnKey`, Task 5 test `NoPlayerRow_IsNotExcluded_Unlocks`.
2. **Burn from a non-ATA token account of the wallet** (owner is wallet but account ≠ derived ATA): `NotABurn`. → Task 4 test `BurnFromNonAtaAccount_IsNotABurn`.
3. **Burn of a different Token-2022 mint with the right memo**: `NotABurn`, nothing recorded. → Task 4 `WrongMint_IsNotABurn`.
4. **Icon key casing / whitespace** (`Burn-01`, ` burn-01`): refused before any RPC (keys are exact). → Task 5 `KeyNotExact_RefusedWithoutRpc`.
5. **Fractional price that is not representable in raw units** (e.g. `0.0000001` at 6 decimals): not purchasable, excluded from `/icon-prices`. → Task 1 `BurnPriceFor_FractionalRaw_IsNull`.

---

### Task 1: Catalog `Burn` kind + per-icon prices in config

**Files:**
- Modify: `SpaceStroker.Domain/Players/PlayerIcons.cs` (L18 enum; append rules after `beanie`, L47)
- Modify: `SpaceStroker.Domain/Configs/Entities/GeneralSettings.cs` (`PlayerIconSettings`, L29-34)
- Modify: `SpaceStroker.Common/spacestrokersettings.json` (`PlayerIcons` block ~L1036)
- Test: `SpaceStroker.Tests/UnitTests/Domain/PlayerIconsTests.cs`, `SpaceStroker.Tests/UnitTests/Api/PlayerIconSettingsBindingTests.cs`

**Interfaces — Produces:**
```csharp
public enum UnlockKind { Free, Trophy, AllTrophies, Staff, Burn }      // PlayerIcons
// PlayerIconSettings:
public Dictionary<string, decimal> BurnPrices { get; set; } = new(StringComparer.Ordinal);
/// Price in UI units, or null when the key is not a Burn icon, price <= 0, or price * 10^decimals is not whole.
public decimal? BurnPriceFor(string key, int decimals);
```

- [ ] **Step 1: Tests.** Rename `UnlockKinds_AreSplit10_10_1_3` → `UnlockKinds_AreSplit10_10_1_3_10` asserting 10 Burn keys `burn-01`…`burn-10` (in that order, after `beanie`). In `PlayerIconSettingsBindingTests` add: `BurnPrices_BindFromJson` (default json → `burn-01` = 10000 for all ten), `BurnPrices_BindFromEnv` (`GeneralSettings__PlayerIcons__BurnPrices__burn-01=25000` → 25000), `BurnPriceFor_NonBurnKey_IsNull` (`edson`), `BurnPriceFor_Zero_IsNull`, `BurnPriceFor_Missing_IsNull`, `BurnPriceFor_FractionalRaw_IsNull` (`0.0000001m`, decimals 6), `BurnPriceFor_Valid_ReturnsPrice` (`10000m`, 6 → `10000m`).
- [ ] **Step 2:** Run `scripts\run_tests.bat` → new tests fail (compile errors count).
- [ ] **Step 3:** Implement. Rules: `new("burn-01", UnlockKind.Burn)` … `"burn-10"`. JSON: `"BurnPrices": { "burn-01": 10000, …, "burn-10": 10000 }` inside `PlayerIcons`.
- [ ] **Step 4:** Run `scripts\run_tests.bat` → green (fix any other test pinning catalog counts).
- [ ] **Step 5:** Commit `Burn icon kind + per-icon burn prices in config (#212)`.

### Task 2: `PlayerIconPurchase` entity, mapping, migration, `IPlayerIconPurchases`

**Files:**
- Create: `SpaceStroker.Domain/Players/Entities/PlayerIconPurchase.cs` (entity + `IconPurchaseStatus { Unlocked = 1, Unmatched = 2 }`, exactly spec §4.1)
- Modify: `SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (DbSet after `ReferralPartners` L46; mapping block next to `Donation` L297-309, exactly spec §4.2)
- Create (generated): `SpaceStroker.Infrastructure/Migrations/<ts>_AddPlayerIconPurchases.cs` + snapshot
- Create: `SpaceStroker.Infrastructure/Services/Players/PlayerIconPurchases.cs`
- Modify: `SpaceStroker.Infrastructure/DependencyInjection.cs` (after L77)
- Test: `SpaceStroker.Tests/UnitTests/Services/Players/PlayerIconPurchasesTests.cs`

**Interfaces — Produces:**
```csharp
public interface IPlayerIconPurchases
{
    /// Distinct IconKey where Wallet == wallet && Status == Unlocked. Empty for unknown wallets.
    Task<IReadOnlyCollection<string>> OwnedKeysAsync(string wallet, CancellationToken ct);
}
public class PlayerIconPurchases(SpaceStrokerDbContext context) : IPlayerIconPurchases
```

- [ ] **Step 1: Tests** `OwnedKeys_ReturnsOnlyUnlockedRowsOfWallet` (rows: wallet A burn-01 Unlocked, A burn-02 Unmatched, B burn-03 Unlocked → A gets `[burn-01]`), `OwnedKeys_DeduplicatesRepeatPurchases` (two Unlocked burn-01 rows → one key), `OwnedKeys_UnknownWallet_Empty`.
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement entity, mapping, service, `services.AddScoped<IPlayerIconPurchases, PlayerIconPurchases>();`. Generate the migration from `src\`; confirm it only creates `PlayerIconPurchases` + the 2 indexes.
- [ ] **Step 4:** Run → green; `dotnet build` the solution.
- [ ] **Step 5:** Commit `PlayerIconPurchases table + owned-keys reader (#212)`.

### Task 3: Unlock rules honour purchased keys (profile, preferences save, guard)

**Files:**
- Modify: `SpaceStroker.Infrastructure/Services/Players/PlayerIconRules.cs` (interface L14-22, impl L34-49, `IsUnlocked` L72-80)
- Modify: `Handlers/Players/UpdatePlayerPreferences/UpdatePlayerPreferencesHandler.cs` (ctor L31, check L152-162)
- Modify: `Handlers/Players/GetPlayerProfile/GetPlayerProfileHandler.cs` (ctor L28-30, unknown-wallet body L72-81, live stamp L108-120)
- Modify: `Services/Players/PlayerAvatarGuard.cs` (L66)
- Test: `UnitTests/Services/Players/PlayerIconRulesTests.cs`, `UnitTests/Services/Players/PlayerAvatarGuardTests.cs`, `UnitTests/Handlers/UpdatePlayerPreferencesIconTests.cs`, `UnitTests/Handlers/GetPlayerProfileHandlerTests.cs`; fix constructions in `UnitTests/Background/RefundInvalidBetConsumerTests.cs:531`.

**Interfaces:**
- Consumes: `IPlayerIconPurchases.OwnedKeysAsync` (Task 2).
- Produces (signature change):
```csharp
IReadOnlySet<string> Unlocked(string wallet, IEnumerable<AchievementDto> achievements, IReadOnlyCollection<string> purchasedKeys);
IReadOnlyList<string> UnlockedOrdered(string wallet, IEnumerable<AchievementDto> achievements, IReadOnlyCollection<string> purchasedKeys);
```
`IsUnlocked` gains the explicit arm `UnlockKind.Burn => purchased.Contains(rule.Key)`; keep `_ => false`. `Effective` unchanged.

- [ ] **Step 1: Tests.**
  - Rules: `BurnKey_LockedWithoutPurchase`, `BurnKey_UnlockedWhenPurchased`, `UnlockedOrdered_PlacesBurnKeysAfterStaff_InCatalogOrder`.
  - Guard: `EquippedBurnIcon_ZeroTrophies_IsNotCleared` (stored `burn-01`, no achievements, no purchases passed → `AvatarId` stays `burn-01`, nothing saved) — this pins permanence (AC5).
  - Preferences: `PurchasedBurnKey_Saves`, `UnownedBurnKey_RefusedAfterSignature` (generic `SelfExclusionRequestInvalid`, nonce consumed, same as any locked icon).
  - Profile: `UnlockedAvatars_IncludesPurchasedKeys_Live` (prime cache, then insert a purchase row → next call lists it without cache drop), `UnknownWallet_WithPurchase_ProfileListsBurnKey`, `Unavailable_BodyStillFreeKeysOnly`.
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement. Profile handler + preferences handler inject `IPlayerIconPurchases` and load owned keys **by wallet** right before the rules call (profile: outside the cached body, both the unknown-wallet branch and the live stamp). Guard passes `Array.Empty<string>()` with a one-line comment: `NeedsTrophyCheck` only admits Trophy/AllTrophies, so purchased keys can never affect it and the query is skipped.
- [ ] **Step 4:** Run `scripts\run_tests.bat` → green.
- [ ] **Step 5:** Commit `Icon rules include purchased burn icons; burn picks survive the guard (#212)`.

### Task 4: On-chain burn verifier

**Files:**
- Modify: `SpaceStroker.Infrastructure/Services/Solana/ISolanaService.cs`, `SolanaService.cs` (static next to `DescribeIncomingTransfer` L1139; async method next to `ValidateIncomingTransfer` L931)
- Create: `SpaceStroker.Infrastructure/Services/Solana/IconBurnCheck.cs`
- Modify: `SpaceStroker.Tests.Integration/Harness/TestDoubles.cs` (`FakeSolanaService` L56: compile + control surface, used in Task 8) and any other `ISolanaService` implementers that fail to compile
- Test: `SpaceStroker.Tests/UnitTests/Services/IconBurnVerificationTests.cs` (mirror `IncomingTransferValidationTests.cs` style)

**Interfaces — Produces:**
```csharp
public enum IconBurnOutcome { Burned, NotABurn, Unreadable }
public record IconBurnCheck(IconBurnOutcome Outcome, decimal Amount, string? Memo, string? Reason);
// ISolanaService:
Task<IconBurnCheck> VerifyIconBurnAsync(string signature, string wallet, HolderTokenSettings token);
// SolanaService:
public static IconBurnCheck DescribeIconBurn(JObject result, string wallet, HolderTokenSettings token); // result = RPC "result" object
// FakeSolanaService:
public void SetIconBurn(string signature, string wallet, decimal amount, string? memo);
public bool IconBurnUnreadable { get; set; }
public int IconBurnReads { get; }
```
Fake semantics: unreadable flag → `Unreadable`; signature set for that wallet → `Burned(amount, memo)`; otherwise `NotABurn`. Reset in `Clear()`.

`VerifyIconBurnAsync`: `ExecuteRpcAsync("getTransaction", _getTransactionPolicy, GetTransactionFailed, c => c.GetTransactionAsync(signature, Commitment.Confirmed))`; failure / null `result` / JSON parse error → `Unreadable`; else `DescribeIconBurn(JObject.Parse(raw)["result"], …)`. Add a code comment that Solnet 6.1.0 cannot fetch v0 transactions, so the front must send legacy.

`DescribeIconBurn` runs spec §6.1 checks 1–7 in order, returning `NotABurn` with a short private reason on the first failure. Instruction data is base58 (`Encoders.Base58`); BurnChecked layout = `u8 tag(15) | u64 amount LE | u8 decimals`. The owner ATA is `AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount`-equivalent for Token-2022 (seeds `[wallet, token.TokenProgramId, mint]`, ATA program); reuse whatever `GetToken2022Balance` already derives with. Raw amounts in balances come from `uiTokenAmount.amount` (string). `Amount = raw / 10^decimals` as decimal.

- [ ] **Step 1: Tests** (build tx JSON with a small helper `BurnTx(...)` in the test file; real keys from `new Account()`):
  `ValidBurnWithMemo_IsBurned` (amount 10000, memo `spacestroker-icon:burn-01`) · `NoMemo_IsBurned_MemoNull` · `WithComputeBudget_IsBurned` · `Transfer_IsNotABurn` (TransferChecked; Σ delta 0) · `WrongMint_IsNotABurn` · `WrongAuthority_IsNotABurn` · `FeePayerNotWallet_IsNotABurn` · `BurnFromNonAtaAccount_IsNotABurn` · `OwnerDeltaDiffers_IsNotABurn` · `MintWideDeltaNotNegativeAmount_IsNotABurn` (owner −N but another account +N) · `FailedTx_IsNotABurn` (`meta.err` set) · `ExtraProgramInstruction_IsNotABurn` (System transfer added) · `TwoTokenInstructions_IsNotABurn` · `DecimalsMismatch_IsNotABurn` · `TwoMemos_IsNotABurn` · `MissingPostEntryCountsAsZero` (owner account closed after burning all → still `Burned`).
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement verifier + fake.
- [ ] **Step 4:** Run `scripts\run_tests.bat` → green; `dotnet build` (integration project must compile).
- [ ] **Step 5:** Commit `On-chain icon burn verifier (#212)`.

### Task 5: `POST /players/icon-purchases` handler

**Files:**
- Create: `SpaceStroker.Infrastructure/Handlers/Players/PurchasePlayerIcon/PurchasePlayerIconRequest.cs` (`PurchasePlayerIconRequestBody { [MaxLength(44)] Wallet, [MaxLength(20)] IconKey, [MaxLength(100)] BurnSignature }`, `record PurchasePlayerIconRequest(PurchasePlayerIconRequestBody Body) : IRequest<PlayerIconPurchaseDto?>`)
- Create: `.../PurchasePlayerIcon/PurchasePlayerIconHandler.cs` (`BaseRequestHandler<,>`, template `Handlers/Chat/ReportDonation/ReportDonationHandler.cs`)
- Create: `SpaceStroker.Infrastructure/Contracts/Players/PlayerIconPurchaseDto.cs` (`IconKey`, `IReadOnlyList<string> UnlockedAvatars`)
- Modify: `SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs` (pattern `PartnerCodeNotFound`, L323 / L630)
- Test: `SpaceStroker.Tests/UnitTests/Handlers/PurchasePlayerIconHandlerTests.cs`

**Interfaces:**
- Consumes: `ISolanaService.VerifyIconBurnAsync` (Task 4), `PlayerIconSettings.BurnPriceFor` (Task 1), `IPlayerIconPurchases` + `IPlayerIconRules.UnlockedOrdered(wallet, achievements, owned)` (Tasks 2–3), `IPlayerProfileService.ComputeAchievementsAsync` for `UnlockedAvatars` (no row → empty achievements), `ICacheService.Remove`.
- Produces: `ErrorMessages.IconUnlockRefused`, `ErrorMessages.IconBurnPending`.

Execution order is spec §6.2 steps 1–8 verbatim (shape → configured → replay → self-exclusion via `Player.IsSelfExcluded(DateTime.UtcNow)` → verify → match/insert → 23505 reload → cache drops `PlayerProfile:`, `PlayerLevel:`, `StrokeBalance:` (Task 6 key)). Logs: NotABurn → Warning with reason; Unmatched → Error with the exact template `"Unmatched icon burn {Signature} wallet {Wallet} icon {Key} amount {Amount} reason {Reason}"`; already-owned re-purchase → Warning.

- [ ] **Step 1: Tests** (Moq `ISolanaService`; real InMemory context; `Settings()` with holder token configured):
  `ValidBurn_Unlocks_RowAndCachesDropped` · `Replay_SameWalletAndIcon_SucceedsWithoutRpc_OneRow` · `Replay_OtherWallet_Refused` · `Replay_OtherIcon_Refused` · `Replay_OfUnmatched_Refused` · `SelfExcluded_Refused_VerifierNeverCalled` · `NoPlayerRow_IsNotExcluded_Unlocks` · `NonBurnKey_Refused_WithoutRpc` · `KeyNotExact_RefusedWithoutRpc` (`Burn-01`) · `InvalidWallet_Refused` · `TokenNotConfigured_Refused` · `Unreadable_ReturnsPending_NoRow` · `NotABurn_Refused_NoRow` · `BelowPrice_UnmatchedRow_ErrorLogged_Refused` · `WrongMemo_Unmatched_MemoMismatch` · `NoPriceConfigured_Unmatched_NoPrice` · `Overpay_UnlockedWithRealAmount` · `AlreadyOwned_UnlockedAgain_Succeeds`. Every refusal asserts the exact `IconUnlockRefused` text; pending asserts `IconBurnPending`.
- [ ] **Step 2:** Run → fail.
- [ ] **Step 3:** Implement handler, DTO, messages.
- [ ] **Step 4:** Run → green.
- [ ] **Step 5:** Commit `POST /players/icon-purchases handler (#212)`.

### Task 6: `$STROKE` balance + icon prices handlers

**Files:**
- Create: `Handlers/Players/GetStrokeBalance/GetStrokeBalanceRequest.cs` + `GetStrokeBalanceHandler.cs`; `Contracts/Players/StrokeBalanceDto.cs` (`Symbol`, `Decimals`, `decimal? Balance`)
- Create: `Handlers/Players/GetIconPrices/GetIconPricesRequest.cs` + `GetIconPricesHandler.cs`; `Contracts/Players/IconPricesDto.cs` (`Symbol`, `Decimals`, `Mint`, `Dictionary<string, decimal> Prices` — insertion in catalog order)
- Modify: `Services/Cache/CacheHelper.cs` (next to `GenerateHolderEligibilityCacheSettings` L202)
- Test: `UnitTests/Handlers/GetStrokeBalanceHandlerTests.cs`, `UnitTests/Handlers/GetIconPricesHandlerTests.cs`

**Interfaces — Produces:**
```csharp
public static CacheSettings GenerateStrokeBalanceCacheSettings(string wallet) => new($"StrokeBalance:{wallet}", TimeSpan.FromSeconds(15));
public record GetStrokeBalanceRequest(string? Wallet) : IRequest<StrokeBalanceDto>;
public record GetIconPricesRequest() : IRequest<IconPricesDto>;
```
Balance uses `IHolderBalanceReader.GetBalanceAsync`; caches only non-null; invalid wallet / unconfigured token → `Balance = null`, never a validation message.

- [ ] **Step 1: Tests:** balance `ReturnsReaderBalance` · `NullRead_ReturnsNull_NotCached` · `InvalidWallet_Null_ReaderNotCalled` · `CacheHit_ReaderNotCalled`; prices `OnlyPricedBurnKeys_InCatalogOrder` · `ZeroAndFractionalRawExcluded` · `TokenNotConfigured_EmptyPrices`.
- [ ] **Step 2:** Run → fail. **Step 3:** Implement. **Step 4:** Run → green.
- [ ] **Step 5:** Commit `GET stroke-balance and icon-prices (#212)`.

### Task 7: Controller routes + rate limits

**Files:**
- Modify: `SpaceStroker.Api/Controllers/PlayersController.cs` — `GET stroke-balance` (`[FromQuery] string? wallet`), `GET icon-prices`, `POST icon-purchases`; `[HttpOptions]` for each (GETs: `Allow: GET,OPTIONS` like `profile`; add `icon-purchases` to the shared POST OPTIONS method L149-151).
- Modify: `SpaceStroker.Common/spacestrokersettings.json` `GeneralRules` (next to L96): `post:/players/icon-purchases` 10/1m, `get:/players/stroke-balance` 30/1m, `get:/players/icon-prices` 30/1m.
- Test: `UnitTests/Api/RateLimitSettingsTests.cs` — assert the three rules.

- [ ] **Step 1:** Add the rate-limit assertions → run → fail. **Step 2:** Implement routes + rules. **Step 3:** Run → green.
- [ ] **Step 4:** Commit `Players routes for icon burns, balance and prices (#212)`.

### Task 8: Integration tests (mandatory)

**Files:**
- Create: `SpaceStroker.Tests.Integration/IconBurnFlowTests.cs` (`[Collection(IntegrationCollection.Name)]`, pattern `ProfileV2FlowTests`; copy its `SaveIconAsync` / `ReadProfileAsync` helpers or lift them to a shared helper if trivially reusable)

Use `_fixture.SolanaService` (the API host's fake). Cases exactly spec §11.2 1–6, plus:
7. **Concurrent replay:** fire the same purchase twice with `Task.WhenAll` on a fresh signature → both 200, exactly one row (exercises the 23505 branch against real Postgres).
Assert rows by querying the DB through the fixture's context helper (whatever `ProfileV2FlowTests` / `PartnerCodeLinkFlowTests` use).

- [ ] **Step 1:** Write the tests. **Step 2:** `scripts\run_integration_tests.bat` → green (fix code, not tests, on genuine failures).
- [ ] **Step 3:** Commit `Integration: icon burn → purchase → equip, replay, retry, self-exclusion (#212)`.

### Task 9: Full verification

- [ ] `scripts\run_tests.bat` → all green. `scripts\run_integration_tests.bat` → all green.
- [ ] Record the list of added/changed test files and test names for the report (constraints §3).
- [ ] `git status` clean apart from intended changes; push `feature/token-integration`.
