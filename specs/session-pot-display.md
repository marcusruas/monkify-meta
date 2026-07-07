# Monkify — "Show Session Pot Value" Feature — Implementation Spec

> **Status: Shipped** — merged via `feature/session-pot-display` on 2026-07-07.
> Distilled summary: `../features/session-pot-display.md`.
>
> _Originally: Ready for implementation_ — refined via Trello card
> [Show pot value for that session.](https://trello.com/c/0FJquICm/32-show-pot-value-for-that-session) (2026-07-06).
> Repos affected: `monkify-front`, and this doc in `monkify-meta`. **`monkify-backend`: none.**
>
> **Purpose:** Surface the **pot** — the amount winners split — on the game screen for **every**
> session type (free-type race, preset race, and lottery), updating in realtime as bets arrive.
> Today only lottery sessions show a `> POT:` line; races show nothing. Player-facing, frontend-only.

---

## 1. Summary

The pot is already computed and displayed for **lottery** sessions in the terminal (`> POT:` line),
seeded from the lottery base pot plus placed bets and incremented live from the `RecentBets` `/Bets`
SignalR stream. This card **generalizes that exact pattern to all three session modes**. Nothing on
the backend changes — the pot is fully derivable client-side from data the client already receives
(the `GET` session instance's `bets` array + the live `/Bets` stream).

| Mode | Pot today | Pot after this change |
| --- | --- | --- |
| **Free-type race** | *(not shown)* | `Σ bets[].amount` — shown always, live |
| **Preset race** | *(not shown)* | `Σ bets[].amount` — shown always, live |
| **Lottery** | `lotteryPot` base `+ Σ bets[].amount` | **unchanged** (`lotteryPot` base `+ Σ bets[].amount`) |

The change is two files in `monkify-front`:

1. `src/hooks/useSession.ts` — generalize the `currentLotteryPot` ref to `currentPot`; seed and
   accumulate it for **all** modes (remove the two `isLottery` gates), keeping the lottery base only
   for lottery; reset/reseed on rollover (already in place).
2. `src/components/game/GameTerminal.vue` — render the `> POT:` line for **all** modes by moving it
   out of the lottery-only block, reusing the existing `formatAmount` helper.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo:** `feature/session-pot-display` in each affected repo (`monkify-front`,
  `monkify-meta`). **No backend branch** — no backend surface. Push after branch work.
- **Backend integration-test mandate — N/A.** The "every new feature ships a backend integration
  test" rule (`constraints.md` → "Integration tests are mandatory") is **backend/session-flow
  scoped**. This card has **no backend surface** (no handler, consumer, entity, endpoint, DTO, or
  SignalR contract change), so it introduces no backend happy path to exercise. No
  `Monkify.Tests.Integration` test is added.
- **Frontend has no unit-test harness.** `monkify-front/package.json` exposes only `serve`, `build`,
  and `lint` (no `test`/jest/vitest, and no `.spec`/`.test` files under `src/`). Verification is
  therefore **`npm run lint` + `npm run build` clean + manual QA in the running app** (§13), not an
  automated unit test. Do **not** introduce a test framework for this small display change.
- **EF mapping / Migrations / Secrets:** not touched (no backend, no config, no DB).
- **Operator messaging:** unaffected; no operator/contract status is surfaced.
- **RG / fairness:** pot is **display-only**; settlement/payout math is untouched
  (`BetDomainService.SetPotAmount` remains the source of truth). No spend CTA is attached.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Frontend-only.** No backend/DB/SignalR-contract change. Pot is derived client-side from data already received (`GET` session `bets` + the `/Bets` stream). |
| 2 | **Applies to all three modes** — free-type race, preset race, and lottery. |
| 3 | **Seed on load** from the `GET` session instance's `bets` array: `pot = Σ bets[].amount`. For **lottery**, additionally add the `lotteryParameters.lotteryPot` base (unchanged). For **races**, no base — pot is just the sum of bet amounts. |
| 4 | **Realtime** via the existing `RecentBets` `/Bets` SignalR stream: the pot increment moves **out** of the `if (sessionInstance.value.isLottery)` gate so every session accumulates as new bets arrive. |
| 5 | **Currency/format:** reuse the existing `formatAmount` helper in `GameTerminal.vue` — **USDC** for crypto operators, the player's **preferred display currency** for regulated operators — identical to the `> BET:` line. No new currency logic. |
| 6 | **Label:** reuse the exact `> POT:` line for all session types. |
| 7 | **Visibility:** show the pot **always while a session is loaded** — from load (0, or the lottery base, when no bets yet), through betting, the run, and after it ends (final pot). It **resets and reseeds on rollover** (`updateNewSession`, already in place for lottery; now applies to all). |
| 8 | **Naming:** rename the `currentLotteryPot` ref → `currentPot` (semantics broadened). It is consumed **only** by `GameTerminal.vue`, so no back-compat alias is needed. |
| 9 | **Free bets (`amount = 0`) contribute 0** — consistent with backend `SetPotAmount`. In dev free-bet mode a race pot reads `0`; a lottery still shows its base pot. |
| 10 | **POT line placement (design detail resolved here):** in the **pre-run view** the `> POT:` `<p>` becomes a **single universal line**; the lottery block is reduced to `> FREQUENCY:` only. For lottery this preserves the existing "frequency then pot" grouping (POT renders directly under FREQUENCY); for races the POT line renders under `> BET:`. This satisfies "move it out of the lottery-only block" with **no lottery value regression** (same number displayed via the same `formatAmount`). |
| 11 | **POT during the run and after (AC #8 resolution, confirmed with user 2026-07-06):** the terminal renders a **separate view branch** while the session is `InProgress`/`Ended`/rewarding (the `RACING…`/`DRAWING WINNER…` + typer + `WINNER` block), where the pre-run POT line does **not** appear. To keep the pot visible through the run and after it ends (card "Visibility" decision + AC #8), a **second `> POT:` line** is rendered inside that running/ended view branch (right after the `RACING…`/`DRAWING WINNER…` header). Both lines read the same `currentPot`, which retains the final accumulated total. This mirrors the existing pattern of duplicated per-view status labels. |

---

## 4. Data model

**No change.** No entities, columns, EF mapping, or migrations. This section is N/A — the feature is
display-only and computed from types the frontend already holds.

Relevant existing frontend types (`monkify-front/src/types/index.ts`, no edits needed):

```ts
export interface LotteryParameters { lotteryFrequency: string; lotteryPot: number; }
export interface ActiveSession   { /* … */ lotteryParameters: LotteryParameters; requiredAmount: number; /* … */ }
export interface AppliedBet      { externalPlayerId: string | null; /* … */ amount: number; /* … */ }
export interface SessionInstance { sessionId: string; status: SessionStatus; bets: AppliedBet[]; isLottery?: boolean; /* … */ }
```

---

## 5. Configuration

**None.** No env vars, `GeneralSettings`, or seed data. (For reference, the front's env prefix is
`VUE_APP_*`, but no new flag is introduced.)

---

## 6. Backend behaviour

**None.** No SignalR change, no new event, no entity/enum, no EF mapping/migration, no endpoint.
Outcomes, payouts, and the typer are untouched; the feature reads only the character/bet data the
client already receives and stays fully seed-reproducible and auditable. `BetDomainService`
`SetPotAmount` remains the settlement source of truth and is not consulted by this display.

---

## 7. Frontend behaviour

All changes are in `monkify-front`. Current code references use the files read during spec authoring.

### 7.1 `src/hooks/useSession.ts`

**(a) Rename the ref (module scope, line ~45).**

```diff
- const currentLotteryPot = ref(0);
+ const currentPot = ref(0);
```

**(b) Seed for all modes in `getBets` (lines ~68–74).** Replace the lottery-gated seed with an
unconditional sum of bets, adding the lottery base only when lottery:

```ts
// Seed the pot for every session type: pot = Σ bets. Lottery additionally starts from its base pot.
const totalBetsAmount = sessionData.bets.reduce((sum, bet) => sum + bet.amount, 0);
const lotteryBase =
  sessionData.isLottery && selectedSession.value?.lotteryParameters
    ? selectedSession.value.lotteryParameters.lotteryPot
    : 0;
currentPot.value = lotteryBase + totalBetsAmount;
```

> The lottery-base add stays gated on `isLottery` **and** the presence of `lotteryParameters`
> (mirrors today's guard), so a non-lottery race never picks up a stray base.

**(c) Accumulate for all modes on the `/Bets` stream (lines ~178–181).** Remove the `isLottery` gate:

```diff
- // Update lottery pot when new bet is received
- if (sessionInstance.value.isLottery) {
-   currentLotteryPot.value += newBet.amount;
- }
+ // Accumulate the pot for every session type as new bets arrive.
+ currentPot.value += newBet.amount;
```

**(d) Reset on rollover in `updateNewSession` (line ~321).** Rename only — the reset already exists;
`getBets` (called first in `updateNewSession`) reseeds it for the new session:

```diff
- currentLotteryPot.value = 0; // Reset pot for new session
+ currentPot.value = 0; // Reset pot for new session (reseeded by getBets above)
```

**(e) Expose `currentPot` (line ~360).**

```diff
- currentLotteryPot: computed(() => currentLotteryPot.value),
+ currentPot: computed(() => currentPot.value),
```

### 7.2 `src/components/game/GameTerminal.vue`

**(a) Destructure the renamed value (line ~34).**

```diff
-   currentLotteryPot,
+   currentPot,
```

**(b) Move the `> POT:` line out of the lottery-only block (lines ~392–405).** Reduce the lottery
block to `> FREQUENCY:` only, and render a universal `> POT:` line immediately after it (so lottery
keeps "frequency then pot", and races get a pot line under `> BET:`):

```html
<!-- Lottery specific info -->
<div v-if="isLotteryMode && selectedSession?.lotteryParameters">
  <br />
  <p>
    > FREQUENCY:
    {{ formatLotteryFrequency(selectedSession.lotteryParameters.lotteryFrequency) }}
  </p>
</div>

<!-- Pot: shown for every session type -->
<p>
  > POT:
  <span :title="formatAmount(currentPot).title">{{ formatAmount(currentPot).text }}</span>
</p>
```

**(c) Also render the `> POT:` line in the running/ended view (AC #8).** The terminal switches to a
separate view branch while the session is `InProgress`/`Ended`/rewarding
(`v-else-if="… status > SessionStarting && status < ErrorWhenProcessingRewards"`). Add a second POT
line there, right after the `RACING…`/`DRAWING WINNER…` header, so the pot stays visible during and
after the run with the final total:

```html
<br />
<p>> {{ isLotteryMode ? 'DRAWING WINNER...' : 'RACING...' }}</p>
<!-- Pot: stays visible during the run and after it ends (final total). -->
<p>
  > POT:
  <span :title="formatAmount(currentPot).title">{{ formatAmount(currentPot).text }}</span>
</p>
<br />
<game-typer />
```

- `formatAmount` is already defined locally in `GameTerminal.vue` and returns `{ text, title? }`,
  handling crypto (`USDC`) vs regulated (display currency) exactly like the `> BET:` line — **reused
  as-is**.
- No new API calls, headers, types, or currency logic. Both POT lines read the same `currentPot`,
  which retains the final accumulated total after the run.

---

## 8. Flow diagrams

Flow is straightforward; no sequence diagram needed. Data path:

```
GET session instance ─┐
   bets[].amount ──────┼─► currentPot (seed = Σ bets, + lotteryPot base if lottery)
RecentBets /Bets ──────┘        │
   newBet.amount ───────────────┘  (currentPot += amount, every mode)
                                     │
                          GameTerminal `> POT:` (formatAmount → USDC | display currency)
updateNewSession (rollover) ─► currentPot = 0 ─► getBets reseeds
```

---

## 9. Out of scope

- Any backend `pot` field, DTO, or SignalR pot event (explicitly chosen against — frontend-only).
- Changes to payout/pot/refund math or the settlement pipeline (`SetPotAmount` untouched).
- Per-choice pot breakdowns, potential-winnings/odds display, or pot history.
- Currency changes beyond reusing the existing `formatAmount` behavior.
- Any change to the lottery **value** shown (base + bets is preserved; only the markup location moves).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.**

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/hooks/useSession.ts` | Rename `currentLotteryPot` → `currentPot`; seed for all modes in `getBets` (`Σ bets` + lottery base only when lottery); remove `isLottery` gate on the `/Bets` increment; reset/reseed on rollover (rename only); expose `currentPot`. |
| 2 | `src/components/game/GameTerminal.vue` | Destructure `currentPot`; in the **pre-run view** move the `> POT:` `<p>` out of the lottery-only `v-if` block (which keeps only `> FREQUENCY:`) into a universal line using `formatAmount(currentPot)`; **also** add a second `> POT:` line in the **running/ended view** branch (after the `RACING…`/`DRAWING WINNER…` header) so it stays visible during and after the run (AC #8). |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/session-pot-display.md` | This spec (new). |
| 2 | `INDEX.md` | Add the spec row (status **Ready**); update "Feature currently in flight". |

---

## 11. Unit tests

**None — the frontend has no unit-test harness** (see §2). `monkify-front` ships no test runner and no
`src` tests; adding a framework for this display-only change is out of scope. Verification is
`npm run lint` + `npm run build` + the manual QA in §13. (Backend `Monkify.Tests` is untouched —
there is no backend change.)

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. **Race pot from bets.** Given a free-type or preset race session with placed bets, when the player
   opens the game screen, then a `> POT:` line is shown equal to `Σ bets[].amount` (no lottery base),
   formatted via `formatAmount`.
2. **Realtime increment.** Given a player is watching any session, when a new bet arrives over the
   `RecentBets` `/Bets` stream, then the displayed pot increases by that bet's amount in realtime
   without a page reload.
3. **Lottery no regression.** Given a lottery session, when the player opens the game screen, then the
   `> POT:` value equals `lotteryParameters.lotteryPot + Σ bets`, matching current behavior.
4. **Empty race pot = 0.** Given a race session with no bets yet, when the player opens the game
   screen, then the `> POT:` line shows `0` (formatted via `formatAmount`).
5. **Rollover reset + reseed.** Given the current session ends and rolls over, when the new session
   loads, then the pot resets to `0` and reseeds from the new session's bets (base pot for lottery),
   for every session type.
6. **Currency parity with BET.** Given a crypto operator vs a regulated operator, when the pot is
   displayed, then it renders as `USDC` for crypto and in the player's preferred display currency for
   regulated (identical to `> BET:` via `formatAmount`).
7. **Free bets contribute 0.** Given free bets (`amount = 0`) in a dev free-bet race, when placed,
   then they contribute nothing to the pot and the race pot reads `0` (a lottery still shows its base).
8. **Visible during/after run, no contract change.** Given a session that is `InProgress` or `Ended`,
   when the player views the terminal, then the pot line remains visible and shows the final
   accumulated total, with no backend/DB/SignalR-contract change introduced. *(Satisfied by the
   second `> POT:` line added in the running/ended view branch — see decision #11 / §7.2(c).)*

---

## 13. Manual QA

Run the local stack (`setup_local_environment.bat` / the front's `npm run serve`) and:

1. **Race, no bets:** open a free-type or preset race with no bets → `> POT:` shows `0`.
2. **Race, live bets:** place/observe bets from one or more players → pot increases by each bet's
   amount in realtime (no reload). Confirm it equals the sum of visible `BETS PLACED` amounts.
3. **Lottery parity:** open a lottery session → `> POT:` = base `lotteryPot` + bets, and the
   `> FREQUENCY:` line still shows above it (no visual regression).
4. **Currency:** on a crypto operator the pot reads `… USDC`; on a regulated operator it reads in the
   preferred display currency (same formatting as `> BET:`), with the USD title tooltip.
5. **Free-bet dev mode:** enable FREE BET MODE on an `allowFreeBet` race → placed free bets keep the
   pot at `0`.
6. **Rollover:** let the session end and roll to the next → pot resets to `0`/base and reseeds from
   the new session's bets.
7. **During/after run:** pot stays visible while `InProgress` and after `Ended`, showing the final
   total.
8. Confirm `npm run lint` and `npm run build` are clean.

---

## 14. Trello / pipeline

- **Source card:** [Show pot value for that session.](https://trello.com/c/0FJquICm/32-show-pot-value-for-that-session)
  — list **Specs created**; the **Implementation** section on the card lists branch + spec + worktree.
- **After implementation:** `/monkify-implement-feature Show pot value for that session.`
- **After merge:** `/monkify-ship-feature Show pot value for that session.` (reads branch from card).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-06 | Initial spec from refined card #32 (`0FJquICm`), grounded in `useSession.ts` + `GameTerminal.vue`. |
| 2026-07-06 | Implementation: resolved AC #8 (pot visible during/after the run) by adding a second `> POT:` line in the running/ended view branch (decision #11, §7.2(c)); confirmed with user. |
