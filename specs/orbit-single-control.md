# Space Stroker — "Orbit single control" — Implementation Spec

> **Status: Shipped** — merged to `main` on **2026-08-07** via **`feature/orbit-new-ui`** in
> `monkify-backend` and `monkify-front` (front and backend together — the API contract changed).
> Designed in-session with the owner on **2026-08-07**; ⚠️ **no Trello card**. Distilled in
> [`../features/orbit-single-control.md`](../features/orbit-single-control.md).
>
> **Purpose:** Orbit currently has FOUR points of control — `Enabled` (enrolment on/off, where OFF
> force-cashes-out everything and starts an hours-long re-enable cool-off), `Stopped` (the free
> pause), the re-enable lock, and the manual cash-out gap. Live feedback: the flow reads as
> *sign in → enable? → set up → then hit play (to unpause??)*. The owner's ask, verbatim in intent:
> **one point of control.** `Enabled` becomes the only flag and behaves exactly like play/pause;
> cash-outs are their own thing; the only remaining "cool-off" is the manual cash-out gap.

---

## 0. Base branch — read this before anything else

⚠️ **This work is NOT based on `main`.** It continues **`feature/orbit-new-ui`**, which already
carries the Orbit page restructure (#141), the feedback adjustments (#150) and the auto-pick work.
Both repo worktrees for the branch exist at `.worktrees/orbit-ui-overhaul/<repo>`. Front and backend
**must ship together** — the API contract changes (§6.5).

## 1. Summary

One stored flag, **`Enabled`**, with today's *pause* semantics:

- Flipping it **moves no money**, requires **no fresh wallet signature**, has **no cool-off**, and
  may be flipped at any time (self-exclusion excepted).
- `OrbitEnrollment.Stopped` is **dropped** (migration: `Enabled = Enabled AND NOT Stopped`).
- The **forced cash-out on disable** (`ForcedOnDisable`) and the **re-enable lock**
  (`OrbitReEnableLock`, `ReEnableLockHours`, the `COOLING OFF` state) are **deleted**, not hidden.
  Their rationale — "disable is an uncapped withdrawal" — no longer holds once disable stops
  withdrawing.
- "Leave with my money" becomes a plain **manual cash-out** (full by default from the control bar,
  partial from the Money tab), signature-gated and subject to the existing rolling
  `ManualCashOutGapMinutes` — which is now the ONLY cool-off in Orbit, surfaced as a countdown on
  the cash-out control, never as an Orbit *state*.
- The word claim is held **only while Orbit can actually play**: `Enabled` **and** balance ≥ the
  enrolled game's stake (owner's rule, 2026-08-07: "you only keep word ownership while ACTIVELY
  running, with an actual balance and enabled"). The word *preference* (`Choice`) survives
  everything, exactly as today.
- New-player flow: **sign in → set up (game / word / money, any order) → press RUN.** No separate
  "enable" step; the first RUN (or first deposit, parked) creates the enrolment.

### 1.1 What does NOT change

- `ManualCashOutGapMinutes` and `OrbitManualCashOutGate` — untouched, still Manual-only.
- Threshold / Floor auto-cash-outs, and their exemption from the gap.
- Self-exclusion: still disables Orbit and force-cashes-out everything
  (`ForcedOnSelfExclusion`, exempt from limits), still blocks re-enabling while active.
- Deposit crediting mechanics, idempotency, and the "never refuse money that already moved
  on-chain" rule.
- The bet path (`PlaceOrbitBetConsumer`): word resolution, auto-pick opt-in, one-bet-per-player.
- Manual cash-out signing (`orbit-cashout` nonce purpose) for BOTH full and partial amounts.
- Enable still requires a game type and refuses while self-excluded.

## 2. Constraints (MANDATORY)

1. Money movement stays in Consumers (constraints.md §8) — this spec removes a money-moving path,
   it adds none.
2. Config files: mock/placeholder values only.
3. EF config inline in `SpaceStrokerDbContext.OnModelCreating`; migration applied by Consumers.
4. Run `SpaceStroker.Tests` (via `run_tests.bat`) and report changed tests.
5. Integration test rule (AGENTS.md §8): the reworked flow gets a happy-path integration test
   (§10.2).
6. Auth/wallet failures stay vague (constraints.md §6) — unchanged messages except where a check is
   deleted outright.

## 3. Resolved product decisions (owner, 2026-08-07, this session)

| # | Decision |
|---|----------|
| 1 | **Single flag**, named `Enabled`, with pause semantics. `Stopped` is assimilated, not renamed. |
| 2 | **Word claim held only while `Enabled` and funded** (balance ≥ stake). Any other state releases it. The preference is remembered; coming back re-*attempts* the claim (existing machinery). |
| 3 | **Control bar = toggle + cash out**: one RUN/PAUSE toggle plus a CASH OUT button (full manual cash-out). Cashing out everything empties the balance, so Orbit parks itself — "leave" stays one press with no special code path. |
| 4 | **Approach A — real merge**: drop the column, delete the cool-off machinery, migrate. (Rejected: UI-only merge that keeps both flags server-side; keep-as-is.) |
| 5 | The manual cash-out gap is the **only** cool-off and **only** affects manual cash-out timing. A full cash-out is a Manual cash-out: it consumes and respects the gap. This closes the "off/on/off = unlimited withdrawals" loophole the deleted lock existed for — every player-initiated withdrawal now goes through one gate. |

Accepted consequences (called out to the owner):

- Players mid-cool-off at deploy are **freed immediately** (the lock resolver is gone; historical
  `ForcedOnDisable` rows stop mattering).
- "Leave with everything" can be **blocked for up to the gap window** (minutes) after a recent
  manual cash-out — deliberate, and strictly milder than today's hours-long lock.
- Breaking API change: front and backend ship together on this branch.

## 4. Data model

### 4.1 `OrbitEnrollment`

- **Drop `Stopped`.** `Enabled` doc comment rewritten: it is the play/pause switch; flipping it
  moves no money and releases/re-attempts the word claim per §6.2.
- Everything else (`Choice`, `AutoPickChoice`, `CashOutAtThreshold`, `CashOutFloor`, Telegram
  fields, `ZeroOutNotifiedAt`) unchanged.

### 4.2 Migration — `MergeOrbitStoppedIntoEnabled`

1. Data first: `UPDATE "OrbitEnrollments" SET "Enabled" = ("Enabled" AND NOT "Stopped");`
   (via `migrationBuilder.Sql`, before the column drop).
2. Drop column `Stopped`.
3. Remove the `Stopped` property config from `SpaceStrokerDbContext.OnModelCreating`.

No claim-table migration: `OrbitChoiceClaim` is unchanged. Existing claims held by players whose
merged `Enabled` lands `false` are released by a follow-up data statement in the same migration:
`DELETE FROM "OrbitChoiceClaims" WHERE "PlayerId" IN (SELECT "PlayerId" FROM "OrbitEnrollments" WHERE NOT "Enabled");`
— so every parked player satisfies the new invariant (§6.2) from the first minute. The one
straggler — enabled, un-stopped, but already dry — cannot be cleaned in SQL (funded-ness lives in
the ledger sum); those converge on their next debit or park, and are rare because the front parks
dry Orbits on page load.

### 4.3 `OrbitCashOutSource`

`ForcedOnDisable = 3` **stays in the enum** (historical rows reference it; ledger history must keep
explaining itself) with its doc comment rewritten to: *legacy — written until the single-control
merge; no writer remains.* No code path may write it after this card.

## 5. Configuration

- **Remove `ReEnableLockHours`** from `OrbitSettings` and from every `spacestrokersettings.json`
  (mock values). `ManualCashOutGapMinutes` stays.

## 6. Backend behaviour

### 6.1 `UpdateOrbitSettingsHandler` — the toggle becomes a plain settings write

- **Remove from the request body:** `Stopped`, `Nonce`, `Signature` (the latter two existed only
  for the disable-is-a-withdrawal proof).
- **Delete:** the `isDisabling` proof (`OrbitProof.TryConsume` w/ `OrbitCashOut` purpose), the
  `OrbitReEnableLock.ResolveLockedUntil` refusal on enable, and the post-save
  `_reserver.Reserve(..., ForcedOnDisable, ...)` call.
- **Keep on enable:** game-type-required, self-exclusion refusal, claim re-attempt (§6.2),
  `ConsiderTheCurrentRound` when the result is enabled.
- **Keep on disable:** `_claims.Release(...)` — now justified by §6.2's invariant, not by a
  cash-out.
- The threshold/floor/notification/choice patching logic is untouched.

### 6.2 The word-claim invariant (free-text types only)

> **A player holds their claim iff `Enabled` AND balance ≥ the enrolled type's `RequiredAmount`.**

The preference (`enrollment.Choice`) is never touched by any of this — only the reservation.

Release points (all already-existing or newly hooked):

| Trigger | Where | Note |
|---|---|---|
| Toggle off | `UpdateOrbitSettingsHandler` | existing call, kept |
| `ClearChoice` | `UpdateOrbitSettingsHandler` | existing, unchanged |
| Cash-out debit (ALL sources) leaving balance < stake | `OrbitCashOutReserver.Reserve` | **new** — after the reservation commits, if the remaining ledger balance is below the enrolled type's stake (or no type is enrolled), `_claims.Release(playerId)`. Fixes a pre-existing gap: self-exclusion's forced cash-out never released the claim. |
| Settlement resolving to balance < stake | `OrbitBetSettlementService.EvaluateZeroOut` | **new** — the SAME hook the zero-out notification uses, and for the same documented reason that hook exists at settlement rather than placement: a stake leaves the balance the instant a bet is placed, so releasing at debit would surrender the word EVERY round for a player holding ~1× the stake, then re-claim on the win — a churn window another player could steal the word through, over a bet that was still riding. A bet in flight IS active play; the invariant is evaluated on RESOLVED balances. |

Claim (re-)attempt points:

| Trigger | Where | Behaviour on lost race |
|---|---|---|
| Toggle on, balance ≥ stake | `UpdateOrbitSettingsHandler` (existing re-enable block) | refuse the patch with `OrbitChoiceAlreadyClaimed`, exactly as today |
| Deposit credited, `Enabled`, choice set, no claim held, balance now ≥ stake | `CreditOrbitDepositHandler` | **non-fatal**: the deposit must still credit (money already moved). No claim → the DTO's `ChoiceIsTakenByAnother` / `ChoiceIsReserved` tell the player, and the bet path's existing "taken" guard keeps Orbit off the word. |
| Settlement resolving to balance ≥ stake, choice set, no claim held | `OrbitBetSettlementService.EvaluateZeroOut` (the recovered branch) | **non-fatal** — covers the partial-cash-out-then-win path, where the release above fired while a bet was riding and the win restored a playable balance with no deposit or toggle in between. |

Toggle-on with balance < stake **skips** the claim attempt (nothing to play with — claiming would
violate the invariant); the deposit-credit hook picks it up when money arrives. The front gates RUN
on funds anyway, so this is a stale-client path.

**The explicit word save** (`body.Choice`) honours the same invariant: saved while enabled AND
funded it claims (refusing on a lost race, as today); saved while parked or unfunded it stores the
**preference only** and releases any stale reservation the player still held — a claim must never
outlive the word it reserved, and an unfunded save that claimed would be exactly the indefinite
squat this rule forbids (implementation decision, 2026-08-07).

### 6.3 `CreditOrbitDepositHandler` — gate change

Today: refuse unless `Enabled`, with a carve-out for the disable cool-off. New rule:

- **Accept when an `OrbitEnrollment` row exists** for the player (regardless of `Enabled`). A
  parked Orbit holding a balance is now a first-class state, and a dry player must be able to
  refund while parked.
- **Refuse (same `OrbitNotEnabled` refusal path, message text reworded to "set up Orbit first"
  semantics) when no enrolment exists** — the original concern stands: money must not land in a
  custodial balance whose owner has never seen the Orbit page.
- **Refuse while self-excluded** (`SelfExclusionActive`). New check made necessary by the gate
  change: the old `Enabled` gate blocked this implicitly (self-exclusion force-disables), and the
  enrolment-exists gate alone would let someone who asked to stop fund a parked gambling balance.
- Delete the cool-off carve-out (nothing to carve around).
- Add the claim re-attempt per §6.2.

### 6.4 Planner / settlement eligibility

- `OrbitBetPlacementService`: eligibility becomes `enrollment.Enabled` (drop `&& !Stopped`).
- `OrbitBetSettlementService` (zero-out notification guard): drop the `Stopped` term.

### 6.5 `GET /orbit` — DTO changes

- **Drop:** `Stopped`, `EnableLockedUntil`.
- **Keep:** `NextManualCashOutAt` (the countdown the cash-out controls render), `Enabled`,
  `ChoiceIsReserved`, `ChoiceIsTakenByAnother`, everything else.
- `GetOrbitHandler`: delete the `OrbitReEnableLock` resolution.

### 6.6 Self-exclusion — unchanged, now cleaner

`SetSelfExclusionHandler.StopOrbitAndReturnTheMoney` already sets `Enabled = false` directly and
reserves `ForcedOnSelfExclusion` uncapped. Unchanged — and via §6.2's reserver hook it now also
releases the claim, which it previously leaked.

### 6.7 Deletions

- `OrbitReEnableLock.cs` — delete the file.
- Every remaining reference to `ReEnableLockHours`, `EnableLockedUntil`, `ForcedOnDisable`
  (as a writer), `ErrorMessages.OrbitEnableLocked`.

## 7. Frontend behaviour

### 7.1 `types/orbit.ts`

- `OrbitStatus` collapses to: `'signed-out' | 'off' | 'unfunded' | 'running'`.
  `cooling-off` and `stopped` die; `paused` (the ran-dry derivation) folds into `off` — with one
  flag there is no stored difference, and the *labels* (RESUME vs RUN, "add money first") are
  derived exactly as the control bar already derives them (`hasRunBefore`, `hasFunds`).
- `OrbitSettingsPatch`: drop `stopped`, `nonce`, `signature`.
- `OrbitState`: drop `stopped`, `enableLockedUntil`.

### 7.2 `hooks/useOrbit.ts`

- `updateSettings`: delete the whole `isDisabling` block — the nonce fetch, the cash-out message
  signing, the deeplink third-press handling, `lastForcedCashOut`. The toggle is a plain PATCH.
- `orbitStatus`: `signed-out` → `off` (`!enabled`) → `unfunded` (enabled, can't cover stake) →
  `running`.
- **Auto-park keeps its #150 semantics with the new flag:** the watcher that today PATCHes
  `{ stopped: true }` when a running Orbit can no longer play now PATCHes `{ enabled: false }`.
  Same guard rails (condition self-falsifies after the patch; a refused PATCH leaves a derived
  paused view). A deposit must never restart betting by itself — restarting is always a RUN press.
- Enrol-on-deposit: the parked enrolment is created with `{ enabled: false }` (today:
  `{ enabled: true, stopped: true }`), legal because deposits no longer require `Enabled` (§6.3).
- Delete `enableLockedUntil` / `enableLockedLabel` / the cool-off deposit error branch
  ("Deposits are paused until your Orbit cool-off ends.").
- The dedup key/localStorage for the paused toast stays keyed off the park event.
- `markHandled` (owner feedback, 2026-08-07, post-deploy): every op that reports through
  `lastError`/`historyError` (deposit, recover, cash-out, `updateSettings`, history, the Telegram
  trio) marks its error `handled` in the catch — the shared axios interceptor toasts the RAW
  status line 200 ms later for any unflagged rejection, so RUN on a taken word showed the proper
  inline refusal AND a "Request failed with status code 400" toast.

### 7.3 `OrbitControlBar.vue` — trio → duo

- **RUN/PAUSE toggle** (one button): `off` w/ setup blockers → disabled with the existing
  "Pick a game / Pick a word / Add money first" notes; `off` ready → RUN (RESUME once
  `hasRunBefore`); `running` → PAUSE ("Pause automated mode"). Emits `toggle`.
- Palette (owner feedback, 2026-08-07, post-deploy): RUN/RESUME is always the **live green** of the
  modal transport's RUN — the amber "primary" first-run face was cut; PAUSE keeps the red;
  CASH OUT's outline is **neutral ink (white)**, not red. Trio palette = green / red / neutral.
- Gap note (owner feedback, 2026-08-07, post-deploy): the static "HH:MM UTC" stamp became a **live
  countdown** — `Cashout cool off — 12:34` (bare remaining time, no clock beside it) — off a shared
  1 s ticker in `useOrbit` that only runs while a gap is pending. The same ticker drives the shared
  `isWithinCashOutGap`, so the bar's CASH OUT and the MONEY tab's controls **re-enable themselves**
  the second the gap elapses (no reload). Same phrase on both surfaces. Server stays authoritative.
- **CASH OUT** (replaces TURN ORBIT OFF): full-balance manual cash-out. Disabled when balance is 0
  ("Nothing to cash out") or inside the gap (note shows the `nextManualCashOutAt` countdown —
  the old `COOLING OFF` chip's information moves HERE, onto the money control it actually
  gates). Emits `cash-out`. Keeps the outline styling of the old third button.
- All `cooling-off` arms and the `lockedLabel` prop die.

### 7.4 `OrbitPage.vue`

- `onRun`/`onStop` merge into `onToggle` (plain `updateSettings({ enabled: !enabled })`).
- `onTurnOff` becomes `onCashOut`: the existing manual cash-out flow (nonce + `orbit-cashout`
  signature) with `amount = balance`. Reuses `useOrbit.cashOut` — the Money tab's partial
  cash-out and this button share one path, one gate, one signature UX.
- Delete the cool-off banner/panel state (`lastForcedCashOut` "SENT BACK" cell included).

### 7.5 Status surfaces

- `OrbitStatusTag` / `OrbitStatusBanner`: drop `COOLING OFF` and `STOPPED`; render
  `OFF` / `PAUSED` (off with a balance or a played history — wording per tag) / `RUNNING`.
- `OrbitLauncher`: attention dot logic drops the `enableLockedUntil` guard; aria strings updated.
- `OrbitModal.vue`: same collapse; and (owner feedback, 2026-08-07, post-deploy) the panel is
  reviewed as "the small simplified version of the /orbit page":
  - **Switch guard**: opened beside a round that is NOT the game Orbit is pointed at — **running
    or parked** (owner's correction: a parked Orbit on Cosmic Sprint opened beside Beast Mode must
    also get "just a message", not Cosmic Sprint's setup) — the body is replaced by a guard.
    Running: "Orbit is busy running *A*; pause first, then set it up" — the primary press does
    both in ONE PATCH (`{ enabled: false, gameParametersId: B }`). Parked: "Orbit is set up for
    *A*" — the press just switches (`{ gameParametersId: B }`). The switch also folds in
    `clearChoice: true` when the saved word does not fit *B* (`choiceFitsGame`, shared with
    `wordFits`) — left behind it kept `hasPlayableChoice` true for a word the backend would refuse.
    Either way it drops into the normal body pointed at *B*; "Keep *A*" dismisses for that open
    (re-arms on reopen / route change).
  - **Game picker**: the "Open Orbit settings" hand-off block (no game saved, no contextual game)
    is now a picker + confirm. Confirm saves `gameParametersId` (creates the enrolment) and
    `router.push`es the game page **without closing the panel** — the header-owned singleton
    survives navigation, which is the "redirected with the modal auto opened" flow.
  - **Auto-point**: a fresh Orbit (no game ever saved) opened beside a playable round is silently
    pointed at it on open — the same preference-only PATCH the GAME tab's `commitGame` sends.
    Creates the enrolment so ADD MONEY / the word work immediately; never fires once ANY game is
    saved.
  - **Word gates the money**: ADD MONEY and AUTO CASHOUT render dimmed + disabled
    (`.orbit-block--locked`) until `hasPlayableChoice` (word saved or auto-pick on); RUN's sub-note
    names the missing step. Word validations mirror the page's GAME tab: a refused word KEEPS its
    letters and wears TAKEN + the inline reason (no more field reset), `choiceIsTakenByAnother`
    drives the saved-word TAKEN state. The tab's third state (`savedWordUnreserved` and its "still
    free but not reserved" sentence) is deliberately NOT ported (owner: the panel just keeps the
    green VALID chip), and word saves emit NO success notice ("Orbit will play X." cut — the VALID
    chip is the report).
  - **Preset types show ONLY the picker + auto-pick checkbox** (owner's correction: Beast Mode
    rendered the picker AND six cells still spelling the previous game's word — the cells were
    missing the GAME tab's `v-else`). Preset validity is MEMBERSHIP, not length/alphabet (a
    six-letter BANANA must not wear VALID beside Beast Mode's runner list); a stale word left by a
    game switch gets an inline "isn't one of this game's picks" hint. The auto-pick checkbox
    adopts the GAME tab's rule: never disabled by a typed word — ticking it clears the word.
  - **Transport parity**: `canRun` gains the page's funds gate (`balance >= stake` of
    `activeGameType ?? currentGame`) — RUN with an empty balance used to "succeed" into an
    immediate auto-park; RUN's sub is now the blocker text (game → word → money), same order as
    the control bar's `setupBlocker`.

### 7.6 Copy

- `GuideOrbit.vue`: rewrite the "TURNING IT OFF SENDS THE MONEY BACK" card → pausing is free and
  instant, money leaves ONLY via cash-out (yours or a stop rule), manual cash-outs have a short
  gap between them. Sweep How-it-works/legal surfaces for "cool-off"/"cooling off" mentions of the
  re-enable lock (the self-exclusion cool-off is a different feature and stays).

## 8. Flow

```
                    setup incomplete            ready (game+word+funds)
 sign in ──► OFF ──[RUN disabled+reason]   OFF ──RUN──► RUNNING ──PAUSE──► OFF
             │                                            │   ▲
             │  deposit (allowed while parked)            │   └─ RUN (re-claims word)
             │                                   runs dry / balance < stake
             │                                            ▼
             └────────────────────────────────── OFF (auto-park; deposit never auto-resumes)

 CASH OUT (bar: full · Money tab: partial) — signature + rolling gap; independent of the toggle.
 Full cash-out ⇒ balance 0 ⇒ auto-park ⇒ "left Orbit", claim released, word preference kept.
```

## 9. Out of scope

- Any change to stop rules (threshold/floor) semantics or the Telegram notifications.
- Preset-type claim behaviour (still claimless).
- Renaming `Enabled` in the API (it keeps its name; only `Stopped` dies).
- The Recover tab flow (only its copy if it mentions the deleted cool-off).

## 10. Tests

### 10.1 Unit (`SpaceStroker.Tests`, via `run_tests.bat`)

- `UpdateOrbitSettings` tests: delete lock-refusal and forced-cash-out-on-disable cases; add
  toggle-moves-no-money, no-signature-needed, claim released on disable, claim re-attempt on
  enable honouring the funded guard.
- `CreditOrbitDepositTests`: enabled-gate cases become enrolment-gate cases; cool-off carve-out
  cases die; new claim re-attempt (incl. non-fatal lost race).
- `PlaceOrbitBetConsumerTests` / `OrbitBetSettlementServiceTests`: `Stopped` arms collapse to
  `Enabled`; new release-on-low-balance coverage in placement and reserver.
- `OrbitReEnableLock` tests: deleted.

### 10.2 Integration (`SpaceStroker.Tests.Integration`, Testcontainers)

One new happy-path test for the reworked flow: enrol → deposit → RUN (claim held) → PAUSE (no
money moves, balance intact, claim released) → RUN again (claim re-acquired) → full manual
cash-out (gap consumed, balance 0, claim released, enrolment parked, word preference intact).
Adjust `OrbitFeedbackFlowTests` / `OrbitAutoPickChoiceFlowTests` where they assert `Stopped` or
the cool-off.

## 11. File checklist

**monkify-backend** (`feature/orbit-new-ui`)

- `Domain/Orbit/Entities/OrbitEnrollment.cs` — drop `Stopped`, rewrite comments
- `Domain/Configs/Entities/OrbitSettings.cs` — drop `ReEnableLockHours`
- `Domain/Orbit/ValueObjects/OrbitCashOutSource.cs` — legacy-mark `ForcedOnDisable`
- `Infrastructure/Handlers/Orbit/UpdateOrbitSettings/*` — §6.1
- `Infrastructure/Handlers/Orbit/GetOrbit/*` — §6.5
- `Infrastructure/Handlers/Orbit/CreditOrbitDeposit/*` — §6.3
- `Infrastructure/Services/Orbit/OrbitReEnableLock.cs` — DELETE
- `Infrastructure/Services/Orbit/OrbitCashOutReserver.cs` — §6.2 release hook
- `Infrastructure/Services/Orbit/OrbitBetPlacementService.cs` — §6.4
- `Infrastructure/Services/Orbit/OrbitBetSettlementService.cs` — §6.4 + §6.2 settlement release/re-claim
- `Infrastructure/Data/Context/SpaceStrokerDbContext.cs` — drop `Stopped` config
- `Infrastructure/Migrations/` — new `MergeOrbitStoppedIntoEnabled` (§4.2)
- `Common/spacestrokersettings.json` (+ per-project copies) — drop `ReEnableLockHours`
- error messages: delete `OrbitEnableLocked`; reword the no-enrolment deposit refusal
- tests per §10

**monkify-front** (`feature/orbit-new-ui`)

- `types/orbit.ts`, `hooks/useOrbit.ts`, `hooks/useOrbitNotifications.ts`
- `pages/OrbitPage.vue`
- `components/orbit/OrbitControlBar.vue`, `OrbitStatusTag.vue`, `OrbitStatusBanner.vue`,
  `OrbitLauncher.vue`, `OrbitModal.vue`
- `components/orbit/tabs/OrbitMoneyTab.vue` (parked enrol), `OrbitRecoverTab.vue` (copy only)
- `components/guide/sections/GuideOrbit.vue` (copy)
- `assets/styles/arcade.css` — retire `--off`/cooling styles the duo no longer uses
