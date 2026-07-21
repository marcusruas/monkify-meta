# Space Stroker — "APP (front) design refinement" — Implementation Spec

> **Status: Shipped (2026-07-21)** — merged via `feature/app-design-refinement`
> (`monkify-front` `1a426af` → `main`; `monkify-meta` KB → `main`). Distilled into
> `../features/app-design-refinement.md`. A matching `SPACE STROKER` wordmark tweak shipped separately
> to `monkify-lp` `master` (`ba7c203`). Originally refined via Trello card
> [APP (front) design refinement #79](https://trello.com/c/R2x70aWY) (2026-07-21).
> Repos affected: **`monkify-front`** (the only app repo touched) and this doc in `monkify-meta`.
> **`monkify-backend` is NOT touched** — frontend-only, app-only.
>
> **Purpose:** Close the visual gap between the freshly-redesigned landing page (`monkify-lp`,
> arcade-cabinet + nebula/parallax) and the older-looking game app (`monkify-front`, "Space Stroker")
> by **porting the LP's design system into the app** and raising the surrounding chrome (tokens,
> fonts, buttons, header, background, lobby, shared UI) to LP quality — **without restructuring the
> in-play game screen**. Audience: players who bounce from the LP into the app and currently feel a
> jarring quality drop.

---

## 1. Summary

The LP (`monkify-lp`, Astro 5 + Vue 3 + **Tailwind v4** `@theme` OKLCH) ships one coherent arcade
design system: a single OKLCH token set (orange brand + cyan/magenta accents + a 5-step deep-space
"void" ramp), a shared material layer (`.arcade-panel`, `.btn-arcade`, `.chip`, `.crt-screen`,
`.pixel-notch`, glows), and exactly **2 fonts** (Press Start 2P + VT323). The app (`monkify-front`,
**Tailwind v3** + `colors.css` + `tailwind.config.js` + DaisyUI) looks older because of **token
chaos** (three drifting blues + a stale README that contradicts `colors.css`), **button/typography
sprawl** (flat off-palette buttons referencing an undefined `.title`; **4 fonts** loaded, one
unused), and the **absence of a shared material system** (effects copy-pasted per component).

This card **faithfully ports the LP's design system into the app** — one source of truth **inside
`monkify-front`**, values translated/mirrored into the app's v3 + `colors.css` + DaisyUI setup. **No
shared npm package, no cross-repo build coupling.** The app's genuinely-good centerpieces (the
reactive-speed animated space background, the clip-path `GameTypeCard`) are **re-tokenized, not
replaced**.

**Frontend-only, `monkify-front` only, off `main`.** No `monkify-lp` / backend / DB / test work.

### Scope shape (ambition = C, chrome overhaul; in-play game screen NOT restructured)

| Tier | What | Prune-able? |
| --- | --- | --- |
| **Non-negotiable core** | Unified token system (collapse the drifting blues) · 2-font system (Press Start 2P + VT323) · `.btn-arcade` buttons · LP-quality **always-visible** header | **No** — this ships |
| **Chrome overhaul** | Background re-tokenize + LP richness · lobby to LP material quality · shared-surface re-skin (toasts, loaders, TokenBalance, wallet button, customization modal) | Yes — individual items may be cut during in-app QA if judged unnecessary once seen running |
| **Inherited only** | In-play game screen (`GameTerminal`/`GameMonkey`/`GameSidePanel` + green-phosphor CRT) — **layout unchanged**, inherits tokens/fonts/buttons so it doesn't clash | Layout frozen; only the token/font/button inheritance applies |

> **Flexibility note (from the user, carried from the card):** the surface list below is a **target,
> not a rigid contract.** During implementation/QA, items — especially the "add richness" extras and
> parts of the shared-surface list — **may be cut** if judged unnecessary once seen in the running
> app. The token/font/button/header consistency **core** is non-negotiable; the rest is prune-able by
> in-app judgment. The implementer should exercise that judgment and note anything dropped.

### LP (source) → app (target), at a glance

| Layer | LP (source of truth) | App today | After |
| --- | --- | --- | --- |
| **Tokens** | OKLCH `@theme` in `src/styles/global.css` (one orange + cyan/magenta + void ramp + ink ramp + radii/easing) | `colors.css` hex + 3 drifting blues + a stale README | LP tokens translated into `colors.css` + `tailwind.config.js`; one blue value everywhere; README fixed/removed; radii + easing tokens added |
| **Material** | `.arcade-panel(--accent)`, `.btn-arcade(--primary/--cyan/--sm)`, `.chip`, `.crt-screen`, `.pixel-notch`, `.scanlines`, glows, `.step-num`, `.pixel-rule` | effects copy-pasted per component | the same classes re-implemented in the app (a shared `@layer components` block) |
| **Fonts** | 2 (Press Start 2P + VT323) via one Google Fonts link | 4 loaded (Press Start 2P, VT323, **Silkscreen**, **Pixelify Sans**=unused) + Courier New in markup | 2 only; Pixelify Sans, Silkscreen, and Courier New removed/migrated |
| **Buttons** | `.btn-arcade` (chunky 3D press, hover brightness) | `PrimaryButton`/`SecondaryButton` flat block-shadow, `bg-blue-500`, undefined `.title` | ported `.btn-arcade` variants |
| **Header** | frosted `bg-void/80` blur, coin glow, `SPACE`·`STROKER` split logotype | plain gradient bar, `edson.png` + text, no treatment | LP-quality frosted bar + coin glow + logotype; **always visible (no auto-hide)** |

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there). Frontend-only subset:

- **Worktree-first (per repo).** App work lives in `.worktrees/app-design-refinement/monkify-front`
  off the front hub's **`main`**; the spec lives in
  `.worktrees/app-design-refinement/monkify-meta` off `main`. The shared checkouts (hubs) stay on
  `main` — never switch a hub's branch. **Push after branch work** (AGENTS.md rule 5).
- **No backend unit/integration test required** — this is a **frontend-only, non-game-flow** change,
  not a backend feature (constraints §3's integration-test mandate is scoped to backend features).
- **No frontend unit tests** — the app has no test suite. Definition of done = `yarn lint` +
  `yarn build` clean + browser QA against the live LP (§13).
- **Secrets/config = mock-only.** N/A for this card (no secrets touched), but keep the rule.
- **Operator messaging** — vague errors only; **do not** touch operator error copy. (Relevant here
  only because the operator-themable `--color-secondary` hook is in scope to *preserve*, §4.4.)
- **`prefers-reduced-motion`** — every new glow/animation must freeze/reduce under reduced motion,
  matching the LP's discipline (`monkify-lp/src/styles/global.css` reduced-motion block is the
  pattern). See §6.7.
- **CRLF/prettier lint trap** (repo memory) — `monkify-front` `yarn lint` requires uniform CRLF
  (`core.autocrlf=true`); editing CSS/Vue with a LF-normalizing tool triggers `Insert ␍` failures.
  Keep CRLF; verify with `yarn lint` before declaring done.

---

## 3. Resolved product decisions

| # | Decision | Source |
| --- | --- | --- |
| 1 | **Ambition = C (full chrome overhaul)**; the **in-play game screen is NOT restructured** — it inherits the shared token/font/button restyle only (not frozen pixel-for-pixel, not restructured). | Card / Q2=A |
| 2 | **Port, don't couple.** Faithfully port the LP's system into the app — **one source of truth inside `monkify-front`**. No shared npm package, no cross-repo build dependency (LP is TW v4 `@theme` OKLCH; app is TW v3 + `colors.css` + `tailwind.config.js` + DaisyUI). Values are **translated/mirrored** into the app's setup. | Card / Q3=A |
| 3 | **Keep the app's strengths, re-tokenize them.** The reactive-speed animated space **background** and the **clip-path `GameTypeCard`** stay; they get the new tokens (+ LP richness for the bg), never a replacement. | Card |
| 4 | **Background:** keep the reactive-speed bg **and its gameplay tie-in** (`speedMultiplier = typingSpeed`); recolor overlays/scanlines to the new tokens; add LP richness (nebula tint / dot-grid / seam vignette). **Do NOT** replace it with the LP's per-section `ContentSection` system. | Card |
| 5 | **Header:** LP-quality restyle (frosted `bg-void/80` backdrop-blur, coin glow, `SPACE`·`STROKER` logotype, arcade-styled icon/wallet buttons) but **always visible — no auto-hide** (it holds live controls: wallet, mute, settings, admin). The app header already has **no** scroll-hide — **do not port the LP header's `showHeader` scroll logic**. | Card + finding §7.4 |
| 6 | **Lobby:** level `GameTypes` / `GameTypeCard` up to LP material quality (arcade-panel bevel, new tokens, consistent glows/hovers) while keeping the **clip-path pixel-card identity**; fix the Courier New "GAME SELECTION" header → Press Start 2P. | Card |
| 7 | **Fonts collapse to 2** — Press Start 2P (headings/labels) + VT323 (body/terminal), matching the LP. Drop **Pixelify Sans** (unused) **and** migrate **Silkscreen** usages (the card named only Pixelify Sans + Courier New, but "only 2 fonts" requires Silkscreen to go too — see §7.1). Replace all Courier New usage (lobby headers, toast). | Card + finding §7.1 |
| 8 | **Preserve operator theming.** The new token system must not hardcode over the operator-themable `--color-secondary` variable or its Tailwind `secondary` mapping / `useOperator.applyOperatorTheme` runtime hook. | Card + finding §7.5 |
| 9 | **Game screen must not clash.** It inherits tokens/fonts/buttons but keeps its layout + green-phosphor CRT terminal; verify no regression to the "away"-state escalation or terminal readability. | Card |
| 10 | **Mobile & embedded structure preserved.** `GameMobileTabs`, the `GAME | CHAT·STATS` segmented view (portrait + landscape, standalone & embedded), and the fill-the-frame embedded look are **re-skin only — no layout change**. | Card |
| 11 | **Flexibility.** The surface list is a target; prune-able items may be cut in QA provided the non-negotiable core (unified tokens, 2-font system, `.btn-arcade` buttons, LP-quality header) still ships. | Card / user |

---

## 4. Design system port (the core — `monkify-front`)

This is the heart of the card. Everything downstream (buttons, header, lobby, shared surfaces)
consumes these. The LP values below are extracted verbatim from
`monkify-lp/src/styles/global.css` (`@theme`, lines 10–59) and its `@layer components`.

> **Where things live in the app.** `src/main.css` already `@import`s
> `./assets/styles/colors.css` (line 5). Keep `colors.css` as the **token source of truth** (translate
> the LP OKLCH tokens in), extend `tailwind.config.js` (`fontFamily` + `colors`), and add the LP's
> material classes in a **new `@layer components` block** — recommended file
> `src/assets/styles/arcade.css`, `@import`ed from `main.css` right after `colors.css`. The
> `color-mix(in oklab, …)` / OKLCH usage in the material classes is **pure CSS** and ports verbatim;
> only the token-variable wiring changes (v4 `@theme` → `:root` vars + `tailwind.config` mapping).

### 4.1 Tokens — translate LP OKLCH into `colors.css` (+ `tailwind.config.js`)

Add the LP token set to `colors.css` `:root` (keep the OKLCH values — modern browsers + the app's
Chrome target support OKLCH/`color-mix`; the LP already relies on both). Suggested variable names
mirror the LP so the ported material classes work unchanged:

| Token (app var) | LP value | Role |
| --- | --- | --- |
| `--color-primary` | `oklch(0.795 0.166 67)` (≈`#ffa214`) | brand orange (already the app's primary) |
| `--color-primary-bright` | `oklch(0.88 0.155 82)` | hot highlight |
| `--color-primary-deep` | `oklch(0.585 0.135 52)` | amber 3D edge / shadow (button key-drop) |
| `--color-cyan` | `oklch(0.83 0.125 205)` | CRT cyan accent |
| `--color-cyan-deep` | `oklch(0.52 0.09 215)` | cyan 3D edge |
| `--color-magenta` | `oklch(0.66 0.23 350)` | **live/bet pulse only** |
| `--color-void` | `oklch(0.115 0.018 280)` | darkest pit |
| `--color-bg` | `oklch(0.158 0.022 280)` | page background |
| `--color-surface` | `oklch(0.215 0.026 280)` | cabinet panel |
| `--color-surface-2` | `oklch(0.275 0.030 282)` | raised panel |
| `--color-line` | `oklch(0.40 0.035 282)` | hairline / pixel edge |
| `--color-ink` | `oklch(0.975 0.012 85)` | primary text |
| `--color-ink-dim` | `oklch(0.855 0.02 245)` | secondary text |
| `--color-ink-mute` | `oklch(0.715 0.022 258)` | muted labels |
| `--radius-arcade` | `4px` | panel/button radius |
| `--radius-chip` | `3px` | chip radius |
| `--ease-out-expo` | `cubic-bezier(0.16, 1, 0.3, 1)` | transitions |
| `--ease-arcade` | `cubic-bezier(0.3, 1.3, 0.6, 1)` | arcade press overshoot |

Two recurring **ink-on-color** literals the LP uses (keep as literals, not tokens): **`#1a1205`**
(text on orange) and **`#04181c`** (text on cyan).

**`tailwind.config.js` (`theme.extend`):**
- `fontFamily`: add `display: ['"Press Start 2P"', 'system-ui', 'sans-serif']` and
  `body: ['"VT323"', 'ui-monospace', 'monospace']` (v3 will not auto-generate these — the app
  currently has **no** `fontFamily` extension; fonts are applied via `.press-start-2p-regular` /
  `.vt323-regular` utility classes). Wire the new `font-display` / `font-body` utilities in.
- `colors`: extend with the new tokens as arbitrary-value-friendly entries (e.g.
  `void: 'var(--color-void)'`, `surface: 'var(--color-surface)'`, `surface2: 'var(--color-surface-2)'`,
  `line: 'var(--color-line)'`, `cyan: 'var(--color-cyan)'`, `magenta: 'var(--color-magenta)'`,
  `ink: { DEFAULT, dim, mute }`) so `bg-void`, `border-line`, `text-ink-dim`, etc. resolve. **Keep
  `secondary.DEFAULT: 'var(--color-secondary)'`** untouched (operator hook, §4.4). DaisyUI stays
  loaded; no DaisyUI theme block is required (the app has none today) — the ported material classes
  do the visual work.

### 4.2 Collapse the drifting blues (finding §7.2 — the token-chaos fix)

Today: the token `--color-blue-vivid` is **`#3668d4`**, but every glow/scanline is **hardcoded**
`rgba(91, 127, 204, …)` = **`#5b7fcc`**, and the stale README documents `#5b7fcc` as the token.
There is also a stray cyan `rgba(0, 183, 255, …)` = **`#00b7ff`** in a couple of glows. **Collapse to
one system:**

- Route the **cyan accents** (scanlines, retro-bar glows, card glows) to the ported **`--color-cyan`**.
- Retire the ad-hoc blue ramp (`--color-blue-*`) in favor of the **void ramp + primary/cyan** — or
  keep a single blue alias if a component genuinely needs it, but **one value, referenced by token**,
  never a raw `rgba(91,127,204)` / `#00b7ff` literal.
- **Every** occurrence must move to a token. Exact hit list (from code exploration):

| File:line | Current | → |
| --- | --- | --- |
| `src/assets/styles/colors.css:11` | `--color-blue-vivid: #3668d4` | reconcile to the single blue/cyan token |
| `src/assets/styles/colors.css:29` | `--color-bg-button: rgba(91,127,204,0.15)` | token-derived |
| `src/assets/styles/colors.css:36` | `--color-shadow-blue: rgba(91,127,204,0.5)` | token-derived |
| `src/assets/styles/colors.css:40` | `--color-scanline: rgba(91,127,204,0.05)` | token-derived (cyan) |
| `src/components/game-selection/GameTypeCard.vue:194,195` | scanlines `rgba(91,127,204,0.05)` | `var(--color-scanline)` / cyan token |
| `src/components/game-selection/GameTypeCard.vue:204` | drop-shadow `rgba(91,127,204,0.5)` | shadow token |
| `src/components/game-selection/GameTypeCard.vue:234` | `.press-start` bg `rgba(91,127,204,0.15)` | button-bg token |
| `src/components/game/GameSidePanel.vue:210` | scanline `rgba(91,127,204,0.05)` | scanline token |
| `src/components/game-selection/GameTypes.vue:137-149` (retro-bar) & `src/main.css:94-95,126` (toast) | `rgba(0,183,255,…)` cyan glow | `--color-cyan`-derived |
| `src/assets/styles/README.md:23-24,31-32` | documents `#5b7fcc` / wrong hexes | rewrite to match the new tokens, or delete (§7.2) |

### 4.3 Material classes — port the LP `@layer components`

Re-implement these in the app (recommended `src/assets/styles/arcade.css`, `@layer components`),
copying the LP rule bodies verbatim (they only reference the tokens above):

- **`.arcade-panel`** + **`.arcade-panel--accent`** — beveled 4-layer-shadow surface (gradient fill,
  `2px solid var(--color-line)`, inset highlight + inset dark + hard drop + soft ambient).
- **`.btn-arcade`** base + **`--primary`** (orange, `#1a1205` text, `0 5px 0 var(--color-primary-deep)`
  key-drop) + **`--cyan`** (cyan, `#04181c` text) + **`--sm`**. Mechanic: `:hover{filter:brightness(1.08)}`,
  `:active{transform:translateY(3px)}` and the box-shadow collapses `0 5px 0` → `0 2px 0` (physical
  keypress), driven by `--ease-arcade`.
- **`.chip`** + **`.chip--cyan`** — small `font-display` pill.
- **`.crt-screen`** — vignette + inner cyan rim (`inset 0 0 60px -10px …, inset 0 0 0 2px cyan/18%`).
- **`.scanlines::after`** — CRT scanline overlay (repeating-linear-gradient).
- **`.pixel-notch`** — clipped arcade corners (the 8-point `clip-path`).
- Glows: **`.glow-primary` / `.glow-cyan` / `.text-glow-primary` / `.text-glow-cyan`**.
- Support: **`.step-num`**, **`.pixel-rule`** (if a divider is needed).

> The app's clip-path `GameTypeCard` already achieves a `pixel-notch`-like identity via its own
> polygon — **keep that identity**; align its bevel/shadows/tokens to `.arcade-panel`, don't swap the
> clip-path out (Decision 3, AC6).

### 4.4 Preserve the operator-theming hook (finding §7.5 — hard requirement)

`--color-secondary` (`colors.css:8`, default `#181b31`) is **operator-themable at runtime**:
`src/hooks/useOperator.ts:28-31` `applyOperatorTheme(color)` does
`document.documentElement.style.setProperty('--color-secondary', color)` (from `loadOperator()`,
`:54`), and `tailwind.config.js:18` maps `secondary.DEFAULT: 'var(--color-secondary)'`. **The port
must keep this variable name and the Tailwind `secondary` mapping intact** — any `bg-secondary` /
`border-secondary` consumer must still re-theme live under operator branding. Do **not** fold
`--color-secondary` into the void ramp or hardcode over it.

### 4.5 Fonts — collapse to 2 (finding §7.1)

- **`public/index.html:9-11`** — the Google Fonts link loads **four** families (Pixelify Sans, Press
  Start 2P, Silkscreen, VT323). Reduce to **`Press+Start+2P` + `VT323`** only.
- **`src/main.css`** — keep `.press-start-2p-regular` + `.vt323-regular`; **remove**
  `.silkscreen-regular` / `.silkscreen-bold` (lines 43–53) and any Pixelify usage.
- **Migrate Silkscreen usages** to Press Start 2P (headings/labels) or VT323 (body/terminal) in the
  game panels that use it: `GameStatsPanel.vue`, `GameSidePanel.vue`, `GameMobileTabs.vue`,
  `GameChatPanel.vue`, `GameConsole.vue`, `EmbedRelaunching.vue`. (These are game-screen surfaces —
  this is exactly the "inherit the shared font restyle" of Decision 9; layout untouched.)
- **Replace Courier New:** `src/main.css:90` (`.space-stroker-toast` `font-family: 'Courier New'`) →
  VT323; `GameTypes.vue:73` and `:86` (`font-['Courier_New',monospace]` on the GAME SELECTION +
  LOADING headers) → Press Start 2P.
- **Root default:** `public/index.html:17` `<div id="app" class="press-start-2p-regular">` — keep as
  the app-wide display default; VT323 is applied per body/terminal surface as today.

---

## 5. Data model / API / backend / config

**None.** No entity, DTO, EF mapping, migration, controller, consumer, SignalR channel, `.env`, or
`GeneralSettings` change. This is a pure frontend re-skin of `monkify-front`. The data-model / API /
backend / config sections of the spec template are **N/A** for this card.

---

## 6. Frontend behaviour — surface-by-surface (`monkify-front`)

Line numbers below are against the `feature/app-design-refinement` worktree tip (front hub
`202bb14`). All of §6 consumes the §4 tokens/material classes.

### 6.1 Buttons — `src/components/ui/PrimaryButton.vue`, `SecondaryButton.vue` (AC3)

- **PrimaryButton** (`:22` `bg-primary title text-white … shadow-amber-700 shadow-[0px_5px_2px]`) →
  the ported **`.btn-arcade .btn-arcade--primary`** (chunky 3D press, hover brightness). Keep the
  `asLink`/`href`/`small` props (`small` → `.btn-arcade--sm`).
- **SecondaryButton** (`:22` `bg-blue-500 … shadow-blue-800 hover:bg-blue-600`) → **`.btn-arcade
  .btn-arcade--cyan`** — removes the off-palette `bg-blue-500`/`shadow-blue-800`/`hover:bg-blue-600`.
- **Undefined `.title` class** (used in `PrimaryButton.vue:22`, `SecondaryButton.vue:22`, and
  `PlayerCustomizationModal.vue:145`): no `.title { }` exists anywhere (§7.3). Either **define**
  `.title` as a `font-display` label utility (the LP uses a `.title` = headings/`font-display`
  convention) **or remove** the class from all three call sites. AC3 requires no button to reference
  an undefined `.title`.

### 6.2 Header — `src/components/GameHeader.vue` (AC5; note: file is at `components/`, not `header/`)

- **Bar** (`:93-95` `bg-gradient-to-r from-terminal-darker … to-blue-800/10 max-h-[64px] h-[64px]`) →
  a **frosted LP-style bar**: `bg-void/80` + `backdrop-blur` + `border-b border-line/60`, keep the
  fixed 64px height. Mounted by `App.vue:123` with `class="relative z-40"` (hidden only on the
  `WalletBridge` route) — keep that.
- **Wordmark** (`:98-101` `edson.png` `h-12 md:h-16` + `SPACE<span text-primary>STROKER`) → add the LP
  **coin-glow** drop-shadow on the icon (`drop-shadow-[0_0_8px_color-mix(in_oklab,var(--color-primary)_55%,transparent)]`,
  `pixelated`) and treat the wordmark as the LP logotype (`font-display`, orange `STROKER` split; keep
  `edson.png` as the coin/mascot icon). Keep it inside the home link.
- **Icon/wallet buttons** — restyle the mute button + volume slider, the wallet cluster
  (`TokenBalance` + `WalletButtons`, shown only for SolanaToken games, `:161-170`), the settings gear,
  and the dev `DevAdminButton` to the new tokens + material (arcade chip/`.btn-arcade--sm` treatment).
  The scoped `.swv-button` height override (`:205-211`) stays.
- **Always visible — no auto-hide.** The app header has **no** scroll listener today (finding §7.4);
  keep it persistent. **Do not** import the LP header's `showHeader`/`scrollY>100` translate logic
  (AC5: "stays visible at all times").

### 6.3 Background — `src/components/background/GameBackground.vue` (+ `App.vue`) (AC7)

- **Keep the reactive-speed behavior + gameplay tie-in.** `speedMultiplier` (bound to `typingSpeed`
  from `useGame` in `App.vue:104-120`; two stacked instances `#secondary-bg` + `#main-bg`, plus
  `TorpedoSpawner`) and the easing constants (`SPEED_BOOST_FACTOR=3`, 1s transition, 30fps,
  `MAX_ELEMENTS=64`) are **unchanged**. (Decision 4.)
- **Re-tokenize the overlays, not the PNG.** The base image (`spacebg2.png`) stays; recolor the
  **scanline / element color-filter / glow** layers to the new tokens (the app's element tinting comes
  from `@/constants/color-filters` + `@/constants/space-elements`). The `html/body` fallback
  `background-color` (`main.css:65`) → `--color-void`/`--color-bg`.
- **Add LP richness (prune-able):** a subtle **nebula tint**, **dot-grid**, and **seam vignette**
  overlay — port the LP's `.nebula-field(--tinted)` / `.dot-grid` / `.section-vignette` techniques
  (`monkify-lp/src/styles/global.css`) as **atmospheric overlays behind app content**, low-opacity,
  `pointer-events:none`, no horizontal overflow. **Do NOT** adopt the LP's per-section
  `ContentSection` system (Decision 4). These extras are the most prune-able items in QA.

### 6.4 Lobby — `GameSelectPage.vue` → `GameTypes.vue` → `GameTypeCard.vue` (AC6)

- **`GameTypes.vue`:** the **"GAME SELECTION"** header (`:73` `font-['Courier_New',monospace]
  text-[28px] … pixel-title-shadow`) and the **LOADING** header (`:86`) → **Press Start 2P**
  (`font-display`). Re-token `.header-content` / `.retro-box` borders (currently
  `border-[var(--color-blue-vivid)]`), `.pixel-title-shadow` (currently
  `2px 2px 0 var(--color-blue-vivid)`), and the `.retro-bar` scan animation (currently
  `rgba(0,183,255,…)` cyan + blue-vivid border) to the ported tokens. Keep the responsive grid
  (`grid-cols-1 md:grid-cols-2 xl:grid-cols-3`).
- **`GameTypeCard.vue`:** **keep the clip-path pixel-card identity** (`.pixel-border-outer` /
  `.pixel-border-inner` / `.card-content`). Level it to LP material quality: align the bevel/gradient
  to `.arcade-panel`, route every hardcoded blue/cyan/`#ffa214`/`#000000` literal (lines 194, 195, 204,
  222–231, 234, 167, 208, 223, …) to tokens, keep the orange hover glow + `crt-flicker` + `.blink`
  `PRESS TO START` + `.lottery-badge` (re-tokenized). This is the app's showcase card — polish, don't
  rebuild.

### 6.5 Shared surfaces (AC9 — re-skin only, prune-able)

| Surface | File | Change |
| --- | --- | --- |
| **Toast** | `src/main.css:84-139` (+ `main.ts:65-74` wiring) | `.space-stroker-toast`: `font-family` Courier New → VT323; border `--color-blue-vivid` → token; cyan `rgba(0,183,255,…)` glow → `--color-cyan`; keep the error/`--error` red variant + `::after` scanline. Class name **`.space-stroker-toast` stays** (wired in `main.ts` as `toastClassName`). |
| **Loaders** | `LoaderSpinner.vue`, `LoadingDots.vue` | Re-token the overlay (`bg-gray-900/75` → void-based) + spinner tint; `LoadingDots` inherits font. |
| **TokenBalance** | `src/components/TokenBalance.vue` | Minimal — inherits header tokens/font; coin `@/assets/coin.png` + `LoadingDots` kept. |
| **Wallet button** | `src/components/header/WalletButtons.vue` | The exposed `PrimaryButton` ("My Wallet"/"Connect Wallet") inherits `.btn-arcade`; keep the hidden `WalletMultiButton` wiring + `.swv-*` overrides. |
| **Customization / settings modal** | `src/components/header/PlayerCustomizationModal.vue` | Panel (`:142-144` `rounded-md border border-primary/40 bg-terminal-darker … shadow-amber-900/40`) → `.arcade-panel` treatment + new tokens; inputs/selects (`:166,176,208`) re-tokened; resolve the `.title` h2 (`:145`, §6.1); keep all controls (display-name, currency, provably-fair client-seed). |

### 6.6 Game-screen inheritance — no restructure (AC8, Decision 9/10)

The in-play game screen (`GamePage` + `GameTerminal` / `GameMonkey` / `GameSidePanel` / the
green-phosphor CRT) **keeps its layout**. It **inherits**:
- the new tokens (route `GameSidePanel.vue:210`'s hardcoded blue + any panel literals to tokens),
- the 2-font system (the Silkscreen→PS2P/VT323 migration of §4.5 touches these panels),
- `.btn-arcade` buttons.

**Do not** restructure `GameConsole` / `GameTerminal` / `GameMonkey` / `GameSidePanel` /
`GameStatsPanel` / `GameChatPanel` / `GameMobileTabs`, and **do not** alter the green-phosphor CRT
"away"-state escalation or terminal readability. **Mobile** (`GameMobileTabs`, `GAME | CHAT·STATS`
segmented view, portrait/landscape, standalone & embedded) and the **embedded fill-the-frame** look
are **re-skin only** — verify no layout regression (AC9, Decision 10).

### 6.7 `prefers-reduced-motion` (AC10)

Every new glow/animation (button press transitions, header coin glow if animated, nebula/dot-grid/
vignette drift, any lobby hover motion) must **freeze/reduce** under
`@media (prefers-reduced-motion: reduce)`, mirroring the LP's global block
(`monkify-lp/src/styles/global.css` ~632-665: `animation-duration: 0.001ms !important`, drift
`animation: none`, revealed content forced visible). Add the guard alongside the ported material
classes.

---

## 7. Code-exploration findings (hidden blockers — resolve in-implementation)

1. **A 3rd + 4th font are live, not just Pixelify Sans.** `public/index.html:9-11` loads **4**
   families. Pixelify Sans is unused (drop cleanly). **Silkscreen** (`.silkscreen-regular/-bold`,
   `main.css:43-53`) **is used** in `GameStatsPanel`/`GameSidePanel`/`GameMobileTabs`/`GameChatPanel`/
   `GameConsole`/`EmbedRelaunching`. The card named only Pixelify Sans + Courier New, but the AC
   ("only Press Start 2P + VT323") **requires migrating Silkscreen too** — folded into §4.5.
2. **The "three drifting blues" precisely.** Token `--color-blue-vivid` = **`#3668d4`**
   (`colors.css:11`, the only real use). The README documents **`#5b7fcc`** (`README.md:23`) — which
   exists **only** in the doc. Every actual glow/scanline is hardcoded **`rgba(91,127,204,…)` = `#5b7fcc`**
   (`colors.css:29,36,40`; `GameTypeCard.vue:194,195,204,234`; `GameSidePanel.vue:210`). A **4th** stray
   cyan **`rgba(0,183,255,…)` = `#00b7ff`** lives in `GameTypes.vue:137-149` + `main.css:94-95,126`.
   Collapse all per §4.2.
3. **`.title` is an undefined class** used at `PrimaryButton.vue:22`, `SecondaryButton.vue:22`,
   `PlayerCustomizationModal.vue:145` — no `.title { }` rule exists. Define or remove (§6.1). (Other
   `title` grep hits are HTML `title=` attributes, not the class.)
4. **The app header has NO auto-hide today** — no scroll listener; `App.vue` renders it persistently.
   The AC wants it always visible, so this is already satisfied — the risk is the implementer
   **accidentally porting** the LP header's `showHeader`/`scrollY>100` logic. Explicitly don't (§6.2).
5. **Operator theming is a live runtime hook.** `--color-secondary` is set per-operator via
   `useOperator.applyOperatorTheme` and mapped by Tailwind `secondary`. Preserve the variable +
   mapping (§4.4) — a naive "one void ramp for everything" would break operator branding.
6. **The background is image-based**, not token-painted. "Recolor to new tokens" applies to the
   scanline/filter/glow overlays + the html/body fallback, **not** repainting `spacebg2.png` (§6.3).
7. **Tailwind version divide is real.** LP = TW v4 `@theme`; app = TW v3 + `colors.css` +
   `tailwind.config.js` + DaisyUI (loaded, no theme block). Port tokens into `colors.css` + extend
   `tailwind.config` (`fontFamily` is currently **absent** — must be added), and port material classes
   into an app-side `@layer components`. `color-mix`/OKLCH port verbatim (§4).

---

## 8. Out of scope

- **In-play game screen layout** — `GamePage` structure, the `GameTerminal`/`GameMonkey`/
  `GameSidePanel` layout, and the green-phosphor CRT terminal keep their design (inherit shared
  restyle only).
- **The LP** (`monkify-lp`) — reference/source only, **no changes**.
- **Backend / gameplay logic / SignalR / wallet flows / DB / config** — none.
- **Mobile layout restructure** — re-skin only; `GameMobileTabs` + segmented view structure preserved.
- **New/replacement art assets** — reuse existing (`edson.png`, `coin.png`, card art, `spacebg2.png`,
  space elements). No new sprites.
- **DaisyUI theme overhaul** — the ported material classes carry the look; no DaisyUI theme block is
  required (none exists today).

---

## 9. File checklist

### 9.1 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/assets/styles/colors.css` | Translate the LP OKLCH token set in (primary(-bright/-deep), cyan(-deep), magenta, void ramp, surface(-2), line, ink(-dim/-mute), radii, easings); collapse the drifting blues; **keep `--color-secondary`**. |
| 2 | `src/assets/styles/README.md` | Rewrite to match the new tokens **or delete** (currently stale, documents `#5b7fcc`/wrong hexes, omits `--color-secondary`). |
| 3 | `src/assets/styles/arcade.css` *(new)* | Ported LP `@layer components`: `.arcade-panel(--accent)`, `.btn-arcade(--primary/--cyan/--sm)`, `.chip(--cyan)`, `.crt-screen`, `.scanlines`, `.pixel-notch`, `.glow-*`/`.text-glow-*`, `.step-num`, `.pixel-rule`, + reduced-motion guards. |
| 4 | `src/main.css` | `@import` `arcade.css`; drop `.silkscreen-*`; toast `font-family` Courier New → VT323 + cyan glow → token; html/body fallback bg → void. |
| 5 | `tailwind.config.js` | Add `fontFamily` (`display`/`body`); extend `colors` with the new tokens; keep `secondary: var(--color-secondary)` + DaisyUI. |
| 6 | `public/index.html` | Reduce the Google Fonts link to **Press Start 2P + VT323** only (drop Pixelify Sans, Silkscreen). |
| 7 | `src/components/ui/PrimaryButton.vue` | → `.btn-arcade--primary`; resolve `.title`. |
| 8 | `src/components/ui/SecondaryButton.vue` | → `.btn-arcade--cyan`; remove `bg-blue-500`/`shadow-blue-800`; resolve `.title`. |
| 9 | `src/components/GameHeader.vue` | Frosted `bg-void/80` blur bar + `border-line/60`; coin-glow wordmark; arcade-styled icon/wallet buttons; **stay always-visible (no auto-hide)**. |
| 10 | `src/components/background/GameBackground.vue` | Re-token scanline/filter/glow overlays; keep reactive-speed + gameplay tie-in; optional nebula/dot-grid/vignette richness (reduced-motion-safe, no overflow). |
| 11 | `src/App.vue` | Only if the bg-stack wiring / fallback bg needs the new tokens; keep the two-instance + `TorpedoSpawner` structure. |
| 12 | `src/components/game-selection/GameTypes.vue` | GAME SELECTION + LOADING headers → Press Start 2P; re-token `.header-content`/`.retro-box`/`.pixel-title-shadow`/`.retro-bar` (drop `rgba(0,183,255)`/blue-vivid literals). |
| 13 | `src/components/game-selection/GameTypeCard.vue` | Keep clip-path identity; align bevel to `.arcade-panel`; route all hardcoded blue/cyan/`#ffa214`/`#000` literals to tokens; keep hover glow / `crt-flicker` / `.blink` / lottery badge. |
| 14 | `src/main.ts` | Only if the toast class wiring needs adjustment (class name `.space-stroker-toast` stays). |
| 15 | `src/components/LoaderSpinner.vue`, `LoadingDots.vue` | Re-token overlay + spinner tint. |
| 16 | `src/components/TokenBalance.vue` | Inherit header tokens/font (minimal). |
| 17 | `src/components/header/WalletButtons.vue` | `PrimaryButton` inherits `.btn-arcade`; keep `.swv-*` overrides. |
| 18 | `src/components/header/PlayerCustomizationModal.vue` | Panel → `.arcade-panel` + tokens; re-token inputs/selects; resolve `.title` h2. |
| 19 | `src/components/header/DevAdminButton.vue` | Re-token to the new icon/button style (dev-only). |
| 20 | `src/components/game/GameSidePanel.vue` (+ `GameConsole`, `GameStatsPanel`, `GameChatPanel`, `GameMobileTabs`, `GameTerminal`, `EmbedRelaunching`) | **Inherit only:** route `GameSidePanel.vue:210` blue literal + panel literals to tokens; migrate Silkscreen → PS2P/VT323. **No layout change**; preserve green-phosphor CRT + mobile segmented view. |

*(Items 10, 15–20 shared-surface polish are prune-able per the flexibility note; the token/font/button/header core — items 1–9, 12–13 — is not.)*

### 9.2 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/app-design-refinement.md` | This spec (this run). |
| 2 | `INDEX.md` | KB-map row for this spec (status **Ready**) + "Feature currently in flight" entry (this run). |

---

## 10. Tests

- **Backend unit/integration tests: N/A** — no backend change (constraints §3's integration mandate
  is scoped to backend features; this is a frontend re-skin, not a game-flow change).
- **Frontend tests: N/A** — `monkify-front` has no test suite.
- **Definition of done = lint + build + browser QA (§11):** `yarn lint` (watch the CRLF/prettier
  trap — repo memory) and `yarn build` both green; the app QA'd in the browser **side-by-side with the
  live LP**; all new glows/animations freeze under `prefers-reduced-motion`; no console errors; mobile
  + embedded structure unchanged.

---

## 11. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. **Palette consistency** — app + LP side by side, the app uses the same ported token system (one
   orange brand + cyan/magenta accents + deep-space "void" ramp) and reads as visually consistent
   with the LP.
2. **Single token source** — one source of truth (no README contradicting `colors.css`) and the three
   drifting blues (`#3668d4` vs `#5b7fcc`/`rgba(91,127,204)`) collapsed to one value used everywhere.
3. **Buttons** — every primary/secondary button uses the ported `.btn-arcade` style (chunky 3D press
   + hover brightness), no button references the undefined `.title` class, and no button uses
   off-palette raw Tailwind colors (e.g. `bg-blue-500`).
4. **Fonts** — only Press Start 2P (headings/labels) and VT323 (body/terminal) are used — Pixelify
   Sans and Courier New (and, per §7.1, Silkscreen) removed from `index.html` and all
   component/toast/lobby-header styles.
5. **Header** — a frosted backdrop-blur bar with a glowing coin and a styled `SPACE·STROKER`
   logotype; icon/wallet buttons match the new arcade style; it stays visible at all times (no
   auto-hide on scroll).
6. **Lobby** — the "GAME SELECTION" header is in Press Start 2P (not Courier New) and the game cards
   render at LP material quality (arcade-panel bevel/shadows, new tokens, consistent glows/hovers)
   while keeping their clip-path pixel-card identity.
7. **Background** — retains its gameplay-reactive speed, uses the new token palette, and gains the
   LP's added richness (nebula tint / dot-grid / seam vignette) — without being replaced by the LP's
   per-section system.
8. **Game screen** — its layout (`GameTerminal` / `GameMonkey` / `GameSidePanel`) and green-phosphor
   CRT terminal are unchanged in structure, yet inherit the new tokens/fonts/buttons so they don't
   clash with the restyled chrome.
9. **Shared surfaces + mobile** — toasts, loaders, `TokenBalance`, wallet button,
   settings/customization modal use the new tokens + material system; the existing mobile responsive
   structure (`GameMobileTabs`, `GAME | CHAT·STATS` segmented view, portrait/landscape, standalone &
   embedded) is preserved unchanged (re-skin only).
10. **Build + reduced-motion** — `yarn lint` and `yarn build` pass, the app was QA'd in the browser
    against the live LP, and all new glows/animations freeze under `prefers-reduced-motion`.
11. **Flexibility** — an in-scope item judged unnecessary during in-app QA may be dropped, provided
    the non-negotiable core (unified tokens, 2-font system, `.btn-arcade` buttons, LP-quality header)
    still ships.

---

## 12. Manual QA

1. `cd .worktrees/app-design-refinement/monkify-front && yarn install && yarn serve` →
   `http://localhost:8080`; open the live LP alongside for side-by-side comparison.
2. **Tokens/palette:** the app reads as the same arcade family as the LP; inspect — no
   `rgba(91,127,204)` / `#00b7ff` / `bg-blue-500` literals survive; `colors.css` is the single source.
3. **Buttons:** primary (orange) + secondary (cyan) show the 3D key-press (`:active` drop) + hover
   brightness; no `.title` warning; used everywhere (header, wallet, modal).
4. **Fonts:** only Press Start 2P + VT323 render; no Courier New in the lobby headers or toasts; game
   panels no longer use Silkscreen; check the network tab loads only 2 font families.
5. **Header:** frosted blur bar + coin glow + `SPACE·STROKER` logotype; scroll the lobby/game — the
   header **stays put** (never hides); mute/volume, wallet cluster (SolanaToken game), settings gear,
   dev admin all restyled and functional.
6. **Lobby:** GAME SELECTION header in Press Start 2P; cards keep the clip-path shape at
   arcade-panel quality (bevel/glow/hover), lottery badge + PRESS TO START intact.
7. **Background:** speed still reacts to typing during a running game; nebula/dot-grid/vignette
   richness present (if kept); no horizontal scroll.
8. **Game screen:** layout + green-phosphor CRT unchanged; "away"-state escalation + terminal
   readable; tokens/fonts/buttons match the chrome (no clash). Resize to portrait + landscape phone —
   `GAME | CHAT·STATS` segmented view unchanged; test embedded (`?embed=1`) fill-the-frame look.
9. **Operator theming:** with an operator-branded launch (non-default `--color-secondary`), the
   `secondary` surfaces still re-theme live.
10. **Reduced motion** (OS setting): new glows/animations frozen; no stuck-hidden content.
11. `yarn lint` (CRLF clean) + `yarn build` succeed; console = 0 errors.

---

## 13. Trello / pipeline

- **Source card:** [APP (front) design refinement #79](https://trello.com/c/R2x70aWY) — list
  **Specs created**; the **Implementation** block on the card carries the worktree, branch, and spec.
- **Branch:** `feature/app-design-refinement` in `monkify-front` (off `main`) and `monkify-meta` (off
  `main`). Worktrees at `.worktrees/app-design-refinement/<repo>` (registered on the card).
- **Design source (reference only):** the LP design system lives in `monkify-lp/src/styles/global.css`
  (`@theme` tokens + `@layer components` materials) and `src/components/Header.vue` — §4 quotes the
  exact values so implementation does not need to re-derive them.
- **Next:** `/monkify-implement-feature APP (front) design refinement` to build it in the worktree
  (frontend-only ⇒ **no integration test**; the implement skill does live browser iteration +
  `yarn lint` + `yarn build`), then `/monkify-ship-feature APP (front) design refinement` to sync the
  KB and merge `feature/app-design-refinement` → `main` (front + meta).

---

## 14. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #79. Frontend-only, app-only (`monkify-front`). Grounded in a code-exploration pass over both repos: extracted the LP's exact OKLCH token set + material-class bodies (`monkify-lp/src/styles/global.css`) as the port source (§4), and mapped every app surface + literal to change (§6, §9). Documents five findings the card under-specified: (a) **Silkscreen** is a live 3rd font used in game panels — the 2-font AC requires migrating it, not just Pixelify Sans/Courier New (§7.1); (b) the "three blues" are precisely token `#3668d4` vs glow-hardcode `#5b7fcc`(=`rgba(91,127,204)`) vs README doc `#5b7fcc`, plus a stray `#00b7ff`(=`rgba(0,183,255)`) (§7.2); (c) `.title` is an undefined class used in 3 places (§7.3); (d) the app header has **no** auto-hide today — don't port the LP's `showHeader` logic (§7.4); (e) `--color-secondary` is a live operator-theming runtime hook to preserve (§7.5). |
