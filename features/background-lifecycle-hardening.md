# Feature — Background & game display lifecycle hardening

> Status: **shipped.** Authoritative spec: `../specs/background-lifecycle-hardening.md`
> (merged via `feature/background-lifecycle-hardening` on 2026-08-09).
> **Frontend-only** — `monkify-front`. **Zero backend surface**: no entity, endpoint, header,
> consumer, migration or SignalR change, and no `monkify-backend` branch ever existed for it.
> Bug fix + QOL, so `constraints.md` §3's mandatory-integration-test rule did not apply.

## What it does

Switching Chrome tabs while rounds rolled over used to leave the game screen visibly broken — the
backdrop looked wrong ("many BGs"), new starfield elements stopped spawning, the typer never returned
to normal speed, and the lag carried into the next round. All of that came from **one root-cause
family: the animation layer trusted `setTimeout` and in-flight transitions across tab hide/show**, and
Chrome throttles (then freezes) timers in hidden tabs. Tab return is now a **resync to the live game**
rather than a resume, and every deadline is a wall-clock timestamp with a `visibilitychange` catch-up.

Two unrelated display bugs shipped alongside it (the terminal's horizontal scroll and a root-element
colour seam), plus one QOL addition: the starfield's baseline cost is now **lower for every player**,
behind a single `BG_DENSITY` constant a future slider can drive.

## Core rules

### Tab return = resync, never resume

- On becoming visible, `GameBackground` **re-derives its speed from the current `speedMultiplier`
  prop** (`currentSpeed = targetSpeed = speedTransitionStartSpeed = props.speedMultiplier`,
  `speedTransitionStartTime = 0`). It never repairs a paused transition — the game moved on while the
  tab was hidden, so there is nothing worth repairing.
  ⚠️ **This is the headline fix and it is arithmetic, not throttling.** The old code did
  `speedTransitionStartTime += pauseDuration`, but the `speedMultiplier` watcher may already have
  fired *while hidden* and reset that stamp to a later `performance.now()`. The sum then landed in the
  **future**, `progress` went negative, the ease function **squared** it (`2 * p * p`) and the
  transition ran **backwards** — and because speed is **cubed** (`SPEED_BOOST_FACTOR = 3`), a wrong
  `currentSpeed` was up to a **~27× error** on both backdrop drift and spawn cadence.
- Sprites are **advanced by the real elapsed wall-clock time** and anything at `progress >= 1` is
  spliced out, instead of a screen full of stale sprites resuming mid-flight.
- Spawning is **re-armed unconditionally**, and any in-flight `animationFrameId` is cancelled before a
  new frame is requested — rapid hide/show cycling can never leave two rAF loops or two spawn
  intervals running.
- **`restartSpawning` and `spawnElement` read `document.hidden` directly**, not the module-local
  `isPageVisible` flag. `isPageVisible` survives only as the rAF guard and is assigned in exactly one
  place. A desynced flag used to leave spawning **permanently** dead while the rAF loop kept running —
  that is the reported "no elements spawning", and it is now unreachable.

### Timestamps, not timers

- Every deadline this feature touches is a **wall-clock target plus a `visibilitychange` catch-up**,
  the convention `useIdleDisconnect.ts` already states ("Timestamps, NOT timers"). A `setTimeout` is
  kept only as the **fast path for a visible tab**; correctness never depends on it firing.
- `useGame.gradualSpeedReset()` sets `speedResetDeadline = Date.now() + delay` (2000ms when stepping
  down from 3, else 1500ms) and `evaluateSpeedReset()` applies it on becoming visible. A round that
  **ends while the tab is hidden** can no longer strand `typingSpeed` at 3 — the reported "a new round
  starts, it kinda goes back to normal but it's not".
- A second `gradualSpeedReset()` while one is pending **overwrites** the deadline rather than stacking
  (it fires from both `onGameReceived` and the `Ended` status branch, and both can run for one
  rollover). The step delay is read from a local, not from `typingSpeed` after mutating it.
- The listener is registered **once at module scope** in `useGame.ts` — the composable is a
  module-level singleton with no lifecycle hooks, and it deliberately does **not** hang off the idle
  watch, which follows the socket: the speed must still land after an idle disconnect.

### Torpedoes

- A torpedo is removed by its **`animationend` event**, with a wall-clock `BLAST_MS = 22000` deadline
  as the backstop. The old removal was a 15s `setTimeout` — **shorter than its own 22s `blast`
  animation**, and throttled to nothing while hidden, so orphans accumulated in `#torpedo-container`
  across every rollover watched from another tab.
- On becoming visible the component **sweeps** every tracked node past its deadline, plus — defensively
  — any `#torpedo-container` child it is not tracking.
- Concurrency is capped at `MAX_TORPEDOES = round(12 × BG_DENSITY)` (**5** today). Past the cap the
  **flourish is skipped, never the bet** — it still appears in the terminal and counts in the POT,
  because this component renders decoration only.
- Node ids are resolved with `getElementById`, not `querySelector('#' + id)`: the id embeds the
  player's choice and is not guaranteed to be a valid CSS selector.

### `BG_DENSITY` — one knob for how busy the sky is

- **`src/constants/background.ts` exports a single `BG_DENSITY` (0..1), currently `0.45`.** It scales
  the per-layer element cap, the spawn cadence **and** the torpedo cap together. A future user-facing
  slider must need to write **only this value** — the slider itself and any persistence are **out of
  scope** and not built.
- `maxElements = lightMode ? min(LIGHT_MAX_ELEMENTS, scaled) : scaled`, where
  `scaled = max(1, round(MAX_ELEMENTS × BG_DENSITY))`. Today that is **29 per layer** against the
  pre-#149 64, i.e. ~33 sprites on screen across both layers versus **up to 128** before.
- ⚠️ **Light mode is a `Math.min` FLOOR over the density value, never a competing knob.** At
  `BG_DENSITY = 1` light mode still gets 8; at `0.05` it gets 3. Lowering the constant can only ever
  make light mode **cheaper** — see [light-graphics-mode](light-graphics-mode.md).
- `MAX_ELEMENTS = 64` stays as the `BG_DENSITY = 1` reference point. **Do not lower it to reduce
  density** — that would silently shrink the future slider's range.
- `spawnDelay` **divides** by the density (`base / max(BG_DENSITY, 0.01)`), so a lower value means a
  *longer* delay: the cap and the cadence move the same direction under one slider. The
  `timeout / speed³` coupling is **kept** (fast typing must still read as a busier sky) but floored at
  `SPAWN_DELAY_FLOOR_MS = 150`, because the secondary layer used to reach **~37ms** at speed 3
  (`1000 / 27`). The floor is applied **after** the light-mode factor, so it can never make light mode
  faster.
- **Same density everywhere.** `BG_DENSITY` is a module constant read by the component, and both
  `GameBackground` layers live globally in `App.vue`, so `/space-stroker/background` and the practice
  screens inherit it automatically. **No per-route override exists, and none may be added** — the
  capture page exists precisely so the asset can never drift from the in-game look.

### Terminal & backdrop display

- Outside `InProgress` the terminal form is **`overflow-y-auto overflow-x-hidden`** — both axes named
  explicitly, because CSS computes a `visible` axis to `auto` as soon as the other is not `visible`
  (see the comment at `main.css:56-62`), so setting only `overflow-x` would silently make Y a scroll
  container anyway. `PracticeTerminal` carries the identical branch.
- The `PAID TO` lines carry **`break-all`**. `winnerLabel` falls back to the raw external player id —
  one long unbroken string — so `overflow-x-hidden` alone would have made the overflow *invisible
  instead of absent* and clipped the winner's identifier.
- The backdrop drift multiplier is **derived from the tile width the browser actually applies**
  (`tileWidthVw`: 70 above 1024px, 150 at or below, tracked with `matchMedia` and recomputed on
  resize/rotation) instead of a hard-coded `70vw`.
- **The root element paints `--color-void`** via `html:root, body`. DaisyUI's base layer ships
  `:root, [data-theme] { background-color: hsla(var(--b1) / …) }`, and `:root` (0,1,0) outranks the
  bare type selector `html` (0,0,1) — so the root was painting DaisyUI's `base-100` (`#2A303C`).

## Gotchas

- **The `@media (max-width: 768px) { background-size: 200% }` rule in `GameBackground.vue` is DEAD
  CODE, deliberately kept.** It is declared *before* the `max-width: 1024px { 150% }` block and both
  match below 768px at identical specificity, so **source order wins and 150% is what phones actually
  render**. `tileWidthVw` mirrors that reality. **Do not reorder, delete, or "fix" the 200% rule into
  effect** — that would change how zoomed the backdrop looks on phones.
- **Defect #6's tile seam is near-unreachable in practice, and was never the cause of the reported
  "many BGs".** `lightMode = isMobile || weakRenderer`, `isMobile` is `(max-width: 1023px)`, and light
  mode **freezes** `bgPosition` entirely — so below 1024px the backdrop never drifts and cannot show a
  seam. The `150%` `background-size` starts at `max-width: 1024px`, leaving exactly a **1px band
  (viewport = 1024px)** where the old hard-coded `70vw` was genuinely wrong. The fix is kept as code
  correctness and as insurance if light mode ever stops freezing the drift. The real cause of "many
  BGs" was the ~27× speed error above.
- **The root-background bug was a *canvas* problem, not a `<body>` sizing problem.** The root element's
  background paints the whole scrollable area; `<body>` paints only its own box, and body is
  viewport-height here because the `min-h-screen` wrapper inside it overflows visibly rather than
  stretching it. That produced a hard horizontal colour seam exactly one viewport down on **every page
  taller than the screen**, showing through the `opacity-95` starfield. **Do not "fix" a recurrence by
  giving `<body>` a `min-height`** — that papers over the symptom and leaves the root still wrong.
- `usePerfMode.ts` is **not** density-aware and must not become so: it publishes the `lightMode`
  verdict only, and the floor composition lives at the consumer (`GameBackground.vue`).
- `monkify-front` has **no test runner** (`.spec` / `.test` files do not exist), so the automated bar
  for changes in this area is `yarn lint` + `yarn build`. Lint needs **uniform CRLF** line endings.

## Key code touchpoints

`monkify-front`:

- `src/constants/background.ts` — **the** `BG_DENSITY` constant. New with this feature.
- `src/components/background/GameBackground.vue` — `handleVisibilityChange` (the resync),
  `restartSpawning` / `spawnElement` reading `document.hidden`, `maxElements` (density × light-mode
  floor), `spawnDelay` (density division + `SPAWN_DELAY_FLOOR_MS`), `tileWidthVw` + the `matchMedia`
  listener wired in `onMounted` / `onBeforeUnmount`, and the commented dead `<=768px` media query.
- `src/components/background/TorpedoSpawner.vue` — `BLAST_MS`, `MAX_TORPEDOES`, the `live`
  `Map<id, deadline>`, `removeNode` / `sweep`, the `animationend` listener and the `visibilitychange`
  sweep.
- `src/hooks/useGame.ts` — `SPEED_RESET_FROM_3_MS` / `SPEED_RESET_MS`, `speedResetDeadline`,
  `evaluateSpeedReset()`, the module-scope `visibilitychange` registration, and `gradualSpeedReset()`.
- `src/components/game/GameTerminal.vue` — the overflow branch on the terminal form and `break-all` on
  the `PAID TO` lines.
- `src/components/practice/PracticeTerminal.vue` — the same overflow branch.
- `src/main.css` — `html:root, body { background-color: var(--color-void) }` and its comment.
- `src/hooks/useIdleDisconnect.ts` — **unchanged**, but it is where the timestamps-not-timers rule is
  written down; read it before adding any new deadline.
