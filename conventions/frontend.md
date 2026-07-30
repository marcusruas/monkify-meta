# Frontend conventions (`monkify-front`, Vue 3 + TS)

> Status: **stub** — to be fully drafted. Captures "how we write frontend code here".

## Patterns observed (fill in / verify)

- **Vue 3 with `<script setup>`** components; TypeScript throughout. Build via Vue CLI (webpack).
- **Composables live in `src/hooks/`** and are the primary state/logic unit (e.g. `useGame`,
  `useConnection`, `useOperator`, `useDisplayCurrency`). Several use **module-level `ref`s** so state
  is shared as a singleton across components — be careful with lifecycle/reset.
- **Shared types & enums live in `src/types/`** (`ActiveGame`, `BodyGameBet`,
  `OperatorBranding`, `DefaultResponse<T>`, …). Mirror backend enums exactly (codes must match).
- **API access via `src/services/mainService.ts`** — a single axios instance with a request
  interceptor that sets `baseURL` (`VUE_APP_API_BASE_URL`) and attaches the **`X-Launch-Token`**
  (from the `src/utils/launchToken.ts` store); a response interceptor routes any `401` to `/error`.
  Add new endpoints here and export them from the default object. (Only `401` is intercepted — a `429`,
  `5xx` or network error rejects and each caller handles it locally: a toast in `GameTypes.vue` /
  `GamePage.vue`, a silent swallow in `useDisplayCurrency`.)
- **Concurrent-request dedupe (`singleFlight`)** — `mainService.ts` holds a module-local
  `Map<string, Promise>`; while a request for a key is in flight every later caller receives **that same
  promise**, and the entry is deleted in `finally`. It is **not a cache**: nothing survives across time,
  so a caller can never be served a value fetched before it asked — which is what makes it safe near the
  money path. Keys are `method + path + deterministically-serialized params`; the params matter, because
  `getPlayerPreferences(wallet)` and `getPlayerPreferences()` resolve **different players** (connected
  wallet vs launch-token `sub`) and must never share a promise.
  **Applied to an explicit allow-list of five boot-path functions only** — `getGamesTypes`,
  `getPlayerPreferences`, `getFxRates`, `getOperator`, `getDefaultLaunchToken`. **Never wrap it around
  the interceptor or apply it to every GET:** `getTokenBalance(..., { live: true })` exists precisely for
  the reads that must not be stale (own bet, post-win settle, refund), and collapsing it into a
  concurrent poll would turn a rate-limit fix into a payout bug. Adding an endpoint to the list is a
  deliberate, reviewable line of code. Every waiter also shares the **same resolved object** — treat
  these responses as read-only, and clone before mutating. Shipped by Trello #104; see
  `specs/player-lockouts.md` §6.
- **Launch token:** `src/hooks/useLaunch.ts` captures the token from the launch URL fragment
  (`…#launch_token=<jwt>`), strips it from the URL, and persists it per-tab in `sessionStorage`
  (a fresh fragment overrides a stored one). In dev, tokens come from the Crypto/Regulated buttons on
  the `/space-stroker/game` landing — no silent auto-mint (see `features/dev-qol-stuff.md`).
- **Operator entry:** game routes are **param-free** (`/space-stroker/game/games`, `/space-stroker/game/play`);
  the operator is decoded from the token's `iss` (`getOperatorIdFromToken` in `utils/launchToken.ts`),
  not the URL. Missing/undecodable token, inactive operator, or any token `401` → `/error`.
  `useOperator.loadOperator()` ensures a launch token, fetches `GET /operators/{id}`, and applies
  `--color-secondary` when branding includes a color. Branding no longer carries a payment model —
  payment UX is driven by the **selected game's `paymentMethod`**, not the operator.
- **Wallet UX gated on the game's `paymentMethod`:** the game header (`GameHeader.vue`) shows the
  wallet cluster — the Connect Wallet button (`WalletButtons.vue`) **and** the `TokenBalance` — **only**
  when the current game is `SolanaToken` (`selectedGame?.paymentMethod === PaymentMethod.SolanaToken`),
  using the game's `tokenSymbol` / `tokenMintAddress`; `Standard` games and the games list hide
  it. `GameConsole.vue` requires a connected wallet to bet only for SolanaToken. The settings gear is
  gated on operator context from the launch token (`operatorId ?? getOperatorIdFromToken()`) so it
  survives an operatorId reset on a hard reload. See `features/payment-methods.md`.
- **Bet/pot display:** the play-page terminal (`GameTerminal.vue`) `formatAmount` branches on the
  game's method — **`SolanaToken`** → `{amount} {tokenSymbol}` (token decimals); **`Standard`** →
  `useDisplayCurrency.formatUsd` (preferred fiat with `≈` when not USD; silent USD fallback when rates
  unavailable). The preferred currency has no effect on SolanaToken amounts. See
  `features/payment-methods.md` + `features/player-customization-modal.md`.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`** in `useGame`: two hubs (`Games`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`; join per-game groups; handle `…/Terminal`, `…/DisplaySpeed`,
  `…/Status`, `…/Bets`, `GameReceived`, and `{gameId}/ChatMessage` (game chat — see
  `features/game-chat.md`).
- **Near-miss reveal ("Characters Away"):** a pure detector (`src/utils/nearMiss.ts`, KMP tail-prefix)
  plus a module-singleton composable (`src/hooks/useNearMiss.ts`) fed the raw `…/Terminal` batches from
  `useGame` (independent of the terminal crop). It tracks each of the player's **own** bet choices
  (`ownBetChoices`, recorded on successful bets) and exposes `nearMissEntries` — one closest-approach
  row per choice that `GameTerminal.vue` renders in a bottom-docked area with the in-game `> POT:`
  line. Timing (2 s stall-reset loop, win type-out) lives in the composable; the detector stays pure
  and side-effect-free. See `features/characters-away.md`.
- **Solana wallet** via `solana-wallets-vue` + `@solana/web3.js` + `@solana/spl-token`
  (`useConnection.sendToken`) — used **only for `SolanaToken` games**. `useGame.bet()` branches on
  `selectedGame.paymentMethod`: SolanaToken transfers to the game's `tokenReceiverPublicKey` /
  `tokenMintAddress` and posts the returned signature + wallet; Standard posts `{ amount, choice, seed }`
  with no wallet/signature (identity via the launch token `sub`). The `Bet.PaymentSignature` field is
  gone — the reference is carried as `ExternalTransactionReference` server-side.
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
- **Styling:** Tailwind CSS + DaisyUI. Operator theming overrides `--color-secondary` at runtime
  (see `useOperator.applyOperatorTheme`).
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
- **Two overlay tiers — this is deliberate, do not "consolidate" it away (decided 2026-07-29):** the app
  runs **two** modal treatments on purpose, chosen by what the overlay is *for*, not by who wrote it.
  - **System tier — neutral hairline.** Everything the player opens *themselves* and may open again and
    again: Settings, Provably Fair, Connect Wallet, errors/rejected-tx, and the side panels. Quiet
    chrome that recedes: 1px `--color-line` border, `--radius-arcade`, amber reserved for the primary
    action. This is the tier the Claude Design spec below describes.
  - **Tutorial / help tier — black + cyan frame.** Onboarding and how-to-play overlays: they appear
    **unprompted**, have to earn attention, and are **shown once then never again** (the Quick Tour,
    and anything reached from `How to play`). A louder CRT-cyan frame on near-black is correct here
    precisely *because* it does not have to live comfortably beside repeat-use chrome. The test is
    frequency, not subject matter: **Provably Fair is system tier even though it explains something**,
    because the player opens it themselves and reopens it every round.
  A future agent finding `QuickTourModal` styled differently from `GameFairModal` is looking at the
  rule, not a bug. What the tiers **do** share: size scale, titlebar/padding geometry, scroll-at-90dvh,
  dismissal contract (X / Esc / backdrop / primary), and a **dithered** backdrop rather than a blurred
  one (`backdrop-filter` is stripped under `html.light-graphics`).
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
  (including operator `--color-secondary`). Do **not** introduce ad-hoc inline styles, one-off
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
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
