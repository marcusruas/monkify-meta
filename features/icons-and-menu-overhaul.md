# Feature — Icons and Menu overhaul

> Status: **shipped.** Authoritative spec: `../specs/icons-and-menu-overhaul.md`
> (merged via `feature/icons-and-menu-overhaul` on 2026-08-09).
> **Frontend-only** — `monkify-front` + `monkify-meta`. **Zero backend surface**: no entity, enum,
> handler, consumer, EF mapping, migration, endpoint or SignalR contract, and no `monkify-backend`
> branch ever existed for it. `AGENTS.md` rule 8's mandatory integration test had no backend happy
> path to drive, and `monkify-front` has no test runner — the bar was `yarn lint` + `yarn build` +
> a browser pass at 0 console errors.

## What it does

The app had **no icon system**: nine feather-style **stroke** SVGs pasted inline in two files, a
literal VT323 letter `X` as every modal's close button, `✓ ✗ ▶ ▼` **text characters** standing in for
glyphs, and `spinner.svg`. The only real pixel art was Orbit's, whose own source said it was *"NOT
the seed of an app-wide icon system"*.

There is now **one pixel-art icon language** — a shared registry (`src/constants/icons.ts`) rendered
by one component (`ui/AppIcon.vue`) — every page and feature carries an **identity mark** the way
Orbit already did, and the ☰ header menu is an **icon pad** instead of a text list.

## Core rules

### The registry is the only place a mark lives

- **Every player-facing mark renders through `<AppIcon>`.** Nothing re-inlines an `<svg>`. Adding an
  icon is a **data change** in `constants/icons.ts`, not a system change.
- Every icon ships `shape-rendering="crispEdges"`, the drawing's own `viewBox`, `aria-hidden="true"`
  and `focusable="false"`.
- ⚠️ **Icons are decorative.** The accessible name always comes from the control — its `aria-label`
  or its visible label — never from the SVG.
- ⚠️ **`AppIcon` must not set `inheritAttrs: false`**, and must keep `class="flex-none"`. Call sites
  tint by passing `class="text-orbit"` (attribute fallthrough is how that reaches the root element),
  and several Orbit call sites are flex children that squash without `flex-none` — both carried over
  from the deleted `OrbitMark`/`OrbitGlyph`.

### Two grids, declared per icon

| Grid | Authoring key | One cell | Pixel-exact at | Used by |
| --- | --- | --- | --- | --- |
| **Coarse 8×8** (default) | `b` — eight 8-char rows | 2 units of a 16-unit box | **16 / 24 / 32 / 40px** | everything but the two below |
| **Fine 16×16** (opt-in) | `b16` — sixteen 16-char rows | 1 unit | **16 / 32 / 48px** | `practice`, `wallet` |
| **Raw rects** | `r` | — | as drawn | Orbit's six folded-in drawings |

- **Coarse by default.** Fine is opt-in and only when the coarse grid provably cannot hold the
  silhouette — `practice` (a console gamepad: wings, d-pad cross, four-button diamond and two centre
  dashes do not fit in eight cells across) and `wallet` (a lipped body, a card seam and a **solid 2×2
  clasp knockout** — the coarse cut was a blob with a bitten corner).
- ⚠️ **Opting into fine is not free — it costs the 24px rung.** A fine drawing at 24px gets 1.5
  device px per unit at 1× DPI and its finest details round unevenly. Check where a drawing is
  actually *placed* before choosing its grid; `practice` accepts exactly this in the header pad.
- ⚠️ **Raw-rect drawings are emitted VERBATIM, never rasterised.** That verbatim path is what
  guarantees Orbit's folded-in marks emit byte-identical SVG at their call sites.
- The rasteriser (`iconRects`) run-length-encodes each row into the fewest rects and caches per icon
  at **module level** — which is why it lives in `constants/icons.ts` beside the data, not inside
  `AppIcon.vue` (a `<script setup>` body re-runs for every instance).
- `iconGrid` is 16 for everything except `run` / `stop`, which keep their inherited **24**-unit box.

### Semantic fill is exactly two icons

- **Only `win-at` (green `--color-live`) and `falls-to` (warm red `oklch(0.8 0.16 25)`) carry a baked
  fill.** Everything else paints `currentColor` and takes the surface's tint for free — the amber
  wallet pill, the violet Orbit pill, a muted eyebrow, the FAIR panel's pass/fail colour.
- ⚠️ **Do not bake a colour into a third.** The design artifact's `tone:` fields were **preview-cell
  tinting, not fills**; promoting them would have turned Orbit's `run` glyph green.

### Orbit folded in — and the #124 rule is retired

- `OrbitMark.vue` and `OrbitGlyph.vue` are **deleted**; their rects moved into the registry
  **unchanged** and all ~20 call sites now render `<AppIcon>`.
- ⚠️ **This retires the `#124` D7 / AC16 rule** (*"NOT the seed of an app-wide icon system"*). Orbit's
  technique **is** the house technique now. The comment was **rewritten**, not silently contradicted.
- **Orbit's mark is not redrawn.** A heavier re-cut was mocked and rejected — zero visual change at
  its call sites is the requirement.
- ⚠️ **Orbit's call sites render at 11/13/14/18/20/26/28/30/52px — off the legal-size ladder — and
  they stay that way.** Zero visual change on a shipped surface outranks the ladder; the ladder
  governs **new** placements. Do not "fix" them.

### Money marks: the brand asset, not a pixel coin

- ⚠️ **Every money figure in the app is marked with `usdc.svg`** — the lobby's TICKET and POT chips
  already were, and Orbit's **PER ROUND** readouts (`OrbitStatusBanner`'s card, `/orbit`'s stat
  strip) joined them. Rendered through `<img>` at `image-rendering: auto`, 24px in the modal / 26px
  on the page.
- ⚠️ **`usdc.svg` is a brand mark and must never be folded into the pixel registry.** It stays a
  two-tone vector; pixel-snapping someone else's logo mangles the `$`. A *pixel* USDC is
  **un-buildable in this system**: `AppIcon` renders **one fill per drawing**, so "blue disc with a
  white `$`" needs a second colour layer.
- **The pixel `stake` and `pot` coins do not exist.** The card's stake-vs-pot split (one coin vs a
  three-coin stack) was superseded before shipping: with every money figure on the brand asset, both
  drawings had **zero consumers** and were deleted rather than kept as dangling rules. A standing
  note sits where they were — **do not re-add one without a consumer.**
- The old *"three coins seen edge-on: what ONE round costs"* comment is gone with the glyph; the
  Orbit files now record **why the stake mark changed twice and only the second change shipped**.
  ⚠️ Do not "restore" a pixel coin to Orbit's stake readouts.
- `src/assets/usdc.svg` itself was **replaced with the official Circle mark** (blue disc, broken
  white ring, white `$`). It is a **shared** asset, so this also changed the lobby's TICKET/POT chips
  at 14px — a shipped surface this card did not otherwise touch.

### The ☰ menu is an icon pad

- The trigger is unchanged (the ☰ button, now drawing the pixel `menu` glyph at 24px). It opens the
  same **anchored popover**, whose fill, border, radius, shadow and anchor are the utilities they
  have always been — `.menu-pad` carries **geometry only**.
- **250px wide, `repeat(3, minmax(0, 1fr))`, 24px icon above a centred VT323 15px uppercase label.**
  Cells: Home · Practice · Orbit · Stats · Fair · How to play · History · Responsible · Settings.
  **`/game` gets no cell** (a leftover page absorbed into Home).
- ⚠️ **The label font is VT323, and that is a measurement, not a preference.** Press Start 2P's
  advance is exactly `1.0em` against VT323's ~`0.4em`, so a PS2P label is ~2.5× as wide; at **8px** —
  already under the app's ramp — labels still overflowed and collided with their own icons. **No PS2P
  size fits this pad.** In VT323 every label fits on one line at one size, so the per-label shrink
  step was **deleted, not tuned**.
- ⚠️ **`minmax(0, 1fr)`, never `1fr`.** `1fr` is `minmax(auto, 1fr)`, so one unbreakable label wider
  than its share grows *its* column — the pad shipped once at **75 / 114 / 85px**, which is not a 3×3.
- **A 3×3 only when Orbit is enabled.** The Orbit cell is gated on `orbitEnabled`; with Orbit dark
  the pad renders **eight** cells in the same three columns.
- **Fair and Settings open modals, not routes, and look identical to the route cells** — the player
  is never told which is which. They carry no route, so they are **never** marked current.
- **Current page** = amber label + faint amber wash (`.is-current`) + `aria-current="page"`.
- **Dismissal is unchanged**: item click, outside click (`useClickOutside`), Esc. ⚠️ `menuRoot` must
  keep wrapping **both** the pad and the ☰ button, or the opening click closes it in the same tick.
- **No arrow-key roving focus.** Plain DOM order gives left-to-right, top-to-bottom tabbing for free;
  the Orbit tab bar's arrow keys are a `tablist` pattern and do not apply to a grid of links.
- **Audio is not a cell.** Below `md` the mute toggle + volume slider render as a **full-width strip
  above the grid**, outside the grid element, with a hairline under it — a cell cannot hold a slider.
  At `md+` audio stays on the header bar, unchanged.
- **Embedded renders the SAME grid.** With Orbit, Stats, Fair, How to play and Responsible Gaming
  hidden and Home/Practice redirected, the survivors (**History + Settings**) lay out as two cells in
  the same three columns — no alternate layout, no collapse-to-one-column branch. The embed is the
  least-tested path and must not own the only conditional layout.
- ⚠️ **`html.light-graphics .menu-pad { background: var(--color-void) }` is required, not
  belt-and-braces.** The blur-strip rule only removes `backdrop-filter`; the opacity rules beside it
  name `.app-header`, `.header-content`, `.retro-box` and `.card-content` — the pad is none of those,
  and every viewport below 1024px runs light-graphics.
- ⚠️ **`.menu-pad__cell` declares no base `color` on purpose.** `arcade.css` is imported after
  `@tailwind utilities` and these are single-class rules, so a `color` here would beat the
  `text-terminal-light` utility at the call site. Hover and `.is-current` are (0,2,0) and win without
  one.

### A mark replaces the generic marker

- **`ArcadeModal` sets `.dialog--has-lead` whenever the `titlebar-lead` slot is filled**, and
  `arcade.css` hides `.dialog__title::before` for it. This **generalises** the rule
  `.dialog--orbit` already applied: two markers state the identity twice, and the amber square is the
  louder. Orbit's own variant-scoped rule is left in place so it cannot regress.
- **`HomeSection` gains an optional `icon` prop that REPLACES the `+ TITLE +` markers**, same rule.
  All five front-door sections are marked: `how-to-play`, `fair`, `orbit`, `history` (YOUR RECORD),
  `stats` (THE FLOOR). ⚠️ The `+ TITLE +` fallback branch therefore has **zero consumers** — kept
  deliberately as the right treatment for a future section with no identity, recorded rather than
  left as drift.
- **Colour rule for marks: `currentColor`, which reads amber — Orbit is the named exception** and
  carries violet, because it owns a colour (`tone === 'orbit'`).
- Pages marked beside their `h1`: `/game/stats`, `/game/history` (24px), `/how-to-play`,
  `/responsible-gaming` (16px). Modals marked through `titlebar-lead`: `GameFairModal` (`fair`),
  `PlayerCustomizationModal` (`settings`).
- ⚠️ **Deliberately not marked:** `/` (the wordmark is the front door's identity; `home` stays
  menu-only wayfinding) and `/game/practice` (its only heading is the self-excluded refusal).
- THE FLOOR's eyebrow `.live-dot` was **removed** — a 7px cyan square bound to nothing, and the last
  generic marker on a now-marked card. ⚠️ `GameTypeCard`'s own `.live-dot` is unrelated and stays.

### The characters and the spinner

- **The modal close button is `<AppIcon name="close" :size="16" />`**, not the VT323 letter `X`, and
  `.dialog__close`'s now-dead `font-family` / `font-size` are gone. ⚠️ **16px, not 20/22** — neither
  box size is on the coarse ladder, and 16 is the largest legal size that fits.
- **`✓` / `✗` are gone from `GameFairPanel` and `GameHistoryPage`**, replaced by `check` / `close`.
  The colour comes from the existing `:class` on the wrapping span and reaches the icon through
  `currentColor` — no per-call-site fill. ⚠️ A string-interpolating ternary had to become **markup**
  for the mark to be an element.
- **`▶` / `▼` are gone from all four sites** (`OrbitModal` ×2, `OrbitPage` ×2) — but ⚠️ **only one of
  them is a real disclosure** (`OrbitPage`'s expand/collapse), and only that one **rotates 90°**. The
  others are a navigation button and two inline text links. This also fixes a documented bug: `▶`
  measures 10.3px against Press Start 2P's 12px cell, so the browser substituted a system font —
  a smooth anti-aliased arrow inside pixel type.
- **`LoaderSpinner` renders `<AppIcon name="loading" :size="160" />`**; `spinner.svg` is deleted.
  160 is a multiple of both 8 and 16, so it is exact on either grid.
- ⚠️ **The rotation is four 90° steps (`steps(4)`), never continuous** — a continuous spin puts pixel
  edges at arbitrary angles and destroys the crisp read `crispEdges` exists to give. It freezes under
  `prefers-reduced-motion`.
- ⚠️ **`.app-icon--spin` is deliberately NOT in the `html.light-graphics … animation: none` list.**
  Every viewport below 1024px runs that mode, so freezing the loader there would make the app look
  hung on every phone — the same reasoning as the documented `.animate-wobble` and money-readout
  exceptions.

### Kept out

- `usdc.svg` (a brand mark), game-type art (`utils/gameArt.ts`, `assets/types/*` — already bespoke
  pixel art), and the guide's illustration components (`GuideWalletArt`, `GuideBetTerminalArt`, …:
  illustrations, not icons).
- Game-screen in-play glyphs, the admin portal and wallet-bridge, and `monkify-lp`.

## Key code touchpoints

**`monkify-front`**

- Registry — `src/constants/icons.ts` (`IconName`, `IconDef`, `ICONS`, `iconRects`, `iconGrid`;
  **24 icons**: 9 identity + 9 chrome + 6 Orbit).
- Component — `src/components/ui/AppIcon.vue`.
- ⚠️ **Deleted** — `src/components/orbit/OrbitMark.vue`, `src/components/orbit/OrbitGlyph.vue`,
  `src/assets/spinner.svg`.
- Header + menu — `src/components/GameHeader.vue` (`menuCells`, `isCurrent`, the phone audio strip);
  `src/assets/styles/arcade.css` (`.menu-pad*`, `html.light-graphics .menu-pad`, `@keyframes
  app-icon-spin`, `.app-icon--spin`, `.dialog--has-lead`, the `:focus-visible` list).
- Chrome — `src/components/header/WalletButtons.vue`, `src/components/ui/ArcadeModal.vue`,
  `src/components/LoaderSpinner.vue`.
- Orbit — `OrbitStatStrip.vue`, `OrbitStatusBanner.vue`, `OrbitBetLock.vue`, `OrbitControlBar.vue`,
  `OrbitFirstWinModal.vue`, `OrbitLauncher.vue`, `OrbitModal.vue`, `src/pages/OrbitPage.vue`.
- Front door — `src/components/home/HomeSection.vue` (the `icon` prop), `HomeOrbitCard.vue`,
  `HomeFeatureCards.vue`, `HomeHowItWorks.vue`, `HomeFloor.vue`, `HomePlayerStats.vue`.
- Marked pages/modals — `src/pages/GameStatsPage.vue`, `src/pages/GameHistoryPage.vue`,
  `src/pages/HowToPlayPage.vue`, `src/pages/ResponsibleGamingPage.vue`,
  `src/components/game/GameFairModal.vue`, `src/components/header/PlayerCustomizationModal.vue`.
- Characters — `src/components/game/GameFairPanel.vue`, `src/pages/GameHistoryPage.vue`.
- Brand asset — `src/assets/usdc.svg` (replaced with the official Circle mark).

**`monkify-backend`** — none. AC17's zero-diff assertion holds: no branch was ever created.

## Known drift and follow-ups

- ⚠️ **Two stale comments in `GameHeader.vue`** survive the second cut: the `.menu-pad` comment still
  says **270px** (it is 250px), and the menu's own header comment still describes the pre-#159 list
  (*"Settings (customization modal) + History (+ dev Admin)"*). Harmless at runtime, wrong to read.
- The FAIR panel's `check` mark inherits `text-primary` (**amber**), not the "semantic green" AC9
  worded — the colour is the surface's pre-existing class and was deliberately not changed.
- **`RESPONSIBLE` is the one label flagged as a copy decision** (shorten the word, keep the smaller
  step, or widen the pad) — carried over from the PS2P cut; VT323 fits it, so this is cosmetic now.
- **Not verified by eye at ship time** (needs a real wallet / a finished round): signed-in Orbit's
  stat strip and status banner, and the FAIR panel's check/close markers.
