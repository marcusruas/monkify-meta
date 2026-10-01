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
  from the default object. Nothing is intercepted **by status** — a `401`, `429`, `5xx` or network error
  rejects like any other; there is no blanket `401 → /error` on `mainAPI` (that belongs to
  `adminService.ts`).
- **Error copy comes from the API, through one reader (`utils/apiError.ts`).** Every rejection carries
  the envelope `{ data, messages: [{ type, value }] }`, and `apiErrorMessage(err)` is the **single**
  place in `src/` that reads it — five hand-rolled copies were folded into it by #158. It prefers the
  first `type === 2` entry with a usable `value`, then the first usable entry of any type, then `null`.
  ⚠️ **`type` is NUMERIC** — `Informational 0 / Alert 1 / ValidationFailure 2 / Error 3`, and the API has
  no `JsonStringEnumConverter`. Two screens filtered `type === 1` under a `/* ValidationFailure */`
  comment; that is **Alert**, so they never matched. Use `2`.
- **`error.message` is never shown to a player.** On an AxiosError it is the status line
  ("Request failed with status code 400") — it goes to `console.error` and nowhere else.
  `services/handlers.ts` toasts `apiErrorMessage(error) ?? API_ERROR_FALLBACK`
  (`Something went wrong — please try again.`), keyed by the message text as its `toastId` so identical
  failures refresh one toast instead of stacking. A surface that renders the failure **itself** uses
  `displayableErrorMessage(err, fallback)`, which keeps a genuinely thrown sentence (a wallet that
  cannot sign) while still blocking status lines — the presence of `response` separates the two.
- **If you report a failure yourself, mark it `handled`.** `err.handled = true` short-circuits the
  interceptor's toast, and the 200 ms `setTimeout` in `handlers.ts` exists precisely so a caller's
  `catch` runs first. Since the toast now carries real copy, a site that also shows its own toast or an
  inline error line **must** set the flag or the player reads the same sentence twice
  (`useGame`'s game-data load, `useTokenBalance` on **both** branches, `DisplayNamePrompt`,
  `PlayerCustomizationModal`, `ResponsibleGamingPage`, plus the pre-existing `GamePage`, `GameTypes`,
  `useBootRecovery`, `useOrbit*`, `OrbitTelegramTab`).
  ⚠️ The counter-example is deliberate: **`useGame`'s `submitBet` catch is NOT marked** — its terminal
  line (`> There was an error processing your bet`) carries no reason, so the interceptor toast is the
  only explanation the player gets. See `features/api-error-messages.md`.
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
  cluster uses the `tokenSymbol` / `tokenMintAddress` published on the game-type payload — there is no
  `paymentMethod` branch. `GameConsole.vue` requires a connected wallet to place a **paid** bet.
  ⚠️ Since #151 the cluster is **two balance-carrying pills** (`WalletButtons.vue` amber +
  `OrbitLauncher.vue` violet); `TokenBalance.vue` is deleted and the balance lives in the
  **`useTokenBalance` module singleton**, which is the app's only `getTokenBalance` call site. The
  header **owns the mint** and pushes it in with `setMint()`.
- **A money figure that moves is rendered by `MoneyValue.vue`, never by hand.** One component owns the
  roll, the `± ` chip, the credit-green/debit-dim-ink colours, the queue and the reduced-motion branch;
  the **formatter is injected** per call site. A change animates **only when it is attributed** — see
  [`../features/money-changing-animations.md`](../features/frontend-ux.md). Do not add a
  second animated readout, and do not animate a figure the player is currently typing into.
- **A write that proves wallet ownership must be signed.** `usePlayerPreferences.ts` is the pattern:
  fetch a purpose-scoped nonce (`GET /players/nonce`), build the canonical message **byte-for-byte
  identical** to the backend's `BuildMessage`, sign it with the connected wallet, then send
  `{wallet, …, nonce, signature}`. Drifting from the backend's string silently breaks every save.
- **Amounts are token-native, always.** `formatAmount` renders `{amount} {tokenSymbol}` at the token's
  decimals — there is no preferred display currency, no `useDisplayCurrency`, and no FX conversion.
  Pass it the amount, never an object: `formatAmount` reading a whole game/bet object is how
  `[object Object] SOL` reached the terminal.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`: one connection per tab, owned by `useGameHub`.**
  `src/hooks/useGameHub.ts` is the only module that builds a `HubConnection` — a single socket to the
  `Games` hub at `VUE_APP_WEBSOCKET_BASE_URL`, opened lazily on the first game load and **kept** across
  round rollovers, game-type switches and trips back to the lobby. A round change is a **group swap**
  (`attachToGame` → `JoinGame` + bind `{gameId}/…`; `detachFromGame` → unbind + `LeaveGame`), never a
  reconnect; `GameReceived` is bound **once** for the tab via `onBroadcast`. `useGame` consumes it and
  handles `…/Terminal`, `…/DisplaySpeed`, `…/Status` and `…/Bets` per round. **Game chat is a separate
  channel keyed by game type** (`attachToChat(gameTypeId)` → `JoinChat` + bind `{gameTypeId}/ChatMessage`;
  a call for the room already joined is a no-op, so rollover leaves chat alone; `onreconnected` re-joins it
  and fires `onChatRejoined` so `useGame` backfills history — see `features/game-chat.md`). Rules when
  touching it: **unbind before joining** (a handler that
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
  padding, `gap-2` loses to its `gap: 0.6ch`. Only `!important` utilities (`!h-auto`, `!text-primary`)
  win.
  So: use `.btn-arcade` **as-is** when you want a standard arcade button, and build with **plain
  Tailwind** when you need a differently-shaped control — do not half-adopt the class and try to
  override its box. `GameMobileTabs.vue` (segmented control) and the `GameConsole` mobile preset keypad
  both take the plain-Tailwind route for exactly this reason. Reach for the arcade **tokens**
  (`bg-primary`, `text-terminal-light`, `border-terminal-border`) to stay on-system either way.
  See `specs/mobile-preset-keypad.md` → §7.3.

  ⚠️ **This is not theoretical — #135 found SIX stacks that had shipped doing nothing**, four of
  them user-visible. Recorded so the failure mode is recognisable, not re-derived:
  1. `PrimaryButton`'s `asLink` → `<a>` branch — `false` at every call site (dead code, not cascade).
  2. `WalletButtons`' `size === 'sm' ? 'text-xs …' : 'text-sm'` — **both** lose to `.btn-arcade`'s
     `font-size`. Its `md:w-[200px]` sibling, by contrast, **works**: `.btn-arcade` declares no
     `width`, so there is no tie to lose. *"A utility on an arcade class"* is not the test — *"does
     the class declare that property"* is.
  3. `/responsible-gaming`'s duration `<select class="… text-primary">` — the input class sets
     `color`, so a compliance-surface control **written to be amber shipped rendering in ink**.
  4. The Settings **nonce** readout (`.dialog-field border-0 bg-transparent p-0 text-cyan`) — all
     four utilities lost; it shipped as a bordered ink box instead of a borderless cyan value.
  5. The Settings **client seed** (`.dialog-field … text-primary`) — #3's twin, same ink render.
  6. **The subtlest one, found during #135's own QA:** the Settings custom-seed row's
     `basis-full sm:basis-auto`. Nothing "loses" here — `flex-basis` computes to `auto` exactly as
     written — but **`auto` defers to the item's own `width`**, and the input class declares
     `width: 100%`, so the flex base size was the whole row and SET / ROTATE wrapped underneath it
     **on desktop too**. The row the card was filed to align had never been a row. Fixed with
     `sm:basis-0` — `flex-basis` and `width` are different properties, so there is no tie to lose.
     ⚠️ **The lesson generalises:** a utility can be applied *and still not do what it was written
     for*, if the value it sets defers to a property the class owns. `basis-auto`, `w-auto`,
     `h-auto`, `min-h-0` and `max-w-none` are all of this shape.

  **The fix is `!` at the call site (3 and 5), the declaration moving into `arcade.css` (2 and 4), or
  a property the class does not own (6).**
  ⚠️ And it bites *while you fix it*: adopting `.btn-dialog` on the games-history `MINE ONLY` toggle
  would have silently killed its `border-primary text-primary bg-primary/10` active state, which is
  why that state is now the `.btn-dialog--active` **modifier** and the shrink-to-fit width on the
  history/stats selects is `.input-arcade--auto` rather than a `w-auto` utility.
- **ONE control-sizing scale — height comes from a token, skin stays per family (Trello #135,
  2026-08-02):** before this, no button or input class declared a `height` at all; each one's height
  fell out of its vertical padding plus its font-size, so two controls in the same row were only ever
  the same height by coincidence. They were not: the Settings seed row shipped a 44.7px input beside a
  33.2px button. **Every in-scope family now takes `height` from one of three steps**, so any button
  and any input in a row line up **by construction, not by hand-tuned padding at the call site**.

  **The tokens live in `colors.css`** beside `--radius-arcade`, and **one** `@media (max-width: 1023px)`
  `:root` block steps the two lower rungs up — **no call-site class changes on mobile, ever**:

  | Token | Desktop | `≤1023px` | Anchored on |
  | --- | --- | --- | --- |
  | `--ctl-h-sm` | 34.4px (`2.15rem`) | **40px** | `.btn-arcade--sm`, unchanged |
  | `--ctl-h-md` | 40px | **44px** (**36px inside `.app-header`** — see the header opt-out below) | the new default rung |
  | `--ctl-h-lg` | 48px (`3rem`) | 48px | `.btn-arcade`, unchanged |
  | `--ctl-px-sm` / `md` / `lg` | 10 / 13 / 16px | same | horizontal padding only |

  `sm` and `lg` are **pinned to the arcade values that already shipped**, so the lever, the wallet
  cluster, the dashboard and `GameTypeCard` did not move a pixel and QA concentrated on dialogs and
  pages.

  **Family defaults — the common case is one class and no `px-`/`py-`:**

  | Class | Step | Skin it keeps |
  | --- | --- | --- |
  | `.btn-arcade` | `lg` 48 | 2px border, bevel, 5px amber key-drop, press dip |
  | `.btn-arcade--sm` | `sm` 34.4 | same |
  | `.btn-dialog` (+ `--primary`, `--active`) | `md` 40 | flat 1px hairline |
  | `.input-arcade` (+ `--select`, `--auto`) | `md` 40 | inset fill |
  | `.dialog-field` | `md` 40 | inset fill, centred by `line-height` |
  | `.btn-icon` | `md` 40 square | **none** — geometry only |

  Rules that go with it:
  - ⚠️ **THE HEADER BAR OPTS OUT OF THE `md` MOBILE STEP-UP (owner's call, 2026-08-13).** `.app-header`
    re-declares `--ctl-h-md: 2.25rem` (**36px**) inside the same `@media (max-width: 1023px)` block in
    `colors.css`, so below `lg` the two doors (`OrbitLauncher` + the connected wallet pill) and the two
    `.btn-icon` squares (audio, ☰) are **36px, not 44px**. Reason: the bar itself is only **54px** with
    4px of padding — 46px of usable box — so a 44px control fills it wall to wall and the cluster read
    as "too fat, too much Y padding". **It is delivered as the TOKEN on `.app-header`, not as
    per-class heights**, which is what keeps the two families in that bar the same height by
    construction, and it is inherited **only** by the header subtree: every dialog field, page input
    and dialog button keeps the 44px thumb rung, and the modals mounted in `GameHeader.vue`'s template
    are `<Teleport to="#app">`, so they leave the subtree before they could inherit it. The touch
    target is trimmed **vertically only** — the pills stay as wide as their contents. If you add a
    control to the bar, this is why it is 36px there and 44px everywhere else.
  - ⚠️ **`.btn-arcade` ships NO `:disabled` rule, and there is no muted variant** beside `--primary` /
    `--cyan` / `--ghost`. A bare `.btn-arcade[disabled]` therefore renders as **transparent text that
    still brightens on hover and still dips 3px on press** — a control that animates like a live button
    while doing nothing. Spell the inert state out locally (inert fill, inert border, `box-shadow:
    none`, `cursor: default`, and **both** motion rules cancelled), as `GameTypeCard`'s `.btn-gated`
    does for the coming-soon card. Keep it local until a second consumer appears — a system-wide
    `--muted` variant with one user is the dangling-rule problem below.
  - **Height is not skin.** `.btn-arcade` joins the scale for height and keeps its material. The old
    rule *"do not restyle `.btn-arcade` to match a dialog"* stands and is untouched by this.
  - **A step is BORDER-BOX height.** `.btn-arcade`'s `box-shadow: 0 5px 0` key-drop **overhangs** it,
    so a 48px arcade button and a 48px input share a top edge and differ by 5px at the bottom. That is
    intended — it is a 3D object — and the two families share a row almost nowhere. **Do not "fix" it.**
  - **Font-size comes from the step but the VALUE is per family**, so there is deliberately **no
    `--ctl-fs-*` token**: Press Start 2P 8px and VT323 19px are the same *apparent* size. Ramps —
    arcade `0.58 / 0.65 / 0.72rem`, dialog button `7 / 8 / 9px`, input `17 / 19 / 21px`.
  - **The scale pins height, horizontal padding and font-size only.** Border width stays skin (the
    arcade 2px is structural to the bevel); `--radius-arcade: 4px` was already uniform.
  - **A step is materialised only where it has a consumer.** All three tokens always exist, but a
    family's `--sm`/`--lg` *class* is written only when something uses it — hence no `.btn-arcade--md`
    and no `.btn-dialog--sm`. Adding one later is two lines; a class nobody uses is the dangling-rule
    problem #124 was filed to fix (which is also why `.btn-dialog--tour` was deleted at 0 consumers).
  - **Documented NON-members — a future agent finding these off the scale is looking at the rule:**
    `.dialog-row` (a full-width list item with an icon and two lines: a fixed height would clip it, and
    it never sits beside a control) · **text-link buttons** (the `retry` links under a failed history
    or stats load — a legitimate third kind of button with no box and no height) · `.dialog__close`
    (20/22px, part of the shell's titlebar geometry) · **`/admin`** and its `.admin-input`, which stays
    `<style scoped>`-duplicated in `GameTypeForm.vue` **and** `BetRefundManager.vue` **on purpose**
    (blue/Courier surface; bringing it onto the arcade scale is its own card) · the **game page's
    furniture** (terminal word input, lever, mobile preset keypad, `GameMobileTabs`).
    ⚠️ **The chat composer left this list on 2026-08-02 (Trello #134).** `GameChatPanel`'s input and
    SEND now take `--ctl-h-sm`: off the scale they had sized themselves from font + padding alone and
    landed **6.6px apart** (33.6px VT323/16px/`py-1` beside 27px Press Start/10px/`py-1.5`), which is
    exactly the defect the scale exists to remove. Heights come off the token, so **padding there is
    horizontal only** — adding `py-*` back fights the fixed height and reintroduces the drift. Every
    other non-member above still stands.
- **Focus is an `outline`, never a `box-shadow` (Trello #135):** one treatment —
  `outline: 2px solid var(--color-cyan); outline-offset: 2px` — declared once in `arcade.css` for
  `.btn-arcade`, `.btn-dialog`, `.btn-icon`, `.dialog-row`, `.dialog__close` (`:focus-visible`) and
  `.input-arcade` (**`:focus`**, because a `<select>` clicked with a mouse does not match
  `:focus-visible` in Chrome and the input has always shown its ring on click).
  ⚠️ **The property choice is load-bearing, not style.** `.btn-arcade--primary`'s amber key-drop **is**
  a `box-shadow`, and `html.light-graphics .btn-arcade--primary` re-declares `box-shadow` at
  specificity **(0,2,1)** — which beats a `.btn-arcade:focus-visible` rule at **(0,2,0)**. A
  box-shadow ring would therefore **wipe the key-drop on desktop** and be **invisible on every phone**,
  since every phone runs `light-graphics`. Same family of trap as the `backdrop-filter` rule above:
  check a GPU-rasterizing desktop **and** a phone, because it is invisible on one of them.
  Before #135 five classes hand-rolled their own ring and **`.btn-arcade` — the loudest, most-shipped
  family — had none at all**. There is still **no global focus rule** in the app; a new family that
  needs a ring joins this selector list.
- **Three panels — grey, black and orbit (Trello #124, 2026-08-02; extended #141, 2026-08-04):** the
  design source is the Space Stroker modal design doc (`Space Stroker Modals.dc.html`, chrome
  direction **1b hairline**, adopted). It defines **two skins over one geometry**; a third,
  **feature-scoped page material** was added for Orbit — see "The third material" at the end of this
  section. The grey/black rule below is unchanged by it.

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
  ⚠️ **Updated 2026-08-02 (#130): the quick tours were deleted, and the black material's one consumer
  is now the How to play guide's `GuideTip` callout block** (`.arcade-frame--black`, a *page* sibling
  of `.dialog--black`). That is still the intended density — **two** tips on the whole guide,
  bookending it, and at most one per section. If black starts showing up on a third and fourth
  surface, the rule is being read too loosely. It is also why the material was **not** retired with
  the tours: a documented rule with zero consumers is exactly the dangling-rule condition #124 was
  filed to fix.

  **The third material — `.arcade-frame--orbit` (Trello #141, 2026-08-04).** `--color-void-deep` fill
  behind a **2px `--color-orbit-deep`** frame, no glow. **Scope is Orbit's own surfaces** — it was made
  for the `/orbit` page (**deleted in #198**), and since **#156** the front door's `HomeOrbitCard` (via `HomeSection`'s third
  `tone="orbit"`). The scope test did not loosen: it is still *"is this surface about Orbit?"*, never
  *"does this surface want depth?"*. A panel that is not about Orbit takes `line` or `cyan`.

  It exists because `/orbit` needed the *depth* of the black panel and could not have it: black
  requires **both** informative-only-and-brief **and** light in tone, and `/orbit` is a money page
  carrying a stop-loss — the exact case the rule excludes. The choice was to bend the black rule or
  give Orbit its own material; **the material won**, because bending the rule for one money page is
  how "black is never a warning about money" stops being true everywhere else. `--black` and its
  single `GuideTip` consumer are untouched.

  The frame is `--color-orbit-**deep**`, not `--color-orbit`: the **open step** inside the page takes
  the full-intensity violet, and a full-intensity frame would flatten it. Same hue, two intensities —
  do not "fix" this by giving the active step a different colour. **Cyan was rejected**: it is the
  focus ring and Beast Mode's archetype accent, and a cyan frame at page scale spends the scarcest
  accent in the app on a feature that already owns a colour.

  ⚠️ **A third material is a real cost.** If a fourth surface wants this one, that is the signal the
  rule is being read too loosely — the same test `--black` is held to.

- **Orbit's semantic palette (Trello #141, 2026-08-04) — colour marks meaning, never completion.**
  Scoped to the Orbit surfaces. Four hues, one job each:

  | Hue | Owns | Never |
  | --- | --- | --- |
  | **Green** `--color-live` | active / running / satisfied — the ✓ pips, the `RUNNING` tag, `linked` | asks for anything; it is status |
  | **Violet** `--color-orbit` / `-deep` | Orbit identity **and** "you are here" (frame + mark at `-deep`, open step at full) | a value, a button fill |
  | **Red** `oklch(0.8 0.16 25)` | warning / error — `no floor`, `paused`, cool-off, a failed deposit | decoration |
  | **Amber** `--color-primary` | the **active step's primary action button** only | status, borders, hover tints, headings, the page title, or an always-available action |

  ⚠️ **The weight rule is what actually prevents a colour salad**, and it is not optional: colour
  lives only in **small marks** — a pip, a 3px left bar, one status tag, one filled button. **Every
  value is plain ink.** The first draft used the correct *number* of hues and still read as noise
  because every value was a filled, bordered pill. The one exception is `no floor`, which is red
  **text** inside an otherwise-ink value.

  The test: **a healthy, fully-configured running Orbit shows no amber and no red anywhere.** That is
  what makes either mean something when it appears. This supersedes `specs/orbit-ui-overhaul.md`
  D6.1, which made violet identity-only and turned a paused Orbit amber.

  ⚠️ The "needs BOTH" tiebreak is this document's reading of the two axes above, not something the
  owner stated in those words. If a future surface makes it feel wrong, change it here rather than
  quietly making an exception at the call site.

  ⚠️ **`--color-phosphor` is a fifth hue on an Orbit surface, and it is a NAMED EXCEPTION** (#157,
  2026-08-08). `#4ade80` — the exact green `GameTerminal.vue` already paints its hot near-miss rows
  and its pot value in — is promoted to a token so the first-win modal can render the winning word
  inside a miniature CRT (`.win-screen`, below). It is licensed **only** for **text the monkey typed,
  drawn on a black screen, in the display face** — i.e. output being *quoted* from the game. It is not
  a status, not an accent, and not available to anything that is not a screen showing machine output.
  Violet still owns Orbit identity, amber still owns the action, red is still warning only.
  ⚠️ **Do not confuse it with `--color-live`.** `--color-live` (`oklch(0.79 0.19 145)`) is a **state**
  — "something is happening right now". `--color-phosphor` is a **material** — phosphor on glass. They
  are close in hue and mean entirely different things; using one for the other's job is the mistake
  this paragraph exists to prevent.
  ⚠️ It is named `phosphor`, not `term`, on purpose: `--color-terminal-border` already exists in
  `GuideBetPanelArt.vue`, and `--color-term` reads as its prefix.

  | | **GREY** | **BLACK** |
  | --- | --- | --- |
  | The player is | doing something — typing, choosing, confirming, signing | reading something short |
  | Tone | serious: money, identity, compliance, failures | light: teaching, welcoming, celebrating |
  | Frame | 1px `--color-line` hairline | **2px `--color-cyan`** + a cyan glow |
  | Fill | `--color-surface` (lifted) | `--color-void-deep` (below the void) |
  | Titlebar | title **left**, amber, behind a 6px amber square, on a sunken `--color-surface-bar` | title **centred**, cyan, between two pixel sparkles, on the panel's own fill |
  | Footer | right-aligned button pair on the sunken bar | one centred amber ghost button |
  | Intent | it recedes | it earns the attention it takes |
  | Today | everything else | the How to play guide's tip blocks, and nothing else — as a **page** material (`.arcade-frame--black`). As a **dialog** (`variant="black"`) it has **no consumer at all** since #157 moved the first-win upsell to the Orbit material; the tier is kept, not deleted (see "Two overlay tiers") |

  **Shared by both, by construction in the shell:** sizes `compact` 540 / `standard` 832 / `wide` 1000
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
    | ⚰️ The quick tours | **black** | short, purely to read, light in tone — **deleted in #130**; the rule they proved now applies to the guide's tips |
    | How to play — `GuideTip` | **black** | same two axes, on a page instead of an overlay. The guide's *serious* sections (Refunds, What you win) deliberately get **no** tip: tone breaks the tie |
  - **Cyan stays scarce on a grey panel: at most ONE element carries it.** In practice that is the
    keyboard focus ring, `TRY IT FREE` on the dashboard, and a verified/revealed value in a readout.
    Exercised exception (#130, no longer "future"): the How to play guide's tip/callout blocks use
    cyan — and they do it by taking the **black material itself**, where cyan *is* the frame.
    On a **black** panel cyan is the frame itself, so the rule does not apply — the accent inside it
    is amber (badges, step labels, the action).
  - **The anatomy lives in `arcade.css` under "THE DIALOG SHELL"**, not in Tailwind utilities in the
    component — see the cascade rule above; a dialog built from utilities could not be overridden by
    `arcade.css` anyway, and one place holding the whole anatomy is what stops the drift that filed
    this card. `ArcadeModal.vue` is the **only** component that writes `.dialog*` structure classes.
  - **The FLAT CONTROL FAMILY (`.btn-dialog` + `.input-arcade`), and `.btn-arcade` for the cabinet.**
    ⚠️ **Amended by #135 — this used to read "`.btn-dialog` inside a dialog, `.btn-arcade` everywhere
    else", and the dialog-only half is gone.** The flat button is flat — 1px, 4px radius, Press Start
    2P 8px, 40px tall, hover to amber, the shared cyan focus outline, `.35` disabled — because
    `.btn-arcade`'s 3rem bevelled body with a 5px amber key-drop is the cabinet's furniture and reads
    as the only 3D object inside a 36px-titlebar hairline dialog. Do **not** restyle `.btn-arcade` to
    match: it is on the lever, the wallet cluster, the dashboard and the cards.
    **The rule is now: use the flat family wherever a control has to line up with an input** — in a
    dialog *or* on a page. `/game/history`'s `MINE ONLY` sits beside a 40px `<select>`, and the only
    alternative was a 48px lever in a filter bar. `.btn-arcade` stays the cabinet's furniture and the
    page-level call to action (`/responsible-gaming`'s own buttons are `.btn-arcade` by #124's rule
    and stay that way — they share a row with nothing).
    **Inputs never had this split at all:** a dialog input and a page input are visually identical, so
    there is **one** input class, `.input-arcade`, used in both. It was renamed from the old
    dialog-scoped name and **deliberately not aliased** — an alias is how two names for one thing
    start drifting.
    **One way to make a button: a class.** `PrimaryButton.vue` and `SecondaryButton.vue` were deleted
    in #135 (0 and 4 consumers); do not reintroduce a wrapper component for a button.
  - **Sizes: `compact` 540 · `standard` 832 · `wide` 1000 · `xwide` 1280** — desktop (`lg:`) caps
    only; below `lg` every dialog is already a full-width sheet or full screen, so the scale has no
    effect there. **Rebased upwards 2026-08-05** from 380/520/720/1024 (owner's call: "on PC these
    modals are way too small"). The old scale was drawn against a laptop; on a 2000px-wide display a
    380px dialog reads as a tooltip that happened to dim the page. **Not a flat ×1.6:** `compact` and
    `standard` took the full +60% because they are the sizes the complaint was about, while `wide` and
    `xwide` — already the tuned "big modal on PC" end — grew less, because ×1.6 would have put them at
    1152/1638, collapsed the two sizes into each other, and covered 85% of a 1920px screen.
    The design doc draws the
    first three; **`xwide` is ours** (2026-08-02) — the "big modal on PC" size, added because the
    5-step quick tour lays five illustrated steps out side by side and at 720 each column was ~128px,
    which shrank the art to a stamp and wrapped every label. **Keep it rare.** If a surface reaches for
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
    - `.arcade-frame .arcade-frame--orbit` — the Orbit material above, as a page/section surface.
      **The `HomeOrbitCard` panel on `/`** (#156; `/orbit` itself was deleted in #198). Its eyebrow takes **full
      `--color-orbit`** through `.eyebrow--orbit`, not the deep token: 9px Press Start 2P on
      near-black needs the full intensity, and this is the same "frame deep, content full" split
      `.eyebrow--cyan` already makes inside a cyan frame.
    - `.arcade-frame` alone — near-black. `/`'s other `HomeSection` panels, `/error`, `/soon`, **`/how-to-play`**
      (#130 — it is the destination of a button on `/`, so the material must not change under the
      player mid-journey, and it gives the black tip blocks far less contrast to fight), and the side
      panel's mobile chat container.
    - `.arcade-frame .arcade-frame--grey` — the grey panel's `--color-surface` on a 1px hairline, so
      the page reads as part of the dialog family. **`/game/history`, `/game/stats` and
      `/responsible-gaming`** (owner's call, 2026-08-02; #130 added the third — grey is what this KB
      pairs with money and compliance, and it made the modal→page conversion **material-preserving**).
    - `.arcade-frame .arcade-frame--black` — **new in #130.** The black dialog material as a *page*
      surface (`--color-void-deep` behind a 2px cyan frame), matching `.dialog--black`'s values
      exactly. **Its one consumer is the How to play guide's `GuideTip` callout**, which inherited the
      black material when the quick tours — its previous only consumer — were deleted. **At most one
      per guide section**, and the two-axis rule below is unchanged.

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
  - **Geometry:** four size tokens (`compact` 540 / `standard` 832 / `wide` 1000 / `xwide` 1280);
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
- **Design system (required):** **always build new UI with the existing frontend design system.** The
  system is **the tokens in `colors.css` + the material classes in `arcade.css` + `ArcadeModal.vue`**,
  plus Tailwind/DaisyUI for everything those do not cover. Concretely:
  - **Buttons and inputs are classes, not components** — `.btn-arcade` (+ `--primary` / `--cyan` /
    `--ghost` / `--ghost-cyan` / `--sm`) for the cabinet, `.btn-dialog` (+ `--primary` / `--active`)
    and `.input-arcade` (+ `--select` / `--auto`) for the flat family, `.btn-icon` for an icon square.
    ⚠️ **`PrimaryButton` / `SecondaryButton` no longer exist** (deleted in #135 — 4 and 0 consumers,
    and the wrapper was a pass-through over a class). Do not reach for a wrapper component, and do not
    reintroduce one: **one way to make a button is a class**.
  - **Every control takes its height from the control-sizing scale** (see the bullet above) — a new
    control joins a step, it does not invent a height out of padding.
  - **Every overlay goes through `ArcadeModal.vue`**; surfaces use `.arcade-frame` / `.arcade-panel`
    and the `.dialog-*` content helpers rather than hand-rolled boxes.
  - **`.win-screen` — the miniature CRT (#157, 2026-08-08).** The one primitive licensed to *quote the
    game's terminal*: black glass (`#000`), a phosphor-green `>` prompt and value in **Press Start 2P**,
    an inset shadow, and a block caret. Reach for it when a surface has to show **what the monkey
    typed** and that string is the point of the surface; do **not** reach for it to make an ordinary
    value look exciting — a second decorative consumer is the signal the rule is being read too
    loosely, the same test `--black` and `--orbit` are held to.
    ⚠️ **The font size is computed from the character count, not fixed.** `--win-cells` is set by the
    consumer to `word.length + 3` (the `>`, its space, the caret) and the class resolves
    `min(44px, calc(100cqi / var(--win-cells) / 1.06))` inside a `container-type: inline-size` wrapper.
    This is load-bearing, not polish: **Press Start 2P's advance is exactly 1.0em** (measured), against
    VT323's **0.4em**, so a size that flatters a 5-letter word overflows the 20-character
    `choiceRequiredLength` ceiling by a factor of four. A plain `font-size` is declared **above** the
    `min()` as the required fallback for engines without container-query units (older in-app wallet
    browsers) — do not delete it as duplication.
    ⚠️ Its entrance types the value out one character at a time in JS, then fires one glow throb and
    starts the caret blinking. **`prefers-reduced-motion` is honoured in the component, not the
    stylesheet** — a media query cannot stop a timer, so the consumer reads `matchMedia` and prints the
    whole string on the first frame. `html.light-graphics` drops the bloom and the throb (both
    `text-shadow`) and keeps the typing and the caret; the green is ≈11:1 on black, so no legibility
    depended on the glow.
  - Do **not** introduce ad-hoc inline styles, one-off component libraries, or bespoke controls when a
    design-system primitive already exists; reuse and extend so new screens (modals, headers, forms)
    match the current look and feel. **If a needed primitive is missing, add it to the design system**
    — a modifier in `arcade.css`, not a stack of utilities at the call site, which the cascade rule
    above shows silently loses.
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
- **The mobile game screen is the arcade cabinet, and it is ONE piece of hardware (Trello #134,
  2026-08-02):** below `lg`, `/game/play` is a three-band column — `GameMobileTabs` → `GameTerminal` →
  `GameMonkey` → `GameConsole` pinned bottom — and **`GamePage` owns the console on the mobile path**
  (`GameTerminal` renders it only on desktop) because Edson sits between them. Band priority is
  **console > terminal > Edson**: the console never shrinks (the lever must stay reachable), the
  terminal keeps a readable minimum and scrolls internally, Edson gives ground first. Rules that cost
  real time to learn:
  - **The tab strip, the terminal and the chat panel share one housing** — true-black glass, a **7px
    `#5c6a8c` bezel**, **4px corners**. Keep the four values in lockstep across all three components,
    or switching tabs resizes the housing. The bezel tone is picked for **contrast, not tone**: taken
    down to the console's own `#424c6e` navy it vanished between black glass and the dark page, so a
    6px bezel read *thinner* than the 4px one it replaced. **Thickness only reads when there is enough
    contrast to see it.**
  - **A selected tab is the lit screen, not a brand fill** — black glass + `#4ade80` phosphor over a
    crisp 2px bar. The bar is the **non-colour selection cue** (WCAG 1.4.1), not decoration; it is an
    `inset` box-shadow so switching tabs moves no pixels.
  - **`terminal.png` is 970×420 holding THREE plates.** Both console modules show *the same* well
    (`x ∈ [0, 300)`); a "one third per module" crop drags the seam and a sliver of well 2 into the left
    module. Crop vertically from **y=20**, past the art's own navy top border — starting at y=12 leaves
    a hairline that reads as a seam under Edson.
  - **Shrink a control with padding, never by insetting its button.** Padding sits inside the hit area,
    so the art gets smaller while the whole plate stays tappable. The lever is the case that forced it.
  - **An overlap needs a lit edge to read as depth.** The cabinet tucks over Edson with an upward
    shadow, and the tuck depth was measured off the GIF's alpha + brightness profile: cut in the
    saucer's near-black underside it reads as fading out, not as going behind something, and the shadow
    has nothing to land on. Scale such an overlap with `clamp(..., dvh, ...)` — a fixed tuck that works
    on a full phone swallows half of the band a short viewport leaves.
  - **Text inputs on this screen are floored at 16px.** `public/index.html` ships the viewport with no
    `maximum-scale`, so iOS Safari zooms the page whenever a focused input computes under 16px. Trim
    with leading and tracking instead; going lower means either breaking pinch-zoom or a `:focus`-only
    bump, both of which are trade-offs to raise explicitly.
  - **The keyboard lift has TWO teardown routes, and a device-only bug needs device evidence
    (Trello #170, 2026-08-13).** The console lifts on the bet input's focus and unwinds on its `blur`;
    `src/hooks/useKeyboardDismiss.ts` force-blurs the input when the keyboard goes away without the
    input saying so — `visualViewport` for a **dismissal** (BACK / swipe-down / iOS "Done", 150px gap,
    behind a latch that waits for the keyboard to be seen open) and **`window`'s `blur`** for the
    player **leaving the app**. The full rules live in
    [`../features/frontend-ux.md`](../features/frontend-ux.md) §3; two apply to any focus work here:
    ⚠️ **`document.activeElement` stays the input** — Android fires an input's focus/blur *events* from
    **window** focus changes while the element keeps DOM focus, and Chrome restores that focus on
    return, so a focus *flag* and real DOM focus are different things on this screen; and ⚠️ **a
    listener that must hear the window blur cannot be armed on a focus flag**, because the flag flips
    ~1 ms first.
    ⚠️ **The process lesson generalises beyond this screen:** two fixes shipped green in desktop Chrome
    and failed on the phone, because the synthetic repro encoded the same wrong model of the device as
    the fix did. For a mobile-only defect, **instrument the page and read real device events before the
    first fix** — a temporary on-page log (persisted to `localStorage` so it survives Chrome discarding
    a backgrounded tab, plus a patched `HTMLInputElement.prototype.blur` so *our* blur is
    distinguishable from the browser's) turned three rounds of guessing into one screenshot. And make
    the repro model **causality**, not the observed effect: it only became honest once "Chrome restores
    focus" was made conditional on the element still holding DOM focus.
  - **A mobile-only surface must be built crisp — `usePerfMode` forces `html.light-graphics` on every
    viewport below `lg`.** A component that only renders below `lg` therefore *always* runs in that
    mode, so a wide soft glow there is built specifically to be stripped: dead weight on the one device
    that ever sees it. Carry the effect in something that survives (a solid bar, a tight `0 0 6px`
    halo like `.pot-value`), not in a bloom. Conversely, **check what that mode kills before assuming
    a class does anything**: `.animate-wobble` sat in the strip list and left Edson frozen on the one
    screen he is the mascot of, which is why it was pulled out of it (`arcade.css`) — it is one
    `<img>`, transform-only, on an 8s loop.
  See `features/mobile-game-design-update.md`.
- **Routing:** `src/routes.ts`, **prefix-free** since `home-dashboard` — `/` (`Home`, the starting page)
  · `/game` (the plain lobby, and the URL `EMBEDDING.md` points integrators at) · `/game/play` ·
  `/game/history` · `/game/stats` · **`/how-to-play`** · **`/responsible-gaming`** (both #130) ·
  **`/game/practice`** (#129) · `/admin` · `/wallet-bridge` · `/background` · `/error`.
  ⚠️ **`/soon` is gone** (#129 — practice mode was that placeholder's last consumer); a bookmarked
  `/soon` now falls through the generic catch-all to `/error`.
  **Embedded redirects live in ONE place** — `main.ts`'s existing `beforeEach` reads a single
  `EMBED_REDIRECT_ROUTES` set (`Home`, `HowToPlay`, `ResponsibleGaming`, `Practice`) and returns
  `GameLobby`, so
  the rules cannot drift apart. It is registered **before** the analytics `afterEach`, so a framed load
  never counts as a pageview and the component never mounts at all. ⚠️ **Neither new route is an
  `isPlayerRoute()`** — neither renders a display name, so `App.vue` boots nothing for them and each
  page fetches what it needs itself (the guide the catalogue, the RG page preferences).
  ⚠️ **There is no `scrollBehavior` on the router.** A cold load of `/how-to-play#refunds` scrolls
  nowhere on its own — any page wanting hash anchors must handle `route.hash` itself on mount, after
  the content has painted, and must match the hash against a **registry** rather than interpolating it
  into a `querySelector`.
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
- **A surface that shadows the game screen gets its own components AND a factory store — not the
  module-singleton idiom (Trello #129, 2026-08-03):** practice mode reproduces the whole cabinet at
  `/game/practice` without touching one line of the real one. Two rules make that hold, and both are
  departures from how the rest of this app is written:
  - **The store is a `provide`/`inject` FACTORY** (`usePracticeGame` returns a fresh store, created
    once in `PracticePage`), not module-level `ref`s. Module state survives unmount, so a stale
    rollover timer or a second mount bleeds across navigations — and practice state could be mistaken
    for the live game's. A factory makes *"this state cannot outlive its page"* **structural** rather
    than a rule to remember. Use this shape for any second instance of an existing screen.
  - **An explicit import ban is the isolation mechanism.** Nothing on the practice path may import
    `useGame`, `useNearMiss`, `useGameHub`, `useGameChat`, `useIdleDisconnect`, `mainService` or
    `solana-wallets-vue`; what is genuinely shared is the **pure** layer (`utils/nearMiss.ts`,
    `formatGameAmount`, the art and audio assets). ⚠️ `useNearMiss` in particular is a module singleton
    shared with the live screen — calling it from a second surface puts its rows on a real round.
  - **Reuse the pure helper, re-declare the CSS.** `GameMonkey`'s keyframes live in its *scoped* block
    and `GameTerminal`'s `.animate-rise-in` in an *unscoped* one, so depending on either from another
    component is invisible coupling that breaks the moment someone scopes it. Copy the values, own the
    names.
  - **Distinct DOM ids are not cosmetic.** The desktop terminal computes its width from
    `getElementById('game-terminal-container').clientHeight`; a second surface reusing that id collides
    the moment both mount.
  - ⚠️ **A page whose branches are a `v-if`/`v-else-if`/`v-else` chain is a SINGLE root** and inherits
    `App.vue`'s `RouterView` classes (`mx-auto h-5/6 block`). Adding a fourth sibling (an audio
    component, a watcher host) makes it a **fragment**, the class is silently dropped, and a
    height-dependent layout resolves against nothing — a blank page with only a Vue *warning*. Mount
    such helpers **inside** a branch.
  - **Where a value exists on both sides, share it rather than copy it** — the practice hold reads the
    same `VUE_APP_NEW_GAME_DELAY` the real console does. Backend-owned numbers that the frontend cannot
    see (`TimeUntilGameStarts` = 10, `TerminalBatchLimit` = 500) are hard-coded with a comment naming
    the setting, so the next person knows where the truth lives.
  See `specs/practice-mode.md`.
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

## One named z-index scale — and a stacking context caps everything inside it

Every layer number lives **once**, as a custom property in `src/assets/styles/colors.css`;
`tailwind.config.js` only maps names onto those variables (`header: 'var(--z-header)'`). Never write a
bare `z-[47]` in markup — a number with no name is a number nobody can reason about later.

The scale, low to high:

| Token | Value | What sits there |
| --- | --- | --- |
| `--z-base` / `--z-app` / `--z-furniture` | 0 / 10 / 20 | page, `App.vue` root, in-page docked chrome |
| `--z-practice-dim` | 41 | the practice tutorial's backdrop |
| `--z-practice-cue` | 42 | every band the tutorial lifts (Edson, terminal, cabinet) |
| `--z-practice-bubble` | 43 | the tutorial bubble — **must never be covered** (#171) |
| `--z-header` | 45 | the sticky `GameHeader` |
| `--z-popover` | 50 | dropdowns, tooltips, the consent banner |
| `--z-modal` / `--z-blocking` / `--z-legal` | 60 / 70 / 80 | teleported overlays, boot/idle recovery, the 18+ gate |

⚠️ **A `position` + `z-index` element is a STACKING CONTEXT, so nothing nested inside it can outrank
it — however loudly the child asks.** `.app-header` is `sticky` + `z-index`, so its menu dropdown and
its wallet dropdown composite at the **header's** number even though both are written as
`--z-popover: 50`. While the header was at **40** that put them *under* practice's lifted bands (42):
`elementFromPoint` over the open menu returned a terminal `<p>`, i.e. the entries were **unreachable,
not merely dim**. Raising `--z-header` to 45 fixed both dropdowns at once precisely *because* they
inherit its tier. It stays below `--z-popover` because the consent banner and the loader are
**outside** the header, so their 50 is real.

Two corollaries worth keeping:

- **Adding a tier means checking what it now outranks**, and what still outranks it. The tier is the
  cheap part; the audit is the work.
- **Between elements on the same tier, DOM order decides** — which is the whole of #171's original
  bug: the bubble and Edson's band were both 42, and he is written later. If two things must never
  cover each other, give one of them its own number rather than relying on source order.
- The consent banner, the loader, the legal gate and every `--z-modal` overlay are **teleported to
  `#app`** for the same reason — see "Teleport to `#app`" below.

## Two overlay tiers

Overlays are **not** all meant to look alike. There are two deliberate tiers, and the test that picks
between them is **frequency, not subject matter**.

- **System tier — neutral hairline.** Overlays the player opens *themselves* and reopens constantly:
  Settings, Provably Fair, Connect Wallet, the **Orbit wallet modal**, the **header balance cluster**,
  the side panels, and the error/history/stats surfaces. Chrome that recedes:
  - sizes: compact **540** · standard **832** · wide **1000** · mobile **100%**
  - titlebar **36px** desktop / **44px** mobile; body padding **18px**; footer **12-14px**
  - border **1px `--color-line`**, radius **4px**
  - backdrop: **void at 55% + a 9px diagonal dither**
  - **Esc** closes, and so does a backdrop click
- **Tutorial / help tier — black + a 2px CRT-cyan frame.** Overlays that appear **unprompted**, have to
  earn attention, and are shown **once and then never again**.

  ⚠️ **This tier currently has NO implementation** (#157, 2026-08-08). The quick tours became the
  `/how-to-play` **page** in #130, and the first-win Orbit upsell (`OrbitFirstWinModal.vue`) — its last
  overlay — moved to the **Orbit material** (`variant="orbit"`), because a surface that sells Orbit
  should read as one object with `/orbit`, `OrbitModal` and `HomeOrbitCard`. `variant="black"` /
  `.dialog--black` are **kept, not deleted**: the tier is still the right answer for the next
  unprompted, brief, light-in-tone overlay, and deleting a shipped material inside a copy/skin card
  would have been an unrelated design-system change. It is recorded here rather than left as drift —
  a documented tier that no code proves is exactly the dangling-rule condition #124 was filed to fix.
  `.arcade-frame--black` (the How to play guide's `GuideTip`) is the black **material's** remaining
  live consumer and is untouched.

  The behavioural half of the tier below is **not** consumer-less — it still describes
  `useFirstWinUpsell`, which kept every guard, the `localStorage` key and every-exit-marks-seen
  through #157. Only the skin moved.

  A once-ever overlay owes two things beyond the chrome. **Every exit marks it seen** — X, Esc,
  backdrop and the primary CTA alike — so nobody is nagged for dismissing it "the wrong way". And its
  guards run **cheapest first and refuse silently**, including "another overlay is already open": two
  unprompted modals at once is worse than missing one. `useFirstWinUpsell` and `useQuickTour` share
  that ordering (`recovering`, `isIdleDisconnected`, `relaunching`, terms not accepted, embedded).
  "Seen" is `localStorage`-only in both, and deliberately **per browser, not per wallet** — server-side
  would mean either asking someone to sign a Solana message to dismiss a modal, or an unsigned write
  keyed on a public address that would let anybody suppress a stranger's modal.

Provably Fair is *system* tier even though it explains something, because it is reopened every round.
The first-win upsell is *tutorial* tier **by behaviour** even though it is small, because it arrives
uninvited — the same test that put the quick tours there before they became a page. ⚠️ Since #157 it
no longer wears the tier's **skin**: it is the Orbit material, because it sells Orbit and should read
as one object with `/orbit`. Tier and material are separate choices here, and this is the one shipped
surface where they diverge.

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

## Feature flags

All flags live in **`src/constants/featureFlags.ts`** — one file, one exported `const` each, read
from a **`VUE_APP_*`** env var. They are **build-time**: Vue CLI inlines `process.env.VUE_APP_*` at
compile time, so a flag cannot be flipped at runtime and a changed value needs a rebuild.

**State the fail direction, and pick it deliberately.** The two shipped flags test in *opposite*
directions, and that is not an inconsistency to tidy up:

```ts
// fail-OPEN: absent key ⇒ enabled. The default is the feature being on.
export const allowFreeBets = process.env.VUE_APP_ALLOW_FREE_BETS !== 'false';

// fail-CLOSED: absent key ⇒ hidden. The default is the feature being off.
export const orbitEnabled = process.env.VUE_APP_ORBIT_ENABLED === 'true';
```

Choose fail-closed whenever an un-configured build shipping the feature would be worse than one
missing it — anything that moves real money, or anything not production-deliverable yet. Say which
one you chose in a comment on the const, and why; the next reader cannot tell `!==` from `===` apart
by intent alone.

**Gate at the mount point *and* inside the composable.** A `v-if` alone only hides pixels — the
composable still fires requests, joins hub groups and starts pollers. Guard both, because the mount
point is what a reviewer sees and the composable guard is what a future caller cannot bypass by
forgetting the `v-if`. `orbitEnabled` (#146) is the reference implementation: `GameHeader.vue` gates
six mount points, and `useOrbitNotifications` / `useOrbitRedirect` / `useFirstWinUpsell` each refuse
on their own first line.

⚠️ **Guard the narrowest thing that does the feature's work**, never a shared block it happens to sit
in. `useFirstWinUpsell.maybeOpen()` is called from inside `useGame`'s victory one-shot, alongside the
victory audio and the embedded `bet-settled` post — gating that `if` would have silently broken both.

⚠️ **A flag does not shrink the bundle.** Gated code is still imported and still compiled in, so
grepping `dist/` for the feature's strings finds them all and proves nothing. Verify a flag by
running the app in both states and watching the network tab.

**A gate whose env var carries a VALUE lives with that value, not in `featureFlags.ts`.** `#160`'s
token surfaces are gated on `VUE_APP_TOKEN_MINT`, which is an address the UI also *renders*; splitting
"is it on" from "what is it" across two files would let them disagree. The whole block —
the derived `tokenLive`, the ticker, the URLs built from the mint — lives in **`src/constants/token.ts`**,
and `featureFlags.ts` stays what it says it is: booleans.

⚠️ **Fail-closed means a real test, not a truthiness check.** An address gate validates shape —
`/^[1-9A-HJ-NP-Za-km-z]{32,44}$/` for base58 — so a half-typed or whitespace value is treated as unset
rather than rendered. A `Boolean(process.env.X)` gate would happily ship a Buy button pointing at `" "`.

**Document every new flag in `.env.example`** with its production value and its fail direction; add
it to `monkify-meta/deploy/secrets.example.json` → `frontBuild` if it must reach a deploy (the
deploy tool serializes that whole block verbatim, so no C# change is needed), and to
`monkify-backend/scripts/setup_local_environment.bat` if the local Docker stack needs it —
`.env.production.local` is Vue CLI's highest-priority env file and overrides `.env.local`.

## Player settings that belong to the DEVICE — a `localStorage` singleton, never the backend

**Rule: a setting about the machine the player is sitting at — display, graphics, anything the
hardware decides — is stored in `localStorage` and never routed through the API.** Two shipped
examples: the provably-fair **client seed** (`useClientSeed.ts`) and the **graphics controls**
(`useDisplaySettings.ts`, #161). Both live in `PlayerCustomizationModal.vue` beside the display name,
which *is* server-backed — and the split is deliberate.

**The deciding test is not "is it small?", it is "does it need an identity?"** The modal's `SAVE`
button only renders when a wallet is connected (`v-if="!needsWalletToCustomize"`) and pressing it
prompts an **ed25519 wallet signature**. Route a device setting through it and a wallet-less player
cannot save it at all, and dragging a slider would prompt for a signature. So:

- **Apply instantly, persist on change, and do NOT revert on `CANCEL`.** Consistency matters more than
  the modal metaphor here: SET / ROTATE / the graphics controls all behave the same way, and `CANCEL`
  only ever discards the *name* edit.
- **Render the control OUTSIDE the wallet `v-if / v-else`.** With no wallet the `IDENTITY` block is a
  connect gate rather than a name field, so anything placed "under the name" disappears exactly when
  the player has no identity — which is precisely when a device setting must still work.

**Shape it as a module-scope singleton, hydrated once on first import** — refs at module level, read
in an IIFE-ish initializer, `computed` for anything derived, setters that write through to storage.
`useClientSeed.ts` is the reference.

```ts
const storedDensity = ref<number>(readDensity());   // hydrate ONCE, at import
const density = computed(() => canonical.value ? DEFAULT : storedDensity.value);
```

**Every read and every write is wrapped, and a bad stored value falls back rather than throwing.**
Private mode and blocked storage make `localStorage` itself throw, and a hand-edited value is a normal
thing to find. Clamp numbers into range; treat anything that is not exactly a recognized literal as
absent. ⚠️ **Distinguish "stored `false`" from "never set"** — a tri-state (`boolean | null`) is
usually what you want, because "the player explicitly turned this off" and "the player has never
touched it" must resolve differently. Collapsing them is how an explicit OFF gets silently re-enabled
by auto-detection on the next load.

**Watch the import direction.** A settings module is a *leaf*: things import it, it imports nothing
that imports it back. `usePerfMode` imports `useDisplaySettings`, never the reverse — and `usePerfMode`
runs its detection at import time, so a cycle would not merely be untidy.

**When a route must ignore the player's settings, seed the exception from the pathname, not
`route.meta`.** `/background` renders canonical defaults so the captured asset cannot drift with
whoever records it — but the backdrop lives globally in `App.vue`, and `App.vue` mounts before the
initial navigation has necessarily populated `route.meta`, so a watcher alone paints the first frames
with the player's values. Seed synchronously from `window.location.pathname` and keep the watcher only
for in-SPA navigation.

## Deadlines, not timers — anything that must survive a hidden tab

**Rule: if a delay must still be correct after the player switches tabs, express it as a wall-clock
timestamp and evaluate it on `visibilitychange`. Never trust `setTimeout`/`setInterval` alone.**
Chrome throttles timers in background tabs and **freezes them entirely** in a discarded/frozen one, so
an armed timer is not a trigger you can rely on. `useIdleDisconnect.ts:26-29` is where this is written
down; read it before adding any new deadline.

The shape, copied from `useIdleDisconnect.ts:70-79`:

```ts
const onVisibilityChange = () => {
  if (document.hidden) { hiddenSince = Date.now(); return; }
  // Evaluate BEFORE clearing hiddenSince: this is the load-bearing path — it is what notices that
  // the deadline elapsed while the timer was asleep.
  evaluate();
  hiddenSince = null;
};
```

- **Keep the `setTimeout` if you want, but only as the visible-tab fast path.** Correctness comes from
  the timestamp; the timer just makes the common case fire on time. `useGame.gradualSpeedReset()` does
  exactly this.
- **A second arm overwrites the deadline, it does not stack.** Clear the fast-path timer when you
  re-arm, and read the delay from a local rather than from state you just mutated.
- **Where the listener lives depends on the composable's shape.** A component registers in
  `onMounted` / tears down in `onBeforeUnmount`. A **module-level singleton** composable (`useGame`,
  `useIdleDisconnect`) has no lifecycle hooks — register once at module scope, for the tab's lifetime.
  ⚠️ Do **not** hang a deadline off `startIdleWatch`/`stopIdleWatch` unless it genuinely belongs to the
  socket: that watch follows the connection, and most deadlines must still land after an idle
  disconnect.
- **Also resync visual state on return, do not resume it.** Anything animating (positions, speeds,
  transitions) should be re-derived from current props/state on becoming visible rather than
  fast-forwarded from a paused value — see
  [background-lifecycle-hardening](../features/frontend-ux.md) for what "repair the
  paused transition" cost.

- **A deadline that stages state across rounds must be stamped with the round it belongs to.** A
  throttled tab can evaluate late, and a step-down applied into a round that has already started drives
  the wrong animation. `useGame.gradualSpeedReset` stamps `speedResetGameId` and
  `evaluateSpeedReset` drops the deadline when the round is gone. Fix this at the ARM site, not in the
  watcher that consumes it — a guard at the far end hides the next regression in the source.

Used by: `useIdleDisconnect.ts`, `useTokenBalance.ts`, `useGame.ts` (`gradualSpeedReset`, and the
**round rollover** — deadline + 500 ms ticker + `visibilitychange`, see
[`game-mode-ux.md`](../features/game-mode-ux.md)), `GameBackground.vue`, `TorpedoSpawner.vue`.

## A timer's CARDINALITY must not be a property of the layout

If a component can be mounted more than once, any timer, deadline or one-shot inside it runs once **per
mount** — and how many mounts exist is a question about breakpoints, not about the behaviour.

`GameConsole.vue` is mounted by `GameTerminal` on desktop **and** by `GamePage` on mobile in two
breakpoint-gated places. Its round-rollover timer therefore fired up to three times, which nothing in
the component made visible. Moving the *decision* into `useGame` module scope behind a **register-once
flag** (the `moneyWiringDone` / `idleWiringDone` / `rolloverWiringDone` pattern) makes the cardinality a
property of the tab instead.

- **The component keeps the presentation, the composable owns the decision.** `GameConsole` still plays
  the lever; it watches a `rolloverCount` the composable bumps.
- **Register-once inside the composable is safe** when everything the closure touches is module-scope
  state — capturing the first consumer's closure is then not a leak. Say so in a comment, as
  `useGame`'s money wiring does.
- **A manual trigger and its automatic counterpart must be the same function**, or they diverge. The
  lever calls the same `triggerRollover` the deadline does.

## Global effects driven by a tab-scoped singleton need a ROUTE gate, at the consumer

**Rule: when something mounted in `App.vue` reacts to `useGame` state, ask which routes it should be
visible on — and gate it at the consumer, never by muting the shared state and never by detaching the
socket.** `useGame`'s SignalR subscription is tab-scoped and **deliberately** outlives `GamePage`
(`unified-websocket-connection`), so anything global that reads it keeps reacting to a round the
player has left. That is how the starfield and torpedoes ended up animating an abandoned round on
every other page ([off-page-game-presentation](../features/frontend-ux.md)).

The three wrong fixes, and why:

- **Detaching from the hub on leaving the page** — the same `Status → Ended` drives the balance
  re-read with win/refund attribution, the embedded `bet-settled` / `game-ended` signals and
  `useOrbitRedirect`. Detaching breaks settlement feedback for the most ordinary case there is: bet,
  wander off, round settles while away.
- **Zeroing the shared state** (e.g. `typingSpeed = 1` on leave) — other consumers read the same ref,
  so a returning player stares at a stalled round until the next server event.
- **`v-if`-ing the component out of `App.vue`** — only safe if unmounting is harmless. It usually is
  not: `TorpedoSpawner.onUnmounted` removes every tracked node and its template root **is** the
  container, so a `v-if` deletes anything mid-animation. Guard the reactive path inside the component
  instead, and let what is already running finish.

The shape:

```ts
// useGame.ts — module scope, because handlers bound onto the tab's connection outlive the component
// that bound them, so a computed in App.vue is unreachable from them.
const inGame = ref(false);
// …exposed as `inGame` + `setInGame` on the composable's return.
```

```js
// App.vue — the SINGLE writer.
watch(() => route.name === 'Game', (onGamePage) => setInGame(onGamePage), { immediate: true });
```

```html
<game-background :speed-multiplier="inGame ? typingSpeed : 1" />
```

- **One writer, many readers.** `App.vue` owns the route→flag mapping; consumers only read. Same shape
  as `useDisplaySettings.setCanonicalGraphics`.
- **A pathname seed is only needed when `false` at first paint is WRONG.** `canonicalGraphics` needs
  one (`route.meta` is unresolved at first paint and the stored density would paint for a few frames);
  `inGame` does not, because an unresolved route yields the default speed, which is correct on a cold
  load anyway. Ask which way your flag fails before copying the seed.
- **Ordering matters around one-shots.** Put the gate **after** any one-shot flag the watcher
  consumes, or the one-shot stays armed while gated and misfires later.
- **Gate the effect, still consume the event.** The victory/lose sting is suppressed off-page but
  `endAudioFiredForGame` is stamped either way — a suppressed one-shot must be *spent*, not *deferred*,
  or it fires late and reads as a bug.

Used by: `useGame.ts` (`inGame`), `App.vue`, `TorpedoSpawner.vue`.

## DaisyUI's base layer outranks bare element selectors

`:root` is a **pseudo-class** (specificity 0,1,0); `html` is a **type selector** (0,0,1). DaisyUI's
base layer ships:

```css
:root, [data-theme] {
  background-color: hsla(var(--b1) / var(--tw-bg-opacity, 1));
  color: hsla(var(--bc) / var(--tw-text-opacity, 1));
}
```

so a rule written as `html { background-color: … }` **loses to DaisyUI** and never applies. This
shipped as a real bug (#149): the root element painted DaisyUI's `base-100` (`#2A303C`) instead of
`--color-void`.

- **Write `html:root, body`** (0,1,1) when you need to beat it. A plain `:root` only **ties** and then
  depends on stylesheet order — the exact fragility that caused the bug.
- ⚠️ **On the root element this is never merely cosmetic.** The root's background paints the
  **canvas**, i.e. the whole scrollable area, while `<body>` paints only its own box — and body is
  viewport-height in this app, because the `min-h-screen` wrapper inside it overflows visibly instead
  of stretching it. A mismatch therefore shows as a **hard horizontal colour seam exactly one viewport
  down**, on every page taller than the screen. Fix the root's colour; do **not** paper over it by
  giving `<body>` a `min-height`.
- The same specificity trap applies to any other bare-element override of a DaisyUI base rule
  (`color` on `html`, etc.). Check the computed value in DevTools rather than assuming your rule won.

## Icons — one registry, two grids, always decorative

Since **#159** the app has exactly one icon system. **Never inline an `<svg>` icon in a component,
and never reach for a text character as a glyph** (`✓ ✗ ▶ ▼` and a VT323 letter `X` all used to be
marks here). Full behaviour: `features/icons-and-menu-overhaul.md`.

- **Render `<AppIcon :name="…" :size="…" />`** (`src/components/ui/AppIcon.vue`). Adding an icon is a
  **data change** in `src/constants/icons.ts` — declare a drawing, get a mark. It is not a system
  change and needs no component edit.
- **Authoring is a bitmap, not path data.** `b` = eight 8-character rows (coarse); `b16` = sixteen
  16-character rows (fine); `#` is filled, anything else is empty. `r` = raw rects, **emitted
  verbatim and never rasterised** — for production drawings that must not shift by a unit.
- **Legal sizes are a real constraint, not a style note.** Coarse is pixel-exact at
  **16 / 24 / 32 / 40**; fine only at **16 / 32 / 48**. Off-ladder sizes go visibly soft.
  ⚠️ **Opting a drawing into the fine grid costs it the 24px rung** — check where it is actually
  placed before choosing. Coarse is the default; reach for fine only when the coarse grid provably
  cannot hold the silhouette (today: `practice`, `wallet`).
  ⚠️ Orbit's inherited call sites render off-ladder (11/13/14/18/20/26/28/30/52px) and **stay that
  way**: zero visual change on a shipped surface outranks the ladder, which governs new placements.
- ⚠️ **Icons are DECORATIVE.** `aria-hidden="true"` + `focusable="false"` are unconditional; the
  accessible name comes from the control's `aria-label` or its visible label. A cell whose visible
  label is shortened (`RESPONSIBLE`) carries the full name in `aria-label`.
- **Colour comes from the surface.** Icons paint `currentColor` and inherit the call site's tint —
  tint by passing a text utility (`class="text-orbit"`), never a per-call-site `fill`.
  ⚠️ **Exactly two icons carry a baked fill** (`win-at` green, `falls-to` warm red). Do not add a
  third. `AppIcon` renders **one fill per drawing**, so a two-colour mark (a brand logo) cannot live
  in the registry at all — `usdc.svg` stays a two-tone vector rendered through `<img>` with
  `image-rendering: auto`.
- **A mark replaces the generic marker, it does not join it.** `ArcadeModal` drops its 6px amber
  square when `titlebar-lead` is filled (`.dialog--has-lead`); `HomeSection` drops its `+ TITLE +`
  pluses when given an `icon`. Two markers state the identity twice and the generic one is louder.
  Marks read amber via `currentColor`; **Orbit is the named exception** and carries violet.
- **Pixel art animates in steps.** `.app-icon--spin` is `steps(4)` — a continuous rotation puts pixel
  edges at arbitrary angles and destroys the read `shape-rendering="crispEdges"` exists to give. It
  freezes under `prefers-reduced-motion` and is **deliberately excluded** from the light-graphics
  `animation: none` list (every phone runs light-graphics; a frozen loader looks hung).
- ⚠️ **A PascalCase grep is not a call-site audit in this codebase.** `HomeOrbitCard` used
  `<orbit-mark />` kebab-case and a `git grep OrbitMark` reported the import as unused. Search both
  casings before declaring a component dead.

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
