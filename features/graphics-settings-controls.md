# Feature — Graphics settings controls (the player's own light mode + sky density)

> Status: **shipped.** Authoritative spec: `../specs/graphics-settings-controls.md`
> (merged via `feature/graphics-settings-controls` on 2026-08-09).
> **Frontend-only** — `monkify-front`. **Zero backend surface**: no entity, endpoint, header,
> consumer, migration or SignalR change, and no `monkify-backend` branch ever existed for it.
> QOL, not a new feature, so `constraints.md` §3's mandatory-integration-test rule did not apply.

## What it does

The two knobs [light-graphics-mode](light-graphics-mode.md) (#83) and
[background-lifecycle-hardening](background-lifecycle-hardening.md) (#149) built but never exposed are
now real controls in the **SETTINGS** modal: a **light graphics mode checkbox** and a **sky density
slider** (`0–100%`). Both are saved per browser in `localStorage`, apply **instantly**, and involve no
backend, no wallet and no signature. A player on a real-but-weak GPU finally has an escape hatch, and a
player who wants the pre-#149 busy sky back can ask for it.

## Core rules

### The light-graphics checkbox

- **A manual choice wins permanently, in both directions.** Auto-detection only **seeds** the state on
  a browser that has never touched the control; once the player clicks, the stored value applies on
  every subsequent load and the detection verdict never overrides it. An explicit **OFF** survives a
  weak-renderer verdict — that is the point, not an oversight.
- The #83 detection itself is **unchanged**: same renderer strings, same ~2s FPS sample, same
  thresholds, still resolved once per load and never re-run.
- **Phones stay locked light.** Below `1024px` light mode remains unconditional and the checkbox is
  rendered **ticked and disabled** with the note *"Always on for small screens."* — deliberately
  **shown rather than hidden**, so a player who set it on desktop understands why it is not editable on
  their phone. The mobile lock is a term in the computed, never a write, so **the saved desktop choice
  survives a trip below the breakpoint and back**.
- Resolution order is now: `isMobile` → stored choice → `weakRenderer` → normal.

### The density slider

- **A percentage: `0–100%`, default `45%`, in 5% steps.** It is the density multiplier, not an element
  count, and it drives all three #149 consumers together — the per-layer element cap, the spawn cadence
  and the concurrent torpedo cap.
- **`0%` is genuinely off**, not "very quiet": no sprites, no spawn interval, no torpedoes, and the
  **rAF loop goes idle** rather than spinning with nothing to do (verified: zero frame callbacks). The
  static backdrop tile, `.section-vignette` and the `--color-void` root colour still render, so the
  page still looks like the game.
  ⚠️ **Idling the loop also freezes the backdrop drift**, because `animate()` is the only thing that
  advances `bgPosition`. That is intended — at `0%` the tile is meant to be static.
- **Lowering the slider trims live sprites on the spot.** The #83 watcher that did this on entering
  light mode now watches the density too; without it the cap would only apply to *new* spawns and the
  sky would stay busy until the old sprites drifted off.
- Crossing the `0%` boundary in either direction leaves **exactly one** rAF loop and **one** spawn
  interval. `startLoop()` is the only place a frame is requested from outside `animate()` itself, and
  it always cancels any in-flight `animationFrameId` first (#149's rule).
- Two degenerate-value guards carry a **zero carve-out**: `Math.max(1, …)` on the element cap (a
  *near*-zero density must still leave a starfield, but an exact zero means off) and
  `Math.max(density, 0.01)` in `spawnDelay` (unreachable at `0`, where no interval is armed at all).

### How the two compose

- ⚠️ **Light mode is still a `Math.min` FLOOR over the density, never a competing knob.**
  `maxElements = lightMode ? min(LIGHT_MAX_ELEMENTS, scaled) : scaled`. Nothing the slider does can
  make light mode heavier. That `min` is load-bearing and must stay a `min`.
- Consequence, surfaced in the UI: light mode's `8` binds while `round(64 × d) ≥ 8`, i.e. `d ≥ 0.1172`.
  **On the 5% step grid that means 15% and above are all flat at 8**, and only **10% (→ 6)** and
  **5% (→ 3)** actually move it. While light mode is on the modal shows *"Light mode caps the sky at 8
  elements."*
- **Both controls stay enabled in every state**, including on phones. Disabling the slider there would
  remove the only working graphics control from exactly the players who need it most.

### Persistence

- **`localStorage` only, zero backend.** A hardware/display setting belongs to the **device**, not the
  account: it has to work with **no wallet connected**, and the modal's `SAVE` button only renders when
  one is (it prompts a wallet signature for the display name). Keys are
  `space-stroker.graphics.lightMode` (`'true'` / `'false'`; absent = never touched) and
  `space-stroker.graphics.density` (stringified `0..1`).
- **Instant apply; `CANCEL` does not revert** — the same contract as the client seed's SET / ROTATE
  rows directly below in the same modal.
- Corrupt or out-of-range values **never break boot**: the density clamps to `0..1` or falls back to
  the default, and any light-mode value that is not exactly one of the two literals is treated as
  never-touched (⇒ auto-detection). `localStorage` being unavailable (private mode, blocked storage) is
  wrapped everywhere — the settings apply for the session and nothing throws.

### `/background` renders the canonical defaults

- The chromeless capture route **ignores both stored settings** and renders the default density and the
  un-overridden detection verdict, so the marketing asset can never drift with whoever records it.
  #149 forbade per-route density overrides for exactly this reason; a player-local slider would have
  reintroduced the drift by the back door.
- ⚠️ **The flag is seeded synchronously from `window.location.pathname`**, not from `route.meta`. Both
  `GameBackground` layers live globally in `App.vue`, so this cannot be a page prop — and `App.vue`
  mounts before the initial navigation has necessarily populated `route.meta`, so a watcher alone would
  paint the first frames at the stored density. `App.vue`'s watcher keeps it right for in-SPA
  navigation only. Matched with `endsWith('/background')` so adding a `publicPath` later cannot
  silently break it.

### Unchanged by this feature

- **What light mode strips** (#83's downgrade list) and the auto-detection heuristics.
- The **two documented light-mode exceptions** — the animated mascot image and `.money-value*` (#151).
  Do not re-add either to the strip list.
- `MAX_ELEMENTS = 64` (the `density = 1` reference point — **do not lower it**),
  `LIGHT_MAX_ELEMENTS = 8`, `LIGHT_TARGET_FPS`, `SPAWN_DELAY_FLOOR_MS`, the `speed³` spawn coupling,
  and the #149 tab-return resync contract.
- **No new embed `postMessage` event.** The settings gear is not hidden when embedded, so the group
  appears inside a regulated iframe too — acceptable, because it is presentational and device-local.

## Key code touchpoints

`monkify-front`:

- `src/hooks/useDisplaySettings.ts` — **new.** The settings singleton: storage IO, clamping, the
  canonical-route flag, the effective `density` / `lightModeChoice` computeds, and the setters.
  ⚠️ It must **never** import `usePerfMode` — that would cycle.
- `src/hooks/usePerfMode.ts` — `lightMode` composes the override; everything else is untouched. It
  stays **not density-aware** (the floor lives at the consumer).
- `src/constants/background.ts` — `BG_DENSITY` → **`BG_DENSITY_DEFAULT`**; the live value moved to the
  composable. This constant is now the default *and* the `/background` canonical value.
- `src/components/background/GameBackground.vue` — reactive density in `maxElements` / `spawnDelay`,
  the zero carve-out, `startLoop()` / `stopLoop()`, the trim watcher extended to the density, and the
  0% self-terminate inside `animate()`.
- `src/components/background/TorpedoSpawner.vue` — `maxTorpedoes` is a computed over the density.
- `src/App.vue` — the torpedo gate (`!lightMode && density > 0`) and the canonical-route watcher.
- `src/routes.ts` — `canonicalGraphics: true` on the `Background` route meta.
- `src/components/header/PlayerCustomizationModal.vue` — the `GRAPHICS` group.
- `src/assets/styles/arcade.css` — `.dialog-check` / `.dialog-check__box` (+ the disabled state), and
  `.dialog-check__box:focus-visible` added to the shared "ONE focus treatment" list.
- `src/components/ui/ArcadeInfoTip.vue` — **moved** from `orbit/OrbitInfoTip.vue`, with a
  `tone?: 'default' | 'orbit'` prop. All four Orbit call sites pass `tone="orbit"`, so nothing changed
  on any Orbit surface.
- `src/components/guide/sections/GuidePlayerPreferences.vue` — a fourth row for the graphics controls.

## Gotchas

- **The group must render OUTSIDE the wallet `v-if / v-else`.** With no wallet connected the `IDENTITY`
  block renders a **connect gate instead of the name field**, so "below your display name" is only
  literal when there is a name to be below. These settings have nothing to do with identity.
- **The info tip is a sibling of the checkbox label, never inside it** — a `<button>` nested in a
  `<label>` toggles the checkbox on every click.
- **The GRAPHICS group is ONE centre-aligned row, and that is a deliberate revision.** It first shipped
  with the slider stacked under its label, which made the group ~72px tall against the checkbox's ~25px
  and set the height of the whole modal section; the owner rejected it on sight. The label sits
  *before* the track in `.dialog-copy` (matching the checkbox label it is a peer of), and the
  percentage readout takes a **fixed width** so the track does not resize as the number goes 5% → 100%.
- **`lightMode` has far more consumers than the starfield** — `GuideLeverArt`, `GuideTerminalArt`,
  `GuideWalletArt`, plus `GameTypeCard`, `GameMobileTabs` and `OrbitTabBar` reason about it. A manual
  override therefore changes guide art and scroll behaviour too. That is correct; it is the same
  light-graphics mode. Verify `/how-to-play`, not only the game screen.
- **`.orbit-autopick` is page-scoped** inside `OrbitGameTab.vue`'s `<style scoped>` block, so it is
  unreachable from a modal — hence the separate `.dialog-check` pair rather than reuse.
- `monkify-front` has **no test runner**, so the bar for changes here is `yarn lint` + `yarn build` +
  a manual pass. Lint needs **uniform CRLF** line endings.
- **Four acceptance criteria shipped unverified** (spec §14): the mid-round SignalR/audio check, the
  tab-hidden-across-a-rollover resync, the embedded-iframe case, and the screen-reader half of the a11y
  criterion. Each exercises machinery this change does not touch, but none was driven end to end.
