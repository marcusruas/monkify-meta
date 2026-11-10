# Monkey Duel Quick Flip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Monkey Duel's VS HOUSE tab a stake-and-pull coinflip shown as one spinning $STROKE coin, with no word to type, ½ · 2× · MAX stake keys and a device-local LAST 6 strip, leaving OPEN DUELS, the backend and the payout untouched.

**Architecture:** Front-only (Vue 3 + TS, `monkify-front`) plus one blurb in `monkify-lp`. A new pure view `DuelCoin.vue` sits in a new centre slot of `DuelCard.vue`; `GameTerminal.vue`'s house-round branch drives it from the round status + `winnerChoice` (never a timer) and keeps `GameTyper` as the proof strip; `DuelPlate.vue` becomes tab-aware (house tab = quick-stake keys + LAST 6, board tab = today's); `DuelConsole.vue` drops word state on the house tab and clamps the quick keys through `useDuel.setStake`. Spec: `monkify-meta/specs/duel-quick-flip.md` (Trello #219).

**Tech Stack:** Vue 3 `<script setup>` + TypeScript (Vue CLI / webpack), Tailwind + arcade.css tokens, `pngjs` (already in `node_modules`) for the two generated sprites, Playwright (`E:/Monkify/.promo-work/wordlines/node_modules/playwright`, `channel: 'msedge'`) for the smoke.

## Global Constraints

- Work only in `E:/Monkify/.worktrees/duel-quick-flip/monkify-front` and `…/monkify-lp` (branch `feature/duel-quick-flip`); never touch the hubs. Push after every commit.
- Backend untouched: no `.cs`, config or migration change.
- Coin art: heads `src/assets/game/duel/monk-coin-main.png`, spin `monk-coin-spin.gif`, tails the new `monk-coin-tails.png`. **Never reference `monk-coin.png`.**
- The coin lands only on the server's `winnerChoice`; minimum spin 600 ms after the spin began, land animation ≤ 600 ms; `prefers-reduced-motion` ⇒ `?` face + instant land; `html.light-graphics` ⇒ no glow.
- Fair-draw rounds (`winnerWasTyped === false`) keep the shipped sentences; never claim a word was typed.
- Player copy: no em dashes, no litotes, no figures that go stale in the guide.
- Stake keys clamp to `[minStake, stakeCeiling]` on the 0.001 SOL grid via `useDuel.setStake` (`clampDuelStake`); MAX = `useDuel.maxStake` (the live coverage quote).
- Front has no unit-test runner: every task's gate is `yarn lint` (+ `yarn build` at the end) and the final Playwright smoke; TDD here means the smoke script is written before the feature code (Task 1).
- Prettier `endOfLine: lf`: write LF files; `yarn lint` runs prettier through eslint.
- `monkify-lp` base branch is `master`.

---

### Task 1: Playwright smoke (the failing test first)

**Files:**
- Create: `E:/Monkify/.worktrees/duel-quick-flip/monkify-front/scripts/smoke-duel-coin.mjs`

**Interfaces:**
- Produces: a script `node scripts/smoke-duel-coin.mjs <frontUrl> <duelTypeId> [rounds]` that exits 0 only when every round shows `[data-coin-phase="spinning"]` then `[data-coin-phase="landed"]`, the result card, the `FLIP` tag, and logs no console errors / failed requests. Later tasks make it pass; Task 11 runs it against the local stack.

- [ ] **Step 1: Write the smoke script**

```js
// scripts/smoke-duel-coin.mjs — #219: pull on VS HOUSE with a free bet, assert the coin spins and lands
// on the server's winner, the result card shows and the lever re-arms as FLIP. No wallet needed
// (free bet). usage: node scripts/smoke-duel-coin.mjs http://localhost:6003 <duelTypeId> [rounds=5]
import { chromium } from 'E:/Monkify/.promo-work/wordlines/node_modules/playwright/index.mjs';

const [front, typeId, roundsArg] = process.argv.slice(2);
const rounds = Number(roundsArg ?? 5);
if (!front || !typeId) { console.error('usage: smoke-duel-coin <frontUrl> <duelTypeId> [rounds]'); process.exit(2); }

const browser = await chromium.launch({ headless: true, channel: 'msedge' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
await ctx.addCookies([
  { name: 'ss_terms', value: 'v1', url: front },
  { name: 'ss_consent', value: 'denied', url: front },
]);
const page = await ctx.newPage();
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push(`[console] ${m.text()}`); });
page.on('pageerror', (e) => errors.push(`[pageerror] ${e.message}`));
page.on('requestfailed', (r) => errors.push(`[reqfail] ${r.url()} ${r.failure()?.errorText ?? ''}`));

await page.goto(`${front}/game/play?type=${typeId}`, { waitUntil: 'load' });
await page.locator('label:has-text("FREE BET MODE") input').check();
const lever = page.getByRole('button', { name: /^Pull the lever/ });
const coin = page.locator('[data-coin-phase]');
const fails = [];
for (let i = 1; i <= rounds; i++) {
  await lever.click();
  try {
    await coin.filter({ has: page.locator(':scope') }).first().waitFor({ state: 'attached', timeout: 8000 });
    await page.waitForFunction(() => document.querySelector('[data-coin-phase]')?.dataset.coinPhase === 'spinning', null, { timeout: 8000 });
    await page.waitForFunction(() => document.querySelector('[data-coin-phase]')?.dataset.coinPhase === 'landed', null, { timeout: 20000 });
    const face = await coin.first().getAttribute('data-coin-face');
    const stamp = (await coin.first().getAttribute('data-coin-stamp')) ?? '';
    const lines = await page.locator('.duel-screen p').allInnerTexts();
    const fair = lines.some((l) => l.includes('NEITHER WORD CAME UP BEFORE THE LIMIT'));
    const typedMine = lines.some((l) => l.includes('EDSON TYPED YOUR WORD'));
    const typedHouse = lines.some((l) => l.includes("EDSON TYPED THE HOUSE'S WORD"));
    if (!face || !stamp) fails.push(`round ${i}: no face/stamp`);
    if (!fair && !typedMine && !typedHouse) fails.push(`round ${i}: no outcome line`);
    if (fair && (typedMine || typedHouse)) fails.push(`round ${i}: fair draw AND typed claim`);
    await page.getByRole('button', { name: /^Pull the lever/ }).waitFor({ timeout: 8000 });
    const tag = await page.locator('.cp-tag').first().innerText();
    if (!/^FLIP /.test(tag)) fails.push(`round ${i}: tag "${tag}" is not FLIP`);
    console.log(`round ${i}: face=${face} stamp=${stamp} fair=${fair} tag=${tag}`);
  } catch (e) {
    fails.push(`round ${i}: ${e.message.split('\n')[0]}`);
    break;
  }
  await page.waitForTimeout(1200); // let RaceResultFx settle before the next pull
}
await browser.close();
if (errors.length) console.log('ERRORS:\n' + errors.join('\n'));
if (fails.length) console.log('FAILS:\n' + fails.join('\n'));
process.exit(errors.length || fails.length ? 1 : 0);
```

- [ ] **Step 2: Run it against the current build to see it fail**

Only possible once the local stack is up (Task 11). Expected today: `round 1: … waiting for [data-coin-phase]` timeout → exit 1. Record that as the red state.

- [ ] **Step 3: Commit**

```bash
git -C E:/Monkify/.worktrees/duel-quick-flip/monkify-front add scripts/smoke-duel-coin.mjs
git -C E:/Monkify/.worktrees/duel-quick-flip/monkify-front commit -m "test: Playwright smoke for the VS HOUSE coin flip (#219)"
```

---

### Task 2: `utils/duelLastResults.ts`

**Files:**
- Create: `src/utils/duelLastResults.ts`

**Interfaces:**
- Produces: `type DuelFace = 'heads' | 'tails'`, `readDuelResults(wallet, gameTypeId): DuelFace[]`, `rememberDuelResult(wallet, gameTypeId, face): DuelFace[]`, `DUEL_LAST_RESULTS_CAP = 6`. Consumed by Task 7 (DuelConsole).

- [ ] **Step 1: Write the module**

```ts
/**
 * #219 Monkey Duel quick flip — the LAST 6 strip on the house plate: which face the last house duels
 * this console saw settle landed on. Heads = my bet won, tails = the house (typed or fair draw).
 * Device-local on purpose, keyed by wallet AND type like `lastStake.ts`; the backend knows nothing
 * about it and nothing here is money. Every storage access is wrapped: a blocked or full storage
 * just means an empty strip.
 */
export type DuelFace = 'heads' | 'tails';

export const DUEL_LAST_RESULTS_CAP = 6;

const ANON = 'anon';
const KEY = (gameTypeId: string, wallet: string) => `space-stroker.duel-last.${gameTypeId}.${wallet}`;

const isFace = (value: unknown): value is DuelFace => value === 'heads' || value === 'tails';

export const readDuelResults = (
  wallet: string | null | undefined,
  gameTypeId: string | null | undefined
): DuelFace[] => {
  if (!gameTypeId) return [];
  try {
    const raw = localStorage.getItem(KEY(gameTypeId, wallet || ANON));
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter(isFace).slice(0, DUEL_LAST_RESULTS_CAP) : [];
  } catch {
    return [];
  }
};

/** Prepends `face` (newest first), keeps the cap, writes back; returns the new list either way. */
export const rememberDuelResult = (
  wallet: string | null | undefined,
  gameTypeId: string | null | undefined,
  face: DuelFace
): DuelFace[] => {
  if (!gameTypeId) return [];
  const next = [face, ...readDuelResults(wallet, gameTypeId)].slice(0, DUEL_LAST_RESULTS_CAP);
  try {
    localStorage.setItem(KEY(gameTypeId, wallet || ANON), JSON.stringify(next));
  } catch {
    // Storage unavailable — the strip still shows this session's results from memory.
  }
  return next;
};
```

- [ ] **Step 2: Lint**

Run: `yarn lint` (in the front worktree). Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add src/utils/duelLastResults.ts && git commit -m "feat(duel): device-local LAST 6 results store (#219)"
```

---

### Task 3: Generated sprites — tails coin + `LAST 6` engraving (+ recipe)

**Files:**
- Create: `scripts/coin-tails.mjs`, `scripts/coin-tails.md`
- Create (generated): `src/assets/game/duel/monk-coin-tails.png`, `src/assets/game/duel/label-last.png`

**Interfaces:**
- Produces: `monk-coin-tails.png` (480×500, same grid as heads) and `label-last.png` (23×5, plate font, same ink as `race/label-help.png`), both imported by Tasks 4 and 6.

- [ ] **Step 1: Write the generator**

```js
// scripts/coin-tails.mjs — #219: derive the HOUSE (tails) face of the $STROKE coin from the heads
// still, and cut the `LAST 6` plate engraving. Re-runnable: node scripts/coin-tails.mjs
// Tails recipe: keep every pixel of monk-coin-main.png whose hue is the coin's gold rim/body, remap
// the warm palette to the duel blue (#3d8cff) / light blue (#78adff) / navy (#14338f) by luminance,
// blank the face area (the inner disc) to the mid blue, and stamp HOUSE in the 3x5 plate font,
// scaled 8x, centred. Hand-drawn art later just replaces the PNG.
import fs from 'node:fs';
import { PNG } from 'pngjs';

const read = (p) => PNG.sync.read(fs.readFileSync(p));
const write = (p, png) => fs.writeFileSync(p, PNG.sync.write(png));

// 3x5 plate font (same grid as the shipped label sprites), 1 = ink.
const FONT = {
  L: ['100', '100', '100', '100', '111'],
  A: ['010', '101', '111', '101', '101'],
  S: ['111', '100', '111', '001', '111'],
  T: ['111', '010', '010', '010', '010'],
  H: ['101', '101', '111', '101', '101'],
  O: ['111', '101', '101', '101', '111'],
  U: ['101', '101', '101', '101', '111'],
  E: ['111', '100', '111', '100', '111'],
  '6': ['111', '100', '111', '101', '111'],
  ' ': ['000', '000', '000', '000', '000'],
};
const textWidth = (t) => t.length * 3 + (t.length - 1);
const stamp = (png, text, x0, y0, scale, rgba) => {
  let x = x0;
  for (const ch of text) {
    const g = FONT[ch];
    for (let r = 0; r < 5; r++) for (let c = 0; c < 3; c++) if (g[r][c] === '1')
      for (let dy = 0; dy < scale; dy++) for (let dx = 0; dx < scale; dx++) {
        const i = ((y0 + r * scale + dy) * png.width + x + c * scale + dx) * 4;
        png.data.set(rgba, i);
      }
    x += 4 * scale;
  }
};

// ---- 1. the LAST 6 engraving: 23x5, the ink colour of the shipped HELP engraving ----
{
  const help = read('src/assets/game/race/label-help.png');
  let ink = [0, 0, 0, 255];
  for (let i = 0; i < help.data.length; i += 4) if (help.data[i + 3] > 0) { ink = [...help.data.slice(i, i + 4)]; break; }
  const text = 'LAST 6';
  const png = new PNG({ width: textWidth(text), height: 5 });
  png.data.fill(0);
  stamp(png, text, 0, 0, 1, ink);
  write('src/assets/game/duel/label-last.png', png);
  console.log('label-last.png', png.width, 'x', png.height);
}

// ---- 2. the tails face ----
{
  const src = read('src/assets/game/duel/monk-coin-main.png');
  const out = new PNG({ width: src.width, height: src.height });
  const cx = src.width / 2, cy = src.height / 2;
  const BLUE = [0x3d, 0x8c, 0xff], LIGHT = [0x78, 0xad, 0xff], NAVY = [0x14, 0x33, 0x8f], MID = [0x1d, 0x4d, 0xb3];
  let rMax = 0;
  for (let y = 0; y < src.height; y++) for (let x = 0; x < src.width; x++) {
    const i = (y * src.width + x) * 4;
    if (src.data[i + 3] > 0) rMax = Math.max(rMax, Math.hypot(x - cx, y - cy));
  }
  const faceR = rMax * 0.72; // inside this radius the face is blanked for the stamp
  for (let y = 0; y < src.height; y++) for (let x = 0; x < src.width; x++) {
    const i = (y * src.width + x) * 4;
    const a = src.data[i + 3];
    if (a === 0) { out.data[i + 3] = 0; continue; }
    const r = Math.hypot(x - cx, y - cy);
    const lum = (0.299 * src.data[i] + 0.587 * src.data[i + 1] + 0.114 * src.data[i + 2]) / 255;
    const pick = r < faceR ? MID : lum > 0.75 ? LIGHT : lum > 0.4 ? BLUE : NAVY;
    out.data.set([...pick, a], i);
  }
  const scale = 8, text = 'HOUSE';
  const w = textWidth(text) * scale, h = 5 * scale;
  stamp(out, text, Math.round(cx - w / 2), Math.round(cy - h / 2), scale, [0xff, 0xff, 0xff, 0xff]);
  write('src/assets/game/duel/monk-coin-tails.png', out);
  console.log('monk-coin-tails.png', out.width, 'x', out.height);
}
```

- [ ] **Step 2: Run it and eyeball both PNGs**

Run: `node scripts/coin-tails.mjs` (from the front worktree; `pngjs` resolves from the junctioned `node_modules`). Expected: `label-last.png 23 x 5` and `monk-coin-tails.png 480 x 500`. Open both PNGs (Read tool) — tails must read as the same coin in blue with a white HOUSE stamp; the label as `LAST 6` in the plate ink.

- [ ] **Step 3: Write `scripts/coin-tails.md`** — what the script does (source, palette table, stamp grid), how to re-run it, and that a hand-drawn `monk-coin-tails.png` simply replaces the file.

- [ ] **Step 4: Commit**

```bash
git add scripts/coin-tails.mjs scripts/coin-tails.md src/assets/game/duel/monk-coin-tails.png src/assets/game/duel/label-last.png
git commit -m "feat(duel): tails face of the STROKE coin + LAST 6 engraving, generated (#219)"
```

---

### Task 4: `DuelCoin.vue` + `DuelCard.vue` centre slot

**Files:**
- Create: `src/components/game/DuelCoin.vue`
- Modify: `src/components/game/DuelCard.vue` (template `.duel-card__vs` → slot; style `container-type`)

**Interfaces:**
- Produces: `<duel-coin :phase :face :stamp :house-word :mobile :reduced-motion />` with `phase: 'idle' | 'spinning' | 'landing' | 'landed'`, `face: 'heads' | 'tails' | null`, `stamp: string`, `houseWord: string`; root carries `data-coin-phase`, `data-coin-face`, `data-coin-stamp` (the smoke reads them). `DuelCard` exposes `<template #centre>`.

- [ ] **Step 1: Write `DuelCoin.vue`**

```vue
<script setup lang="ts">
  import { computed } from 'vue';
  import coinHeads from '@/assets/game/duel/monk-coin-main.png';
  import coinSpin from '@/assets/game/duel/monk-coin-spin.gif';
  import coinTails from '@/assets/game/duel/monk-coin-tails.png';

  // #219 Monkey Duel quick flip — the $STROKE coin that IS the VS HOUSE race on screen. A pure view:
  // the owner (GameTerminal / PracticeDuelScreen) decides the phase from the round, and the face from
  // the server's winner — never from a timer here. ⚠️ Heads/spin/tails are monk-coin-main.png /
  // monk-coin-spin.gif / monk-coin-tails.png; NEVER monk-coin.png (retired red-rim art).
  export type DuelCoinPhase = 'idle' | 'spinning' | 'landing' | 'landed';
  export type DuelCoinFace = 'heads' | 'tails';

  const props = withDefaults(
    defineProps<{
      phase: DuelCoinPhase;
      face: DuelCoinFace | null;
      /** The word under the coin: yours while it spins (when known), the winner's once landed. */
      stamp: string;
      /** The house word once drawn — typed onto the tails caption while spinning; '' = ??????. */
      houseWord?: string;
      /** Re-keys the spin GIF so it restarts from frame 0 on every new round. */
      roundKey?: string | null;
      mobile?: boolean;
      reducedMotion?: boolean;
    }>(),
    { houseWord: '', roundKey: null, mobile: false, reducedMotion: false }
  );

  const showing = computed<'heads' | 'tails' | 'spin' | 'unknown'>(() => {
    if (props.phase === 'idle') return 'heads';
    if (props.phase === 'spinning') return props.reducedMotion ? 'unknown' : 'spin';
    return props.face ?? 'unknown';
  });
  const src = computed(() =>
    showing.value === 'heads' ? coinHeads : showing.value === 'tails' ? coinTails : showing.value === 'spin' ? coinSpin : null
  );
  const stampClass = computed(() => ({
    'duel-coin__stamp--you': props.face === 'heads' || props.phase === 'spinning' || props.phase === 'idle',
    'duel-coin__stamp--house': props.face === 'tails' && props.phase !== 'spinning',
  }));
</script>

<template>
  <div
    class="duel-coin"
    :class="[`duel-coin--${phase}`, { 'duel-coin--mobile': mobile, 'duel-coin--still': reducedMotion }]"
    :data-coin-phase="phase"
    :data-coin-face="face ?? ''"
    :data-coin-stamp="stamp"
    role="img"
    :aria-label="phase === 'landed' ? `The coin landed on ${face === 'heads' ? 'heads, your word' : 'tails, the house'}: ${stamp}` : phase === 'idle' ? 'The STROKE coin, heads up' : 'The STROKE coin is spinning'"
  >
    <img v-if="src" :key="`${roundKey ?? ''}-${showing}`" :src="src" alt="" draggable="false" class="duel-coin__img" />
    <div v-else class="duel-coin__unknown" aria-hidden="true">?</div>
    <p v-if="stamp" class="duel-coin__stamp" :class="stampClass">{{ stamp.toUpperCase() }}</p>
    <p v-else-if="phase === 'idle'" class="duel-coin__stamp duel-coin__stamp--dim">HEADS = YOU</p>
  </div>
</template>

<style scoped>
  .duel-coin {
    --coin-size: clamp(96px, 34cqi, 220px);
    display: grid;
    justify-items: center;
    align-content: center;
    gap: 0.35em;
    min-width: 0;
    padding-inline: 0.3em;
  }
  .duel-coin--mobile { --coin-size: clamp(72px, 30cqi, 150px); }
  .duel-coin__img,
  .duel-coin__unknown {
    width: var(--coin-size);
    aspect-ratio: 480 / 500;
    image-rendering: pixelated;
    user-select: none;
    filter: drop-shadow(0 0 10px rgba(255, 162, 20, 0.35));
  }
  .duel-coin--landed .duel-coin__img[src*='tails'] { filter: drop-shadow(0 0 10px rgba(61, 140, 255, 0.45)); }
  .duel-coin__unknown {
    display: grid;
    place-items: center;
    border-radius: 50%;
    background: #1d4db3;
    box-shadow: inset 0 0 0 6px #3d8cff;
    color: #fff;
    font-size: calc(var(--coin-size) * 0.45);
  }
  .duel-coin--landing .duel-coin__img { animation: duel-coin-land 0.5s steps(4) both; }
  @keyframes duel-coin-land {
    0% { transform: scale(0.72) rotate(-12deg); opacity: 0.4; }
    60% { transform: scale(1.12) rotate(4deg); opacity: 1; }
    100% { transform: scale(1) rotate(0); opacity: 1; }
  }
  .duel-coin__stamp {
    margin: 0;
    max-width: 100%;
    font-size: 0.95em;
    letter-spacing: 0.1em;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .duel-coin__stamp--you { color: #ff6a70; text-shadow: 0 0 8px rgba(234, 50, 60, 0.6); }
  .duel-coin__stamp--house { color: #78adff; text-shadow: 0 0 8px rgba(61, 140, 255, 0.6); }
  .duel-coin__stamp--dim { color: rgba(34, 197, 94, 0.45); text-shadow: none; }
  .duel-coin--still .duel-coin__img,
  :global(html.light-graphics) .duel-coin__img,
  :global(html.light-graphics) .duel-coin__unknown { filter: none; animation: none; }
  @media (prefers-reduced-motion: reduce) {
    .duel-coin--landing .duel-coin__img { animation: none; }
  }
</style>
```

- [ ] **Step 2: Slot the card** — in `DuelCard.vue` replace `<span class="duel-card__vs">VS</span>` with:

```vue
      <slot name="centre"><span class="duel-card__vs">VS</span></slot>
```

and add `container-type: inline-size;` to `.duel-card`. Add the header comment line: `#219: the centre is a slot — VS HOUSE mounts the $STROKE coin (DuelCoin) there; the board keeps VS.`

- [ ] **Step 3: Lint** — `yarn lint`. Expected clean.

- [ ] **Step 4: Commit**

```bash
git add src/components/game/DuelCoin.vue src/components/game/DuelCard.vue
git commit -m "feat(duel): DuelCoin view + DuelCard centre slot (#219)"
```

---

### Task 5: `DuelPlate.vue` — tab-aware wells

**Files:**
- Modify: `src/components/game/DuelPlate.vue` (imports, props, emits, `LABEL_ART`, left/right templates, styles)

**Interfaces:**
- Consumes: `label-last.png`, `monk-coin-tails.png` (Task 3).
- Produces: new props `lastResults?: ('heads'|'tails')[]`, `halfDisabled?`, `doubleDisabled?`, `maxDisabled?`, `quickDisabled?`; new emits `half`, `double`, `max`. `word`/`wordLength` become optional (`word: ''`, `wordLength: 6` defaults). Board tab (`boardTab === true`) renders exactly today's markup.

- [ ] **Step 1: Imports + props + emits**

```ts
  import keyHalf from '@/assets/game/stake/key-half.png';
  import keyDouble from '@/assets/game/stake/key-double.png';
  import keyMax from '@/assets/game/stake/key-max.png';
  import labelHalf from '@/assets/game/stake/label-half.png';
  import labelX2 from '@/assets/game/stake/label-x2.png';
  import labelMax from '@/assets/game/stake/label-max.png';
  import labelLast from '@/assets/game/duel/label-last.png';
  import coinHeadsMini from '@/assets/game/duel/monk-coin-main.png';
  import coinTailsMini from '@/assets/game/duel/monk-coin-tails.png';
  import type { DuelFace } from '@/utils/duelLastResults';
```

Props (add; make `word` / `wordLength` optional with defaults `''` / `6`):

```ts
      /** #219 house tab: the LAST 6 strip, newest first. */
      lastResults?: DuelFace[];
      /** #219 house tab: the ½ · 2× · MAX keys, dimmed at the bound they cannot pass. */
      quickDisabled?: boolean;
      halfDisabled?: boolean;
      doubleDisabled?: boolean;
      maxDisabled?: boolean;
```

Defaults: `lastResults: () => []`, the four booleans `false`. Emits: `(e: 'half'): void; (e: 'double'): void; (e: 'max'): void;`.

`LABEL_ART` additions: `half: { src: labelHalf, w: 15 }, x2: { src: labelX2, w: 7 }, max: { src: labelMax, w: 11 }, last: { src: labelLast, w: 23 }`.

Add `const lastSlots = computed(() => Array.from({ length: 6 }, (_, i) => props.lastResults[i] ?? null));`

- [ ] **Step 2: Left well** — wrap today's row 1 in `<template v-if="boardTab">` and add the house row:

```vue
        <!-- #219 Row 1 on VS HOUSE: ½ · 2× · MAX (the Word Lines stake keys, same sprites). -->
        <div v-else class="cp-row cp-row--kkk">
          <div class="cp-cell">
            <button type="button" class="sprite-key" :disabled="quickDisabled || halfDisabled" aria-label="Halve the stake" @click="emit('half')">
              <img :src="keyHalf" alt="" draggable="false" class="sprite-key__art" />
            </button>
            <img :src="LABEL_ART.half.src" alt="" draggable="false" class="duel-label-art" :style="{ width: engraving(LABEL_ART.half.w) }" />
          </div>
          <div class="cp-cell">
            <button type="button" class="sprite-key" :disabled="quickDisabled || doubleDisabled" aria-label="Double the stake" @click="emit('double')">
              <img :src="keyDouble" alt="" draggable="false" class="sprite-key__art" />
            </button>
            <img :src="LABEL_ART.x2.src" alt="" draggable="false" class="duel-label-art" :style="{ width: engraving(LABEL_ART.x2.w) }" />
          </div>
          <div class="cp-cell">
            <button type="button" class="sprite-key" :disabled="quickDisabled || maxDisabled" aria-label="Stake the most the house can cover right now" @click="emit('max')">
              <img :src="keyMax" alt="" draggable="false" class="sprite-key__art" />
            </button>
            <img :src="LABEL_ART.max.src" alt="" draggable="false" class="duel-label-art" :style="{ width: engraving(LABEL_ART.max.w) }" />
          </div>
        </div>
```

Row 2 (stake) stays shared.

- [ ] **Step 3: Right well** — keep the switch cell; wrap UP/DOWN cells and row 2 in `<template v-if="boardTab">`; add for the house tab, in row 1 next to the switch, the `?` HELP cell (same markup as today's left-well HELP cell, `cp-span-2` is NOT needed: row stays `cp-row--mkk` with switch + HELP + an empty `<div class="cp-cell" aria-hidden="true" />`), and row 2:

```vue
        <div v-else class="cp-row cp-row--full">
          <div class="cp-cell">
            <div class="duel-last cp-key-h" role="list" aria-label="Your last six flips, newest first">
              <span v-for="(face, i) in lastSlots" :key="i" role="listitem" class="duel-last__slot" :class="{ 'duel-last__slot--empty': !face }">
                <img v-if="face" :src="face === 'heads' ? coinHeadsMini : coinTailsMini" :alt="face === 'heads' ? 'heads' : 'tails'" draggable="false" class="duel-last__coin" />
              </span>
            </div>
            <img :src="LABEL_ART.last.src" alt="" draggable="false" class="duel-label-art" :style="{ width: engraving(LABEL_ART.last.w) }" />
          </div>
        </div>
```

- [ ] **Step 4: Styles** (scoped, in `DuelPlate.vue`):

```css
  /* #219 three equal keys (½ · 2× · MAX) on the stake row's 7 : 17 : 7 width: 7 : 7 : 7 centred. */
  .cp-row--kkk { grid-template-columns: 7fr 7fr 7fr; justify-content: center; }
  /* #219 the LAST 6 strip: six mini coins on a dark LCD-like bed, a key's height. */
  .duel-last {
    display: grid;
    grid-template-columns: repeat(6, minmax(0, 1fr));
    align-items: center;
    gap: 4%;
    width: 100%;
    padding: 6% 7%;
    background: #000;
    box-shadow: inset 0 0 0 2px #424c6e;
    box-sizing: border-box;
  }
  .duel-last__slot { display: grid; place-items: center; aspect-ratio: 1; border-radius: 50%; background: #0b0d14; }
  .duel-last__slot--empty { box-shadow: inset 0 0 0 1px #1c2238; }
  .duel-last__coin { width: 100%; height: auto; image-rendering: pixelated; user-select: none; }
```

Update the header comment's row map to describe both tabs.

- [ ] **Step 5: Lint** — `yarn lint`. Expected clean (unused `keyUp`/`keyDown` etc. still used under `v-if="boardTab"`).

- [ ] **Step 6: Commit**

```bash
git add src/components/game/DuelPlate.vue && git commit -m "feat(duel): tab-aware plate — quick-stake keys and LAST 6 on VS HOUSE (#219)"
```

---

### Task 6: `DuelConsole.vue` — the house tab without a word

**Files:**
- Modify: `src/components/game/DuelConsole.vue`

**Interfaces:**
- Consumes: `readDuelResults` / `rememberDuelResult` (Task 2), the plate props/emits (Task 5), `useGame().winnerChoice`.
- Produces: lever tag `FLIP <stake>` on the house tab; `lastResults` state; `onHalf/onDouble/onMax`.

- [ ] **Step 1: Header comment** — replace the row map + the `⚠️ Quick-stake chips are OUT OF SCOPE…` line with:

```ts
  //   VS HOUSE  — left row 1: ½ · 2× · MAX · left row 2: − / stake LCD / + · right row 1: switch + `?`
  //               · right row 2: LAST 6. No word: every pull picks one (#219, owner 2026-11-10 —
  //               this reverses the 2026-09 "no quick-stake chips" call for the house tab only).
  //   OPEN DUELS — the shipped plate: word LCD + `?`, stake row, switch + UP/DOWN, SORT / OK / CLEAR.
  //   centre: the lever. FLIP x (house) · READY / OPEN x / JOIN x / CANCEL / LIVE / END (board).
```

- [ ] **Step 2: Imports + state**

```ts
  import { readDuelResults, rememberDuelResult, DuelFace } from '@/utils/duelLastResults';
  // …
  const { /* existing */, winnerChoice } = useGame();  // add winnerChoice to the destructure
  const lastResults = ref<DuelFace[]>([]);
  const reloadLastResults = () => {
    lastResults.value = readDuelResults(currentPlayerId.value, selectedGame.value?.gameTypeId);
  };
  watch(() => [currentPlayerId.value, selectedGame.value?.gameTypeId] as const, reloadLastResults, { immediate: true });
  // The round whose face is already on the strip — one entry per round, whatever re-renders.
  let recordedRoundId: string | null = null;
  watch(atResult, (now) => {
    const round = gameInstance.value;
    if (!now || !round || round.duelKind !== DuelKind.House || round.gameId === recordedRoundId) return;
    const mine = round.bets?.find((b) => b.externalPlayerId === currentPlayerId.value)?.choice ?? userWord.value;
    const won = winnerChoice.value;
    if (!won) return;
    recordedRoundId = round.gameId;
    lastResults.value = rememberDuelResult(currentPlayerId.value, selectedGame.value?.gameTypeId, mine && mine === won ? 'heads' : 'tails');
  });
```

(Check how `GameTerminal` finds "my bet" — `duelMyBet` by `externalPlayerId`; mirror its exact field. If `winnerChoice` can arrive after `atResult`, watch `[atResult, winnerChoice]` instead.)

- [ ] **Step 3: Word state becomes board-only** — `fillRememberedWord` early-returns when `!isBoardTab.value`; `wordForPull()` on the house path always picks: in `placeCreatorBet(kind)` use `const pull = kind === DuelKind.House ? pickForHouse() : wordForPull();` where

```ts
  /** #219 VS HOUSE: there is no word field — every pull picks. */
  const pickForHouse = () => {
    const word = pickDuelWord(wordLength.value);
    if (!word) return null;
    userWord.value = word;        // the screen's header and the my-bet fallback read it until the bet loads
    autoFilledWord = null;
    pickedWord.value = word;
    return { word, picked: true };
  };
```

`pullLever`: house branch `if (!duel.isHouseTabAvailable.value) { leverError(); return; }` (drop `wordPlayable`); result branch `if (playAgain && duel.isHouseTabAvailable.value)`. `leverTag`: both `PLAY ${…}` → `FLIP ${formatPayout(stake.value)}`, and the result-tag condition drops `&& wordPlayable.value` for House.

- [ ] **Step 4: Quick-stake handlers**

```ts
  // #219 ½ · 2× · MAX — on the 0.001 grid the backend validates, then useDuel.setStake clamps to
  // [minStake, the house ceiling quote]. MAX is the quote itself (the type ceiling, or less on a thin treasury).
  const toGrid = (value: number) => Math.round(value * 1000) / 1000;
  const quickStake = (value: number) => {
    if (orbitLocked.value || inFlight.value || isBoardTab.value) return;
    playAudio(buttonPress);
    setStake(toGrid(value));
    autoFilledStake = null; // the player chose it: lastStake must not overwrite it this round
  };
  const onHalf = () => quickStake(stake.value / 2);
  const onDouble = () => quickStake(stake.value * 2);
  const onMax = () => quickStake(duel.maxStake.value ?? duel.minStake.value);
  const halfDisabled = computed(() => stake.value <= duel.minStake.value);
  const maxDisabled = computed(() => stake.value >= duel.stakeCeiling.value);
```

(`useDuel` must export `minStake` and `stakeCeiling` — check its return object; add them if missing.)

- [ ] **Step 5: Template** — pass `:last-results="lastResults" :quick-disabled="!canEditStake || orbitLocked" :half-disabled="halfDisabled" :double-disabled="maxDisabled" :max-disabled="maxDisabled" @half="onHalf()" @double="onDouble()" @max="onMax()"`.

- [ ] **Step 6: Lint** — `yarn lint`. Expected clean.

- [ ] **Step 7: Commit**

```bash
git add src/components/game/DuelConsole.vue src/hooks/useDuel.ts
git commit -m "feat(duel): house tab pulls without a word, FLIP tag, quick-stake keys, LAST 6 (#219)"
```

---

### Task 7: `GameTerminal.vue` — the coin on the VS HOUSE screen

**Files:**
- Modify: `src/components/game/GameTerminal.vue` (script: reveal beat removed, coin phase; template: house idle block + house race/result block; styles: `.duel-proof`)

**Interfaces:**
- Consumes: `DuelCoin` (Task 4), `DuelCard` slot.
- Produces: `[data-coin-phase]` in the DOM during house rounds (the smoke's hook).

- [ ] **Step 1: Remove the reveal beat** — delete `DUEL_REVEAL_MS`, `DUEL_REVEAL_TYPE_MS`, `duelRevealing`, `duelRevealTyped`, `duelRevealedRoundId`, `duelRevealTimer`, `duelRevealTypeTimer`, `stopDuelReveal`, both reveal watchers, `onUnmounted(stopDuelReveal)`, `duelRevealRivalWord`; `duelShowResult` becomes `finishedTyping.value && (gameInstance.value?.status ?? 0) > GameStatus.InProgress`; the `if (isDuel.value && duelRevealing.value) return null;` line at ~924 goes; the `duel-race--behind` class and the `<transition name="duel-reveal"><duel-vs-splash v-if="duelRevealing" …/></transition>` block go (keep the PvP `GameStarting` splash).

- [ ] **Step 2: Coin phase state**

```ts
  // ---- #219 the $STROKE coin (VS HOUSE only) ----
  // Spins from the moment the house round is on screen; lands only once the round is settled ON SCREEN
  // (duelShowResult: the proof strip already showed the end of the stream) and at least 600 ms after the
  // spin began; the face is the server's verdict (duelIWon), never a timer.
  const COIN_MIN_SPIN_MS = 600;
  const COIN_LAND_MS = 500;
  const reducedMotion = ref(false);
  onMounted(() => { reducedMotion.value = window.matchMedia('(prefers-reduced-motion: reduce)').matches; });
  const coinPhase = ref<DuelCoinPhase>('idle');
  let coinRound: string | null = null;
  let coinSpinStartedAt = 0;
  let coinTimer: ReturnType<typeof setTimeout> | undefined;
  const landCoin = () => {
    clearTimeout(coinTimer);
    if (reducedMotion.value) { coinPhase.value = 'landed'; return; }
    coinPhase.value = 'landing';
    coinTimer = setTimeout(() => { coinPhase.value = 'landed'; }, COIN_LAND_MS);
  };
  watch(
    () => [isHouseDuelRound.value, gameInstance.value?.gameId ?? null, gameInstance.value?.status ?? 0, duelShowResult.value] as const,
    ([house, id, status, settled]) => {
      clearTimeout(coinTimer);
      if (!house || !id || status < GameStatus.GameStarting) { coinPhase.value = 'idle'; coinRound = null; return; }
      if (id !== coinRound) { coinRound = id; coinSpinStartedAt = Date.now(); coinPhase.value = settled ? 'landed' : 'spinning'; if (settled) return; }
      if (!settled) { coinPhase.value = 'spinning'; return; }
      const wait = Math.max(0, COIN_MIN_SPIN_MS - (Date.now() - coinSpinStartedAt));
      if (wait === 0) landCoin(); else coinTimer = setTimeout(landCoin, wait);
    },
    { immediate: true }
  );
  onUnmounted(() => clearTimeout(coinTimer));
  const coinFace = computed<DuelCoinFace | null>(() => (coinPhase.value === 'landing' || coinPhase.value === 'landed' ? (duelIWon.value ? 'heads' : 'tails') : null));
  const coinStamp = computed(() => (coinPhase.value === 'landing' || coinPhase.value === 'landed' ? (winnerChoice.value ?? '') : (duelMyBet.value?.choice ?? userWord.value ?? '')));
```

(Reload onto a finished round: `settled` is already true on the first tick for that id ⇒ straight to `landed`.)

- [ ] **Step 3: Template — idle house tab** — replace the house-tab `<duel-card … :your-word="userWord">` + prompt with the spec §7.5 markup (`:your-word="''"`, `<template #centre><duel-coin phase="idle" :face="null" stamp="" :mobile="mobile" :reduced-motion="reducedMotion" /></template>`, prompt `> PULL THE LEVER: FLIP x, WIN y` + `> SET A STAKE AND PULL. THE $STROKE COIN DECIDES.`). `DUEL_PICK_HINT` stays for the board prompts.

- [ ] **Step 4: Template — house race/result** — inside the race/result `<template v-if=…>`: wrap `<race-lanes …>` in `<template v-if="!isHouseDuelRound">` and add the house branch:

```vue
            <template v-else>
              <duel-card :your-word="duelMyBet?.choice ?? userWord" rival-label="THE HOUSE" :rival-word="duelRivalWord" :stake="duelStake" :word-length="duelWordLength" :commission-percentage="duelRate" :token-symbol="duelTokenSymbol" class="duel-card--coin">
                <template #centre>
                  <duel-coin :phase="coinPhase" :face="coinFace" :stamp="coinStamp" :house-word="duelRivalWord" :round-key="gameInstance.gameId" :mobile="mobile" :reduced-motion="reducedMotion" />
                </template>
              </duel-card>
              <div class="duel-proof"><game-typer :max-chars="mobile ? 40 : 70" /></div>
            </template>
```

In the `duelShowResult` block change the house line to `> ROUND OVER. PULL THE LEVER TO FLIP AGAIN.`; the outcome lines, `DUEL WON!`, `PAID`, `POT · HOUSE CUT`, server seed stay. Keep `<race-result-fx>` as is.

- [ ] **Step 5: Styles** — `.duel-proof { margin-top: 0.4em; max-height: 2.6em; overflow: hidden; }` and `.duel-screen--mobile .duel-card--coin { gap: 0.4em; }`.

- [ ] **Step 6: Lint + build** — `yarn lint && yarn build` (copy hub `yarn.lock` in first if missing). Expected: both clean; grep the bundle for `monk-coin-tails`.

- [ ] **Step 7: Commit**

```bash
git add src/components/game/GameTerminal.vue && git commit -m "feat(duel): the STROKE coin replaces reveal + lanes on VS HOUSE (#219)"
```

---

### Task 8: Practice duel

**Files:**
- Modify: `src/constants/practiceGame.ts` (`PRACTICE_DUEL_TUTORIAL_LINES`), `src/hooks/usePracticeDuel.ts` (quick keys), `src/components/practice/PracticeDuelConsole.vue`, `src/components/practice/PracticeDuelScreen.vue`

- [ ] **Step 1: Tutorial lines** — replace the four strings with spec §7.9's four (no em dashes).
- [ ] **Step 2: `usePracticeDuel.ts`** — replace the three "Word Lines only" members with real ones:

```ts
    halveStake: () => setStake(Math.round((stakeValue.value / 2) * 1000) / 1000),
    doubleStake: () => setStake(Math.round(stakeValue.value * 2 * 1000) / 1000),
    stakeSteps: computed(() => ({
      canHalve: stakeValue.value > PRACTICE_DUEL_MIN_STAKE,
      canDouble: stakeValue.value < PRACTICE_DUEL_MAX_STAKE,
    })),
```

The pull already picks a word when `choice` is empty; the console will always pass `''`.
- [ ] **Step 3: `PracticeDuelConsole.vue`** — pass `:half-disabled="!stakeSteps.canHalve" :double-disabled="!stakeSteps.canDouble" :max-disabled="!stakeSteps.canDouble" :quick-disabled="!editable" @half="practice.halveStake()" @double="practice.doubleStake()" @max="practice.setMaxStake()"`; `placeBet('')` on the pull (drop `userWord`); tag `FLIP ${…}`; in-memory `lastResults` ref pushed `'heads'` on each result (`watch(atResult)`), passed to the plate.
- [ ] **Step 4: `PracticeDuelScreen.vue`** — import `DuelCoin`; idle: `<duel-card :your-word="''" …><template #centre><duel-coin phase="idle" :face="null" stamp="" :mobile="mobile" /></template></duel-card>` and the prompt `> SET A STAKE AND PULL. THE $STROKE COIN DECIDES.`; racing: a `coinPhase` computed from `finishedTyping` with the same 600 ms minimum (a `ref` + `watch` as in Task 7, `face = 'heads'` since `playerWon` is always true), mounted in the card's centre slot above the `practice-typer` stream; result lines unchanged except `PULL THE LEVER TO FLIP AGAIN.`.
- [ ] **Step 5: Lint** — `yarn lint`.
- [ ] **Step 6: Commit** — `git commit -am "feat(practice): the duel tutorial flips the coin (#219)"`.

---

### Task 9: Guide copy + LP blurb

**Files:**
- Modify: `src/components/guide/sections/GuideCoinflip.vue` (steps 2–5 bodies/titles, tip)
- Modify (LP worktree): `E:/Monkify/.worktrees/duel-quick-flip/monkify-lp/src/components/GameModes.vue:62-63`

- [ ] **Step 1: Guide copy** — spec §7.10 strings verbatim; art imports unchanged for now (Task 12 re-crops; `wordArt` import renamed to `coinArt` pointing at `coinflip-coin.png` once it exists — do that in Task 12 to keep the build green).
- [ ] **Step 2: LP blurb** — `"Flip the $STROKE coin against the house on the spot, or take another player's open duel. First word Edson types takes both stakes."`
- [ ] **Step 3: Lint front; `npx astro build` in the LP worktree** (install deps there first if `node_modules` is absent: junction the LP hub's `node_modules`).
- [ ] **Step 4: Commit both** — front: `git commit -am "docs(guide): VS HOUSE has nothing to type (#219)"`; LP: `git commit -am "Monkey Duel card: flip the STROKE coin (#219)"`; push both branches.

---

### Task 10: Retired-art guard + push

- [ ] **Step 1:** `grep -rn "monk-coin.png" src/` → must print nothing.
- [ ] **Step 2:** `git push origin feature/duel-quick-flip` in both worktrees.

---

### Task 11: Local deploy + smoke (implement-feature Step 10/11)

- [ ] **Step 1:** Backend worktree at `origin/main` for the deploy (`git -C E:/Monkify/monkify-backend worktree add ../.worktrees/duel-quick-flip/monkify-backend origin/main`), copy hub `monkify-front/yarn.lock` into the front worktree, run `.worktrees/duel-quick-flip/monkify-backend/scripts/setup_local_environment.bat` (the running stack is `main@…`, marker kind `main` — a Replace of the main stack; confirm via the skill's 10b rule).
- [ ] **Step 2:** Verify api/consumers/database/redis + `monkify-front` all Up together; bundle contains `monk-coin-tails`.
- [ ] **Step 3:** `node scripts/smoke-duel-coin.mjs http://localhost:6003 <duelTypeId> 10` → exit 0, at least one fair-draw round observed.
- [ ] **Step 4:** Headless-Edge screenshots at 1440×900 and 390×844 (idle / spinning / landed, OPEN DUELS, practice) for the hand-off.

---

### Task 12: Guide art re-crops (needs the running stack)

- [ ] **Step 1:** From the live local round at 1440×900 with `VUE_APP_ALLOW_FREE_BETS=false` (or the dev toggle hidden via CSS in the capture page), crop at 2x nearest-neighbour: `coinflip-coin.png` (coin mid-spin + proof strip), `coinflip-stake.png` (½ · 2× · MAX + stake row), `coinflip-lever.png` (tag `FLIP 0.01`), `coinflip-screen.png` (switch + LAST 6). Delete `coinflip-word.png`; rename the import to `coinArt`.
- [ ] **Step 2:** `yarn lint && yarn build`; commit `docs(guide): re-crop the duel steps from the coin cabinet (#219)`; push.
