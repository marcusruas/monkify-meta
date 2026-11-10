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

⚠️ **The header menu's trigger is a PROFILE CARD, not a ☰ glyph** (#186, owner 2026-10-16,
`header/ProfileCard.vue`): the player's Edson profile icon on a tile in their color profile (Profile V2,
[`profile-v2.md`](profile-v2.md)) + `LV` badge + a chevron, **no name at any width**; signed out, a grey
guest tile with Edson. It opens the same menu (the docs still call it "the ☰
menu").

⚠️ **The menu is an identity head over a 4-column icon pad, not a list of rows** (`.menu-pad`, 336px).
From the top:
1. **`header/ProfileMenuHead.vue`** — avatar, name, `LV`, a 10-segment level bar and
   `x / y SOL gambled → LV n`, linking to your own `/player/:wallet` (a plain block when embedded); then a
   **wallet row** (balance, tap copies the address) and an **Orbit row** (balance, opens Orbit; not when
   embedded). Signed out: **Connect wallet**. Mounted only while the menu is open, so its profile read
   costs nothing until then. This block is the way to your own profile — **there is no PROFILE cell**.
2. The phone-only audio strip.
3. The grid: **eight** cells in a 4×2 pad (Home · Practice · Stats · Fair · How to play · History ·
   Responsible · Settings), 24px pixel marks over VT323 labels. **The embed renders the same grid** with
   its two survivors (History + Settings), **not an alternate layout**.
4. The token row (when a mint is configured), then a **DISCONNECT** button when a wallet is connected.

The dismissal contract (opens on click, closes on item click or outside click, **no hover**) is unchanged,
and the dev Admin shortcut is long gone.

⚠️ **A ninth cell is not an option.** It would start a third row the 4-column grid can never fill; the
audio strip and the token row stay full-width strips around the grid (keep them in step), and the token row
is the only destination in the pad that **leaves the app**, which a cell would misrepresent as a page. See
[`token-launch.md`](token-launch.md).

⚠️ **The header BAR carries no promotional control.** It is the funding/round strip — wallet, balance,
audio — and a token pill shipped there for one build before being pulled: an always-on outbound CTA
competed with the in-game primary actions on every screen, mid-round included.

⚠️ **The bar shows ONE balance, at every width** (#186, owner 2026-10-16): while Orbit holds money
(session + balance > 0) the Orbit pill shows its balance and the wallet pill is **hidden** (`v-show` —
`WalletButtons` registers the desktop connect trigger on mount); otherwise the wallet pill shows its balance
and the Orbit pill folds to its icon square (`OrbitLauncher compact` → `door-compact`). The other balance is
one tap away in the menu head and in Orbit's **WALLET HOLDS** card ([`orbit.md`](orbit.md)).

⚠️ **The bar DOES carry a game selector since 2026-10-02** (`header/GameSwitcher.vue`, owner's request) — it
is navigation, the same class of object as the ☰ pad, not promotion. Before it, changing game from inside
one meant wordmark → home → scroll → card.

- **Two shapes, mounted twice by `GameHeader`:** from `lg` up a small labelled box beside the wordmark
  (the game on screen, or `GAMES` off the game route); below `lg` a bare icon square in the **same slot**,
  one bar-gap (8px) from Edson's head. ⚠️ It first shipped in the right-hand cluster, before the ☰ glyph,
  and the owner moved it left the same day. The panel anchors left in both shapes.
- ⚠️ **Its size took three cuts, and the middle one stuck.** 40px tall with 10px type was "way too big";
  28px with 8px type was "way too small". It is **34px tall with 9px type**, 12px of side padding, 10px
  between the mark and the name, a 1px frame and a 230px panel with 38px rows (44px below `lg`). Still
  smaller than a pill on purpose.
- ⚠️ **Names are shortened here** — a trailing parenthetical is dropped, so `Monkey Duel (Coinflip)` reads
  `MONKEY DUEL`. The lobby card and the accessible name keep the full name.
- Picking a game goes through **`useGamePicker.chooseGame`**, the handler the mode cards and the home hero
  use, so `game_type_selected` fires and the route changes the same way from every entry point. The list
  is the lobby's own (`shownGameTypes`): same games, same order, COMING SOON listed but inert. The
  catalogue is fetched only if the store is empty, and only when the panel is first opened.
- The row dots mirror `GameTypeCard`'s per-archetype accents by hand — change one, change the other.
- ⚠️ **The phone bar's cluster stepped down to make room**: `.app-header`'s control height is 32px (was
  36px), the pills lose padding and gap, and below `md` the balance figure is 15px (was 17px) and the
  signed-out ORBIT word tightens. The SOL tag stays 7px (6px was once reported as too small). Measured at
  360px with a wallet connected: ~41px of slack between the selector square and the Orbit pill (measured
  before #186, when both pills carried a number; only one is expanded now and the ☰ glyph became the wider
  profile card). Re-measure with a three-digit balance in the expanded pill before changing any of those numbers.
- New coarse icon `games` (four tiles) — not a gamepad, which is `practice`, and not three bars, which is
  `menu`.

⚠️ **Press Start 2P draws `$` and `!` wrong, and coarse marks like `●` fall outside the self-hosted
subset** (they silently fall back to a system font with that font's baseline). Display-face copy carrying
them goes through **`ui/PixelText.vue`**, and marks are drawn geometry, never a bare character. Its
wrapper `<span>` is load-bearing: inside `.btn-arcade` (an `inline-flex` with a `gap`) a fragment makes
every piece its own flex item and renders `BUY $ S T ROKE`. ⚠️ **VT323's `$` is correct and must not be
patched** — `MoneyValue.vue`'s prefix is only safe through `PixelText` because all four of its call sites
render in the title face.

## 3. Mobile

Mobile engages **below `lg` (1024px)** — reusing the `GameMonkey hidden lg:block` boundary, so landscape
phones are covered. Portrait **and** landscape, standalone **and** inside an iframe.

- **Lobby:** 1-column condensed cards.
- **Game:** a `GAME` label strip (the terminal's top bezel — the CHAT segment and its unread dot left
  with #205; chat is the app-wide sheet from the header button); full-width fluid terminal + compact bet
  panel (**no amount field** — the lever *is* PLACE BET); monkey hidden.
- **Mobile crypto wallet connect** is a single modal with multi-wallet **deeplinks** into the
  Phantom/Solflare in-app browser, reusing `sendToken`. ⚠️ An **already-hydrated deeplink session never
  fires the wallet watcher**, which is why the name prompt also evaluates on mount — see
  [`player.md`](player.md).
- Later passes brought the arcade cabinet + Edson to mobile and fixed tablet-console / lever-scaling
  edge cases.

### The native-keyboard lift, and the TWO ways it is torn down

While the bet input is focused, the game/practice column lifts the console above the native keyboard
(`pb-[38dvh]` on the scrolling column + Edson hidden via `v-show="!betInputFocused"`), so the visor and
the lever stay reachable. Nothing in the app can observe the keyboard directly — `public/index.html`
ships **no `interactive-widget`**, so `dvh` does not move when the keyboard opens or closes.

The lift is unwound by the input's `@blur`, and **one shared composable
(`src/hooks/useKeyboardDismiss.ts`) force-blurs the input in the two cases where the keyboard goes away
without the input reporting it.** It is used by **both** `GameConsole` and `PracticeConsole` — the bug
shipped twice because the lift logic was copy-pasted, and the fix must not become a third copy. Its
only output is `input.blur()`: the existing `@blur` handler then runs the **same** teardown as every
other dismissal, so the app keeps **one** teardown route rather than two that can drift.

| Route | Signal | Why not the other one |
| --- | --- | --- |
| **Dismissed** — system BACK, swipe-down, iOS "Done" | `visualViewport` `resize`; the keyboard is open while `innerHeight − visualViewport.height > 150px` | Keying on the *keyboard* rather than on which button was pressed covers all three for free |
| **Left the app** — system app switcher, screen lock | `window` `blur` | `visualViewport` cannot see it at all, and `visibilitychange` arrives **~967 ms** too late (measured) |

Four rules that cost real time to learn, all of them load-bearing:

- **The threshold is ABSOLUTE, not a ratio.** A real soft keyboard is ~250–350px and URL-bar chrome
  shifts are ~50–110px, so 150 separates them with room on both sides. 25% of a tall phone is ~250px,
  which a numeric pad or a floating/split keyboard need not clear — and a false *close* collapses the
  layout under a player who is still typing, the one failure to avoid.
- **The detector LATCHES on "keyboard seen open" before it will fire a close.** The keyboard *animates*
  open and Chrome Android emits `resize` through that animation, so an unlatched "gap below 150 ⇒ blur"
  blurs the input on the first partial frame — the keyboard would never finish opening and the fix
  would be worse than the bug.
- **⚠️ The window-blur listener is armed on the SURFACE, not on `isFocused`.** Measured on a real
  phone: pressing the app switcher fires the input's `blur` **event** at `t+0` (so `isFocused` is
  already false) and `window` `blur` at `t+1ms`. A focus-armed listener is detached before it arrives.
  Hence `useKeyboardDismiss(input, isFocused, onLiftSurface)` — two refs, two lifetimes.
  **Collapsing them back into a single `armed` ref reintroduces the bug.**
- **⚠️ `document.activeElement` stays the input the whole time.** Android fires the input's focus/blur
  *events* from **window** focus changes while the element keeps real DOM focus — and that retained
  focus is exactly what Chrome **restores** on return, re-firing `focus`, re-lifting the cabinet, with
  no keyboard behind it. Giving up DOM focus *while we still hold it* is what leaves nothing to
  restore. So the handler's guard is `document.activeElement === input`, not a focus flag.

**Three inputs are wired, not two:** `GameConsole`'s *desktop* input branch is also a mobile surface —
`GamePage.vue` renders `<game-console wide>` for viewports 600–1023px wide **and** ≥600px tall and
binds `@focus-change` to it. `PracticeConsole` has no `wide` band, so only its mobile input is wired;
its desktop branch does not even emit `focus-change`.

**Desktop is untouched** — `onLiftSurface` is false above `lg`, so no listener is attached and a
desktop player who alt-tabs away mid-word **keeps their caret**. Blurring costs only the caret in any
case: the typed word lives in the store (`v-model`), never in the DOM node.

⚠️ **This was a device-only defect and two fixes shipped green in desktop Chrome before failing on the
phone**, because both were verified against synthetic events that encoded the same wrong model of
Android. See `specs/mobile-keyboard-dismiss.md` §7.2b for the measured trace — it is the primary
evidence and should not be re-derived.

### The practice tutorial bubble below `lg` — a stacking bug, not a sizing bug

The tutorial bubble on `/game/practice` is **the one element in the app that nothing may ever cover**:
SKIP ALL is the only way out of the step-1 control lock, and the bubble is the first thing a
first-time visitor reads on the one surface that works without a wallet. It sits on its **own tier,
`--z-practice-bubble: 43`**, above every lifted band (`--z-practice-cue: 42`) and below
`--z-popover: 50`, so Settings still covers it.

It used to share `z-practice-cue` with Edson's band, and **he is later in the DOM**, so his saucer
painted over the bubble's third line and over the whole CONTINUE button. The same DOM-order rule hid
it behind the **console** while the native keyboard was open. Both were fixed by the tier alone — the
bubble was never too big.

**Edson sinks behind the cabinet while a bubble is up** (a 220ms `transition` on the wrapper, `none`
under `prefers-reduced-motion`), and rises when it closes — four dips per round, accepted. Four rules
hold that geometry together:

- **⚠️ Solve for the PEEK, not the depth.** `--practice-monkey-peek` is how much of Edson stays above
  the cabinet's top edge; the sink is `max(0px, 100% − peek − overlap)` and the bubble anchors at
  `console-h + peek + 20px`. A flat "sink 50%" is what put the bubble **on his face, twice**: his band
  height is **flex-derived**, so his head lands at a different offset on every viewport and a
  cabinet-relative anchor chases a moving target. Solving for the peek makes the two constants
  relative to each other — measured across 360×{640, 742, 934}, the band moves 172 → 202 → 265px, the
  sink follows 68 → 95 → 151px, and the tail-to-head gap holds at **2px**.
- **The peek IS the floor.** On a viewport too short to give up that much travel (landscape, where the
  band floors at 80px) the `max()` resolves to `0px` and he simply does not sink. He can never vanish
  — no separate clamp needed.
- **⚠️ The peek is also the terminal-coverage dial, and the two cannot both win.** There are only
  ~95px between the terminal's bottom and a sunk Edson's head on a 360×740 phone, against ~160px of
  bubble: every px of peek is a px of terminal covered (80px ≈ 21% of the band, 48px ≈ 11%, 24px ≈ 5%).
  **100px is the owner's call** — "from his arms up" on an iPhone SE — paid for by tightening the
  bubble's type and padding **mobile-only** (`font-size: 24 → 21px`, tighter padding: −26px, measured;
  21px keeps clear of the 16px iOS-zoom floor). Desktop keeps the original scale. The accepted cost is
  that on a short viewport the bubble covers the terminal's **status line**.
- **⚠️ `--practice-monkey-peek` may only appear where Edson is actually rendered.** The
  keyboard-open branch is `38dvh + console-h + 20px` with **no peek**: `PracticeMonkey` is `v-show`n
  off there, so reserving 100px of headroom for an absent mascot pushed the bubble's first line under
  the header and clipped it — caught on a real phone, not in emulation.

Two implementation hazards, both instances of failure modes this component family has hit before:

- **The sink goes on the wrapper `<div>`, never on the `<img>`.** The image already owns `transform`
  for its entry animation and for `practice-wobble 8s infinite`, whose keyframes set `transform`
  outright and would erase a translate 5× per loop. (Same reason the bubble's tail uses `margin-left`.)
- **A `transition`, never an animation** — every phone runs `html.light-graphics`, which strips
  infinite animations by selector. Do not add this selector to `arcade.css`'s kill list.
- **Positioning stays at the call site.** `PracticeBubble.vue`'s scoped block (0,2,0) beats a Tailwind
  utility (0,1,0); adding `position`/`bottom` there is the mechanic that once stretched the bubble to
  the full height of the desktop row. ⚠️ In Tailwind arbitrary values, `calc()`'s required whitespace
  is written as an **underscore** (`calc(var(--x)_+_20px)`) — without it the declaration is dropped
  **silently** and the bubble lands at the top of the column. And `-mt-[var(--x)]` cannot negate a
  variable; use `mt-[calc(var(--x)_*_-1)]`.

**Desktop (`>= lg`) is untouched** — there the bubble is a later sibling inside Edson's own positioned
band, so it already wins the stacking contest, and a 360px bubble in a desktop row has no wrap problem.
The action row does `flex-wrap` so **SKIP ALL drops to a second line** at 320px rather than being
clipped; the buttons never shrink, because a squeezed tap target on the only exit is not an
improvement over a clipped one.

⚠️ **Do not tune any of this in a desktop browser.** Chrome will not size a window below ~500px, and
at 500px these bubbles are two lines instead of three — every measurement comes out ~30px optimistic
and the status-line collision does not reproduce. Use CDP device emulation at a real 360px, or the
phone.

### The game console — one plate for every game (owner, 2026-10-02)

"Standardize console sizes and positions of elements — height and scales." Before this, the band the
console sits in was already one size, but the three consoles inside it were not: at 1440x900 the lever was
114, 95 or 106px wide depending on the game, a round key 37, 38 or 43px, the rows started at three different
heights, the LCD type came in four sizes, and on a phone Hyper Roll was a two-plate layout with its lever on
the right (it jumped ~90px sideways when you switched to it) while the others were two different
three-plate splits. Edson also changed size between games on a phone.

- **`components/game/ConsolePlate.vue` is the shell every live console mounts on**: `GameConsole`'s Hyper
  Roll branch, `RacePlate` (Cosmic Sprint / Space Jackpot) and `DuelPlate` (Monkey Duel), plus the
  practice faces (`PracticeConsole` race + Hyper Roll, `PracticeDuelConsole`). It owns the plate art, how
  it is sized in each context (desktop / `wide` band / phone / practice `fill` / `fitHeight`), the three
  wells and the phone re-cut, and **the lever and its tag** (not slots — one size and one place in every
  game). The consoles fill a `left` and a `right` slot.
- **The plate art is `assets/game/console/plate.png`** (97x42, generated): the duel's even 33 | 29 | 33
  split, with the face ending at row 30 — between `terminal.png`'s 28 and the duel plate's 32. The owner's
  call: the duel's plate "goes a lot lower", Sprint's was shorter, "something in between". It is the duel
  plate with two flat face rows removed and two flat base rows added, so every edge and rivet is original.
  `terminal.png` and `duel/plate.png` are now only used by the legacy preset-type branches.
- **The standard layout for a well** is global CSS in ConsolePlate (`.cp-col`, `.cp-row--kmk|mkk|full`,
  `.cp-cell`, `.cp-span-2`, `.cp-key-h`, `.cp-grid8`), in plate columns of the well's own `cqi`: a 26.4-column
  centred column, rows 9.2 columns tall and 0.8 apart, the first 4.4 down; a round key is 5.6 columns
  (7 / 31 of the column, the 7 : 17 : 7 module); the eight-key grid is 28.4 columns wide with its rows at
  the same heights. Engravings are sized by `consolePlate.ts`'s `engraving(glyphColumns)`, so glyphs are
  one size on every plate. Every LCD's readout is 60% of its bezel's height (16px floor on a phone).
- **Row 2 of the classic plate is Hyper Roll's mode-dial row** (two keys, seam, three small keys over a
  VT323 LCD), with HELP / RANDOM and the tab keys in it.
- **Measured after the change** (all four games identical): desktop 1440x900 — lever 100x158, tag 96x24 at
  13px, round keys 40x52, rows at 645 / 717, LCD type 31px. Phone 390x844 — wells 150 / 82 / 150, lever
  58x91, keys 27x34, rows at 724 / 772, LCD type 20.5px, terminal 430px, Edson ~300x190. Tablet 768x1024 —
  lever 92x145, keys 37x47.
- **Phone**: the plate shows rows 12-344 of the art and re-cuts its wells to 38.5 | 21 | 38.5% for every
  game; `GamePage` caps its width with `--plate-max-w` (= `--console-h` x 970/332) and gives every
  plate game the classic modes' 7 : 3 terminal / Edson split. Practice keeps the whole, uncut plate.
- ⚠️ The live console's legacy two-plate phone branch and three-well desktop branch in `GameConsole` are
  now reached only by legacy preset types (none live); their Hyper Roll markup is dead code.

### Mode entrances — a signature display animation per game (owner, 2026-10-02)

Switching into a game plays a short entrance on its display, once (`hooks/useModeIntro.ts`, keyed on the
game type whose round is on screen; not replayed on a tab change, a new round or a re-render; reset when
the terminal unmounts; never under reduced motion). Styles live at the bottom of `GameTerminal.vue`.
- **Hyper Roll** — the board reel (rows scramble and settle in turn), which already played on every board.
- **Cosmic Sprint** — the plates, then the bet cards, glide in from the left, staggered.
- **Space Jackpot** — the plates, then the tickets, fade in as they slide up, staggered. (A "power-on" — plates
  opening out of a line — was rejected as wonky.)
- **Monkey Duel** — PLAYER slides in from the left, STROKE from the right (since #219 the coin in the middle is
  left alone: a centre slam hid most of the sub-second flip; the board's `DuelCard` still slams its VS).
- ⚠️ Transform and opacity only, smooth easing. The first Sprint / Jackpot cut (a stepped `clip-path`
  wipe and a neon flicker) was rejected as "laggy and weird": `clip-path` animates on the main thread
  while the round is still rendering, and stepped timing reads as dropped frames.

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

- ⛔ **No music at all since 2026-10-22 (#209, owner's call): sound effects only.** The lobby loop, the
  waiting-bets loop, the three race loops and the Game Starting jingle were removed with their assets and
  `playLoopingAudio` / `preloadAudios`; `GameAudio` / `PracticeAudio` stay mounted only to clear leftover
  audio (and keep `GamePage` multi-root). An 8-bit **lever-pull** arpeggio (`lever_pull.mp3`) plays on every
  valid pull (bet, NEW ROUND, manual rollover, duel post/join/cancel, practice consoles); the automatic
  rollover is silent and an invalid pull keeps the error buzz. Every mode's **loss sting** is now Edson's
  curse (`word-lines/edson-swore.wav`, garbled blips, no bleep) instead of `lose.mp3`. Word Lines' own SFX are
  in [`hyper-roll-word-lines.md`](hyper-roll-word-lines.md).
- Own-bet, button and error SFX; **bettors-only** victory/lose cues at end of round. **Monkey Duel VS HOUSE
  (#219):** a short synthesized bell "plim" (`coin-flip.wav`) as the coin leaves the hand, and the victory /
  lose sting plays **when the coin lands** rather than on the Ended frame (the server settles the round
  before the coin is down) — `useGame` stays quiet for House duels, `GameTerminal` fires it on `landed`.
- **Edson's phone beep on the player's own bet** (`phone_beep.mp3`, with the call animation) plays only on
  **Monkey Duel and Space Jackpot**. On **Hyper Roll and Cosmic Sprint** (archetypes `houseEdge` / `race`)
  the lever-pull sound marks the bet, so the beep is skipped (#209; Sprint since 2026-10-23). Live:
  `GameMonkey.vue`; practice: `PracticeMonkey.vue` beeps only in the practice duel.
- Header **mute + volume**, persisted to `localStorage`, governing everything through a master gain.
- ⚠️ **The master gain starts at 20%, not 100% (owner's call, 2026-08-13)** —
  `DEFAULT_MASTER_VOLUME = 0.2` in `constants/helpers.ts`, which is both the module's own starting
  value and what `GameHeader` hydrates the slider to when nothing is stored. This **supersedes the
  spec's "default unmuted / full volume"**: opening a fresh tab at full master gain "gives you a jump
  scare once it starts". It is a **master multiplier**, so it scales the per-track bases too — music
  loops and SFX are started at `0.1`, so a fresh tab now plays them at `0.02`. **A stored preference
  always wins**, so this never claws back a volume a player has already set.
- ⚠️ **Shipped reality differs from the spec:** the 3/2/1 proximity "coin" bleeps were **dropped**
  (Suno could not produce them) — **9 generated mp3s, not 12**, and no proximity audio. `error1.wav` was
  kept. Assets were generated **manually in the Suno UI** (the MCP ran out of credits) and
  post-processed with ffmpeg.
- ⚠️ Suno assets must be genuine 8/16-bit arcade chiptune, and loops must be **truly seamless** (ffmpeg
  `acrossfade` wrap).

## 6. Chat

One app-wide chat on **every page** (not embedded, not practice): a header button (sound → chat → Orbit →
wallet → profile) toggles a docked 340px desktop column that pushes the page and collapses to a 48px rail,
or a full-screen sheet on mobile. Tabs **GLOBAL | DMs | 🔔 notifications** (donations, trophies, refunds,
jackpot wins — [`profile-v2.md`](profile-v2.md)). The desktop open/collapsed/closed
state is remembered in `localStorage` (`space-stroker.chat-dock`). The game screen has no chat panel of
its own. Full rules: [`chat.md`](chat.md).

## 7. Money animations and misc

- **Money change animations** on balance/pot movements, plus a redesigned win modal.
- **Standardized scrollbars** across the app and LP (CSS-only).

## Key code touchpoints (`monkify-front`)

`src/styles/arcade.css` + `colors.css` + `tailwind.config.js` (tokens), `src/constants/icons.ts` +
`src/components/ui/AppIcon.vue`, `src/components/GameHeader.vue` (icon pad),
`src/components/game/{GameTerminal,GameConsole,GameMonkey}.vue`, `src/components/chat/*.vue`,
`src/components/game/ConsolePlate.vue` + `consolePlate.ts` + `assets/game/console/plate.png` (the shared
console plate, its lever and its well grid),
`src/components/GameBackground.vue` + `TorpedoSpawner.vue`, `src/hooks/{usePerfMode,useIsMobile,useChatUnread}.ts`,
`src/utils/walletDeepLink.ts`, `src/components/GameAudio.vue` + `src/assets/audios/`,
`src/components/game/GameMobileTabs.vue`, `src/hooks/useKeyboardDismiss.ts` (+ its three wiring
points: both `GameConsole.vue` input branches and `PracticeConsole.vue`'s mobile one),
`src/pages/PracticePage.vue` (the mobile column: the bubble's tier + anchor, `:sunk`, the cabinet's
geometry tokens) + `src/components/practice/{PracticeBubble,PracticeMonkey}.vue` (the wrapping action
row, the mobile-only type scale, and the sink on the wrapper `<div>`).
