# Orbit feedback adjustments — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or
> superpowers:subagent-driven-development) to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Give the Orbit player control and visibility — a fixed reserved word, partial cash-out, a
STOP state, per-event Telegram settings, a bet log, and deposits during the cool-off.

**Architecture:** Six changes on one branch. The load-bearing new concept is `OrbitChoiceClaim`, a row
whose **existence is a reservation** of one word on one game type (unique index), read on the bet path
and released on disable. Everything else extends shipped Orbit surfaces: five columns on
`OrbitEnrollment`, an amount + rolling-gap on the cash-out reserver, a `Stopped` predicate in the
planner, one new read endpoint, and flag-gated Telegram sends.

**Tech Stack:** .NET 8 (MediatR, MassTransit/Postgres transport, EF Core + Npgsql, xUnit +
Testcontainers), Vue 3 + TypeScript (composables, axios), Postgres, Redis.

**Spec:** `specs/orbit-feedback-adjustments.md` — authoritative. **Card:** Trello #150
([fiKfaawz](https://trello.com/c/fiKfaawz)).

## Global Constraints

- **Branch `feature/orbit-feedback-adjustments`**, worktrees `.worktrees/orbit-feedback-adjustments/<repo>`; hubs stay on `main`.
- **EF mapping inline** in `SpaceStrokerDbContext.OnModelCreating` — never `IEntityTypeConfiguration`.
- **Migrations** live in `SpaceStroker.Infrastructure/Migrations`, applied **only** by `SpaceStroker.Consumers`.
- ⚠️ **`SpaceStrokerDbContext` is `NoTracking` by default** — load-mutate-`SaveChanges` persists nothing. Use `AsTracking()` or `ExecuteUpdateAsync`.
- **Unit tests:** `scripts/run_tests.bat` only (never the full `dotnet test` — `ProductionSessions` is a slow Monte Carlo suite). **Integration:** `scripts/run_integration_tests.bat`.
- **Mock/placeholder secrets only.**
- **Orbit stays invisible to other players** — no message, log row or notification may reveal that a player runs Orbit.
- **Frontend:** everything behind `orbitEnabled` (`VUE_APP_ORBIT_ENABLED`, fail-closed `=== 'true'`), gated at mount points **and inside composables**. No test runner ⇒ `yarn lint` + `yarn build`.
- **Choice normalization:** trimmed, lowercase, no spaces or symbols; exactly `ChoiceRequiredLength` from the type's `AllowedCharacters`.
- **`ErrorMessages` is a `.resx`** — edit `ErrorMessages.resx`; the `.Designer.cs` is generated alongside it.

---

## File Structure

**`monkify-backend`**

| File | Responsibility |
| --- | --- |
| `Domain/Orbit/Entities/OrbitChoiceClaim.cs` | **new** — the reservation row |
| `Domain/Orbit/Entities/OrbitEnrollment.cs` | +5 columns |
| `Domain/Configs/Entities/OrbitSettings.cs` | `ReEnableLockHours` 24→1, +`ManualCashOutGapMinutes` |
| `Infrastructure/Services/Orbit/OrbitManualCashOutGate.cs` | **new** — the ONE definition of the manual gap (read + write) |
| `Infrastructure/Services/Orbit/OrbitChoiceClaimService.cs` | **new** — claim/release/validate, so the handler stays readable |
| `Infrastructure/Services/Orbit/OrbitCashOutReserver.cs` | amount + gap |
| `Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` | `!Stopped` |
| `Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` | zero-out evaluation |
| `Infrastructure/Handlers/Orbit/GetOrbitBets/*` | **new** — the paged log |
| `Infrastructure/Handlers/Orbit/{UpdateOrbitSettings,GetOrbit,RequestOrbitCashOut,CreditOrbitDeposit}/*` | the rest |
| `Infrastructure/Consumers/{OrbitBetPlacement,OrbitCashOut,OrbitNotification}/*` | choice + flag gating |
| `Infrastructure/Handlers/Games/RegisterBet/RegisterBetHandler.cs` | the reservation check |
| `Api/Controllers/OrbitController.cs` | `GET /orbit/bets` |

**`monkify-front`** — `types/orbit.ts`, `services/orbitService.ts`, `hooks/useOrbit.ts`,
`hooks/useGame.ts`, `pages/OrbitPage.vue`, `components/orbit/{OrbitBetLog,OrbitModal,OrbitStatusBanner,OrbitStatusTag}.vue`.

---

## Task 1: Domain + EF + migration

**Files:**
- Create: `src/SpaceStroker.Domain/Orbit/Entities/OrbitChoiceClaim.cs`
- Modify: `src/SpaceStroker.Domain/Orbit/Entities/OrbitEnrollment.cs`
- Modify: `src/SpaceStroker.Infrastructure/Data/Context/SpaceStrokerDbContext.cs` (DbSet ~`:26-28`, Orbit region `:222+`)
- Test: `src/SpaceStroker.Tests/UnitTests/Domain/Orbit/OrbitChoiceClaimTests.cs`

**Interfaces produced:** `OrbitChoiceClaim { Guid PlayerId; Guid GameParametersId; string Choice; }`;
`OrbitEnrollment.{Choice, Stopped, NotifyOnCashOut, NotifyOnZeroOut, NotifyOnWin, ZeroOutNotifiedAt}`;
`SpaceStrokerDbContext.OrbitChoiceClaims`.

- [ ] **Step 1:** Add the five columns to `OrbitEnrollment` exactly as spec §4.1 (with its doc-comments — they carry the *why*).
- [ ] **Step 2:** Create `OrbitChoiceClaim` exactly as spec §4.2.
- [ ] **Step 3:** `DbSet<OrbitChoiceClaim> OrbitChoiceClaims` + the inline mapping from spec §4.3 (unique `(GameParametersId, Choice)`, index on `PlayerId`, `HasMaxLength(20)` matching `Bet.ExclusiveChoice`).
- [ ] **Step 4:** Generate the migration:
  ```bash
  dotnet ef migrations add AddOrbitFeedbackAdjustments \
    --project src/SpaceStroker.Infrastructure --startup-project src/SpaceStroker.Consumers
  ```
  Expected: one new table, five new columns, two indexes. **Never hand-edit the snapshot.**
- [ ] **Step 5:** `scripts/run_tests.bat` — green (nothing asserts the new columns yet; this proves the model still builds).
- [ ] **Step 6:** Commit `Add the Orbit choice claim and enrolment columns.`

---

## Task 2: Config

**Files:** `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs`, `src/SpaceStroker.Common/spacestrokersettings.json`

- [ ] **Step 1:** `ReEnableLockHours` default `24` → `1`; **update its doc-comment** — it currently justifies 24h by "the one manual cash-out a player gets per day", which is no longer true.
- [ ] **Step 2:** Add `public int ManualCashOutGapMinutes { get; set; } = 60;` with a comment saying `0` disables the gap (mirroring `ReEnableLockHours = 0`).
- [ ] **Step 3:** Set both in `spacestrokersettings.json` under `GeneralSettings:Operator:Token:Orbit` (`:114+`).
- [ ] **Step 4:** Commit `Cut the Orbit cool-off and cash-out gap to one hour.`

---

## Task 3: Error messages

**Files:** `src/SpaceStroker.Common/Resources/ErrorMessages.resx` (+ generated `.Designer.cs`)

- [ ] **Step 1:** Add `OrbitChoiceInvalid` — *"That word does not fit this game. Check the length and use only the characters the game allows."* (clear: the player's own input).
- [ ] **Step 2:** Add `OrbitChoiceAlreadyClaimed` — *"That word is already taken on this game. Pick another one."* (clear: ordinary state, per #121's vague/clear split).
- [ ] **Step 3:** Re-word `OrbitAlreadyCashedOutToday` → *"You have cashed out recently. Please try again shortly."* — the old string says "today", which is a lie at a 1h gap. **Keep the key**, only the value changes.
- [ ] **Step 4:** Delete `OrbitDepositCoolOff` (unused after Task 12).
- [ ] **Step 5:** Commit `Add the Orbit choice messages and retire the deposit cool-off one.`

---

## Task 4: The manual cash-out gap (ONE definition) + partial amount

⚠️ **`HasManualCashOutToday` is duplicated** — `OrbitCashOutReserver.cs:93-102` (the write path) and
`GetOrbitHandler.cs:98-107` (the read the UI greys the button from). Moving only one makes them
disagree. Extract a shared static exactly as `OrbitReEnableLock` already does for the re-enable lock.

**Files:**
- Create: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitManualCashOutGate.cs`
- Modify: `src/SpaceStroker.Infrastructure/Services/Orbit/OrbitCashOutReserver.cs`, `IOrbitCashOutReserver.cs`
- Test: `src/SpaceStroker.Tests/UnitTests/Services/OrbitCashOutReserverTests.cs`

**Interfaces produced:**
`OrbitManualCashOutGate.ResolveNextAllowedAt(context, accounts, settings, playerId, ct) -> Task<DateTime?>`
(null ⇒ allowed now); `IOrbitCashOutReserver.Reserve(playerId, source, enforceLimits, decimal? requestedAmount = null, ct)`.

- [ ] **Step 1: Write the failing tests** in `OrbitCashOutReserverTests`:
  partial within bounds reserves exactly that amount and leaves the remainder; below `MinimumCashOut` ⇒ `BelowMinimum`; above the balance ⇒ `InsufficientFunds`; `null` ⇒ the whole balance; a `Manual` row 10 min ago ⇒ `AlreadyCashedOutToday`; one 2 h ago ⇒ reserved; **`enforceLimits: false` with a requestedAmount ⇒ still takes the WHOLE balance**.
- [ ] **Step 2:** Run `scripts/run_tests.bat` — expect failures (signature does not exist).
- [ ] **Step 3:** Write `OrbitManualCashOutGate` — same shape as `OrbitReEnableLock.ResolveLockedUntil`: read `ManualCashOutGapMinutes`, `<= 0` ⇒ null, else the newest pool-scoped `CashOut` row with `Source == Manual` newer than `UtcNow - gap`, returning `row.CreatedDate + gap`.
- [ ] **Step 4:** Add `decimal? requestedAmount = null` to the interface + implementation; apply spec §6.6's ordering (`available` → `amount = requestedAmount ?? available` → bounds only under `enforceLimits`), replace `HasManualCashOutToday` with the shared gate, and **delete the private duplicate**.
- [ ] **Step 5:** Run `scripts/run_tests.bat` — green. Fix every existing call site (`UpdateOrbitSettingsHandler`, the sweep job, settlement) — they pass positionally today, so the new optional param must not shift `cancellationToken`. ⚠️ **Verify each call site compiles with the token still bound to the token parameter.**
- [ ] **Step 6:** Commit `Give the Orbit cash-out an amount and a rolling gap.`

---

## Task 5: Cash-out request plumbing

**Files:** `Handlers/Orbit/RequestOrbitCashOut/{RequestOrbitCashOutRequest.cs,RequestOrbitCashOutHandler.cs}`

- [ ] **Step 1:** Add `public decimal? Amount { get; set; }` to `RequestOrbitCashOutRequestBody`.
- [ ] **Step 2:** ⚠️ **Correct the record's doc-comment** — it says *"There is deliberately NO destination and NO amount in this contract"*. There is still no destination; there is now an amount. Correct `OrbitCashOutDto.Balance`'s *"Always 0 on success"* too — a partial leaves a remainder.
- [ ] **Step 3:** Pass `body.Amount` into `Reserve(..., enforceLimits: true, body.Amount, ct)`. **The fresh `orbit-cashout` ed25519 signature check stays exactly as it is.**
- [ ] **Step 4:** Return the **remaining** balance in the DTO.
- [ ] **Step 5:** `scripts/run_tests.bat` — green. Commit `Accept a partial amount on the Orbit cash-out request.`

---

## Task 6: The reservation on the bet path

**Files:** `Handlers/Games/RegisterBet/RegisterBetHandler.cs` (the `isFreeTextGame` block, `:259-268`)
**Test:** `src/SpaceStroker.Tests/UnitTests/Handlers/Games/RegisterBetTests.cs`

- [ ] **Step 1: Write the failing tests:** word claimed by **another** player on this type ⇒ rejected + refunded; claimed by **the caller** ⇒ allowed; claimed on a **different** type ⇒ allowed; **preset** type ⇒ never consulted; anonymous free bet (`PlayerId == null`) vs a claim ⇒ rejected (it is not the claimant).
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** Implement spec §6.1 verbatim, inside the existing `isFreeTextGame` block, after the per-round `choiceTaken` check. Use `RefundInvalidBet(ErrorMessages.ChoiceAlreadyTaken)` — **the same message**, deliberately indistinguishable.
  ⚠️ Write the player comparison explicitly (`_bet.PlayerId == null || x.PlayerId != _bet.PlayerId`), following the null discipline the `SecondBetNeedsAnotherPlayer` block documents at `:246-250` — EF's null semantics differ between the in-memory and Npgsql providers.
- [ ] **Step 4:** Run — green.
- [ ] **Step 5:** Commit `Reject a bet on a word another player has reserved.`

---

## Task 7: Claim lifecycle in the settings handler

**Files:**
- Create: `Infrastructure/Services/Orbit/{IOrbitChoiceClaimService,OrbitChoiceClaimService}.cs`
- Modify: `Handlers/Orbit/UpdateOrbitSettings/{UpdateOrbitSettingsRequest.cs,UpdateOrbitSettingsHandler.cs}`
- Test: `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/UpdateOrbitSettingsTests.cs`

**Interfaces produced:** `IOrbitChoiceClaimService.{ValidateForType(choice, parameters) -> bool,
TryClaim(playerId, gameParametersId, choice, ct) -> Task<bool>, Release(playerId, ct) -> Task,
ReleaseFor(playerId, gameParametersId, ct) -> Task}`.

- [ ] **Step 1: Write the failing tests** — the eleven cases in spec §11 row 2.
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** Add the body fields (`Choice`, `ClearChoice`, `Stopped`, the three notify flags).
- [ ] **Step 4:** Write `OrbitChoiceClaimService`. `ValidateForType` must reuse the predicate `BetDomainService.ChoiceIsValidForGame` applies, so a claimable word can never be one the bet path rejects. `TryClaim` inserts and returns `false` on `DbUpdateException` whose inner is a **23505** unique violation (the same shape `GameService.InsertBet` maps for `ChoiceAlreadyTaken`).
- [ ] **Step 5:** Wire the handler per spec §6.7 **in that order**: validate → preset⇒store-only → free-text⇒claim → switch claims new-before-releasing-old → clear/disable releases → enable re-attempts (refuse **and do not enable** if taken) → `Stopped` + flags are plain writes needing **no signature**.
- [ ] **Step 6:** ⚠️ Make `ConsiderTheCurrentRound` not fire when the patch leaves the player `Stopped`.
- [ ] **Step 7:** Run — green. Commit `Own the Orbit word claim from the settings patch.`

---

## Task 8: Placement — use the word, skip when stopped

**Files:** `Consumers/OrbitBetPlacement/PlaceOrbitBetConsumer.cs` (`:193-210`),
`Services/Orbit/OrbitBetPlacementService.cs` (`ResolveCandidates`, `:61-79`)
**Tests:** `UnitTests/Consumers/PlaceOrbitBetConsumerTests.cs`, `UnitTests/Services/OrbitBetPlacementServiceTests.cs`

- [ ] **Step 1: Write the failing tests:** enrolment `Choice` set ⇒ that exact choice is bet and `DeriveChoice` is **not** called; `Choice` null ⇒ derived as today; `Choice` set but already taken this round ⇒ the player is **skipped**, not retried; `Stopped` ⇒ excluded from candidates; not stopped ⇒ included.
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** `ResolveCandidates`: add `&& !enrollment.Stopped` to the first `Where`.
- [ ] **Step 4:** The consumer: if `Choice` is set, use it verbatim and **skip the `MaxChoiceAttempts` loop entirely**; else keep today's loop. ⚠️ **Do not touch `DeriveSeed`** — `Bet.Seed` stays `HMAC(ServerSeed, "orbit-seed|playerId")` with `Nonce = 0` either way, so offline verification is unaffected.
- [ ] **Step 5:** Run — green. Commit `Bet the player's own word and skip a stopped Orbit.`

---

## Task 9: `GET /orbit` — new fields

**Files:** `Handlers/Orbit/GetOrbit/{GetOrbitRequest.cs,GetOrbitHandler.cs}`

- [ ] **Step 1:** Add `Choice`, `ChoiceIsReserved`, `Stopped`, the three notify flags, `NextManualCashOutAt` to `OrbitDto`.
- [ ] **Step 2:** **Remove `CanCashOutToday`** and its private `HasManualCashOutToday` (`:98-107`) — populate `NextManualCashOutAt` from `OrbitManualCashOutGate` (Task 4) so read and write share one definition. Leaving both would let them disagree.
- [ ] **Step 3:** `ChoiceIsReserved` = a claim row exists for this player on the selected type (false on preset types even when `Choice` is set).
- [ ] **Step 4:** `scripts/run_tests.bat` — green. Commit `Expose the Orbit word, stop state and notify flags.`

---

## Task 10: The bet log endpoint

**Files:**
- Create: `Handlers/Orbit/GetOrbitBets/{GetOrbitBetsRequest.cs,GetOrbitBetsHandler.cs}`
- Modify: `Api/Controllers/OrbitController.cs` (routes `:33-40`, add after `Get()` `:61`), `Common/spacestrokersettings.json` (rules `:26-61`)
- Test: `UnitTests/Handlers/Orbit/GetOrbitBetsTests.cs`, `UnitTests/Api/RateLimitSettingsTests.cs`

**Interfaces produced:** `OrbitBetLogDto { DateTime PlacedAt; string GameTypeName; string Choice;
decimal Stake; OrbitBetOutcome Outcome; decimal? Payout; string TokenLabel; }`,
`enum OrbitBetOutcome { Won, Lost, Refunded, Pending }`.

- [ ] **Step 1: Write the failing tests:** pairing on **`BetId`**; each of `Won`/`Lost`/`Refunded`/`Pending`; **a `BetDebit` with `BetId == null` is excluded**; another player's rows never returned; `pageSize` clamps to 30; page 999 ⇒ empty page, not an error; rows scoped to the current pool.
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** Implement the query from spec §6.4.3. ⚠️ **Pair on `BetId`, not `GameId`** — `GameId` is set on `BetDebit` rows only (it *is* the placement claim); `WinCredit`/`Refund` carry `BetId`. Use `PaginatedList<T>.CreateAsync`.
- [ ] **Step 4:** Add the controller action (session-gated via `OrbitSessionAccessor`, exactly like `Get()`), `[HttpOptions("bets")]`, and the `"*:/orbit/bets"` rate rule.
- [ ] **Step 5:** ⚠️ Update `RateLimitSettingsTests`' **exact-set** assertion for the new rule — **leave its guard that the bet path never gains a rule untouched**.
- [ ] **Step 6:** Run — green. Commit `Serve the player their own Orbit bet history.`

---

## Task 11: Notifications

**Files:** `Consumers/OrbitCashOut/OrbitCashOutConsumer.cs`,
`Consumers/OrbitNotification/OrbitNotificationConsumer.cs`,
`Services/Orbit/OrbitBetSettlementService.cs`
**Tests:** the three matching `UnitTests/Consumers/*` + `UnitTests/Services/OrbitBetSettlementServiceTests.cs`

- [ ] **Step 1: Write the failing tests:** cash-out send gated on `NotifyOnCashOut`; a **failed/reversed** transfer still sends nothing either way; win Telegram gated on `NotifyOnWin` while the **SignalR push stays ungated**; zero-out fires once per episode, clears on recovery, never fires when `OrbitBetsPlaced == 0`.
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** Gate the two existing sends on their flags.
- [ ] **Step 4:** Add the zero-out evaluation in `OrbitBetSettlementService`, in the same settle-time pass as `EvaluateFloor` (so it acts on a **resolved** bet — the #123 rule), using `ZeroOutNotifiedAt` per spec §6.5.2.
- [ ] **Step 5:** Run — green. Commit `Let the player choose what Telegram tells them.`

---

## Task 12: Deposits during the cool-off

**Files:** `Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` (`:87-102`)
**Test:** `UnitTests/Handlers/Orbit/CreditOrbitDepositTests.cs`

- [ ] **Step 1: Write the failing tests:** a player inside the cool-off is **credited**; a player who never enabled Orbit is still refused with `OrbitNotEnabled`; a replayed signature still returns the balance and writes nothing.
- [ ] **Step 2:** Run — expect failures.
- [ ] **Step 3:** Delete the `OrbitReEnableLock.ResolveLockedUntil` block and its refusal. **Keep** the enable-first deferral below it, its position **after** the idempotency check, the daily cap, the float cap and the transfer verification. ⚠️ **`OrbitReEnableLock` itself stays** — it still gates *re-enabling*.
- [ ] **Step 4:** Clear `ZeroOutNotifiedAt` on a credited deposit (Task 11's other half).
- [ ] **Step 5:** Run — green. Commit `Accept a deposit while the Orbit cool-off runs.`

---

## Task 13: The mandatory integration test

**Files:** Create `src/SpaceStroker.Tests.Integration/OrbitFeedbackFlowTests.cs`

⚠️ Level is **`Feature`**, so this is mandatory — and it is the **only** place the claim's unique-index
violation, the ledger's `ExecuteUpdateAsync` writes and the placement claim execute at all.

- [ ] **Step 1:** Follow the shipped `OrbitFlowTests` / `OrbitAutoBetFlowTests` harness pattern (real API + Consumers against Testcontainers Postgres/Redis).
- [ ] **Step 2:** Drive the happy path end to end: enable Orbit **with a word** → deposit → Orbit places a bet **on that word** → a second player's bet on that word is **rejected and refunded** → the round settles → `GET /orbit/bets` lists the bet with its result → a **partial** cash-out leaves the ledger correct.
- [ ] **Step 3:** `scripts/run_integration_tests.bat` — green (Docker required).
- [ ] **Step 4:** ⚠️ Re-run the existing Orbit suites (`OrbitFlowTests`, `OrbitAutoBetFlowTests`, `OrbitNotificationsFlowTests`) — they seed enrolments and may need the new defaults.
- [ ] **Step 5:** Commit `Cover the Orbit feedback happy path end to end.`

---

## Task 14: Frontend types + service

**Files:** `src/types/orbit.ts`, `src/services/orbitService.ts`

- [ ] **Step 1:** `OrbitState`: add `choice: string | null`, `choiceIsReserved: boolean`, `stopped: boolean`, the three notify booleans, `nextManualCashOutAt: string | null`; **remove `canCashOutToday`**.
- [ ] **Step 2:** `OrbitSettingsPatch`: add `choice?: string | null`, `clearChoice?: boolean`, `stopped?: boolean`, the three flags.
- [ ] **Step 3:** Add `OrbitBetLogRow` + `OrbitBetOutcome` and a `PaginatedList`-shaped response type.
- [ ] **Step 4:** `orbitService.getOrbitBets(page, size)` → `GET /orbit/bets`; add the optional amount to the cash-out call.
- [ ] **Step 5:** `yarn lint` — clean. Commit.

---

## Task 15: `useOrbit`

**Files:** `src/hooks/useOrbit.ts`

- [ ] **Step 1:** Surface the new state; keep module-level state and **per-wallet re-scoping** intact (switching wallets must not leave the previous wallet's word or rows on screen).
- [ ] **Step 2:** Add `saveChoice`/`clearChoice`/`setStopped`/`setNotify`, all through the existing `PATCH /orbit/settings`, all surfacing **the server's own message** on failure (#123's fix).
- [ ] **Step 3:** `cashOut(amount?)` — omitted ⇒ MAX, so today's call shape still works.
- [ ] **Step 4:** `loadBets(page)` with its own loading/error state — **not** folded into the `GET /orbit` poll.
- [ ] **Step 5:** `yarn lint` — clean. Commit.

---

## Task 16: `/orbit` page + the log

**Files:** `src/pages/OrbitPage.vue`, create `src/components/orbit/OrbitBetLog.vue`

⚠️ **3 Claude-design mockups per UI change, signed off by the owner, before writing these components.**

- [ ] **Step 1:** Word field in **Step 1 · Game** (a property of the chosen game; re-validates when the game changes); collapsed summary shows the word.
- [ ] **Step 2:** The three notify toggles inside the existing **Step 4 · Telegram** linked block.
- [ ] **Step 3:** Amount field + **MAX** on the cash-out row — still the **outline** control, never amber (#141 R5).
- [ ] **Step 4:** **STOP** / **RESUME** in the utility strip (not a step — it acts on a running Orbit).
- [ ] **Step 5:** `OrbitBetLog.vue` below the main card: WHEN · GAME · CHOICE · STAKE · RESULT · PAYOUT, newest first, paged. Wide content scrolls **inside its own container**; empty state is a plain line, not an error. No `backdrop-filter`/`blur()`/infinite animation (every phone is `light-graphics`).
- [ ] **Step 6:** `yarn lint` + `yarn build` — clean. Commit.

---

## Task 17: Modal, banner, and the pre-pay guard

**Files:** `src/components/orbit/{OrbitModal,OrbitStatusBanner,OrbitStatusTag}.vue`, `src/hooks/useGame.ts`

- [ ] **Step 1:** STOP surfaced in the compact modal (a mid-round action). The **word stays page-only** — a mis-tap beside the bet lever must never re-point a player's Orbit.
- [ ] **Step 2:** Banner + tag distinguish **`STOPPED`** (deliberate) from **`PAUSED`** (`isPausedForFunds`) at a glance.
- [ ] **Step 3:** `useGame.bet()` pre-pay guard against a reserved word — best-effort, backend authoritative. ⚠️ Copy must **not** name Orbit: reuse the same "that choice is already taken" the per-round guard shows.
- [ ] **Step 4:** `yarn lint` + `yarn build` — clean. Commit.

---

## Task 18: Verify + ship the branch

- [ ] **Step 1:** `scripts/run_tests.bat` — green; record **exactly which** unit tests changed (`constraints.md` §3).
- [ ] **Step 2:** `scripts/run_integration_tests.bat` — green.
- [ ] **Step 3:** `yarn lint` + `yarn build` in the front worktree. ⚠️ **Copy the hub's `yarn.lock` into the front worktree first** — it is gitignored, and without it the build silently boots a **stale** image.
- [ ] **Step 4:** Push all three branches; build the GitHub `compare/main...feature/orbit-feedback-adjustments` links from each repo's **own** `origin` remote (backend and front have different owners).
- [ ] **Step 5:** Deploy to Docker. ⚠️ **#141's stack is currently deployed** from `.worktrees/orbit-ui-overhaul/monkify-backend` — ask Replace/Combine/Skip before touching it. Note that #150's branch already **contains** #141, so Replace here deploys a superset rather than losing that feature's code.
- [ ] **Step 6:** ⚠️ **There is no `rabbitmq` container** — #64 replaced it with the MassTransit Postgres transport. The joint health check is `api`, `consumers`, `database`, `redis` **and** `monkify-front`.

---

## Self-review

**Spec coverage:** §4.1→T1 · §4.2→T1 · §4.3→T1 · §4.4→T1 · §5→T2 · §6.1→T6 · §6.2→T8 · §6.3→T8 ·
§6.4→T10 · §6.5→T11 · §6.6→T4 · §6.7→T7 · §6.8→T12 · §6.9→T9 · §7.1→T15 · §7.2→T16 · §7.3→T17 ·
§7.4→T16 · §7.5→T17 · §11→every task's tests · §11.1→T13. No gap.

**Type consistency:** `OrbitChoiceClaim`, `OrbitBetLogDto`, `OrbitBetOutcome`,
`OrbitManualCashOutGate.ResolveNextAllowedAt`, `IOrbitChoiceClaimService.*` and
`Reserve(..., decimal? requestedAmount, ct)` are used with the same names and signatures in every task
that references them.

**Two additions this plan makes beyond the spec's file list, both to stop a drift the spec implies but
does not name:** `OrbitManualCashOutGate` (because `HasManualCashOutToday` is **duplicated** in the
reserver *and* `GetOrbitHandler`, so a single-sided change would desync the button from the rule) and
`OrbitChoiceClaimService` (so the claim lifecycle is testable without driving the whole settings
handler).
