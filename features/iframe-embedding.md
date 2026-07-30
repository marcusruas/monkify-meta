# Feature — Iframe Embedding

> Status: **shipped.** Authoritative spec: `../specs/iframe-embedding.md`
> (merged via `feature/iframe-crypto-games` — stacked with [[iframe-crypto-games]] — on 2026-07-14).
>
> ⚠️ **Reduced by `features/remove-operators.md` (2026-07-30).** The embed **mechanics are kept** —
> `useEmbed` keys off `window.self !== window.top`, never off any operator row — but everything that
> depended on a launch token is gone: there is no token to pass in the URL fragment, no
> `token-expired` / "relaunching" branch, and no inactive-operator case. An embedding site now just
> points an iframe at the app. The `Standard`-only scope note below is also moot: every game is
> on-chain, and `features/iframe-crypto-games.md` already made paid crypto bets work in the frame.

## What it does

A site can embed the SpaceStroker game in an `<iframe>` on its own page —
the white-label delivery path for the B2B integration. The SPA detects it is framed, speaks a stable
`postMessage` API to the parent casino (lifecycle + resize), and — the core fix — **never dead-ends on
`/error` inside a frame**: on an auth failure it signals `token-expired` and shows a light "relaunching"
state so the casino can re-launch with a fresh token. **Frontend-only**: no backend, DB, SignalR, or
game-flow change. *(Originally scoped to **Standard (USD / operator-wallet)** games; on-chain crypto embedding
is the sibling feature [[iframe-crypto-games]].

## Core rules

- **Embedded detection is runtime, not a flag:** `isEmbedded = window.self !== window.top` (or `?embed=1`
  for local testing), evaluated once at load. Loaded standalone, behavior is exactly as before and **no
  `postMessage` is emitted**.
- **Token handoff is unchanged:** the casino sets the iframe `src` to
  `…/space-stroker/game/games#launch_token=<jwt>`; the existing `useLaunch` fragment capture stores the token
  in `sessionStorage`, strips it from the URL, and decodes the operator from the token's `iss` claim. *(Historical — there is no launch token.)* The
  signed **RS256 launch token is the security boundary**, not the frame header.
- ~~**No `/error` when embedded:**~~ *(historical — there is no token to expire)* on a 401 / invalid / expired token / inactive operator, the app emits a
  `token-expired` postMessage and shows an in-frame "Relaunching…" state instead of routing to `/error`.
  Standalone still routes to `/error`.
- **Clean re-launch:** the casino re-sets the iframe `src` with a fresh `#launch_token=` → full reload
  back to game-select, with prior-game SignalR torn down (`disconnectAllPreviousConnections`), no
  stale state.
- **Parent `postMessage` API — stable envelope `{ source: 'space-stroker', type, payload }`, `targetOrigin '*'`,
  no secrets/tokens in any payload:** events are `ready`, `token-expired`, `game-started`,
  `bet-placed`, `bet-settled` (carries `won`), `game-ended`, `idle-disconnected`, and a one-time
  `resize` size **hint** (not continuous auto-height).
- **`idle-disconnected` (`{ gameId }`)** fires when the idle rule closed the frame's realtime
  connections to save resources — the same rules apply embedded, since a parent casino tab is exactly
  where tabs get left open. It is **not an error** and needs no action: the frame shows its own
  in-frame reconnect prompt and stays put. There is deliberately **no matching "resumed" event** —
  once the player reconnects, the normal `game-started` / `bet-*` / `game-ended` events simply resume.
  See [[idle-websocket-disconnect]] (`../specs/idle-websocket-disconnect.md`).
- **`bet-placed` is the embedded player's own bet** (emitted after a successful `applyBet`), a
  balance-refresh hint — not every bet on the `/Bets` stream. **`bet-settled` is bettors-only** (same gate
  as the victory/lose one-shot); spectators get `game-ended` but no `bet-settled`.
- **Lifecycle amounts are Standard/USD here** (the settlement currency) so the casino can refresh the
  player's displayed balance. (For SolanaToken games the same events carry token-native amounts — see
  [[iframe-crypto-games]].)
- **Vague messaging holds** (constraints §6): *(historical)* an inactive/expired operator uses the **same**
  `token-expired`/relaunch path as an expired token — no reason string, no contract status ever revealed.
- **Fills the frame:** `html, body, #app { height: 100% }` so the game fills 100%×100% of the casino's box
  with no letterbox (deep mobile responsiveness stays card #5).
- **No frame-busting:** in embedded mode no in-app action performs a top-level navigation; the wallet
  cluster is already hidden for Standard games.
- **Frame header:** the committed `nginx.conf` sends `Content-Security-Policy: frame-ancestors *` and no
  `X-Frame-Options` (permissive posture; token is the boundary). The production host must match — a
  deploy-time dependency, not fully committable in-repo.

## Key code touchpoints

- `monkify-front/src/hooks/useEmbed.ts` — **new.** `isEmbedded` + module-scope `relaunching` ref +
  `setRelaunching`.
- `monkify-front/src/utils/embed.ts` — **new.** `EMBED_SOURCE`, `EmbedEventType`, `postToParent()`
  (envelope, `targetOrigin '*'`, no-op standalone, secret-free).
- `monkify-front/src/components/EmbedRelaunching.vue` — **new.** Light full-fill "Relaunching…" in-frame
  state (generic copy).
- `monkify-front/src/services/mainService.ts` — 401 interceptor branches: embedded → `token-expired` +
  relaunch (no nav); standalone → `/error`.
- ~~`monkify-front/src/hooks/useOperator.ts`~~ *(deleted)* — the three `/error` routes went through a `failAuth()` helper
  that relaunches instead when embedded.
- `monkify-front/src/hooks/useGame.ts` — emits `game-started` (once/game), `bet-placed`,
  `bet-settled` (bettors-only), `game-ended` via `postToParent`.
- `monkify-front/src/App.vue` — emits `ready` + `resize` hint; renders `<EmbedRelaunching v-if>`.
- `monkify-front/src/main.css` — `html, body, #app { height: 100% }` fill chain.
- `monkify-front/nginx.conf` — `add_header Content-Security-Policy "frame-ancestors *" always;`.
- `monkify-front/public/embed-example.html` — reference host page: iframe host + launch-token input *(now unused)* +
  live postMessage log + re-launch demo (reachable at `/embed-example.html`).
- `monkify-front/EMBEDDING.md` — integration README (snippet, fragment, event reference,
  re-launch flow, frame-header requirement).
