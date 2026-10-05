# Orbit auto-bet for Monkey Duel & Hyper Roll — Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Orbit modal's auto-bet section becomes mockup **A2 · game tiles** (Cosmic Sprint / Monkey Duel / Hyper Roll with per-game fields), and the Duel and Hyper Roll screens follow Orbit's auto rounds live with the lever locked.

**Architecture:** Contract-first against the backend spec (`OrbitDto` + `OrbitEligibleGameTypeDto` new fields, `PATCH /orbit/settings` new fields, private hub event `OrbitAutoRound`). The section is extracted from `OrbitModal.vue` into `OrbitAutoBetSection.vue` with three small presentational children. `useGame` gains `attachRound(roundId)` and follows the hub event.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, composables in `src/hooks`, SignalR client in `useGameHub.ts`, yarn.

**Spec:** `monkify-meta/specs/auto-bet-duel-hyper-roll.md` §7. **Design:** <https://claude.ai/artifact/Uzzm7DYGqqV9GHeYkE3yj7> (variant **A2**). **Worktree:** `E:\Monkify\.worktrees\auto-bet-duel-hyper-roll\monkify-front` on `feature/auto-bet-duel-hyper-roll`.

## Global Constraints

- Work only in the worktree; never touch `E:\Monkify\monkify-front` (hub).
- **Install:** do not `yarn install` from scratch. Copy `E:\Monkify\monkify-front\yarn.lock` into the worktree if missing, and junction the hub's `node_modules` into the worktree (`cmd //c mklink /J node_modules E:\Monkify\monkify-front\node_modules` from the worktree root) — the hub's node_modules is populated.
- **Checks:** `yarn tsc --noEmit` (or the repo's `type-check` script — read `package.json`), `yarn lint` (if defined) and `yarn build`. `prettier --check` false-alarms on every Windows CRLF file — judge prettier only on files you changed, and write LF in new files (prettier `endOfLine: lf`; the docker build runs prettier and fails on CRLF / reflow).
- Everything stays behind `orbitEnabled` (`src/constants/featureFlags.ts`).
- Copy verbatim from the spec: *"Pause auto-bet to switch games or change its setup."*, *"Waiting for the house…"*, *"Orbit is auto-playing this game — pause it to bet manually."* (existing), *"Same word as Cosmic Sprint. The house draws its word on every duel."*, *"A different multiplier every round, picked from the round's committed seed."*, odds hint *"Pays N× · wins about 1 in X · every pick on this board pays back R%."*
- Palette: selected tile = violet (`--color-orbit`, as in the modal today); START violet fill `.btn-dialog--orbit`; PAUSE violet outline `.btn-dialog--orbit-outline` (unchanged).
- Commit after each task (message ends with the trailer `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`); don't push.

## Facts from the codebase (verified)

- `src/components/orbit/OrbitModal.vue` holds the auto-bet section inline: `<OrbitSection id="autobet" v-if="autoBetType" :title="autoBetTitle">`, `autoBetType = computed(() => orbit.value?.eligibleGameTypes[0] ?? null)`, `autoBetTitle = LET ORBIT PLAY ${name}`. Word: `OrbitWordField` (props `v-model`, `length`, `allowed`, `disabled`, `taken`) autosaving after 600 ms via `commitWord`; preset types use a `<select>`. Random checkbox → `onToggleAutoPick`. Rules `.orbit-rules` (`armRule` 1.2 s, `commitRule`, `flushPendingRules`). START/PAUSE `onToggle` → `updateSettings(wallet, { gameParametersId: autoBetType.id, enabled })`; label from `toggleState`; blockers incl. `hasFundsForRound` (uses `runTarget.requiredAmount`, `runTarget = autoBetType`). Help popover ~l.831 says *"It can also auto-play Cosmic Sprint for you"*.
- `src/types/orbit.ts`: `OrbitEligibleGameType { id, name, requiredAmount, choiceRequiredLength, allowedCharacters, isFreeText, presetChoices }`, `OrbitState`, `OrbitSettingsPatch`, `OrbitWinNotification`.
- `src/hooks/useOrbit.ts`: `updateSettings(wallet, patch)` → `orbitService.patchOrbitSettings(token, patch)` (`PATCH /orbit/settings`, header `X-Orbit-Session`) returns the full `OrbitState`. `orbitStatus` (~l.944) uses `needsFirstDeposit || isPausedForFunds`, both comparing against the type's `requiredAmount`.
- `src/hooks/useGameHub.ts`: `joinOrbitChannel(token, onWin)` → `JoinOrbitNotifications`; event `'OrbitNotification'`; re-joined in `onreconnected`. Driven by `src/hooks/useOrbitNotifications.ts` `start()` (called from `GameHeader.vue`).
- `src/hooks/useGame.ts`: `orbitAutoPlaysThisGame` (Orbit enabled && `gameParametersId === selectedGame.gameTypeId`). `getBets` → `loadHouseEdgeRound(typeId)` / `loadDuelRound(typeId)`. `mainService.getHouseEdgeRound(roundId)` (`GET /games/rounds/{id}` — backend now also serves House duels). `initalizeWebsocketConnections()` (sic), `disconnectAllPreviousConnections()`, `updateNewGame`, stored-board helpers `readStoredBoard/storeBoard` (`hyperRoll:board:{typeId}`).
- `src/components/game/DuelConsole.vue` ignores `orbitAutoPlaysThisGame`; `GameConsole.vue` uses `inputsLocked = !allowBets || orbitAutoPlaysThisGame` and `OrbitBetLock` (`components/orbit/OrbitBetLock.vue`).
- `src/utils/houseEdge.ts`: `boardTier(board, mult)`, `tierColor`, `tierRim`, `formatMultiplier`, `stepStake`, `parseStake`. `src/utils/gameArt.ts` `archetypeOf(game: ActiveGame)` / `artFor(game)`; `src/utils/gameArchetype.ts` `resolveGameArchetype`.

---

### Task 1: Contract — types, service, useOrbit, hub event

**Files:** `src/types/orbit.ts`, `src/services/orbitService.ts`, `src/hooks/useOrbit.ts`, `src/hooks/useGameHub.ts`, `src/hooks/useOrbitNotifications.ts`

- [ ] **Step 1: Types**

```ts
export interface OrbitBoardSlot { multiplier: number; weight: number }
export interface OrbitBoardMode { name: string; wordCount: number; rtpPercent: number; slots: OrbitBoardSlot[] }

// OrbitEligibleGameType +=
  isHouseEdge: boolean
  isDuel: boolean
  maximumStake: number | null
  modes: OrbitBoardMode[]

// OrbitState +=
  stake: number | null
  boardMode: string | null
  multiplier: number | null
  waitingForHouse: boolean
  currentAutoRoundId: string | null

// OrbitSettingsPatch +=
  stake?: number
  clearStake?: boolean
  boardMode?: string
  clearBoardMode?: boolean
  multiplier?: number
  clearMultiplier?: boolean

export interface OrbitAutoRoundEvent { gameTypeId: string; roundId: string | null; waitingForHouse: boolean }
```

- [ ] **Step 2: `useOrbit.ts`** — add `effectiveStakeFor(type: OrbitEligibleGameType): number` = `type.isHouseEdge || type.isDuel ? (orbit.value?.stake ?? type.requiredAmount) : type.requiredAmount`, export it, and use it in `needsFirstDeposit` / `isPausedForFunds` instead of `requiredAmount`. Add `applyAutoRound(evt: OrbitAutoRoundEvent)` that patches `orbit.value.waitingForHouse = evt.waitingForHouse` and, when `evt.roundId`, `orbit.value.currentAutoRoundId = evt.roundId` (no network).
- [ ] **Step 3: Hub** — in `useGameHub.ts` `joinOrbitChannel(token, onWin, onAutoRound?)`: register `connection.on('OrbitAutoRound', onAutoRound)` beside the win handler (and off on leave); keep the reconnect re-join. In `useOrbitNotifications.ts` pass a handler that calls `useOrbit().applyAutoRound(evt)` and sets an exported `lastAutoRound = ref<OrbitAutoRoundEvent | null>(null)`.
- [ ] **Step 4: Service** — `patchOrbitSettings` already sends the patch object; confirm no field whitelist strips new keys (fix if it does).
- [ ] **Step 5: Type-check → clean. Commit** — `Orbit types and hub event for solo auto-bet (#200).`

### Task 2: Presentational components

**Files:** Create `src/components/orbit/OrbitGameTiles.vue`, `src/components/orbit/OrbitStakePlate.vue`, `src/components/orbit/OrbitMultiplierPicker.vue`; modify `src/utils/gameArt.ts` (or `gameArchetype.ts`) to add an adapter.

- [ ] **Step 1: Art adapter** — `export function orbitTypeArchetype(t: OrbitEligibleGameType): 'monkey-duel' | 'hyper-roll' | 'preset' | 'race'` keyed on `t.isDuel` → `'monkey-duel'`, `t.isHouseEdge` → `'hyper-roll'`, `t.presetChoices.length` → `'preset'`, else `'race'`; and `orbitTypeArt(t)` returning the same `{ poster, gif, fit }` `artFor` returns for that archetype (refactor `artFor` to delegate to an archetype-keyed lookup if needed — do not match by name or id).
- [ ] **Step 2: `OrbitGameTiles.vue`** — props `types: OrbitEligibleGameType[]`, `modelValue: string | null`, `disabled: boolean`; emits `update:modelValue`. A 3-column grid (collapses to 1 column under ~360px); each tile is a `<button type="button" role="radio" :aria-checked>` inside `role="radiogroup"`, showing the art (poster image, `object-fit` from `fit`), the name (display font, small), and a description: race → `race · fixed ${requiredAmount} SOL`; duel → `1v1 vs the house`; house-edge → `pick a multiplier`. Selected: violet border + inset ring. Disabled: `opacity .45`, no pointer events.
- [ ] **Step 3: `OrbitStakePlate.vue`** — props `modelValue: number`, `min: number`, `max: number`, `disabled: boolean`; emits `update:modelValue`. Row: `−` key / LCD (`modelValue.toFixed(3)` + `SOL`) / `+` key / `MIN` / `MAX`. Step 0.01 via `stepStake` from `utils/houseEdge.ts`, clamped to `[min, max]`; MIN/MAX jump to bounds. Label above: `STAKE PER ROUND`. Keys are `<button type="button">` with aria-labels ("Lower stake", "Raise stake", "Minimum stake", "Maximum stake").
- [ ] **Step 4: `OrbitMultiplierPicker.vue`** — props `modes: OrbitBoardMode[]`, `mode: string | null`, `multiplier: number | null`, `disabled: boolean`; emits `update:mode`, `update:multiplier` (null = RANDOM). `1 · BOARD`: three mode tiles (`name`, `${wordCount} words · ≤${max}×`). Effective mode = `mode ?? modes[0]?.name`. `2 · MULTIPLIER`: distinct multipliers of the effective mode's `slots` (ascending), each chip coloured by `boardTier(slots.map(s => s.multiplier), m)` → `tierColor`, plus an `RND` chip (violet). Hint: RND → *"A different multiplier every round, picked from the round's committed seed."*; else take the **first** slot with that multiplier, `odds = 1_000_000 / weight` rounded to 1 dp → *"Pays ${m}× · wins about 1 in ${odds} · every pick on this board pays back ${rtpPercent}%."* Selecting a mode whose ladder lacks the current multiplier emits `update:multiplier` null.
- [ ] **Step 5: Type-check + lint → clean. Commit** — `Orbit game tiles, stake plate and multiplier picker (#200).`

### Task 3: `OrbitAutoBetSection` — mockup A2 in the modal

**Files:** Create `src/components/orbit/sections/OrbitAutoBetSection.vue`; modify `src/components/orbit/OrbitModal.vue`.

- [ ] **Step 1: Extract** the inline auto-bet section (word field, random checkbox, rules, START/PAUSE, blockers, `toggleState`) from `OrbitModal.vue` into `OrbitAutoBetSection.vue` **without behaviour change**; the modal renders `<OrbitAutoBetSection />` inside `<OrbitSection id="autobet" title="LET ORBIT PLAY">`. Keep `flushPendingRules` reachable on modal close (expose it via `defineExpose` and call it from the modal's close path, or move the close hook). Type-check; manually diff that every handler moved intact.
- [ ] **Step 2: Selected type** — `selectedType = computed(() => types.find(t => t.id === orbit.gameParametersId) ?? types[0] ?? null)` (replaces `eligibleGameTypes[0]`). `<OrbitGameTiles :types :model-value="selectedType?.id" :disabled="running" @update:model-value="id => updateSettings(wallet, { gameParametersId: id })">` at the top.
- [ ] **Step 3: Per-type fields**
  - Cosmic Sprint (`!isDuel && !isHouseEdge`): today's word field / preset select + random checkbox.
  - Monkey Duel: a `YOU · {word || '??????'}  VS  HOUSE` strip (red/blue as the duel console), the same `OrbitWordField` (length = `choiceRequiredLength`), the random checkbox, the hint *"Same word as Cosmic Sprint. The house draws its word on every duel."*, then `OrbitStakePlate`.
  - Hyper Roll: `OrbitMultiplierPicker` (emits → `updateSettings(wallet, { boardMode })` / `{ multiplier }` or `{ clearMultiplier: true }`), then `OrbitStakePlate`.
  - Stake plate `v-model` = local ref initialised from `orbit.stake ?? type.requiredAmount`; commit `updateSettings(wallet, { stake })` debounced 600 ms (flush on modal close with the rules).
  - Rules block + START/PAUSE unchanged for all three.
- [ ] **Step 4: Locks & states** — `running = orbitStatus === 'running'`: tiles, mode tiles, multiplier chips and stake plate `disabled`, with the line *"Pause auto-bet to switch games or change its setup."* above them. Word field and rules stay enabled (spec decision 12). When `orbit.waitingForHouse && running`: show *"Waiting for the house…"* (muted, under the section subtitle). `hasFundsForRound` uses `effectiveStakeFor(selectedType)`. The duel "pick a word first" blocker applies when `!choice && !autoPickChoice`; Hyper Roll needs no word (no word blocker for it).
- [ ] **Step 5: Subtitle per type** — Sprint: *"Orbit plays one Cosmic Sprint round for you every round, and cashes out when your rules say so."* (existing); Duel: *"Orbit duels the house for you, one duel after another, with your word and stake."*; Hyper Roll: *"Orbit rolls a fresh board for you, one round after another, and bets the multiplier you pick."*
- [ ] **Step 6: Copy** — help popover (~l.831): *"It can also auto-play Cosmic Sprint, Monkey Duel or Hyper Roll for you — one game at a time."*; update the two code comments that call the section hard-wired.
- [ ] **Step 7: Type-check, lint, build → clean. Commit** — `Orbit modal: pick the auto-bet game with tiles, per-game setup (#200).`

### Task 4: Game screens follow auto rounds; duel lock

**Files:** `src/hooks/useGame.ts`, `src/components/game/DuelConsole.vue` (and `DuelPlate.vue` if the lever lives there).

- [ ] **Step 1: `attachRound(roundId: string)`** in `useGame.ts`: `disconnectAllPreviousConnections()`; the per-round resets `updateNewGame` performs (factor them into a `resetRoundState()` if they are inline); `const data = await mainService.getHouseEdgeRound(roundId)`; `gameInstance.value = data`; for Hyper Roll `storeBoard(typeId, roundId)` so a reload resumes; `initalizeWebsocketConnections()`. Errors → log and leave the current state.
- [ ] **Step 2: Follow** — `watch(lastAutoRound, evt => { if (evt?.roundId && orbitAutoPlaysThisGame.value && evt.gameTypeId === selectedGame.value?.gameTypeId) attachRound(evt.roundId) })`.
- [ ] **Step 3: Load path** — at the top of `loadHouseEdgeRound` and `loadDuelRound`: `if (orbitAutoPlaysThisGame.value && orbit.value?.currentAutoRoundId) return attachRound(orbit.value.currentAutoRoundId)`. While `orbitAutoPlaysThisGame`, never call `openHouseEdgeBoard` / open a duel from the screen (guard `openFreshBoard`, `rerollBoard`, `setMode`, lever handlers) — between rounds the last result stays on screen.
- [ ] **Step 4: Duel lock** — in `DuelConsole.vue` import `orbitAutoPlaysThisGame` from `useGame` and add it to the lever/OK disable condition exactly as `GameConsole.vue` does, wrapping the controls in `OrbitBetLock` with the shipped message.
- [ ] **Step 5: Type-check, lint, build → clean. Commit** — `Duel and Hyper Roll screens follow Orbit auto rounds; lock the duel lever (#200).`

### Task 5: Verify

- [ ] `yarn build` in the worktree → success. Grep the built bundle (`dist/js/*.js`) for `OrbitAutoRound` and `Waiting for the house` to prove the code is in.
- [ ] `prettier --check` only on the files changed in Tasks 1–4 → clean (convert any CRLF you introduced to LF).
- [ ] Commit any fixes.
