# Domain — Frontend UX (the app's look, chrome, motion, audio and mobile)

> **What this covers.** Everything about how `monkify-front` *presents* the game — the arcade design
> system, the icon language and header menu, the responsive/mobile layouts, the animated background
> and its performance controls, audio, chat, money animations, and the rules about what a round is
> allowed to do to pages that are not the game.
>
> **Frontend-only.** No entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract
> or gameplay change lives here, and `monkify-front` has **no test runner** — `lint` + `build` + a
> local Docker deploy is the DoD.
>
> Deep design: [`../specs/app-design-refinement.md`](../specs/app-design-refinement.md) ·
> [`../specs/control-sizing-system.md`](../specs/control-sizing-system.md) ·
> [`../specs/modal-panel-design-system.md`](../specs/modal-panel-design-system.md) ·
> [`../specs/icons-and-menu-overhaul.md`](../specs/icons-and-menu-overhaul.md) ·
> [`../specs/mobile-layout.md`](../specs/mobile-layout.md) ·
> [`../specs/mobile-game-design-update.md`](../specs/mobile-game-design-update.md) ·
> [`../specs/mobile-console-edge-cases.md`](../specs/mobile-console-edge-cases.md) ·
> [`../specs/mobile-wallet-connect.md`](../specs/mobile-wallet-connect.md) ·
> [`../specs/light-graphics-mode.md`](../specs/light-graphics-mode.md) ·
> [`../specs/graphics-settings-controls.md`](../specs/graphics-settings-controls.md) ·
> [`../specs/background-lifecycle-hardening.md`](../specs/background-lifecycle-hardening.md) ·
> [`../specs/off-page-game-presentation.md`](../specs/off-page-game-presentation.md) ·
> [`../specs/audio-overhaul.md`](../specs/audio-overhaul.md) ·
> [`../specs/game-chat.md`](../specs/game-chat.md) ·
> [`../specs/money-changing-animations.md`](../specs/money-changing-animations.md) ·
> [`../specs/redesign-nice-win-modal.md`](../specs/redesign-nice-win-modal.md) ·
> [`../specs/standardize-scrollbars.md`](../specs/standardize-scrollbars.md).

---

## 1. The design system

The landing page's arcade design system was ported into the app to close the visual gap. It is a
**chrome overhaul** — tokens, fonts, buttons, header, background, lobby, shared UI — that raised the app
to LP quality **without restructuring the in-play game screen**, which only *inherits* the new
tokens/fonts/buttons.

- **One accent.** Three drifting blues collapsed into a single cyan; `--color-blue-vivid` kept as that
  cyan hex.
- **Two fonts:** Press Start 2P + VT323 (Pixelify Sans, Silkscreen and Courier New dropped).
- ⚠️ **`arcade.css` is plain CSS, NOT `@layer components`** — a Vue CLI css-loader gotcha.
- The app's own strengths (the reactive-speed animated space background, the clip-path lobby cards) were
  **re-tokenized, not replaced**. Lobby cards are neutral brushed metal with amber hover.
- A **frosted, always-visible header** with the `SPACE STROKER` wordmark.
- Sibling reference systems: a **control sizing system** and a **consolidated modal + panel design
  system**, both distilled into `../conventions/frontend.md` rather than getting their own docs.

## 2. Icons and the header menu

The app had **no icon system**: nine feather-style **stroke** SVGs pasted inline in two files, a literal
VT323 letter `X` as every modal's close button, `✓ ✗ ▶ ▼` **text characters** standing in for glyphs,
and `spinner.svg`. The only real pixel art was Orbit's — whose own source said it was *"NOT the seed of
an app-wide icon system"*.

There is now **one pixel-art icon language**: a shared registry (`src/constants/icons.ts`) rendered by
one component (`ui/AppIcon.vue`). Every page and feature carries an **identity mark**.

⚠️ **The ☰ header menu is a 3-column icon pad, not a list of rows** — nine cells (Home · Practice ·
Orbit · Stats · Fair · How to play · History · Responsible · Settings), 24px pixel marks over VT323
labels, with a pixel glyph on the trigger. **The embed renders the same grid** with its two survivors
(History + Settings), **not an alternate layout**. Stats and Fair are hidden when embedded. The
dismissal contract (opens on click, closes on item click or outside click, **no hover**) is unchanged,
and the dev Admin shortcut is long gone.

## 3. Mobile

Mobile engages **below `lg` (1024px)** — reusing the `GameMonkey hidden lg:block` boundary, so landscape
phones are covered. Portrait **and** landscape, standalone **and** inside an iframe.

- **Lobby:** 1-column condensed cards.
- **Game:** a `GAME | CHAT` segmented view, **absolutely stacked so a hidden view cannot intercept
  taps**; full-width fluid terminal + compact bet panel (**no amount field** — the lever *is* PLACE
  BET); monkey hidden; unread dot on chat.
- **Mobile crypto wallet connect** is a single modal with multi-wallet **deeplinks** into the
  Phantom/Solflare in-app browser, reusing `sendToken`. ⚠️ An **already-hydrated deeplink session never
  fires the wallet watcher**, which is why the name prompt also evaluates on mount — see
  [`player.md`](player.md).
- Later passes brought the arcade cabinet + Edson to mobile and fixed tablet-console / lever-scaling
  edge cases.

## 4. Background, motion and performance

- ⚠️ **`NebulaField.vue` was deleted outright** — 5 clouds of 40–82vw radial gradient carrying
  `blur(24–54px)` **and** `mix-blend-mode: screen`, restyled every rAF tick. Confirmed live as the
  dominant cost.
- **Light graphics mode** is resolved per load with **no UI and no persistence**: mobile (`< lg`) ⇒
  always light → WebGL unmasked-renderer probe (SwiftShader/llvmpipe/software ANGLE) → a ~2 s rAF FPS
  sample (< 25 FPS) → inconclusive ⇒ normal. Published as `html.light-graphics`, which strips all
  blur/`backdrop-filter`, infinite animations, blend overlays and multi-layer glows. The background
  **keeps drifting**, heavily reduced.
- ⚠️ **Then `background-lifecycle-hardening` changed the arithmetic:** the cap and spawn rate are scaled
  by a global **`BG_DENSITY`** — normal mode is **29 per layer, not 64** — and light mode composes as a
  **`Math.min` floor** over it, so its 8 is a **ceiling**, not a fixed number. ⚠️ Light mode's frozen
  scroll means the backdrop **never drifts below 1024px**, which hides all backdrop-drift bugs there.
- **Graphics settings controls** later gave the player their own light-mode and sky-density switches in
  the settings modal — `localStorage` only, applied instantly, ⚠️ **rendered outside the wallet
  `v-if`** so a device setting never sits behind an identity the player lacks.

### A round must not drive pages that are not the game

⚠️ **The cause was structural, not a leak.** `GameBackground` (×2) and `TorpedoSpawner` are mounted in
`App.vue`, so they render on **every** page, and they read `useGame` — a module singleton whose SignalR
subscription is tab-scoped and **deliberately outlives `GamePage`**.

Leaving `/game/play` mid-round used to leave that round's `DisplaySpeed` / `Bets` / `Status` events
ramping the starfield to speed 2/3 on the lobby, Orbit, how-to-play, history and stats, firing a torpedo
per incoming bet, and dropping the victory/lose sting on top of another page's music — or into a page
with no music at all. **The global animation layer now mirrors the live round only on the game page**;
everywhere else it renders the calm default sky.

## 5. Audio

One cohesive AI-generated (Suno) **warm arcade-chiptune** identity. **Entirely frontend, display-only —
audio never affects bets, settlement, winners or game state.**

- Background music follows the game lifecycle (idle / starting / random running); a menu track plays
  while browsing games.
- Own-bet, button and error SFX; **bettors-only** victory/lose cues at end of round.
- Header **mute + volume**, persisted to `localStorage`, governing everything through a master gain.
- ⚠️ **Shipped reality differs from the spec:** the 3/2/1 proximity "coin" bleeps were **dropped**
  (Suno could not produce them) — **9 generated mp3s, not 12**, and no proximity audio. `error1.wav` was
  kept. Assets were generated **manually in the Suno UI** (the MCP ran out of credits) and
  post-processed with ffmpeg.
- ⚠️ Suno assets must be genuine 8/16-bit arcade chiptune, and loops must be **truly seamless** (ffmpeg
  `acrossfade` wrap).

## 6. Chat

A lightweight **per-game chat** on `/game` during the betting/waiting phase and the live run, in the
right-side panel, attributed to each player's **display name** (the same identity as bets and winners).

- ⚠️ **Reuses the existing `ActiveGamesHub` connection and game SignalR group — no third hub.**
- DB audit persistence via event → consumer, with a daily purge (`Chat.RetentionDays`, default 7).
- Client-side 200-char / 1 s limits. **No history replay** — late joiners see nothing. Read-only on end,
  reset on rollover.
- The side panel keeps its arcade-cabinet chrome (collapse / dock-rail / maximize) but is **chat-only**
  since Stats and Fair became menu destinations — see [`stats-and-history.md`](stats-and-history.md).

## 7. Money animations and misc

- **Money change animations** on balance/pot movements, plus a redesigned win modal.
- **Standardized scrollbars** across the app and LP (CSS-only).

## Key code touchpoints (`monkify-front`)

`src/styles/arcade.css` + `colors.css` + `tailwind.config.js` (tokens), `src/constants/icons.ts` +
`src/components/ui/AppIcon.vue`, `src/components/GameHeader.vue` (icon pad),
`src/components/game/{GameTerminal,GameConsole,GameMonkey,GameSidePanel,GameChatPanel}.vue`,
`src/components/GameBackground.vue` + `TorpedoSpawner.vue`, `src/hooks/{usePerfMode,useIsMobile,useChatUnread}.ts`,
`src/utils/walletDeepLink.ts`, `src/components/GameAudio.vue` + `src/assets/audios/`,
`src/components/game/GameMobileTabs.vue`.
