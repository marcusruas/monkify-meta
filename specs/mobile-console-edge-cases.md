# Space Stroker — "Fix mobile UI edge cases in game page" — Implementation Spec

> **Status: SHIPPED 2026-08-02** — branch `feature/mobile-console-edge-cases` merged to `main` in
> `monkify-front` + `monkify-meta`.
> Refined via Trello card
> [Fix mobile UI edge cases in game page (#137)](https://trello.com/c/PvfaU50d/137-fix-mobile-ui-edge-cases-in-game-page)
> (2026-08-02), read against `monkify-front@82829b6` / `monkify-meta@59f057b`.
> Repos affected: `monkify-front`, and this doc in `monkify-meta`. **No backend change.**
>
> **Purpose:** the mobile arcade cabinet shipped by `specs/mobile-game-design-update.md` was tuned for a
> portrait phone but is applied to **every** viewport below `lg` (1024px). Between ~450px and 1023px the
> two-plate composition stops working: it stretches a phone layout across a tablet-width screen. This
> card fixes the two symptoms the card owner reported, and nothing else.

---

## 1. Summary

Two independent defects, one component.

| | Today | After this card |
| --- | --- | --- |
| Console at **600–1023px** (and ≥600px tall) | the phone's **two-plate** cabinet stretched full width: keyboard/keypad crammed into the left half, a mostly-empty right plate, tiny lever | the **real PC console** — three plates (visor+keyboard · lever · preset keypad), the desktop art, minus the desktop room background |
| Lever on the compact cabinet | shrinks *relative to its plate* as the viewport widens — 46.8% at 360px down to **23.9%** at 600px | a constant **44.5%** wherever it fits, **35.3%** at the top of the band |
| Console breakpoint | one: `isMobile` (`< 1024px`) | two: `isMobile` (`< 1024px`, page layout) + `isWideConsole` (`≥600px` wide **and** tall) |

### 1.1 Measured before (`monkify-front@82829b6`, live browser)

The band is `GameConsole mobile`, `h-[clamp(140px,22dvh,195px)]`, two 50/50 plates. Lever ÷ plate width:

| 320px | 360px | 400px | 450px | 500px | 560px | 598px |
| --- | --- | --- | --- | --- | --- | --- |
| 49.2% | 46.8% | 43.4% | 36.7% | 31.6% | 26.6% | **23.9%** |

The lever image is **effectively a fixed ~70 × 111px** from ~450px up — it does not grow at all while
the plate it sits on keeps growing. That is the "goofy small" the card reports.

**Root cause.** The lever button is `absolute inset-0 … px-[12%] py-[11%]`. Per CSS, **percentage
padding resolves against the containing block's _inline_ size (width) on every side — `padding-top` and
`padding-bottom` included.** So as the plate widens, the *vertical* padding grows with it (21px at 400px,
32px at 600px) while the band's height stays capped by `22dvh`/`195px`. The lever art is 140×220
(aspect 0.636), so inside a plate far wider than it is tall it is always **height-bound** — and the
padding was taxing exactly the axis that binds. The lever therefore shrank in relative terms precisely
as the plate grew.

### 1.2 Card fidelity

The card gives three instructions; this spec implements all three and adds no fourth.

1. *"game page with 1000px now, we could simply adapt to show the real console from PC version, the full
   one minus the BG"* → §3.
2. *"so basically from 600px to 1000px width show this console"* → §2, **shipped as 600–1023px wide**.
3. *"from 600px and below it looks fine, just make sure the lever scales with the size too, it looks goofy
   small on things above 400px"* → §4.

---

## 2. The breakpoint

`useIsMobile` keeps its `(max-width: 1023px)` query for the **page layout** (the tabs, the three-band
column, the full-width chat panel). This card does **not** move that boundary — nothing about the column
changes. A **second** flag is added to the same composable:

```ts
const WIDE_CONSOLE_QUERY = '(min-width: 600px) and (min-height: 600px)';
```

`useIsMobile()` returns `{ isMobile, isWideConsole }`. `isWideConsole` picks the three-plate console;
everything else keeps the compact two-plate cabinet.

**D1 — the card's "600px to 1000px" ships as 600–1023px.** The card names 1000 because that is the width
the owner screenshotted; the real upper edge is the `lg` boundary where the desktop page layout already
takes over. Honouring 1000 literally would leave **1000–1023px on the broken two-plate console** — a 24px
dead zone that is a smaller version of the exact bug being fixed.

**D2 — the height half of the query is load-bearing, not defensive.** The three-plate console is a wide,
short shape, so it costs vertical room the compact cabinet does not: `40dvh` on top of the tabs (46) +
the terminal's `min-h` (180) + Edson's `min-h` (80), inside `100dvh - 54`. Solving
`0.4H + 306 ≤ H − 54` gives **H ≥ 600px** — which is where the threshold comes from, not a round number.
A landscape phone (740×380) passes the width test easily and fails that budget badly: measured, it
pushed the lever below the fold of a column the player then had to scroll, breaking the shipped
"the console never shrinks, the lever is always reachable" rule from
`specs/mobile-game-design-update.md`. Sideways phones keep the compact cabinet, which suits them anyway.

**D3 — one composable, not two.** Both queries describe "how big is the viewport" and are consumed by the
same page. A separate `useIsTablet` would leave two files to keep consistent.

**D4 — 600 is a hard cut, not a transition.** The three-plate console needs ~3× the horizontal room for
the keypad; there is no width at which a blended composition beats one of the two.

---

## 3. The 600–1023px console — the real PC console

`GamePage` renders `<game-console wide>` in this band, so the existing **desktop branch** paints:
`terminal.png` as the background with three children over it — `w-[30%]` visor + `keyboard.png`,
`flex-1` lever, `w-[37%]` preset keypad (`glass.png` frosting it on free-text games). No markup is
duplicated; the desktop composition *is* the deliverable. The `wide` prop only swaps the root's height
classes (`h-full` instead of the desktop's `h-[36%]`, a percentage of a parent that does not exist here).

**"Minus the BG"** = the console band only. On desktop the console sits inside `GameTerminal`'s room art;
here it sits on the game page's own background, exactly as the two-plate band does today.

### 3.1 The band is aspect-locked at 2.73

The desktop branch positions its three children **by percentage of its own content box** while painting
`terminal.png` (a 970×420 3-up sprite) with `bg-contain`. Alignment between the two is therefore a
function of the **box aspect alone** — and it is **scale-invariant**: the box, the `8%` padding, the
`30%`/`flex-1`/`37%` child widths and the contained image all scale linearly together, so an aspect that
lands the children on their wells at one width lands them at *every* width.

**2.73 is the aspect the live desktop console already runs at.** Measured at 1280×800: a 726.2 × 265.8
box, wells at x = 56.1 / 251.7 / 441.6, children at x = 58.1 / 241.1 / 442.4 — under 1px of error on the
keypad. Locking that aspect carries the desktop's own alignment into this band for free.

```
width:  min(100%, 40dvh × 2.73)      /* height is bounded through the WIDTH */
aspect-ratio: 273 / 100
margin-inline: auto
```

**D5 — do not "correct" this to the sprite's own 970/420 = 2.31.** That fits the art edge to edge and
feels like the right number, but it makes `bg-contain`'s letterbox vanish while the children keep their
percentages — the `8%` padding then insets them off wells 1 and 3. Built and measured: at 1000px the
keypad pulled visibly clear of its plate's right edge (~17px of drift).

**D6 — height is bounded through the width, never with `h-*`/`max-h-*`.** Either would override the
aspect lock and re-introduce the drift. `40dvh × 2.73` is the widest the band may get before its derived
height crowds the other bands (see D2 for where 40dvh comes from). `min(100%, …)` takes over on any
viewport too narrow to afford that, leaving the band full-width and shorter.

Measured result at 1000×800: an 876 × 321 band, per-plate drift ≤ 6px on plates 1 and 3 — the same order
as desktop's own error.

### 3.2 Band priority is unchanged

Console > terminal > Edson still holds. The console is `shrink-0`; the terminal keeps `min-h-[180px]`;
Edson gives ground first and the column scrolls if all three cannot fit. The `-mt-[clamp(…)]` overlap and
the upward shadow stay — they are band-level, not console-branch-level.

### 3.3 The visor reports focus from both branches

`setFocused()` now backs the desktop input's `@focus`/`@blur` too (it previously set a local ref
directly). Without it the native-keyboard lift (`betInputFocused` → `pb-[38dvh]`, Edson hidden) would
never fire in this band, since the band renders the desktop branch. Desktop is unaffected: the emit has
no listener there.

---

## 4. The lever (compact cabinet)

Two changes to the mobile branch's lever, together:

1. **The vertical inset stops being width-relative** — `px-[12%] py-[11%]` → `px-[8%] py-[6px]`. The plate
   stays fully tappable (padding is inside the hit area, preserving the
   `specs/mobile-game-design-update.md` rule), but the axis that binds the lever is no longer taxed by
   the plate's width.
2. **The art gets a width target of `w-[53%]`** of the button's content box — ~44.5% of the plate once
   `px-[8%]` is off — so it grows into the room step 1 frees, still capped by the plate's height through
   `object-contain`.

Measured lever ÷ plate width:

| | 320px | 360px | 400px | 450px | 500px | 560px | 598px |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Before | 49.2% | 46.8% | 43.4% | 36.7% | 31.6% | 26.6% | 23.9% |
| **After** | 44.5% | 44.5% | 44.5% | 44.5% | 42.4% | 37.8% | **35.3%** |

Below ~450px the target is reachable and the art is **width-bound**, so it holds a constant proportion;
from there up the plate's height runs out and it goes back to height-bound, which is why the curve tails
off to 35.3%. The lever is now a roughly constant fraction of its plate instead of drifting across a
25-point range — which is what *"make sure the lever scales with the size"* asks for.

**D7 — tune this number by measuring, not by eye.** A first pass used `w-[46%]`, which looked right in
isolation but bound at *every* width and so quietly **shrank** the lever on the small phones this fix is
meant to leave alone (46.8% → 38.6% at 360px). The `object-contain` letterbox also means
`getBoundingClientRect()` on the `<img>` returns the *element* box, not the painted art — measure
`min(width, height × 140/220)` or the numbers overstate the lever.

**D8 — the compact band's height is not touched.** Growing `h-[clamp(140px,22dvh,195px)]` with a `vw`
term would let the lever hold 44.5% all the way to 599px, but `dvh`-vs-`vw` interacts badly with landscape
phones and it perturbs the phone layout this fix exists to protect. 35.3% at the top of the range is a
~50% larger lever and reads correctly.

**Landscape footnote.** Short-and-wide viewports now keep the compact cabinet (D2), where the band is at
its `140px` floor while the plate is very wide, so the lever is height-bound at its physical ceiling:
22.4% at 740×380 (was 10.5%), 19.2% at 860×420. Improved, but capped by the band height — the only lever
that could grow there is a taller band, which is the space landscape does not have.

---

## 5. Out of scope

- **Desktop (≥1024px) — not touched.** No change to the desktop branch's own classes or to `GameTerminal`.
- The three-band column, the tab strip, the CRT housing, Edson, the overlap/shadow, the chat/stats panels.
- The lobby (`/game`), which already has its own responsive treatment.

---

## 6. Acceptance criteria

All verified in a live browser — see §7 for how the sub-600px widths were reached.

1. ✅ At **1000 × 800**, free-text game: three plates — visor + keyboard, lever, frosted keypad — matching
   the ≥1024px desktop console, with no room background behind it.
2. ✅ At **1000 × 800**, preset game: same three plates, with the **8 numbered keys** lit in the
   right-hand plate instead of crammed into the left one.
3. ✅ At **600 × 800** and **600 × 600**: the three-plate console renders and each control sits inside its
   own well of `terminal.png` (measured drift ≤ 6px on plates 1 and 3).
4. ✅ The cut is clean: **≥600×600 → three plates**, **598 × 800 → two plates**, with no broken
   intermediate state on either side.
5. ✅ At **598 × 800**: lever = **35.3%** of its plate (target ≥33%; before: 23.9%).
6. ✅ At **500 × 800**: lever = **42.4%** (target ≥38%; before: 31.6%).
7. ✅ At **≤400px**: the lever lands at a constant **44.5%**, inside the 43.4–49.2% range it already
   occupied across those widths, and the hit area is still the whole plate (≥44px). *Note: this is a
   flat proportion replacing a drifting one, so individual widths move by up to ~10% relative (49.2% →
   44.5% at 320px) — the original AC wording of "within ~5% at every width" was not achievable, and is
   corrected here rather than claimed.*
8. ✅ Interaction intact in both bands: a preset key selects (key 3 → `donkey` in the visor) and an
   invalid lever pull twitches rather than failing silently.
9. ✅ No page scroll introduced at 600–1023px on a normal viewport (checked 1023/1000/800/700/600 wide);
   short viewports still scroll rather than crushing the bands.
10. ✅ `yarn lint` clean; no new console **errors** on `/game/play` in either band (the pre-existing
    `GameBackground` missing-prop and `GamePage` fragment-class warnings are unchanged).

---

## 7. Test plan

**No unit or integration test.** This is a bug fix (`AGENTS.md` rule 8 scopes the mandatory backend
integration test to *new features*), and `monkify-front` has no test suite. Verification is a live browser
pass on **both** a free-text (Cosmic Sprint) and a preset (Beast Mode) game, with the lever ÷ plate ratio
**measured** rather than eyeballed.

**Widths below 600px cannot be reached by resizing a real Chrome window** — the OS enforces a ~600px
minimum, and a request for 599 silently lands at 600. They are verified by loading `/game/play` in a
**same-origin iframe** pinned to the target size, which gives the inner document a real viewport of that
size for `matchMedia` and `dvh`. Sizes covered: 1023/1000/800/700/600/598/560/500/450/400/360/320 wide,
plus 740×380, 860×420 and 1000×599 for the short-viewport path.

Sub-pixel caveat: inside the iframe harness the flip happens between 598 and 599 rather than 599 and 600,
because the harness's layout can hand the frame a fractional viewport width. In a real top-level window
`(min-width: 600px)` flips at exactly 600.

---

## 8. Key code touchpoints

- `monkify-front/src/hooks/useIsMobile.ts` — the `(min-width: 600px) and (min-height: 600px)` flag.
- `monkify-front/src/pages/GamePage.vue` — pick the branch, size the aspect-locked band.
- `monkify-front/src/components/game/GameConsole.vue` — the `wide` prop, the lever inset + width target,
  `setFocused` on the desktop input.
