# SpaceStroker — "In-place mobile wallet connect (Phantom + Solflare deep-link)" Feature — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [\[FRONT\] Connect wallet doesn't work on mobile](https://trello.com/c/Csb9U6bn) (2026-07-24).
> Repos affected: `monkify-front` (all code) and this doc in `monkify-meta`. **No `monkify-backend` change.**
>
> **Purpose:** Let a player on **standalone mobile web** connect a Solana wallet **in place** and fund a
> `SolanaToken` bet without being dumped out of the game into the wallet app's in-app browser. The player
> stays in their normal mobile browser (Safari/Chrome); a wallet action only ever causes a **brief hop** to
> the Phantom/Solflare app to approve, after which the browser returns to the **same game and session** and
> the bet resumes automatically.

---

## 1. Summary

Today the **only** way to fund a `SolanaToken` bet on standalone mobile web is the shipped
[`mobile-layout`] affordance: an "OPEN IN WALLET APP" button that fires a Phantom **universal link**
(`https://phantom.app/ul/browse/<page>`) which **relaunches the whole page inside Phantom's in-app
browser** (`utils/walletDeepLink.ts` → `openInWalletApp`). It works, but it yanks the player out of the
game, Solflare is never wired, and there is no in-place connect. There is **no** Mobile Wallet Adapter and
**no** WalletConnect anywhere in the repo.

This feature adds each wallet's **official mobile deeplink protocol** (Phantom + Solflare) with the required
encrypted `x25519` handshake, implemented with **`tweetnacl`** (already a dependency). The player stays in
their browser; the wallet app opens only to approve **connect** and **sign+send**, then redirects back.

| | Today (shipped `mobile-layout`) | This feature |
| --- | --- | --- |
| Connect on mobile | none — page is relaunched inside Phantom's in-app browser | in-place: `connect` deeplink → approve → return to same page |
| Wallets | Phantom only (hardcoded) | Phantom **and** Solflare (chooser) |
| Signing | happens inside the in-app browser via `sendToken` | `signAndSendTransaction` deeplink → return with signature |
| Stays in the player's browser? | **no** | **yes** (brief hop to wallet app only) |
| Session | n/a | persists in `localStorage` until Disconnect; repeat bets = **one** sign hop |
| Fallback | — | today's universal link kept as a **labeled** last resort |
| Backend | unchanged | **unchanged** |

The deeplink **return is a full page navigation**, so the crux of the work is a **reload-surviving**
pending-bet record (persisted before every hop) plus **auto-resume** on boot, and surfacing the deeplink
session as the app's **effective `publicKey` / `connected`** so the existing bet gate and identity UI work
unchanged. This mirrors the existing **embedded popup** branch (`useGame.bet()` +
`useEmbeddedWallet`) — a parallel "acquire signature out-of-band" path — so the change fits the established
shape rather than rebuilding the bet flow.

[`mobile-layout`]: ../features/mobile-layout.md

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** work lives in `.worktrees/mobile-wallet-connect/<repo>`; the shared checkout of
  each repo stays on `main` (hub). Branch `feature/mobile-wallet-connect` in `monkify-front` **and**
  `monkify-meta` (both already created + pushed).
- **Frontend-only feature → no backend integration test applies.** There is **no backend surface** (bet
  POST, `/rpc` proxy, on-chain settlement all unchanged), and the happy path needs a real device + wallet
  app, so — as with the shipped `mobile-layout` and `iframe-crypto-games` frontend-only features — **no
  `SpaceStroker.Tests.Integration` test is added**. Verify via frontend/device QA (§13). A dedicated
  `features/mobile-wallet-connect.md` KB doc is still created at ship.
- **Unit tests (frontend):** pure protocol/util logic (deeplink URL building, x25519 encrypt/decrypt
  round-trip, pending-bet (de)serialization, mobile-context detection) is unit-testable and **must** be
  covered; UI/redirect flows are covered by manual device QA (§11, §13).
- **Secrets/config:** any `.env`/`.env.example` values are **mock/placeholder** only. No real keys. The
  dapp `x25519` keypair is **ephemeral and generated at runtime** — it is **not** a secret to commit and is
  **not** the user's wallet key.
- **Operator messaging:** all new copy (connecting, cancel/error, round-changed, disconnect) stays
  **vague** — never reveal operator/contract status.
- **Frontend conventions:** composables in `src/hooks`, utils in `src/utils`, types in `src/types`; env
  vars are **`VUE_APP_*`** (this repo has **not** migrated to `VITE_*`).

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Target UX = brief round-trip.** A momentary switch to the wallet app to approve, then auto-return to the **same** game + session, is the desired behavior. The pain to remove is being dumped into the wallet's in-app browser. |
| 2 | **Approach = native wallet deeplink protocols** (Phantom + Solflare) with an encrypted `x25519` handshake via **`tweetnacl`**. **WalletConnect rejected** (needs a cloud project ID + relay; weak/unreliable Solana-Phantom support). **Mobile Wallet Adapter rejected** (Android-only; requirement is iOS **and** Android). |
| 3 | **Platforms = iOS + Android** (both wallets, both OSes). |
| 4 | **Wallets = Phantom + Solflare** only. |
| 5 | **Scope = standalone mobile web only.** Mobile **inside an iframe** (embedded) is **out of scope** and keeps the existing same-origin popup bridge. |
| 6 | **Session persists until disconnect** (`localStorage`): shared secret + wallet pubkey + wallet session token + ephemeral dapp keypair. Repeat bets need only **one** sign hop. A mobile **Disconnect** clears it. |
| 7 | **All mobile connect entry points route to the new flow** — one shared session from the header wallet button, the bet-panel connect, and the customization modal's "Connect wallet to customize" gate. |
| 8 | **Existing universal link kept as a labeled fallback** (protocol failure / unsupported wallet); Solflare wired into it too. |
| 9 | **Reload-surviving resume:** persist a pending-bet record before each hop and auto-resume on boot; the player lands back exactly where they were. |
| 10 | **Round-changed guard:** if the intended round ended/rolled over during a hop, abort + clear + plain message — never place the bet on a different round (best-effort UX; the on-chain money path is unchanged — see §6.5). |
| 11 | **Unified effective wallet state:** the deeplink session surfaces through the same `publicKey` / `connected` the app reads today (from `solana-wallets-vue`), so the bet gate (`walletGateBlocks`), `YOU`/recent-bets/winner identity, and the wallet-keyed customization name work identically to a desktop extension connection. |
| 12 | **Sign method:** prefer the wallet's **`signAndSendTransaction`** deeplink (the wallet signs **and** broadcasts); the SPL transfer tx is still **built in-page** by reusing `sendToken`'s idempotent-ATA + transfer + optional priority-fee + integer amount math; confirmation stays on the public connection. |

---

## 4. Data model

**No backend data model change.** No entity, EF mapping, migration, DTO, or SignalR contract is added or
altered. The bet POST body is unchanged (`{ amount, choice, paymentSignature, wallet, seed, nonce }`, per
`useGame.submitBet`), carrying the same `X-Launch-Token` header.

### 4.1 Client-side persisted shapes (new — `localStorage` / `sessionStorage`, not server state)

Two new client-only records (proposed `src/types/mobileWallet.ts`):

```ts
// Persisted for the life of a mobile wallet session (localStorage key e.g. "swMobileWalletSession").
export interface MobileWalletSession {
  walletApp: 'phantom' | 'solflare';
  walletPublicKey: string;        // base58 — the connected wallet address
  session: string;                // opaque session token returned by the wallet on connect
  sharedSecretB58: string;        // base58 x25519 shared secret (nacl.box.before)
  dappSecretKeyB58: string;       // base58 ephemeral dapp x25519 secret key (NOT a wallet key)
  dappPublicKeyB58: string;       // base58 ephemeral dapp x25519 public key
  connectedAt: number;            // epoch ms
}

// Persisted immediately BEFORE each deeplink hop so the bet survives the redirect/reload
// (localStorage key e.g. "swMobileWalletPendingBet").
export interface MobilePendingBet {
  phase: 'connect' | 'sign';      // what we hopped out to do
  walletApp: 'phantom' | 'solflare';
  parametersId: string;           // game-type id (to re-resolve the active round on return)
  gameId: string;                 // the specific round the player intended to bet on
  choice: string;
  amount: number;                 // token-native units
  seed: string;                   // provably-fair seed derived BEFORE the hop (do NOT re-derive on resume)
  nonce: number;                  // provably-fair nonce (already advanced — persist so it isn't advanced twice)
  receiver: string;               // tokenReceiverPublicKey
  mint: string;                   // tokenMintAddress
  network: string;                // VUE_APP_SOLANA_NETWORK
  createdAt: number;
}
```

> **Provably-fair note:** the seed/nonce come from `useClientSeed().nextBetSeed()`, which **advances the
> nonce**. Derive them **once** before the connect/sign hop and persist them in `MobilePendingBet` so the
> resume path reuses the same `{ seed, nonce }` and never double-advances. (See [`provably-fair-final`].)

[`provably-fair-final`]: ../features/provably-fair-final.md

---

## 5. Configuration

No **required** new env var. Optional, add to `monkify-front/.env.example` (mock values only) **only if**
the implementer needs an explicit dapp identity / redirect base rather than deriving them from
`window.location`:

- `VUE_APP_WALLET_DEEPLINK_APP_URL` — the `app_url` metadata shown by the wallet on connect (default
  `window.location.origin`).
- `VUE_APP_WALLET_DEEPLINK_REDIRECT_BASE` — base for `redirect_link` (default `window.location.origin` +
  current game path).

Reuses existing vars unchanged: `VUE_APP_SOLANA_NETWORK` (cluster passed to the deeplinks),
`VUE_APP_SOLANA_RPC_URL` / `VUE_APP_SOLANA_PROXY_URL` (blockhash + confirmation via the existing
`useConnection` failover), `VUE_APP_SOLANA_PRIORITY_FEE_MICROLAMPORTS`.

---

## 6. Backend behaviour

**None.** This feature adds no controller, handler, consumer, endpoint, header, migration, or settlement
change. The bet POST and the `/rpc` proxy behave exactly as today; on-chain signature validation and the
existing refund/settlement path are unchanged (relevant to §6.5).

---

## 7. Frontend behaviour

### 7.1 New: the wallet deeplink protocol util (`src/utils/mobileWalletProtocol.ts`)

Pure, framework-free functions (unit-testable) implementing the **Phantom** and **Solflare** deeplink
protocols. Both wallets follow the same encrypted shape; keep one module with a per-wallet base URL.

**Encryption (both wallets, via `tweetnacl` + base58):**
- `nacl.box.keyPair()` → the **ephemeral dapp** `x25519` keypair (public key sent to the wallet).
- On the wallet's response, derive the shared secret: `nacl.box.before(walletEncryptionPubKey, dappSecretKey)`.
- Decrypt response payloads: `nacl.box.open.after(ciphertext, nonce, sharedSecret)` → JSON.
- Encrypt request payloads (for `signAndSendTransaction`): `nacl.box.after(message, nonce, sharedSecret)`.
- Base58 encode/decode keys/nonces/ciphertext (add **`bs58`** if a direct import isn't already available;
  it is a transitive dep of `@solana/web3.js`).

**Connect deeplink (no payload):**
```
Phantom : https://phantom.app/ul/v1/connect?app_url=<app>&dapp_encryption_public_key=<b58>&redirect_link=<enc>&cluster=<network>
Solflare: https://solflare.com/ul/v1/connect?app_url=<app>&dapp_encryption_public_key=<b58>&redirect_link=<enc>&cluster=<network>
```
Response params appended to `redirect_link`:
`phantom_encryption_public_key` (Solflare: `solflare_encryption_public_key`), `nonce`, `data` →
decrypt `data` → `{ public_key, session }`.

**Sign-and-send deeplink (encrypted payload):**
```
<base>/ul/v1/signAndSendTransaction?dapp_encryption_public_key=<b58>&nonce=<b58>&redirect_link=<enc>&payload=<enc>
payload (before encryption) = { session, transaction: <base58 serialized UNSIGNED tx> }
```
Response params: `nonce`, `data` → decrypt → `{ signature }`. On user reject/error the wallet appends
`errorCode` / `errorMessage` instead — treat as a generic failure (never surface raw wallet text).

Exported helpers (names indicative):
`buildConnectDeeplink(walletApp, { dappPublicKeyB58, redirectLink, appUrl, cluster })`,
`parseConnectReturn(walletApp, params, dappSecretKeyB58)` → `{ walletPublicKey, session, sharedSecretB58 }`,
`buildSignAndSendDeeplink(walletApp, { dappPublicKeyB58, sharedSecretB58, session, txBase58, redirectLink })`,
`parseSignReturn(params, sharedSecretB58)` → `{ signature }`,
`detectDeeplinkReturn(params)` → `'connect' | 'sign' | 'error' | null`.

### 7.2 New: `src/hooks/useMobileWallet.ts` (module-scope singleton, mirrors `useConnection`/`useEmbeddedWallet`)

Owns the mobile deeplink session and the reload-surviving flow.

- **State:** `mobilePublicKey` (ref<string|null>), `mobileConnected` (computed), hydrated from
  `MobileWalletSession` in `localStorage` on first use.
- **`isMobileDeeplinkContext()`** — synchronous: `isMobileBrowser() && !isEmbedded && wallet.readyState !==
  Installed` (i.e. a real mobile browser with no injected provider, not in an iframe). Drives whether the
  mobile flow (vs desktop adapter / embedded popup) is used.
- **`connect(walletApp)`** — generate the ephemeral dapp keypair, persist a `MobilePendingBet`/connect
  marker if a bet is in flight, build the connect deeplink with `redirect_link` = current game URL, and
  `window.location.href = link` (the hop). Store the ephemeral secret key so the return can decrypt.
- **`signAndSendTransfer({ txBase58, ...ctx })`** — persist the `MobilePendingBet` (phase `sign`), build the
  sign deeplink, hop.
- **`disconnect()`** — clear the session + pending record from storage; reset refs.
- **`handleDeeplinkReturn()`** — called at app boot (§7.6): read URL params; if a connect return, finalize
  the session (persist `MobileWalletSession`), strip the params from the URL (`history.replaceState`), and
  **resume** the pending bet; if a sign return, decrypt the signature and resume `submitBet`; if an error
  return, clear pending + show the generic message; if nothing, no-op.
- **Effective wallet exposure:** export `effectivePublicKey` / `effectiveConnected` that OR the adapter's
  `publicKey`/`connected` with the mobile session, so consumers can read one source (§7.5).

### 7.3 `src/hooks/useConnection.ts` — extract a tx builder seam

Split the transaction **construction** out of `sendToken` so the mobile path can serialize an **unsigned**
tx for the wallet to sign+send:

- New `buildTransferTransaction(toPubkey, tokenAddress, value, feePayer)` → `{ tx: Transaction,
  latestBlockHash }` containing the current body of `sendToken` up to and including setting
  `recentBlockhash` + `feePayer` (idempotent ATA creation for sender+receiver, optional priority fee,
  transfer, integer `toBaseUnits`, blockhash via `withFailover`). **`feePayer` = the connected wallet
  pubkey** (adapter `publicKey` on desktop, `mobilePublicKey` on mobile).
- `sendToken` keeps its current behaviour by calling `buildTransferTransaction` then signing/sending as it
  does today (desktop/embedded byte-for-byte unchanged).
- The mobile path calls `buildTransferTransaction`, serializes the unsigned tx
  (`tx.serialize({ requireAllSignatures: false })` → base58), hands it to the `signAndSendTransaction`
  deeplink, and on return **confirms on the primary/public connection** (`getPrimaryConnection().confirmTransaction`).

### 7.4 `src/hooks/useGame.ts` — mobile branch in `bet()` + resume

Add a **third** acquisition branch alongside the existing standalone (`sendToken`) and embedded (popup)
branches in the `PaymentMethod.SolanaToken` block (around `useGame.ts:413-478`):

```
if (isMobileDeeplinkContext() && !mobileConnected) → persist pending bet (phase 'connect'); connect(walletApp); return  // hop
if (isMobileDeeplinkContext() &&  mobileConnected) → build unsigned tx; persist pending bet (phase 'sign'); signAndSendTransfer(...); return  // hop
```

The function **returns** after firing a hop (the page navigates away). The **resume** logic — invoked by
`handleDeeplinkReturn()` on boot — reconstructs `submitBet(gameId, choice, { amount, choice,
paymentSignature: signature, wallet: mobilePublicKey, seed, nonce })` from the persisted `MobilePendingBet`,
after the **round-changed guard** (§6.5 / below). Reuse the existing `pendingCryptoBet`-style stash pattern,
but backed by `localStorage` (survives reload) rather than an in-memory ref.

**Round-changed guard (on resume):** before `submitBet`, re-resolve the active round for `parametersId`; if
the active `gameId` differs or the round no longer accepts bets, **do not** submit — clear the pending
record and set a plain message (e.g. "That round ended before your bet went through"). Note: for a **sign**
resume the on-chain transfer already happened; the existing backend refund/settlement path (unchanged)
handles the funds exactly as it does for a desktop sign that outlives its round (§6.5). For a **connect**
resume, no money has moved, so this is purely a clean re-entry.

### 7.5 Effective wallet identity — one read for gate + identity

Today `GameConsole.walletGateBlocks` (`GameConsole.vue:67-68`) blocks `placeBet` when
`requiresWallet && !connected && !isFreeBetActive && !isEmbedded`, and identity UI keys off adapter
`publicKey`. Relax/extend both to the **effective** wallet so the mobile session counts as connected:

- `walletGateBlocks` → also false when `mobileConnected` (parallels the existing `!isEmbedded` relaxation).
- `YOU` / recent-bets / winners / customization identity read `effectivePublicKey` (adapter OR mobile).
  Because SolanaToken identity is already the **wallet** everywhere (see [`wallet-display-name`]), feeding
  the mobile wallet into `effectivePublicKey` makes the name/`YOU` resolution work with no other change.

[`wallet-display-name`]: ../features/wallet-display-name.md

### 7.6 Entry points & UI

- **`src/components/game/GameConsole.vue`** — replace the single hardcoded "OPEN IN WALLET APP" primary
  action (`canDeepLinkWallet` block, `GameConsole.vue:204-263`) with a **Phantom/Solflare chooser** that
  calls `connect(walletApp)`; once `mobileConnected`, PLACE BET routes through the mobile sign path. The
  existing universal-link `openInWalletApp` becomes the **labeled fallback** ("Open in wallet app instead").
  Generalize the gate from `canDeepLinkWallet` to the mobile-context check.
- **`src/components/header/WalletButtons.vue`** — on mobile (`isMobileDeeplinkContext()`), the header button
  opens the Phantom/Solflare chooser / shows connected + **Disconnect** via `useMobileWallet` instead of the
  `solana-wallets-vue` `WalletMultiButton` (which lists only extensions and does nothing useful on mobile).
  Desktop path unchanged.
- **`src/components/header/PlayerCustomizationModal.vue`** — the "Connect wallet to customize" gate (from
  [`wallet-display-name`]) uses the mobile connect flow on mobile; the modal's identity reads
  `effectivePublicKey`. (The modal already scrolls on mobile per `mobile-layout`.)
- **`src/App.vue` / app boot** — call `useMobileWallet().handleDeeplinkReturn()` **early**, after the
  launch-token bootstrap, so the launch-token re-mint (per [`main-production-operator`]) still runs on the
  post-hop reload and the resumed `submitBet` carries a valid `X-Launch-Token`. Strip deeplink params from
  the URL after handling.
- **`src/utils/walletDeepLink.ts`** — keep as the fallback; `openInWalletApp` already supports `'solflare'`,
  so wire a Solflare fallback option in the chooser.

### 7.7 User-facing copy (all vague)

Connecting… · "Approve in your wallet app" · generic cancel/error ("There was an error processing your bet")
· round-changed ("That round ended before your bet went through") · Disconnect. No operator/contract status
in any string.

[`main-production-operator`]: ../features/main-production-operator.md

---

## 8. Flow diagrams

### 8.1 First bet on mobile (connect hop, then sign hop)

```mermaid
sequenceDiagram
  participant U as Player (mobile Safari/Chrome)
  participant App as SpaceStroker SPA
  participant W as Phantom/Solflare app
  participant Chain as Solana

  U->>App: Tap PLACE BET (SolanaToken, not connected)
  App->>App: persist MobilePendingBet(phase=connect, seed/nonce)
  App->>W: connect deeplink (dapp x25519 pubkey, redirect_link)
  W-->>U: approve connect
  W->>App: redirect_link?...&data (encrypted)  [page reload]
  App->>App: boot → handleDeeplinkReturn(): decrypt → session; resume
  App->>App: build UNSIGNED transfer tx (reuse sendToken builder)
  App->>App: persist MobilePendingBet(phase=sign)
  App->>W: signAndSendTransaction deeplink (encrypted tx)
  W->>Chain: sign + broadcast
  W->>App: redirect_link?...&data(signature)  [page reload]
  App->>App: boot → decrypt signature; round-changed guard
  App->>Chain: confirmTransaction (public connection)
  App->>App: submitBet({ paymentSignature, wallet, seed, nonce })
```

### 8.2 Repeat bet (session persisted → one hop)

```mermaid
sequenceDiagram
  participant U as Player
  participant App as SPA
  participant W as Wallet app
  U->>App: Tap PLACE BET (mobileConnected from localStorage)
  App->>App: build unsigned tx; persist pending(phase=sign)
  App->>W: signAndSendTransaction deeplink
  W->>App: redirect back with signature  [reload]
  App->>App: confirm + submitBet
```

---

## 9. Out of scope

- **Embedded / iframe** mobile crypto bets (stays on the existing same-origin popup bridge —
  `useEmbeddedWallet` / `WalletBridgePage`; separate card if needed).
- **WalletConnect** and **Mobile Wallet Adapter** (evaluated and rejected — decisions 2).
- Any **backend** change (bet POST, `/rpc` proxy, on-chain settlement, refunds, entities, migrations).
- **Desktop** wallet-connect (the `solana-wallets-vue` extension adapter flow is unchanged).
- **Standard/USD** games and **free** (amount 0) bets (no wallet involved).
- Wallet brands beyond **Phantom + Solflare**.
- Solving the "round ends mid-sign after funds moved" case with new logic — parity with today's
  desktop/embedded risk; the existing refund path applies (§6.5).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — frontend-only feature.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/utils/mobileWalletProtocol.ts` | **New.** Phantom/Solflare connect + signAndSendTransaction deeplink builders + response decrypt (x25519 via `tweetnacl` + base58); `detectDeeplinkReturn`. |
| 2 | `src/hooks/useMobileWallet.ts` | **New.** Module-scope session singleton: `mobileConnected`/`mobilePublicKey`, `isMobileDeeplinkContext`, `connect`/`signAndSendTransfer`/`disconnect`, `handleDeeplinkReturn` (resume), `localStorage` persistence, `effectivePublicKey`/`effectiveConnected`. |
| 3 | `src/types/mobileWallet.ts` | **New.** `MobileWalletSession`, `MobilePendingBet`, `WalletApp` re-export. |
| 4 | `src/hooks/useConnection.ts` | Extract `buildTransferTransaction(...)` (unsigned tx + blockhash, `feePayer` param) from `sendToken`; `sendToken` reuses it (desktop unchanged). Export the builder. |
| 5 | `src/hooks/useGame.ts` | `bet()` SolanaToken block: add the mobile connect/sign branches; move the pending-bet stash to `localStorage`-backed resume; round-changed guard before the resumed `submitBet`; use `mobilePublicKey` as `wallet`. |
| 6 | `src/components/game/GameConsole.vue` | Replace hardcoded `openWalletApp('phantom')` with a Phantom/Solflare chooser → `connect`; generalize `canDeepLinkWallet` to the mobile-context check; relax `walletGateBlocks` for `mobileConnected`; universal link demoted to labeled fallback. |
| 7 | `src/components/header/WalletButtons.vue` | Mobile branch: chooser + connected/Disconnect via `useMobileWallet`; desktop `WalletMultiButton` path unchanged. |
| 8 | `src/components/header/PlayerCustomizationModal.vue` | "Connect wallet to customize" gate uses the mobile flow; identity reads `effectivePublicKey`. |
| 9 | `src/App.vue` | Call `handleDeeplinkReturn()` early at boot (after launch-token bootstrap); strip deeplink params from the URL. |
| 10 | `src/utils/walletDeepLink.ts` | Keep as fallback; ensure Solflare option is reachable from the chooser fallback (no behavior change to the existing functions). |
| 11 | `.env.example` | Optional mock-only `VUE_APP_WALLET_DEEPLINK_*` vars if explicit app-url/redirect base is used. |
| 12 | `package.json` | Add `bs58` explicitly **if** a direct base58 import isn't resolvable via existing deps. `tweetnacl` already present. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/mobile-wallet-connect.md` | **New.** This spec. |
| 2 | `INDEX.md` | Add the spec row (status Ready) + set "Feature currently in flight". |
| 3 | `features/mobile-wallet-connect.md` | **Created at ship** by `/monkify-ship-feature` (not now). |

---

## 11. Unit tests

**No `SpaceStroker.Tests` (backend) change** — frontend-only feature. Add **frontend** unit tests for the
pure logic (project: `monkify-front`, existing test runner). UI redirect flows are verified via device QA
(§13).

| Area | Test cases | Expected |
| --- | --- | --- |
| `mobileWalletProtocol` connect URL | build Phantom + Solflare connect deeplinks | correct base host, `dapp_encryption_public_key`, encoded `redirect_link`, `cluster` from network |
| `mobileWalletProtocol` sign URL | build signAndSendTransaction deeplink | correct host, `nonce`, encrypted `payload` present, encoded `redirect_link` |
| x25519 round-trip | encrypt a payload with a shared secret, decrypt it | decrypted JSON equals the input (using two `nacl.box` keypairs) |
| `parseConnectReturn` | given wallet encryption pubkey + nonce + encrypted `data` | returns `{ walletPublicKey, session, sharedSecretB58 }`; throws/erks on tampered `data` |
| `parseSignReturn` | encrypted signature payload / error params | returns `{ signature }`; error params → thrown/flagged as generic failure |
| `detectDeeplinkReturn` | connect params / sign params / error params / none | returns `'connect' \| 'sign' \| 'error' \| null` |
| `MobilePendingBet` (de)serialize | round-trip through `localStorage` string | equal object; seed/nonce preserved (no re-derivation) |
| `isMobileDeeplinkContext` | mobile UA + not embedded + no injected wallet vs desktop/embedded | true only in the mobile standalone no-extension case |

---

## 12. Acceptance criteria

(1:1 from the Trello **Acceptance Criteria** checklist.)

1. Given a standalone mobile browser on a SolanaToken game with no wallet connected, when the player opens any connect entry point (header wallet button, bet-panel connect, or the customization modal's "Connect wallet to customize" gate), then a Phantom/Solflare chooser appears and selecting one opens that wallet app via its native deeplink — not a relaunch into the wallet's in-app browser.
2. Given the player approves the connect request in the wallet app, when the browser returns to the game, then the app shows the wallet as connected (its address surfaced as the effective publicKey) and the player remains on the same game screen and session.
3. Given a connected mobile wallet session, when the player places a paid SolanaToken bet, then the app builds the SPL transfer, opens the wallet once to sign+send, returns to the game, confirms the transaction, and submits the bet with the resulting signature — placing exactly the intended bet on the intended round.
4. Given the round the player intended to bet on has ended or rolled over during the wallet hop, when the browser returns with the signature, then the app does not place a bet on a different round; it aborts, clears the pending bet, and shows a clear non-technical message.
5. Given a persisted mobile wallet session, when the player places a second bet (same visit or a later page load), then no reconnect is required — only a single sign hop occurs.
6. Given a connected mobile wallet, when the player taps Disconnect, then the stored session is cleared and all connect entry points return to the disconnected state.
7. Given the player cancels or rejects the request in the wallet app, or the browser returns with an error, when control returns to the game, then a generic error is shown, the bet lever resets, no stuck/pending state remains, a retry starts cleanly, and operator/contract status is never revealed.
8. Given a mobile SolanaToken flow where the native deeplink protocol cannot be used (unsupported wallet or protocol failure), when the player attempts to connect or bet, then the existing "OPEN IN WALLET APP" universal-link fallback is offered, clearly labeled as a last resort.
9. Given a free SolanaToken bet (amount 0) or a Standard/USD game on mobile, when the player bets, then no wallet connection is required and the flow is unchanged from today.
10. Given desktop (browser-extension) play or the embedded/iframe context, when a player connects or bets, then behavior is unchanged from today — the new deeplink flow is scoped to standalone mobile web only.
11. Given a mobile wallet connected via the deeplink flow, when identity-dependent UI renders (the YOU label, recent-bets rows, the winners list, and the customization display name keyed on the wallet), then it reflects the connected wallet identically to a desktop extension connection.

---

## 13. Manual QA (device)

Real devices required (deeplinks can't run in a desktop emulator or Testcontainers). Point the mobile
browser at the local/dev stack over LAN or a tunnel; use a devnet wallet holding the game's token (see the
"Local paid SolanaToken bet" runbook for funding a devnet token account).

1. **iOS Safari + Phantom** — open a SolanaToken game, tap PLACE BET → chooser → Phantom → approve connect →
   land back on the same game (AC 1, 2). Tap PLACE BET → Phantom sign → back → bet lands on the intended
   round (AC 3). Second bet = one hop only (AC 5).
2. **Android Chrome + Solflare** — repeat the connect + bet flow via Solflare (AC 1, 3).
3. **Cancel** in the wallet app on both connect and sign → generic error, clean retry, no stuck state (AC 7).
4. **Round-changed** — start a bet near round end so the round rolls during the hop → plain message, no bet
   on the new round (AC 4).
5. **Disconnect** from the header → connect points reset (AC 6).
6. **Fallback** — force an unsupported/failed protocol path → labeled "open in wallet app" fallback (AC 8).
7. **Identity** — set a display name, confirm `YOU` + name render on your mobile-placed bet like desktop
   (AC 11).
8. **Unaffected** — a free SolanaToken bet and a Standard game place with no wallet (AC 9); desktop +
   embedded iframe unchanged (AC 10).

---

## 14. Trello / pipeline

- **Source card:** [\[FRONT\] Connect wallet doesn't work on mobile](https://trello.com/c/Csb9U6bn) — list
  **Specs created**; the **Implementation** section on the card carries branch + spec + worktree + Level.
- **Level:** `Feature` (frontend-only; no backend integration test applies — §2).
- **Next:** `/monkify-implement-feature [FRONT] Connect wallet doesn't work on mobile` to build it, then
  `/monkify-ship-feature ...` to create `features/mobile-wallet-connect.md`, sync the KB, and merge
  `feature/mobile-wallet-connect` → `main`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-24 | Initial spec from refined card #92 (Approach A — Phantom + Solflare native deeplink protocols; frontend-only; grounded in a full read of `useConnection`, `useGame.bet`, `GameConsole`, `WalletButtons`, `useEmbeddedWallet`, `walletDeepLink`). |
