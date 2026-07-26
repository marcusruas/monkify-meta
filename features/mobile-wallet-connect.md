# Feature — Mobile wallet connect (single-modal, multi-wallet deeplink)

> Status: **shipped.** Authoritative spec: `../specs/mobile-wallet-connect.md`
> (merged via `feature/mobile-wallet-connect` on 2026-07-25). **Frontend-only** (`monkify-front`); no
> backend, DB, SignalR, or game-flow change. Supersedes the old `mobile-layout` "OPEN IN WALLET APP"
> affordance.

## What it does

Lets a player on **standalone mobile web** (Safari/Chrome, no extension) connect a Solana wallet and
fund a `SolanaToken` bet from a single **"Connect Wallet"** button that opens one wallet-picker modal.
Two tiers of wallet, and — for the three protocol wallets — a **connect-method toggle**: *Stay in
browser* (native encrypted deeplink; brief hop to the wallet app to approve, then back to the same
game) or *Open in wallet app* (the game runs inside the wallet's own in-app browser). iOS returns to
the same tab; on Android the deeplink return unavoidably opens a new browser tab (see rules), so the
modal shows a disclaimer and offers the in-app option. Desktop (extension) and embedded/iframe are
unchanged.

## Core rules

- **Scope = standalone mobile web only.** Desktop browser-extension play and the embedded/iframe
  same-origin popup bridge are untouched. Free (`amount` 0) `SolanaToken` and Standard/USD games need
  no wallet.
- **One entry, one modal.** A single "Connect Wallet" button opens `MobileWalletModal`; every mobile
  connect entry point (header wallet button, bet-panel connect, the customization "connect to
  customize" gate) opens the **same** modal via the shared `walletModalOpen` state.
- **Tier 1 — native deeplink (Phantom, Solflare, Backpack).** All three publish the same encrypted
  `x25519` handshake (`tweetnacl`) + session protocol, so **one adapter** covers them (only the base
  host + response-param prefix differ). Each Tier-1 wallet honours the modal's **connect-method
  toggle**:
  - *Stay in browser* (default): `connect` deeplink → approve → return; repeat bets are a single
    `signAllTransactions` hop.
  - *Open in wallet app*: opens the game inside that wallet's in-app browser (the Tier-2 path).
- **Tier 2 — in-app browser only (Coinbase, Trust, OKX, Bitget).** No stay-in-browser protocol; each
  has a one-tap "open this URL in my dApp browser" deeplink. The game reloads inside the wallet's
  webview where the injected provider + the existing `useConnection.sendToken` flow run unchanged.
  Glow / Ultimate / Exodus are **not** listed (WalletConnect-only; WalletConnect + MWA were rejected).
- **Signing = `signAllTransactions` + self-broadcast.** The transfer tx is built in-page
  (`buildTransferTransaction`, reusing `sendToken`'s idempotent-ATA + integer-amount + optional
  priority-fee math); the wallet **signs only**; the app broadcasts via
  `useConnection.broadcastSignedTransaction` (keeps the 429 failover) and confirms on the public
  connection. `signAndSendTransaction` is **not** used — it is deprecated and fails on devnet.
- **Round-changed guard runs before broadcast.** On the sign return the app holds the signed-but-unsent
  tx; if the intended round ended/rolled over during the hop it does **not** broadcast, clears the
  pending bet, and shows a plain message ("nothing was charged") — never spends on a dead round.
- **Reload-surviving resume (the connect-loop fix).** The deeplink return is a full page load (and on
  Android usually a **new tab**). `main.ts` stashes the wallet's return query to **`localStorage`**
  (not `sessionStorage`) *before the router exists* so the redirect can't strip it and a new tab can
  still read it. The deeplink `redirect_link` keeps the current **`?type=<game>`**, so the returned tab
  loads the exact game directly (`GamePage`), not the lobby. A pending-bet record (choice, amount,
  seed/nonce, wallet, dapp keypair) is persisted before each hop and auto-resumed on boot; a stale hop
  (older than ~5 min) is discarded.
- **Bet shows without a manual refresh.** Because the return reloads the SPA, the sign-resume **waits
  for the reloaded game view to reconnect its live `/Bets` SignalR stream** before submitting, so the
  just-placed bet's echo is caught and rendered.
- **Session persists until Disconnect.** The encrypted session (shared secret + wallet pubkey + wallet
  session token + ephemeral dapp keypair) lives in `localStorage`; repeat bets need only one sign hop.
  **Disconnect** is a deliberate two-step (the connected header button opens a small menu showing the
  address + an explicit Disconnect) — never a single-tap.
- **Auto-connect inside a wallet's in-app browser.** On mobile an injected Solana provider only exists
  inside a wallet's webview, so when the game loads there the app auto-selects + connects it (best
  effort) — the *Open in wallet app* path (and Tier 2) spawns already connected.
- **Unified effective wallet.** The deeplink session surfaces through the same **effective
  `publicKey` / `connected`** the app reads from `solana-wallets-vue`, so the bet gate
  (`walletGateBlocks`), `YOU` labels, recent-bets/winner identity, the wallet-keyed customization name,
  and the **token balance** all work identically to a desktop connection.
- **Android new tab is unavoidable, made harmless.** Phantom/Solflare/Backpack fire a fresh intent at
  the `https` return URL and Chrome doesn't reuse the tab (an OS/Chrome limitation — `intent://`
  breaks their param append and `window.close()` is blocked on the return tab). The returned tab lands
  on the correct game, connected, with the bet visible; the modal warns about it (in-place mode on
  Android) and points to the in-app option. iOS returns in place.
- **Errors stay vague and non-blocking.** Cancel/reject → generic message, lever resets, no stuck
  state, clean retry. A blockhash that expired during a slow approval → a plain "approval took too
  long — tap PLACE BET to try again" (the session persists, so retry is one hop). No operator/contract
  status is ever revealed.
- **No new npm dependency.** base58 is inlined in `mobileWalletProtocol.ts`; `tweetnacl` was already a
  dependency. Keeps the Docker `yarn install --frozen-lockfile` build intact.

## Key code touchpoints

- `monkify-front/src/utils/mobileWalletProtocol.ts` — **new.** Tier-1 `connect` + `signAllTransactions`
  deeplink builders (Phantom/Solflare/Backpack), x25519 encrypt/decrypt via `tweetnacl` + **inlined**
  base58, `detectDeeplinkReturn`, `DEEPLINK_RETURN_PARAMS`.
- `monkify-front/src/utils/walletDeepLink.ts` — `isMobileBrowser()` + **`isAndroid()`**; generalized
  `WALLET_BROWSE_LINKS` + `openInWalletBrowser()` for the in-app-browser path (Tier 1 fallback + all of
  Tier 2); legacy `openInWalletApp` kept.
- `monkify-front/src/hooks/useMobileWallet.ts` — **new.** Module-scope session singleton:
  `connectMobileWallet` / `openInWallet` / `signMobileTransfer` / `disconnectMobileWallet`,
  `finalizeConnectReturn`, `localStorage` session + return stash, `effectivePublicKey` /
  `effectiveConnected`, shared `walletModalOpen` + `openWalletModal`/`closeWalletModal`,
  `isMobileDeeplinkContext`, stale-hop discard.
- `monkify-front/src/types/mobileWallet.ts` — **new.** `WalletApp` (`phantom|solflare|backpack`),
  `Tier2Wallet`, `MobileWalletSession`, `MobileConnectAttempt`, `MobilePendingBet`.
- `monkify-front/src/hooks/useConnection.ts` — `buildTransferTransaction` (unsigned tx + `feePayer`),
  `broadcastSignedTransaction` (self-broadcast + confirm via failover), `confirmSignatureOnPrimary`;
  `getTokenBalance(tokenAddress, owner?)` accepts an owner override (the mobile wallet).
- `monkify-front/src/hooks/useGame.ts` — `bet()` mobile connect/sign branches;
  `handleMobileDeeplinkReturn` (finalize connect / resume sign: `waitForRoundLoaded` → round guard →
  broadcast → `submitBet`).
- `monkify-front/src/components/wallet/MobileWalletModal.vue` — **new.** The single picker: 2 tiers, the
  connect-method toggle, the Android new-tab disclaimer, the store-link fallback.
- `monkify-front/src/components/game/GameConsole.vue` — mobile "CONNECT WALLET" → `openWalletModal`;
  `walletGateBlocks` relaxed for `mobileConnected`; PLACE BET routes through the mobile sign path.
- `monkify-front/src/components/header/WalletButtons.vue` — mobile "Connect Wallet" → modal; connected
  → address + explicit **Disconnect** menu.
- `monkify-front/src/components/header/PlayerCustomizationModal.vue` — the connect gate renders
  `WalletButtons` (→ the modal on mobile); identity reads `effectivePublicKey`.
- `monkify-front/src/components/TokenBalance.vue` — reads the **effective** wallet (desktop adapter OR
  mobile session).
- `monkify-front/src/main.ts` — pre-router `localStorage` stash of the wallet return params.
- `monkify-front/src/App.vue` — `handleMobileDeeplinkReturn` at boot; auto-connect inside a wallet's
  in-app browser; `MobileWalletModal` rendered once.
- `monkify-front/src/pages/GamePage.vue` — a deeplink-return load (params, no `?type=`) routes to the
  pending game instead of the lobby.
