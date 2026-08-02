# Feature — APP (front) design refinement

> Status: **shipped.** Authoritative spec: `../specs/app-design-refinement.md`
> (merged via `feature/app-design-refinement` on 2026-07-21; `monkify-front` → `main` as
> `1a426af`, `monkify-meta` KB → `main`). **Frontend-only, app-only.** A matching `SPACE STROKER`
> wordmark-spacing tweak shipped **separately** to `monkify-lp` `master` (`ba7c203`) and is not part
> of this branch.

## What it does

Ports the landing page's arcade design system into the game app (`monkify-front`, Vue 3 + Tailwind
**v3** + DaisyUI) to close the visual gap with the freshly-redesigned LP. It is a **chrome overhaul**
— tokens, fonts, buttons, header, background, lobby, shared UI — that raises the app to LP quality
**without restructuring the in-play game screen** (that screen only *inherits* the new
tokens/fonts/buttons). No backend / DB / API / SignalR / gameplay change; the app's strengths (the
reactive-speed animated space background and the clip-path lobby cards) are re-tokenized, not
replaced. Audience: players who bounce from the LP into the app and used to feel a quality drop.

## Core rules (current app design system)

- **One token source of truth.** `src/assets/styles/colors.css` holds the ported LP OKLCH tokens
  (`--color-primary(-bright/-deep)`, `--color-cyan(-deep)`, `--color-magenta`, the deep-space "void"
  ramp `--color-void/-bg/-surface/-surface-2/-line`, the ink ramp, radii + easings). The three
  previously-drifting blues (`#3668d4` token vs the hardcoded `rgba(91,127,204)` glows vs the stale
  README's `#5b7fcc`, plus a stray `#00b7ff`) are **collapsed to one cyan accent**; `--color-blue-vivid`
  survives only as a **legacy alias equal to the cyan** (a hex, `#47deee`, so existing
  `border-[var(--color-blue-vivid)]/opacity` markup keeps building).
- **`--color-secondary` preserved.** Default `#181b31`. *(It was the runtime operator-themable
  variable — `useOperator.applyOperatorTheme` set it on `:root`; that hook was deleted with operators,
  so the token is now static, but it is still a token — Tailwind maps `secondary` to it.)*
  The token port must never fold it into the void ramp.
- **Material layer.** The LP arcade classes are re-implemented in `src/assets/styles/arcade.css`
  (`.arcade-panel(+--accent/--cyan)`, `.btn-arcade(+--primary/--cyan/--sm)`, `.chip`, `.crt-screen`,
  `.scanlines`, `.glow-*`/`.text-glow-*`, `.pixel-notch`, `.step-num`, `.pixel-rule`, `.title`, plus
  the seam `.section-vignette`). **Gotcha:** these are **plain CSS rules, deliberately NOT wrapped in
  `@layer components`** — Vue CLI's css-loader runs Tailwind's PostCSS plugin on each `@import`'d file
  standalone, where a bare `@layer components` has no matching `@tailwind components` directive and
  fails the build. `colors.css` + `arcade.css` are `@import`ed from `src/main.css`.
- **Two fonts only.** Press Start 2P (headings/labels, `.press-start-2p-regular` / `font-display`) +
  VT323 (body/terminal, `.vt323-regular` / `font-body`). `public/index.html` loads only those two;
  Pixelify Sans, Silkscreen, and Courier New are gone from player-facing surfaces (the game panels'
  Silkscreen labels migrated to PS2P for nav/buttons, VT323 for small readouts).
- **Buttons.** `PrimaryButton` → `.btn-arcade--primary` (orange, 3D key-press), `SecondaryButton` →
  `.btn-arcade--cyan`; no button uses the off-palette `bg-blue-500` or references an undefined `.title`
  (a `.title` = `font-display` label utility now exists for the customization-modal heading).
- **Header** (`GameHeader.vue`) is a **frosted** `bg-void/blur` bar with a bottom hairline, a coin-glow
  `SPACE STROKER` wordmark (white `SPACE` + a medium gap + orange `STROKER`, Press Start 2P), and
  arcade-styled icon/wallet buttons. It **stays visible at all times — no scroll auto-hide** (it holds
  live controls: wallet, mute, settings, dev-admin).
- **Background.** The reactive-speed starfield (`GameBackground.vue`, `speedMultiplier = typingSpeed`,
  two stacked instances + `TorpedoSpawner`) is unchanged. Atmospheric **nebula** is a live component
  (`NebulaField.vue`): randomized colored clouds (random position/size/color/opacity `0.05–0.18`) that
  **drift with the reactive background** (bound to the same `typingSpeed`, a touch slower for parallax),
  respawning like the space elements; it freezes under `prefers-reduced-motion`. A static seam vignette
  frames the content. (The LP dot-grid was intentionally **not** ported — it moirés against the
  animated starfield.)
- **Lobby** (`GameTypes.vue` / `GameTypeCard.vue`): the "GAME SELECTION" header is Press Start 2P; the
  cards **keep their clip-path pixel identity** but wear a **neutral brushed-metal bevel** that **warms
  to brand amber on hover** (default frame is not coloured — the earlier bright-cyan frame was dropped in
  QA). Lottery badge, `crt-flicker`, and the blinking `PRESS TO START` are re-tokenized.
- **Game screen inherits, never restructures.** `GamePage` / `GameTerminal` / `GameMonkey` /
  `GameSidePanel` and the signature **green-phosphor CRT terminal** keep their layout; they only inherit
  the new tokens/fonts/buttons so they don't clash. (One layout-adjacent fix shipped: `GameMonkey`
  stopped reserving the chat panel's full right column, so the floating monkey now centres in the open
  area instead of being squished.) The mobile `GAME | CHAT·STATS` segmented view and the embedded
  fill-the-frame behaviour are re-skin-only, structure preserved.
- **Reduced motion.** Every new glow is a static box-shadow; the only new animation (the `.btn-arcade`
  press transition) and the nebula drift both freeze under `@media (prefers-reduced-motion: reduce)`.
- **Definition of done** (no test suite in `monkify-front`): `yarn lint` + `yarn build` green + browser
  QA against the running stack. Frontend-only ⇒ **no backend unit/integration test**.

## Key code touchpoints (`monkify-front` on `main`)

- `src/assets/styles/colors.css` — the ported token set + collapsed-blue legacy aliases.
- `src/assets/styles/arcade.css` *(new)* — the material classes (plain CSS, not `@layer`) + vignette +
  reduced-motion guard.
- `src/assets/styles/README.md` — rewritten to document the unified tokens.
- `src/main.css` — `@import`s colors + arcade; toast re-tokenized (VT323, cyan glow); `html/body` bg →
  `--color-void`; Silkscreen utilities removed.
- `tailwind.config.js` — `fontFamily` (`display`/`body`) + `colors` extended with the new tokens; keeps
  `secondary: var(--color-secondary)` + DaisyUI.
- `public/index.html` — Google-Fonts link reduced to Press Start 2P + VT323.
- `src/components/GameHeader.vue` — frosted bar, `SPACE STROKER` wordmark, arcade icon buttons.
- `src/components/ui/PrimaryButton.vue`, `SecondaryButton.vue` — `.btn-arcade` variants.
- `src/components/background/NebulaField.vue` *(new)* + `src/App.vue` — drifting randomized nebula +
  seam vignette wiring.
- `src/components/game-selection/GameTypes.vue`, `GameTypeCard.vue` — PS2P lobby header, neutral-metal
  cards (amber hover), tokenized literals.
- `src/components/game/GameMonkey.vue` — relaxed the right-column reservation (monkey placement).
- Re-tokenized/inherited: `GameSidePanel`, `GameStatsPanel`, `GameChatPanel`, `GameConsole`,
  `GameMobileTabs` (Silkscreen → PS2P/VT323), `LoaderSpinner`, `TokenBalance`, `WalletButtons`,
  `header/PlayerCustomizationModal.vue` (→ `.arcade-panel`; **superseded by #124** — it is now the
  shared `ui/ArcadeModal.vue` shell on the grey panel, as is every other overlay),
  `header/DevAdminButton.vue`.

## Not covered / follow-ups

- The lobby **"GAME SELECTION" header keeps a cyan CRT glow** — the one remaining bright-cyan accent on
  the lobby, left as-is by choice (trivial to neutralize later).
- **Admin / dev-only surfaces** (admin portal, wallet-bridge) still use Courier New and
  `--color-blue-vivid` (now cyan) borders — outside the player-facing scope; changing them risks
  breaking un-QA'd admin forms.
- The third **DM Sans** font seen in the browser comes from the `solana-wallets-vue` widget's own
  stylesheet, not app chrome.
- A pre-existing prettier nit in `src/services/mainService.ts` was cleared by `lint:fix` so the branch
  lints green (formatting only, unrelated to this feature).
