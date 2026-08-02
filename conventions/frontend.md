# Frontend conventions (`monkify-front`, Vue 3 + TS)

> Status: **stub** — to be fully drafted. Captures "how we write frontend code here".

## Patterns observed (fill in / verify)

- **Vue 3 with `<script setup>`** components; TypeScript throughout. Build via Vue CLI (webpack).
- **Composables live in `src/hooks/`** and are the primary state/logic unit (e.g. `useGame`,
  `useConnection`, `usePlayerPreferences`). Several use **module-level `ref`s** so state
  is shared as a singleton across components — be careful with lifecycle/reset.
- **Shared types & enums live in `src/types/`** (`ActiveGame`, `BodyGameBet`,
  `DefaultResponse<T>`, …). Mirror backend enums exactly (codes must match).
- **API access via `src/services/mainService.ts`** — a single axios instance with a request
  interceptor that sets `baseURL` (`VUE_APP_API_BASE_URL`) and attaches **no auth header** (player
  endpoints are public; only `adminService.ts` sends a token). Add new endpoints here and export them
  from the default object. Nothing is intercepted by status any more — a `401`, `429`, `5xx` or network
  error rejects and each caller handles it locally (a toast in `GameTypes.vue` / `GamePage.vue`).
- **Concurrent-request dedupe (`singleFlight`)** — `mainService.ts` holds a module-local
  `Map<string, Promise>`; while a request for a key is in flight every later caller receives **that same
  promise**, and the entry is deleted in `finally`. It is **not a cache**: nothing survives across time,
  so a caller can never be served a value fetched before it asked — which is what makes it safe near the
  money path. Keys are `method + path + deterministically-serialized params`; the params matter, because
  `getPlayerPreferences(walletA)` and `getPlayerPreferences(walletB)` resolve **different players** and
  must never share a promise.
  **Applied to an explicit allow-list of boot-path functions only** — `getGamesTypes` and
  `getPlayerPreferences`. **Never wrap it around
  the interceptor or apply it to every GET:** `getTokenBalance(..., { live: true })` exists precisely for
  the reads that must not be stale (own bet, post-win settle, refund), and collapsing it into a
  concurrent poll would turn a rate-limit fix into a payout bug. Adding an endpoint to the list is a
  deliberate, reviewable line of code. Every waiter also shares the **same resolved object** — treat
  these responses as read-only, and clone before mutating. Shipped by Trello #104; see
  `specs/player-lockouts.md` §6.
- **Entry is unauthenticated.** Game routes are **param-free** (`/game`, `/game/play`) and carry no
  token: there is no launch token, no `useLaunch`, no
  `utils/launchToken.ts`, and no blanket `401 → /error`. The only fragment still handled is
  `#admin_token=<jwt>` on the game page, which hands off to the admin portal (`hooks/useAdmin.ts`).
- **Wallet UX is unconditional.** Every game settles in the one configured token, so the header wallet
  cluster (`WalletButtons.vue` + `TokenBalance`) uses the `tokenSymbol` / `tokenMintAddress` published
  on the game-type payload — there is no `paymentMethod` branch. `GameConsole.vue` requires a connected
  wallet to place a **paid** bet.
- **A write that proves wallet ownership must be signed.** `usePlayerPreferences.ts` is the pattern:
  fetch a purpose-scoped nonce (`GET /players/nonce`), build the canonical message **byte-for-byte
  identical** to the backend's `BuildMessage`, sign it with the connected wallet, then send
  `{wallet, …, nonce, signature}`. Drifting from the backend's string silently breaks every save.
- **Amounts are token-native, always.** `formatAmount` renders `{amount} {tokenSymbol}` at the token's
  decimals — there is no preferred display currency, no `useDisplayCurrency`, and no FX conversion.
  Pass it the amount, never an object: `formatAmount` reading a whole game/bet object is how
  `[object Object] USDC` reached the terminal.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`: one connection per tab, owned by `useGameHub`.**
  `src/hooks/useGameHub.ts` is the only module that builds a `HubConnection` — a single socket to the
  `Games` hub at `VUE_APP_WEBSOCKET_BASE_URL`, opened lazily on the first game load and **kept** across
  round rollovers, game-type switches and trips back to the lobby. A round change is a **group swap**
  (`attachToGame` → `JoinGame` + bind `{gameId}/…`; `detachFromGame` → unbind + `LeaveGame`), never a
  reconnect; `GameReceived` is bound **once** for the tab via `onBroadcast`. `useGame` consumes it and
  handles `…/Terminal`, `…/DisplaySpeed`, `…/Status`, `…/Bets` and `{gameId}/ChatMessage` (game chat —
  see `features/game-chat.md`). Rules when touching it: **unbind before joining** (a handler that
  outlives its round double-pushes bets and double-counts the pot), `onreconnected` re-joins **only**
  the current `gameId`, and `ensureConnected` is single-flight so a rollover racing a mount cannot open
  two sockets. `useGameHub` also arms/stops the **idle watch**, because the connection outlives
  `GamePage` and a page-scoped watch would leave a kept socket unpoliced in the lobby. See
  `specs/unified-websocket-connection.md`.
- **Near-miss reveal ("Characters Away"):** a pure detector (`src/utils/nearMiss.ts`, KMP tail-prefix)
  plus a module-singleton composable (`src/hooks/useNearMiss.ts`) fed the raw `…/Terminal` batches from
  `useGame` (independent of the terminal crop). It tracks each of the player's **own** bet choices
  (`ownBetChoices`, recorded on successful bets) and exposes `nearMissEntries` — one closest-approach
  row per choice that `GameTerminal.vue` renders in a bottom-docked area with the in-game `> POT:`
  line. Timing (2 s stall-reset loop, win type-out) lives in the composable; the detector stays pure
  and side-effect-free. See `features/characters-away.md`.
- **Solana wallet** via `solana-wallets-vue` + `@solana/web3.js` + `@solana/spl-token`
  (`useConnection.sendToken`). `useGame.bet()` transfers to the configured token's
  `tokenReceiverPublicKey` / `tokenMintAddress` and posts the returned signature + wallet; a free bet
  (amount 0) skips the transfer and posts anonymously. Server-side the reference is carried as
  `Bet.Signature`.
- **Web Workers** (`src/workers/`, first introduced by the FAIR panel's outcome replay): instantiate as
  a **classic** worker — `new Worker(new URL('@/workers/x.worker.ts', import.meta.url))` **without**
  `{ type: 'module' }`. Webpack emits the chunk as a plain IIFE bundle; asking for a module worker made
  the browser load the chunk (HTTP 200) and then die silently before `onmessage` ever ran. Always wire
  `onerror` as well as `onmessage` — a worker that fails to load must say so, not sit at 0% forever.
  **Anything sent through `postMessage` must be a plain object:** structured clone rejects Vue reactive
  Proxies with `DataCloneError: [object Array] could not be cloned`, so hold worker payloads in a
  `shallowRef` and/or send `toRaw(...)` with copied arrays. Cancel by `terminate()` rather than a
  cancel message (a message can be missed between ticks), and always terminate on unmount. Keep the
  algorithm in `src/utils/` and **import** it from the worker so the bundle carries one copy.
- **Overlays and the header's containing block (learned the hard way):** `GameHeader.vue`'s root
  carries `backdrop-blur-md`, and a `backdrop-filter` other than `none` makes an element the
  **containing block for `position: fixed` descendants** (same as `transform`/`filter`). So a
  `fixed inset-0` overlay authored **inside the header** resolves against the **64px header box**, not
  the viewport. Two consequences, and they pull in opposite directions:
  - **Modals must be teleported out** — `<Teleport to="#app">` (not `body`: `#app` keeps the app's
    styling/inheritance chain). `ArcadeModal.vue` does this unconditionally for every overlay, so
    individual modals no longer decide it. Their `z-modal` (60) beats `App.vue`'s root `z-app` (10),
    so they paint above everything below the blocking/legal layers — see the named z-index scale.
  - **Click-away backdrops must *not* be teleported** — they need to sit above the page but **below
    their own dropdown**, which is unexpressible once teleported: `App.vue` renders its root as
    `relative z-10` (its own stacking context), so the header's `z-40` is sealed inside it and a
    teleported sibling is compared against `10`. At `z ≥ 10` it covers the dropdown and swallows its
    clicks; below `10` it sits under the full-viewport app root and is never clicked. Use
    **`useClickOutside(target, handler, active)`** (`src/hooks/useClickOutside.ts`) instead — a
    document `click` listener registered only while open, with a containment check against a wrapper
    that holds **both** the toggle and the dropdown (so the opening click can't close it), removed on
    close and on unmount. Wired in `GameHeader.vue` (☰ menu) and `WalletButtons.vue` (wallet popover).
  - This class of bug is **invisible on phones and on hardware-acceleration-off desktops**, because
    `arcade.css` strips `backdrop-filter` under `html.light-graphics` — so always check a
    GPU-rasterizing desktop. See `specs/fair-modal-off-screen.md`.
- **Styling:** Tailwind CSS + DaisyUI. `--color-secondary` is a **static token** in `colors.css` —
  the runtime operator-branding override (`useOperator.applyOperatorTheme`) was removed with operators,
  so the value never changes at runtime.
- **`arcade.css` beats Tailwind utilities — do not fight it with classes (learned the hard way):**
  `src/main.css` `@import`s `colors.css` + `arcade.css` **after** `@tailwind utilities`, and the arcade
  material classes are plain single-class rules (deliberately **not** in `@layer components` — Vue CLI's
  css-loader runs Tailwind's PostCSS plugin on each `@import`ed file standalone, where a bare
  `@layer components` has no matching `@tailwind components` directive and **fails the build**). Equal
  specificity + later in the cascade means **`.btn-arcade` wins over any utility you stack on it**:
  `justify-start` loses to its `justify-content: center`, `py-2.5` loses to `.btn-arcade--sm`'s
  `padding: 0 1em`, `gap-2` loses to its `gap: 0.6ch`. Only `!important` utilities (`!h-auto`) win.
  So: use `.btn-arcade` **as-is** when you want a standard arcade button, and build with **plain
  Tailwind** when you need a differently-shaped control — do not half-adopt the class and try to
  override its box. `GameMobileTabs.vue` (segmented control) and the `GameConsole` mobile preset keypad
  both take the plain-Tailwind route for exactly this reason. Reach for the arcade **tokens**
  (`bg-primary`, `text-terminal-light`, `border-terminal-border`) to stay on-system either way.
  See `specs/mobile-preset-keypad.md` → §7.3.
- **Two panels — grey and black (Trello #124, 2026-08-02):** the design source is the Space Stroker
  modal design doc (`Space Stroker Modals.dc.html`, chrome direction **1b hairline**, adopted). It
  defines **two skins over one geometry**.

  **The names are `grey` and `black`** — the prop value (`variant="grey" | "black"`), the CSS modifier
  (`.dialog--grey` / `.dialog--black`, `.arcade-frame--grey`) and the word to use in review. The
  design doc calls them "tier 1" and "tier 2"; **we deliberately do not.** A tier name encodes a
  ranking nobody remembers six months later, and it is exactly how the previous KB ended up
  documenting a "tutorial tier" that no longer matched the code. A colour name cannot rot.

  **HOW TO CHOOSE (owner's rule, 2026-08-02) — two axes, and black needs BOTH:**

  1. **Interaction.** Is the player *doing* something here — typing, choosing, confirming, filtering,
     signing? → **grey**. Is it purely something to *read*, and short? → black is available.
  2. **Tone.** Is this serious — money, identity, compliance, a failure, a broken connection? →
     **grey**. Is it light — teaching, welcoming, celebrating? → black is available.

  **Grey is the default and the safe answer. A surface only goes black when it is BOTH
  informative-only-and-brief AND light in tone.** Requiring both is what keeps the set small and
  keeps the meaning legible: a dropped-connection notice is short and informative but *serious*, so
  it stays grey; a long provably-fair explainer is informative but *not brief*, so it stays grey.
  Today the quick tours are the only black surfaces in the app, and that is the intended density —
  if black starts showing up on a third and fourth surface, the rule is being read too loosely.

  ⚠️ The "needs BOTH" tiebreak is this document's reading of the two axes above, not something the
  owner stated in those words. If a future surface makes it feel wrong, change it here rather than
  quietly making an exception at the call site.

  | | **GREY** | **BLACK** |
  | --- | --- | --- |
  | The player is | doing something — typing, choosing, confirming, signing | reading something short |
  | Tone | serious: money, identity, compliance, failures | light: teaching, welcoming, celebrating |
  | Frame | 1px `--color-line` hairline | **2px `--color-cyan`** + a cyan glow |
  | Fill | `--color-surface` (lifted) | `--color-void-deep` (below the void) |
  | Titlebar | title **left**, amber, behind a 6px amber square, on a sunken `--color-surface-bar` | title **centred**, cyan, between two pixel sparkles, on the panel's own fill |
  | Footer | right-aligned button pair on the sunken bar | one centred amber ghost button |
  | Intent | it recedes | it earns the attention it takes |
  | Today | everything else | the quick tours, and nothing else |

  **Shared by both, by construction in the shell:** sizes `compact` 380 / `standard` 520 / `wide` 720
  (mobile 100%), titlebar 36px desktop / 44px mobile, 18px body, 12–14px footer, 4px radius, a 90dvh
  cap with the body scrolling and both bars pinned, the dither backdrop, and dismissal by X / Esc /
  backdrop / the primary button.

  - **Where each one landed, and why** — the rule applied to what shipped:
    | Surface | Panel | Which axis decided it |
    | --- | --- | --- |
    | Settings, wallet picker, name prompt, Responsible Gaming | grey | interactive **and** serious |
    | 18+/Terms gate | grey | compliance — the most serious surface in the app |
    | Provably Fair | grey | informative, but nowhere near brief |
    | Idle / boot-recovery / relaunching | grey | brief and informative, but a broken connection is **not** fun — tone breaks the tie |
    | The quick tours | **black** | short, purely to read, and light in tone — the only surface that clears both |
  - **Cyan stays scarce on a grey panel: at most ONE element carries it.** In practice that is the
    keyboard focus ring, `TRY IT FREE` on the dashboard, and a verified/revealed value in a readout.
    Documented future exception: the How to play guide page may use cyan on its tip/callout blocks.
    On a **black** panel cyan is the frame itself, so the rule does not apply — the accent inside it
    is amber (badges, step labels, the action).
  - **The anatomy lives in `arcade.css` under "THE DIALOG SHELL"**, not in Tailwind utilities in the
    component — see the cascade rule above; a dialog built from utilities could not be overridden by
    `arcade.css` anyway, and one place holding the whole anatomy is what stops the drift that filed
    this card. `ArcadeModal.vue` is the **only** component that writes `.dialog*` structure classes.
  - **`.btn-dialog` inside a dialog, `.btn-arcade` everywhere else.** The dialog button is flat —
    1px, 4px radius, Press Start 2P 8px, 10/13px padding, hover to amber, a 2px cyan focus ring,
    `.35` disabled — because `.btn-arcade`'s 3rem bevelled body with a 5px amber key-drop is the
    cabinet's furniture and reads as the only 3D object inside a 36px-titlebar hairline dialog. Do
    **not** restyle `.btn-arcade` to match: it is on the lever, the wallet cluster, the dashboard and
    the cards. `PrimaryButton` / `SecondaryButton` are likewise not dialog controls.
  - **Sizes: `compact` 380 · `standard` 520 · `wide` 720 · `xwide` 1024.** The design doc draws the
    first three; **`xwide` is ours** (2026-08-02) — the "big modal on PC" size, added because the
    5-step quick tour lays five illustrated steps out side by side and at 720 each column is ~128px,
    which shrinks the art to a stamp and wraps every label. **Keep it rare.** If a surface reaches for
    `xwide` and is not a wide illustrated row, it probably wants a page, not a dialog. Note that a
    consumer choosing a size is also choosing its mobile treatment: `wide` and `xwide` go **full
    screen** below `lg`, `compact` and `standard` become bottom sheets.
  - **`.arcade-panel` is the cabinet, not a content surface.** Its amber bevel and key-drop are the
    material the machine is *made of*. Nothing content-shaped should use it.
  - **Named exceptions — a future agent finding these different is looking at the rule, not a bug:**
    the game page's own furniture (`GameTerminal`, `GameConsole`, `GameTyper`, `GameMonkey`, the
    lever, Edson) and **the desktop docked side-panel cabinet** (metal bezel + rivets + recessed CRT —
    the same cabinet language, on the most-viewed screen); and **`GameTypeCard`**, whose deliberate
    three-way archetype accent (race → primary, preset → cyan, lottery → magenta) knowingly
    contradicts the one-accent rule.
  - **Pages and sections use `.arcade-frame`**, the same flat hairline family — `.dialog--grey` is
    its overlay member, with the sunken bars a page has no use for. Two fills exist and **which one a
    surface takes is a recorded decision, not a free choice**:
    - `.arcade-frame` alone — near-black. `/`'s `HomeSection`, `/error`, `/soon`, and the side
      panel's mobile chat container.
    - `.arcade-frame .arcade-frame--grey` — the grey panel's `--color-surface` on a 1px hairline, so
      the page reads as part of the dialog family. **`/game/history` and `/game/stats` only**
      (owner's call, 2026-08-02).

    ⚠️ These two lists disagree on purpose. The owner was shown that splitting them recreates the
    `/` vs `/game/stats` mismatch decision 3 was written to close, and chose this scope anyway. **Do
    not unify them without asking** — a future agent finding them different is looking at the
    decision, not at a bug.
- **One named z-index scale — and the teleport rule that makes it mean anything (Trello #124):**
  the values live as `--z-*` custom properties in `colors.css` and are exposed as Tailwind utilities
  (`z-app`, `z-modal`, …) via `theme.extend.zIndex`. **Never write a literal `z-*` on a root-level
  fixed overlay again.**

  ```
  INSIDE the app root (composited at 10):   base 0 · app 10 · furniture 20 · header 40 · popover 50
  TELEPORTED siblings of the app root:      modal 60 · blocking status 70 · legal gate 80
                                            toast 9999 (vue3-toastify, library-owned)
  ```

  The mechanism matters more than the numbers: `App.vue`'s root is `relative z-app` — **its own
  stacking context** — so **anything rendered inside it can never paint above a teleported modal,
  whatever z-index you give it.** The idle overlay at `z-40` was not losing to `z-[60]`; its entire
  subtree was composited at `10`. **Teleporting it, not renumbering it, was the fix.** Therefore:
  **every value ≥ 60 must be `<Teleport to="#app">`** (never `body` — `#app` keeps the app's style
  inheritance chain). The legal gate sits on top of everything, which is a compliance requirement.
- **All overlays go through `ArcadeModal.vue` (`src/components/ui/`) — do not hand-roll another one:**
  it owns the whole contract, so surfaces cannot drift apart again.
  - **Props:** `variant` (`grey` default / `black`), `size`, `layer`, `dismissible`, `chrome`,
    `footerLayout`. Slots: `default` (body, exposes `titleId`), `strip`, `footer`, `titlebar-actions`.
  - **Geometry:** four size tokens (`compact` 380 / `standard` 520 / `wide` 720 / `xwide` 1024);
    titlebar 36px
    desktop, 44px below `lg`; body padding 18px; footer 12–14px; 4px radius; `max-h-[90dvh]` with the
    body as the internal scroll container. **No consumer declares its own max-width or max-height.**
  - **Status strip:** a full-bleed `ERR` (25° hue) or `OK` (205° cyan) bar between the titlebar and
    the body, outside the body's padding and its scroll, so a failure or a verification result stays
    visible while the body scrolls under it. **Failures go here, not into a red line in the body.**
  - **Backdrop = `.overlay-backdrop`: void at 55% + a 9px 45° dither. NEVER `backdrop-filter`** — it
    is stripped under `html.light-graphics`, which every phone runs, so a blurred backdrop degrades
    invisibly to a flat wash.
  - **Dismissal:** X, Esc and backdrop all produce the **same** outcome and fire the same `close`
    event. (`DisplayNamePrompt` routes all three to `skip()`, because skipping is the recorded "asked
    and declined" outcome and a dismissal must stick.) **`dismissible: false` is a first-class mode**,
    not a per-component escape hatch — `LegalGate`, `GameIdleOverlay`, `BootRecovering` and
    `EmbedRelaunching` use it. **Blocking overlays must never gain Esc or backdrop dismissal**;
    dismissing the 18+/Terms gate by Esc is a compliance bug, not a UX improvement.
  - **Esc routing is stacked, not per-component** (`useOverlayStack.ts`): one document listener, and
    only the **topmost** overlay sees the key. A blocker opening over a modal therefore *swallows*
    Esc rather than closing the modal sealed underneath it — which a per-component
    `window.addEventListener` cannot express, because every mounted listener fires.
  - **A11y package:** `role="dialog"`, `aria-modal="true"`, `aria-labelledby` wired to the shell's
    generated `titleId` (exposed as a slot prop, so `chrome="none"` consumers point it at their own
    body heading), focus trap on the topmost overlay only (`useFocusTrap.ts`), **safe** focus restore
    (the trigger is often gone — the wallet modal's lives in a popover that closed — so restore falls
    back to `#app` and never throws), and a count-based body scroll lock (`useBodyScrollLock.ts`,
    `overflow` on `<html>`, never `position: fixed` on `<body>`).
  - **Mobile: a bottom sheet below `lg` (1023px)** — full width, `90dvh`, internal scroll, **no
    drag-to-dismiss**. Reuses the existing `useIsMobile()` / `max-lg:` boundary; no new breakpoint.
  - **Build it with plain Tailwind + arcade tokens**, never by adopting `.arcade-panel`/`.btn-arcade`
    and overriding their box — see the cascade rule above.
- **Design workflow (required for any new visual surface):** a spec describes *behavior*; it does not
  describe *design*. Building a component straight from spec prose reliably produces something that
  passes its acceptance criteria and still looks wrong. So, before writing markup for a new
  component/modal/page:
  1. **Read the card's mockups** — Trello attachments are the design source of truth and outrank the
     spec's prose description of the UI. They need the user's Trello session, so open them with
     **`claude-in-chrome`**; the `chrome-devtools` automation profile is not signed in and will bounce
     to an error page.
  2. **Run the `impeccable` skill** (`craft` for new surfaces, `critique` / `polish` for existing ones)
     and apply its design rules. It will report **`NO_PRODUCT_MD`** and ask to run its `init` interview
     — **skip that**: this workspace deliberately keeps no `PRODUCT.md` / `DESIGN.md` (decided
     2026-07-29). The design source is the card's mockups plus the arcade tokens already committed in
     `colors.css` / `arcade.css`.
  3. **Screenshot-compare** the running surface against the mockup at desktop *and* phone widths before
     hand-off. Looking at it is the step that catches what tests never will.
  ⚠️ Match the mockup's **design**, but take **copy** from the card's refinement decisions — a mockup
  routinely carries wording that refinement later rejected (the quick-tour mockup says *"Pick an animal
  number (1-8)"*, which decision #4 on #115 explicitly killed).
  Wired into the pipeline as `monkify-implement-feature` → Step 6b.
- **Design system (required):** **always build new UI with the existing frontend design system** —
  Tailwind CSS + DaisyUI plus the shared UI components under `src/components/ui/`
  (e.g. `PrimaryButton`, `SecondaryButton`) and the established layout/spacing/theming patterns
  (including `--color-secondary`). Do **not** introduce ad-hoc inline styles, one-off
  component libraries, or bespoke controls when a design-system primitive already exists; reuse and
  extend the existing components so new screens (modals, headers, forms) match the current look and
  feel. If a needed primitive is missing, add it to the design system rather than styling in place.
- **Responsive / mobile layout (`< lg` / 1024px):** the app is desktop-first; below `lg` a shared
  **`useIsMobile()`** composable (`src/hooks/useIsMobile.ts`, `matchMedia('(max-width: 1023px)')`) gates
  mobile structure. This reuses the `GameMonkey hidden lg:block` boundary and covers landscape phones,
  so portrait + landscape share one layout. The lobby grid is already 1-col below `md`; the game screen
  swaps its desktop three-sibling row for a **`GAME | CHAT·STATS` segmented view** (`GameMobileTabs`)
  whose two views are **absolutely stacked** (only the active one visible + interactive, the hidden one
  `invisible pointer-events-none`) so a hidden view can't intercept taps, with both kept mounted so chat
  keeps its subscription. Mobile SolanaToken bets use an "open in wallet app" deep link
  (`src/utils/walletDeepLink.ts`). Prefer `max-lg:`/`lg:` Tailwind variants + `dvh` heights over new
  breakpoints. See `features/mobile-layout.md`.
- **Routing:** `src/routes.ts`, **prefix-free** since `home-dashboard` — `/` (`Home`, the starting page)
  · `/game` (the plain lobby, and the URL `EMBEDDING.md` points integrators at) · `/game/play` ·
  `/game/history` · `/game/stats` · `/admin` · `/wallet-bridge` · `/background` · `/soon` · `/error`.
  Two catch-alls, and they coexist by vue-router **specificity ranking, not declaration order**: a
  **strip-prefix shim** `/space-stroker/:pathMatch(.*)*` → the stripped path **preserving query and
  hash** (it exists because a mobile wallet deeplink hop straddling a deploy returns a player to the old
  path mid-bet), and a **generic `/:pathMatch(.*)*` → `/error`**, so an unmatched path is never a blank
  page. **Embedded `/` redirects to `/game`** — `HomePage` never mounts in a frame. Navigate by **route
  name**, never by literal path. See `features/home-dashboard.md`.
- **Transient vs terminal failures on the boot path (required):** a failed boot call must be
  **classified** before it decides anything. `src/utils/transientError.ts` → `isTransient(error)` is the
  single rule: **transient = no response at all, or 429 / 5xx / 408**; terminal = any other 4xx that
  *has* a response, plus a malformed/absent token. ⚠️ **"No response" is the important arm, not a
  fallback.** Cloud Armor denies at the edge *before the app runs*, so its 429 carries no CORS headers,
  the browser blocks it, and axios reports `error.response === undefined` — a classifier written as
  `status === 429 || status >= 500` **never fires on the real rate-limit case** (this is exactly why
  the 2026-07-26 lockout was reported as "a CORS error"). Transient failures go through
  `useBootRecovery().runWithRecovery(task)`, which retries on a **jittered schedule whose total
  deliberately exceeds Armor's 60 s enforcement interval** (`utils/retrySchedule.ts`) behind a shared
  `> RECONNECTING…` state, then rethrows; terminal failures rethrow immediately. Two rules when
  touching this: **check the jitter floor, not the mean** (the first draft's `[5,20,45]` ±20 % bottomed
  out at 56 s, under the window it exists to cross), and `runWithRecovery` must go **outside**
  `singleFlight`, never inside — inside, concurrent callers keep the original rejected promise while
  the retry succeeds. Absorbed errors are marked `handled = true` so `services/handlers.ts` doesn't
  toast once per retry. See `specs/entry-path-recovery.md`.
- **Non-component code navigates via `services/navigation.ts`, never `window.location.assign`.** The
  router is created in `main.ts` and not exported, and `main.ts` transitively imports `mainService`, so
  importing it back is a cycle; `setNavigationRouter(router)` hands it over once and `navigateToError()`
  uses it. This is not cosmetic — a full document reload destroys any in-flight boot recovery (module
  state) and re-spends the whole boot request budget against the limit that caused the failure.
- **`/error` is never a dead end.** `ErrorPage.vue` always offers RETRY, whatever routed the player
  there. The button is unconditional on purpose: its presence must not become a tell about *why* the
  game is unavailable (`constraints.md` §6 — expired, inactive and not-found all render identically).
- **A wallet deeplink return does not say WHICH request it answers.** `signAllTransactions` (a bet) and
  `signMessage` (a self-exclusion) come back with the **same** `data`+`nonce` params and no encryption
  key, so `detectDeeplinkReturn` classifies both as `'sign'`. What the app **stashed in localStorage
  before leaving** is the only thing that tells them apart — a pending bet vs a pending
  self-exclusion, in separate keys, each tried in turn on the way back. Never try to infer the hop from
  the return params. See `features/legal-docs-self-exclusion.md`.
- **Cross-host state between `www.` and `app.` must be a cookie on `.spacestroker.com`, never
  localStorage** — they are different origins. Both `ss_consent` (analytics) and `ss_terms` (18+/Terms)
  do this, and both **omit the `domain` attribute off our own hosts**: a domain the browser refuses
  writes no cookie at all, so the prompt would return on every single visit. Store a **version string**
  rather than a boolean when you may need to re-ask later.
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
