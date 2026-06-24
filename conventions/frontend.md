# Frontend conventions (`monkify-front`, Vue 3 + TS)

> Status: **stub** — to be fully drafted. Captures "how we write frontend code here".

## Patterns observed (fill in / verify)

- **Vue 3 with `<script setup>`** components; TypeScript throughout. Build via Vue CLI (webpack).
- **Composables live in `src/hooks/`** and are the primary state/logic unit (e.g. `useSession`,
  `useConnection`, `useOperator`, `useCurrency`). Several use **module-level `ref`s** so state is
  shared as a singleton across components — be careful with lifecycle/reset.
- **Shared types & enums live in `src/types/`** (`ActiveSession`, `BodySessionBet`, `PaymentMethod`,
  `OperatorBranding`, `DefaultResponse<T>`, …). Mirror backend enums exactly (codes must match).
- **API access via `src/services/mainService.ts`** — a single axios instance with a request
  interceptor that sets `baseURL` (`VUE_APP_API_BASE_URL`) and attaches the `operator-id` header
  from `sessionStorage`. Add new endpoints here and export them from the default object.
- **Responses** are `DefaultResponse<T>` = `{ data, messages }`.
- **Real-time via `@microsoft/signalr`** in `useSession`: two hubs (`Sessions`, `RecentBets`) at
  `VUE_APP_WEBSOCKET_BASE_URL`; join per-session groups; handle `…/Terminal`, `…/DisplaySpeed`,
  `…/Status`, `…/Bets`, `SessionReceived`.
- **Solana wallet** via `solana-wallets-vue` + `@solana/web3.js` + `@solana/spl-token`
  (`useConnection.sendToken`). The returned signature is used as the bet's `externalReference`.
- **Styling:** Tailwind CSS + DaisyUI. Operator theming overrides `--color-secondary` at runtime.
- **Routing:** `src/routes.ts` — `/` → `/game-select` → `/game`.
- **Env vars** are `VUE_APP_*` (do not commit `.env*`; see `constraints.md`).

<!-- TODO: confirm component folder conventions, state management (Vuex usage), toast/error
     handling patterns, and add examples. -->
