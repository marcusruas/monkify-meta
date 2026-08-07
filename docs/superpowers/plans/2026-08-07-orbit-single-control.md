# Orbit Single Control — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge `OrbitEnrollment.Stopped` into `Enabled` (pause semantics — no money moves on toggle), delete the disable-cash-out + re-enable cool-off machinery, make cash-out the only money door, and enforce the claim invariant *held iff enabled ∧ balance ≥ stake, evaluated on resolved balances*.

**Architecture:** Backend-first on `feature/orbit-new-ui` in `.worktrees/orbit-ui-overhaul/monkify-backend`, then front in `.worktrees/orbit-ui-overhaul/monkify-front`, then the integration test. Spec: `monkify-meta/specs/orbit-single-control.md` (authoritative — read it first).

**Tech Stack:** .NET 8 + EF Core/Postgres (migrations via `dotnet ef`, applied by Consumers), MediatR handlers, xUnit (`run_tests.bat`), Testcontainers integration suite; Vue 3 + TS (Vue CLI, **yarn**), Tailwind 3.

## Global Constraints

- Worktrees: backend `C:\Users\kurok\Desktop\monkify\.worktrees\orbit-ui-overhaul\monkify-backend`, front `...\monkify-front`. Never touch the `main` hubs.
- Backend tests: **`run_tests.bat`**, never bare `dotnet test` (excludes the ProductionGames Monte Carlo suite by design).
- Front: **yarn** only; repo files are CRLF — never `sed -i` (breaks `Insert ␍` prettier rule); `yarn lint` must pass.
- Config files: mock/placeholder values only.
- Player-facing refusals stay deliberately vague (constraints.md §6); reasons go to logs.
- Commit style: short imperative sentence (match repo history), footer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Push (`git push origin feature/orbit-new-ui`) after each repo's commits land; never force-push.
- Front + backend must both be pushed before any local-stack deploy — the API contract changes (DTO loses `stopped`/`enableLockedUntil`; PATCH loses `stopped`/`nonce`/`signature`).
- Keep surrounding comment style: this codebase documents WHY at decision points; update stale comments you touch rather than deleting the rationale.

---

### Task 1: Backend — drop `Stopped` from the domain, gates, and schema

**Files:**
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs` (delete `Stopped` + its doc block; rewrite `Enabled` doc: "the play/pause switch — flipping it moves no money; claim lifecycle per the single-control spec")
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs:242` (delete the `Stopped` property config line)
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetPlacementService.cs:62-65`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs:200`
- Create: `src/SpaceStroker.Infrastructure/Migrations/<ts>_MergeOrbitStoppedIntoEnabled.cs` (+Designer, via `dotnet ef`)
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitBetPlacementServiceTests.cs`, `OrbitBetSettlementServiceTests.cs`, `src/SpaceStroker.Tests/UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs` (compile fixes: any `Stopped = ...` seeding collapses to `Enabled = ...` truth table)

**Interfaces:**
- Produces: `OrbitEnrollment` without `Stopped`; planner eligibility = `enrollment.Enabled && enrollment.GameParametersId == parameters.Id`; settlement guard = `enrollment is not { Enabled: true } || enrollment.GameParametersId == null`.

- [ ] **Step 1:** Edit the four source files above. Placement where-clause becomes:
```csharp
.Where(enrollment => enrollment.Enabled && enrollment.GameParametersId == parameters.Id)
```
(keep the #150 comment, rewritten: pausing is now `Enabled = false`; the planner skips and nothing else changes).
- [ ] **Step 2:** Generate the migration. First check for a design-time factory / previous invocation style (`grep -rn "IDesignTimeDbContextFactory" src/`); then from the backend worktree root:
```
dotnet ef migrations add MergeOrbitStoppedIntoEnabled --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Api
```
(If `dotnet-ef` is missing: `dotnet tool install -g dotnet-ef` — version 8.x.)
- [ ] **Step 3:** Hand-edit the generated `Up()` so the data moves BEFORE the column drops (verify exact table/column quoting against `20260731142630_AddOrbitAutoBetEngine.cs`):
```csharp
// Merge: a stopped enrolment was a pause; the merged flag holds the pause meaning.
migrationBuilder.Sql("""UPDATE "OrbitEnrollments" SET "Enabled" = ("Enabled" AND NOT "Stopped");""");
// Claim invariant (spec §6.2): parked players hold no word reservation.
migrationBuilder.Sql("""DELETE FROM "OrbitChoiceClaims" WHERE "PlayerId" IN (SELECT "PlayerId" FROM "OrbitEnrollments" WHERE NOT "Enabled");""");
migrationBuilder.DropColumn(name: "Stopped", table: "OrbitEnrollments");
```
`Down()`: re-add the column, `nullable: false, defaultValue: false`.
- [ ] **Step 4:** `dotnet build SpaceStroker.sln` (or the solution file present) — fix every compile error that references `Stopped` in the two test files listed (seed data truth table: old `Enabled=true, Stopped=true` → `Enabled=false`; old `Enabled=true, Stopped=false` → `Enabled=true`). Do NOT touch `UpdateOrbitSettingsTests`/`CreditOrbitDepositTests` beyond compiling — their behavior changes come in Tasks 2–4.
- [ ] **Step 5:** Run the two touched suites: `dotnet test src/SpaceStroker.Tests --filter "FullyQualifiedName~OrbitBetPlacementService|FullyQualifiedName~OrbitBetSettlementService|FullyQualifiedName~PlaceOrbitBetConsumer"` → PASS.
- [ ] **Step 6:** Commit: `Merge the Orbit pause into Enabled and drop the Stopped column.`

### Task 2: Backend — the toggle becomes a plain settings write

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsRequest.cs`
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/UpdateOrbitSettings/UpdateOrbitSettingsHandler.cs`
- Modify: `src/SpaceStroker.Api/Controllers/OrbitController.cs` (only if it references the removed body fields)
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` + `ErrorMessages.Designer.cs` (delete `OrbitEnableLocked` entry + property)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/UpdateOrbitSettingsTests.cs`, `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs`

**Interfaces:**
- Consumes: Task 1's merged entity.
- Produces: `UpdateOrbitSettingsRequestBody` WITHOUT `Stopped`, `Nonce`, `Signature`; disabling keeps the balance and releases the claim; enabling re-claims only when `balance >= parameters.RequiredAmount` (skips silently when unfunded — the deposit hook in Task 4 picks it up).

- [ ] **Step 1 (failing tests first):** Rewrite the affected cases in `UpdateOrbitSettingsTests` — delete every test asserting: the re-enable lock refusal, the disable signature/nonce requirement, the `ForcedOnDisable` reservation on disable, and `Stopped` patching. Add (using the existing test fixture/builder patterns in that file):
```csharp
[Fact] // disable: free, keeps money, releases claim
public async Task Disabling_ReleasesTheClaim_AndMovesNoMoney() { /* enabled+funded enrolment with a claim → patch { Enabled = false } with NO nonce/signature → handler returns DTO, reserver mock NEVER called, claims.Release called once, enrollment.Enabled == false */ }

[Fact]
public async Task Enabling_Unfunded_SkipsTheClaimAttempt() { /* enrolment with Choice, balance 0 → patch { Enabled = true } → succeeds, claims.TryClaim NOT called */ }

[Fact]
public async Task Enabling_Funded_ReclaimsTheWord() { /* balance >= stake → TryClaim called; a lost race still rejects with OrbitChoiceAlreadyClaimed */ }
```
Run them → FAIL (current handler still demands the signature / calls the reserver).
- [ ] **Step 2:** Edit `UpdateOrbitSettingsRequestBody`: delete `Stopped`, `Nonce`, `Signature` and their comments.
- [ ] **Step 3:** Edit the handler:
  - Keep `var isDisabling = body.Enabled == false && enrollment is { Enabled: true };` (the claim release still keys on it).
  - Delete: the `OrbitProof.TryConsume` block; the `OrbitReEnableLock.ResolveLockedUntil` refusal inside the enable branch; `if (body.Stopped is { } stopped) ...`; the post-save `await _reserver.Reserve(..., ForcedOnDisable, ...)`; the `_reserver`/`_cache` fields, ctor params and now-unused usings.
  - Add `IOrbitLedgerService ledger` to the ctor. In the re-enable claim block (the `else if (body.Enabled == true && ...)` arm), wrap ONLY the `TryClaim` in a funded guard, keeping the validity check unconditional:
```csharp
if (parameters.PresetChoices.IsNullOrEmpty())
{
    if (!_claims.IsValidForType(enrollment.Choice, parameters))
        return Reject(ErrorMessages.OrbitChoiceInvalid);

    // Claim only what can actually play (spec §6.2): an unfunded enable holds no reservation;
    // the deposit-credit hook re-attempts the moment money arrives.
    if (await _ledger.ResolveBalance(player.Id, cancellationToken) >= parameters.RequiredAmount
        && !await _claims.TryClaim(player.Id, parameters.Id, enrollment.Choice, cancellationToken))
        return Reject(ErrorMessages.OrbitChoiceAlreadyClaimed);
}
```
  - Same funded guard on the explicit `body.Choice` claim path? NO — leave that path as-is: saving a NEW word claims it regardless (that is today's behavior for an enabled player, and an off player's save doesn't claim — verify the existing conditions and keep them).
  - Final nudge: `if (enrollment.Enabled)` (drop `&& !enrollment.Stopped`), and update its `⚠️ Not for a STOPPED player` comment.
  - Rewrite the class doc: auth is session-only now; nothing here moves money.
- [ ] **Step 4:** Delete the `OrbitEnableLocked` resx `<data>` entry and its Designer property. Fix `OrbitSettingsBindingTests` if it binds the removed fields.
- [ ] **Step 5:** Run: `dotnet test src/SpaceStroker.Tests --filter "FullyQualifiedName~UpdateOrbitSettings|FullyQualifiedName~OrbitSettingsBinding"` → PASS.
- [ ] **Step 6:** Commit: `Make the Orbit toggle a plain settings write with no forced cash-out.`

### Task 3: Backend — delete the re-enable lock; trim GetOrbit; deposits need an enrolment, not Enabled

**Files:**
- Delete: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitReEnableLock.cs`
- Modify: `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs` (drop `ReEnableLockHours` + doc)
- Modify: `src/SpaceStroker.Common/spacestrokersettings.json` (drop the `"ReEnableLockHours": 1,` line — source copy only; `bin/` copies regenerate)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` (DTO: drop `Stopped`, `EnableLockedUntil`)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitHandler.cs` (drop both assignments)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs`
- Modify: `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (+Designer): reword `OrbitNotEnabled` value to `Set Orbit up before depositing. Anything you have already sent will be credited as soon as you do.` (keep the key name — the front matches on server text nowhere, and the constant name churn isn't worth it)
- Modify: `src/SpaceStroker.Domain/Orbit/ValueObjects/OrbitCashOutSource.cs` (doc `ForcedOnDisable` as legacy: written until the single-control merge; no writer remains; also fix the enum's class doc: "one per UTC day" → the rolling gap)
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/CreditOrbitDepositTests.cs`

**Interfaces:**
- Produces: deposit gate = an `OrbitEnrollments` row exists AND the player is not self-excluded; `OrbitDto` without `Stopped`/`EnableLockedUntil`.

- [ ] **Step 1 (failing tests first):** In `CreditOrbitDepositTests`: repurpose the "refused when not enabled" cases → `Deposit_WithNoEnrolment_IsDeferred` (no row → `OrbitNotEnabled` message path) and `Deposit_IntoParkedEnrolment_Credits` (row with `Enabled=false` → credits fine). Delete the cool-off-exception cases. Add `Deposit_WhileSelfExcluded_IsRefused`. Run → FAIL.
- [ ] **Step 2:** Rewrite handler step 3 (the whole `isEnabled`/`inCoolOff` block):
```csharp
// 3. An enrolment has to EXIST — not be enabled. A parked Orbit holding a balance is a
//    first-class state now (the toggle moves no money), and a dry player must be able to refund
//    while parked. What still must not happen is money landing for someone who has never seen
//    the Orbit page: no row, no credit — the client keeps the stash and replays it after setup.
var isEnrolled = await Context.OrbitEnrollments
    .AnyAsync(x => x.PlayerId == player.Id, cancellationToken);

if (!isEnrolled)
{
    Log.Warning("Orbit deposit deferred: player {PlayerId} has no Orbit enrolment.", player.Id);
    Messaging.ReturnValidationFailureMessage(ErrorMessages.OrbitNotEnabled);
    return null;
}

// Someone who has asked to stop must not be able to fund a gambling balance, parked or not.
// A refusal, not a deferral — mirrors the enable branch of UpdateOrbitSettings.
if (player.IsSelfExcluded(DateTime.UtcNow))
{
    Log.Warning("Orbit deposit refused: player {PlayerId} is self-excluded.", player.Id);
    Messaging.ReturnValidationFailureMessage(ErrorMessages.SelfExclusionActive);
    return null;
}
```
(Check `ErrorMessages.SelfExclusionActive` exists — UpdateOrbitSettings uses it.)
- [ ] **Step 3:** Delete `OrbitReEnableLock.cs`, the setting, the json line, the two GetOrbit fields/assignments. Build; chase remaining references (`grep -rn "ReEnableLock\|EnableLockedUntil" src/ --include="*.cs"` → only Migrations/Designer history and tests may remain; fix tests).
- [ ] **Step 4:** Run: `dotnet test src/SpaceStroker.Tests --filter "FullyQualifiedName~CreditOrbitDeposit|FullyQualifiedName~GetOrbit"` → PASS.
- [ ] **Step 5:** Commit: `Delete the Orbit re-enable cool-off and gate deposits on enrolment.`

### Task 4: Backend — the claim invariant hooks (reserver, settlement, deposit)

**Files:**
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitCashOutReserver.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` (`EvaluateZeroOut`)
- Modify: `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs`
- Test: `OrbitCashOutReserverTests.cs`, `OrbitBetSettlementServiceTests.cs`, `CreditOrbitDepositTests.cs`, `SetSelfExclusionOrbitTests.cs`

**Interfaces:**
- Consumes: `IOrbitChoiceClaimService` (`TryClaim`, `Release`, `HoldsClaim`, `IsValidForType`) — already DI-registered (verify alongside the other Orbit services' registrations).
- Produces: release on any cash-out that leaves `< stake`; release/re-claim at settlement (`EvaluateZeroOut`); best-effort re-claim after a deposit credit.

- [ ] **Step 1 (failing tests first):**
```csharp
// OrbitCashOutReserverTests
[Fact] public async Task Reserving_BelowTheStake_ReleasesTheClaim() { /* full cash-out (any source) → claims.Release called */ }
[Fact] public async Task Reserving_LeavingAPlayableBalance_KeepsTheClaim() { /* partial, remainder >= stake → Release NOT called */ }
// OrbitBetSettlementServiceTests
[Fact] public async Task Settling_UnderTheStake_ReleasesTheClaim() { /* enabled, choice, balance < stake → Release called (alongside the zero-out flag logic) */ }
[Fact] public async Task Settling_BackOverTheStake_ReclaimsAnUnheldWord() { /* enabled, free-text choice, balance >= stake, HoldsClaim false → TryClaim called; false result is swallowed */ }
// CreditOrbitDepositTests
[Fact] public async Task Crediting_ReclaimsTheWord_WhenEnabledAndNowFunded() { /* enabled enrolment, choice, no claim, credit lifts balance over stake → TryClaim; a lost race does NOT fail the credit */ }
// SetSelfExclusionOrbitTests
[Fact] public async Task SelfExcluding_ReleasesTheClaim() { /* via the reserver hook — full forced cash-out → Release */ }
```
Run → FAIL.
- [ ] **Step 2:** Reserver — inject `IOrbitChoiceClaimService claims`; after the transaction commits and the event publishes, before the final return:
```csharp
// Spec §6.2 — the claim is held only while the balance can pay for a round. Best-effort:
// the money is already reserved, and a claim-cleanup failure must not turn that into an error.
try
{
    var stake = await _context.OrbitEnrollments
        .Where(x => x.PlayerId == playerId && x.GameParametersId != null)
        .Select(x => (decimal?)x.GameParameters!.RequiredAmount)
        .FirstOrDefaultAsync(cancellationToken);

    if (stake is not { } required || available - amount < required)
        await _claims.Release(playerId, cancellationToken);
}
catch (Exception ex)
{
    Log.Warning(ex, "Could not evaluate the word claim after a cash-out by player {PlayerId}.", playerId);
}
```
(Add the Serilog using; verify the navigation property name for the join — use an explicit join to `GameParameters` if the entity has no nav.)
- [ ] **Step 3:** Settlement — inject `IOrbitChoiceClaimService`; extend `EvaluateZeroOut`'s enrollment/stake query to also select `Choice` and whether the type is free-text (`!x.PresetChoices.Any()`), then:
  - in the `balance >= requiredAmount` (recovered) branch, BEFORE the flag-clear early return: if free-text, `Choice` set, and `!await _claims.HoldsClaim(...)` → `await _claims.TryClaim(...)` (ignore the bool — `ChoiceIsTakenByAnother` already reports a lost word).
  - in the under-stake fall-through, FIRST thing: `await _claims.Release(playerId, cancellationToken);` (idempotent), then the existing notification logic untouched.
  - Add a comment carrying the spec's reasoning: evaluated at settlement, on resolved balances, for the same reason the zero-out is — releasing at debit would churn the claim every round over a bet still riding.
- [ ] **Step 4:** Deposit handler — inject `IOrbitChoiceClaimService`; after `PublishDepositCredited`, add a private `TryReclaimWord(player.Id, ct)`: load the enrolment (`Enabled`, `Choice`, `GameParametersId`); bail unless enabled + choice + type; load parameters with `PresetChoices`; bail unless free-text, `IsValidForType`, balance (the `balance` local just returned by `CreditDeposit`) `>= RequiredAmount`, and `!HoldsClaim`; `TryClaim` and ignore the result; whole method in try/catch logging a warning — a credited deposit must never fail on claim upkeep.
- [ ] **Step 5:** Run: `dotnet test src/SpaceStroker.Tests --filter "FullyQualifiedName~OrbitCashOutReserver|FullyQualifiedName~OrbitBetSettlement|FullyQualifiedName~CreditOrbitDeposit|FullyQualifiedName~SetSelfExclusion"` → PASS.
- [ ] **Step 6:** Commit: `Tie the Orbit word claim to a playable balance at settlement and cash-out.`

### Task 5: Backend — full unit suite + push

- [ ] **Step 1:** `run_tests.bat` from the backend worktree root → fix any remaining fallout (expect stragglers in `OrbitAutoBetFlowTests`-adjacent unit fixtures referencing `Stopped`, `EnableLockedUntil`, or the disable signature). Zero failures.
- [ ] **Step 2:** Report which tests changed (AGENTS.md rule 3) in the session, not the commit.
- [ ] **Step 3:** Commit anything from step 1: `Fix the unit suite for the single-control merge.` Push: `git push origin feature/orbit-new-ui`.

### Task 6: Front — types + `useOrbit` core

**Files:**
- Modify: `src/types/orbit.ts`, `src/types/mobileWallet.ts`
- Modify: `src/hooks/useOrbit.ts`
- Test: `yarn build` (no test runner in this repo)

**Interfaces:**
- Produces: `OrbitStatus = 'signed-out' | 'off' | 'unfunded' | 'running'`; `OrbitSettingsPatch` without `stopped`/`nonce`/`signature`; `OrbitState` without `stopped`/`enableLockedUntil`; `useOrbit` no longer exports `enableLockedUntil`/`enableLockedLabel`/`lastForcedCashOut`; new export `nextManualCashOutLabel` (string | null, `"HH:MM UTC"`).

- [ ] **Step 1:** `types/orbit.ts` — collapse the union (update its doc: four values), delete the three `OrbitSettingsPatch` fields + comments, delete `OrbitState.stopped`/`enableLockedUntil`. `types/mobileWallet.ts` — drop `'disable'` from `MobilePendingOrbit['action']` if present.
- [ ] **Step 2:** `useOrbit.ts`:
  - Delete `lastForcedCashOut` (state, reset, export, comments), `isEnableLocked`, `isDepositBlocked`, `enableLockedUntil`, `enableLockedLabel`.
  - `deposit()`: delete the cool-off guard and the whole `isOrbitOff()` enrol-parked PATCH block (the Game tab's own `gameParametersId` PATCH is what creates the row — verify `OrbitGameTab` patches on select before deleting; if it does not, patch `{ gameParametersId: orbit.value.gameParametersId }` in its place). Delete `wasUnableToPlay` + the post-credit `{ stopped: true }` park (single flag: an off Orbit stays off; a running one was deliberately started). Keep the self-excluded and no-game guards.
  - `resumePendingDeposit()`: drop the two `isDepositBlocked()` terms (stash aging + 4xx drop are now unconditional).
  - `updateSettings()`: delete the whole `isDisabling` signing block — the PATCH is plain.
  - `resumeOrbitDeeplink()`: delete the `'disable'` branch and its toast.
  - `orbitStatus`:
```ts
const orbitStatus = computed<OrbitStatus>(() => {
  if (!hasSession.value) return 'signed-out';
  if (!orbit.value?.enabled) return 'off';
  // Enabled but unable to pay for a round — either never funded or ran dry (the auto-park
  // below flips it off moments later; until then "needs money" is still the truth).
  if (needsFirstDeposit.value || isPausedForFunds.value) return 'unfunded';
  return 'running';
});
```
  - Auto-park watcher: condition drops the `!orbit.value?.stopped` term; the patch becomes `updateSettings(wallet, { enabled: false })`; rewrite the header comment (same #150 rule, new flag: RUN is the only thing that restarts betting).
  - New export:
```ts
/** The manual cash-out gap, as the button countdown. UTC to match what the server enforces. */
const nextManualCashOutLabel = computed(() => {
  const next = orbit.value?.nextManualCashOutAt;
  if (!next) return null;
  const at = new Date(next);
  return at.getTime() > Date.now() ? `${at.toISOString().slice(11, 16)} UTC` : null;
});
```
  - Deposit stash comment sweep: the "two DEFERRALS" notes now describe only the no-enrolment deferral.
- [ ] **Step 3:** `yarn build` → expect failures ONLY in components not yet updated (Tasks 7–8). Note them; do not fix here if they belong to a later task's file list.

### Task 7: Front — control bar duo + page handlers

**Files:**
- Modify: `src/components/orbit/OrbitControlBar.vue`
- Modify: `src/pages/OrbitPage.vue`

**Interfaces:**
- Consumes: `useOrbit().cashOut(wallet)` (full balance when amount omitted), `nextManualCashOutLabel`.
- Produces: `OrbitControlBar` props `{ status, busy, hasChoice, hasGame, hasFunds, hasRunBefore, hasBalance, cashOutGapLabel }`, emits `toggle` and `cash-out`.

- [ ] **Step 1:** Rewrite `OrbitControlBar.vue` script (template keeps the `.orbit-trio*` classes — two buttons in the same flex):
```ts
const props = defineProps<{
  status: OrbitStatus; busy: boolean;
  hasChoice: boolean; hasGame: boolean; hasFunds: boolean; hasRunBefore: boolean;
  hasBalance: boolean; cashOutGapLabel: string | null;
}>();
const emit = defineEmits<{ toggle: []; 'cash-out': [] }>();

const setupBlocker = computed((): string | null => {
  if (!props.hasGame) return 'Pick a game first';
  if (!props.hasChoice) return 'Pick a word first';
  if (!props.hasFunds) return 'Add money first';
  return null;
});

// ONE switch. RUN when off (with the setup reasons), PAUSE when running — pressing it is the
// only thing that starts or stops automated play, and it never moves money.
const toggle = computed((): { state: TrioState; label: string; note: string } => {
  if (props.status === 'running')
    return { state: 'enabled', label: 'PAUSE ORBIT', note: 'Pause automated mode' };
  if (props.status === 'unfunded')
    return { state: 'disabled', label: 'RUN ORBIT', note: 'Add money first' };
  if (setupBlocker.value)
    return { state: 'disabled', label: 'RUN ORBIT', note: setupBlocker.value };
  return props.hasRunBefore
    ? { state: 'enabled', label: 'RESUME', note: 'Start automated mode' }
    : { state: 'primary', label: 'RUN ORBIT', note: 'Start automated mode' };
});

// The money door. Disabled only for the two honest reasons; the gap countdown — the ONLY
// cool-off left in Orbit — lives here, on the control it actually gates.
const cashOut = computed((): { state: TrioState; label: string; note: string } => {
  if (!props.hasBalance)
    return { state: 'disabled', label: 'CASH OUT', note: 'Nothing to cash out' };
  if (props.cashOutGapLabel)
    return { state: 'disabled', label: 'CASH OUT', note: `Next cash-out ${props.cashOutGapLabel}` };
  return { state: 'enabled', label: 'CASH OUT', note: 'Send balance to your wallet' };
});
```
Template: RUN/PAUSE button keeps the `run` glyph + `--run`/`--primary` classes; CASH OUT keeps the outline `--off` class and the `⏻` glyph slot (swap the glyph for `⌁` or keep `⏻` — keep `⏻`, it now reads "power the money home"; note in comment). Preserve the aria pattern (`aria-label` states condition vs action) and the `isNativeDisabled` helper. Delete the `stop` computed, `lockedLabel` prop, and all `cooling-off`/`stopped` arms.
- [ ] **Step 2:** `OrbitPage.vue` — replace `onRun`/`onStop`/`onTurnOff`:
```ts
const onToggle = async () => {
  if (!wallet.value) return;
  notice.value = null;
  try {
    await updateSettings(wallet.value, { enabled: orbitStatus.value !== 'running' });
  } catch { /* useOrbit surfaced the reason in lastError */ }
};

// The one press that moves money on this bar. Same signed flow as the Money tab's cash-out —
// one path, one gate, one signature UX.
const onCashOut = async () => {
  if (!wallet.value) return;
  notice.value = null;
  const amount = orbit.value?.balance ?? 0;
  const label = orbit.value?.tokenLabel ?? '';
  try {
    await cashOut(wallet.value);
    toast.info(`${amount} ${label} is on its way to your wallet.`);
  } catch { /* surfaced in lastError */ }
};
```
(Pull `cashOut` + `nextManualCashOutLabel` from the `useOrbit()` destructure; drop `enableLockedLabel`.) Check the mobile-hop edge: `cashOut` returns early with `'hopped'` swallowed — if `orbit.value` is returned unchanged and a hop happened, the toast would lie; mirror however `OrbitMoneyTab.onCashOut` handles that today (read it first; reuse its pattern).
Wire the template:
```html
<OrbitControlBar v-if="signedIn" class="mt-4 md:mt-5"
  :status="orbitStatus" :busy="isBusy"
  :has-choice="hasPlayableChoice" :has-game="!!orbit?.enabled || !!activeGameType"
  :has-funds="hasFundsForARound" :has-run-before="(orbit?.orbitBetsPlaced ?? 0) > 0"
  :has-balance="(orbit?.balance ?? 0) > 0" :cash-out-gap-label="nextManualCashOutLabel"
  @toggle="onToggle" @cash-out="onCashOut" />
```
Delete any cool-off panel / `lastForcedCashOut` usage on the page.
- [ ] **Step 3:** `yarn build` → remaining failures only in Task 8 files.

### Task 8: Front — status surfaces, modal, copy

**Files:**
- Modify: `src/components/orbit/OrbitStatusTag.vue`, `OrbitStatusBanner.vue`, `OrbitLauncher.vue`, `OrbitModal.vue`
- Modify: `src/components/orbit/tabs/OrbitMoneyTab.vue` (stale comments + cash-out hop pattern reuse), `OrbitRecoverTab.vue` (copy only, if it mentions the cool-off)
- Modify: `src/hooks/useOrbitNotifications.ts` (pause copy — verify it says "Press RUN", keep)
- Modify: `src/components/guide/sections/GuideOrbit.vue`
- Modify: `src/assets/styles/arcade.css` (only if unused `cooling` selectors remain — grep)

- [ ] **Step 1:** `OrbitStatusTag.vue` — the tag distinguishes a deliberate pause from never-set-up by history, since the flag alone no longer can:
```ts
const { orbitStatus, orbit } = useOrbit();
const isParkedPause = computed(
  () => orbitStatus.value === 'off' && (orbit.value?.orbitBetsPlaced ?? 0) > 0
);
const label = computed(() =>
  isParkedPause.value
    ? 'PAUSED'
    : ({ 'signed-out': 'SIGNED OUT', off: 'OFF', unfunded: 'NEEDS MONEY', running: 'RUNNING' })[orbitStatus.value]
);
```
Dot classes: running → live (unchanged); `isParkedPause` → violet (the deliberate state, exactly what STOPPED wore); unfunded → amber (unchanged); plain off/signed-out → mute. Delete the `cooling-off`/`stopped` arms.
- [ ] **Step 2:** `OrbitStatusBanner.vue` — status prop type becomes the four-value union; collapse its `cooling-off`/`stopped`/`paused` arms into the same off/unfunded logic (read the component fully first; keep its hide-when-signed-out/off behavior).
- [ ] **Step 3:** `OrbitLauncher.vue` — `needsAttention = isPausedForFunds` (drop the lock guard + import); labels unchanged otherwise.
- [ ] **Step 4:** `OrbitModal.vue` — `isRunning = !!orbit.value?.enabled` (+ not unfunded? keep its exact current meaning: it drove RUN/STOP button state — rewire to the single toggle exactly as the page: RUN patches `{ enabled: true, gameParametersId }` when un-enrolled, STOP patches `{ enabled: false }`); delete every `enableLockedUntil`/`enableLockedLabel` reference and the `stopped` terms in `canRun`/`onRun`/`onStop`/copy. Read the file in full before editing — it has its own `canRun` note structure.
- [ ] **Step 5:** Copy sweep:
  - `GuideOrbit.vue`: replace the last card with:
```ts
{
  title: 'PAUSING IS FREE — CASH OUT IS THE MONEY DOOR',
  body: 'Switch Orbit off and on whenever you like — pausing moves no money and your balance stays put. Money only leaves when you cash out (or a stop rule fires for you), and manual cash-outs just have a short gap between them.',
},
```
  - Also update the dry-run card if it says "waits for you to top it up" (still true — verify wording).
  - `grep -rn -i "cool" src/` → fix every Orbit-related hit (the self-exclusion cool-off is a different feature — leave it).
- [ ] **Step 6:** `yarn lint` (CRLF intact — no `sed`), `yarn build` → clean.
- [ ] **Step 7:** Commit: `Collapse the Orbit trio into one toggle and a cash-out door.` Push: `git push origin feature/orbit-new-ui`.

### Task 9: Backend — integration tests

**Files:**
- Modify: `src/SpaceStroker.Tests.Integration/OrbitFeedbackFlowTests.cs`, `OrbitAutoPickChoiceFlowTests.cs`, `OrbitAutoBetFlowTests.cs`, `OrbitFlowTests.cs`, `SelfExclusionFlowTests.cs` (whatever references `Stopped`, `EnableLockedUntil`, the disable signature, or the cool-off — grep first)
- Create: `src/SpaceStroker.Tests.Integration/OrbitSingleControlFlowTests.cs`

**Interfaces:**
- Consumes: the Testcontainers fixture + HTTP-call helpers the existing Orbit flow tests use (read `OrbitFeedbackFlowTests.cs` first and mirror its setup verbatim).

- [ ] **Step 1:** Docker Desktop must be running (`docker info`).
- [ ] **Step 2:** Grep the integration project for the removed surface; update each test to the new contract (PATCH without `stopped`/nonce; DTO without the two fields; deposits into parked enrolments succeed).
- [ ] **Step 3:** New happy-path test (spec §10.2), one test method following the suite's naming style:
enrol (PATCH game type + word) → deposit → RUN (`enabled: true`; assert DTO `choiceIsReserved == true`) → PAUSE (`enabled: false`; assert balance unchanged, no new cash-out transaction, `choiceIsReserved == false`, `choice` still set) → RUN again (assert re-claimed) → full manual cash-out (nonce + signature; assert balance 0, a `Manual` cash-out row, `nextManualCashOutAt` set, claim released, enrolment still holds the word preference).
- [ ] **Step 4:** Run the integration suite the way the repo does (check for a script beside `run_tests.bat`; else `dotnet test src/SpaceStroker.Tests.Integration`). → PASS.
- [ ] **Step 5:** Commit: `Cover the single-control Orbit flow end to end.` Push.

### Task 10: Wrap-up

- [ ] **Step 1:** `run_tests.bat` once more (final green), `yarn lint && yarn build` once more in the front worktree.
- [ ] **Step 2:** Report: which unit tests changed (rule 3); the KB spec (`specs/orbit-single-control.md`) + this plan sit uncommitted in `monkify-meta` — ask the user whether to commit them now or leave for the ship-feature KB sync.
- [ ] **Step 3:** Offer the local Docker stack deploy for hands-on testing (memory: `local-docker-deploy-from-worktree` — hub `yarn.lock` copy, port remaps).
