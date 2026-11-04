# Key Mines — Frontend + Landing Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Key Mines game page, lobby/guide/practice/history/verify surfaces in `monkify-front`, and the LP card + terms in `monkify-lp`, against the backend contract in `specs/key-mines.md` §6.

**Architecture:** One pure util (`utils/keyMines.ts`) mirrors the C# ladder; one composable (`useKeyMines.ts`) owns the round (board + token, presses, cash-out, restore) and plugs into `useGame.ts`'s existing bet flow (`runBet` with `choice = 'J' + M`). New self-contained `KeyMinesConsole`/`KeyMinesScreen` are the **first** branch in `GameConsole`/`GameTerminal`, ahead of every `isHouseEdge` branch.

**Tech Stack:** Vue 3 + TypeScript (Vue CLI), Tailwind, axios services, SignalR; LP: Astro 5 + Vue 3 + Tailwind v4.

**Spec:** `monkify-meta/specs/key-mines.md` (§7, §10.2, §10.3). Worktrees: `E:\Monkify\.worktrees\key-mines\monkify-front` (branch `feature/key-mines` off `main`), `E:\Monkify\.worktrees\key-mines\monkify-lp` (off `master`).

## Global Constraints

- Work only in the two worktrees above. Never touch the hubs.
- Front worktree: before any `yarn` command, junction the hub's `node_modules` in and copy the hub `yarn.lock` (memory: front-worktree-yarnlock-gotcha). ⚠️ Remove the junction **before** any `git worktree remove`.
- `VUE_APP_*` env names only. No new env flag (the backend's `comingSoon`/`underMaintenance` are the gates).
- Archetype id `'keyMines'` (TS union), art/CSS key `'key-mines'`, resolved by `game.isKeyMines` **before** `isHouseEdge`. Never by name or id.
- Constants: keys `qwertyuiopasdfghjklzxcvbnm`, choice `'J' + M`, jam options from `game.jamOptions` (shipped `[1,3,5,10]`), header `X-Round-Token`, routes `POST /games/rounds/{id}/press` `{ key }` and `POST /games/rounds/{id}/cashout`.
- Copy: badges `1P · VS HOUSE` and `UP TO 250X`; lobby tag `26 KEYS · PICK YOUR JAMS`; result `YOU WON! CONGRATS!`, `PAID:`, `AUTO CASH`; refusal = the server message. Guide copy: no em dashes, no litotes, no stale figures.
- Money display: `formatPayout` (`utils/money.ts:201`) for paid amounts; multipliers to 2 dp with `×`.
- `yarn build` must pass (prettier `endOfLine: lf` — memory: front-build-crlf-prettier-gotcha; write new files with LF).
- Boot-check the app in a headless browser after Task 4 (memory: front-module-level-watch-wallet-crash — no module-scope `watch` that reaches `useWallet()`).

---

### Task 1: Types, `utils/keyMines.ts`, provably-fair jam draw

**Files:**
- Modify: `src/types/index.ts` — `ActiveGame` (23): `isKeyMines?: boolean; jamOptions?: number[]; maxPayoutPerRound?: number;` · `GameInstance` (476): `keyMinesJamCount?: number | null; keyMinesPressedKeys?: string | null; keyMinesStep?: number | null; keyMinesEnd?: KeyMinesEnd | null; keyMinesCashedStep?: number | null; keyMinesJamKeys?: string | null; playToken?: string | null;` · new `export type KeyMinesEnd = 'cashout' | 'cap' | 'clear' | 'idle' | 'jam' | 'refund';` · new `export interface KeyMinesMove { result: 'safe' | 'jam' | 'cashed'; step: number; multiplier: number; payout: number | null; ended: boolean; end: KeyMinesEnd | null; jamKeys: string | null; serverSeed: string | null; }` · `GameVerification` (592): `keyOrder?, jamCount?, pressedKeys?, keyMinesEnd?, cashedStep?, jamKeys?` · `GameHistoryRow` (~229): `keyMinesJamCount?, keyMinesCashedStep?, keyMinesEnd?` · `ProfileMode` (376): add `'keyMines'`.
- Create: `src/utils/keyMines.ts`
- Modify: `src/utils/provablyFair.ts` (add `keyMinesJams` beside the weighted draw ~123, KEEP-IN-SYNC comment), `public/verify.html` (same function inline + a `KEYS` render branch, see Task 5)

**Interfaces — Produces:**
```ts
export const KEY_MINES_KEYS = 'qwertyuiopasdfghjklzxcvbnm';
export const KEY_COUNT = 26;
export const binomial = (n: number, k: number): bigint;
export const multiplier = (jams: number, step: number, rtpPercent: number): number;   // display
export const payLamports = (stakeLamports: bigint, jams: number, step: number, rtpPercent: number): bigint;
export const pay = (stake: number, jams: number, step: number, rtpPercent: number): number; // SOL, 9 dp truncated
export const lastStep = (stake: number, jams: number, rtpPercent: number, cap: number): number;
export const ladderWindow = (jams: number, step: number, rtpPercent: number, lastReachable: number, size?: number)
  => { step: number; multiplier: number; next: boolean; reached: boolean }[];
export const jamChoice = (jams: number) => `J${jams}`;
export const parseJamChoice = (choice?: string | null): number | null;
// provablyFair.ts
export async function keyMinesJams(serverSeed: string, prefix: string, jams: number): Promise<string>;
```

- [ ] **Step 1: Implement `utils/keyMines.ts`** (exact integer math mirrors C# `Pay`; RTP in basis points so 98 ⇒ 9800):

```ts
export const KEY_MINES_KEYS = 'qwertyuiopasdfghjklzxcvbnm';
export const KEY_COUNT = 26;
const LAMPORTS = 1_000_000_000n;

export const binomial = (n: number, k: number): bigint => {
  if (k < 0 || k > n) return 0n;
  const kk = Math.min(k, n - k);
  let r = 1n;
  for (let i = 1; i <= kk; i++) r = (r * BigInt(n - kk + i)) / BigInt(i);
  return r;
};

const rtpBp = (rtpPercent: number) => BigInt(Math.round(rtpPercent * 100));

export const payLamports = (stakeLamports: bigint, jams: number, step: number, rtpPercent: number): bigint =>
  (stakeLamports * rtpBp(rtpPercent) * binomial(KEY_COUNT, step)) / (10000n * binomial(KEY_COUNT - jams, step));

export const toLamports = (sol: number): bigint => BigInt(Math.round(sol * 1e9));

export const pay = (stake: number, jams: number, step: number, rtpPercent: number): number =>
  Number(payLamports(toLamports(stake), jams, step, rtpPercent)) / 1e9;

export const multiplier = (jams: number, step: number, rtpPercent: number): number =>
  Number(payLamports(LAMPORTS * 10_000n, jams, step, rtpPercent)) / 1e13; // 4+ dp precision for display

export const lastStep = (stake: number, jams: number, rtpPercent: number, cap: number): number => {
  const capL = toLamports(cap);
  const stakeL = toLamports(stake);
  let last = 0;
  for (let k = 1; k <= KEY_COUNT - jams; k++) {
    if (payLamports(stakeL, jams, k, rtpPercent) > capL) break;
    last = k;
  }
  return last;
};

export const ladderWindow = (jams: number, step: number, rtpPercent: number, lastReachable: number, size = 8) => {
  const start = Math.max(1, Math.min(step + 1 - 3, lastReachable - size + 1));
  const rows = [];
  for (let k = start; k < start + size && k <= lastReachable; k++)
    rows.push({ step: k, multiplier: multiplier(jams, k, rtpPercent), next: k === step + 1, reached: k <= step });
  return rows;
};

export const jamChoice = (jams: number) => `J${jams}`;
export const parseJamChoice = (choice?: string | null): number | null => {
  const m = /^J(\d{1,2})$/.exec(choice ?? '');
  return m ? Number(m[1]) : null;
};
```

- [ ] **Step 2: Verify against the spec table.** If the repo has a TS test runner (`package.json` scripts), add `utils/keyMines.spec.ts` with: `multiplier(3,3,98)` ≈ 1.4387 (to 4 dp), `multiplier(10,9,98)` ≈ 267.6625, `pay(0.01,3,3,98) === 0.014387351`, `lastStep(0.002,10,98,1) === 9`, `lastStep(0.05,1,98,1) === 24`, `lastStep(0.05,3,98,1) === 15`. Otherwise add a dev-only self-check script at `scripts/check-key-mines.ts` run with `npx ts-node` and paste the output into the task report.
- [ ] **Step 3: `keyMinesJams` in `provablyFair.ts`**, reusing the file's existing keystream + `nextIndex` helpers (same as the C# `KeyMinesJams`: keystream over `prefix + '|jams'`, pool = the 26 keys, `jams` picks with removal). Add a comment block with the **V5 vector** (seed, prefix, and the two frozen outputs from the backend plan Task 1 Step 6 — get them from `ProvablyFairTests.cs` in the backend worktree) and the KEEP-IN-SYNC note.
- [ ] **Step 4:** `yarn lint && yarn build` (or the repo's type-check script) green. **Commit** `"Key Mines: types, ladder util and jam draw."`

---

### Task 2: Archetype, lobby, switcher, maps, art

**Files:**
- Modify: `src/utils/gameArchetype.ts` — union gains `'keyMines'`; in `resolveGameArchetype` add `if (game.isKeyMines) return 'keyMines';` right after the `isDuel` check and **before** `isHouseEdge`.
- Modify: `src/utils/gameCatalogue.ts:20` — `SHOWN_ORDER = ['duel', 'houseEdge', 'keyMines', 'race', 'preset', 'lottery']`.
- Modify: `src/utils/gameArt.ts` (:42 union, :61 `archetypeOf`, :72 `ART`, :117 `orbitTypeArchetype`) — `'key-mines'` entry using a placeholder poster `src/assets/types/key-mines.png` (create a simple 2× pixel placeholder: hazard-yellow keycap on dark, consistent size with the other posters; brief: Edson at a typewriter, one key smoking).
- Modify: `src/components/guide/GuideModeCards.vue` `BLURB` (98) `'Press Edson\'s keys, dodge the jams, cash out when you like.'`, `ARCHETYPE_LABEL` (121) `'KEY MINES'`, `ARCHETYPE_ACCENT` (131) the new accent; update the section intro that counts table kinds.
- Modify: `src/components/game-selection/GameTypeCard.vue` — `tag` (145) `'26 KEYS · PICK YOUR JAMS'`; `badges` (274) `['1P · VS HOUSE', 'UP TO 250X']` for key mines; "from <min> SOL" chip (393) also for key mines; `.accent-key-mines` CSS (339, 505+).
- Modify: `src/components/header/GameSwitcher.vue` (134, 318), `src/hooks/useLiveGames.ts:101`, `src/components/game/GameStatsPanel.vue` (65-120), `src/pages/GameStatsPage.vue` (345-356), `src/components/admin/GameTypeList.vue` `modeLabel` (23), `src/pages/PlayerProfilePage.vue` `ART` (208) + rows meta (214-236) — handle `keyMines` explicitly (stats shows `98% RTP` like house-edge; no live pill).
- Modify: `src/components/orbit/OrbitGameTiles.vue`, `src/hooks/useOrbit.ts` (902-990), `useOrbitAutoBet.ts` (88-90), `useOrbitRedirect.ts:135` — wherever `isHouseEdge` means "Hyper Roll", add `&& !isKeyMines` (Key Mines is never in `eligibleGameTypes`, but the guards must not misfire).
- Modify: `src/assets/styles/colors.css` + `tailwind.config.js` — `--color-hazard: #f5c518`, `--color-ivory: #f3ecd8`, `--color-jam: #e5484d` and Tailwind names `hazard`, `ivory`, `jam`.
- Modify: `src/components/game-selection/GameTypes.vue` — five cards must fit one row at `lg` (1024px): if `lg:grid-cols-4` → `lg:grid-cols-5` breaks the card layout, tighten the card (smaller poster/tag) rather than wrapping (`conventions/general.md`).

- [ ] Steps: implement → `yarn build` green → run the dev server and screenshot the lobby at 1024/1280/1440 (headless Edge) → commit `"Key Mines: archetype, lobby card and maps."`

---

### Task 3: Services + `useKeyMines` + `useGame` integration

**Files:**
- Modify: `src/services/mainService.ts`:

```ts
export const pressKeyMines = (roundId: string, key: string, playToken: string) =>
  api.post<KeyMinesMove>(`/games/rounds/${roundId}/press`, { key }, { headers: { 'X-Round-Token': playToken } });
export const cashOutKeyMines = (roundId: string, playToken: string) =>
  api.post<KeyMinesMove>(`/games/rounds/${roundId}/cashout`, null, { headers: { 'X-Round-Token': playToken } });
```
  (match the file's existing axios instance name and export style).
- Modify: `src/hooks/useGame.ts`:
  - `storeBoard`/`readStoredBoard` (127/134) store and return `playToken` alongside the round id.
  - `loadHouseEdgeRound` (1062) / `openFreshBoard` (2942): keep the `playToken` from `openHouseEdgeBoard`'s response.
  - `isStreamerPlayable` (624): true for key mines.
  - `runBet` (1737): accept a `choice` override so Key Mines bets `'J' + M` (wallet, Orbit `placeFromOrbit` 1606, streamer); `/availability` gets that choice.
  - Solo-round safety net (2982-3075): for a `KEYS` round treat `InProgress` as unsettled **without** the 60 s stall/"DELAYED" path.
- Create: `src/hooks/useKeyMines.ts`:

```ts
export function useKeyMines() {
  // state
  const jams = ref<number>(3);          // selected before the bet; locked after
  const pressed = ref<string>('');      // from the server
  const step = computed(() => (end.value === 'jam' ? pressed.value.length - 1 : pressed.value.length));
  const end = ref<KeyMinesEnd | null>(null);
  const jamKeys = ref<string | null>(null);
  const payout = ref<number | null>(null);
  const busy = ref(false);              // one request in flight; extra taps dropped
  const hasToken = computed(() => !!storedToken());
  // derived from ActiveGame + stake
  const lastReachable = computed(() => lastStep(stake, jams.value, rtp, cap));
  const nowMultiplier = computed(() => (step.value ? multiplier(jams.value, step.value, rtp) : 0));
  const cashOutFor = computed(() => (step.value ? pay(stake, jams.value, step.value, rtp) : 0));

  async function bet(): Promise<void>      // useGame.runBet({ choice: jamChoice(jams.value) })
  async function press(key: string): Promise<void>   // if busy or ended or pressed.includes(key) return; on 400 → reload()
  async function cashOut(): Promise<void>
  async function reload(): Promise<void>   // getHouseEdgeRound(roundId) → hydrate pressed/end/jamKeys/step
  function hydrate(instance: GameInstance): void
  return { jams, pressed, step, end, jamKeys, payout, busy, hasToken, lastReachable, nowMultiplier, cashOutFor, bet, press, cashOut, reload, hydrate };
}
```
  Physical keyboard: `onMounted` add a `keydown` listener (lowercase a–z → `press`) ignored when `document.activeElement` is an input/textarea; remove on unmount. No module-scope watchers.
  No token for a live round (other device) ⇒ `hasToken === false`; the screen shows the round read-only with *"This round finishes on its own in a few minutes."*

- [ ] Steps: implement → `yarn build` → commit `"Key Mines: round composable and services."`

---

### Task 4: Game screen components

**Files:**
- Create: `src/components/game/KeyMinesKeyboard.vue` — props `pressed: string`, `jamKeys: string | null`, `ended: boolean`, `disabled: boolean`; emits `press(key)`. Three QWERTY rows from `KEY_MINES_KEYS` (`qwertyuiop` / `asdfghjkl` / `zxcvbnm`); ivory keycaps with chrome rims; a pressed safe key lit hazard-yellow with its step number; a jam red (only after the end, all jams revealed).
- Create: `src/components/game/KeyMinesLadder.vue` — props from `ladderWindow(...)`; 8 cells, NEXT highlighted, reached cells lit.
- Create: `src/components/game/KeyMinesConsole.vue` — on `ConsolePlate.vue`: **ready** = stake plate (reuse the Word Lines HALF / ×2 / MAX keys and stake LCD from `HyperRollModeDial.vue`/`useGame` stake helpers, bounds from `requiredAmount`/`maximumStake`), jam segment `1 · 3 · 5 · 10` from `game.jamOptions`, lever/START → `useKeyMines.bet()`; **live** = CASH OUT FOR `<formatPayout>` (amber, enabled from step 1); **ended** = lever opens the next board (9 s hold then auto-rollover).
- Create: `src/components/game/KeyMinesScreen.vue` — CRT frame (cyan), hazard-stripe band under the name, `COMMIT <first 16 of serverSeedHash>`, `YOU TYPED:` line (phosphor, pressed keys), `KeyMinesLadder`, NOW `×` readout; **jam** state: the jam line red, `SERVER SEED:` + VERIFY (opens the Fair modal); **cashed**: `YOU WON! CONGRATS!` + `PAID: <formatPayout>` (+ `AUTO CASH` when `end === 'idle'`), reuse `HyperRollWinFx.vue` with `variant="win"` and the paid multiplier for coins/shake tiers.
- Modify: `src/pages/GamePage.vue` `onPlate` (45-50) add `isKeyMines`; `src/components/game/GameConsole.vue` (865) — first branch `<KeyMinesConsole v-if="isKeyMinesGame" />`; `src/components/game/GameTerminal.vue` (~1058) — first branch `<KeyMinesScreen v-if="isKeyMinesGame" />`; `src/components/game/GameMonkey.vue:113` — Edson idle/typing art for key mines (typing on each press).
- Sounds: reuse `utils/wordLinesSfx.ts` samples (key press = trace tick, jam = `edson-swore.wav`, cash = win sting) — no new audio assets.

- [ ] Steps: implement → `yarn build` → local manual run against the backend worktree stack (or mock) → headless boot check (no blank page) → commit `"Key Mines: game screen."`

---

### Task 5: History, fair panel, verify.html, profile

**Files:**
- Modify: `src/pages/GameHistoryPage.vue` (45-52, 231/281): a `boardMode === 'KEYS'` row reads `3 JAMS · STEP 7 · 2.63×`, `JAMMED AT STEP 4`, or `AUTO CASH · STEP 7`; result `WON <payout>` when paid, `LOST` on a jam.
- Modify: `src/components/game/GameFairPanel.vue` (113/127/306): decided-by `JAM DRAW - KEY MINES`; show keys pressed vs jams; the instant check recomputes `keyMinesJams(serverSeed, prefix, jamCount)` and compares with `jamKeys`.
- Modify: `public/verify.html` (24 header, 178 draw, 424-452 render): inline `keyMinesJams` (KEEP-IN-SYNC) + the V5 vector comment; for a `KEYS` payload print `JAMS: …`, `PRESSED: …`, `STEP: k`, `RESULT: CASHED k (×m)` / `JAMMED`, and `JAMS MATCH: yes/no`.
- Modify: `src/pages/PlayerProfilePage.vue` — recent rounds render `keyMines` with its art/label.

- [ ] Steps: implement → `yarn build` → verify a real ended round in `verify.html` → commit `"Key Mines: history, fair panel, verify and profile."`

---

### Task 6: Practice mode

**Files:**
- Modify: `src/constants/practiceGame.ts` `PracticeMode` (404) + `resolvePracticeMode` (416): `'key-mines'`.
- Create: `src/hooks/usePracticeKeyMines.ts` (model: `usePracticeHyperRoll.ts` `createPracticeHyperRollGame` :69) — jams drawn with `Math.random` (picks without replacement from `KEY_MINES_KEYS`), stake fixed at the type minimum display value, ladder from `utils/keyMines.ts`, no backend call, no wallet.
- Modify: `src/hooks/usePracticeGame.ts` `createPracticeGame` (648) dispatch; `src/pages/PracticePage.vue:36`; `src/components/practice/PracticeTerminal.vue` (78-83) / `PracticeConsole.vue` — render `KeyMinesScreen`/`KeyMinesConsole` in practice mode (pass the practice composable through props, don't call `useKeyMines`); `src/hooks/useHowToPlay.ts` `practiceMode` (26) / `openPractice` (55).

- [ ] Steps: implement → `yarn build` → open `/game/practice?mode=key-mines`, play to a jam and a cash-out → commit `"Key Mines: practice mode."`

---

### Task 7: How to play

**Files:**
- Modify: `src/constants/guideSections.ts` — `GuideSectionId` gains `'key-mines'`; a `GUIDE_SECTIONS` entry after `hyper-roll` (:219): `{ id: 'key-mines', navLabel: 'KEY MINES', title: 'HOW KEY MINES WORKS', ... }` (copy the shape of the hyper-roll entry, incl. any `GuideArtKind`).
- Create: `src/components/guide/sections/GuideKeyMines.vue` (model: `GuideHyperRoll.vue`) — tag chip `1P · VS HOUSE`, one intro paragraph, a five-step `GuideRail`: (1) Set your stake. (2) Pick how many keys are jammed. (3) Press keys to climb the ladder. (4) Cash out, or hit a jam and lose the stake. (5) Check the jams with VERIFY after the round. One closing line; `GuideTip` labelled **THE WHOLE GAME**. No figures.
- Create: `src/assets/guide/key-mines-{stake,jams,press,cashout,verify}.png` — 2× nearest-neighbour crops of the live cabinet from a local round with dev-only controls hidden (free-bet toggle, HOW TO PLAY key). Take them after Task 4 runs on the local stack.

- [ ] Steps: implement → `yarn build` → open `/how-to-play#key-mines` → commit `"Key Mines: how-to-play section."`

---

### Task 8: Landing page (`monkify-lp`, off `master`)

**Files:**
- Modify: `src/components/GameModes.vue` — `Mode.accent` union (12) gains `"keymines"`; a Key Mines entry in `modes` (32-79) in lobby order (after Hyper Roll): title `KEY MINES`, tag `1P · VS HOUSE · UP TO 250X`, blurb `Press Edson's keys one at a time and cash out whenever you like. Hit a jammed key and the stake is gone.`, poster `assets/cards/keymines-poster.png` (copy the front placeholder); accent class (177-182); keep all cards on one row at `lg` (189: `lg:grid-cols-5`, tighten the card if needed); update the "all four visible" comment (186).
- Modify: `src/pages/index.astro:371` — "Five ways to ride Edson's keystrokes…".
- Modify: `src/components/SpaceRoadmap.vue:53` — "All 5 game modes live — races, Hyper Roll, Key Mines, Monkey Duel & lotteries".
- Modify: `src/pages/terms.astro` — after the Monkey Duel clause (:91), a **Key Mines** clause: played against the house, no house cut; the player chooses how many of 26 keys are jammed; each safe key raises the payout along a published ladder that returns 98% of the stake on average at every step; cashing out pays the stake times the current step; a jammed key loses the stake; a round pays at most 1 SOL and stops at the last step under that limit; a round with no action for 5 minutes is cashed out at its current step, or refunded if no key was pressed; the jammed keys are drawn provably fair and can be verified after the round. Bump `LAST_UPDATED` (6) to `27 October 2026`. ⚠️ The owner reviews this clause before merge.

- [ ] Steps: implement → `npm run build` (or `astro build`) green → browser check of `#modes` on desktop and phone → commit `"Key Mines: landing page card and terms."`

---

## Self-review notes

- Spec §7.1 → Task 1; §7.2 → Task 1; §7.3 → Task 2; §7.4 → Task 4; §7.5 → Task 3; §7.6 → Task 5 (+ Task 1 for the jam draw); §7.7 → Task 6; §7.8 → Task 7; §10.3 → Task 8.
- Task 1 Step 3 depends on the backend plan Task 1's frozen V5 vector.
- Task 7's crops depend on Task 4 running on the local stack.
