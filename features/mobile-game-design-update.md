# Feature — Mobile game design update (arcade cabinet + Edson)

> Status: **shipped.** Authoritative spec: `../specs/mobile-game-design-update.md`
> (merged via `feature/mobile-game-design-update` on 2026-08-02).
> **Frontend-only** — `monkify-front`. No backend, API, SignalR or database surface.

## What it does

Below `lg` (1024px) the game screen is the arcade cabinet, not a bet form. `features/mobile-layout.md`
had deliberately hidden Edson and collapsed the three console modules into one compact gradient panel
with a text input and a `PLACE BET` button; phone players got something functional and generic instead
of the machine that is the product's identity. The GAME segment is now a three-band column — tabs,
CRT terminal, Edson, cabinet — built from the same art desktop uses, and the compact panel is deleted.

## Core rules

### Layout

- The mobile GAME view is a **three-band column**: `GameMobileTabs` → `GameTerminal` (`.mobile-terminal`)
  → `GameMonkey` → `GameConsole`, in that order, fitting a portrait phone with no page scroll.
- ⚠️ **Amended 2026-08-02 by `features/mobile-console-edge-cases.md`:** the two-plate cabinet described
  below is no longer used for the *whole* sub-`lg` range. It is now the **phone-shaped** cabinet only;
  viewports at least **600px wide and 600px tall** render the real desktop three-plate console instead
  (`isWideConsole`). The column, the bands and their priority are unchanged.
- **`GamePage` owns `GameConsole` on the mobile path.** `GameTerminal` renders the console only on
  desktop (`v-if="!mobile"`), because on mobile Edson sits *between* the terminal and the cabinet.
- Band priority when space is tight is **console > terminal > Edson**: the console never shrinks (the
  lever must always be reachable), the terminal holds `min-h-[180px]` and scrolls internally, Edson
  takes the remainder and gives ground first.
- A viewport too short for three bands (a phone held sideways, a short embedded frame) **scrolls**;
  the page root stays `overflow-hidden` so the tabs never scroll away. No separate short layout exists.
- The mobile header is **54px**, not 64 — `GamePage`'s column is `h-[calc(100dvh-54px)]` and must match
  it exactly or the cabinet is pushed off the bottom edge.
- While the visor input is focused the column takes `pb-[38dvh]` and Edson hides, so the visor and the
  lever clear the native keyboard. Chrome Android's default `interactive-widget=resizes-visual` leaves
  `100dvh` at full height when the keyboard opens, so hiding Edson alone would not lift the cabinet —
  the terminal would simply absorb the freed space.

### The console

- The console is `terminal.png` re-composed for portrait as **two equal-width plates**: visor +
  `keyboard.png` (or the 4×2 preset grid) on the left, the lever on the right.
- **The sprite is 970×420 holding THREE plates side by side.** Both modules show *the same* well
  (`x ∈ [0, 300)`) via `background-size: 323.333% 143.836%`, so the 10px seam and the neighbouring well
  can never leak in and both plates carry the same four rivets. A naive "one third per module" crop
  drags the light seam plus a sliver of well 2 into the left module.
- The vertical crop starts at **y=20** (`background-position-y: 15.625%`), past the plate art's own
  navy top border. A crop starting at y=12 leaves a hairline that reads as a seam between Edson and
  the cabinet.
- The lever is sized with **padding on the button, never by insetting the button**: padding sits inside
  the hit area, so the art shrinks while the whole plate stays tappable. This is the one control that
  must never be missed.
- The lever is **always pullable** — an invalid pull twitches and plays `error1.wav` through the
  existing `leverError()`; it is never dimmed or disabled. It doubles as "next round" at
  `RewardForWinnersCompleted`, and the terminal copy is the only label.
- Preset games render the 8 `button<N>.png` keys inside the plate, **numbers only**; the terminal's
  `> CHOICES:` list is the mapping. Choices 9+ are unreachable, matching desktop. Selection is
  **derived from `userWord`, never stored locally**, or a key stays lit after `placeBet()` clears it.
- The in-console `CONNECT WALLET` button is gone; the header `CONNECT` is the only connect affordance.
  **Exception:** the embedded popup-blocked "Connect wallet" retry stays on the plate — an embedded
  casino frame may hide or restyle our header, and it rescues an in-flight bet.

### The screen panel

- The tab strip, the terminal and the chat panel are **one piece of hardware**: true-black glass, a
  **7px `#5c6a8c` bezel**, **4px corners**. Keep those values in lockstep across all three, or
  switching tabs resizes the housing.
- The bezel tone is chosen for **contrast, not just tone**. Taking it down to the console's `#424c6e`
  navy made it vanish between black glass and the dark page, so a 6px bezel read *thinner* than the
  old 4px one. Thickness only reads when there is enough contrast to see it.
- **The selected tab is the lit screen**, not a brand fill: black glass plus `#4ade80` phosphor over a
  crisp 2px bar. The bar is the **non-colour selection cue** (WCAG 1.4.1), not decoration. Measured
  contrast: selected **12.05:1**, unselected **4.77:1**.
- The cabinet **overlaps Edson** (`-mt-[clamp(16px,3.6dvh,34px)]`, `z-20`) with a two-cast upward
  shadow. The tuck depth is measured, not guessed: `monkeyIdle.gif`'s hull is bright out to ~63-82% of
  frame width and then falls into a near-black underside, so a shallower tuck cuts the dark tail and
  reads as the saucer fading out rather than going behind something — and the shadow has no lit
  surface to land on. The clamp is required because a fixed 34px would swallow half of the 80px Edson
  band a short viewport leaves.
- The visor input's font size is **floored at 16px**. `public/index.html` ships the viewport as
  `width=device-width,initial-scale=1.0` with **no `maximum-scale`**, so iOS Safari zooms the whole
  page whenever a focused input computes under 16px — which would fight this screen's own focus
  handling and strand the player zoomed into a cabinet. Trim with leading and tracking instead.

### Motion

- **Every viewport below `lg` runs `html.light-graphics`** (`usePerfMode` forces it on phone-sized
  viewports). Anything mobile-only must therefore be built crisp: a wide soft bloom here is built to
  be stripped, i.e. dead weight on the one device that always sees it.
- The rise-in entry belongs to **Edson on mobile and the terminal on desktop**. Edson rises 40vh
  (`monkey-rise-in`, scoped to `GameMonkey`); the mobile terminal paints immediately with no
  `opacity-0` pre-state. Desktop keeps the terminal's `animate-rise-in` and Edson's horizontal
  `animate-fly-in`.
- `.animate-wobble` was **removed from the `html.light-graphics` kill list** in `arcade.css`. Every
  phone runs that mode, so stripping the wobble froze the mascot on the one screen he is the mascot
  of. It is also the cheapest animation in the app to keep: one `<img>`, transform-only, 8s loop.

## Deviations from the card and spec (the code is the newer decision)

Three owner decisions were taken after the card was written. Where the card's prose and the shipped
code disagree, **the code is authoritative**; the card and spec carry amendments.

- **AC #17 is superseded.** The tabs did *not* keep "today's filled-amber active state" — the amber
  chip fought the CRT once the glass went true black.
- **AC #18 and "Out of scope: Desktop — not touched, in any way" are amended.** Two changes reach
  desktop: the chat composer moved onto `--ctl-h-sm` (**overriding `specs/control-sizing-system.md`
  D5**, which had scoped the composer out), and the `.animate-wobble` un-strip also applies to a
  desktop running a software rasterizer. `GameHeader`'s change is `max-lg:`-scoped and is genuinely
  mobile-only.
- **The screen panel was re-skinned mid-flight** to true-black glass in a `#5c6a8c` housing with 4px
  corners; the spec was written against the older `#0b0d18` / `#92a1b9` / 8px treatment.

## Verification status — partial, on purpose

Checked in a live browser at 430×932 and 360×640: band order with no page scroll, console composition,
short-viewport scrolling, Edson shrinking first, the lever tap target (full plate, ≥44px), tab
contrast and heights, composer alignment, and the entry animation captured frame by frame.

**Not exercised** (they need a real round and a connected wallet): betting through the lever, the
preset keypad, the per-status Edson animations, native-keyboard focus behaviour, wallet connect and
the mobile deeplink path, near-miss rows during a live race, and round-rollover clearing. The card's
18 acceptance criteria are **left unchecked** rather than claimed.

## Key code touchpoints

- `monkify-front/src/pages/GamePage.vue` — the three-band mobile column, the console overlap + shadow,
  `betInputFocused` handling.
- `monkify-front/src/components/game/GameConsole.vue` — the `mobile` branch: two plates, sprite crop,
  visor input, keyboard / preset grid, lever padding.
- `monkify-front/src/components/game/GameMobileTabs.vue` — the tab strip, `game-tab--lit` /
  `game-tab--dim`, the unread dot.
- `monkify-front/src/components/game/GameTerminal.vue` — `.mobile-terminal` housing, the desktop-only
  console render, the desktop-only rise-in.
- `monkify-front/src/components/game/GameMonkey.vue` — mobile mount, `monkey-rise-in`, the
  post-first-paint GIF prefetch.
- `monkify-front/src/components/game/GameSidePanel.vue` — the mobile chat half of the screen panel.
- `monkify-front/src/components/game/GameChatPanel.vue` — the composer on `--ctl-h-sm`.
- `monkify-front/src/components/GameHeader.vue` — the `max-lg:` 54px header.
- `monkify-front/src/assets/styles/arcade.css` — the `.animate-wobble` light-graphics exception.
