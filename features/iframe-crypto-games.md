# Feature — Embedded (iframe) crypto (SolanaToken) games

> Status: **shipped.** Authoritative spec: `../specs/iframe-crypto-games.md`
> (merged via `feature/iframe-crypto-games` — stacked on [[iframe-embedding]] — on 2026-07-14).

## What it does

Lets a player place a **paid on-chain `SolanaToken` bet from inside the embedded `<iframe>`** delivered
by [[iframe-embedding]]. Browser wallet extensions (Phantom / Solflare) refuse to inject into a
cross-origin iframe, so the bet-time **wallet connect + SPL transfer + signing move into a same-origin
popup on the game host** (a top-level window where the extension *does* inject); the popup returns only
the on-chain signature + wallet to the iframe, and the existing bet POST runs unchanged. **Frontend-only**,
no backend surface. Builds directly on [[iframe-embedding]] and reuses its embed detection, token handoff,
and lifecycle `postMessage` machinery — it adds **no new parent-facing contract**, only an internal
same-origin iframe↔popup channel. See [[payment-methods]] for what a `SolanaToken` game is.

## Core rules

- **Adapt, don't rebuild:** the standalone `solana-wallets-vue` connect + `useConnection.sendToken` +
  `useGame.bet()` path is reused verbatim. Only *how the wallet connection + signature are acquired in
  embedded mode* is new; the SPL transfer, signature handling, and bet POST are unchanged.
- **Try in-frame first, else popup:** on a paid SolanaToken bet, if `isEmbedded && !inFrameWalletAvailable()`
  → popup handoff; otherwise (standalone, or an extension is actually present in-frame) → the existing
  in-frame `sendToken` path. `inFrameWalletAvailable()` is **synchronous**
  (`connected || wallet.readyState === 'Installed'`). In a real cross-origin iframe no adapter injects →
  popup.
- **Popup is same-origin and opened synchronously in the bet-click gesture** (dodges popup-blockers) at
  `<origin>/space-stroker/wallet-bridge`. It carries **no launch token and no operator context** in its URL. If
  the browser still blocks it, a visible **"Connect wallet"** affordance appears — **never a silent
  failure**.
- **Internal handshake envelope `{ source: 'space-stroker-wallet', … }`** (distinct from the parent-facing
  `spacestroker` envelope), strictly **same-origin**: both sides target and verify `window.location.origin` and
  check `event.source` (opener ↔ popup). Messages: `bridge-ready` → `sign-request`
  `{ receiver, mint, amount, network }` → `sign-result` `{ signature, wallet }` (or `sign-error` /
  `cancelled`). Payloads carry **only public data** (mint / receiver / amount / network / signature /
  wallet) — never the launch token or any secret.
- **Cancel is clean:** closing the popup before it returns a signature (watched via `popup.closed`) aborts
  the bet with no stuck state; the lever resets and a second pull re-opens the popup.
- **Free SolanaToken games (amount 0) embed with no wallet and no popup** — the free path returns before
  any wallet logic (verified, no new work).
- **Amounts are token-native:** embedded SolanaToken amounts render and are emitted in the token's own
  units (`tokenSymbol` / `tokenDecimals`), **never USD**. This makes [[iframe-embedding]]'s lifecycle
  payloads (`game-started` / `bet-placed` / `bet-settled`) **method-aware** — `currency` is the token
  symbol for SolanaToken, `'USD'` for Standard — with no new event types.
- **The in-frame `connected` bet gate is relaxed when embedded** (the wallet lives in the popup, so an
  in-frame connection is not required to start the bet); standalone the gate is identical.
- **Vague messaging holds:** a 401 / expired token on the bet POST while embedded goes through
  [[iframe-embedding]]'s `token-expired` + relaunch path; contract status is never revealed.
- **The wallet-bridge route is registered in production too** and boots without operator/launch context
  (`App.vue` runs `loadOperator` only for the Game/Game routes), so the popup can never dead-end on
  `/error`.
- **No backend change:** SolanaToken settlement (on-chain signature validation) already runs API-side; the
  frame changes nothing server-side (the bet POST carries the same `X-Launch-Token` + `wallet` +
  `paymentSignature` as standalone).

## Key code touchpoints

- `monkify-front/src/hooks/useEmbeddedWallet.ts` — **new.** `inFrameWalletAvailable()` (sync),
  `openWalletPopup()` (sync, same-origin `/space-stroker/wallet-bridge`), `requestSignatureFromPopup()`
  (handshake + origin/source validation + `popup.closed` cancel watch), `walletPopupBlocked` state.
- `monkify-front/src/pages/WalletBridgePage.vue` — **new.** The popup page: posts `bridge-ready`, shows the
  wallet connect UI, on `sign-request` runs `startConnection()` + `sendToken` → posts `sign-result` +
  `window.close()`; generic `sign-error` on reject/fail.
- `monkify-front/src/utils/walletBridge.ts` — **new.** `WALLET_BRIDGE_SOURCE = 'space-stroker-wallet'`, the
  `WalletBridgeMessage` union, same-origin targeting/validation.
- `monkify-front/src/hooks/useGame.ts` — `bet()` SolanaToken branch acquires the signature in-frame
  (`sendToken`) **or** via popup; plus the method-aware `currency`/amount on the [[iframe-embedding]]
  lifecycle emits (`tokenSymbol` for SolanaToken).
- `monkify-front/src/components/game/GameConsole.vue` — relaxes the in-frame `connected` gate in
  `placeBet()` when embedded; adds the popup-blocked "Connect wallet" affordance.
- `monkify-front/src/routes.ts` — `WalletBridge` route `/space-stroker/wallet-bridge` → `WalletBridgePage.vue`
  (registered in prod).
- `monkify-front/public/embed-example.html` + `monkify-front/EMBEDDING.md` — extend [[iframe-embedding]]'s
  example page + README with the SolanaToken in-frame-then-popup flow.
