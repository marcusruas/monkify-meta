# Feature — Light graphics mode (software-rendering performance)

> Status: **shipped.** Authoritative spec: `../specs/light-graphics-mode.md`
> (merged via `feature/light-graphics-mode` on 2026-07-22).
> **Frontend-only, app-only** — `monkify-front`. No backend, DB, SignalR or game-flow change; the
> landing page (`monkify-lp`) was explicitly out of scope.

## What it does

When a player's browser is rasterizing on the CPU — Chrome with **hardware acceleration off**, or any
software renderer — Space Stroker automatically drops into a **light graphics mode** that strips the
expensive parts of the arcade skin (blurs, blend modes, glows, infinite animations) so the game stays
playable instead of crawling. Phones always get light mode. The decision is entirely automatic:
**there is no setting, no toggle and nothing persisted**. Separately and permanently, the drifting
nebula layer was **deleted** for everyone — it was the single most expensive thing on the page.

## Core rules

- The visual mode is either **normal** or **light**. It is resolved on **every page load**; nothing is
  written to or read from `localStorage`, and no UI anywhere exposes it.
- Resolution order:
  1. **Viewport below `lg` (1024px)** — light mode, unconditionally, regardless of the GPU. Covers
     portrait and landscape phones (the same breakpoint the mobile layout uses).
  2. **WebGL unmasked renderer string** — `swiftshader`, `llvmpipe`, `software`,
     `softwarerasterizer`, `microsoft basic render` or `angle (software` means acceleration is off ⇒
     light mode. A recognized GPU string ⇒ normal mode, and no further probing.
  3. **~2 s `requestAnimationFrame` sample** (first 250 ms discarded) — only when the renderer string
     is unavailable, blocked or unrecognized. Sustained **< 25 FPS** ⇒ light mode.
  4. **Still inconclusive ⇒ normal mode.** Good hardware is never punished for a masked renderer.
- Detection **runs once per load** and never re-runs, so light mode improving the frame rate can never
  flip the verdict back. The mobile branch stays **reactive**: rotating a phone or resizing a desktop
  window across 1024px re-evaluates the mode live.
- The verdict is published as a **`light-graphics` class on `<html>`**; all visual downgrades are
  plain CSS keyed off `html.light-graphics …`.
- A weak-renderer verdict must never break boot: every WebGL call is wrapped, and a throwing or
  blocked implementation falls through to the frame sample.
- **In light mode:**
  - No `backdrop-filter` / `filter: blur()` anywhere — the header, lobby panels, game-type cards and
    toasts become opaque.
  - The space background **keeps drifting**, heavily reduced: **at most 8 elements** (trimmed
    immediately on entering the mode, not lazily), a **24 FPS** frame target, a **frozen**
    `background-position` scroll, and ~2.5× slower spawns. Per-bet **torpedoes are disabled**.
    ⚠️ **Since #149 that 8 is a ceiling, not a fixed number** — see the `BG_DENSITY` note below.
  - Infinite animations stop (`blink`, `bar-scan`, `pulse`, `crt-flicker`, the mascot's `wobble`
    wrapper), `mix-blend-mode` overlays go `normal`, multi-layer `box-shadow` / `text-shadow` glows
    collapse to a flat edge, and the lobby art's `drop-shadow` filters are dropped.
  - The **animated mascot image itself keeps animating** — only its wrapper's infinite wobble stops.
  - ⚠️ **The strip list is for INFINITE animations only, and there are two documented exceptions.**
    The mascot image above is one. The other is the **money change readout** (#151,
    [money-changing-animations](money-changing-animations.md)): `.money-value*` is deliberately **not**
    in the list. Its chip is a **one-shot** (900ms for a debit, 3.4s for a credit, most of which is a
    motionless hold) that fires only when the player's own money actually moved, and the number roll is
    a transform/text update on a single inline element. **Every viewport below 1024px is permanently in
    light-graphics**, so adding it would mean *most players never see the feature* — which is exactly
    why the owner ruled that phones get the full animation. Both exceptions carry their reasoning as a
    comment in `arcade.css`; **do not re-add either selector in a future perf pass** — cut the loop or
    change the `@keyframes` timing instead.
- **In normal mode the app looks exactly as it did before this feature**, apart from the removed
  nebula. The background's 30 FPS cap in normal mode is pre-existing behavior, unchanged.
  ⚠️ **Superseded in part by #149:** normal mode is no longer "unchanged" — the starfield's baseline
  cost was lowered for *everyone*, not just weak renderers. See the next bullet.
- ⚠️ **Since #149 ([background-lifecycle-hardening](background-lifecycle-hardening.md)), light mode is
  a FLOOR over a global density value, not a competing knob.** The element cap and spawn rate are now
  scaled by a single exported `BG_DENSITY` (0..1, currently `0.45`) in
  `src/constants/background.ts`, and light mode composes as
  `Math.min(LIGHT_MAX_ELEMENTS, round(MAX_ELEMENTS × BG_DENSITY))`. Consequences for anyone touching
  this area:
  - **Light mode can only ever get cheaper.** At `BG_DENSITY = 1` it still gets 8; at `0.05` it gets
    3. Nothing done to the density can make light mode heavier than it is today — that `Math.min` is
    the load-bearing part, and it must stay a `min`.
  - **Normal mode is now 29 elements per layer, not 64** (`round(64 × 0.45)`) — ~33 sprites on screen
    across the two layers instead of up to 128. `MAX_ELEMENTS = 64` remains only as the
    `BG_DENSITY = 1` reference point; **do not lower it** to reduce density, lower `BG_DENSITY`.
  - `spawnDelay` **divides** by the density and is floored at `SPAWN_DELAY_FLOOR_MS = 150` (the
    secondary layer used to reach ~37ms at speed 3). The floor is applied **after**
    `LIGHT_SPAWN_FACTOR`, so it can never make light mode spawn faster.
  - Torpedoes now also carry a concurrent cap (`round(12 × BG_DENSITY)`), on top of the `v-if`
    that already disables them entirely in light mode.
  - `usePerfMode.ts` is **not** density-aware and must not become so — it publishes the `lightMode`
    verdict only; the floor composition lives in `GameBackground.vue`.
- `.section-vignette` is kept in both modes: one static linear gradient with no blur, blend or
  animation, and with the nebula gone it is the only thing framing the screen edges.
- Mode changes are **presentational only**. Flipping mid-game never remounts the terminal, drops the
  SignalR connection, loses own-bet cues or restarts audio.
- The embedded (iframe) flow runs the same detection and **emits no new postMessage events** — the
  embed contract is unchanged.
- `prefers-reduced-motion` behavior is untouched and composes with light mode.

## Key code touchpoints

`monkify-front`:

- `src/hooks/usePerfMode.ts` — the whole verdict: mobile short-circuit, WebGL renderer probe
  (`WEBGL_debug_renderer_info` → `UNMASKED_RENDERER_WEBGL`, context released via
  `WEBGL_lose_context`), rAF frame sample. Module-scoped singleton exposing `lightMode` (a `computed`
  over `useIsMobile`) and `detectionSettled`.
- `src/hooks/useIsMobile.ts` — the `(max-width: 1023px)` media query reused for the mobile rule.
- `src/App.vue` — mirrors `lightMode` onto `document.documentElement` as `light-graphics`; gates
  `<torpedo-spawner>`.
- `src/components/background/GameBackground.vue` — `maxElements` / `frameInterval` / `spawnDelay`
  branch on the mode (`maxElements` and `spawnDelay` now compose the mode **with** `BG_DENSITY`, #149);
  the watcher trims live elements on entry; frozen `bgPosition` in light mode.
  Also the per-frame cleanups: rarity pool hoisted to a `computed`, `transform` precomputed on each
  element instead of a template helper.
- `src/assets/styles/arcade.css` — the single `html.light-graphics` override block (documented
  in-file). Lives here globally on purpose: several targeted classes are declared inside
  `<style scoped>` blocks, where a rule would land on the wrong element.
- `src/main.css` — toast blur/blend overrides.
- `src/components/GameHeader.vue` — carries the `app-header` hook class.
- `src/components/background/TorpedoSpawner.vue` — the `blast` keyframe animates `transform`, not
  `right`, so it no longer forces a layout pass per frame (in **both** modes).

**Removed:** `src/components/background/NebulaField.vue` — deleted, along with every reference. It
rendered 5 clouds, each a 40–82 vw radial gradient carrying **both** `filter: blur(24–54px)` and
`mix-blend-mode: screen`, re-styled on every animation frame.

## Gotchas

- **Scoped styles.** `GameTypes.vue`, `GameTypeCard.vue` and `GameMonkey.vue` use `<style scoped>`.
  A `html.light-graphics .x { }` rule written *inside* those components will not match. Add new
  light-mode overrides to the `arcade.css` block instead.
- The **LP's `.nebula-field` / `.nebula-field--tinted` CSS classes** (`monkify-lp/src/styles/global.css`)
  are unrelated static gradients — **not** the deleted Vue component. Do not remove them.
- The frame throttle applies **only** to `GameBackground`'s own rAF loop, not to the page, the
  terminal or input handling.
- **The frozen `bgPosition` has a non-obvious side effect on narrow viewports.** Because every
  viewport below 1024px is permanently light-graphics and light mode freezes the backdrop scroll, the
  backdrop **never drifts on phones or tablets** — so backdrop-drift bugs (tile seams, shift/tile-width
  mismatches) are invisible there. #149's breakpoint-aware shift fix is therefore mostly insurance;
  see [background-lifecycle-hardening](background-lifecycle-hardening.md) → Gotchas. Anything that
  un-freezes the drift below 1024px makes those paths live again.
- Feature worktrees are fresh checkouts and do **not** carry gitignored files: without copying
  `.env.local` from the hub, `VUE_APP_API_BASE_URL` is undefined and every API call 404s against the
  dev server's own origin.
