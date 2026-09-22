# Hyper Roll Game Modes — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a rotary **MODE DIAL** in the Hyper Roll console where the `.01/.02/.05/MAX` stake chips sit today, so the player picks AVERAGE / SUPER / HYPER, and make the rest of the app speak in modes.

**Architecture:** `HyperRollModeDial.vue` is **presentational** (props in, events out) exactly as `HyperRollStakeChips.vue` was, so the practice console mounts it without importing `useGame`. Turning the dial calls the existing solo-round **reroll** with a mode, so the board redraws in place with a fresh seed. The chosen mode is remembered in `localStorage` beside the board id.

**Tech Stack:** Vue 3 + TypeScript, Tailwind, Vite/vue-cli (`VUE_APP_*`), yarn.

## Global Constraints

- Spec: `monkify-meta/specs/hyper-roll-modes.md` §8. Worktree `E:\Monkify\.worktrees\hyper-roll-modes\monkify-front`, branch `feature/hyper-roll-modes`.
- ⚠️ **The knurl must stay low-contrast.** At full contrast the radial conic-gradient stripes swallow the pointer notch and the dial's position becomes unreadable — a real defect found in the mockup pass. The notch also carries a light outline.
- ⚠️ **Prettier runs in the docker build with `endOfLine: lf`.** Author every new file with LF endings, and run `yarn prettier --write` on what you touch before building, or the container build fails.
- ⚠️ **yarn.lock is gitignored in a worktree.** Before any docker build, copy the hub's `monkify-front/yarn.lock` in, or the build silently boots a **stale** image.
- The tier-colour scale in `src/utils/houseEdge.ts` is **global and unchanged** — a colour means the same multiplier on every mode.
- `localStorage` access is always wrapped in `try/catch` (it throws in some embedded/private contexts), following `readStoredBoard`.
- Display-only rule: the backend decides stake bounds, winner and payout. `utils/houseEdge.ts` only describes them.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/components/game/HyperRollModeDial.vue` | **New** — the rotary control, desktop + phone variants |
| `src/components/game/HyperRollStakeChips.vue` | **Deleted** |
| `src/components/game/GameConsole.vue` | Mounts the dial where the chips were |
| `src/components/practice/PracticeConsole.vue` | Same, for practice |
| `src/hooks/useGame.ts` | Mode state, persistence, `setMode` |
| `src/hooks/usePracticeHyperRoll.ts` | Offline mode state |
| `src/constants/practiceGame.ts` | Per-mode ladders, pool keyed by length |
| `src/services/mainService.ts` | `mode` on the two board calls |
| `src/types/index.ts` | `BoardMode`, `modes`, `boardMode` |

---

### Task 1: Types and service

**Files:**
- Modify: `src/types/index.ts`, `src/services/mainService.ts`

**Interfaces:**
- Produces: `BoardMode { name: string; wordCount: number; rtpPercent: number; topMultiplier: number }`; `GameParameters.modes?: BoardMode[] | null`; `ActiveGame.boardMode?: string | null`; `mainService.openHouseEdgeBoard(gameTypeId, mode?)`, `mainService.rerollHouseEdgeBoard(gameId, mode?)`.

- [ ] **Step 1: Add the type**

```ts
/**
 * #193 — one selectable Hyper Roll board, in dial order. `topMultiplier` is what the dial face shows
 * (in its tier colour) and `wordCount` is how many keys light up.
 */
export interface BoardMode {
  name: string;
  wordCount: number;
  rtpPercent: number;
  topMultiplier: number;
}
```

Add `modes?: BoardMode[] | null;` next to `rtpPercent` on the game-parameters interface, and
`boardMode?: string | null;` on the active-game, verification and history interfaces.

- [ ] **Step 2: Send the mode**

```ts
const openHouseEdgeBoard = async (gameTypeId: string, mode?: string | null) => {
  const { data } = await mainAPI.post<DefaultResponse<GameInstance>>(
    `/games/${gameTypeId}/boards`,
    mode ? { mode } : undefined
  );
  return data;
};

const rerollHouseEdgeBoard = async (gameId: string, mode?: string | null) => {
  const { data } = await mainAPI.post<DefaultResponse<GameInstance>>(
    `/games/rounds/${gameId}/reroll`,
    mode ? { mode } : undefined
  );
  return data;
};
```

- [ ] **Step 3: Type-check**

Run: `yarn lint` (or `yarn build` if the repo has no separate type-check script)
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add src/types src/services
git commit -m "Carry the Hyper Roll board mode on the wire."
```

---

### Task 2: The MODE DIAL component

**Files:**
- Create: `src/components/game/HyperRollModeDial.vue`

**Interfaces:**
- Consumes: `BoardMode` (Task 1), `tierColor` from `@/utils/houseEdge`.
- Produces: props `{ modes: BoardMode[]; active: string; disabled?: boolean; compact?: boolean }`, emit `change(name: string)`.

- [ ] **Step 1: Build it**

Structure (mirroring mockup **M2**):

- A plate (`~82px` tall desktop, `~94px` compact) holding a `58px` (compact `70px`) round knob.
- Knob background: a **low-contrast** `repeating-conic-gradient` knurl. Reference values from the approved mockup: `#e4eaf6 0 6deg, #c3ccdf 6deg 12deg, #aab5cc 12deg 18deg` — do not raise the contrast.
- Pointer notch: `6×15px`, `#1b2038`, with `box-shadow: 0 0 0 2px rgba(255,255,255,.75)` so it reads against the knurl.
- Knob rotation by index: `-90deg` / `0deg` / `90deg` for a three-mode dial; derive as
  `(index - (modes.length - 1) / 2) * 90` so a future 2- or 4-mode config still lands sensibly.
- Face: an inset circle (`inset: 11px`) in the LCD colour showing `formatMultiplier(mode.topMultiplier)`, coloured with `tierColor(mode.topMultiplier)`.
- The three names around the plate: left / top / right, active inked dark, others dimmed.
- ⚠️ Small numerals use the **body** font (VT323), never Press Start 2P — below 12px it renders `6` as `G` and `8` as `&`.
- Desktop: clicking the knob advances to the next mode; clicking a name selects it directly.
- `compact` (phone): the three **names become the tap targets** in a full-width row and the knob animates round to follow.
- `disabled` greys the whole plate and blocks both interactions.

- [ ] **Step 2: Eyeball it**

Run: `yarn serve` and open the Hyper Roll screen (or mount it in isolation).
Expected: at every position the pointer is unmistakable, and the face reads `7×` blue / `9×` purple / `10×` gold.

- [ ] **Step 3: Commit**

```bash
git add src/components/game/HyperRollModeDial.vue
git commit -m "Add the Hyper Roll MODE DIAL."
```

---

### Task 3: Mode state in `useGame`

**Files:**
- Modify: `src/hooks/useGame.ts`

**Interfaces:**
- Consumes: `mainService` (Task 1).
- Produces: `activeMode` (`Ref<string>`), `boardModes` (`ComputedRef<BoardMode[]>`), `setMode(name: string): Promise<boolean>`; `rerollBoard(mode?: string | null)`.

- [ ] **Step 1: Persist the choice**

Beside `boardStorageKey` (`:86`):

```ts
// #193 — the browser remembers which board the player last used, so a reload does not re-teach the dial.
const modeStorageKey = (gameTypeId: string) => `hyperRoll:mode:${gameTypeId}`;
const readStoredMode = (gameTypeId: string): string | null => {
  try {
    return localStorage.getItem(modeStorageKey(gameTypeId));
  } catch {
    return null;
  }
};
const storeMode = (gameTypeId: string, mode: string | null) => {
  try {
    if (mode) localStorage.setItem(modeStorageKey(gameTypeId), mode);
    else localStorage.removeItem(modeStorageKey(gameTypeId));
  } catch {
    // storage blocked — the dial simply starts on the default each load
  }
};
```

- [ ] **Step 2: Send the mode when opening**

At `:652`, pass the stored mode: `mainService.openHouseEdgeBoard(gameTypeId, readStoredMode(gameTypeId))`.
⚠️ An unknown stored mode is **not** an error — the server falls back to the default and returns the mode it actually used, which the next step adopts.

- [ ] **Step 3: Adopt whatever the server says**

Wherever `gameInstance.value` is assigned from a board response (open, reroll, round re-read), also take
`boardMode`. Keep `activeMode` in sync with `gameInstance.value?.boardMode` so the dial always shows the
board that is actually on screen — including after the `> THE BOARD CHANGED. PICK A KEY AGAIN.` resync,
where another tab may have switched modes.

- [ ] **Step 4: Let the reroll carry a mode**

Change `rerollBoard` to `rerollBoard(mode?: string | null)` and forward it:
`mainService.rerollHouseEdgeBoard(round.gameId, mode)`. Also copy `boardMode: data.boardMode` into the
patched `gameInstance.value`, next to `boardChoices` and `serverSeedHash`.

- [ ] **Step 5: `setMode`**

```ts
/** Turn the dial: redraw the unbet board in the new mode (same round, new seed). */
const setMode = async (name: string): Promise<boolean> => {
  const round = gameInstance.value;
  if (!round || !selectedGame.value?.isHouseEdge) return false;
  if (name === activeMode.value) return false;
  // The dial is locked once money is committed: the same guards the lever's reroll uses.
  if (round.status !== GameStatus.WaitingBets || round.bets.length > 0) return false;

  storeMode(round.parametersId ?? selectedGame.value.gameTypeId, name);
  return rerollBoard(name);
};
```

- [ ] **Step 6: Export**

Add `activeMode`, `boardModes`, `setMode` to the hook's return object (beside `rerollBoard`, `:1779`).

- [ ] **Step 7: Build**

Run: `yarn build`
Expected: succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/hooks/useGame.ts
git commit -m "Remember and switch the Hyper Roll board mode."
```

---

### Task 4: Swap the chips for the dial in both consoles

**Files:**
- Modify: `src/components/game/GameConsole.vue` (import `:21`, mount `:836`, style comment `:939`, hint comment `:738`)
- Modify: `src/components/practice/PracticeConsole.vue` (import `:31`, comment `:42`, mount `:556`)
- Delete: `src/components/game/HyperRollStakeChips.vue`

- [ ] **Step 1: Replace the live mount**

`GameConsole.vue:836` becomes:

```html
<hyper-roll-mode-dial
  v-if="isHouseEdge"
  :class="wide ? 'mt-1' : 'mt-[6px]'"
  :modes="boardModes"
  :active="activeMode"
  :disabled="!allowBets || !!myBetThisRound"
  :compact="!wide && isPhone"
  @change="setMode($event)"
/>
```

Swap the import on `:21`, drop `setStake`/`setMaxStake` from the destructure **only if** nothing else
uses them, and update the two comments that reference the chips (`:738`, `:939`).

- [ ] **Step 2: Replace the practice mount**

`PracticeConsole.vue:556` becomes the same tag driven by the practice engine's
`practice.modes` / `practice.activeMode` / `practice.setMode`. Update the `:42` comment — the dial is now
the one component shared with the live game.

- [ ] **Step 3: Delete the chips**

```bash
git rm src/components/game/HyperRollStakeChips.vue
```

Then `grep -rn "HyperRollStakeChips\|hyper-roll-stake-chips" src` must return **nothing**.

- [ ] **Step 4: Check the key row at 5 and 6 keys**

The board renders from `boardChoices.length` already. Confirm the row centres at 5/6/8 without resizing a
key or moving the lever, on desktop, wide and phone.

- [ ] **Step 5: Build + commit**

```bash
yarn prettier --write src/components/game/GameConsole.vue src/components/practice/PracticeConsole.vue
yarn build
git add -A src/components
git commit -m "Put the MODE DIAL where the stake chips were."
```

---

### Task 5: Practice gets all three modes

**Files:**
- Modify: `src/constants/practiceGame.ts`, `src/hooks/usePracticeHyperRoll.ts`

**Interfaces:**
- Produces: `PRACTICE_BOARD_MODES: { name; rtpPercent; topMultiplier; slots: { length; multiplier }[] }[]`, `PRACTICE_BOARD_WORDS_BY_LENGTH: Record<number, string[]>`, `drawPracticeBoard(modeName: string)`.

- [ ] **Step 1: Re-key the pool by length**

`PRACTICE_BOARD_WORDS` is keyed by **multiplier** today (`4: ['banana', …]`). Re-key it by **length**
(`6: ['banana', …]`) and add the 4- and 5-letter tiers from spec §6.2. Rename to
`PRACTICE_BOARD_WORDS_BY_LENGTH` so a stale call site fails loudly instead of silently reading the wrong tier.

- [ ] **Step 2: Add the three ladders**

Mirror spec §4.1 exactly — the same numbers the backend config carries. Keep the comment that says these
mirror `spacestrokersettings.json` by hand.

- [ ] **Step 3: Draw by length**

`drawPracticeBoard(modeName)` selects the mode's slots, picks a distinct unused word of each slot's
length, takes the multiplier from the slot, and shuffles — mirroring `HouseEdgeBoard.Draw`.

- [ ] **Step 4: Hold the mode offline**

`usePracticeHyperRoll` keeps `activeMode`, exposes `modes` and `setMode(name)`, and redraws locally with
**no** network call. Edson's "Eight words, eight keys…" beat (~`:519`) must follow the key count.

- [ ] **Step 5: Build + commit**

```bash
yarn build
git add src/constants src/hooks
git commit -m "Teach practice mode all three Hyper Roll boards."
```

---

### Task 6: The copy that still says "8 words, up to 10×"

**Files:**
- Modify: `src/components/guide/GuideModeCards.vue` (~`:108`), `src/components/guide/sections/GuideHyperRoll.vue`
- Modify: `src/pages/GameStatsPage.vue`, `src/components/game/GameStatsPanel.vue` (~`:64`)
- Modify: `src/components/game/GameTerminal.vue`, `src/components/game/GameFairPanel.vue`
- Modify: the games-history row component

- [ ] **Step 1: Per-mode RTP**

`GameStatsPage.vue` (the mounted screen) shows one RTP row per `mode` instead of the single
`${formatRtp(rtpPercent)}% RTP` badge. Mirror the change in `GameStatsPanel.vue` so the two never drift,
even though it is not mounted.

- [ ] **Step 2: Guide copy**

`GuideModeCards.vue:108`'s *"Pick 1 of 8 words and set your stake … up to 10× … 91.84% RTP"* becomes
mode-aware: name the three boards and their word counts, and drop the single hardcoded RTP.
`GuideHyperRoll.vue`'s five steps gain the dial.

- [ ] **Step 3: Terminal + fairness + history**

`GameTerminal.vue`'s `> PICK A KEY [1-8] TO LOCK A WORD.` must follow `boardChoices.length`.
`GameFairPanel.vue` names `boardMode` beside the board it verifies.
The history row shows `boardMode` for a house-edge round.

- [ ] **Step 4: Prove nothing hardcodes the old board**

```bash
grep -rn "1-8\]\|1 of 8 words\|91\.84" src
```

Expected: only comments that deliberately describe HYPER.

- [ ] **Step 5: Build + commit**

```bash
yarn prettier --write src
yarn build
git add -A src
git commit -m "Teach the guide, stats and terminal about the three boards."
```

---

## Self-Review

**Spec coverage:** §8.1 types → Task 1. §8.2 dial → Task 2. §8.3 consoles → Task 4. §8.4 useGame → Task 3. §8.5 service → Task 1. §8.6 practice → Task 5. §8.7 copy → Task 6.

**Placeholders:** none — every step names the file, the line and the change.

**Type consistency:** `BoardMode` (Task 1) is the prop type in Task 2 and the return of `boardModes` in Task 3. `setMode(name: string)` is defined in Task 3 and bound in Task 4. `drawPracticeBoard(modeName)` is defined in Task 5 and used only there.
