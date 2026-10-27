# Partner code on profile — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A wallet-signed `POST /players/partner-code` that links a partner code to a player exactly once, a race-free write-once referral writer used by every referral write site, and `partnerCode`/`partnerLinkedAt` on `GET /players/profile`.

**Architecture:** One new service `IPlayerReferralWriter` owns every write of `Player.ReferralCode`/`ReferredAt` via a conditional `ExecuteUpdate … WHERE ReferralCode IS NULL`. The new MediatR handler `LinkPartnerCodeHandler` follows `RegisterReferralPartnerHandler` (shape → `WalletProof.TryConsume` → checks → write). `RegisterBetHandler` and `CreateOrbitSessionHandler` stop writing the referral columns themselves.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, xUnit + Shouldly + Moq, Testcontainers integration suite.

**Spec:** `monkify-meta/specs/partner-code-on-profile.md` (authoritative). Worktree: `E:\Monkify\.worktrees\partner-code-on-profile\monkify-backend`. Branch `feature/partner-code-on-profile`.

## Global Constraints

- Work ONLY in `E:\Monkify\.worktrees\partner-code-on-profile\monkify-backend`; never touch the hub `E:\Monkify\monkify-backend`.
- Unit tests: `scripts\run_tests.bat` (never raw full `dotnet test`). Integration: `scripts\run_integration_tests.bat` (Docker).
- No migration, no new config except one rate-limit rule. EF mapping inline only.
- Every refusal before a valid proof = `ErrorMessages.SelfExclusionRequestInvalid` (vague). House/streamer wallet refusals stay vague after the proof.
- Nonce purpose string is exactly `partner-code`.
- Signed message is exactly (joined with `"\n"`): `Space Stroker partner code` / `Wallet: {wallet}` / `Code: {code}` / `I understand this partner code is permanent.` / `Nonce: {nonce}` where `{code}` = `ReferralDirectory.Normalize(input)`.
- Error copy exactly: `That code doesn't exist.` · `That code is no longer active.` · `You can't use your own partner code.` · `You already have a partner code.`
- Store `partner.Code` (canonical), never user input.
- Don't run `sed -i` across many .cs files (CRLF flips repo-wide). Use Edit.
- DbContext is NoTracking by default: never load-mutate-SaveChanges without `AsTracking()`/`Update`.
- Unit tests use EF InMemory, which cannot run `ExecuteUpdateAsync` — unit tests use the in-memory fake writer; the SQL writer is tested in the integration suite.

---

### Task 1: `IPlayerReferralWriter` (SQL) + in-memory fake + DI

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Referrals/IPlayerReferralWriter.cs`
- Create: `src/SpaceStroker.Infrastructure/Services/Referrals/PlayerReferralWriter.cs`
- Create: `src/SpaceStroker.Tests/UnitTests/Shared/InMemoryPlayerReferralWriter.cs` (pattern: `InMemoryOrbitStakeClaimStore.cs` in the same folder)
- Modify: `src/SpaceStroker.Infrastructure/DependencyInjection.cs` (beside `IReferralDirectoryProvider` registration ~L129): `services.AddScoped<IPlayerReferralWriter, PlayerReferralWriter>();`

**Interfaces — Produces:**
```csharp
namespace SpaceStroker.Infrastructure.Services.Referrals
{
    /// The ONLY writer of Player.ReferralCode / ReferredAt (#175 write-once, #213 race fix).
    public interface IPlayerReferralWriter
    {
        /// Sets the code iff the player's ReferralCode is NULL; creates the Player row if missing.
        /// Returns true when THIS call set the code; false when the player already had one.
        Task<bool> TryLinkAsync(string wallet, string canonicalCode, DateTime now, CancellationToken ct);
    }
}
```

- [ ] **Step 1: Write `PlayerReferralWriter`**

```csharp
public sealed class PlayerReferralWriter : IPlayerReferralWriter
{
    private readonly SpaceStrokerDbContext _context;
    public PlayerReferralWriter(SpaceStrokerDbContext context) => _context = context;

    public async Task<bool> TryLinkAsync(string wallet, string canonicalCode, DateTime now, CancellationToken ct)
    {
        if (await UpdateIfNullAsync(wallet, canonicalCode, now, ct))
            return true;

        if (await _context.Players.AnyAsync(p => p.ExternalPlayerId == wallet, ct))
            return false; // row exists and already carries a code

        var player = new Player { ExternalPlayerId = wallet, LastSeenAt = now, ReferralCode = canonicalCode, ReferredAt = now };
        _context.Players.Add(player);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // A concurrent writer created the row first — fall back to the conditional update.
            _context.Entry(player).State = EntityState.Detached;
            return await UpdateIfNullAsync(wallet, canonicalCode, now, ct);
        }
    }

    // ⚠️ The WHERE is the guarantee: once ReferralCode is non-NULL nothing changes it.
    private async Task<bool> UpdateIfNullAsync(string wallet, string code, DateTime now, CancellationToken ct)
        => await _context.Players
            .Where(p => p.ExternalPlayerId == wallet && p.ReferralCode == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReferralCode, code).SetProperty(p => p.ReferredAt, now), ct) == 1;
}
```

- [ ] **Step 2: Write the unit-test fake** `InMemoryPlayerReferralWriter(SpaceStrokerDbContext context)`: `AsTracking()` load by wallet; if null → add a new Player with the code and save, return true; if `ReferralCode != null` → return false; else set code + `ReferredAt`, save, return true. (Non-atomic — unit tests only; doc-comment says so.)
- [ ] **Step 3: Register in DI**, build: `dotnet build src/SpaceStroker.sln` (or the solution file in `src/`). Expected: success.
- [ ] **Step 4: Commit** `git commit -m "Add write-once IPlayerReferralWriter (#213)"`.

---

### Task 2: Route every existing referral write through the writer (race fix)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` — constructor (L40-70) gains `IPlayerReferralWriter referralWriter` (append as the LAST parameter); `UpsertPlayerAsync` (L363-412).
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreateOrbitSession/CreateOrbitSessionHandler.cs` — constructor + `EnsurePlayerExists` (L80-116).
- Modify tests that construct these handlers: `src/SpaceStroker.Tests/UnitTests/Handlers/RegisterBetHandlerTests.cs` (L99), `src/SpaceStroker.Tests/UnitTests/Handlers/DuelHarness.cs` (L292), and wherever `CreateOrbitSessionHandler` is constructed (grep `CreateOrbitSessionHandler(` under `SpaceStroker.Tests`; it may be resolved via DI in `OrbitHandlerTestsBase` — add the fake there). Pass `new InMemoryPlayerReferralWriter(context)`. Also grep for any other `new RegisterBetHandler(` (e.g. Consumers tests) and update.
- Modify comments: `src/SpaceStroker.Domain/Games/Entities/Player.cs` L27-28 and `SpaceStrokerDbContext.cs` L106-108 → "written only through IPlayerReferralWriter (bet, Orbit session, profile link #213)".

**Interfaces — Consumes:** `IPlayerReferralWriter.TryLinkAsync` (Task 1).

- [ ] **Step 1: Failing test** in `RegisterBetHandlerTests.cs`:
```csharp
[Fact]
public async Task ABetWithoutReferral_DoesNotClearALinkedCode()
{
    // Arrange: player exists with no code; a SEPARATE context links "edsonfm" after the handler's
    // context has already been created (simulates the profile link racing the bet).
    // Act: place a paid bet with NO referral body.
    // Assert: reloading the player from a fresh context still shows ReferralCode == "edsonfm".
}
```
Concretely: seed the Player through a separate context (see existing referral tests at L241-319 for seeding helpers), create the handler context, then in another context set `ReferralCode = "edsonfm"` and save, then run the handler with a body without `ReferralCode`. With today's `Players.Update(existing)` the stale NULL is written back → test FAILS.
- [ ] **Step 2: Run** `scripts\run_tests.bat` filtered: `dotnet test src\SpaceStroker.Tests --filter "FullyQualifiedName~RegisterBetHandlerTests"`. Expected: the new test FAILS.
- [ ] **Step 3: Rewrite `UpsertPlayerAsync`:**
```csharp
var directory = await _referrals.GetAsync(cancellationToken);
var resolvedReferral = directory.ResolveActiveCode(referralCode);

var existing = await _spacestrokerDbContext.Players.AsTracking()
    .FirstOrDefaultAsync(x => x.ExternalPlayerId == externalPlayerId, cancellationToken);

if (existing == null)
{
    // Insert WITHOUT the referral columns — the writer owns them.
    existing = new Player { ExternalPlayerId = externalPlayerId, DisplayName = displayName, LastSeenAt = now };
    await _spacestrokerDbContext.Players.AddAsync(existing, cancellationToken);
}
else
{
    // ⚠️ TRACKED, so SaveChanges writes ONLY the columns changed here. The old NoTracking +
    // Players.Update(existing) rewrote every column — including a stale NULL ReferralCode over a
    // code linked from the profile a moment earlier (#213).
    existing.LastSeenAt = now;
    if (!string.IsNullOrWhiteSpace(displayName))
        existing.DisplayName = displayName;
}
await _spacestrokerDbContext.SaveChangesAsync(cancellationToken);

// ⚠️ WRITE-ONCE (#175) — first touch wins, atomically. Result ignored: silent on purpose.
if (resolvedReferral != null)
    await _referralWriter.TryLinkAsync(externalPlayerId, resolvedReferral, now, cancellationToken);

// Re-read the referral columns so the bet stamp sees whatever ANY writer stored.
var stored = await _spacestrokerDbContext.Players.AsNoTracking()
    .Where(x => x.ExternalPlayerId == externalPlayerId)
    .Select(x => new { x.ReferralCode, x.ReferredAt })
    .FirstAsync(cancellationToken);
existing.ReferralCode = stored.ReferralCode;   // in-memory only; entity is tracked but these were just read from the DB
existing.ReferredAt = stored.ReferredAt;
return existing;
```
⚠️ After assigning those two properties on a TRACKED entity, a later `SaveChanges` in the handler would write them (same values, harmless) — but if any later SaveChanges exists, make sure the values equal the DB's (they do — just read). Check whether the old code relied on `existing` being untracked (e.g. `GameService.InsertBet` detaching `bet.Player`, see the comment at L226-227 and `features/player.md`). If tracking `existing` causes an EF identity conflict in `InsertBet`, detach it before returning: `_spacestrokerDbContext.Entry(existing).State = EntityState.Detached;` (do this, and keep the in-memory assignments).
- [ ] **Step 4: Rewrite `CreateOrbitSessionHandler.EnsurePlayerExists`:** keep the AsTracking load/insert + `LastSeenAt`, but remove `ReferralCode`/`ReferredAt` from the insert and delete the conditional block; after `SaveChangesAsync`, `if (resolvedReferral != null) await _referralWriter.TryLinkAsync(wallet, resolvedReferral, now, cancellationToken);`.
- [ ] **Step 5: Run** RegisterBet + Orbit + Duel unit tests: `dotnet test src\SpaceStroker.Tests --filter "FullyQualifiedName~RegisterBet|FullyQualifiedName~Orbit|FullyQualifiedName~Duel"`. Expected: all pass, including the existing `…NeverOverwritesAnExistingReferral` cases and the new test.
- [ ] **Step 6: Commit** `git commit -m "Route bet + Orbit referral writes through the write-once writer (#213)"`.

---

### Task 3: Nonce purpose, message, error messages, rate limit

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` — add `public const string PartnerCode = "partner-code";` with comment, and `|| purpose == PartnerCode` in `IsKnown` (L55-60).
- Modify: `src/SpaceStroker.Infrastructure/Services/Referrals/ReferralMessages.cs` — add:
```csharp
/// <summary>#213 — the profile partner-code link. <paramref name="code"/> is NORMALIZED on both sides.</summary>
public static string BuildPartnerCodeMessage(string wallet, string code, string nonce)
    => string.Join("\n",
        "Space Stroker partner code",
        $"Wallet: {wallet}",
        $"Code: {code}",
        "I understand this partner code is permanent.",
        $"Nonce: {nonce}");
```
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs` — keys `PartnerCodeNotFound` "That code doesn't exist.", `PartnerCodeInactive` "That code is no longer active.", `PartnerCodeOwnCode` "You can't use your own partner code.", `PartnerCodeAlreadyLinked` "You already have a partner code." (copy an existing `ReferralCodeTaken` entry's shape in both files; resx is UTF-8).
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` → `IpRateLimiting.GeneralRules`: `{ "Endpoint": "post:/players/partner-code", "Period": "1m", "Limit": 10 }` next to `post:/referrals/registrations`.
- Test: `src/SpaceStroker.Tests/UnitTests/Api/RateLimitSettingsTests.cs` — add the endpoint to `GeneralRules_AreExactlyTheQuotaProtectingPatterns` (L81-111) and a new `PartnerCodeRule_Is10PerMinute_OnThePostOnly` modelled on `ReferralRegistrationRule_Is5PerMinute_OnThePostOnly` (L266).
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetWalletNonceHandlerTests.cs` — `PartnerCode_IsAKnownNoncePurpose` (`WalletNoncePurpose.IsKnown("partner-code").ShouldBeTrue()`).

- [ ] Step 1: write the two tests → run → FAIL. Step 2: implement → run `dotnet test src\SpaceStroker.Tests --filter "FullyQualifiedName~RateLimitSettings|FullyQualifiedName~GetWalletNonce"` → PASS. Step 3: commit `"Add partner-code nonce purpose, message, errors and rate limit (#213)"`.

---

### Task 4: `LinkPartnerCodeHandler` + controller route

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Handlers/Players/LinkPartnerCode/LinkPartnerCodeRequest.cs`
- Create: `src/SpaceStroker.Infrastructure/Handlers/Players/LinkPartnerCode/LinkPartnerCodeHandler.cs`
- Create: `src/SpaceStroker.Infrastructure/Contracts/Players/PlayerPartnerCodeDto.cs`
- Modify: `src/SpaceStroker.Api/Controllers/PlayersController.cs` — `[HttpPost("partner-code")]` + add `[HttpOptions("partner-code")]` to the OPTIONS handler (L134-135).
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Players/LinkPartnerCodeHandlerTests.cs` (base on `UnitTests/Handlers/Referrals/ReferralHandlerTestsBase.cs` + `RegisterReferralPartnerHandlerTests.cs` for signing with a real Solnet keypair and a real `MemoryCacheService`).

**Interfaces — Produces:**
```csharp
public record LinkPartnerCodeRequest(LinkPartnerCodeRequestBody? Body) : IRequest<PlayerPartnerCodeDto?>;
public class LinkPartnerCodeRequestBody { public string? Wallet {get;set;} public string? Code {get;set;} public string? Nonce {get;set;} public string? Signature {get;set;} }
public record PlayerPartnerCodeDto { public string PartnerCode { get; init; } = ""; public DateTime PartnerLinkedAt { get; init; } }
```
Controller:
```csharp
/// <summary>Link a partner code to the connected wallet ONCE (#213). Signed over
/// GET /players/nonce?purpose=partner-code. Refusals before the proof are vague.</summary>
[HttpPost("partner-code")]
public async Task<IActionResult> LinkPartnerCode([FromBody] LinkPartnerCodeRequestBody body)
    => await ProcessRequest(new LinkPartnerCodeRequest(body));
```

**Handler** (ctor: `SpaceStrokerDbContext context, IMessaging messaging, ICacheService cache, IReferralDirectoryProvider referrals, IStreamerLedger streamers, IPlayerReferralWriter writer, GeneralSettings settings`):
```csharp
var body = request.Body;
var wallet = body?.Wallet?.Trim() ?? string.Empty;
var code = ReferralDirectory.Normalize(body?.Code);
if (body == null || !WalletAddress.IsValid(wallet) || code == null)
    return Vague();
if (!WalletProof.TryConsume(_cache, WalletNoncePurpose.PartnerCode, wallet, body.Nonce, body.Signature,
        (w, n) => ReferralMessages.BuildPartnerCodeMessage(w, code, n)))
    return Vague();
if (IsHouseWallet(wallet) || await _streamers.IsActiveStreamer(wallet, cancellationToken))
    return Vague();   // never reveal a streamer (PlayerProfileDto.cs:13-14)
var partner = (await _referrals.GetAsync(cancellationToken)).ResolveByCode(code);
if (partner == null) return Fail(ErrorMessages.PartnerCodeNotFound);
if (!partner.Active) return Fail(ErrorMessages.PartnerCodeInactive);
if (string.Equals(partner.PayoutWallet, wallet, StringComparison.Ordinal)) return Fail(ErrorMessages.PartnerCodeOwnCode);
var now = DateTime.UtcNow;
if (!await _writer.TryLinkAsync(wallet, partner.Code, now, cancellationToken))
    return Fail(ErrorMessages.PartnerCodeAlreadyLinked);
Log.Warning("Partner code linked from profile {Code}", partner.Code); // prod logs Warning+ only
return new PlayerPartnerCodeDto { PartnerCode = partner.Code, PartnerLinkedAt = now };
```
`Vague()` = `Messaging.ReturnValidationFailureMessage(ErrorMessages.SelfExclusionRequestInvalid); return null;`. `IsHouseWallet` copied from `RegisterReferralPartnerHandler.cs:155-164`. Confirm the `WalletAddress` helper's namespace (used in `GetPlayerProfileHandler.cs`). Confirm `ReferralDirectory.Normalize` is a public static.

**Unit test cases** (fake writer = `InMemoryPlayerReferralWriter`; streamer mock; `FakeReferralDirectoryProvider` with partners `edsonfm` (active, PayoutWallet = a separate keypair) and `oldie` (inactive)):
1. `Links_AnActiveCode_StoresCanonicalSpelling` — input `" EdsonFM "` signed over normalized `edsonfm` → DTO.PartnerCode == "edsonfm"; player row ReferralCode == "edsonfm", ReferredAt set.
2. `RefusesVaguely_WhenBodyNull` / `_WhenWalletMalformed` / `_WhenCodeShapeInvalid` (`"bad code!"`) → message == `SelfExclusionRequestInvalid`, nonce still in cache.
3. `RefusesVaguely_WhenSignatureInvalid` and `RefusesVaguely_WhenNonceMintedForAnotherPurpose` (nonce stored under `preferences`) → vague; no player row.
4. `ConsumesTheNonce` — success, then same request again → vague.
5. `RefusesVaguely_HouseWallet` (settings `TokenOwnerPublicKey` = caller) and `RefusesVaguely_ActiveStreamer` (mock true) → vague; nothing written.
6. `UnknownCode_SaysDoesNotExist`, `InactiveCode_SaysNoLongerActive`, `OwnPayoutWallet_SaysOwnCode` → exact messages; nothing written.
7. `AlreadyLinked_SaysAlreadyHaveACode` — player seeded with `ReferralCode = "other"` → message; row unchanged.
8. `BuildPartnerCodeMessage_IsExactlyTheFiveLines` — exact string.

- [ ] Step 1: write tests → FAIL (types missing). Step 2: implement request/DTO/handler/controller. Step 3: `dotnet test src\SpaceStroker.Tests --filter "FullyQualifiedName~LinkPartnerCode"` → PASS. Step 4: commit `"Add POST /players/partner-code (#213)"`.

---

### Task 5: `partnerCode` + `partnerLinkedAt` on `GET /players/profile`

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Contracts/Players/PlayerProfileDto.cs` — `public string? PartnerCode { get; init; }`, `public DateTime? PartnerLinkedAt { get; init; }` with a doc comment: "#213 — public by owner decision; the player-level code only. Never on Unavailable." `Unavailable` unchanged.
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Players/GetPlayerProfile/GetPlayerProfileHandler.cs` — projection (L60-63) adds `p.ReferralCode, p.ReferredAt`; final `body with { …, PartnerCode = player.ReferralCode, PartnerLinkedAt = player.ReferredAt }` (stamped LIVE, never in the cached body).
- Modify comment only: `src/SpaceStroker.Tests.Integration/ReferralAttributionFlowTests.cs` `TheReferralCode_IsOnNoPublicResponse` — note `/players/profile` is the deliberate #213 exception (do NOT widen the test).
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/GetPlayerProfileHandlerTests.cs` — `ReturnsPartnerCode_Live` (call once to cache, then set the code on the row via a separate context, call again → code present) and `Unavailable_HasNoPartnerCode` (malformed wallet → PartnerCode null).

- [ ] Step 1: tests → FAIL. Step 2: implement. Step 3: `dotnet test src\SpaceStroker.Tests --filter "FullyQualifiedName~GetPlayerProfile"` → PASS. Step 4: commit `"Return the linked partner code on the public profile (#213)"`.

---

### Task 6: Integration tests (MANDATORY happy path + writer SQL)

**Files:**
- Create: `src/SpaceStroker.Tests.Integration/PartnerCodeLinkFlowTests.cs`
- Create: `src/SpaceStroker.Tests.Integration/PlayerReferralWriterSqlTests.cs` (pattern: `OrbitRunStoreSqlTests.cs` — resolve the real writer from a scope)
- Modify: `src/SpaceStroker.Tests.Integration/Harness/IntegrationTestFixture.cs` — correct the stale comment near L289 (partners are table rows; a test may insert a `ReferralPartner` and call `IReferralDirectoryProvider.Invalidate()`), and add a helper if needed: `Task LinkPartnerCodeAsync(Account wallet, string code)` that mints the `partner-code` nonce (`MintWalletNonceAsync`), signs `ReferralMessages.BuildPartnerCodeMessage(wallet, normalized, nonce)` with `Convert.ToBase64String(account.Sign(Encoding.UTF8.GetBytes(msg)))`, and POSTs `/players/partner-code` returning the `HttpResponseMessage`.

Reuse from `ReferralPlayersFlowTests.cs`: `PaidBet(...)`, `_fixture.PlaceBetAsync`, `_fixture.WaitForStatusAsync(game, GameStatus.RewardForWinnersCompleted, …)` (L143-150), the referral session sign-in (L62-80) and `GET /referrals/me/wagers` with `X-Referral-Session` (L85-87). Partner = `IntegrationTestFixture.ReferralCode` (`edsonfm`), its payout wallet keypair from the fixture (`ReferralPartnerWallet`).

**PartnerCodeLinkFlowTests:**
1. `LinkedCode_EarnsOnlyOnBetsPlacedAfterLinking` — fresh wallet: paid bet, NO ref → settle → link `"EdsonFM"` → 200 with `partnerCode == "edsonfm"` → `GET /players/profile?wallet=` has `partnerCode == "edsonfm"` and non-null `partnerLinkedAt` → second paid bet → settle → partner's `/referrals/me/wagers` row for this wallet has wagered == the second stake only; first bet row in DB has `ReferralCode == null`, second == "edsonfm".
2. `SecondLink_IsRefused_AndChangesNothing` — insert a second active partner row (unique code, e.g. `p213b{guid8}`) + `Invalidate()`; link it → 400 whose body contains "You already have a partner code."; DB code still "edsonfm".
3. `UnknownCode_AfterValidProof_SaysDoesNotExist` and `WrongPurposeNonce_IsVague` (nonce minted for `preferences`) → body contains "We could not verify that request".

**PlayerReferralWriterSqlTests:** `SetsCodeOnce_ThenNeverOverwrites` (existing player, two calls, second returns false, code unchanged), `CreatesPlayer_WhenMissing`, `ConcurrentLinks_ExactlyOneWins` (`Task.WhenAll` of two writers in two scopes with different codes on the same NEW wallet → exactly one true; stored code equals the winner).

- [ ] Step 1: write tests. Step 2: `scripts\run_integration_tests.bat` (or with `--filter "FullyQualifiedName~PartnerCode|FullyQualifiedName~PlayerReferralWriter|FullyQualifiedName~Referral"` first) → PASS. Step 3: commit `"Integration tests for the profile partner-code link (#213)"`.

---

### Task 7: Full verification

- [ ] `scripts\run_tests.bat` → all green. Record the full list of added/changed test names.
- [ ] `scripts\run_integration_tests.bat` → all green (known flakiness: typer cap / Orbit enrollment churn — rerun a failing unrelated test once before investigating).
- [ ] `git push origin feature/partner-code-on-profile`.
