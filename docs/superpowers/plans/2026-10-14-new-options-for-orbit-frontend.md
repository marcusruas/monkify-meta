# Orbit bet count & stake steps (#208) — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Orbit modal's LET ORBIT PLAY section gets mockup **C4c** — two tabs (**WHEN TO CASH OUT** / **BETS & STAKE**, named **HOW MANY BETS** on Cosmic Sprint) with PLAY [n] BETS, a CHANGE STAKE switch and AFTER A WIN / AFTER A LOSS rows (BACK TO BASE | RAISE BY + %), a read-back sentence, a run progress / "Finished N/N bets — paused" line, and an in-app toast when a run ends.

**Architecture:** Contract-first against the backend plan written in parallel (new `OrbitState` / `OrbitSettingsPatch` fields, private hub event `OrbitRunFinished`). All copy-producing and validation logic lives in one pure module (`src/utils/orbitRunReadback.ts`) so it can be reasoned about without Vue. State, auto-save (600 ms, armed from DOM `@input`), close-flush and the START fold live in `useOrbitAutoBet` (owned by the modal, survives section unmounts). Two new presentational components (`OrbitRulesTabs`, `OrbitStakeStepRow`) and an inline switch render it in `OrbitAutoBetSection.vue`.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, composables in `src/hooks`, SignalR client in `useGameHub.ts`, `vue3-toastify`, yarn (`vue-cli-service`). **No frontend test framework** — every task is gated by `yarn lint` + `yarn build`; behaviour is proven by the manual QA in Task 8.

**Spec:** `monkify-meta/specs/new-options-for-orbit.md` §3, §7, §10.2, §12 (AC 14–20), §13. **Design:** mockup **C4c · 'sentence' label set** (owner's artifact <https://claude.ai/artifact/NVprNoqbTC8x2GX3y92rCr>, round 2). **Worktree:** `E:\Monkify\.worktrees\new-options-for-orbit\monkify-front` on `feature/new-options-for-orbit` (equals `main` at plan time, head `6478eb5`).

## Global Constraints

- Work only in the worktree `E:\Monkify\.worktrees\new-options-for-orbit\monkify-front`; never edit the hub `E:\Monkify\monkify-front`.
- **Install:** never `yarn install`. Copy `E:\Monkify\monkify-front\yarn.lock` into the worktree (gitignored) and junction the hub's populated `node_modules` (Task 1 Step 1). Before any front `docker build`, remove the junction with `cmd //c rmdir node_modules` (never `rm -rf`, it follows the junction) and delete `dist/`. **Never copy the hub `.env.production.local`** (it holds prod config).
- **Gates per task:** `yarn lint` (eslint + `prettier/prettier` with `endOfLine: auto`, so CRLF is fine) and `yarn build`, both exit 0. If lint reports only `prettier/prettier` reflow errors in files you touched, run `yarn lint:fix` and re-run `yarn lint`. Do **not** use `prettier --check` (false-alarms on every CRLF checkout). Write new files with LF.
- Existing files check out **CRLF**; edit them with the Edit tool (preserves endings), never with `sed -i`.
- Everything stays behind `orbitEnabled` (already true of the whole modal and `useOrbitNotifications.start()`).
- **Exact copy (spec §7.2):** tabs `WHEN TO CASH OUT`, `BETS & STAKE` (Cosmic Sprint `HOW MANY BETS`); captions `BALANCE OVER`, `BALANCE UNDER`; bet caption `PLAY`, unit `BETS`, placeholder `no limit`; switch `CHANGE STAKE`; rows `AFTER A WIN` / `AFTER A LOSS`; segments `BACK TO BASE` / `RAISE BY`; read-back *"Orbit keeps playing until a cash-out rule fires or you pause."* / *"Orbit plays N bets, then pauses."* + *"Same stake every bet."* / *"After a loss it {doubles the stake | raises the stake by X% | goes back to {base}}; after a win it {…}."*; progress *"Bet {runBetsPlaced} / {betLimit}"* + *" · next stake {nextStake}"*, or *"Next stake {nextStake}"*; finished *"Finished {betLimit}/{betLimit} bets — paused"*; toast *"Orbit finished {n} bets on {gameTypeName} and paused. Your money stays in Orbit."*; lock tooltip *"Pause auto-bet to switch games or change its setup."*
- **Bounds (spec §3 decisions 2, 8):** bet limit integer 1–10,000, empty or 0 = no limit (sent as `clearBetLimit: true`); raise % 0–1000 with at most 2 decimals; BACK TO BASE sent as `clearOnWinIncrease` / `clearOnLossIncrease`.
- **Locks:** bet field, CHANGE STAKE switch and both rows use `setupLocked`; cash-out fields keep their `wordLocked` gate only. Cash-out rules and the word stay editable while running.
- **Auto-save:** run fields debounce **600 ms** (`STAKE_AUTOSAVE_DELAY_MS`), armed from DOM `@input` / component emits only — **never** a `watch` on the input refs (PATCH-loop trap, `useOrbitAutoBet.ts:245-254`). The switch saves immediately. Pending run saves flush on modal close and fold into the START PATCH.
- **Tab memory:** `localStorage` key `space-stroker.orbit.rules-tab` (`cash` | `bets`), default `cash`, every access in try/catch, written only by the player's clicks.
- **Fit:** the pinned modal must not scroll on Hyper Roll + BETS & STAKE + CHANGE STAKE on, desktop (1366×768) and phone sheet. If it does, shorten copy first; never add a scroll.
- Commit after each task in the front worktree (`git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front …`), message ending with the trailer `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. **Do not push.**

## Spec deviations (code contradicts or under-specifies the spec — adapted here)

1. **`refreshQuietly(wallet, 'run-finished')` does not type-check.** `refreshQuietly`'s 2nd argument is `MoneyChangeReason` = `'stake' | 'win' | 'refund' | 'deposit' | 'cashout'` (`src/types/money.ts:5`), and arming it publishes a balance delta. A run end moves no money, so the plan calls the **unarmed** `refreshQuietly(wallet)`.
2. **The private channel is left the moment `orbit.enabled` reads false** (`useOrbitNotifications.ts:165-174`). Run-end sets `Enabled = false` server-side, so any quiet refresh that lands before the `OrbitRunFinished` push (e.g. the final bet's win-toast refresh) unbinds the handler and the toast is lost (AC 14). Added a **dedupe-guarded fallback**: the same toast fires on a live `runFinished` false → true transition when the event did not already announce it. A reload into an already-finished run shows only the status line (no toast).
3. **Nothing re-reads `GET /orbit` per auto round** (`applyAutoRound` is network-free, `useOrbit.ts:1101-1106`), so `runBetsPlaced` / `nextStake` would freeze between wins. Added: on an `OrbitAutoRound` event carrying a **new** `roundId`, a quiet refresh **only when the enrolment has a bet limit or stake steps on**. The backend pushes `OrbitAutoRound` for **solo types only** (`OrbitSoloAutoBetService.cs:498`), so on Cosmic Sprint the progress line updates on modal open, win refreshes and the run-end refresh only.
4. **`effectiveStakeFor` also feeds the STAKE plate** (`useOrbitAutoBet.ts:600-602`, `savedStake`). Returning `nextStake` there would show the stepped stake on the base-stake plate while running and snap it back on pause. Added `baseStakeFor` (today's expression) for the plate; `effectiveStakeFor` gains the `nextStake` branch as specified (which also makes `DuelConsole.vue:457` and `GameTerminal.vue:284` quote the real next stake while Orbit auto-plays).
5. **`OrbitStakeStepRow` v-model is `{ mode: 'base' | 'raise'; percentText: string }`, not `{ percent: number | null }`.** "RAISE BY selected, nothing typed yet" and decision 15's "keep the last typed % while BACK TO BASE is selected" are both states `percent: null` cannot hold. The pure module maps it to the wire (base ⇒ clear flag ⇒ null %).
6. **An invalid run field blocks START** (button reads `FIX BETS & STAKE FIRST`). The spec only says an invalid value "is not sent"; without the block, START would run on the stored value (e.g. *no limit*) while the field shows something else — a money-relevant mismatch.
7. **Read-back pluralises one bet:** `Orbit plays 1 bet, then pauses.` (spec template is `N bets`).
8. **"AUTO-BETS ONLY moves into the tip text":** `thresholdHelp` and `floorHelp` keep their sentences and gain the suffix *" Manual bets never trigger it."* (the spec says the tips stay and the note moves in, without giving the words).
9. **CHANGE STAKE switch** is a visually hidden native `<input type="checkbox" role="switch">` plus a drawn track span — still native (keyboard + focus), but Firefox does not paint `::after` on inputs, so the knob cannot be drawn on the input itself.
10. **An unsaved run-field edit is dropped on a game-tile switch / wallet switch / failed START** (`cancelPendingSetup` now covers the run fields, as it does the stake). Stored values are untouched — the backend keeps them across a type change (decision 14); only an edit still inside its 600 ms window is discarded.
11. **The progress / finished line sits directly above START** (spec §7.2), not above the tab strip as in the mockup.

## Facts from the codebase (verified 2026-10-14 in the worktree)

- `src/hooks/useOrbitAutoBet.ts` (894 lines): `setupLocked` `:72`; `wordLocked` `:82`; `thresholdHelp` `:110-111`, `floorHelp` `:113-117`; rules `asText` `:141`, `syncRuleFields` `:143-147`, `armRule` `:255-261`, `flushPendingRules` `:275-280`; `withSelectedTypeIfUnset` `:515-518`; `STAKE_AUTOSAVE_DELAY_MS = 600` `:599`, `savedStake` `:600-602`, `stakeInput` `:603`; `onMultiplierChange` ends `:717`; `cancelPendingSetup` `:720-725`; `flushPendingSetup` `:728-731`; `watch(selectedType.id, cancelPendingSetup, { immediate: true })` `:734` (**immediate** — anything it calls must be initialised above it); `hasFundsForRound` `:769-778`; `onToggle` `:791-813`; `toggleState` `:820-842`; return `:844-891`.
- `src/components/orbit/OrbitModal.vue:215-225` calls `flushPendingRules()` + `flushPendingSetup()` on close and `cancelPendingSetup()` on a wallet switch (`:244-250`) — extending those two functions covers the modal with **no modal change**.
- `src/components/orbit/sections/OrbitAutoBetSection.vue`: AUTO CASHOUT block `:189-268`; START button `:274-287`; `.orbit-rules`, `.orbit-rules-row` (2 cols ≥ 400px), `.rule-field`, `.rule-field__unit` scoped CSS `:301-419`.
- `src/hooks/useGameHub.ts`: `ORBIT_NOTIFICATION_EVENT` `:46`, `ORBIT_AUTO_ROUND_EVENT` `:48`, handlers `:49-50`; `joinOrbitChannel(sessionToken, handler, onAutoRound?)` `:315-341`; `leaveOrbitChannel` `:345-363`; `stopForIdle` resets `:395-397`.
- `src/hooks/useOrbitNotifications.ts`: `onWin` toast `:65`, `onAutoRound` `:89-102`, `start()` joins/leaves on `[hasSession, orbit.enabled]` `:165-174`.
- `src/hooks/useOrbit.ts`: `effectiveStakeFor` `:900-903`; used by `isPausedForFunds` `:933`, `needsFirstDeposit` `:966`, `roundsLeft` `:1034`; `activeGameType` `:982-986`; returned `:1367-1438`.
- `src/components/orbit/OrbitStakePlate.vue:23-26` formats a stake "at least 3 places, up to 9" (`0.01` → `0.010`) — the read-back reuses that rule.
- `src/components/game/RaceTabs.vue` (tablist pattern) and `src/components/orbit/OrbitMultiplierPicker.vue:78-124` (radiogroup buttons, violet `--color-orbit` selected state) are the visual models for the new components. Tokens in use: `--color-orbit`, `--color-orbit-deep`, `--color-void`, `--color-void-deep`, `--color-ink`, `--color-ink-dim`, `--color-ink-mute`, `--color-live`, `--radius-arcade`, `--font-display`, `--font-body`, `--ctl-h-sm`, `--ctl-px-sm`.
- No `components/ui` tabs / switch / segmented control exists. `ArcadeInfoTip` props: `text`, `label`, `tone?: 'default' | 'orbit'`.

## File map

| File | Responsibility | Task |
| --- | --- | --- |
| `src/types/orbit.ts` | `OrbitState` + `OrbitSettingsPatch` fields, `OrbitRunFinishedEvent`, `OrbitRulesTabId` | 1 |
| `src/utils/orbitRunReadback.ts` *(new)* | pure: bounds, parsing/validation, stake format, read-back sentence, status line | 2 |
| `src/hooks/useGameHub.ts` | `OrbitRunFinished` handler slot | 3 |
| `src/hooks/useOrbitNotifications.ts` | run-finished toast (+ fallback), per-round quiet refresh | 3 |
| `src/hooks/useOrbit.ts` | `baseStakeFor`, `effectiveStakeFor` uses `nextStake` while enabled | 4 |
| `src/components/orbit/OrbitRulesTabs.vue` *(new)* | two-tab strip with hidden-tab dot | 5 |
| `src/components/orbit/OrbitStakeStepRow.vue` *(new)* | label + BACK TO BASE \| RAISE BY + % field | 5 |
| `src/hooks/useOrbitAutoBet.ts` | run inputs, sync, debounce/flush/fold, validation, read-back, status, tab memory, START label/blocker | 6 |
| `src/components/orbit/sections/OrbitAutoBetSection.vue` | C4c layout, labels, switch, status line | 7 |

---

### Task 1: Workspace prep + contract types

**Files:**
- Modify: `src/types/orbit.ts` (`OrbitState` `:64-169`, `OrbitSettingsPatch` `:176-219`, after `OrbitAutoRoundEvent` `:279-283`, after `OrbitSectionId` `:400`)

**Interfaces:**
- Produces: `OrbitState.betLimit: number | null`, `.stakeStepsEnabled: boolean`, `.onWinIncreasePercent: number | null`, `.onLossIncreasePercent: number | null`, `.runBetsPlaced: number`, `.runFinished: boolean`, `.nextStake: number | null`; `OrbitSettingsPatch.betLimit?`, `.clearBetLimit?`, `.stakeStepsEnabled?`, `.onWinIncreasePercent?`, `.clearOnWinIncrease?`, `.onLossIncreasePercent?`, `.clearOnLossIncrease?`; `interface OrbitRunFinishedEvent { gameTypeId: string; gameTypeName: string; betsPlaced: number }`; `type OrbitRulesTabId = 'cash' | 'bets'`.

- [ ] **Step 1: Prepare the worktree for the gates (no install)**

```bash
cp E:/Monkify/monkify-front/yarn.lock E:/Monkify/.worktrees/new-options-for-orbit/monkify-front/yarn.lock
test -e E:/Monkify/.worktrees/new-options-for-orbit/monkify-front/node_modules || cmd //c mklink /J "E:\Monkify\.worktrees\new-options-for-orbit\monkify-front\node_modules" "E:\Monkify\monkify-front\node_modules"
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front status --short
```

Expected: the junction is created; `git status` prints nothing (both paths are gitignored).

- [ ] **Step 2: Baseline gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: both exit 0. Note any pre-existing warnings as the baseline — later tasks must not add errors.

- [ ] **Step 3: Add the `OrbitState` fields** — insert after `isStreamer: boolean;` (`:168`), before the closing `}`:

```ts
  // ---- #208 run limits & stake steps (specs/new-options-for-orbit.md §6.8) ----

  /** Auto bets per run (one START). Null = no limit. Any auto-bet type. */
  betLimit: number | null;

  /** Whether the AFTER A WIN / AFTER A LOSS steps apply. Solo types only; ignored on Cosmic Sprint. */
  stakeStepsEnabled: boolean;

  /** AFTER A WIN: null = back to base, else raise the previous stake by this % (0–1000, ≤ 2 dp). */
  onWinIncreasePercent: number | null;

  /** AFTER A LOSS: same shape as `onWinIncreasePercent`. */
  onLossIncreasePercent: number | null;

  /**
   * Auto bets claimed in the CURRENT run (since the last START), net of refunds. Not
   * `orbitBetsPlaced`, which is the all-time count and keeps its meaning.
   */
  runBetsPlaced: number;

  /** The last run ended on its bet limit and Orbit paused itself (`!enabled && limit reached`). */
  runFinished: boolean;

  /**
   * The stake the next solo auto round uses — the base, or the stepped one, clamped to the type's
   * bounds. Null on Cosmic Sprint and when no solo type is enrolled.
   */
  nextStake: number | null;
```

- [ ] **Step 4: Add the `OrbitSettingsPatch` fields** — insert after `clearMultiplier?: boolean;` (`:210`):

```ts

  /**
   * #208: auto bets per run. `clearBetLimit` (the backend also reads `betLimit: 0` as a clear) means
   * no limit. Locked while Orbit is enabled, like the stake.
   */
  betLimit?: number;
  clearBetLimit?: boolean;

  /** Solo types only — the backend refuses `true` while the target type is Cosmic Sprint. */
  stakeStepsEnabled?: boolean;

  /** 0–1000, at most 2 decimals. The clear flag stores BACK TO BASE (a null %). */
  onWinIncreasePercent?: number;
  clearOnWinIncrease?: boolean;
  onLossIncreasePercent?: number;
  clearOnLossIncrease?: boolean;
```

- [ ] **Step 5: Add the event and the tab id** — after the `OrbitAutoRoundEvent` interface (`:279-283`):

```ts

/**
 * #208: pushed on the Orbit channel (`OrbitRunFinished`) when a run reaches its bet limit and Orbit
 * pauses itself. The money stays in Orbit. No balance on purpose — the number comes from a re-read.
 */
export interface OrbitRunFinishedEvent {
  gameTypeId: string;
  gameTypeName: string;
  betsPlaced: number;
}
```

and after `export type OrbitSectionId = …;` (`:400`):

```ts

/**
 * #208: the two tabs of LET ORBIT PLAY's rules area — WHEN TO CASH OUT and BETS & STAKE (HOW MANY
 * BETS on Cosmic Sprint). The ids are what `space-stroker.orbit.rules-tab` remembers.
 */
export type OrbitRulesTabId = 'cash' | 'bets';
```

- [ ] **Step 6: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0 (no object literal builds an `OrbitState`, so the new required fields break nothing).

- [ ] **Step 7: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/types/orbit.ts
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: contract types for run limits, stake steps and OrbitRunFinished.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: Pure run rules — `src/utils/orbitRunReadback.ts`

**Files:**
- Create: `src/utils/orbitRunReadback.ts`

**Interfaces:**
- Consumes: nothing (no Vue, no imports).
- Produces (exact exports):
  - `MAX_BET_LIMIT = 10_000`, `MAX_INCREASE_PERCENT = 1000`, `INCREASE_PERCENT_DECIMALS = 2`
  - `type StakeStepMode = 'base' | 'raise'`; `interface StakeStepValue { mode: StakeStepMode; percentText: string }`
  - `type BetLimitParse = { ok: true; value: number | null } | { ok: false }`; `parseBetLimit(raw: string): BetLimitParse`
  - `type StepTarget = { kind: 'base' } | { kind: 'raise'; value: number } | { kind: 'pending' } | { kind: 'invalid' }`; `stepTarget(step: StakeStepValue): StepTarget`; `percentForReadback(step: StakeStepValue): number | null`
  - `formatStakeAmount(value: number): string`
  - `interface RunReadbackInput { betLimit: number | null; isSolo: boolean; stakeStepsEnabled: boolean; onWinPercent: number | null; onLossPercent: number | null; baseStakeLabel: string }`; `buildRunReadback(input: RunReadbackInput): string`
  - `interface RunStatusInput { running: boolean; finished: boolean; betLimit: number | null; runBetsPlaced: number; showNextStake: boolean; nextStake: number | null }`; `runStatusLine(input: RunStatusInput): { text: string; finished: boolean } | null`
  - `BET_LIMIT_INVALID_MESSAGE`, `PERCENT_INVALID_MESSAGE` (strings)

- [ ] **Step 1: Write the module (LF line endings)**

```ts
/**
 * #208 (specs/new-options-for-orbit.md §7.2–7.3): the PURE rules behind LET ORBIT PLAY's BETS &
 * STAKE tab — what a typed bet count or raise % means, whether it may be sent, and the sentences the
 * tab prints. No Vue and no I/O, so every rule here can be read (and checked by hand) in isolation.
 *
 * ⚠️ The bounds MIRROR the backend (`OrbitRunRules`: MaxBetLimit 10,000, MaxIncreasePercent 1000,
 * 2 decimals). The server stays authoritative and refuses with the vague `OrbitRequestInvalid`;
 * these exist so an out-of-range value is explained beside the field and never sent.
 */

export const MAX_BET_LIMIT = 10_000;
export const MAX_INCREASE_PERCENT = 1000;
export const INCREASE_PERCENT_DECIMALS = 2;

export const BET_LIMIT_INVALID_MESSAGE = `PLAY takes a whole number from 1 to ${MAX_BET_LIMIT.toLocaleString(
  'en-US'
)}. Leave it empty for no limit.`;

export const PERCENT_INVALID_MESSAGE = `RAISE BY takes 0 to ${MAX_INCREASE_PERCENT}%, with at most ${INCREASE_PERCENT_DECIMALS} decimals.`;

/** BACK TO BASE or RAISE BY. */
export type StakeStepMode = 'base' | 'raise';

/**
 * One AFTER A WIN / AFTER A LOSS row as the UI holds it.
 *
 * ⚠️ `percentText` is kept while BACK TO BASE is selected (decision 15): flipping back to RAISE BY
 * finds the last typed % instead of an empty field. It is TEXT, not a number, because "RAISE BY
 * selected, nothing typed yet" is a real state the wire's `number | null` cannot hold.
 */
export interface StakeStepValue {
  mode: StakeStepMode;
  percentText: string;
}

export type BetLimitParse = { ok: true; value: number | null } | { ok: false };

/**
 * The PLAY field. Empty or `0` ⇒ no limit (`value: null`, sent as `clearBetLimit`); a whole number
 * 1–10,000 ⇒ that limit; anything else (decimals, signs, over the cap) ⇒ not sendable.
 */
export const parseBetLimit = (raw: string): BetLimitParse => {
  const text = raw.trim();
  if (text === '') return { ok: true, value: null };
  if (!/^\d+$/.test(text)) return { ok: false };
  const value = Number(text);
  if (value === 0) return { ok: true, value: null };
  return value <= MAX_BET_LIMIT ? { ok: true, value } : { ok: false };
};

/** At most 2 decimals, no sign, comma or dot. "1." and ".5" are refused (finish typing). */
const PERCENT_PATTERN = /^\d+(\.\d{1,2})?$/;

export type StepTarget =
  | { kind: 'base' }
  | { kind: 'raise'; value: number }
  | { kind: 'pending' }
  | { kind: 'invalid' };

/**
 * What a row would store. `base` ⇒ the clear flag (null %); `raise` ⇒ that %; `pending` ⇒ RAISE BY
 * with nothing typed yet (nothing to send, nothing wrong); `invalid` ⇒ out of 0–1000 or > 2 dp.
 * A raise of 0 is legal and behaves as back to base server-side (decision 8).
 */
export const stepTarget = (step: StakeStepValue): StepTarget => {
  if (step.mode === 'base') return { kind: 'base' };
  const text = step.percentText.trim().replace(',', '.');
  if (text === '') return { kind: 'pending' };
  if (!PERCENT_PATTERN.test(text)) return { kind: 'invalid' };
  const value = Number(text);
  return value <= MAX_INCREASE_PERCENT ? { kind: 'raise', value } : { kind: 'invalid' };
};

/** The % the read-back describes: a sendable raise, else null (what the backend would do: base). */
export const percentForReadback = (step: StakeStepValue): number | null => {
  const target = stepTarget(step);
  return target.kind === 'raise' ? target.value : null;
};

/**
 * A stake the way the STAKE plate prints it (`OrbitStakePlate.format`): at least 3 places, up to 9,
 * no float noise, never exponent form — `0.01` → `0.010`, `0.0125` → `0.0125`, `1` → `1.000`.
 */
export const formatStakeAmount = (value: number): string => {
  const [whole, fraction = ''] = value.toFixed(9).replace(/0+$/, '').split('.');
  return `${whole}.${fraction.padEnd(3, '0')}`;
};

/** One step in words: back to base (null or 0), doubles (100), else raises by X%. */
const describeStep = (percent: number | null, baseStakeLabel: string): string => {
  if (percent === null || percent === 0) return `goes back to ${baseStakeLabel}`;
  if (percent === 100) return 'doubles the stake';
  return `raises the stake by ${percent}%`;
};

export interface RunReadbackInput {
  /** The limit being set up (null = no limit). */
  betLimit: number | null;
  /** Monkey Duel / Hyper Roll — the types with a stake and stake steps. */
  isSolo: boolean;
  stakeStepsEnabled: boolean;
  onWinPercent: number | null;
  onLossPercent: number | null;
  /** The base stake, already formatted (`formatStakeAmount`). */
  baseStakeLabel: string;
}

/**
 * The read-back sentence under the BETS & STAKE / HOW MANY BETS tab (spec §7.2), e.g.
 * "Orbit plays 20 bets, then pauses. After a loss it doubles the stake; after a win it goes back to 0.010."
 */
export const buildRunReadback = (input: RunReadbackInput): string => {
  const count =
    input.betLimit === null
      ? 'Orbit keeps playing until a cash-out rule fires or you pause.'
      : `Orbit plays ${input.betLimit} ${input.betLimit === 1 ? 'bet' : 'bets'}, then pauses.`;
  if (!input.isSolo) return count;
  if (!input.stakeStepsEnabled) return `${count} Same stake every bet.`;
  const loss = describeStep(input.onLossPercent, input.baseStakeLabel);
  const win = describeStep(input.onWinPercent, input.baseStakeLabel);
  return `${count} After a loss it ${loss}; after a win it ${win}.`;
};

export interface RunStatusInput {
  /** `orbitStatus === 'running'`. */
  running: boolean;
  /** `orbit.runFinished`. */
  finished: boolean;
  betLimit: number | null;
  runBetsPlaced: number;
  /** Solo type with CHANGE STAKE stored on. */
  showNextStake: boolean;
  nextStake: number | null;
}

/**
 * The one line above START (spec §7.2): progress while running, the finished line after a run ended
 * on its limit, else nothing.
 */
export const runStatusLine = (input: RunStatusInput): { text: string; finished: boolean } | null => {
  if (input.running) {
    const next =
      input.showNextStake && input.nextStake !== null ? formatStakeAmount(input.nextStake) : null;
    if (input.betLimit !== null) {
      const suffix = next ? ` · next stake ${next}` : '';
      return { text: `Bet ${input.runBetsPlaced} / ${input.betLimit}${suffix}`, finished: false };
    }
    return next ? { text: `Next stake ${next}`, finished: false } : null;
  }
  if (input.finished && input.betLimit !== null) {
    return { text: `Finished ${input.betLimit}/${input.betLimit} bets — paused`, finished: true };
  }
  return null;
};
```

- [ ] **Step 2: Hand-check the rules against the spec (no test runner exists)** — read the module and confirm each line of this table (fix the code if any disagrees):

| Call | Expected |
| --- | --- |
| `parseBetLimit('')`, `parseBetLimit('0')` | `{ ok: true, value: null }` |
| `parseBetLimit('20')`, `parseBetLimit('10000')` | `{ ok: true, value: 20 }`, `{ ok: true, value: 10000 }` |
| `parseBetLimit('10001')`, `parseBetLimit('1.5')`, `parseBetLimit('-3')` | `{ ok: false }` |
| `stepTarget({ mode: 'base', percentText: '50' })` | `{ kind: 'base' }` |
| `stepTarget({ mode: 'raise', percentText: '' })` | `{ kind: 'pending' }` |
| `stepTarget({ mode: 'raise', percentText: '12,5' })` | `{ kind: 'raise', value: 12.5 }` |
| `stepTarget({ mode: 'raise', percentText: '1000.01' })`, `'1.234'` | `{ kind: 'invalid' }` |
| `formatStakeAmount(0.01)`, `formatStakeAmount(0.04)` | `'0.010'`, `'0.040'` |
| `buildRunReadback({ betLimit: 20, isSolo: true, stakeStepsEnabled: true, onWinPercent: null, onLossPercent: 100, baseStakeLabel: '0.010' })` | `'Orbit plays 20 bets, then pauses. After a loss it doubles the stake; after a win it goes back to 0.010.'` |
| `buildRunReadback({ betLimit: null, isSolo: false, … })` | `'Orbit keeps playing until a cash-out rule fires or you pause.'` |
| `buildRunReadback({ betLimit: 3, isSolo: true, stakeStepsEnabled: false, … })` | `'Orbit plays 3 bets, then pauses. Same stake every bet.'` |
| `runStatusLine({ running: true, finished: false, betLimit: 3, runBetsPlaced: 1, showNextStake: true, nextStake: 0.02 })` | `{ text: 'Bet 1 / 3 · next stake 0.020', finished: false }` |
| `runStatusLine({ running: false, finished: true, betLimit: 3, … })` | `{ text: 'Finished 3/3 bets — paused', finished: true }` |

Optional quick execution (TypeScript is in the junctioned `node_modules`):

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && node_modules/.bin/tsc --target es2020 --module commonjs --outDir C:/Users/Marcu/AppData/Local/Temp/claude/E--Monkify/6341b070-5224-44df-b916-326230bc39a9/scratchpad/readback src/utils/orbitRunReadback.ts && node -e "const r=require('C:/Users/Marcu/AppData/Local/Temp/claude/E--Monkify/6341b070-5224-44df-b916-326230bc39a9/scratchpad/readback/orbitRunReadback.js');console.log(r.buildRunReadback({betLimit:20,isSolo:true,stakeStepsEnabled:true,onWinPercent:null,onLossPercent:100,baseStakeLabel:r.formatStakeAmount(0.01)}));console.log(JSON.stringify(r.runStatusLine({running:true,finished:false,betLimit:3,runBetsPlaced:1,showNextStake:true,nextStake:0.02})))"
```

Expected output:
```
Orbit plays 20 bets, then pauses. After a loss it doubles the stake; after a win it goes back to 0.010.
{"text":"Bet 1 / 3 · next stake 0.020","finished":false}
```

- [ ] **Step 3: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0 (run `yarn lint:fix` first if only `prettier/prettier` reflows the two long template-literal constants).

- [ ] **Step 4: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/utils/orbitRunReadback.ts
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: pure run rules — bet-limit / raise-% validation, read-back sentence, status line.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3: Realtime — `OrbitRunFinished` toast and fresh run progress

**Files:**
- Modify: `src/hooks/useGameHub.ts` (`:46-50`, `:315-341`, `:345-363`, `:395-397`)
- Modify: `src/hooks/useOrbitNotifications.ts` (imports `:1-9`, module scope `:26-29`, destructure `:32-43`, `onAutoRound` `:89-102`, `start()` `:156-177`)

**Interfaces:**
- Consumes: `OrbitRunFinishedEvent` (Task 1); `OrbitState.runFinished`, `.betLimit`, `.runBetsPlaced`, `.stakeStepsEnabled` (Task 1); `useOrbit().activeGameType`, `refreshQuietly(wallet)` (existing).
- Produces: `joinOrbitChannel(sessionToken, handler, onAutoRound?, onRunFinished?)` — the 4th parameter is new and optional.

- [ ] **Step 1: Hub constants and handler slot** — in `useGameHub.ts` replace

```ts
const ORBIT_AUTO_ROUND_EVENT = 'OrbitAutoRound';
let orbitHandler: ((payload: string) => void) | null = null;
let orbitAutoRoundHandler: ((payload: string) => void) | null = null;
```

with

```ts
const ORBIT_AUTO_ROUND_EVENT = 'OrbitAutoRound';
// #208: a run reached its bet limit and Orbit paused itself (specs/new-options-for-orbit.md §6.7).
const ORBIT_RUN_FINISHED_EVENT = 'OrbitRunFinished';
let orbitHandler: ((payload: string) => void) | null = null;
let orbitAutoRoundHandler: ((payload: string) => void) | null = null;
let orbitRunFinishedHandler: ((payload: string) => void) | null = null;
```

- [ ] **Step 2: Bind it in `joinOrbitChannel`** — replace the signature and the binding block:

```ts
const joinOrbitChannel = async (
  sessionToken: string,
  handler: (payload: string) => void,
  onAutoRound?: (payload: string) => void,
  onRunFinished?: (payload: string) => void
): Promise<void> => {
```

and, right after

```ts
  if (onAutoRound) {
    live.on(ORBIT_AUTO_ROUND_EVENT, onAutoRound);
    orbitAutoRoundHandler = onAutoRound;
  }
```

add

```ts
  if (onRunFinished) {
    live.on(ORBIT_RUN_FINISHED_EVENT, onRunFinished);
    orbitRunFinishedHandler = onRunFinished;
  }
```

- [ ] **Step 3: Unbind it in `leaveOrbitChannel`** — after `if (orbitAutoRoundHandler && live) live.off(ORBIT_AUTO_ROUND_EVENT, orbitAutoRoundHandler);` add

```ts
  if (orbitRunFinishedHandler && live)
    live.off(ORBIT_RUN_FINISHED_EVENT, orbitRunFinishedHandler);
```

and after `orbitAutoRoundHandler = null;` (in the same function) add `orbitRunFinishedHandler = null;`.

- [ ] **Step 4: Reset it in `stopForIdle`** — after `orbitAutoRoundHandler = null;` (`:396`) add `orbitRunFinishedHandler = null;`. The reconnect path needs nothing: the server group is re-joined by `JoinOrbitNotifications` and the `.on` bindings live on the same `HubConnection`.

- [ ] **Step 5: Notifications — imports and module state** — in `useOrbitNotifications.ts` replace

```ts
import { OrbitAutoRoundEvent, OrbitWinNotification } from '@/types/orbit';
```

with

```ts
import { OrbitAutoRoundEvent, OrbitRunFinishedEvent, OrbitWinNotification } from '@/types/orbit';
```

and after `let started = false;` add

```ts

/**
 * #208: whether the CURRENT finished run has been announced. The toast has two sources — the
 * `OrbitRunFinished` push and a fallback on `runFinished` turning true (see `watchRunFinished`) —
 * and must fire once. Cleared when `runFinished` reads false again (a fresh START).
 */
let runFinishedAnnounced = false;
```

- [ ] **Step 6: Destructure `activeGameType`** — add `activeGameType,` to the `useOrbit()` destructure list (after `applyAutoRound,`).

- [ ] **Step 7: Per-round quiet refresh** — in `onAutoRound`, replace the three lines

```ts
    applyAutoRound(evt);
    lastAutoRound.value = evt;
  };
```

(the last two statements **and** the function's closing `};`) with the block below. It re-closes `onAutoRound` itself and then declares the new functions after it:

```ts
    // Read BEFORE applying: the event overwrites `currentAutoRoundId`.
    const previousRoundId = orbit.value?.currentAutoRoundId ?? null;

    applyAutoRound(evt);
    lastAutoRound.value = evt;

    /**
     * #208: the modal's "Bet 3 / 20 · next stake 0.040" line reads `runBetsPlaced` / `nextStake`
     * from GET /orbit, and nothing else re-reads it per round (`applyAutoRound` is network-free; the
     * win toast only refreshes on a WIN). So a NEW round re-reads quietly — but only for a player who
     * set a run limit or stake steps, so an ordinary auto-bet pays no extra request. Unarmed: no money
     * moved here. ⚠️ Solo types only in practice: the backend pushes this event from the solo loop.
     */
    const state = orbit.value;
    const wallet = effectivePublicKey.value;
    const tracksRun = state?.betLimit != null || !!state?.stakeStepsEnabled;
    if (wallet && tracksRun && evt.roundId && evt.roundId !== previousRoundId)
      void refreshQuietly(wallet);
  };

  /**
   * #208: one toast per finished run (spec §7.1), from either source. The money stays in Orbit, so
   * the copy says so — a pause the player did not press must not read as a loss.
   */
  const announceRunFinished = (betsPlaced: number, gameTypeName: string) => {
    if (runFinishedAnnounced) return;
    runFinishedAnnounced = true;
    toast.info(
      `Orbit finished ${betsPlaced} bets on ${gameTypeName} and paused. Your money stays in Orbit.`
    );
  };

  /** The `OrbitRunFinished` push: toast, then a quiet re-read so the modal shows the finished line. */
  const onRunFinished = (payload: string) => {
    let evt: OrbitRunFinishedEvent | null = null;

    try {
      evt = JSON.parse(payload) as OrbitRunFinishedEvent;
    } catch {
      return;
    }

    if (!evt) return;

    announceRunFinished(evt.betsPlaced, evt.gameTypeName);

    // ⚠️ UNARMED (no MoneyChangeReason): a run end moves no money, and arming would publish a delta.
    const wallet = effectivePublicKey.value;
    if (wallet) void refreshQuietly(wallet);
  };

  /**
   * ⚠️ THE FALLBACK, and why it exists. `start()` LEAVES the channel the moment `orbit.enabled` reads
   * false — and a run end is exactly an `enabled → false`. Any quiet re-read that lands before the push
   * (the final bet's win toast refreshes too) unbinds the handler and the push is never seen. So a LIVE
   * `runFinished` false → true transition announces it as well; `runFinishedAnnounced` keeps it to one
   * toast. Strictly `false → true` on the same wallet: a reload INTO a finished run (undefined → true)
   * or a wallet switch is told by the modal's status line, not a toast.
   */
  const watchRunFinished = () => {
    watch(
      () => [orbit.value?.runFinished, effectivePublicKey.value] as const,
      ([finished, wallet], previous) => {
        if (finished === false) {
          runFinishedAnnounced = false;
          return;
        }
        if (finished !== true || !previous) return;
        const [wasFinished, previousWallet] = previous;
        if (wasFinished !== false || previousWallet !== wallet) return;

        const state = orbit.value;
        announceRunFinished(
          state?.betLimit ?? state?.runBetsPlaced ?? 0,
          activeGameType.value?.name ?? 'your game'
        );
      }
    );
  };
```

(The first `};` in this block closes `onAutoRound`; `watchPause` and `start` below stay as they are.)

- [ ] **Step 8: Wire both into `start()`** — replace

```ts
        if (signedIn && enabled && token) void joinOrbitChannel(token, onWin, onAutoRound);
```

with

```ts
        if (signedIn && enabled && token)
          void joinOrbitChannel(token, onWin, onAutoRound, onRunFinished);
```

and after `watchPause();` add `watchRunFinished();`. The once-per-episode funds toast (`watchPause`) cannot also fire: `isPausedForFunds` requires `enabled`.

- [ ] **Step 9: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0.

- [ ] **Step 10: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/hooks/useGameHub.ts src/hooks/useOrbitNotifications.ts
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: OrbitRunFinished toast (with a live-transition fallback) and per-round run refresh.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 4: `useOrbit` — the next stake drives affordability

**Files:**
- Modify: `src/hooks/useOrbit.ts` (`effectiveStakeFor` `:896-903`, return `:1432`)

**Interfaces:**
- Consumes: `OrbitState.nextStake` (Task 1).
- Produces: `baseStakeFor(type: OrbitEligibleGameType): number` (today's expression — the player's chosen stake); `effectiveStakeFor(type)` now returns `orbit.nextStake` when Orbit is enabled on that solo type and `nextStake != null`.

- [ ] **Step 1: Split base from effective** — replace the whole `effectiveStakeFor` block (doc comment + function, `:896-903`) with:

```ts
  /**
   * The stake the PLAYER chose on `type`: their stake on house-edge and duel types, the type's fixed
   * `requiredAmount` everywhere else. What the STAKE plate edits — never stepped (#208: the stored
   * `stake` is the run's BASE and is never overwritten by a step).
   */
  const baseStakeFor = (type: OrbitEligibleGameType): number =>
    type.isHouseEdge || type.isDuel
      ? (orbit.value?.stake ?? type.requiredAmount)
      : type.requiredAmount;

  /**
   * The stake Orbit will actually bet NEXT on `type`.
   *
   * #208 (spec §7.1): with CHANGE STAKE on, a solo run's next stake is stepped (back to base, or the
   * last stake raised by X%, clamped to the type's bounds) and the backend reports it as `nextStake`.
   * While Orbit is ENABLED on that type it is the number that matters, so `isPausedForFunds`,
   * `needsFirstDeposit`, `roundsLeft` and the game screens' Orbit stake judge the NEXT bet exactly as
   * the backend does. Paused, the next START resets the run to base — so base it is.
   * ⚠️ `!= null`, not `!== null`: an older API omits the field entirely.
   */
  const effectiveStakeFor = (type: OrbitEligibleGameType): number => {
    const state = orbit.value;
    if (
      (type.isHouseEdge || type.isDuel) &&
      state?.enabled &&
      state.gameParametersId === type.id &&
      state.nextStake != null
    )
      return state.nextStake;
    return baseStakeFor(type);
  };
```

- [ ] **Step 2: Export it** — in the returned object replace `effectiveStakeFor,` with:

```ts
    effectiveStakeFor,
    baseStakeFor,
```

- [ ] **Step 3: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0. (`useOrbitAutoBet`'s plate still calls `effectiveStakeFor` until Task 6 switches it to `baseStakeFor`; nothing renders a stepped stake before the backend sends `nextStake`.)

- [ ] **Step 4: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/hooks/useOrbit.ts
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: effectiveStakeFor follows the stepped nextStake while enabled; baseStakeFor for the plate.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 5: Presentational components — tabs and step row

**Files:**
- Create: `src/components/orbit/OrbitRulesTabs.vue`
- Create: `src/components/orbit/OrbitStakeStepRow.vue`

**Interfaces:**
- Consumes: `StakeStepValue` from `@/utils/orbitRunReadback` (Task 2).
- Produces:
  - `OrbitRulesTabs` — props `tabs: { id: string; label: string; active: boolean }[]`, `modelValue: string`; emits `update:modelValue(id: string)`. Renders a dot on a tab that is **not** selected and whose `active` is true.
  - `OrbitStakeStepRow` — props `label: string`, `modelValue: StakeStepValue`, `disabled: boolean`; emits `update:modelValue(value: StakeStepValue)` (segment click or % typing — DOM-originated only) and `commit()` (% field blur / Enter).

- [ ] **Step 1: `OrbitRulesTabs.vue` (LF)**

```vue
<script setup lang="ts">
  /**
   * #208 (spec §7.2, mockup C4c): the two-tab strip of LET ORBIT PLAY's rules area — WHEN TO CASH
   * OUT and BETS & STAKE (HOW MANY BETS on Cosmic Sprint). Modelled on `components/game/RaceTabs.vue`
   * (a `role="tablist"` of buttons), in the Orbit violet.
   *
   * The DOT: a tab that is NOT open but holds an active rule (a cash-out threshold/floor, a bet limit,
   * stake steps) carries one, so a rule can never hide behind the other tab (decision 13).
   *
   * PRESENTATIONAL — the owner keeps the selection and remembers it.
   */
  defineProps<{
    tabs: { id: string; label: string; active: boolean }[];
    modelValue: string;
  }>();

  const emit = defineEmits<{ (event: 'update:modelValue', id: string): void }>();
</script>

<template>
  <div class="orbit-tabs" role="tablist" aria-label="Auto-bet rules">
    <button
      v-for="tab in tabs"
      :key="tab.id"
      type="button"
      role="tab"
      class="orbit-tabs__tab"
      :class="{ 'orbit-tabs__tab--on': tab.id === modelValue }"
      :aria-selected="tab.id === modelValue"
      :title="tab.active && tab.id !== modelValue ? 'A rule is set on this tab' : undefined"
      @click="emit('update:modelValue', tab.id)"
    >
      {{ tab.label
      }}<span
        v-if="tab.active && tab.id !== modelValue"
        class="orbit-tabs__dot"
        aria-hidden="true"
      ></span>
    </button>
  </div>
</template>

<style scoped>
  .orbit-tabs {
    display: flex;
    gap: 0;
    border-bottom: 2px solid color-mix(in oklab, var(--color-orbit-deep) 60%, var(--color-void));
  }

  .orbit-tabs__tab {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    margin-bottom: -2px;
    padding: 0.45rem 0.55rem;
    border: 2px solid transparent;
    border-bottom: 0;
    background: transparent;
    color: var(--color-ink-mute);
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 0.5rem;
    line-height: 1.4;
    letter-spacing: 0.04em;
    white-space: nowrap;
    cursor: pointer;
  }

  .orbit-tabs__tab--on {
    border-color: color-mix(in oklab, var(--color-orbit-deep) 60%, var(--color-void));
    background: var(--color-void-deep);
    color: var(--color-orbit);
  }

  .orbit-tabs__tab:hover:not(.orbit-tabs__tab--on) {
    color: var(--color-ink-dim);
  }

  .orbit-tabs__tab:focus-visible {
    outline: 2px solid var(--color-orbit);
    outline-offset: 2px;
  }

  .orbit-tabs__dot {
    width: 6px;
    height: 6px;
    flex: none;
    border-radius: 9999px;
    background: var(--color-orbit);
    box-shadow: 0 0 6px var(--color-orbit);
  }
</style>
```

- [ ] **Step 2: `OrbitStakeStepRow.vue` (LF)**

```vue
<script setup lang="ts">
  import { nextTick, ref } from 'vue';
  import type { StakeStepMode, StakeStepValue } from '@/utils/orbitRunReadback';

  /**
   * #208 (spec §7.2, mockup C4c): one AFTER A WIN / AFTER A LOSS row — the label, a segmented
   * BACK TO BASE | RAISE BY (`role="radiogroup"`, the pattern of `OrbitMultiplierPicker`), and a %
   * field that is greyed with "—" while BACK TO BASE is selected.
   *
   * ⚠️ PRESENTATIONAL and DOM-driven: every `update:modelValue` comes from a click or a keystroke,
   * never from a prop change — the owner arms its 600 ms auto-save off these emits, which is what
   * keeps a server resync from re-arming a save (the PATCH-loop trap in `useOrbitAutoBet`).
   *
   * The % text is KEPT while BACK TO BASE is selected (decision 15), so RAISE BY again finds it.
   */
  const props = defineProps<{
    label: string;
    modelValue: StakeStepValue;
    disabled: boolean;
  }>();

  const emit = defineEmits<{
    (event: 'update:modelValue', value: StakeStepValue): void;
    (event: 'commit'): void;
  }>();

  const percentField = ref<HTMLInputElement | null>(null);

  const pick = async (mode: StakeStepMode) => {
    if (props.disabled || mode === props.modelValue.mode) return;
    emit('update:modelValue', { ...props.modelValue, mode });
    // RAISE BY with nothing typed yet: put the cursor where the number goes.
    if (mode === 'raise' && props.modelValue.percentText.trim() === '') {
      await nextTick();
      percentField.value?.focus();
    }
  };

  const onPercentInput = (event: Event) => {
    emit('update:modelValue', {
      ...props.modelValue,
      percentText: (event.target as HTMLInputElement).value,
    });
  };
</script>

<template>
  <div class="orbit-step">
    <span class="dialog-eyebrow orbit-step__label">{{ label }}</span>

    <div class="orbit-step__seg" role="radiogroup" :aria-label="label">
      <button
        type="button"
        role="radio"
        class="orbit-step__opt"
        :class="{ 'orbit-step__opt--on': modelValue.mode === 'base' }"
        :aria-checked="modelValue.mode === 'base'"
        :disabled="disabled"
        @click="pick('base')"
      >
        BACK TO BASE
      </button>
      <button
        type="button"
        role="radio"
        class="orbit-step__opt"
        :class="{ 'orbit-step__opt--on': modelValue.mode === 'raise' }"
        :aria-checked="modelValue.mode === 'raise'"
        :disabled="disabled"
        @click="pick('raise')"
      >
        RAISE BY
      </button>
    </div>

    <span class="orbit-step__pct">
      <input
        ref="percentField"
        type="text"
        inputmode="decimal"
        autocomplete="off"
        class="input-arcade tabular-nums"
        :value="modelValue.mode === 'raise' ? modelValue.percentText : ''"
        :placeholder="modelValue.mode === 'raise' ? '0' : '—'"
        :disabled="disabled || modelValue.mode === 'base'"
        :aria-label="`${label}: raise the stake by this percent`"
        @input="onPercentInput"
        @blur="emit('commit')"
        @keyup.enter="emit('commit')"
      />
      <span v-if="modelValue.mode === 'raise'" class="orbit-step__unit" aria-hidden="true">%</span>
    </span>
  </div>
</template>

<style scoped>
  .orbit-step {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.375rem 0.5rem;
  }

  /* A fixed column so the two rows' controls line up (mockup C4c: 7.5em). */
  .orbit-step__label {
    width: 7.5em;
    flex: none;
  }

  .orbit-step__seg {
    display: inline-grid;
    grid-template-columns: auto auto;
    flex: none;
    border: 2px solid color-mix(in oklab, var(--color-orbit-deep) 60%, var(--color-void));
    border-radius: var(--radius-arcade);
  }

  .orbit-step__opt {
    padding: 0.45rem 0.45rem;
    border: 0;
    background: transparent;
    color: var(--color-ink-mute);
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 0.4375rem;
    line-height: 1.3;
    white-space: nowrap;
    cursor: pointer;
  }

  .orbit-step__opt--on {
    background: var(--color-orbit-deep);
    color: var(--color-ink);
  }

  .orbit-step__opt:focus-visible {
    outline: 2px solid var(--color-orbit);
    outline-offset: 2px;
  }

  .orbit-step__opt:disabled {
    cursor: default;
  }

  /* Same `sm` control rung as the cash-out fields (`.rule-field` in OrbitAutoBetSection). */
  .orbit-step__pct {
    position: relative;
    display: inline-flex;
    width: 4.5rem;
    flex: none;
    align-items: center;
  }

  .orbit-step__pct input {
    width: 100%;
    height: var(--ctl-h-sm);
    padding: 0 1.4rem 0 var(--ctl-px-sm);
    font-size: 17px;
    color: var(--color-ink);
  }

  .orbit-step__pct input:disabled {
    opacity: 0.45;
  }

  .orbit-step__unit {
    position: absolute;
    top: 50%;
    right: 8px;
    transform: translateY(-50%);
    pointer-events: none;
    font-family: 'Press Start 2P', monospace;
    font-size: 0.4375rem;
    color: var(--color-ink-mute);
  }
</style>
```

- [ ] **Step 3: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0 (the components are not mounted yet; they still type-check in the build).

- [ ] **Step 4: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/components/orbit/OrbitRulesTabs.vue src/components/orbit/OrbitStakeStepRow.vue
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: OrbitRulesTabs (hidden-tab dot) and OrbitStakeStepRow (BACK TO BASE | RAISE BY + %).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 6: `useOrbitAutoBet` — run state, auto-save, read-back, status, START

**Files:**
- Modify: `src/hooks/useOrbitAutoBet.ts` (imports `:1-3`; module scope before `export const useOrbitAutoBet`; destructure `:27-37`; `thresholdHelp`/`floorHelp` `:110-117`; `savedStake` `:600-602`; new block between `onMultiplierChange` (ends `:717`) and `cancelPendingSetup` `:719`; `cancelPendingSetup` `:720-725`; `flushPendingSetup` `:728-731`; `onToggle` `:795-801`; `toggleState` `:829-841`; return `:844-891`)

**Interfaces:**
- Consumes: Task 1 types (`OrbitRulesTabId`, new `OrbitState` / `OrbitSettingsPatch` fields); Task 2 exports (`parseBetLimit`, `stepTarget`, `percentForReadback`, `formatStakeAmount`, `buildRunReadback`, `runStatusLine`, `StakeStepValue`, `BET_LIMIT_INVALID_MESSAGE`, `PERCENT_INVALID_MESSAGE`); Task 4 `baseStakeFor`.
- Produces (added to the returned form, consumed by Task 7): `rulesTab: Ref<OrbitRulesTabId>`, `rulesTabs: ComputedRef<{ id: OrbitRulesTabId; label: string; active: boolean }[]>`, `selectRulesTab(id: string): void`, `betLimitInput: Ref<string | number>`, `stakeStepsInput: Ref<boolean>`, `onWinInput: Ref<StakeStepValue>`, `onLossInput: Ref<StakeStepValue>`, `armRunField(field: 'betLimit' | 'win' | 'loss'): void`, `commitBetLimit(): Promise<void>`, `commitStep(which: 'win' | 'loss'): Promise<void>`, `onStepInput(which: 'win' | 'loss', value: StakeStepValue): void`, `onToggleStakeSteps(enabled: boolean): Promise<void>`, `runReadback: ComputedRef<string>`, `runError: ComputedRef<string | null>`, `runStatus: ComputedRef<{ text: string; finished: boolean } | null>`.

- [ ] **Step 1: Imports** — replace lines 1-3:

```ts
import { computed, onBeforeUnmount, ref, watch, type Ref } from 'vue';
import { useOrbit } from '@/hooks/useOrbit';
import type { OrbitEligibleGameType, OrbitSettingsPatch } from '@/types/orbit';
```

with

```ts
import { computed, onBeforeUnmount, ref, watch, type Ref } from 'vue';
import { useOrbit } from '@/hooks/useOrbit';
import type { OrbitEligibleGameType, OrbitRulesTabId, OrbitSettingsPatch } from '@/types/orbit';
import {
  BET_LIMIT_INVALID_MESSAGE,
  PERCENT_INVALID_MESSAGE,
  buildRunReadback,
  formatStakeAmount,
  parseBetLimit,
  percentForReadback,
  runStatusLine,
  stepTarget,
  type StakeStepValue,
} from '@/utils/orbitRunReadback';

/**
 * #208: which rules tab the player last opened, per device. Same contract as `useOrbitSections`:
 * storage is a convenience — every access is wrapped, and blocked storage simply means WHEN TO CASH
 * OUT on every open. Only the player's own clicks write it.
 */
const RULES_TAB_STORAGE_KEY = 'space-stroker.orbit.rules-tab';

const readStoredRulesTab = (): OrbitRulesTabId => {
  try {
    return localStorage.getItem(RULES_TAB_STORAGE_KEY) === 'bets' ? 'bets' : 'cash';
  } catch {
    return 'cash';
  }
};

const writeStoredRulesTab = (id: OrbitRulesTabId) => {
  try {
    localStorage.setItem(RULES_TAB_STORAGE_KEY, id);
  } catch {
    // Storage blocked: the tab is simply not remembered.
  }
};
```

- [ ] **Step 2: Destructure `baseStakeFor`** — in the `useOrbit()` destructure add `baseStakeFor,` after `effectiveStakeFor,`.

- [ ] **Step 3: The AUTO-BETS ONLY note moves into the tips** — replace

```ts
  const thresholdHelp =
    'Orbit sends the balance to your wallet as soon as an auto-bet win takes it here.';

  const floorHelp = computed(() =>
    hasCashOutFloor.value
      ? 'Orbit stops and sends what is left if an auto-bet takes the balance under this.'
      : 'Set a floor and Orbit stops if an auto-bet takes the balance under it. A rule that fires on an auto-bet win cannot catch a fall.'
  );
```

with

```ts
  // #208: the "AUTO-BETS ONLY" eyebrow gave way to the tab strip, so its note rides in the tips.
  const thresholdHelp =
    'Orbit sends the balance to your wallet as soon as an auto-bet win takes it here. Manual bets never trigger it.';

  const floorHelp = computed(() =>
    hasCashOutFloor.value
      ? 'Orbit stops and sends what is left if an auto-bet takes the balance under this. Manual bets never trigger it.'
      : 'Set a floor and Orbit stops if an auto-bet takes the balance under it. A rule that fires on an auto-bet win cannot catch a fall. Manual bets never trigger it.'
  );
```

- [ ] **Step 4: The plate shows the BASE stake** — replace

```ts
  const savedStake = computed(() =>
    selectedType.value ? effectiveStakeFor(selectedType.value) : 0
  );
```

with

```ts
  // #208: the BASE stake (`baseStakeFor`), never the stepped `nextStake` — the plate edits the base,
  // and while a run steps it the plate must not show a number that snaps back on pause.
  const savedStake = computed(() => (selectedType.value ? baseStakeFor(selectedType.value) : 0));
```

- [ ] **Step 5: The run block** — insert this block **after** `onMultiplierChange` (ends `:717`) and **before** the `/** Drops every unsaved setup edit …` comment of `cancelPendingSetup`. ⚠️ It must sit above `cancelPendingSetup`: the `watch(selectedType.id, cancelPendingSetup, { immediate: true })` at `:734` runs immediately, and `cancelPendingSetup` will call into this block — anything declared below that watch is still in its temporal dead zone.

```ts
  // --- Run limits & stake steps (#208, specs/new-options-for-orbit.md §7.2–7.3) ----------------
  //
  // The BETS & STAKE tab (HOW MANY BETS on Cosmic Sprint): PLAY [n] BETS on every game, and on the
  // two solo games a CHANGE STAKE switch with AFTER A WIN / AFTER A LOSS rows. All of it is SETUP:
  // locked while Orbit is on (`setupLocked` — the backend refuses a change on an enabled enrolment
  // too, decision 11) and saved on the STAKE's 600 ms cadence, not the rules' 1.2 s one. None of
  // these moves money by itself, and a run only starts on START, which folds any pending edit in.
  //
  // ⚠️ Same auto-save shape as the rules: armed from DOM `@input` / the step row's emits, NEVER from a
  // `watch` on these refs (the PATCH-loop trap documented on `armRule`). The watches below only SYNC
  // from server state, and only while no save for that field is armed.

  type RunField = 'betLimit' | 'win' | 'loss';
  const RUN_FIELDS: readonly RunField[] = ['betLimit', 'win', 'loss'];

  /** Typed `string | number` and read through `asText`: v-model on a number input casts. */
  const betLimitInput = ref<string | number>('');
  const stakeStepsInput = ref(false);
  const onWinInput = ref<StakeStepValue>({ mode: 'base', percentText: '' });
  const onLossInput = ref<StakeStepValue>({ mode: 'base', percentText: '' });

  /** One inline error per field; the tab shows the first. Set on commit, cleared on a good value. */
  const runFieldErrors = ref<Record<RunField, string | null>>({
    betLimit: null,
    win: null,
    loss: null,
  });

  /** One timer per field, like the rules: editing one row must not cancel another's pending save. */
  const runTimers: Record<RunField, ReturnType<typeof setTimeout> | null> = {
    betLimit: null,
    win: null,
    loss: null,
  };

  const stepRef = (which: 'win' | 'loss') => (which === 'win' ? onWinInput : onLossInput);

  const storedPercent = (which: 'win' | 'loss'): number | null =>
    (which === 'win' ? orbit.value?.onWinIncreasePercent : orbit.value?.onLossIncreasePercent) ??
    null;

  const syncBetLimit = () => {
    const stored = orbit.value?.betLimit ?? null;
    betLimitInput.value = stored === null ? '' : String(stored);
    runFieldErrors.value.betLimit = null;
  };

  const syncStakeSteps = () => {
    stakeStepsInput.value = !!orbit.value?.stakeStepsEnabled;
  };

  const syncStep = (which: 'win' | 'loss') => {
    const stored = storedPercent(which);
    const target = stepRef(which);
    // BACK TO BASE keeps the % last typed, so RAISE BY again finds it (decision 15).
    target.value =
      stored === null
        ? { mode: 'base', percentText: target.value.percentText }
        : { mode: 'raise', percentText: String(stored) };
    runFieldErrors.value[which] = null;
  };

  watch(
    () => orbit.value?.betLimit,
    () => {
      if (runTimers.betLimit === null) syncBetLimit();
    },
    { immediate: true }
  );
  watch(() => orbit.value?.stakeStepsEnabled, syncStakeSteps, { immediate: true });
  watch(
    () => orbit.value?.onWinIncreasePercent,
    () => {
      if (runTimers.win === null) syncStep('win');
    },
    { immediate: true }
  );
  watch(
    () => orbit.value?.onLossIncreasePercent,
    () => {
      if (runTimers.loss === null) syncStep('loss');
    },
    { immediate: true }
  );

  const cancelRunTimer = (field: RunField) => {
    const pending = runTimers[field];
    if (pending === null) return;
    clearTimeout(pending);
    runTimers[field] = null;
  };

  const patchForStep = (which: 'win' | 'loss', percent: number | null): OrbitSettingsPatch => {
    if (which === 'win')
      return percent === null ? { clearOnWinIncrease: true } : { onWinIncreasePercent: percent };
    return percent === null ? { clearOnLossIncrease: true } : { onLossIncreasePercent: percent };
  };

  /**
   * Saves PLAY. Empty or 0 is sent as `clearBetLimit` (null on this PATCH means "leave it alone"). An
   * out-of-range value stays in the field with the inline error and is NOT sent (spec §7.3).
   */
  const commitBetLimit = async () => {
    cancelRunTimer('betLimit');
    if (!props.wallet) return;

    const parsed = parseBetLimit(asText(betLimitInput.value));
    if (!parsed.ok) {
      runFieldErrors.value.betLimit = BET_LIMIT_INVALID_MESSAGE;
      return;
    }
    runFieldErrors.value.betLimit = null;
    // "0" means no limit: show it as the empty "no limit" field it is.
    if (parsed.value === null) betLimitInput.value = '';
    if (parsed.value === (orbit.value?.betLimit ?? null)) return;
    if (setupLocked.value) {
      syncBetLimit();
      return;
    }

    notice.value = null;
    try {
      await updateSettings(
        props.wallet,
        withSelectedTypeIfUnset(
          parsed.value === null ? { clearBetLimit: true } : { betLimit: parsed.value }
        )
      );
    } catch {
      // useOrbit surfaced the reason; the field must not keep a value the server refused.
      syncBetLimit();
    }
  };

  /**
   * Saves one AFTER A WIN / AFTER A LOSS row. BACK TO BASE is the clear flag (a null %); RAISE BY with
   * nothing typed yet saves nothing (and is not an error).
   */
  const commitStep = async (which: 'win' | 'loss') => {
    cancelRunTimer(which);
    if (!props.wallet) return;

    const target = stepTarget(stepRef(which).value);
    if (target.kind === 'invalid') {
      runFieldErrors.value[which] = PERCENT_INVALID_MESSAGE;
      return;
    }
    runFieldErrors.value[which] = null;
    if (target.kind === 'pending') return;

    const next = target.kind === 'raise' ? target.value : null;
    if (next === storedPercent(which)) return;
    if (setupLocked.value) {
      syncStep(which);
      return;
    }

    notice.value = null;
    try {
      await updateSettings(props.wallet, withSelectedTypeIfUnset(patchForStep(which, next)));
    } catch {
      syncStep(which);
    }
  };

  const commitRunField = (field: RunField) =>
    field === 'betLimit' ? commitBetLimit() : commitStep(field);

  /** Arms a field's 600 ms save. Called from `@input` / the row's emits only — see the block note. */
  const armRunField = (field: RunField) => {
    cancelRunTimer(field);
    runTimers[field] = setTimeout(() => {
      runTimers[field] = null;
      void commitRunField(field);
    }, STAKE_AUTOSAVE_DELAY_MS);
  };

  /** A row changed (segment click or % typing): hold it locally and arm its save. */
  const onStepInput = (which: 'win' | 'loss', value: StakeStepValue) => {
    stepRef(which).value = value;
    armRunField(which);
  };

  /** CHANGE STAKE: saved at once — the switch IS the decision, there is no half-typed state. */
  const onToggleStakeSteps = async (enabled: boolean) => {
    if (!props.wallet || setupLocked.value) {
      syncStakeSteps();
      return;
    }
    stakeStepsInput.value = enabled;
    notice.value = null;
    try {
      await updateSettings(props.wallet, withSelectedTypeIfUnset({ stakeStepsEnabled: enabled }));
    } catch {
      syncStakeSteps();
    }
  };

  /**
   * Every valid, unsaved run edit as ONE patch — for START, which must carry it: sent separately it
   * would land after `enabled: true` and be refused by the lock (the same reason the stake is folded).
   * Step rows only on a solo type: the backend refuses step fields on Cosmic Sprint.
   */
  const runPatch = (): OrbitSettingsPatch => {
    const patch: OrbitSettingsPatch = {};

    const limit = parseBetLimit(asText(betLimitInput.value));
    if (limit.ok && limit.value !== (orbit.value?.betLimit ?? null)) {
      if (limit.value === null) patch.clearBetLimit = true;
      else patch.betLimit = limit.value;
    }

    if (hasStakeChoice.value) {
      (['win', 'loss'] as const).forEach((which) => {
        const target = stepTarget(stepRef(which).value);
        if (target.kind !== 'base' && target.kind !== 'raise') return;
        const next = target.kind === 'raise' ? target.value : null;
        if (next !== storedPercent(which)) Object.assign(patch, patchForStep(which, next));
      });
    }

    return patch;
  };

  /** Takes the run patch for START and drops the timers it supersedes. */
  const takeRunPatch = (): OrbitSettingsPatch => {
    RUN_FIELDS.forEach(cancelRunTimer);
    return runPatch();
  };

  /** Close-flush: a field still on its debounce is saved now (Esc / backdrop / ✗ never blur). */
  const flushPendingRunSetup = () => {
    RUN_FIELDS.forEach((field) => {
      if (runTimers[field] !== null) void commitRunField(field);
    });
  };

  /** Drops every unsaved run edit and shows the stored values (game switch, wallet switch, failed START). */
  const cancelPendingRunSetup = () => {
    RUN_FIELDS.forEach(cancelRunTimer);
    syncBetLimit();
    syncStakeSteps();
    syncStep('win');
    syncStep('loss');
  };

  onBeforeUnmount(() => RUN_FIELDS.forEach(cancelRunTimer));

  /**
   * A field holds something that cannot be sent. Blocks START (see `toggleState`): otherwise START
   * would run on the STORED value — e.g. no limit — while the field shows another.
   */
  const runInvalid = computed(
    () =>
      !parseBetLimit(asText(betLimitInput.value)).ok ||
      (hasStakeChoice.value &&
        stakeStepsInput.value &&
        (['win', 'loss'] as const).some((which) => stepTarget(stepRef(which).value).kind === 'invalid'))
  );

  const runError = computed(
    () =>
      runFieldErrors.value.betLimit ?? runFieldErrors.value.win ?? runFieldErrors.value.loss ?? null
  );

  /** The read-back sentence (spec §7.2) — of what is being SET UP, so it answers as you type. */
  const runReadback = computed(() => {
    const limit = parseBetLimit(asText(betLimitInput.value));
    return buildRunReadback({
      betLimit: limit.ok ? limit.value : (orbit.value?.betLimit ?? null),
      isSolo: hasStakeChoice.value,
      stakeStepsEnabled: stakeStepsInput.value,
      onWinPercent: percentForReadback(onWinInput.value),
      onLossPercent: percentForReadback(onLossInput.value),
      baseStakeLabel: formatStakeAmount(stakeInput.value),
    });
  });

  /** Progress while running, "Finished N/N bets — paused" after a run ended on its limit (§7.2). */
  const runStatus = computed(() =>
    runStatusLine({
      running: orbitStatus.value === 'running',
      finished: !!orbit.value?.runFinished,
      betLimit: orbit.value?.betLimit ?? null,
      runBetsPlaced: orbit.value?.runBetsPlaced ?? 0,
      showNextStake: hasStakeChoice.value && !!orbit.value?.stakeStepsEnabled,
      nextStake: orbit.value?.nextStake ?? null,
    })
  );

  // --- The rules tabs (#208, decision 13) ---------------------------------------------------------

  const rulesTab = ref<OrbitRulesTabId>(readStoredRulesTab());

  const selectRulesTab = (id: string) => {
    const next: OrbitRulesTabId = id === 'bets' ? 'bets' : 'cash';
    rulesTab.value = next;
    writeStoredRulesTab(next);
  };

  /** `active` = the tab holds a STORED rule; the strip dots it while the other tab is open. */
  const rulesTabs = computed<{ id: OrbitRulesTabId; label: string; active: boolean }[]>(() => [
    {
      id: 'cash',
      label: 'WHEN TO CASH OUT',
      active: stopRules.value.threshold !== null || stopRules.value.floor !== null,
    },
    {
      id: 'bets',
      label: hasStakeChoice.value ? 'BETS & STAKE' : 'HOW MANY BETS',
      active:
        (orbit.value?.betLimit ?? null) !== null ||
        (hasStakeChoice.value && !!orbit.value?.stakeStepsEnabled),
    },
  ]);
```

- [ ] **Step 6: `cancelPendingSetup` / `flushPendingSetup` cover the run fields** — replace

```ts
  /** Drops every unsaved setup edit (stake, mode, multiplier) and shows the stored values again. */
  const cancelPendingSetup = () => {
    cancelPendingStakeSave();
    hyperPending = null;
    syncStake();
    syncHyper();
  };

  /** The close-flush for the setup: a stake still on its debounce is saved now, like the rules. */
  const flushPendingSetup = () => {
    if (stakeDirty.value) commitStake();
    else cancelPendingStakeSave();
  };
```

with

```ts
  /**
   * Drops every unsaved setup edit (stake, mode, multiplier, and #208's run fields) and shows the
   * stored values again.
   */
  const cancelPendingSetup = () => {
    cancelPendingStakeSave();
    hyperPending = null;
    syncStake();
    syncHyper();
    cancelPendingRunSetup();
  };

  /** The close-flush for the setup: a stake or run field still on its debounce is saved now. */
  const flushPendingSetup = () => {
    if (stakeDirty.value) commitStake();
    else cancelPendingStakeSave();
    flushPendingRunSetup();
  };
```

(`OrbitModal.vue` already calls both on close / wallet switch — no modal change.)

- [ ] **Step 7: START folds the run patch** — in `onToggle` replace

```ts
    if (starting) {
      setup = takeHyperPatch() ?? {};
      if (stakeDirty.value) setup.stake = toSol(stakeInput.value);
      cancelPendingStakeSave();
    }
```

with

```ts
    if (starting) {
      // #208: pending run edits (PLAY, the step rows) ride along too — after `enabled` they'd be locked.
      setup = { ...(takeHyperPatch() ?? {}), ...takeRunPatch() };
      if (stakeDirty.value) setup.stake = toSol(stakeInput.value);
      cancelPendingStakeSave();
    }
```

- [ ] **Step 8: START label and blocker** — in `toggleState` replace

```ts
    const blocker = orbit.value?.selfExcluded
      ? 'Auto-play unavailable'
      : !hasPlayableChoice.value
        ? 'Pick a word first'
        : !hasFundsForRound.value || orbitStatus.value === 'unfunded'
          ? 'Add money first'
          : null;
    return {
      running: false,
      disabled: !!blocker,
      label: (orbit.value?.orbitBetsPlaced ?? 0) > 0 ? 'RESUME AUTO-BET' : 'START AUTO-BET',
      note: blocker ?? 'Orbit bets every round for you',
    };
```

with

```ts
    const blocker = orbit.value?.selfExcluded
      ? 'Auto-play unavailable'
      : !hasPlayableChoice.value
        ? 'Pick a word first'
        : runInvalid.value
          ? 'Fix bets & stake first'
          : !hasFundsForRound.value || orbitStatus.value === 'unfunded'
            ? 'Add money first'
            : null;
    // #208: after a run ended on its limit, START begins a FRESH run (counter 0, stake back to base),
    // so it must not read RESUME.
    const label =
      !orbit.value?.runFinished && (orbit.value?.orbitBetsPlaced ?? 0) > 0
        ? 'RESUME AUTO-BET'
        : 'START AUTO-BET';
    return {
      running: false,
      disabled: !!blocker,
      label,
      note: blocker ?? 'Orbit bets every round for you',
    };
```

- [ ] **Step 9: Return the new members** — in the returned object, after `onToggle,` add:

```ts
    // #208 run limits & stake steps
    rulesTab,
    rulesTabs,
    selectRulesTab,
    betLimitInput,
    stakeStepsInput,
    onWinInput,
    onLossInput,
    armRunField,
    commitBetLimit,
    commitStep,
    onStepInput,
    onToggleStakeSteps,
    runReadback,
    runError,
    runStatus,
```

- [ ] **Step 10: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0. If `effectiveStakeFor` is now reported unused in this file, keep it — `hasFundsForRound` still calls it for Cosmic Sprint.

- [ ] **Step 11: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/hooks/useOrbitAutoBet.ts
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: run limit and stake-step state — 600 ms auto-save, close-flush, START fold, read-back, status, tab memory.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 7: `OrbitAutoBetSection.vue` — mockup C4c

**Files:**
- Modify: `src/components/orbit/sections/OrbitAutoBetSection.vue` (imports `:2-8`, header comment `:10-21`, destructure `:27-67`, AUTO CASHOUT block `:189-268`, before the START button `:270`, styles `:295-426`)

**Interfaces:**
- Consumes: Task 5 components; Task 6 form members (`rulesTab`, `rulesTabs`, `selectRulesTab`, `betLimitInput`, `stakeStepsInput`, `onWinInput`, `onLossInput`, `armRunField`, `commitBetLimit`, `commitStep`, `onStepInput`, `onToggleStakeSteps`, `runReadback`, `runError`, `runStatus`).
- Produces: the rendered C4c section (no new exports).

- [ ] **Step 1: Imports** — after `import OrbitMultiplierPicker from '@/components/orbit/OrbitMultiplierPicker.vue';` add:

```ts
  import OrbitRulesTabs from '@/components/orbit/OrbitRulesTabs.vue';
  import OrbitStakeStepRow from '@/components/orbit/OrbitStakeStepRow.vue';
```

- [ ] **Step 2: Header comment** — replace the sentence `… — then the auto cash-out rules and START/PAUSE.` (line 13) with `… — then the rules in two tabs (#208, mockup C4c: WHEN TO CASH OUT and BETS & STAKE / HOW MANY BETS), the run status line and START/PAUSE.`

- [ ] **Step 3: Destructure the new members** — in the `props.form` destructure, after `onToggle,` add:

```ts
    rulesTab,
    rulesTabs,
    selectRulesTab,
    betLimitInput,
    stakeStepsInput,
    onWinInput,
    onLossInput,
    armRunField,
    commitBetLimit,
    commitStep,
    onStepInput,
    onToggleStakeSteps,
    runReadback,
    runError,
    runStatus,
```

- [ ] **Step 4: Replace the AUTO CASHOUT block** — replace everything from the comment `<!-- AUTO CASHOUT — the stop rules, auto-saved on blur, …` (`:189`) through the closing `</div>` of `.orbit-rules` (`:268`) with:

```vue
    <!-- THE RULES (#208, spec §7.2, mockup C4c): two tabs. WHEN TO CASH OUT holds the stop rules
         (auto-saved on blur, Enter or a 1.2 s pause; empty = OFF; still gated on the word — they only
         fire for an auto-bet). BETS & STAKE (HOW MANY BETS on Cosmic Sprint) holds the run setup,
         locked while Orbit is on like the stake. A tab holding an active rule is dotted while the
         other one is open, so no rule hides. -->
    <div class="orbit-run">
      <OrbitRulesTabs
        :tabs="rulesTabs"
        :model-value="rulesTab"
        @update:model-value="selectRulesTab"
      />

      <div
        v-if="rulesTab === 'cash'"
        class="orbit-rules mt-2"
        :class="{ 'orbit-rules--locked': wordLocked }"
      >
        <!-- A 2-col grid so the pair spans the section (one column below 400px). The AUTO-BETS ONLY
             note now lives in the tips (the tab strip replaced its eyebrow). -->
        <div class="orbit-rules-row">
          <label class="flex min-w-0 items-center gap-2">
            <span class="dialog-eyebrow flex-none">BALANCE OVER</span>
            <ArcadeInfoTip
              :text="thresholdHelp"
              label="What cashing out on a win does"
              tone="orbit"
            />
            <!-- The unit rides inside the field, as in ADD MONEY; hidden while the rule is OFF,
                 because "off SOL" is not a value. -->
            <span class="rule-field rule-field--gain">
              <input
                v-model="thresholdInput"
                type="number"
                inputmode="decimal"
                min="0"
                step="0.01"
                placeholder="off"
                :disabled="isBusy || wordLocked"
                aria-label="Cash out when the Orbit balance goes over this amount"
                class="input-arcade tabular-nums"
                :class="asText(thresholdInput) === '' ? '' : '!pr-[2.5rem]'"
                @input="armRule('threshold')"
                @blur="commitRule('threshold')"
                @keyup.enter="commitRule('threshold')"
              />
              <span v-if="asText(thresholdInput) !== ''" class="rule-field__unit" aria-hidden="true">
                {{ orbit?.tokenLabel }}
              </span>
            </span>
          </label>

          <label class="flex min-w-0 items-center gap-2">
            <span class="dialog-eyebrow flex-none">BALANCE UNDER</span>
            <ArcadeInfoTip :text="floorHelp" label="What the cash-out floor does" tone="orbit" />
            <span class="rule-field rule-field--loss">
              <input
                v-model="floorInput"
                type="number"
                inputmode="decimal"
                min="0"
                step="0.01"
                placeholder="off"
                :disabled="isBusy || wordLocked"
                aria-label="Cash out when the Orbit balance falls below this amount"
                class="input-arcade tabular-nums"
                :class="asText(floorInput) === '' ? '' : '!pr-[2.5rem]'"
                @input="armRule('floor')"
                @blur="commitRule('floor')"
                @keyup.enter="commitRule('floor')"
              />
              <span v-if="asText(floorInput) !== ''" class="rule-field__unit" aria-hidden="true">
                {{ orbit?.tokenLabel }}
              </span>
            </span>
          </label>
        </div>

        <p
          v-if="rulesHint"
          class="dialog-copy mt-2 !text-[17px]"
          :class="rulesHint.warn ? '!text-[color:oklch(0.8_0.16_25)]' : '!text-ink-mute'"
        >
          {{ rulesHint.text }}
        </p>

        <p v-if="ruleError" class="dialog-copy mt-2 !text-[17px] !text-[color:oklch(0.8_0.16_25)]">
          {{ ruleError }}
        </p>
      </div>

      <div v-else class="mt-2">
        <!-- The controls dim and go dead while Orbit is on (decision 11). The lock explains itself as
             this wrapper's hover title — disabled controls take no pointer events, so the hover falls
             through to here (the same trick as the game tiles). -->
        <div
          class="orbit-run__controls"
          :class="{ 'orbit-run__controls--locked': setupLocked }"
          :title="setupLocked ? 'Pause auto-bet to switch games or change its setup.' : undefined"
        >
          <div class="orbit-rules-row">
            <label class="flex min-w-0 items-center gap-2">
              <span class="dialog-eyebrow flex-none">PLAY</span>
              <!-- Empty = no limit (the placeholder says so); BETS rides inside once a number is in. -->
              <span class="rule-field">
                <input
                  v-model="betLimitInput"
                  type="number"
                  inputmode="numeric"
                  min="0"
                  max="10000"
                  step="1"
                  placeholder="no limit"
                  :disabled="setupLocked"
                  aria-label="How many auto-bets Orbit plays before it pauses"
                  class="input-arcade tabular-nums"
                  :class="asText(betLimitInput) === '' ? '' : '!pr-[3.25rem]'"
                  @input="armRunField('betLimit')"
                  @blur="commitBetLimit"
                  @keyup.enter="commitBetLimit"
                />
                <span v-if="asText(betLimitInput) !== ''" class="rule-field__unit" aria-hidden="true">
                  BETS
                </span>
              </span>
            </label>

            <!-- CHANGE STAKE (solo games only — Cosmic Sprint has a fixed price). A native checkbox
                 with role="switch", visually hidden behind a drawn track: keyboard and focus stay
                 native, and the knob is a span because Firefox paints no ::after on inputs. -->
            <label v-if="hasStakeChoice" class="orbit-switch">
              <span class="dialog-eyebrow">CHANGE STAKE</span>
              <input
                type="checkbox"
                role="switch"
                class="orbit-switch__box"
                :checked="stakeStepsInput"
                :aria-checked="stakeStepsInput"
                :disabled="setupLocked || isBusy"
                @change="onToggleStakeSteps(($event.target as HTMLInputElement).checked)"
              />
              <span class="orbit-switch__track" aria-hidden="true"></span>
            </label>
          </div>

          <template v-if="hasStakeChoice && stakeStepsInput">
            <OrbitStakeStepRow
              class="mt-2"
              label="AFTER A WIN"
              :model-value="onWinInput"
              :disabled="setupLocked"
              @update:model-value="onStepInput('win', $event)"
              @commit="commitStep('win')"
            />
            <OrbitStakeStepRow
              class="mt-2"
              label="AFTER A LOSS"
              :model-value="onLossInput"
              :disabled="setupLocked"
              @update:model-value="onStepInput('loss', $event)"
              @commit="commitStep('loss')"
            />
          </template>
        </div>

        <!-- The read-back: the whole setup as one sentence, so nothing is left to guess (C4c). -->
        <p class="dialog-copy mt-2 !text-[17px] !text-ink-mute">{{ runReadback }}</p>

        <p v-if="runError" class="dialog-copy mt-2 !text-[17px] !text-[color:oklch(0.8_0.16_25)]">
          {{ runError }}
        </p>
      </div>
    </div>

    <!-- #208: the run's progress while running ("Bet 3 / 20 · next stake 0.040"), or "Finished 20/20
         bets — paused" after a run ended on its limit. State, not explanation — one line. -->
    <p
      v-if="runStatus"
      class="dialog-copy !text-[17px] tabular-nums"
      :class="runStatus.finished ? '!text-ink-mute' : '!text-[color:var(--color-live)]'"
    >
      {{ runStatus.text }}
    </p>
```

- [ ] **Step 5: Styles** — in `<style scoped>`, after the `@media (min-width: 400px) { .orbit-rules-row … }` block (`:318-322`), add:

```css
  /* ── #208 BETS & STAKE ─────────────────────────────────────────────────────────────────────── */

  .orbit-run__controls {
    transition: opacity 0.15s ease;
  }

  /* Dimmed while Orbit is on. Children take no pointer events so the hover reaches the wrapper's
     title (the lock's explanation); every control also carries a real `disabled`. */
  .orbit-run__controls--locked {
    opacity: 0.45;
  }

  .orbit-run__controls--locked :deep(*) {
    pointer-events: none;
  }

  .orbit-switch {
    display: flex;
    min-width: 0;
    align-items: center;
    justify-self: end;
    gap: 0.5rem;
    cursor: pointer;
    user-select: none;
  }

  /* Visually hidden, still focusable and still the real control. */
  .orbit-switch__box {
    position: absolute;
    width: 1px;
    height: 1px;
    margin: 0;
    opacity: 0;
  }

  .orbit-switch__track {
    position: relative;
    width: 30px;
    height: 16px;
    flex: none;
    border: 2px solid color-mix(in oklab, var(--color-orbit-deep) 60%, var(--color-void));
    background: var(--color-void-deep);
  }

  .orbit-switch__track::after {
    content: '';
    position: absolute;
    top: 2px;
    left: 2px;
    width: 8px;
    height: 8px;
    background: var(--color-ink-mute);
    transition: left 0.12s ease;
  }

  .orbit-switch__box:checked + .orbit-switch__track {
    border-color: var(--color-orbit);
    background: color-mix(in oklab, var(--color-orbit) 22%, var(--color-void-deep));
  }

  .orbit-switch__box:checked + .orbit-switch__track::after {
    left: 16px;
    background: var(--color-orbit);
  }

  .orbit-switch__box:focus-visible + .orbit-switch__track {
    outline: 2px solid var(--color-orbit);
    outline-offset: 2px;
  }

  .orbit-switch__box:disabled + .orbit-switch__track {
    opacity: 0.45;
  }
```

and inside the existing `@media (prefers-reduced-motion: reduce)` block add:

```css
    .orbit-run__controls,
    .orbit-switch__track::after {
      transition: none;
    }
```

- [ ] **Step 6: Gates**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && yarn lint && yarn build
```

Expected: exit 0 (`yarn lint:fix` first if only `prettier/prettier` reflows the template).

- [ ] **Step 7: Commit**

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add src/components/orbit/sections/OrbitAutoBetSection.vue
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: LET ORBIT PLAY rules become C4c tabs — WHEN TO CASH OUT / BETS & STAKE, read-back and run status.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 8: Verify end to end (fit + manual QA)

**Files:** none expected. Any fix found here goes into the file it belongs to, with its own commit.

**Interfaces:** consumes everything above plus the backend branch running locally (the `monkify-implement-feature` skill deploys both to the local Docker stack). Without the backend, the new fields are `undefined` and the UI shows today's behaviour (no limit, no steps) — that is AC 20's mirror and is not a failure.

- [ ] **Step 1: Final gates from a clean tree**

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && git status --short && yarn lint && yarn build
```

Expected: empty status, both exit 0.

- [ ] **Step 2: Copy audit** — confirm every exact string from Global Constraints exists once in the source:

```bash
cd E:/Monkify/.worktrees/new-options-for-orbit/monkify-front && grep -rn "WHEN TO CASH OUT\|BETS & STAKE\|HOW MANY BETS\|BALANCE OVER\|BALANCE UNDER\|no limit\|CHANGE STAKE\|AFTER A WIN\|AFTER A LOSS\|BACK TO BASE\|RAISE BY\|space-stroker.orbit.rules-tab\|Your money stays in Orbit\|bets — paused" src
```

Expected: each label appears in `OrbitAutoBetSection.vue` / `useOrbitAutoBet.ts` / `OrbitStakeStepRow.vue` / `orbitRunReadback.ts` / `useOrbitNotifications.ts`.

- [ ] **Step 3: Manual QA on the local stack (spec §13)** — with Orbit funded locally (devnet), open the modal → LET ORBIT PLAY and check:
  1. Hyper Roll, stake 0.010, BETS & STAKE: PLAY 3, CHANGE STAKE on, AFTER A LOSS → RAISE BY 100. Read-back reads *"Orbit plays 3 bets, then pauses. After a loss it doubles the stake; after a win it goes back to 0.010."* Network tab: PATCHes `{ betLimit: 3 }`, `{ stakeStepsEnabled: true }`, `{ onLossIncreasePercent: 100 }` ~600 ms after typing stops; nothing while typing.
  2. START → progress line *"Bet 1 / 3 · next stake …"* updates each round (a `GET /orbit` follows each `OrbitAutoRound`). After the 3rd round: toast *"Orbit finished 3 bets on Hyper Roll and paused. Your money stays in Orbit."* (once), line *"Finished 3/3 bets — paused"*, tag PAUSED, button **START AUTO-BET** (not RESUME).
  3. START again → progress restarts at `Bet 1 / 3`, stake back to 0.010 (AC 5).
  4. Cosmic Sprint, PLAY 2 → second tab reads **HOW MANY BETS**, no switch, read-back *"Orbit plays 2 bets, then pauses."*; exactly 2 rounds, then paused + toast.
  5. While running: PLAY, the switch and both rows are dimmed and dead; hovering shows *"Pause auto-bet to switch games or change its setup."*; BALANCE OVER / UNDER still edit (AC 12).
  6. Type `10001` in PLAY → red line under the tab, no PATCH, START reads **FIX BETS & STAKE FIRST**; type `1.234` in a RAISE BY field → same for the %. Empty PLAY → `{ clearBetLimit: true }`.
  7. Set a floor, open BETS & STAKE → WHEN TO CASH OUT carries the dot; set PLAY, open WHEN TO CASH OUT → BETS & STAKE carries the dot (AC 18). Reload → the last clicked tab reopens; in a private window → WHEN TO CASH OUT.
  8. Type a PLAY value and press Esc within 600 ms → the PATCH still goes out (close-flush).
  9. Flip a row to BACK TO BASE then back to RAISE BY → the last typed % is still there.

- [ ] **Step 4: Fit (AC 19)** — Hyper Roll + BETS & STAKE + CHANGE STAKE on + a 2-line read-back, at **1366×768** desktop and a **390×844** phone sheet (DevTools device mode), both PAUSED and RUNNING (progress line shown): the modal body must not scroll. Check in DevTools console on the modal's scroll container: `el.scrollHeight <= el.clientHeight`. If it scrolls, shorten copy first (e.g. drop the mt-2 on the read-back, then the error line's margin), never add `overflow`. Commit any fix:

```bash
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front add -A src
git -C E:/Monkify/.worktrees/new-options-for-orbit/monkify-front commit -F - <<'EOF'
Orbit #208: keep the pinned modal scroll-free on BETS & STAKE.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

- [ ] **Step 5: Clean up before any front `docker build`** — `cmd //c rmdir "E:\Monkify\.worktrees\new-options-for-orbit\monkify-front\node_modules"` and delete `dist/`; keep `yarn.lock`. Do not push.

---

## Self-review (against spec §7, §10.2, §12)

- §7.1 types → Task 1; `orbitService.ts` unchanged (passes the patch through) ✓; hub event → Task 3 Steps 1-4; toast + quiet refresh → Task 3 Steps 5-8 (deviations 1-3); `effectiveStakeFor` → Task 4 (deviation 4).
- §7.2 tabs, labels, switch, rows, read-back, progress, finished line, START label, locks, tab memory, fit → Tasks 5-8.
- §7.3 refs synced from `orbit`, 600 ms from `@input`, empty ⇒ `clearBetLimit`, BACK TO BASE ⇒ clear flags, switch immediate, close-flush, START fold, client validation with inline red line → Task 6.
- §10.2 rows 1-8 → Tasks 1, 3, 3, 4, 6, 7, 5, 5.
- AC 14-19 → Task 8 Step 3-4; AC 20 is backend-side (old front, new backend), and the new front degrades to today's behaviour on an old API (`!= null` guards, `?? null` / `?? 0` reads).
