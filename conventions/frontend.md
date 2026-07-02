# Frontend conventions (`monkify-front`, Vue 3 + TS)

> Status: **stub** — to be fully drafted. Captures "how we write frontend code here".

## Patterns observed (fill in / verify)

- **Vue 3 with `<script setup>`** components; TypeScript throughout. Build via Vue CLI (webpack).
- **Composables live in `src/hooks/`** and are the primary state/logic unit (e.g. `useSession`,
  `useConnection`, `useOperator`). Several use **module-level `ref`s** so state is shared as a
  singleton across components — be careful with lifecycle/reset.
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
  `useOperator.loadOperator()` ensures a launch token, fetches `GET /operators/{id}`, exposes
  `paymentModel` (wallet UX is gated on it), and applies `--color-secondary` when branding includes a color.
- **Wallet UX gated on payment model:** the game header (`GameHeader.vue`) hides the whole wallet
  cluster — the Connect Wallet button (`WalletButtons.vue`) **and** the USDC `TokenBalance` — for
  **regulated** operators (`paymentModel === OperatorPaymentModel.Regulated`); crypto/unknown keep it.
- **Bet cost display:** the play-page terminal (`GameTerminal.vue`) shows `> BET: {requiredAmount}
  {betCurrency}` for **every** session type (not just lottery). `betCurrency` is `USDC` for crypto
  operators and a neutral `credits` otherwise — the front has no client-side fiat currency source
  yet (no GET-preferences endpoint, no currency on `OperatorBranding`); showing real fiat is a
  deferred backend follow-up.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`** in `useSession`: two hubs (`Sessions`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`; join per-session groups; handle `…/Terminal`, `…/DisplaySpeed`,
  `…/Status`, `…/Bets`, `SessionReceived`.
- **Solana wallet** via `solana-wallets-vue` + `@solana/web3.js` + `@solana/spl-token`
  (`useConnection.sendToken`). The returned signature is used as the bet's `paymentSignature`.
- **Styling:** Tailwind CSS + DaisyUI. Operator theming overrides `--color-secondary` at runtime
  (see `useOperator.applyOperatorTheme`).
- **Routing:** `src/routes.ts` — `/monkify/game/sessions`, `/monkify/game/play`, `/error`;
  `/monkify/game` is the dev launch landing (gated by `VUE_APP_DEV_LAUNCH`; prod → `/error`); root
  `/` redirects to `/error`.
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
