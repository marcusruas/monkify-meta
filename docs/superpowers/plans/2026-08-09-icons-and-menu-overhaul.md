# Icons and Menu Overhaul — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `monkify-front` one pixel-art icon language — a shared `<AppIcon>` registry of 26 drawings that replaces nine inline feather-stroke SVGs, a literal VT323 `X`, the `✓ ✗ ▶ ▼` text characters and `spinner.svg` — and rebuild the ☰ header menu as a 3-column icon grid.

**Architecture:** One data module (`src/constants/icons.ts`) holds every drawing in the form it was authored in (coarse 8×8 bitmap, fine 16×16 bitmap, or raw rects) plus a memoised rasteriser that run-length-encodes bitmaps into the fewest `<rect>`s per row. One renderer (`src/components/ui/AppIcon.vue`) turns a name + size into the SVG contract (`crispEdges`, `currentColor`, `aria-hidden`, `focusable="false"`). Orbit's seven shipped drawings move in as **raw rects emitted verbatim**, which is what guarantees zero visual change on Orbit's surfaces. `OrbitMark.vue` and `OrbitGlyph.vue` are deleted.

**Tech Stack:** Vue 3.2 `<script setup>` + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3 + DaisyUI, `arcade.css` material layer. Package manager **yarn**.

## Global Constraints

- **Spec is authoritative:** `monkify-meta/specs/icons-and-menu-overhaul.md`. Every drawing in §5 is transcribed verbatim from the design artifact — **copy the bitmaps character for character**; a single flipped `.`/`#` changes the silhouette.
- **Worktrees:** all work happens in `<workspace-root>/.worktrees/icons-and-menu-overhaul/monkify-front` and `.../monkify-meta`, branch `feature/icons-and-menu-overhaul`. The hubs stay on a clean `main`; never `git checkout`/`git switch` in a hub.
- **No backend change.** No file under `monkify-backend` is touched. No unit test and no integration test is added or changed (`monkify-front` has no test suite; `AGENTS.md` rule 8 does not bite — there is no backend happy path to drive). Report that explicitly at the end.
- **⚠️ CRLF.** `yarn lint` runs prettier with `endOfLine: 'auto'`, which means a file must keep **uniform** line endings. Never run `sed -i` or any tool that rewrites a file to LF — prettier then fails with `Insert ␍` on every line. Edit with the Edit/Write tools only; verify with `git diff --stat` staying proportional to the real change.
- **Prettier settings** (from `.eslintrc.js`): `tabWidth: 2`, `singleQuote: true`, `semi: true`, `trailingComma: 'es5'`, `printWidth: 100`, `vueIndentScriptAndStyle: true`.
- **`yarn lint` covers `.ts,.tsx,.js,.jsx,.vue` only** — `arcade.css` is **not** linted, so match the indentation of the surrounding region in that file (the top ~2000 lines are 2-space indented; the `html.light-graphics` section at the end is at column 0).
- **The cascade rule** (`conventions/frontend.md`): `arcade.css` classes are plain single-class rules imported after `@tailwind utilities`, so a class that declares a property **beats** any utility stacked on it. Therefore `.menu-pad__cell` must **not** declare a base `color` — the resting colour stays the `text-terminal-light` utility at the call site, and the class declares `color` only on `:hover` and `.is-current`, which are (0,2,0) and win on specificity.
- **Icons are decorative.** The accessible name always comes from the control (`aria-label` / visible label), never from the SVG.
- **Semantic fill is exactly two icons:** `win-at` → `var(--color-live)`, `falls-to` → `oklch(0.8 0.16 25)`. Everything else is `currentColor`. Do **not** promote the design artifact's `tone:` fields into fills (spec Finding B) — that would turn Orbit's `run` glyph green.
- **Orbit's existing call sites keep their shipped `:size` values verbatim** (11, 13, 14, 18, 20, 24, 26, 28, 30, 52), several of which are off the new legal-size ladder. Zero visual change (AC5) overrides the ladder (AC12) for these (spec Finding C). Do **not** "fix" them.
- **Legal sizes for NEW placements:** coarse 8×8 grid → 16 / 24 / 32 / 40px; fine 16×16 grid → 16 / 32 / 48px.
- **Definition of done:** `yarn lint` green, `yarn build` green, plus the browser QA in Task 7 with **0 console errors**.
- **Secrets/config:** nothing is touched.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/constants/icons.ts` | **NEW.** The 26 drawings + the `IconName` union + the memoised bitmap→rects rasteriser. The system's only data. |
| `src/components/ui/AppIcon.vue` | **NEW.** The render contract: name + size → one `<svg>`. Nothing else in the app writes icon markup. |
| `src/components/orbit/OrbitMark.vue` | **DELETE.** Its rects live in the registry as `orbit`; its Orbit-scoped rule comment is rewritten into the registry header. |
| `src/components/orbit/OrbitGlyph.vue` | **DELETE.** Its six rects + `pot` live in the registry; its 24-unit-grid reasoning moves with `run`/`stop`. |
| `src/components/GameHeader.vue` | 5 inline SVGs → `AppIcon`; the 7-row text dropdown → the 3×3 pad. |
| `src/components/header/WalletButtons.vue` | 3 inline wallet SVGs → `AppIcon name="wallet"`. |
| `src/components/ui/ArcadeModal.vue` | The literal `X` → `AppIcon name="close"`. |
| `src/assets/styles/arcade.css` | `.dialog__close` dead font rules out; `.menu-pad*` in; the `loading` step-rotation keyframes + reduced-motion guard; the light-graphics `.menu-pad` override. |
| `src/components/LoaderSpinner.vue` | `spinner.svg` `<img>` → `AppIcon name="loading" :size="160"`. |
| `src/components/game/GameFairPanel.vue` | `✓`/`✗` ×5 → `check` / `close`. |
| `src/pages/GameHistoryPage.vue` | `✓ MINE ONLY` → reserved-width `check` + constant label. |
| `src/components/orbit/*.vue`, `src/pages/OrbitPage.vue`, `src/components/home/HomeOrbitCard.vue` | Orbit fold-in (20 call sites), the stake→pot split, the four `▶`/`▼` sites, and the four stale comments. |

**Why the rasteriser lives in `icons.ts`, not `AppIcon.vue`** (a deliberate, noted deviation from the spec's file-checklist wording): the memo cache must be **module-level**, and a `<script setup>` body re-runs per component instance. Putting the pure function beside its data keeps the cache a genuine module singleton and keeps `AppIcon.vue` a thin renderer. Same two files, same behaviour.

---

### Task 1: The icon registry

**Files:**
- Create: `src/constants/icons.ts`
- Verify: `yarn lint`, `yarn build`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `export type IconName` — a string-literal union of all 26 names.
  - `export type IconRect = [number, number, number, number]` (`x, y, w, h`).
  - `export interface IconDef { b?: string[]; b16?: string[]; r?: IconRect[]; g?: number; fill?: string; spin?: true }`
  - `export const ICONS: Record<IconName, IconDef>`
  - `export function iconRects(name: IconName): IconRect[]` — memoised.
  - `export function iconGrid(name: IconName): number` — `ICONS[name].g ?? 16`.

- [ ] **Step 1: Create `src/constants/icons.ts` with the header comment, types and rasteriser**

```ts
/**
 * THE icon registry. One shared set of drawings for every player-facing mark in the app.
 *
 * ⚠️ THIS RETIRES THE #124 D7 / AC16 RULE. `OrbitMark` and `OrbitGlyph` used to carry a note saying
 * they were "NOT the seed of an app-wide icon system", because a documented design rule with one
 * feature's worth of consumers is a dangling rule. Trello #159 turned that around: Orbit's technique
 * — pixel-grid inline SVG, `crispEdges`, `currentColor`, decorative — IS the house technique now, and
 * this file is where every drawing lives. Both components are deleted; their rects are below,
 * unchanged, so their call sites render byte-identical SVG.
 *
 * TWO GRIDS, DECLARED PER ICON:
 *   • COARSE 8×8 (`b`) is the DEFAULT — eight 8-character rows, each `#` a 2×2 block in a 16-unit
 *     box. Pixel-exact at multiples of 8: 16 / 24 / 32 / 40px.
 *   • FINE 16×16 (`b16`) is OPT-IN and only for a subject the coarse grid provably cannot hold —
 *     sixteen 16-character rows, each `#` one unit. Pixel-exact at multiples of 16: 16 / 32 / 48px.
 *     Today exactly one drawing uses it: `practice` (a console gamepad — wings, d-pad cross, a
 *     four-button diamond and two centre dashes do not exist in eight cells across).
 *   • RAW RECTS (`r`) are emitted VERBATIM, never rasterised. Production drawings only.
 *
 * A new icon declares its own grid and the renderer handles it — adding one is a data change, not a
 * system change. Coarse by default; reach for fine only when the silhouette cannot be drawn coarse.
 *
 * ⚠️ SEMANTIC FILL IS EXACTLY TWO ICONS — `win-at` (green, a rise) and `falls-to` (warm red, a fall),
 * carried over from OrbitGlyph. Everything else paints `currentColor` and takes the surface's tint for
 * free (amber wallet pill, violet Orbit pill, muted eyebrow). Do not bake a colour into a third one.
 *
 * `.` (and any character that is not `#`) is empty. Rows are exactly as wide as the grid.
 */

export type IconRect = [number, number, number, number];

export type IconName =
  // Page & feature identity
  | 'home'
  | 'practice'
  | 'orbit'
  | 'stats'
  | 'fair'
  | 'how-to-play'
  | 'history'
  | 'responsible'
  | 'settings'
  // Chrome
  | 'menu'
  | 'close'
  | 'sound-on'
  | 'sound-off'
  | 'wallet'
  | 'stake'
  | 'pot'
  | 'loading'
  | 'chevron'
  | 'check'
  | 'warn'
  // Orbit, folded in unchanged
  | 'deposit'
  | 'cash-out'
  | 'win-at'
  | 'falls-to'
  | 'run'
  | 'stop';

export interface IconDef {
  /** Coarse 8×8 authoring form — eight 8-char rows, each '#' a 2×2 block in a 16-unit box. */
  b?: string[];
  /** Fine 16×16 authoring form — sixteen 16-char rows, each '#' one unit. Opt-in. */
  b16?: string[];
  /** Raw rects, emitted verbatim. Production drawings only. */
  r?: IconRect[];
  /** Unit box; defaults to 16. Only `run` / `stop` differ (24). */
  g?: number;
  /** Baked semantic fill. ONLY win-at and falls-to. */
  fill?: string;
  /** Renders with the four-step rotation. Only `loading`. */
  spin?: true;
}
```

- [ ] **Step 2: Append the coarse identity drawings**

Copy these **exactly** from spec §5.1. Each is eight rows of eight characters.

```ts
export const ICONS: Record<IconName, IconDef> = {
  // ── Page & feature identity ──────────────────────────────────────────────────────────────────
  home: {
    b: [
      '...##...',
      '..####..',
      '.######.',
      '########',
      '.######.',
      '.##..##.',
      '.##..##.',
      '.##..##.',
    ],
  },
  /**
   * ⚠️ THE ONE FINE-GRID DRAWING. A console gamepad: wings, a d-pad cross, a four-button diamond and
   * two centre dashes. Three earlier cuts failed — a reticle and an arcade stick were rejected as
   * subjects, and the first gamepad attempt failed because EIGHT CELLS ACROSS CANNOT HOLD ONE. The
   * owner's ~20px-wide reference is what forced the finer grid.
   * Accepted cost: it reads a step finer than its neighbours up close. Legal at 16 / 32 / 48px only.
   */
  practice: {
    b16: [
      '................',
      '................',
      '................',
      '..############..',
      '.##############.',
      '###.#######.####',
      '##...#####.#.###',
      '###.#######.####',
      '#######.#.######',
      '################',
      '.####......####.',
      '.####......####.',
      '..##........##..',
      '................',
      '................',
      '................',
    ],
  },
  /**
   * THE Orbit mark, byte-for-byte as `OrbitMark.vue` shipped it: a body inside a stepped orbital
   * path, with the satellite thickened to a 4px node at the top right so the path reads directional.
   * Not a wallet, a gear or a coin. Stored as raw rects precisely so its ~10 call sites render
   * identical markup — a re-cut was mocked and rejected (#159). Colour stays at the call site, which
   * is what keeps "violet is identity" enforceable there rather than baked in here.
   */
  orbit: {
    g: 16,
    r: [
      [6, 0, 4, 2],
      [4, 2, 2, 2],
      [10, 2, 4, 4],
      [2, 4, 2, 2],
      [0, 6, 2, 2],
      [14, 6, 2, 2],
      [0, 8, 2, 2],
      [14, 8, 2, 2],
      [2, 10, 2, 2],
      [12, 10, 2, 2],
      [4, 12, 2, 2],
      [10, 12, 2, 2],
      [6, 14, 4, 2],
      [6, 6, 4, 4],
    ],
  },
  /** A CRT with a rising trace. Was ascending bars — that collided with Orbit's `win-at` bar chart. */
  stats: {
    b: [
      '########',
      '#.....##',
      '#....#.#',
      '#..##..#',
      '#.#....#',
      '#......#',
      '########',
      '..####..',
    ],
  },
  fair: {
    b: [
      '..####..',
      '.##..##.',
      '.##..##.',
      '########',
      '########',
      '###..###',
      '########',
      '########',
    ],
  },
  'how-to-play': {
    b: [
      '.######.',
      '##....##',
      '......##',
      '...####.',
      '...##...',
      '........',
      '...##...',
      '........',
    ],
  },
  /** A clock face, hands at three. An hourglass and a rewind glyph were both rejected. */
  history: {
    b: [
      '..####..',
      '.#....#.',
      '#..#...#',
      '#..#...#',
      '#..###.#',
      '#......#',
      '.#...##.',
      '..####..',
    ],
  },
  /**
   * A page with a folded corner and three knocked-out lines of text. A shield was rejected: its two
   * knocked-out slots read as a face.
   */
  responsible: {
    b: [
      '.#####..',
      '.######.',
      '.#....#.',
      '.######.',
      '.#....#.',
      '.######.',
      '.#...##.',
      '.######.',
    ],
  },
  settings: {
    b: [
      '........',
      '..##....',
      '########',
      '..##....',
      '.....##.',
      '########',
      '.....##.',
      '........',
    ],
  },
```

- [ ] **Step 3: Append the chrome drawings**

Copy these **exactly** from spec §5.2.

```ts
  // ── Chrome ───────────────────────────────────────────────────────────────────────────────────
  menu: {
    b: [
      '........',
      '########',
      '........',
      '########',
      '........',
      '########',
      '........',
      '........',
    ],
  },
  /** Doubles as the `✗` mismatch marker when the call site tints it warm red. */
  close: {
    b: [
      '........',
      '.#....#.',
      '..#..#..',
      '...##...',
      '...##...',
      '..#..#..',
      '.#....#.',
      '........',
    ],
  },
  'sound-on': {
    b: [
      '........',
      '....#...',
      '...##..#',
      '.####.##',
      '.####.##',
      '...##..#',
      '....#...',
      '........',
    ],
  },
  'sound-off': {
    b: [
      '........',
      '....#...',
      '...###.#',
      '.####.#.',
      '.####.#.',
      '...###.#',
      '....#...',
      '........',
    ],
  },
  wallet: {
    b: [
      '........',
      '.######.',
      '########',
      '########',
      '#####..#',
      '#####..#',
      '########',
      '.######.',
    ],
  },
  /**
   * ⚠️ ONE COIN = THE STAKE, what you put in. The THREE-COIN STACK is `pot`, what is on the table
   * (owner's rule, #159: "pot should be a stack of coins, your stake is generally a single thing").
   * This replaces OrbitGlyph's old argument that three coins meant "what ONE round costs" — that
   * drawing kept its rects and changed its name, and the split applies INSIDE Orbit too:
   * OrbitStatStrip, OrbitStatusBanner and /orbit all mark their stake with this single coin now.
   */
  stake: {
    b: [
      '..####..',
      '.######.',
      '########',
      '###..###',
      '###..###',
      '########',
      '.######.',
      '..####..',
    ],
  },
  /**
   * The three-coin stack — Orbit's shipped `stake` drawing, REASSIGNED to the pot. The rects are
   * unchanged; only the name and the meaning moved. Each coin is a narrow top face over a wider body,
   * which is what reads as a disc rather than as three plain bars.
   */
  pot: {
    g: 16,
    r: [
      [4, 2, 8, 2],
      [2, 4, 12, 2],
      [4, 6, 8, 2],
      [2, 8, 12, 2],
      [4, 10, 8, 2],
      [2, 12, 12, 2],
    ],
  },
  /** Animated in four 90° steps — see `.app-icon--spin` in arcade.css for why not a continuous spin. */
  loading: {
    spin: true,
    b: [
      '..####..',
      '.##..##.',
      '##....#.',
      '##......',
      '#.......',
      '........',
      '........',
      '........',
    ],
  },
  /** Points RIGHT. The expanded state is this same drawing rotated 90° — pixel art stays aligned. */
  chevron: {
    b: [
      '..#.....',
      '..##....',
      '..###...',
      '..####..',
      '..####..',
      '..###...',
      '..##....',
      '..#.....',
    ],
  },
  check: {
    b: [
      '........',
      '......##',
      '.....##.',
      '#...##..',
      '##.##...',
      '.####...',
      '..##....',
      '........',
    ],
  },
  warn: {
    b: [
      '...##...',
      '..####..',
      '..#..#..',
      '.##..##.',
      '.##..##.',
      '##.##.##',
      '##....##',
      '########',
    ],
  },
```

- [ ] **Step 4: Append Orbit's remaining raw-rect drawings and close the map**

Copy these **verbatim** from spec §5.3 — they are the production data from `OrbitGlyph.vue:60-137`.

```ts
  // ── Orbit, folded in unchanged ───────────────────────────────────────────────────────────────
  // deposit and cash-out are ONE glyph flipped: they share a bar.
  deposit: {
    g: 16,
    r: [
      [6, 1, 4, 6],
      [3, 7, 10, 2],
      [5, 9, 6, 2],
      [7, 11, 2, 2],
      [1, 14, 14, 2],
    ],
  },
  'cash-out': {
    g: 16,
    r: [
      [7, 1, 2, 2],
      [5, 3, 6, 2],
      [3, 5, 10, 2],
      [6, 7, 4, 6],
      [1, 14, 14, 2],
    ],
  },
  /**
   * ⚠️ THE TWO RULE GLYPHS CARRY SEMANTIC COLOUR — green for a rise, warm red for a fall, the same two
   * hues the dialog's OK/ERR strips use, so nothing is stolen from the accents: neither is amber, cyan
   * or violet. The ERR hue is written LITERALLY rather than tokenised, exactly as arcade.css's
   * `.dialog__strip--err` states it; adding a token for it would be a design-system change #159 is not
   * making. These two are the ONLY entries in this file with a `fill`.
   */
  'win-at': {
    g: 16,
    fill: 'var(--color-live)',
    r: [
      [0, 0, 16, 2],
      [1, 11, 3, 5],
      [6, 7, 3, 9],
      [11, 2, 3, 14],
    ],
  },
  'falls-to': {
    g: 16,
    fill: 'oklch(0.8 0.16 25)',
    r: [
      [0, 14, 16, 2],
      [1, 0, 3, 4],
      [6, 0, 3, 8],
      [11, 0, 3, 14],
    ],
  },
  /**
   * ⚠️ RUN and STOP draw on a 24-UNIT GRID; every other drawing here keeps 16. This is the precedent
   * for "the grid is per icon" and it is load-bearing, not tidiness.
   *
   * They are the only glyphs rendered at DISPLAY size (24-28px in the transport row) rather than as a
   * 12-13px inline mark, and a diagonal needs step COUNT that 16 units cannot supply there: at 24px a
   * 16-unit box gives 1.5 device pixels per unit, so a 2-unit step is the practical floor and the
   * triangle gets four width steps. Four steps read as a flag on a pole no matter how the rects were
   * arranged. On a 24-unit grid the units land 1:1 on pixels at the size these are actually drawn.
   *
   * ⚠️ `run` is built from ROWS, not columns — that inversion is the whole trick, and two earlier cuts
   * got it wrong. Drawn as vertical columns, a triangle centred on this grid needs column height h at
   * y = 8 - h/2, so an even y only exists when h is a multiple of 4: at most FOUR steps. Drawn as
   * horizontal rows the constrained axis becomes WIDTH, which has no centring constraint at all — so
   * 11 rows of 2 stepping 3/6/9/12/15/18 all stay integral at 1.5x and 2x.
   *
   * `stop` is sized to the play triangle's own bounds (y 1-23) rather than the full box, so the pair
   * reads as one control at one weight instead of pause looking like the taller of the two.
   */
  run: {
    g: 24,
    r: [
      [3, 1, 3, 2],
      [3, 3, 6, 2],
      [3, 5, 9, 2],
      [3, 7, 12, 2],
      [3, 9, 15, 2],
      [3, 11, 18, 2],
      [3, 13, 15, 2],
      [3, 15, 12, 2],
      [3, 17, 9, 2],
      [3, 19, 6, 2],
      [3, 21, 3, 2],
    ],
  },
  stop: {
    g: 24,
    r: [
      [4, 1, 6, 22],
      [14, 1, 6, 22],
    ],
  },
};
```

- [ ] **Step 5: Append the rasteriser**

```ts
/**
 * Rasterised rects, cached per icon. The geometry is static, so a given icon is computed at most once
 * per page load. Module-level on purpose — this is why the function lives beside the data rather than
 * inside `AppIcon.vue`, whose `<script setup>` body re-runs for every instance.
 */
const rectCache = new Map<IconName, IconRect[]>();

/**
 * Run-length encode ONE authoring row: the fewest rects that draw it. `cell` is how many units one
 * character covers — 2 on the coarse grid, 1 on the fine one.
 */
function encodeRow(row: string, y: number, cell: number, out: IconRect[]): void {
  let start = -1;
  // Deliberately runs one PAST the end so a run touching the right edge is still closed.
  for (let x = 0; x <= row.length; x += 1) {
    const filled = row[x] === '#';
    if (filled && start < 0) start = x;
    else if (!filled && start >= 0) {
      out.push([start * cell, y * cell, (x - start) * cell, cell]);
      start = -1;
    }
  }
}

/** The rects to paint for `name`. Raw-rect drawings are returned VERBATIM — never re-encoded. */
export function iconRects(name: IconName): IconRect[] {
  const cached = rectCache.get(name);
  if (cached) return cached;

  const def = ICONS[name];
  let rects: IconRect[];

  if (def.r) {
    // Verbatim. This is what guarantees Orbit's folded-in drawings emit identical markup.
    rects = def.r;
  } else {
    rects = [];
    const rows = def.b16 ?? def.b ?? [];
    const cell = def.b16 ? 1 : 2;
    rows.forEach((row, y) => encodeRow(row, y, cell, rects));
  }

  rectCache.set(name, rects);
  return rects;
}

/** The icon's unit box. 16 for everything except `run` / `stop`, which are 24. */
export function iconGrid(name: IconName): number {
  return ICONS[name].g ?? 16;
}
```

- [ ] **Step 6: Verify it compiles and lints**

Run: `yarn lint` then `yarn build`
Expected: both exit 0. `icons.ts` is imported by nothing yet, so the only failure mode here is a syntax/type error or a prettier complaint.

- [ ] **Step 7: Verify every bitmap row is the right length** (a transcription typo is invisible until it renders)

Run, from the front worktree:

```bash
node -e "
const src = require('fs').readFileSync('src/constants/icons.ts','utf8');
let bad = 0;
for (const m of src.matchAll(/(\w[\w'-]*):\s*\{[^}]*?b(16)?:\s*\[([^\]]*)\]/gs)) {
  const fine = !!m[2];
  const rows = [...m[3].matchAll(/'([^']*)'/g)].map(r => r[1]);
  const w = fine ? 16 : 8;
  if (rows.length !== w || rows.some(r => r.length !== w)) { console.log('BAD', m[1], rows.length, rows.map(r=>r.length).join(',')); bad++; }
}
console.log(bad === 0 ? 'ALL BITMAPS OK' : bad + ' BAD');
"
```

Expected: `ALL BITMAPS OK` (18 bitmap drawings: 17 coarse + `practice` fine).

- [ ] **Step 8: Commit**

```bash
git add src/constants/icons.ts
git commit -m "Add the app-wide pixel icon registry (monkify-front)."
```

---

### Task 2: `AppIcon.vue` — the renderer

**Files:**
- Create: `src/components/ui/AppIcon.vue`
- Modify: `src/assets/styles/arcade.css` (append the `loading` spin rules)
- Verify: `yarn lint`, `yarn build`

**Interfaces:**
- Consumes: `ICONS`, `iconRects`, `iconGrid`, `IconName` from Task 1.
- Produces: `<AppIcon :name="IconName" :size="number" />` — `size` defaults to `16`. Class and style fall through to the root `<svg>`.

- [ ] **Step 1: Create `src/components/ui/AppIcon.vue`**

```vue
<script setup lang="ts">
  import { computed } from 'vue';
  import { ICONS, iconGrid, iconRects, type IconName } from '@/constants/icons';

  /**
   * THE icon. Every player-facing mark in the app renders from the shared registry
   * (`constants/icons.ts`); nothing re-inlines an `<svg>`. This is the technique `OrbitMark` shipped
   * — pixel-grid inline SVG, `crispEdges`, `currentColor` — promoted from Orbit-scoped to app-wide by
   * Trello #159, which retires the #124 D7 / AC16 rule that said it must not be.
   *
   * Two grids, declared per icon: coarse 8×8 by default (pixel-exact at 16/24/32/40), fine 16×16
   * opt-in and only when the coarse grid cannot hold the silhouette (pixel-exact at 16/32/48).
   * ⚠️ Orbit's ~20 existing call sites keep their shipped sizes, several of which are off that ladder:
   * zero visual change on a shipped surface outranks the ladder, which governs NEW placements.
   *
   * ⚠️ ICONS ARE DECORATIVE. The accessible name comes from the control — its `aria-label` or its
   * visible label — never from here. `aria-hidden` + `focusable="false"` are unconditional.
   */
  const props = withDefaults(defineProps<{ name: IconName; size?: number }>(), { size: 16 });

  const rects = computed(() => iconRects(props.name));
  const viewBox = computed(() => {
    const g = iconGrid(props.name);
    return `0 0 ${g} ${g}`;
  });
  // Only `win-at` and `falls-to` carry one; everything else inherits the surface's tint.
  const fill = computed(() => ICONS[props.name].fill ?? 'currentColor');
  const spin = computed(() => ICONS[props.name].spin === true);
</script>

<template>
  <!-- ⚠️ `flex-none` carries over from OrbitMark / OrbitGlyph — several Orbit call sites are flex
       children and rely on it; dropping it squashes the mark inside a flex row.
       ⚠️ Do NOT set `inheritAttrs: false`: call sites tint by passing `class="text-orbit"` and fall
       through is how that reaches the root element. -->
  <svg
    :width="size"
    :height="size"
    :viewBox="viewBox"
    :fill="fill"
    aria-hidden="true"
    focusable="false"
    shape-rendering="crispEdges"
    class="flex-none"
    :class="{ 'app-icon--spin': spin }"
  >
    <rect
      v-for="(rect, index) in rects"
      :key="index"
      :x="rect[0]"
      :y="rect[1]"
      :width="rect[2]"
      :height="rect[3]"
    />
  </svg>
</template>
```

- [ ] **Step 2: Append the spin rules to `arcade.css`**

Add at the **end** of the file, after the last `html.light-graphics` rule, at **column 0** to match that region:

```css
/* ============================================================
   The `loading` icon's rotation (Trello #159)
   ============================================================ */

/* ⚠️ FOUR 90° STEPS, never a continuous spin. A continuous rotation puts pixel edges at arbitrary
   angles and destroys the crisp read that `shape-rendering: crispEdges` exists to give; `steps(4)`
   keeps every frame axis-aligned. */
@keyframes app-icon-spin {
  to {
    transform: rotate(360deg);
  }
}
.app-icon--spin {
  transform-origin: 50% 50%;
  animation: app-icon-spin 1s steps(4) infinite;
}
/* A media query cannot stop a timer, but it can stop this — the whole animation is CSS. */
@media (prefers-reduced-motion: reduce) {
  .app-icon--spin {
    animation: none;
  }
}

/* EXCEPTION — deliberately NOT added to the `html.light-graphics … animation: none` list above.
   That list is for infinite animations that repaint forever for DECORATION; this one is the only
   sign of life on a blocking loader, and every viewport below 1024px is permanently in
   light-graphics, so freezing it there would make the app look hung on every phone. It is also the
   cheapest kind of animation to keep: a transform on one small element, four discrete frames per
   second, nothing underneath repainting. Same reasoning that keeps `.animate-wobble` and the money
   readout out of that list. */
```

- [ ] **Step 3: Verify**

Run: `yarn lint` then `yarn build`
Expected: both exit 0.

- [ ] **Step 4: Commit**

```bash
git add src/components/ui/AppIcon.vue src/assets/styles/arcade.css
git commit -m "Add AppIcon, the shared pixel-icon renderer (monkify-front)."
```

---

### Task 3: Fold Orbit into the registry

Deletes `OrbitMark.vue` + `OrbitGlyph.vue`, moves all 20 call sites to `AppIcon`, applies the stake→pot split, replaces the four `▶`/`▼` characters, and rewrites the four comments this card makes wrong.

**Files:**
- Delete: `src/components/orbit/OrbitMark.vue`, `src/components/orbit/OrbitGlyph.vue`
- Modify: `src/components/orbit/OrbitBetLock.vue:2,28` · `OrbitControlBar.vue:4,150-154` · `OrbitFirstWinModal.vue:4,178` · `OrbitLauncher.vue:3,73` · `OrbitModal.vue:7-8,925,956,978,1022,1408,1421,1444-1452,1774` · `OrbitStatStrip.vue:3-4,38-40,75,92-94` · `OrbitStatusBanner.vue:5-6,83,103-105,116` · `src/pages/OrbitPage.vue:5-6,338-339,377,384,391,398,416,549-552` · `src/components/home/HomeOrbitCard.vue:3` · `src/assets/styles/arcade.css:1612-1615` (comment only)
- Verify: `yarn lint`, `yarn build`, grep checks

**Interfaces:**
- Consumes: `AppIcon` (Task 2), `IconName` (Task 1).
- Produces: nothing new — this task only removes the two old components from the module graph.

- [ ] **Step 1: Swap the simple `OrbitMark` call sites**

In each file, replace the import and the tag. **Sizes and classes are copied across verbatim.**

`src/components/orbit/OrbitBetLock.vue`:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```
```diff
-    <OrbitMark :size="11" class="flex-none text-orbit" aria-hidden="true" />
+    <AppIcon name="orbit" :size="11" class="flex-none text-orbit" />
```

`src/components/orbit/OrbitFirstWinModal.vue`:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```
```diff
-        <OrbitMark :size="30" class="first-win__pitch-mark" aria-hidden="true" />
+        <AppIcon name="orbit" :size="30" class="first-win__pitch-mark" />
```
⚠️ Its comment at `:141` says *"the lead is a small phosphor square rather than the OrbitMark the Orbit panels…"* — change the word `OrbitMark` to `Orbit mark` so the prose still reads after the component is gone.

`src/components/orbit/OrbitLauncher.vue`:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```
```diff
-    <OrbitMark :size="18" />
+    <AppIcon name="orbit" :size="18" />
```

`src/components/home/HomeOrbitCard.vue` — the import is **unused** (no `<OrbitMark>` in its template). Delete the line outright:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
```

`src/components/orbit/OrbitControlBar.vue`:
```diff
-  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```
```diff
-      <OrbitGlyph
+      <AppIcon
         :name="status === 'running' ? 'stop' : 'run'"
         :size="28"
         class="orbit-trio__btn-icon"
       />
```

- [ ] **Step 2: Run the checks so far**

Run: `yarn lint`
Expected: exit 0. (`yarn build` will still fail until every consumer is swapped — that is expected mid-task; do not delete the two components yet.)

- [ ] **Step 3: Swap `OrbitStatStrip.vue` — including the stake→pot split**

Imports:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
-  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```

Header comment, item 1 (`:38-40`) — **rewrite**, do not leave it contradicting the card:
```diff
-   *   1. a mark on the left — `OrbitMark` for holds, the COIN STACK for the stake. Never OrbitMark
-   *      for a stake: the mark means "this money is inside Orbit" and a stake is a rate, so reusing
-   *      it there would make the symbol mean nothing (that rule is stated on the panel's own cards).
+   *   1. a mark on the left — the Orbit mark for holds, the SINGLE COIN for the stake. Never the
+   *      Orbit mark for a stake: the mark means "this money is inside Orbit", so reusing it there
+   *      would make the symbol mean nothing. ⚠️ The coin glyph CHANGED with Trello #159: one coin is
+   *      the stake (what you put in), the three-coin stack is the POT (what is on the table). This
+   *      cell used to draw the stack.
```

Template:
```diff
-      <OrbitMark :size="26" class="orbit-statstrip__icon text-orbit" aria-hidden="true" />
+      <AppIcon name="orbit" :size="26" class="orbit-statstrip__icon text-orbit" />
```
```diff
-      <!-- The COIN STACK, never OrbitMark — see this file's header note on why the two marks are not
-           interchangeable. -->
-      <OrbitGlyph name="stake" :size="26" class="orbit-statstrip__icon text-ink-mute" />
+      <!-- The SINGLE COIN, never the Orbit mark — see this file's header note on why the two marks
+           are not interchangeable, and on the #159 stake/pot split that changed this glyph. -->
+      <AppIcon name="stake" :size="26" class="orbit-statstrip__icon text-ink-mute" />
```

- [ ] **Step 4: Swap `OrbitStatusBanner.vue` — including the stake→pot split**

Imports:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
-  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```

Template:
```diff
-        <OrbitMark :size="24" class="orbit-stat__icon text-orbit" aria-hidden="true" />
+        <AppIcon name="orbit" :size="24" class="orbit-stat__icon text-orbit" />
```
```diff
-        <!-- The COIN STACK, never OrbitMark: the mark means "this money is inside Orbit" and a stake
-             is a rate, so reusing it here would make the symbol mean nothing. See OrbitGlyph. -->
-        <OrbitGlyph name="stake" :size="24" class="orbit-stat__icon text-ink-mute" />
+        <!-- The SINGLE COIN, never the Orbit mark: the mark means "this money is inside Orbit", so
+             reusing it here would make the symbol mean nothing.
+             ⚠️ Trello #159 split the money glyphs — ONE coin is the stake (what you put in), the
+             THREE-COIN STACK is the pot (what is on the table). This card used to draw the stack. -->
+        <AppIcon name="stake" :size="24" class="orbit-stat__icon text-ink-mute" />
```
```diff
-        <OrbitGlyph name="deposit" :size="24" class="orbit-stat__icon text-ink-mute" />
+        <AppIcon name="deposit" :size="24" class="orbit-stat__icon text-ink-mute" />
```

⚠️ Leave the `wallet ▶ orbit ▶ per round` reference in its header comment at `:17` alone — it describes a **deleted** arrow strip, is prose in a comment, and spec §7.6 lists it as an intended survivor.

- [ ] **Step 5: Swap `OrbitModal.vue` — 5 marks, 2 chevrons, 2 dead comments**

Imports:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
-  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```

`:925`:
```diff
-      <OrbitMark :size="13" class="flex-none text-orbit" aria-hidden="true" />
+      <AppIcon name="orbit" :size="13" class="flex-none text-orbit" />
```

`:978` and `:1022` (both identical):
```diff
-          <OrbitMark :size="52" class="orbit-intro__glyph" aria-hidden="true" />
+          <AppIcon name="orbit" :size="52" class="orbit-intro__glyph" />
```

`:1408` / `:1421`:
```diff
-                <OrbitGlyph name="run" :size="24" class="orbit-transport__icon" />
+                <AppIcon name="run" :size="24" class="orbit-transport__icon" />
```
```diff
-                <OrbitGlyph name="stop" :size="24" class="orbit-transport__icon" />
+                <AppIcon name="stop" :size="24" class="orbit-transport__icon" />
```

`:956` — the inline text link. `align-middle`, **not** `align-baseline`: a 16-unit box has no baseline.
```diff
-            How Orbit works ▶
+            How Orbit works <AppIcon name="chevron" :size="16" class="ml-0.5 inline align-middle" />
```

`:1444-1452` — the ADVANCED SETTINGS navigation button. Delete the now-dead `▸` warning from the comment and replace the character:
```diff
              as "How Orbit works ▶" in the help popover, which is the codebase's shape for an inline
              text link.
-             ⚠️ `▶`, never `▸` — U+25B8 is absent from Press Start 2P and renders as a bare dot. -->
+             ⚠️ The trailing arrow is the pixel `chevron` icon (Trello #159), not a text character.
+             It used to be `▶`, which Press Start 2P does not cover at all — the browser substituted a
+             system font, putting a smooth anti-aliased arrow inside pixel type. An icon also sizes
+             INDEPENDENTLY of the 8px font ramp, which is the other half of the fix. -->
```
```diff
-          <button type="button" class="orbit-advanced" @click="goToOrbitSettings">
-            ADVANCED SETTINGS ▶
-          </button>
+          <button type="button" class="orbit-advanced" @click="goToOrbitSettings">
+            ADVANCED SETTINGS
+            <AppIcon name="chevron" :size="16" />
+          </button>
```
⚠️ `.orbit-advanced` is not a flex container. Add `display: inline-flex; align-items: center; gap: 4px;` to its rule (`:1775-1782`) so the icon sits beside the label rather than on its own line, and replace the now-dead comment above it at `:1774`:
```diff
-  /* ⚠️ `▶`, never `▸` — U+25B8 is absent from Press Start 2P and renders as a bare dot. */
+  /* The trailing arrow is the pixel `chevron` icon (#159), sized independently of this 8px ramp. */
   .orbit-advanced {
+    display: inline-flex;
+    align-items: center;
+    gap: 4px;
     padding: 4px 6px;
```

`:1701` — a comment reading *"see the RUN / STOP note in OrbitGlyph"*. Repoint it: `see the RUN / STOP note in constants/icons.ts`.
`:1668` — a comment reading *"OrbitGlyph already assigns exactly these two hues…"*. Repoint it to `the icon registry (constants/icons.ts) already assigns…`.

- [ ] **Step 6: Swap `src/pages/OrbitPage.vue` — 6 marks, 2 chevrons (one rotating)**

Imports:
```diff
-  import OrbitMark from '@/components/orbit/OrbitMark.vue';
-  import OrbitGlyph from '@/components/orbit/OrbitGlyph.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```

`:338-339`, `:377`, `:384`, `:391`, `:398`:
```diff
-        <OrbitMark :size="18" class="text-orbit md:hidden" />
-        <OrbitMark :size="20" class="hidden text-orbit md:block" />
+        <AppIcon name="orbit" :size="18" class="text-orbit md:hidden" />
+        <AppIcon name="orbit" :size="20" class="hidden text-orbit md:block" />
```
```diff
-              <OrbitMark :size="14" class="mt-1 flex-none text-orbit" />
+              <AppIcon name="orbit" :size="14" class="mt-1 flex-none text-orbit" />
```
```diff
-              <OrbitGlyph name="deposit" :size="14" class="mt-1 flex-none text-ink-mute" />
+              <AppIcon name="deposit" :size="14" class="mt-1 flex-none text-ink-mute" />
```
```diff
-              <OrbitGlyph name="win-at" :size="14" class="mt-1 flex-none" />
+              <AppIcon name="win-at" :size="14" class="mt-1 flex-none" />
```
```diff
-              <OrbitGlyph name="falls-to" :size="14" class="mt-1 flex-none" />
+              <AppIcon name="falls-to" :size="14" class="mt-1 flex-none" />
```

`:416` — the inline text link:
```diff
-            How it works ▶
+            How it works <AppIcon name="chevron" :size="16" class="ml-0.5 inline align-middle" />
```

`:549-552` — **the only true disclosure**, and the only rotation in the whole card:
```diff
-        <!-- ▶/▼ rather than a rotating chevron: the page ships no transform animation, and a static
-             glyph that swaps is honest about that. -->
-        <span class="press-start-2p-regular text-[10px] text-orbit" aria-hidden="true">
-          {{ showLog ? '▼' : '▶' }}
-        </span>
+        <!-- The pixel `chevron`, rotated 90° when expanded (Trello #159). A 90° rotation of pixel art
+             stays pixel-aligned, so this is the one place the card rotates a glyph — the other three
+             ex-`▶` sites are a navigation button and two inline text links with no expanded state. -->
+        <AppIcon
+          name="chevron"
+          :size="16"
+          class="text-orbit transition-transform"
+          :class="showLog ? 'rotate-90' : ''"
+        />
```

⚠️ `:33` carries a `permanent ✓ at the top of the page` in prose inside a comment — leave it; it is not a template character.

- [ ] **Step 7: Delete the two components and repoint the last comment**

```bash
git rm src/components/orbit/OrbitMark.vue src/components/orbit/OrbitGlyph.vue
```

`src/assets/styles/arcade.css:1612-1615` — repoint the comment that names the deleted component:
```diff
-  /* 28px, matching the `:size="28"` the RUN/STOP `OrbitGlyph`s in this row already render at — the
+  /* 28px, matching the `:size="28"` the RUN/STOP icons in this row already render at — the
```
and, four lines down:
```diff
-     neighbours'. `OrbitGlyph`'s own `width`/`height` attrs are unaffected by `font-size`, so this rule
+     neighbours'. `AppIcon`'s own `width`/`height` attrs are unaffected by `font-size`, so this rule
```

- [ ] **Step 8: Verify the fold-in is complete**

Run, from the front worktree:
```bash
grep -rn "OrbitMark\|OrbitGlyph" src/ ; echo "exit=$?"
```
Expected: **no matches** (`exit=1`).

```bash
grep -rn "▶\|▼" src/ --include=*.vue
```
Expected: only three **comment** survivors — `OrbitStatusBanner.vue:17`, `OrbitModal.vue:1444` (the reworded prose reference to "How Orbit works ▶") and any other comment prose. **No template character.** If `OrbitModal.vue:1444`'s remaining `▶` bothers a reviewer it can be reworded, but it is prose describing a shape, not markup.

Run: `yarn lint` then `yarn build`
Expected: both exit 0. A `Module not found: OrbitMark.vue` here means a call site was missed.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Fold Orbit's marks into the shared icon registry and split stake from pot (monkify-front)."
```

---

### Task 4: The chrome swaps — wallet, modal close, spinner, pass/fail markers

**Files:**
- Modify: `src/components/header/WalletButtons.vue:151-164,197-210,233-246` · `src/components/ui/ArcadeModal.vue:241-249` · `src/assets/styles/arcade.css:338-358` · `src/components/LoaderSpinner.vue` · `src/components/game/GameFairPanel.vue:342,361,387-388,416` · `src/pages/GameHistoryPage.vue:177`
- Delete (conditionally): `src/assets/spinner.svg`
- Verify: `yarn lint`, `yarn build`, grep checks

**Interfaces:**
- Consumes: `AppIcon` (Task 2).
- Produces: nothing new.

- [ ] **Step 1: `WalletButtons.vue` — three inline wallet SVGs out**

Add the import:
```diff
   import MoneyValue from '@/components/ui/MoneyValue.vue';
+  import AppIcon from '@/components/ui/AppIcon.vue';
```

The **door / not-connected** pair at `:151-164` and `:233-246` are identical apart from context. Both become — note the size branch collapses to a single legal 16px, and the class branch survives because `md:hidden` still has to apply to the not-connected copy:
```diff
-          <svg
-            xmlns="http://www.w3.org/2000/svg"
-            viewBox="0 0 24 24"
-            fill="none"
-            stroke="currentColor"
-            stroke-width="2"
-            stroke-linecap="round"
-            stroke-linejoin="round"
-            :class="isDoor ? 'h-[18px] w-[18px]' : 'h-4 w-4 md:hidden'"
-          >
-            <path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" />
-            <path d="M3 5v14a2 2 0 0 0 2 2h16v-5" />
-            <path d="M18 12a2 2 0 0 0 0 4h4v-4h-4z" />
-          </svg>
+          <!-- 18px and 16px are both off/on the coarse ladder respectively; 16 is the legal
+               neighbour and the size the in-situ header mock draws this pill glyph at. -->
+          <AppIcon name="wallet" :size="16" :class="isDoor ? '' : 'md:hidden'" />
```

The **openWalletModal** copy at `:197-210` (always `h-4 w-4 md:hidden`, never a door):
```diff
-        <svg
-          xmlns="http://www.w3.org/2000/svg"
-          viewBox="0 0 24 24"
-          fill="none"
-          stroke="currentColor"
-          stroke-width="2"
-          stroke-linecap="round"
-          stroke-linejoin="round"
-          class="h-4 w-4 md:hidden"
-        >
-          <path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" />
-          <path d="M3 5v14a2 2 0 0 0 2 2h16v-5" />
-          <path d="M18 12a2 2 0 0 0 0 4h4v-4h-4z" />
-        </svg>
+        <AppIcon name="wallet" :size="16" class="md:hidden" />
```

⚠️ `doorLabel`, `aria-label` and `title` are untouched — the wallet's accessible name is unaffected.

- [ ] **Step 2: `ArcadeModal.vue` + `arcade.css` — the close `X`**

Import `AppIcon` in `ArcadeModal.vue`'s `<script setup>`, then:
```diff
               <button
                 v-if="dismissible"
                 type="button"
                 class="dialog__close"
                 aria-label="Close"
                 @click="requestClose('x')"
               >
-                X
+                <AppIcon name="close" :size="16" />
               </button>
```

`arcade.css:338-358` — the two font rules are now dead. Also update the comment above the rule:
```diff
-  /* — Close box — a bordered VT323 X, not an icon. 20px grey / 22px black. — */
+  /* — Close box — a bordered pixel `close` icon (Trello #159; it was a literal VT323 "X").
+     20px grey / 22px black. Neither box size is on the coarse icon ladder, so the icon inside renders
+     at 16px in both: the largest legal size that fits a 20px box with its 1px border. — */
   .dialog__close {
```
```diff
     border-radius: var(--radius-arcade);
-    font-family: var(--font-body, 'VT323', ui-monospace, monospace);
-    font-size: 16px;
     line-height: 1;
```

- [ ] **Step 3: `LoaderSpinner.vue` — `spinner.svg` out**

```vue
<script setup lang="ts">
  import AppIcon from '@/components/ui/AppIcon.vue';
</script>

<template>
  <div
    class="fixed w-screen h-screen top-0 left-0 bg-[color-mix(in_oklab,var(--color-void)_78%,transparent)] z-popover flex items-center justify-center"
  >
    <!-- 160px is a multiple of both 8 and 16, so it is pixel-exact on either grid. The rotation is
         four 90° steps (see `.app-icon--spin` in arcade.css), which is what keeps the edges hard
         where a continuous spin would smear them. -->
    <AppIcon name="loading" :size="160" class="text-ink/75" />
  </div>
</template>
```

Then check whether the asset is orphaned:
```bash
grep -rn "spinner.svg\|assets/spinner" src/ public/ 2>/dev/null
```
If there are **no** matches, `git rm src/assets/spinner.svg`. If anything still references it, leave the file.

- [ ] **Step 4: `GameFairPanel.vue` — five `✓`/`✗` markers**

Import `AppIcon`. The colour already comes from each site's existing `:class`, so the icon inherits it via `currentColor` — **add no fill**.

`:338-343`:
```diff
         <span
-          class="vt323-regular text-[19px] leading-none"
+          class="vt323-regular inline-flex items-center gap-1.5 text-[19px] leading-none"
           :class="commitmentOk ? 'text-primary' : 'text-red-400'"
         >
-          {{ commitmentOk ? '✓ SHA256 matches' : '✗ hash mismatch' }}
+          <AppIcon :name="commitmentOk ? 'check' : 'close'" :size="16" />
+          {{ commitmentOk ? 'SHA256 matches' : 'hash mismatch' }}
         </span>
```

`:355-362`:
```diff
           <span
             v-for="match in ownBetMatches"
             :key="match.nonce"
-            class="vt323-regular text-[19px] leading-tight text-primary"
+            class="vt323-regular inline-flex items-center gap-1.5 text-[19px] leading-tight text-primary"
           >
-            ✓ HMAC(clientSeed, {{ match.nonce }}) → {{ match.choice }}
+            <AppIcon name="check" :size="16" />
+            HMAC(clientSeed, {{ match.nonce }}) → {{ match.choice }}
           </span>
```

`:380-390` — ⚠️ this one is a **string-interpolating ternary producing the whole line**; it has to become markup so the icon can be an element:
```diff
           <span
-            class="vt323-regular text-[19px] leading-none"
+            class="vt323-regular inline-flex items-center gap-1.5 text-[19px] leading-none"
             :class="recomputeResult.ok ? 'text-primary' : 'text-red-400'"
           >
-            {{
-              recomputeResult.ok
-                ? `✓ "${recomputeResult.recomputedWinner}" matches`
-                : `✗ mismatch — recomputed "${recomputeResult.recomputedWinner}"`
-            }}
+            <AppIcon :name="recomputeResult.ok ? 'check' : 'close'" :size="16" />
+            <span>
+              {{
+                recomputeResult.ok
+                  ? `"${recomputeResult.recomputedWinner}" matches`
+                  : `mismatch — recomputed "${recomputeResult.recomputedWinner}"`
+              }}
+            </span>
           </span>
```

`:414-417`:
```diff
-          <span v-if="recomputeError" class="vt323-regular text-[19px] leading-tight text-red-400">
-            ✗ {{ recomputeError }}
+          <span
+            v-if="recomputeError"
+            class="vt323-regular inline-flex items-center gap-1.5 text-[19px] leading-tight text-red-400"
+          >
+            <AppIcon name="close" :size="16" />
+            {{ recomputeError }}
           </span>
```

- [ ] **Step 5: `GameHistoryPage.vue` — `✓ MINE ONLY` without a width jump**

Import `AppIcon`. `.btn-dialog` is already `inline-flex` with `gap: 0.6ch`, so a **fixed-width slot** keeps the button exactly the same width in both states:

```diff
         <button
           type="button"
           class="btn-dialog"
           :class="{ 'btn-dialog--active': mineOnly }"
           @click="toggleMine"
         >
-          {{ mineOnly ? '✓ MINE ONLY' : 'MINE ONLY' }}
+          <!-- The slot is always 16px wide so toggling does not reflow the filter row. -->
+          <span class="inline-flex h-4 w-4 flex-none items-center justify-center">
+            <AppIcon v-if="mineOnly" name="check" :size="16" />
+          </span>
+          <span>MINE ONLY</span>
         </button>
```

- [ ] **Step 6: Verify**

```bash
grep -rn "✓\|✗" src/ --include=*.vue
```
Expected: only `OrbitWordField.vue:171` and `OrbitPage.vue:33` — both **comments**, both out of scope per spec §7.6.

```bash
grep -rn "<svg" src/components/header/WalletButtons.vue src/components/ui/ArcadeModal.vue ; echo "exit=$?"
```
Expected: no matches.

Run: `yarn lint` then `yarn build`
Expected: both exit 0.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Swap the wallet, close, loader and pass/fail glyphs onto AppIcon (monkify-front)."
```

---

### Task 5: `GameHeader.vue` — the speaker/hamburger swap and the 3×3 menu pad

**Files:**
- Modify: `src/components/GameHeader.vue:222-251,294-307,310-440` (+ script)
- Modify: `src/assets/styles/arcade.css` (add the `.menu-pad*` block and its light-graphics override)
- Verify: `yarn lint`, `yarn build`, browser QA in Task 7

**Interfaces:**
- Consumes: `AppIcon` + `IconName` (Tasks 1-2), `useRoute` from `vue-router`, the existing `isEmbedded`, `orbitEnabled`, `isFairOpen`, `openSettings`, `menuOpen`, `menuRoot`, `audioMuted`, `audioVolume`.
- Produces: nothing consumed elsewhere.

- [ ] **Step 1: Add the imports and the cell model to `<script setup>`**

```diff
   import { computed, onMounted, ref, watch } from 'vue';
+  import { useRoute } from 'vue-router';
+  import AppIcon from '@/components/ui/AppIcon.vue';
+  import type { IconName } from '@/constants/icons';
```

Then, immediately after `openSettings` (`:131-134`), add:

```ts
  const openFair = () => {
    isFairOpen.value = true;
    menuOpen.value = false;
  };

  const route = useRoute();

  /**
   * THE ☰ MENU IS A 3-COLUMN ICON PAD (Trello #159), not the 180px text list it used to be: nine
   * destinations, each a 32px pixel mark above a centred Press Start 2P title.
   *
   * ⚠️ 32px is not arbitrary — it is the ONLY size both icon grids land on exactly (coarse is exact at
   * multiples of 8, fine at multiples of 16), which is also why the pad widened 234 → 270px.
   *
   * Home and Practice are NEW to the menu (Home was reachable only via the wordmark, Practice only
   * from three CTAs on `/`). ⚠️ `/game` — the plain lobby — deliberately gets NO cell: it is a
   * leftover page already absorbed into Home, and it was not in the menu before either.
   *
   * Fair and Settings open MODALS rather than routes, and their cells must look identical to the
   * seven route cells — the player is not told which is which. They are also never marked current,
   * because they have no route to compare against.
   *
   * ⚠️ EMBEDDED RENDERS THE SAME GRID, with the two surviving cells (History + Settings) in the same
   * 3-column frame. No alternate layout and no collapse-to-one-column branch: the embed is the
   * least-tested path in the app and must not own the only conditional layout.
   */
  type MenuCell = {
    key: string;
    icon: IconName;
    label: string;
    /** Overrides the visible label as the accessible name. Only where the two differ. */
    aria?: string;
    route?: { name: string };
    action?: () => void;
  };

  const menuCells = computed<MenuCell[]>(() => {
    const cells: MenuCell[] = [];
    if (!isEmbedded) {
      cells.push({ key: 'home', icon: 'home', label: 'HOME', route: { name: 'Home' } });
      cells.push({
        key: 'practice',
        icon: 'practice',
        label: 'PRACTICE',
        route: { name: 'Practice' },
      });
      if (orbitEnabled) {
        cells.push({ key: 'orbit', icon: 'orbit', label: 'ORBIT', route: { name: 'Orbit' } });
      }
      cells.push({ key: 'stats', icon: 'stats', label: 'STATS', route: { name: 'GameStats' } });
      cells.push({ key: 'fair', icon: 'fair', label: 'FAIR', action: openFair });
      cells.push({
        key: 'how-to-play',
        icon: 'how-to-play',
        label: 'HOW TO PLAY',
        route: { name: 'HowToPlay' },
      });
    }
    cells.push({
      key: 'history',
      icon: 'history',
      label: 'HISTORY',
      route: { name: 'GameHistory' },
    });
    if (!isEmbedded) {
      // The visible label is the short word — it is what fits an ~86px cell without a three-line
      // wrap — but the accessible name stays the full "Responsible Gaming".
      cells.push({
        key: 'responsible',
        icon: 'responsible',
        label: 'RESPONSIBLE',
        aria: 'Responsible Gaming',
        route: { name: 'ResponsibleGaming' },
      });
    }
    cells.push({ key: 'settings', icon: 'settings', label: 'SETTINGS', action: openSettings });
    return cells;
  });

  const isCurrent = (cell: MenuCell) => !!cell.route && route.name === cell.route.name;
```

- [ ] **Step 2: Swap the two bar-speaker SVGs and the hamburger**

`:222-251` (inside the bar audio button) — replace **both** `<svg>` branches with one element:
```diff
-          <svg v-if="audioMuted || audioVolume === 0" … >…</svg>
-          <svg v-else … >…</svg>
+          <AppIcon
+            :name="audioMuted || audioVolume === 0 ? 'sound-off' : 'sound-on'"
+            :size="24"
+          />
```

`:294-307` (the hamburger):
```diff
-          <svg
-            xmlns="http://www.w3.org/2000/svg"
-            viewBox="0 0 24 24"
-            …
-            class="h-6 w-6"
-          >
-            <line x1="3" y1="6" x2="21" y2="6" />
-            <line x1="3" y1="12" x2="21" y2="12" />
-            <line x1="3" y1="18" x2="21" y2="18" />
-          </svg>
+          <AppIcon name="menu" :size="24" />
```

- [ ] **Step 3: Replace the dropdown (`:310-440`) with the pad**

```html
        <!-- ⚠️ `.menu-pad` carries the geometry; the border/fill/radius/anchor stay the utilities they
             have always been, so nothing but the width and the layout changed. See arcade.css for why
             the light-graphics override beside it is required rather than belt-and-braces. -->
        <div
          v-if="menuOpen"
          class="menu-pad absolute right-0 z-popover mt-1 rounded-arcade border border-terminal-border/60 bg-[color-mix(in_oklab,var(--color-void)_94%,transparent)] shadow-lg backdrop-blur-md"
        >
          <!-- Audio lives here on phones (the bar speaker is md-only). ⚠️ NOT a grid cell — a cell
               cannot hold a slider — so it is a full-width strip above the grid, outside the grid
               element, with the same hairline under it that has always been there. -->
          <div class="md:hidden flex items-center gap-2 px-3 py-2">
            <button
              type="button"
              :aria-label="audioMuted ? 'Unmute sound' : 'Mute sound'"
              class="flex-shrink-0 text-terminal-light transition hover:text-primary"
              @click="toggleMute"
            >
              <AppIcon :name="audioMuted || audioVolume === 0 ? 'sound-off' : 'sound-on'" :size="16" />
            </button>
            <input
              v-model.number="audioVolume"
              type="range"
              min="0"
              max="1"
              step="0.01"
              aria-label="Volume"
              class="w-full accent-primary cursor-pointer"
              @input="onVolumeInput"
            />
          </div>
          <div class="md:hidden mx-2 mb-1 border-t border-terminal-border/40"></div>

          <!-- Plain DOM order gives left-to-right, top-to-bottom tabbing for free. ⚠️ No arrow-key
               roving focus: the Orbit tab bar's arrow keys are a `tablist` pattern and do not apply to
               a grid of links. -->
          <div class="menu-pad__grid">
            <template v-for="cell in menuCells" :key="cell.key">
              <RouterLink
                v-if="cell.route"
                :to="cell.route"
                class="menu-pad__cell text-terminal-light"
                :class="{ 'is-current': isCurrent(cell) }"
                :aria-current="isCurrent(cell) ? 'page' : undefined"
                :aria-label="cell.aria"
                @click="menuOpen = false"
              >
                <AppIcon :name="cell.icon" :size="32" />
                <span class="menu-pad__label">{{ cell.label }}</span>
              </RouterLink>
              <button
                v-else
                type="button"
                class="menu-pad__cell text-terminal-light"
                :aria-label="cell.aria"
                @click="cell.action?.()"
              >
                <AppIcon :name="cell.icon" :size="32" />
                <span class="menu-pad__label">{{ cell.label }}</span>
              </button>
            </template>
          </div>
        </div>
```

⚠️ The `menuRoot` ref on the wrapping `<div class="relative">` is **unchanged** and must keep wrapping **both** the ☰ button and this pad — `useClickOutside`'s contract requires it, or the click that opens the menu closes it in the same tick.

- [ ] **Step 4: Add the pad's CSS to `arcade.css`**

Insert **immediately after the `.btn-door--pill` rule block** (around `:1179-1196`, before the shared `:focus-visible` selector list), at the surrounding **2-space** indentation:

```css
  /* ── THE HEADER MENU PAD (Trello #159) ───────────────────────────────────────────────────────
     The ☰ menu is a 3-COLUMN ICON GRID, not the 180px text list it used to be. 270px because the
     cells hold 32px icons — the one size both the coarse 8×8 and the fine 16×16 grid land on
     exactly — and 234px could not.
     Only geometry lives here: the fill, border, radius and anchor stay the utilities on the element,
     unchanged from the old dropdown. */
  .menu-pad {
    width: 270px;
    padding: 4px;
  }
  .menu-pad__grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 2px;
  }
  /* ⚠️ NO base `color` here on purpose. arcade.css is imported after `@tailwind utilities` and these
     are single-class rules, so a `color` declared here would beat the `text-terminal-light` utility at
     the call site — the exact cascade trap `conventions/frontend.md` documents. The resting colour is
     the utility; hover and current are (0,2,0) and win on specificity without one. */
  .menu-pad__cell {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 7px;
    padding: 10px 4px 8px;
    background: transparent;
    border: 0;
    border-radius: 3px;
    cursor: pointer;
    transition:
      color 0.15s ease,
      background-color 0.15s ease;
  }
  .menu-pad__cell:hover {
    background: color-mix(in oklab, var(--color-primary) 12%, transparent);
    color: var(--color-primary);
  }
  /* "You are here" — an amber label over a faint amber wash, paired with `aria-current="page"`. The
     two modal cells (Fair, Settings) have no route and are never marked. */
  .menu-pad__cell.is-current {
    background: color-mix(in oklab, var(--color-primary) 8%, transparent);
    color: var(--color-primary);
  }
  .menu-pad__label {
    font-family: var(--font-display, 'Press Start 2P', system-ui, sans-serif);
    font-size: 0.5625rem;
    line-height: 1.25;
    letter-spacing: 0.07em;
    text-align: center;
    text-transform: uppercase;
  }
```

Then add `.menu-pad__cell` to the shared focus-ring selector list a few lines below (a visible ring is required, AC18):
```diff
   .btn-icon:focus-visible,
   .dialog-row:focus-visible,
   .dialog__close:focus-visible,
+  .menu-pad__cell:focus-visible,
```

And add the light-graphics override in the **opaque-surfaces** group near `:2150-2160`, at **column 0**:
```css
/* ⚠️ REQUIRED, not belt-and-braces. The blur-strip rule above only removes `backdrop-filter`; the
   opacity rules beside it name `.app-header`, `.header-content`, `.retro-box` and `.card-content` —
   the menu pad is none of those. Without this it survives on `color-mix(… 94%)`, i.e. by luck rather
   than by rule, on every viewport below 1024px. */
html.light-graphics .menu-pad {
  background: var(--color-void);
}
```

- [ ] **Step 5: Verify**

```bash
grep -n "<svg" src/components/GameHeader.vue ; echo "exit=$?"
```
Expected: no matches (`exit=1`).

Run: `yarn lint` then `yarn build`
Expected: both exit 0.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Rebuild the header menu as a 3x3 pixel-icon pad (monkify-front)."
```

---

### Task 6: Sync the spec's status and the KB in-flight row

**Files:**
- Modify: `monkify-meta/specs/icons-and-menu-overhaul.md` (§15 revision log)
- Verify: none beyond reading it back

> The full KB sync (`features/icons-and-menu-overhaul.md`, `conventions/frontend.md`,
> `features/orbit-ui-overhaul.md`) is **`/monkify-ship-feature`'s** job, not this plan's — spec §10.3
> marks those rows *(at ship time)*.

- [ ] **Step 1: Add the implementation row to the spec's revision log**

```markdown
| 2026-08-09 | Implemented on `feature/icons-and-menu-overhaul`. Deviation noted: the bitmap→rects rasteriser lives in `constants/icons.ts` beside its data rather than in `AppIcon.vue`, because the memo cache must be module-level and a `<script setup>` body re-runs per instance. `.app-icon--spin` is deliberately NOT in the `html.light-graphics` animation-kill list (a frozen loader reads as a hang on every phone). No unit or integration test changed — frontend-only, no test suite in `monkify-front`. |
```

- [ ] **Step 2: Commit (in the `monkify-meta` worktree)**

```bash
git add specs/icons-and-menu-overhaul.md docs/superpowers/plans/2026-08-09-icons-and-menu-overhaul.md
git commit -m "Record the icons-and-menu-overhaul implementation pass."
```

---

### Task 7: Verify — checks, build and browser QA

**Files:** none modified (fix-forward into the owning task if something fails).

- [ ] **Step 1: The mechanical checks**

From the front worktree, all of these must hold:

```bash
grep -rn "OrbitMark\|OrbitGlyph" src/            # expect: no matches
grep -rn "✓\|✗" src/ --include=*.vue             # expect: OrbitWordField.vue:171 + OrbitPage.vue:33 only (comments)
grep -n  "<svg" src/components/GameHeader.vue src/components/header/WalletButtons.vue src/components/ui/ArcadeModal.vue   # expect: no matches
grep -rn "spinner.svg" src/                      # expect: no matches
git diff --stat main -- '*.vue' '*.ts'           # sanity: line counts proportional to the change, NOT every line of a file (that means CRLF got mangled)
```

- [ ] **Step 2: `yarn lint` and `yarn build`**

Run: `yarn lint`
Expected: exit 0. If prettier reports `Insert ␍` on every line of a file, its line endings were mangled — restore CRLF before continuing (see Global Constraints).

Run: `yarn build`
Expected: exit 0.

- [ ] **Step 3: Bring up the local stack and serve the branch**

Docker must be running. From `monkify-backend` (the hub is fine — nothing there changed): `docker compose up -d`, then confirm `GET http://localhost:6001/health` → `"Api running!"`.

⚠️ In the **front worktree**, copy the hub's gitignored files in first or the app boots to "game unavailable":
```bash
cp ../../../monkify-front/.env.local .
cp ../../../monkify-front/yarn.lock .
yarn install --frozen-lockfile
yarn serve --port <a verified-free port>
```
⚠️ Verify the port is actually free first — another worktree's dev server holding it makes `serve` fail quietly and you end up QA'ing the wrong app.

- [ ] **Step 4: Browser QA — spec §13, all 14 checks, 0 console errors**

1. **Desktop, 64px header, standalone.** Wallet disconnected then connected: the wallet pill glyph is pixel art and amber. The speaker toggles between `sound-on` / `sound-off`. ☰ draws the pixel `menu`.
2. **Open the menu.** 270px, 3 columns, 9 cells, 32px icons above centred PS2P labels. Navigate to `/game/stats` and reopen: the **Stats** cell is amber-washed and carries `aria-current="page"` (check in DevTools).
3. **Tab through the open menu.** Focus moves 1→9 left-to-right, top-to-bottom with a visible cyan ring. Arrow keys do **nothing**. Esc closes; an outside click closes; a cell click closes and navigates.
4. **Fair and Settings cells** open their modals and look identical to the seven route cells.
5. **Phone width (< md).** The audio strip is full width above the grid with a hairline under it; the bar speaker is gone. The popover is legible — confirm `light-graphics` on `<html>` and `backdrop-filter: none` in computed styles.
6. **Embedded.** Load with `?embed=1`. Exactly **two** cells (History, Settings) in the same 3-column frame — first two columns of one row, not a list, not one column.
7. **Any modal** (Settings, Fair, Orbit): the close control is the pixel `close` glyph, not an `X`.
8. **`/game` after a round ends** → the FAIR tab: the commitment line shows a green `check`; force a mismatch (edit the payload in DevTools) and confirm the warm-red `close`.
9. **`/game/history`:** toggle MINE ONLY — the `check` appears and the button does **not** change width.
10. **`/orbit`:** the intro mark, the stat strip and the status banner are pixel-identical to `main` **except** that stake now shows the **single coin**. Expand "WHAT ORBIT HAS DONE" — the chevron rotates 90°. `win-at` is green, `falls-to` warm red.
11. **Orbit panel transport:** RUN/STOP unchanged.
12. **Reduced motion:** enable it in DevTools rendering; the loader freezes.
13. **Zoom to 200%** and check a 32px menu icon and a 24px header icon: edges stay hard, no half-pixel softening.
14. **Console: 0 errors** on every screen above.

- [ ] **Step 5: Screenshot-compare Orbit against `main`**

AC5 is *zero* visual change, so this is worth a side-by-side: open `/orbit` on the branch and on a `main` build at the same width, and confirm the only difference is the stake glyph.

- [ ] **Step 6: Hand off to the user — do NOT commit the branch further or move the Trello card until they confirm.**

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:

| Spec § | Task |
| --- | --- |
| §4 the icon system, §5 the 26 drawings, §4.5 registry shape | Task 1 |
| §4.2 rendering rules, §6 `AppIcon` contract, §7.4 the spin keyframes | Tasks 1-2 |
| §7.3 Orbit fold-in + stake→pot, §7.6 the four `▶`/`▼` sites, D9/D12 comment rewrites | Task 3 |
| §7.2 wallet, §7.5 close + `.dialog__close`, §7.4 LoaderSpinner, §7.6 `✓`/`✗` | Task 4 |
| §7.1 header SVGs, §7.7 the 3×3 pad (incl. Finding E's light-graphics override) | Task 5 |
| §10.3 meta rows that are due **now** | Task 6 |
| §12 acceptance criteria, §13 manual QA | Task 7 |
| §11 unit tests (**none**) | Global Constraints |

Acceptance criteria: AC1 Task 5+4 · AC2 Task 2 · AC3 Tasks 1,4 · AC4 Task 1 · AC5 Task 3 · AC6 Tasks 1,3 · AC7 Task 3 · AC8 Task 4 · AC9 Task 4 · AC10 Task 3 · AC11 Tasks 2,4 · AC12 Task 7 step 4.13 · AC13 Task 1 · AC14-AC19 Task 5 + Task 7 · AC20 Task 7.

**2. Placeholder scan** — no `TBD` / `similar to Task N` / "add error handling"; every code step carries the literal content.

**3. Type consistency** — `IconName`, `IconRect`, `IconDef`, `ICONS`, `iconRects`, `iconGrid` are defined in Task 1 and used with those exact names in Tasks 2 and 5. `MenuCell` is local to `GameHeader.vue`. `AppIcon`'s props (`name`, `size`) match every call site written in Tasks 3-5, and `size` keeps `OrbitMark`/`OrbitGlyph`'s name and `16` default so folded-in call sites need no change beyond the tag.
