# Feature — Orbit single control

> Status: **shipped.** Authoritative spec: [`../specs/orbit-single-control.md`](../specs/orbit-single-control.md)
> (merged via `feature/orbit-new-ui` on 2026-08-07). ⚠️ **No Trello card** — designed and shipped
> in-session with the owner, directly off the branch. Builds on
> [`orbit-feedback-adjustments.md`](orbit-feedback-adjustments.md) (#150, which added `Stopped`)
> and the `feature/orbit-new-ui` UI line ([`orbit-ui-overhaul.md`](orbit-ui-overhaul.md) /
> `specs/orbit-page-restructure.md`). Front and backend shipped **together** — the API contract
> changed.

## What it does

Orbit had FOUR points of control — `Enabled` (where OFF force-cashed-out everything and started an
hours-long re-enable cool-off), `Stopped` (the free pause from #150), the re-enable lock, and the
manual cash-out gap — and live testing read the flow as *"sign in → enable? → set up → then hit
play (to unpause??)"*. This card collapses them to **one**: `Enabled` is the only flag and behaves
exactly like play/pause; cash-outs are their own thing; the **only** cool-off left in Orbit is the
rolling manual cash-out gap.

## Core rules

### One flag

- **`OrbitEnrollment.Stopped` is dropped** (migration `MergeOrbitStoppedIntoEnabled`:
  `Enabled = Enabled AND NOT Stopped`, then the column goes). `Enabled` IS the pause: flipping it
  **moves no money**, needs **no wallet signature** (the toggle request lost `Nonce`/`Signature`),
  has **no cool-off**, and can be flipped any time (self-exclusion excepted).
- **The forced cash-out on disable and the re-enable lock are deleted, not hidden** —
  `OrbitReEnableLock`, `ReEnableLockHours`, the `COOLING OFF` state, and every `ForcedOnDisable`
  writer. Their rationale ("disable is an uncapped withdrawal") died the moment disable stopped
  withdrawing. `OrbitCashOutSource.ForcedOnDisable = 3` **stays in the enum** — historical ledger
  rows must keep explaining themselves — with a *legacy, no writer remains* doc comment.
- Players mid-cool-off at deploy were **freed immediately**; existing claims of players whose
  merged `Enabled` landed `false` were released in the same migration.
- "Leave with my money" is now a plain **manual cash-out** (full from the control bar, partial from
  the Money tab), signature-gated and subject to `ManualCashOutGapMinutes`. ⚠️ A full cash-out IS a
  `Manual` cash-out — it consumes and respects the gap — which is what closes the "off/on/off =
  unlimited withdrawals" loophole the deleted lock existed for.
- **Deposits are gated on the enrolment existing** (a game pick creates the row), not on `Enabled`:
  a **parked Orbit takes deposits** and waits for RUN. A deposit **never** starts play (#150's
  auto-start complaint cannot recur — only RUN sets `Enabled`); the **auto-park** watcher persists
  `Enabled = false` the moment Orbit can no longer play (dry, or nothing to bet on).
- Self-exclusion is untouched: still disables Orbit, still force-cashes-out
  (`ForcedOnSelfExclusion`, exempt from limits), still blocks re-enable while active.

### The word claim follows playability

- The free-text word claim is held **only while Orbit can actually play**: `Enabled` **and**
  balance ≥ the enrolled game's stake (owner's rule: "you only keep word ownership while ACTIVELY
  running"). Pausing, running dry, or cashing out to zero **releases** the claim; the *preference*
  (`Choice`) survives everything, and RUN re-*attempts* the claim through the existing machinery.
- The payload grew two flags the front now renders: `choiceIsReserved` (do I hold the claim right
  now) and `choiceIsTakenByAnother` (someone else does — definite, not derived).
- **Orbit never bets a word the player did not opt into**: a null `Choice` no longer implies
  "derive one"; the explicit `AutoPickChoice` opt-in is required (word and opt-in are mutually
  exclusive — saving one clears the other).

### The front: one toggle + a money door

- `OrbitControlBar` went **trio → duo**: ONE RUN/PAUSE toggle (RUN/RESUME wears the live green on
  every off state — no amber first-run face; PAUSE wears the red) + a **neutral-outline CASH OUT**.
  A dead RUN always states its blocker (`Pick a game / Pick a word / Add money first`).
- `orbitStatus` is ONE shared derivation with four values —
  `signed-out → off → unfunded → running` — and every surface (page header, banner, tag, modal)
  reads it; `COOLING OFF` and `STOPPED` are gone.
- The manual gap surfaces as a **live countdown** on the cash-out controls
  (`Cashout cool off — 12:34`, a shared 1 s ticker in `useOrbit` that only runs while a gap is
  pending); the controls **re-enable themselves** the second it elapses. Never an Orbit *state*.

### The modal is the small `/orbit` page (post-deploy owner review, same day)

- **Switch guard:** opened beside a round that is NOT the game Orbit is pointed at — running or
  parked — the panel body is replaced by a guard naming both games. Running: "pause first, then set
  it up" (one PATCH does both). Parked: the press just switches. The switch also **clears a saved
  word that does not fit the new game** (`choiceFitsGame`, shared with the field validation) —
  left behind it kept `hasPlayableChoice` true for a word the backend would refuse.
- **Game picker:** the old "Open Orbit settings" hand-off is a picker + confirm; confirming saves
  the game (creates the enrolment) and rides the **still-open** panel to that game's page. A fresh
  Orbit opened beside a round is silently **auto-pointed** at it (preference-only PATCH).
- **The word gates the money:** ADD MONEY and the stop rules render dimmed + dead until
  `hasPlayableChoice`. Preset types show **only** the picker + auto-pick checkbox (the letter
  cells were rendering alongside — a missing `v-else`), and preset validity is **membership**, not
  length/alphabet (BANANA must not wear VALID beside Beast Mode's runner list).
- **One quieter error path:** every `useOrbit` op that reports through `lastError` marks its error
  `handled`, so the shared axios interceptor no longer stacks a raw
  "Request failed with status code 400" toast on top of the inline refusal.

## Key code touchpoints

**`monkify-backend`** (all on `feature/orbit-new-ui`)

- `OrbitEnrollment.cs` — `Stopped` gone; migration
  `SpaceStroker.Infrastructure/Migrations/*_MergeOrbitStoppedIntoEnabled.cs` (data-first, then
  column drop + claim release; applied by Consumers).
- `UpdateOrbitSettingsHandler` — plain settings write: no proof, no lock check, no forced cash-out;
  releases/re-attempts the word claim per the playability rule.
- Deleted: `OrbitReEnableLock`, `ReEnableLockHours` (config + every `spacestrokersettings.json`),
  the cool-off deposit refusal. `CreditOrbitDepositHandler` gates on enrolment + self-exclusion.
- Claim lifecycle — `OrbitBetSettlementService` / cash-out path release the claim when the balance
  can no longer cover the stake; `GetOrbitHandler` → `choiceIsReserved` / `choiceIsTakenByAnother`.
- Deposit amount is **read off the chain** by signature (recovery needs no amount).
- Tests — `SpaceStroker.Tests` green (853/853 at ship); integration coverage in
  `SpaceStroker.Tests.Integration` covers the single-control flow end to end (rule 8).

**`monkify-front`** (same branch)

- `src/hooks/useOrbit.ts` — signature-less toggle, auto-park PATCHes `{ enabled: false }`,
  `orbitStatus`, the gap ticker + `nextManualCashOutLabel` / `isWithinCashOutGap`, `markHandled`.
- `src/components/orbit/OrbitControlBar.vue` — the duo. `src/pages/OrbitPage.vue` — `onToggle` /
  `onCashOut`; cool-off banner and `lastForcedCashOut` surfaces deleted.
- `src/components/orbit/OrbitModal.vue` — switch guard, game picker + ride-along navigation,
  auto-point, word gate, preset picker-only + membership validity, GAME-tab word validations
  (TAKEN keeps its letters), funds-gated RUN with blocker sub-notes.
- `OrbitStatusTag` / `OrbitStatusBanner` — `OFF / PAUSED / RUNNING` (+ unfunded wording), no
  `COOLING OFF` / `STOPPED` arms.
