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
- **Entry is unauthenticated.** Game routes are **param-free** (`/space-stroker/game/games`,
  `/space-stroker/game/play`) and carry no token: there is no launch token, no `useLaunch`, no
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
    styling/inheritance chain). `GameFairModal.vue` and `PlayerCustomizationModal.vue` both do this.
    Their `z-[60]` / `z-50` beat `App.vue`'s root `z-10`, so they paint above everything.
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
- **Routing:** `src/routes.ts` — `/space-stroker/game/games`, `/space-stroker/game/play`, `/error`;
  `/space-stroker/game` is the dev launch landing (gated by `VUE_APP_DEV_LAUNCH`; prod → `/error`); root
  `/` redirects to `/error`.
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

## Two overlay tiers

Overlays are **not** all meant to look alike. There are two deliberate tiers, and the test that picks
between them is **frequency, not subject matter**.

- **System tier — neutral hairline.** Overlays the player opens *themselves* and reopens constantly:
  Settings, Provably Fair, Connect Wallet, the **Orbit wallet modal**, the **header balance cluster**,
  the side panels, and the error/history/stats surfaces. Chrome that recedes:
  - sizes: compact **380** · standard **520** · wide **720** · mobile **100%**
  - titlebar **36px** desktop / **44px** mobile; body padding **18px**; footer **12-14px**
  - border **1px `--color-line`**, radius **4px**
  - backdrop: **void at 55% + a 9px diagonal dither**
  - **Esc** closes, and so does a backdrop click
- **Tutorial / help tier — black + a 2px CRT-cyan frame.** Overlays that appear **unprompted**, have to
  earn attention, and are shown **once and then never again**: the Quick Tour / How to Play, and the
  first-win Orbit upsell.

Provably Fair is *system* tier even though it explains something, because it is reopened every round.
The Quick Tour is *tutorial* tier even though it is small, because it arrives uninvited.

Two rules that are easy to get wrong and expensive to debug:

- **Dither, never blur.** `backdrop-filter` is stripped under `html.light-graphics`, which `usePerfMode`
  forces on **every phone**, so a blurred backdrop silently degrades to nothing on mobile.
- **Teleport to `#app`.** `GameHeader`'s root carries `backdrop-blur-md`, which makes it the containing
  block for `position: fixed` descendants — an overlay authored inside the header resolves `inset-0`
  against the 64px header strip and renders half off-screen. That is the shipped `fair-modal-off-screen`
  bug; see the note further up about click-away backdrops, which for the same reason **cannot** be
  teleported and use `useClickOutside` instead.

### `useClickOutside` is for DROPDOWNS, not modals

`useClickOutside(target, handler, active)` requires `target` to wrap **its own toggle button** — the
header menu (`GameHeader`) and the wallet popover (`WalletButtons`) both do, and its doc comment says
so. A **modal** cannot: its trigger lives elsewhere (a menu item, a header button), so the click that
OPENS it lands outside the panel and dismisses it in the same click. The modal appears never to open.

It really does fire in time. A microtask checkpoint runs **between event-listener invocations**, so
Vue's watcher attaches the `document` listener before the opening click finishes bubbling to
`document`. Reasoning "the watcher is a microtask, so it runs after the event" is wrong, and cost a
debugging session on `OrbitModal`.

**Every modal in the app dismisses with an explicit backdrop element** instead:

```html
<div class="absolute inset-0 …" @click="emit('close')"></div>
<div class="relative z-10 …"><!-- panel, above the backdrop --></div>
```

See `GameFairModal.vue`, `PlayerCustomizationModal.vue`, `OrbitModal.vue`.

There is no shared shell component yet. `OrbitModal.vue` hand-rolls this chrome and is the reference
implementation a future shared shell should be extracted from.

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
