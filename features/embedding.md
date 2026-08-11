# Domain — Embedding (running the game inside someone else's page)

> **What this covers.** A third-party site embeds the Space Stroker game in an `<iframe>` on its own
> page: how the SPA detects it is framed, the `postMessage` contract it speaks to the parent, and how a
> **paid on-chain `SolanaToken` bet** is placed from inside a frame where wallet extensions refuse to
> inject. **Frontend-only** — no backend, DB, SignalR or game-flow surface.
>
> Deep design: [`../specs/iframe-embedding.md`](../specs/iframe-embedding.md) (the frame + contract) and
> [`../specs/iframe-crypto-games.md`](../specs/iframe-crypto-games.md) (the wallet popup, stacked on it).
> Both shipped via `feature/iframe-crypto-games` on 2026-07-14 — one merge landed both.

> ⚠️ **Reduced by [`remove-operators.md`](remove-operators.md) (2026-07-30).** The embed **mechanics are
> kept** — `useEmbed` keys off `window.self !== window.top`, never off any operator row — but everything
> that depended on a launch token is gone: there is no token to pass in the URL fragment, no
> `token-expired` / "relaunching" branch, and no inactive-operator case. **An embedding site now just
> points an iframe at the app.** Lines below marked *(historical)* describe the removed launch-token
> flow and are kept only because the spec archives still reference them.

## Detection and framing

- **Embedded detection is runtime, not a flag:** `isEmbedded = window.self !== window.top` (or
  `?embed=1` for local testing), evaluated once at load. Loaded standalone, behaviour is exactly as
  before and **no `postMessage` is emitted**.
- **Fills the frame:** `html, body, #app { height: 100% }`, so the game fills 100%×100% of the host's
  box with no letterbox.
- **No frame-busting:** in embedded mode no in-app action performs a top-level navigation.
- **Frame header:** the committed `nginx.conf` sends `Content-Security-Policy: frame-ancestors *` and no
  `X-Frame-Options`. ⚠️ The production host must match — a **deploy-time dependency**, not fully
  committable in-repo.
- *(historical)* Token handoff was `…/game#launch_token=<jwt>`, captured by `useLaunch` into
  `sessionStorage`; the signed RS256 token, not the frame header, was the security boundary. On a 401 the
  app emitted `token-expired` and showed an in-frame "Relaunching…" state instead of routing to `/error`.

## The parent contract

Stable envelope **`{ source: 'space-stroker', type, payload }`**, `targetOrigin '*'`, **no secrets or
tokens in any payload**. Events: `ready`, `game-started`, `bet-placed`, `bet-settled`, `game-ended`,
`idle-disconnected`, a one-time `resize` size **hint** (not continuous auto-height), and
*(historical)* `token-expired`.

- **`bet-placed` is the embedded player's own bet** (emitted after a successful `applyBet`) — a
  balance-refresh hint, not every bet on the `/Bets` stream.
- **`bet-settled` is bettors-only**, the same gate as the victory/lose one-shot. Spectators get
  `game-ended` but no `bet-settled`.
- **`idle-disconnected` (`{ gameId }`)** fires when the idle rule closed the frame's realtime
  connections to save resources — a parent casino tab is exactly where tabs get left open. It is **not
  an error** and needs no action; the frame shows its own reconnect prompt and stays put. There is
  deliberately **no matching "resumed" event** — the normal `game-started` / `bet-*` / `game-ended`
  events simply resume.
- **Amounts are method-aware:** token-native (`tokenSymbol` / `tokenDecimals`) for `SolanaToken`,
  `'USD'` for the *(historical)* `Standard` method — with **no new event types**.

## Paid crypto bets inside the frame

Browser wallet extensions (Phantom / Solflare) **refuse to inject into a cross-origin iframe**, so
bet-time wallet connect + SPL transfer + signing move into a **same-origin popup on the game host** — a
top-level window where the extension does inject. The popup returns only the on-chain signature +
wallet; the existing bet POST runs unchanged. This adds **no new parent-facing contract**, only an
internal iframe↔popup channel.

- **Adapt, don't rebuild:** the standalone `solana-wallets-vue` connect + `useConnection.sendToken` +
  `useGame.bet()` path is reused verbatim. Only *how the signature is acquired* is new.
- **Try in-frame first, else popup:** if `isEmbedded && !inFrameWalletAvailable()` → popup handoff;
  otherwise the existing in-frame `sendToken` path. `inFrameWalletAvailable()` is **synchronous**
  (`connected || wallet.readyState === 'Installed'`).
- ⚠️ **The popup is opened synchronously inside the bet-click gesture** — that is what dodges
  popup-blockers. If the browser still blocks it, a visible **"Connect wallet"** affordance appears —
  **never a silent failure**.
- **Internal handshake envelope `{ source: 'space-stroker-wallet', … }`** — deliberately distinct from
  the parent-facing envelope — and strictly **same-origin**: both sides target and verify
  `window.location.origin` and check `event.source` (opener ↔ popup). Messages: `bridge-ready` →
  `sign-request { receiver, mint, amount, network, decimals }` → `sign-result { signature, wallet }`
  (or `sign-error` / `cancelled`). Payloads carry **only public data** — never a secret.
- **Cancel is clean:** closing the popup before it returns a signature (watched via `popup.closed`)
  aborts the bet with no stuck state; the lever resets and a second pull re-opens it.
- **Free games (amount 0) embed with no wallet and no popup** — the free path returns before any wallet
  logic.
- **The in-frame `connected` bet gate is relaxed when embedded** (the wallet lives in the popup);
  standalone the gate is identical.
- **The `/wallet-bridge` route is registered in production too** and boots without any player context,
  so the popup can never dead-end on `/error`.
- **No backend change:** on-chain signature validation already runs API-side. The bet POST carries the
  same `wallet` + `paymentSignature` as standalone — and, since operators were removed, no auth header
  in either case.

## Key code touchpoints (`monkify-front`)

| File | Role |
| --- | --- |
| `src/hooks/useEmbed.ts` | `isEmbedded` + module-scope `relaunching` ref |
| `src/utils/embed.ts` | `EMBED_SOURCE`, `EmbedEventType`, `postToParent()` — no-op standalone, secret-free |
| `src/hooks/useEmbeddedWallet.ts` | `inFrameWalletAvailable()` (sync), `openWalletPopup()` (sync, same-origin), `requestSignatureFromPopup()`, `walletPopupBlocked` |
| `src/pages/WalletBridgePage.vue` | The popup page: `bridge-ready` → `sendToken` → `sign-result` + `window.close()` |
| `src/utils/walletBridge.ts` | `WALLET_BRIDGE_SOURCE`, the message union, same-origin validation |
| `src/hooks/useGame.ts` | Lifecycle emits; `bet()` acquires the signature in-frame **or** via popup |
| `src/components/game/GameConsole.vue` | Relaxed `connected` gate when embedded; popup-blocked affordance |
| `src/routes.ts` | `/wallet-bridge` → `WalletBridgePage.vue`, registered in prod |
| `src/App.vue` · `src/main.css` · `nginx.conf` | `ready`/`resize` emits · fill chain · `frame-ancestors *` |
| `public/embed-example.html` · `EMBEDDING.md` | Reference host page + integration README |
