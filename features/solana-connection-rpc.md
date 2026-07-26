# Feature — Solana connection & RPC failover

> Status: **shipped**, but the **frontend transport half is SUPERSEDED**. Authoritative spec:
> `../specs/solana-connection-rpc.md` (merged via `feature/solana-connection-rpc` on 2026-07-24).
>
> ⚠️ **`frontend-helius-gateway` (#99, 2026-07-26) replaced the failover model described below.**
> The `/rpc` proxy is no longer a 429 fallback — it is the browser's **primary and only** Solana
> transport in any deployed environment, `withFailover` and its cooldown are deleted,
> `confirmTransaction`'s WebSocket became a `getSignatureStatuses` poll, the method allow-list is
> **3 methods, not 7**, and balance reads left RPC entirely for `GET /solana/balance`.
> **Read `frontend-helius-gateway.md` for what runs today.** What remains accurate here: the
> single-approval `sendToken` rework, the priority fee, exact integer amount math, and the backend
> proxy's Origin/method/rate gating.

## What it does

*(Historical — see the banner above.)* Kept the game playable under real mainnet traffic. The frontend
talked to the **public** Solana RPC by default, and when that endpoint returned **HTTP 429** it
transparently retried the same call against a thin **backend `POST /rpc` proxy** (forwarding to the
paid, key-bearing RPC the backend already uses), sticking to the proxy for a short cooldown before
drifting back to public. The same change reworked the crypto-bet transfer so a first-time bettor gets
**one** wallet approval instead of up to three, transfers can carry a **priority fee**, and token
amounts use **exact integer math** — those three are still current.

## Core rules

### Frontend transport (`useConnection.ts`) — ⚠️ SUPERSEDED by `frontend-helius-gateway.md`

> Every bullet in this subsection describes the **old** model and no longer runs. Today: one
> connection (the proxy when configured, public otherwise), no failover, no cooldown, and confirmation
> by polling `getSignatureStatuses` rather than a WebSocket.

- The **primary** connection is the public RPC (`VUE_APP_SOLANA_RPC_URL`, default
  `clusterApiUrl(VUE_APP_SOLANA_NETWORK)`). A custom `fetch` on it throws a typed `RpcRateLimitError`
  on `response.status === 429`.
- `withFailover(op)` runs reads/sends on the primary; on a 429 it sets a module-level `proxyUntil =
  now + cooldown` and retries the **same** op on the proxy connection (`VUE_APP_SOLANA_PROXY_URL` →
  backend `/rpc`). While `now < proxyUntil` calls go **straight** to the proxy (no public attempt);
  after it expires they try public first again.
- **Failover triggers on 429 only.** Any other RPC error propagates unchanged (a failed/expired tx
  surfaces exactly as before).
- **`confirmTransaction` never goes through the proxy** — it always runs on the primary/public
  connection so its confirmation WebSocket keeps working. A tx broadcast via the proxy still confirms
  on the public connection (same cluster).
- **Dev/devnet no-op:** if `VUE_APP_SOLANA_PROXY_URL` is unset there is no proxy connection and
  `withFailover` is a pure pass-through — no request is ever sent to a proxy.
- Cooldown is `VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS` (default 60000).

### Crypto transfer (`sendToken`)
- One **single transaction**: idempotent ATA creation for sender **and** receiver
  (`createAssociatedTokenAccountIdempotentInstruction`, no-op if the account exists) bundled with the
  `createTransferInstruction` → at most **one** wallet approval, even for a first-time bettor.
- **Priority fee:** when `VUE_APP_SOLANA_PRIORITY_FEE_MICROLAMPORTS > 0`, prepends
  `ComputeBudgetProgram.setComputeUnitLimit` + `setComputeUnitPrice`; **`0` disables** it (dev
  default). The fee never changes the transfer amount.
- **Exact amount math:** base units come from integer/BigInt conversion (`toBaseUnits`), never
  `value * Math.pow(10, decimals)`.
- **Sign once, send raw with failover:** `signTransaction(tx)` (one approval) then
  `withFailover(c => c.sendRawTransaction(...))` — the raw send needs no approval, so retrying it on
  the proxy is free and safe (identical signed bytes de-duplicate on-chain). Adapters without
  `signTransaction` fall back to a single `sendTransaction(tx, primary)` (no send-failover).
- The send's **`preflightCommitment` is pinned to `'confirmed'`** to match the `'confirmed'` blockhash;
  otherwise preflight simulation runs at `'finalized'` and rejects the just-fetched blockhash with
  *"Blockhash not found"*.
- The embedded **wallet-bridge popup** (`WalletBridgePage.vue`) reuses `useConnection`, so it inherits
  failover + single-approval transfer with **no popup-specific change and no launch token**.
- Standard/USD (off-chain) and free bets never touch RPC → unaffected.

### Backend `/rpc` proxy (`RpcController`)
- `POST /rpc` is a thin JSON-RPC passthrough: **Origin allowlist → parse `method` → method allowlist →
  forward the raw body** to `GeneralSettings.Token.ClusterUrl` via a named `IHttpClientFactory` client
  (`"SolanaRpc"`) → return the provider response **verbatim**.
- **Method allowlist** (exactly, case-sensitive) — ⚠️ **narrowed to 3 by `frontend-helius-gateway`:**
  `getLatestBlockhash`, `sendTransaction`, `getSignatureStatuses`. (Originally seven; `getBalance`,
  `getTokenAccountsByOwner`, `getTokenAccountBalance`, `getParsedAccountInfo` and `getTransaction`
  were removed once their only SPA callers were deleted.) Anything else → **403** before any forward
  (never an open relay).
- **Origin allowlist** is enforced **independent of environment** (it does not rely on the browser
  CORS middleware, which allows any origin in Development): the request `Origin` must be in
  `CorsOriginResolver.ResolveAllowedOrigins` or be a `localhost`/`127.0.0.1` origin; a missing `Origin`
  (same-origin / non-browser) is allowed. Mismatch → **403**.
- **Not launch-token gated** — deliberately kept out of `LaunchTokenValidationMiddleware`'s protected
  prefixes so the wallet-bridge popup (which never carries a launch token) keeps working.
- **Per-IP rate limit:** an `AspNetCoreRateLimit` rule `*:/rpc` (120/min) in `IpRateLimiting.GeneralRules`
  (forwarded headers already expose the real client IP). Over limit → **429** from the limiter.
- Malformed body → **400**; upstream unreachable/timeout → **502**. The api-key is **never** returned
  or logged (endpoints masked host-only via `SolanaRpcClientProvider.MaskRpcUrl`).
- Reuses the **already-configured** `Token.ClusterUrl` — **no new provider, no new secret, no entity,
  no migration, no consumer**. Stays on the stateless `SpaceStroker.Api` host. The backend's own Solnet
  payout/refund RPC usage is unchanged (see `architecture.md` §8).

## Key code touchpoints

- **monkify-backend**
  - `src/SpaceStroker.Api/Controllers/RpcController.cs` — the `POST /rpc` passthrough + guards.
  - `src/SpaceStroker.Api/Rpc/RpcProxyOptions.cs` — method allowlist / timeout / client name.
  - `src/SpaceStroker.Api/Program.cs` — registers the named `"SolanaRpc"` HttpClient.
  - `src/SpaceStroker.Common/spacestrokersettings.json` — the `*:/rpc` rate-limit rule.
  - Tests: `src/SpaceStroker.Tests/UnitTests/Api/RpcMethodAllowlistTests.cs`,
    `src/SpaceStroker.Tests.Integration/RpcProxyTests.cs` (+ `RecordingRpcHandler` in the harness).
- **monkify-front**
  - `src/hooks/useConnection.ts` — primary/proxy connections, `RpcRateLimitError`, `withFailover`,
    `toBaseUnits`, reworked `sendToken`.
  - `.env.example` — `VUE_APP_SOLANA_RPC_URL`, `VUE_APP_SOLANA_PROXY_URL`,
    `VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS`, `VUE_APP_SOLANA_PRIORITY_FEE_MICROLAMPORTS`.

## Out of scope (not changed)

- Proxying the RPC **WebSocket** / switching confirmation to HTTP polling (confirmation stays on the
  public socket).
- The backend's own Solnet payout RPC usage (its primary+fallback retry path is separate — see
  `specs/solana-rpc-api-key.md`).
- `WalletButtons.vue` refactor, trust/UX copy, and Mobile Wallet Adapter (separate cards).
