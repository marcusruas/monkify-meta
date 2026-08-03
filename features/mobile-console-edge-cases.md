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

- **The band crops the sprite's dark base (rows 300–419)** — `wideSpriteCrop` shows `y ∈ [20, 300)`,
  plate body plus its light strip. Desktop can afford the base because its console box is a slice of a
  tall column, so it reads as the cabinet the plates are mounted on; here the box **is** the console, so
  it was ~26% of the band spent on empty grey. `background-size: 84.6% 150%`,
  `background-position: 50% 14.29%` (`Y = T/(S−(B−T))`, the two-plate branch's own formula).
- **The aspect follows from the crop — they are one decision.** Undistorted scaling requires
  `0.846·W / 1.5·H = 970/420` ⇒ **`W/H = 4.0949`** (`aspect-[4095/1000]`). Change one, change the other,
  or the art stretches.
- **The `84.6%` / `50%` X geometry is exactly what `bg-contain` produced at the previous 2.73 aspect**
  (measured at 1280×800: a 726.2×265.8 box, wells at x=56.1/251.7/441.6 vs children at
  x=58.1/241.1/442.4). Alignment is a function of the **box aspect alone** and is scale-invariant, so
  keeping that geometry carried desktop's alignment in for free and let the crop move **nothing**
  horizontally. Do not simplify it to a full-bleed `100%`: the `8%` padding would then inset the
  children ~17px off wells 1 and 3.
- **Height is bounded through the width** (`min(100%, 27dvh × 4.0949)`), never with `h-*`/`max-h-*` —
  either overrides the aspect lock and brings the drift back.
- ⚠️ **`min-h-0` + `overflow-hidden` on the band are required.** A flex item's automatic minimum size
  (`min-height: auto`) lets content override a declared height **including one from `aspect-ratio`** —
  the box measured 228.9px against the 216.6px asked for, and since the crop pins the art's height to
  150% of the *box*, that 6% went straight into stretching the sprite vertically.
- `setFocused()` now backs the **desktop** input's `@focus`/`@blur` as well, or the native-keyboard lift
  would never fire in this band. Desktop is unaffected (no listener there).

### Edson and the wide lever

- **Edson runs `flex-[6_6_0%] min-h-[120px]` wherever the three-plate console renders** (the phone keeps
  `3` / `80px`). He is height-driven in his band (`max-lg:h-full … object-contain`), so his size **is**
  that ratio: 273px at 1000×800, against ~142px before this card.
- **The wide lever is `h-[73%] translate-y-[8%]`.** Desktop's `h-2/3` was calibrated against a box that
  still held the dark base; once cropped it read small and high — 68% of the band against the keypad's
  76.9%. It now spans 9.7% → 79.7% at 42.9% of its plate, clear of the plate body's 92.5% edge.
- The drop is a **`translate`** (a % of the image's own height), not wrapper padding — a percentage
  there resolves against the `flex-1` wrapper's **width** and drifts with the band.

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
invalid lever pull twitches). `yarn lint` clean, no new console errors. **Desktop re-measured after the
crop and unchanged** — `bg-contain`, `50% 50%`, box 726.2×265.8, aspect 2.732.

**Chrome cannot resize a real window below ~600px**, so sub-600 widths were verified through a
same-origin **iframe harness** — the inner document gets a real viewport for `matchMedia` and `dvh`.

## Owner review (2026-08-03, `feature/mobile-console-crop`)

The first pass reused the desktop composition wholesale. That was right for **alignment** and wrong for
**everything else about the box**: the dark base came along with it, ate ~26% of the band, squeezed
Edson, and left desktop's `h-2/3` lever calibrated against a box that no longer existed. "The desktop
branch does it" is not evidence that a part belongs in a band whose box *is* the console. Three owner
notes — crop the base, enlarge Edson, drop and enlarge the lever — are all the same root cause.

## Key code touchpoints

- `monkify-front/src/hooks/useIsMobile.ts` — `isWideConsole`.
- `monkify-front/src/pages/GamePage.vue` — branch pick + the aspect-locked band.
- `monkify-front/src/components/game/GameConsole.vue` — the `wide` prop, the lever inset/width target,
  `setFocused` on the desktop input.
