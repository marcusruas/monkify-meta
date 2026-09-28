# Partner Self-Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let anyone register as a referral partner on `/partner` (sign + code + terms → live `ReferralPartner` row + session), show every partner their link, add a BECOME A PARTNER entry on the cold home hero, and a PARTNER PROGRAM section in LP `/terms`.

**Architecture:** One new MediatR write handler (`RegisterReferralPartner`) behind `POST /referrals/registrations`, guarded by the existing purpose-scoped `WalletProof` + the two existing UNIQUE indexes; one public read (`GET /referrals/program`) that computes the explainer's figures through `ReferralEarningsService.ApplyShare`. Frontend adds a register panel + hook reusing `usePartnerEarnings`'s session storage. LP adds a terms section fed by one constant.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, xUnit/Shouldly/Moq, Testcontainers; Vue 3 + TS; Astro 5.

**Spec:** `monkify-meta/specs/partner-self-registration.md` (authoritative). Worktrees: `E:\Monkify\.worktrees\partner-self-registration\<repo>`.

## Global Constraints

- Self-registered `SharePercentage` = **5** (percent of the house cut). Code rules: **3–20**, `[a-z0-9_-]`, lowercased.
- Nonce purpose: **`referral-register`**. Route: **`POST /referrals/registrations`**, **`GET /referrals/program`**.
- Signed message (both sides, byte-identical, `\n`-joined):
  `Space Stroker partner registration` / `Wallet: {wallet}` / `Code: {code}` / `I accept the Space Stroker partner program terms.` / `Nonce: {nonce}`.
- Rate limit `post:/referrals/registrations`, `1m`, `5`.
- New messages: `ReferralTermsRequired`, `ReferralCodeInvalid`, `ReferralWalletAlreadyPartner`, `ReferralCodeTaken`; everything else `ReferralRequestInvalid`.
- Referral link: `https://spacestroker.com/?ref=<code>`; terms link `${TERMS_URL}#partner-program`.
- EF inline; migration `AddReferralPartnerTermsAcceptedAt` via `dotnet ef`; unit tests via `scripts/run_tests.bat`; integration via `scripts/run_integration_tests.bat`.

---

### Task 1: Domain rules + entity column + migration (backend)

**Files:**
- Create: `src/SpaceStroker.Domain/Referrals/ReferralSelfRegistration.cs`
- Modify: `src/SpaceStroker.Domain/Referrals/Entities/ReferralPartner.cs` (+`TermsAcceptedAt`)
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (ReferralPartner block)
- Create: migration `AddReferralPartnerTermsAcceptedAt`
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/ReferralSelfRegistrationTests.cs`

**Produces:** `ReferralSelfRegistration.SharePercentage` (decimal 5), `MinCodeLength` 3, `MaxCodeLength` 20, `Reserved` (IReadOnlySet<string>), `NormalizeForRegistration(string?) : string?`, `IsReserved(string) : bool`; `ReferralPartner.TermsAcceptedAt : DateTime?`.

- [ ] Write tests: `" EdsonFM "`→`edsonfm`; `"ab"`/21 chars → null; `"abc"`/20 chars ok; `"a.b"`,`"a b"`,`"açb"` → null; every `Reserved` entry equals its own `NormalizeForRegistration` (non-null); `IsReserved("spacestroker")` true; `SharePercentage == 5m`.
- [ ] Run `scripts/run_tests.bat --filter ReferralSelfRegistrationTests` → fails (type missing).
- [ ] Implement:
```csharp
namespace SpaceStroker.Domain.Referrals
{
    public static class ReferralSelfRegistration
    {
        public const decimal SharePercentage = 5m;
        public const int MinCodeLength = 3;
        public const int MaxCodeLength = 20;

        public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "spacestroker", "space-stroker", "space_stroker", "chimpout", "monkify", "edson",
            "admin", "official", "support", "help", "house", "team", "staff", "mod", "moderator",
            "partner", "partners", "referral", "ref", "treasury", "orbit",
        };

        public static string? NormalizeForRegistration(string? raw)
        {
            var normalized = ReferralDirectory.Normalize(raw);
            return normalized is { Length: >= MinCodeLength and <= MaxCodeLength } ? normalized : null;
        }

        public static bool IsReserved(string normalized) => Reserved.Contains(normalized);
    }
}
```
- [ ] Add `public DateTime? TermsAcceptedAt { get; set; }` (doc comment per spec §4.1); map `builder.Property(x => x.TermsAcceptedAt).IsRequired(false);`.
- [ ] `dotnet ef migrations add AddReferralPartnerTermsAcceptedAt --project SpaceStroker.Infrastructure --startup-project SpaceStroker.Consumers`; inspect: one `AddColumn<DateTime>(nullable: true)`.
- [ ] Tests pass; commit.

### Task 2: Nonce purpose + registration message + handler (backend)

**Files:**
- Modify: `…/Handlers/Players/GetWalletNonce/GetWalletNonceRequest.cs` (`ReferralRegister`, `IsKnown`)
- Modify: `…/Services/Referrals/ReferralMessages.cs` (`BuildRegistrationMessage(wallet, code, nonce)`)
- Create: `…/Handlers/Referrals/RegisterReferralPartner/RegisterReferralPartnerRequest.cs`, `RegisterReferralPartnerHandler.cs`
- Modify: `SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs` (4 keys)
- Test: `UnitTests/Handlers/Referrals/RegisterReferralPartnerHandlerTests.cs`

**Consumes:** Task 1. `WalletProof.TryConsume`, `IWalletSessionService.Mint`, `IReferralDirectoryProvider`, `ReferralSessionDto`.
**Produces:** `RegisterReferralPartnerRequest(RegisterReferralPartnerRequestBody? Body) : IRequest<ReferralSessionDto?>`; body `{ Wallet, Code, AcceptTerms, Nonce, Signature }`.

- [ ] Tests (use `ReferralHandlerTestsBase`; handler built with `FakeReferralDirectoryProvider`, `WalletSessionService(settings)`, `CacheReturning(Nonce)`; register `ImpostorWallet` as the newcomer; existing partner = `edsonfm`/PartnerWallet):
  - happy: row with Code/NormalizedCode `newcode`, DisplayName `newcode`, Share 5, PayoutWallet newcomer, Active, TermsAcceptedAt ≈ now; returns session; `InvalidateCount == 1`; no Player row; cache `Remove` called once.
  - input `" NewCode "` stored `newcode` (signature over normalized).
  - `AcceptTerms=false` → `ReferralTermsRequired`, `cache.Remove` never called.
  - `"ab"` → `ReferralCodeInvalid`, `Remove` never called.
  - bad signature / unknown nonce / signature over a different code → `ReferralRequestInvalid`.
  - wallet == PartnerAddress (active) → `ReferralWalletAlreadyPartner`; same with an inactive partner.
  - code `EdsonFM` → `ReferralCodeTaken`; inactive partner's code → taken; reserved `admin` → taken.
  - each house wallet (TokenOwnerPublicKey / Orbit / HouseSeed / ReferralPayout set to newcomer address) → `ReferralRequestInvalid`.
  - null body → `ReferralTermsRequired`? → **No**: null body → `ReferralRequestInvalid` (check body null first).
- [ ] Implement handler per spec §6.6 order: null body → vague; terms; shape; proof; house; already partner; taken; insert with 23505 mapping by `ConstraintName`; `Invalidate()`; mint session. Warning log `"Referral partner self-registered {Code}"`.
- [ ] resx entries (values from spec §6.7) + Designer properties (same pattern as `ReferralCashOutOnCooldown`).
- [ ] Tests pass; commit.

### Task 3: Program endpoint, statement code, controller, rate limit (backend)

**Files:**
- Create: `…/Handlers/Referrals/GetReferralProgram/GetReferralProgramRequest.cs` (+ `ReferralProgramDto`), `GetReferralProgramHandler.cs`
- Modify: `…/GetReferralEarnings/GetReferralEarningsRequest.cs` (`Code`), `GetReferralEarningsHandler.cs` (`Code = partner.Code`)
- Modify: `SpaceStroker.Api/Controllers/ReferralsController.cs`
- Modify: `SpaceStroker.Common/spacestrokersettings.json`
- Test: `GetReferralProgramHandlerTests.cs` (new), `GetReferralEarningsHandlerTests.cs`, `UnitTests/Api/RateLimitSettingsTests.cs`, nonce-purpose test

- [ ] Tests: program at CommissionPercentage 2 → `EarningPerOneSol == 0.001m`, at 5 → `0.0025m`; Share 5, Min/Cooldown passthrough, bounds 3/20. Statement `Code == "edsonfm"`. RateLimit pinned set includes `post:/referrals/registrations`; new rule test (5/1m; matches `post:/referrals/registrations`; not `options:`/`post:/referrals/session`). `WalletNoncePurpose.IsKnown("referral-register")`.
- [ ] Implement handler: `ApplyShare(new ReferralPartner { SharePercentage = ReferralSelfRegistration.SharePercentage }, 1m)`.
- [ ] Controller: `[HttpOptions("registrations")]`, `[HttpOptions("program")]`, `POST registrations`, `GET program`.
- [ ] Settings rule; tests pass; commit.

### Task 4: Integration tests (backend)

**Files:** Create `src/SpaceStroker.Tests.Integration/ReferralSelfRegistrationFlowTests.cs`.

- [ ] (a) happy path register → statement code/share → referred paid bet on a dedicated type with the new code → round `RewardForWinnersCompleted` → bet stamped → `earnedAllTime` delta == `stake × CommissionPercentage/100 × 5/100` floored at 9 dp. (b) duplicate code other case → taken, one row. (c) concurrent same-code two wallets → one 200, one taken. (d) same wallet twice → already partner. (e) rate limit on a dedicated `CreateClientWithForwardedFor` client: 5 non-429 then 429.
- [ ] `scripts/run_integration_tests.bat` green (filter to the class first, then full); commit.

### Task 5: Frontend

**Files:** `src/types/partner.ts`, `src/services/partnerService.ts`, `src/hooks/usePartnerEarnings.ts`, create `src/hooks/usePartnerRegistration.ts`, create `src/components/partner/PartnerRegisterPanel.vue`, `src/pages/PartnerPage.vue`, `src/components/home/HomeHero.vue`, `src/pages/HomePage.vue`, `src/hooks/useAnalytics.ts`, `src/routes.ts`.

- [ ] Types/service/builder/`adoptSession` per spec §7.1–7.3.
- [ ] Hook per §7.4; panel per §7.6 (figures from `/referrals/program` via `utils/money.formatSol`); page per §7.5 (register mode from `?register=1`, BECOME A PARTNER action, YOUR LINK + COPY); hero/home/analytics per §7.7; routes comment.
- [ ] `yarn lint` + `yarn build` (junction hub node_modules; CRLF prettier false alarms on untouched files only). Commit.

### Task 6: LP terms

**Files:** `monkify-lp/src/constants.ts` (`PARTNER_PROGRAM`), `monkify-lp/src/pages/terms.astro`.

- [ ] Section per spec §8 with `id="partner-program"`; `astro build` (or `npm run build`) passes. Commit.

### Task 7: Verify, push, deploy, hand off

- [ ] `scripts/run_tests.bat` + `scripts/run_integration_tests.bat` green; push all four branches; compare links; local stack deploy (with migration → full `setup_local_environment.bat`); joint health; hand-off.
