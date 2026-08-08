# Feature — Frontend Helius gateway

> Status: **shipped.** Authoritative spec: `../specs/frontend-helius-gateway.md`
> (merged via `feature/frontend-helius-gateway` on 2026-07-26).
> **Supersedes the transport half of `solana-connection-rpc.md`** (#87): the `/rpc` proxy is no longer
> a 429 fallback, it is the browser's primary — and only — Solana transport in a deployed environment.

## What it does

The SPA no longer talks to a public Solana RPC. A production browser cannot use
`api.mainnet-beta.solana.com`, so **every** part of a paid `SolanaToken` bet that used to go there now
goes through our own backend: the player's token balance comes from a new cached REST endpoint, and the
transaction path (blockhash, send, confirmation) rides the already-shipped `/rpc` proxy to the paid
provider endpoint. Three RPC round-trips per player were deleted outright rather than migrated, and the
balance poll was slowed and tied to the idle disconnect, so routing player traffic through a paid
provider does not cost credits in proportion to player count.

## Core rules

### Balance endpoint (`GET /solana/balance`)

- `GET /solana/balance?wallet={pubkey}&mint={mint}` returns `{ wallet, mint, uiAmount, rawAmount, decimals }`.
  `rawAmount` is a **string** — base units can exceed JavaScript's `Number.MAX_SAFE_INTEGER`.
- `GET /solana/balance/live` is the same read with the cache **bypassed**. It is a separate **route**,
  not a query flag, because `AspNetCoreRateLimit` matches on `{verb}:{path}` and cannot budget a
  query-string value — only a distinct path can carry the tighter per-IP rule the bypass needs.
- The `mint` must be the **configured mint** (the endpoint has no tenant
  context). Anything else is rejected **before** any upstream RPC call. This is what stops the endpoint
  becoming a free public balance API for every mint on Solana.
- **Every rejection returns one identical vague message** (`ErrorMessages.TokenNotAvailable`, 400):
  unknown mint, malformed wallet and missing parameter are indistinguishable, so a caller cannot probe
  which mints we know about. The query parameters are deliberately **nullable** — non-nullable ones
  made the framework answer "The wallet field is required", which leaked which field was wrong.
- **Decimals always come from the registered token**, never from the caller or the RPC response: the
  database is the authority for what we settle in.
- A wallet with **no associated token account reads `0`**, not an error, and that answer is **never
  retried** — it is a successful RPC round-trip describing a business fact (the wallet has never held
  this token), so retrying would spend four extra calls plus a fallback on every first-time visitor.
- Both endpoints **write** the Redis cache; only the cached route **reads** it, so a `live` read also
  refreshes what the next background poll sees. A **failed** read is never cached.
- Gating is the same as `/rpc`: a server-side **Origin allow-list** (`OriginAllowList`, shared with
  `RpcController`) plus per-IP rate rules (`*:/solana/*` 120/min, `*:/solana/balance/live` 20/min).
  **Not launch-token gated** — the same-origin wallet-bridge popup carries no token by design.
  Stated plainly: `Origin` is self-reported and forgeable, so this is a practical barrier, not a
  guarantee; the registered-mint restriction and the rate limits are the substantive protection.
- ⚠️ **Correction (2026-07-28, Trello #112 / `specs/rate-limiter-keying.md`): those rate rules were
  never per-IP in production.** `ForwardLimit` was unset, so every request resolved to the load
  balancer and all three rules were **one global bucket for the whole player base** —
  `*:/solana/balance/live` at 20/min *in total*, which a handful of concurrent players exhausted.
  Separately, `RealIpHeader` defaulted to `"X-Real-IP"` and outranked the connection address, so a
  client sending that header got a **fresh bucket per request** and bypassed the rules entirely. Both
  are fixed (`ForwardLimit = 2`, `RealIpHeader` pinned `""`), so the rules are now genuinely
  per-player. **`*:/rpc` is now 240/min** (raised from 120) because `confirmSignature` polls
  `getSignatureStatuses` once a second for up to 45 s over that one path, so a single slow-confirming
  bet costs ~47 of a player's budget — and a throttled `/rpc` mid-confirm leaves tokens that already
  left the wallet unconfirmable. The `/solana/*` numbers are unchanged.

### Frontend transport

- **One connection.** With `VUE_APP_SOLANA_PROXY_URL` set (every deployed environment) the browser
  talks only to the backend `/rpc` proxy. Unset (local/devnet) it uses the public endpoint directly —
  no key, no provider credits. There is **no fallback between them**: on mainnet the public endpoint
  does not work, so failing over to it would only delay the error. The 429-failover machinery
  (`RpcRateLimitError`, `proxyUntil`, `withFailover`, `VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS`) is gone.
- **Confirmation is a poll, not a socket.** `confirmSignature` polls `getSignatureStatuses` over the
  same transport until `confirmed`/`finalized`, an on-chain error, or the deadline. It replaced
  `Connection.confirmTransaction`, whose WebSocket subscription cannot be proxied. Desktop, the
  embedded popup and the mobile deeplink return all confirm identically now.
- A confirmation **timeout is not a failed payment** — it raises a distinct
  `TransactionConfirmationTimeoutError`, and the copy says the transfer is still confirming. By that
  point the player's money has most likely already moved.
- The `/rpc` method allow-list is **exactly three** methods: `getLatestBlockhash`, `sendTransaction`,
  `getSignatureStatuses`. The other four were removed because this feature deleted their only callers.
  If a future `@solana/web3.js` emits another method, add **that exact method** — never a wildcard.

### Deleted RPC round-trips

- **Mint decimals** — read from `ActiveGame.tokenDecimals` (the configured token) and passed
  explicitly into `buildTransferTransaction` / `sendToken`. A `SolanaToken` game with no
  `tokenDecimals` **aborts the bet**; it never falls back to a hard-coded 6/9, because a wrong exponent
  transfers the wrong amount of real money.
- **The SOL balance read** in `startConnection`, and `sendSol` — both were dead code.
- **The token-account lookup** — the balance read derives the associated token account locally (pure
  PDA maths, zero RPC), halving it to one upstream call. Accepted trade-off: a wallet holding the token
  in a **non-ATA** account reads `0`; our own transfer path creates and funds precisely the ATA.

### Balance polling

- The poll interval is `VUE_APP_BALANCE_POLL_INTERVAL_MS` (default **30s**, was a hard-coded 10s) and
  stops entirely once `useIdleDisconnect` has cut the SignalR hubs — previously it kept reading the
  chain forever for an abandoned tab. It resumes with one uncached read on the `RECONNECT` click.
- The poll is only a safety net. The moments that matter use the **`live`** route: the own-bet
  reconcile, the post-win settle read, and refund settlement. The optimistic stake decrement and
  keep-last-known-value-on-failure behaviour are unchanged.

### Seeding (bug fix that shipped with this feature)

- The Consumers host **must not cache the game types it has just constructed** when seeding Main on a
  fresh database. *(Historically those objects carried an `OperatorTokenId` with no hydrated navigation, so
  caching them served a **fully null token block** (`tokenSymbol`, `tokenDecimals`, `tokenMintAddress`,
  `tokenReceiverPublicKey`) on `/games/active-types` for the entire 12h TTL. It now **removes** the key
  and lets the next read repopulate it from the query that `Include`s the token.
- This was a latent **money** bug, not a cosmetic one: the SPA silently defaulted mint and receiver to
  `VUE_APP_*` env vars, so for 12h after a fresh seed a paid bet could transfer to the env defaults
  rather than the configured token and receiver. The token now comes from config, so there is no
  navigation to leave unhydrated.)*

## Key code touchpoints

- **monkify-backend**
  - `src/SpaceStroker.Api/Controllers/SolanaController.cs` — the two balance routes + the Origin guard.
  - `src/SpaceStroker.Common/Cors/OriginAllowList.cs` — the shared server-side Origin gate.
  - `src/SpaceStroker.Api/Rpc/RpcProxyOptions.cs` — the three-method allow-list.
  - `src/SpaceStroker.Infrastructure/Handlers/Solana/GetTokenBalance/` — request + handler (mint gate,
    cache, vague rejections).
  - `src/SpaceStroker.Infrastructure/Services/Solana/SolanaService.cs` — `GetTokenBalance`, its
    not-found-is-zero predicate and exact-decimal `ToUiAmount`.
  - `src/SpaceStroker.Infrastructure/Services/Cache/CacheHelper.cs` — `TokenBalance:{mint}:{wallet}`,
    `Solana:ActiveTokenMints`.
  - `src/SpaceStroker.Consumers/Program.cs` — the seed-time cache fix.
  - `src/SpaceStroker.Common/spacestrokersettings.json` — the two rate rules + cache TTLs.
  - Tests: `SpaceStroker.Tests/UnitTests/{Common/OriginAllowListTests,Handlers/GetTokenBalanceHandlerTests}.cs`,
    `SpaceStroker.Tests.Integration/SolanaBalanceTests.cs`.
- **monkify-front**
  - `src/hooks/useConnection.ts` — the single transport, `confirmSignature`, explicit `decimals`.
  - `src/services/mainService.ts` — `getTokenBalance(wallet, mint, { live })`.
  - `src/hooks/useTokenBalance.ts` — service-backed read, tunable + idle-gated poll. ⚠️ Since #151
    ([money-changing-animations](money-changing-animations.md)) this is a **module singleton and the
    app's only `getTokenBalance` call site**; `src/components/TokenBalance.vue` is **deleted**.
  - `src/hooks/useGame.ts` — resolves and guards `tokenDecimals` across all four bet paths.
  - `src/utils/walletBridge.ts` — `SignRequestPayload.decimals` (same-origin contract only).
- **monkify-meta**
  - `deploy/secrets.example.json` + `deploy/README.md` — the four `frontBuild` variables.

## Out of scope (not changed)

- The backend's **own** payout/refund RPC path in `SpaceStroker.Consumers` (`specs/solana-rpc-api-key.md`).
- Helius enhanced APIs (DAS, parsed-transaction, priority-fee, webhooks) and any WebSocket to the
  provider — plain JSON-RPC is what keeps the provider swappable.
- The **"paid but no bet" gap**: if confirmation fails the transfer has already landed and no bet is
  submitted. This feature makes it much rarer (5s → 45s, on a transport that works) and fixes the
  wording, but does not close it. Needs its own card.
- Standard/USD settlement, free bets, reward maths — none touch RPC.
