# Frontend conventions (`monkify-front`, Vue 3 + TS)

> Status: **stub** — to be fully drafted. Captures "how we write frontend code here".

## Patterns observed (fill in / verify)

- **Vue 3 with `<script setup>`** components; TypeScript throughout. Build via Vue CLI (webpack).
- **Composables live in `src/hooks/`** and are the primary state/logic unit (e.g. `useSession`,
  `useConnection`, `useOperator`, `useDisplayCurrency`). Several use **module-level `ref`s** so state
  is shared as a singleton across components — be careful with lifecycle/reset.
- **Shared types & enums live in `src/types/`** (`ActiveSession`, `BodySessionBet`,
  `OperatorBranding`, `DefaultResponse<T>`, …). Mirror backend enums exactly (codes must match).
- **API access via `src/services/mainService.ts`** — a single axios instance with a request
  interceptor that sets `baseURL` (`VUE_APP_API_BASE_URL`) and attaches the **`X-Launch-Token`**
  (from the `src/utils/launchToken.ts` store); a response interceptor routes any `401` to `/error`.
  Add new endpoints here and export them from the default object.
- **Launch token:** `src/hooks/useLaunch.ts` captures the token from the launch URL fragment
  (`…#launch_token=<jwt>`), strips it from the URL, and persists it per-tab in `sessionStorage`
  (a fresh fragment overrides a stored one). In dev, tokens come from the Crypto/Regulated buttons on
  the `/monkify/game` landing — no silent auto-mint (see `features/dev-qol-stuff.md`).
- **Operator entry:** game routes are **param-free** (`/monkify/game/sessions`, `/monkify/game/play`);
  the operator is decoded from the token's `iss` (`getOperatorIdFromToken` in `utils/launchToken.ts`),
  not the URL. Missing/undecodable token, inactive operator, or any token `401` → `/error`.
  `useOperator.loadOperator()` ensures a launch token, fetches `GET /operators/{id}`, and applies
  `--color-secondary` when branding includes a color. Branding no longer carries a payment model —
  payment UX is driven by the **selected session's `paymentMethod`**, not the operator.
- **Wallet UX gated on the session's `paymentMethod`:** the game header (`GameHeader.vue`) shows the
  wallet cluster — the Connect Wallet button (`WalletButtons.vue`) **and** the `TokenBalance` — **only**
  when the current session is `SolanaToken` (`selectedSession?.paymentMethod === PaymentMethod.SolanaToken`),
  using the session's `tokenSymbol` / `tokenMintAddress`; `Standard` sessions and the sessions list hide
  it. `GameConsole.vue` requires a connected wallet to bet only for SolanaToken. The settings gear is
  gated on operator context from the launch token (`operatorId ?? getOperatorIdFromToken()`) so it
  survives an operatorId reset on a hard reload. See `features/payment-methods.md`.
- **Bet/pot display:** the play-page terminal (`GameTerminal.vue`) `formatAmount` branches on the
  session's method — **`SolanaToken`** → `{amount} {tokenSymbol}` (token decimals); **`Standard`** →
  `useDisplayCurrency.formatUsd` (preferred fiat with `≈` when not USD; silent USD fallback when rates
  unavailable). The preferred currency has no effect on SolanaToken amounts. See
  `features/payment-methods.md` + `features/player-customization-modal.md`.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`** in `useSession`: two hubs (`Sessions`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`; join per-session groups; handle `…/Terminal`, `…/DisplaySpeed`,
  `…/Status`, `…/Bets`, `SessionReceived`, and `{sessionId}/ChatMessage` (session chat — see
  `features/session-chat.md`).
- **Near-miss reveal ("Characters Away"):** a pure detector (`src/utils/nearMiss.ts`, KMP tail-prefix)
  plus a module-singleton composable (`src/hooks/useNearMiss.ts`) fed the raw `…/Terminal` batches from
  `useSession` (independent of the terminal crop). It tracks each of the player's **own** bet choices
  (`ownBetChoices`, recorded on successful bets) and exposes `nearMissEntries` — one closest-approach
  row per choice that `GameTerminal.vue` renders in a bottom-docked area with the in-game `> POT:`
  line. Timing (2 s stall-reset loop, win type-out) lives in the composable; the detector stays pure
  and side-effect-free. See `features/characters-away.md`.
- **Solana wallet** via `solana-wallets-vue` + `@solana/web3.js` + `@solana/spl-token`
  (`useConnection.sendToken`) — used **only for `SolanaToken` sessions**. `useSession.bet()` branches on
  `selectedSession.paymentMethod`: SolanaToken transfers to the session's `tokenReceiverPublicKey` /
  `tokenMintAddress` and posts the returned signature + wallet; Standard posts `{ amount, choice, seed }`
  with no wallet/signature (identity via the launch token `sub`). The `Bet.PaymentSignature` field is
  gone — the reference is carried as `ExternalTransactionReference` server-side.
- **Styling:** Tailwind CSS + DaisyUI. Operator theming overrides `--color-secondary` at runtime
  (see `useOperator.applyOperatorTheme`).
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
- **Routing:** `src/routes.ts` — `/monkify/game/sessions`, `/monkify/game/play`, `/error`;
  `/monkify/game` is the dev launch landing (gated by `VUE_APP_DEV_LAUNCH`; prod → `/error`); root
  `/` redirects to `/error`.
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
