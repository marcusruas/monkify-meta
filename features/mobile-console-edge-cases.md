# Feature — Mobile console edge cases (tablet console + lever scaling)

> Status: **shipped.** Authoritative spec: `../specs/mobile-console-edge-cases.md`
> (merged via `feature/mobile-console-edge-cases` on 2026-08-02).
> **Frontend-only** — `monkify-front`. No backend, API, SignalR or database surface.
> Amends `features/mobile-game-design-update.md`, which owns the compact cabinet itself.

## What it does

`features/mobile-game-design-update.md` built the phone arcade cabinet and applied it to **everything**
below `lg` (1024px). That cabinet is a *portrait phone* composition, so from roughly 450px up it degrades:
the keyboard (or the 8 preset keys) stays crammed in the left half, the right plate is mostly empty, and
the lever shrinks relative to the plate it sits on. This card splits the band in two and fixes the lever.

## Core rules

### Which cabinet renders

- `useIsMobile` now exposes **two** flags: `isMobile` (`max-width: 1023px`, the page layout — unchanged)
  and **`isWideConsole`** (`(min-width: 600px) and (min-height: 600px)`, which cabinet).
- **`isWideConsole` ⇒ the real desktop three-plate console** (visor + keyboard · lever · preset keypad),
  rendered by `GameConsole`'s existing desktop branch via a new `wide` prop. Nothing is duplicated —
  the desktop composition is the deliverable, minus the desktop room background.
- **Everything else ⇒ the compact two-plate cabinet**, unchanged.
- **The height half of the query is load-bearing.** The three-plate console needs `40dvh` on top of the
  tabs (46) + terminal `min-h` (180) + Edson `min-h` (80) inside `100dvh - 54`; that only balances from
  a **600px-tall** viewport up. A landscape phone (740×380) clears the width test and fails the budget,
  pushing the lever below the fold. Sideways phones keep the compact cabinet.

### The wide band's geometry

- The band is **aspect-locked at 2.73** — width is set, height derives from `aspect-[273/100]`.
- **Why 2.73 and not the sprite's own 970/420 = 2.31:** the desktop branch places its children by
  percentage of its content box while painting `terminal.png` with `bg-contain`, so alignment is a
  function of the **box aspect alone** — and it is scale-invariant. 2.73 is the aspect the live desktop
  console runs at (measured at 1280×800: a 726.2×265.8 box, wells at x=56.1/251.7/441.6 vs children at
  x=58.1/241.1/442.4). Locking it carries desktop's alignment into the band for free. Using 2.31 kills
  `bg-contain`'s letterbox while the children keep their `8%` padding, drifting them ~17px off wells 1
  and 3.
- **Height is bounded through the width** (`min(100%, 40dvh × 2.73)`), never with `h-*`/`max-h-*` —
  either overrides the aspect lock and brings the drift back.
- `setFocused()` now backs the **desktop** input's `@focus`/`@blur` as well, or the native-keyboard lift
  would never fire in this band. Desktop is unaffected (no listener there).

### The lever on the compact cabinet

- The vertical inset is **fixed** (`py-[6px]`), not a percentage. Percentage padding resolves against the
  containing block's **width on every side**, `padding-top`/`bottom` included — so the old `py-[11%]` grew
  with the plate while the band's height stayed capped, taxing the exact axis that binds a 140×220 lever
  inside a wide, short plate.
- The art carries `w-[53%]` (~44.5% of the plate after `px-[8%]`). Below ~450px that target is reachable
  and the lever is width-bound at a constant proportion; above it the plate's height binds again.
- **Measured, not eyeballed.** Lever ÷ plate: 23.9% → **35.3%** at 598px, 31.6% → **42.4%** at 500px,
  and a flat 44.5% at ≤450px. An earlier `w-[46%]` bound at every width and *shrank* the lever on small
  phones (46.8% → 38.6% at 360px) — the failure mode this number exists to avoid. Note `object-contain`
  letterboxes inside the element, so measure `min(width, height × 140/220)`, not the element box.

## Verification status

Verified in a live browser on both a free-text and a preset game at 1023/1000/800/700/600/598/560/500/
450/400/360/320 wide, plus 740×380, 860×420 and 1000×599. Interaction re-checked (preset key selects,
invalid lever pull twitches). `yarn lint` clean, no new console errors.

**Chrome cannot resize a real window below ~600px**, so sub-600 widths were verified through a
same-origin **iframe harness** — the inner document gets a real viewport for `matchMedia` and `dvh`.

## Key code touchpoints

- `monkify-front/src/hooks/useIsMobile.ts` — `isWideConsole`.
- `monkify-front/src/pages/GamePage.vue` — branch pick + the aspect-locked band.
- `monkify-front/src/components/game/GameConsole.vue` — the `wide` prop, the lever inset/width target,
  `setFocused` on the desktop input.
