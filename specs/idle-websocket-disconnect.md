# SpaceStroker — "Idle websocket disconnect" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Add websocket disconnection when idle](https://trello.com/c/fwdi7Fgh) (#96, 2026-07-25).
> Repos affected: `monkify-front` and this doc in `monkify-meta`. **`monkify-backend` is not touched.**
>
> **Level: Minor change** — a cost/QOL change to the existing connection lifecycle in `useGame.ts`.
> No new game capability, no money/bet/payout behaviour change, no backend surface. ⇒ **no dedicated
> `features/<slug>.md` doc** and **no new `SpaceStroker.Tests.Integration` test** (see §2).
>
> **Purpose:** stop paying for websockets nobody is watching. Both SignalR hubs on the game screen
> stay open for as long as the page is loaded and `.withAutomaticReconnect()` revives them whenever
> they drop, so an abandoned tab — backgrounded on a phone, or left open on a desk overnight — pins
> Cloud Run API instances indefinitely for zero player value. After a defined idle period the app now
> **proactively closes** its hubs and **refuses to reconnect** until the player explicitly returns.

---

## 1. Summary

Today `monkify-front` opens two SignalR hubs on the game play page and never closes them on its own:

| Hub | Endpoint | Carries | Created in |
| --- | --- | --- | --- |
| Games | `${VUE_APP_WEBSOCKET_BASE_URL}/Games` | `GameReceived` (rollover), `{gameId}/Terminal`, `{gameId}/Status`, `{gameId}/DisplaySpeed`, `{gameId}/ChatMessage` | `useGame.ts` → `initalizeWebsocketConnections` |
| RecentBets | `${VUE_APP_WEBSOCKET_BASE_URL}/RecentBets` | `{gameId}/Bets` | same |

Both are built by `startConnection()` (`useGame.ts:224-236`) with `.withAutomaticReconnect()` and
WebSockets-only transport. They are only ever torn down when the player switches game / the round
rolls over (`disconnectAllPreviousConnections`, `useGame.ts:710`) or the tab closes.

This spec adds an **idle lifecycle** in front of that:

| | Before | After |
| --- | --- | --- |
| Tab hidden 30 min | Both hubs stay open; auto-reconnect keeps them alive | Both hubs stopped; reconnect suppressed; overlay on return |
| Tab visible, zero interaction 60 min | Both hubs stay open forever | Both hubs stopped; overlay shown in place |
| Tab visible + interacting | Unchanged | **Unchanged** — never disconnected |
| Player returns | N/A | Explicit `RECONNECT` overlay; nothing reconnects until clicked |
| Embedded in a casino iframe | Unchanged | Same rules + a new `idle-disconnected` postMessage |

The change is **entirely client-side**. No endpoint, DTO, header, entity, migration, consumer or
handler changes; the server keeps its existing SignalR configuration.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo (§2):** `feature/idle-websocket-disconnect` lives in
  `.worktrees/idle-websocket-disconnect/<repo>` for `monkify-front` and `monkify-meta`. The shared
  checkouts stay on `main` — never `git checkout -b` / `git switch` in a hub. Push each branch to
  `origin` after committing.
- **Unit tests (§3):** **no `SpaceStroker.Tests` change** — no backend behaviour changes. `monkify-front`
  has **no test runner**, so verification is the **manual QA matrix in §13**. Report that explicitly at
  the end of implementation rather than claiming a green suite.
- **Integration tests (§3):** this is a **Minor change**, so the "every new feature ships an
  integration test" mandate does **not** apply. It is frontend-only in any case — there is no backend
  happy path for `SpaceStroker.Tests.Integration` to drive.
- **Secrets/config (§1):** the two new `VUE_APP_*` values are non-secret numeric timeouts. Commit only
  mock/dev values in `.env.example`; never touch real credentials.
- **Operator messaging (§6):** the overlay copy is generic ("disconnected due to inactivity"). It must
  **never** reveal operator or contract status, and the 401-on-resume path must keep the existing
  vague error / `token-expired` behaviour.
- **Scope (§8):** prefer editing existing files. One new composable and one new component are
  justified; do not refactor `useGame.ts` beyond the touchpoints in §10.2.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Idle is two independent timers, whichever fires first.** `hidden` = `document.hidden` continuously for `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS` (default **30 min**). `inactive` = no qualifying user input for `VUE_APP_IDLE_DISCONNECT_INACTIVE_MS` (default **60 min**), evaluated whether the tab is visible or not. |
| 2 | **A visible, interacting tab is never disconnected.** A player silently watching the typer stream is protected by the visible-tab rule up to the 60-minute inactivity net. |
| 3 | **The 60-minute net exists specifically for the visible-but-abandoned desktop tab** (laptop left open overnight) — the most expensive case, and one a hidden-only rule would miss entirely. |
| 4 | **Proactively `stop()` both hubs**, not merely "suppress reconnect on drop". A healthy silent socket can stay open forever, so the passive reading saves nothing. |
| 5 | **Gate re-initialisation.** An `isIdleDisconnected` flag must block `initalizeWebsocketConnections` so page watchers/loaders cannot silently re-open the hubs. |
| 6 | **Placing a bet is blocked while idle-disconnected** — the round has almost certainly rolled over and a stale `gameId` must never be POSTed to. |
| 7 | **Explicit resume overlay with a `RECONNECT` button; no silent auto-resume.** Restoring several background tabs at once would immediately re-create the load being avoided, and a frozen stale terminal misrepresents the game state. |
| 8 | **Resume re-runs the full join path** (re-fetch the round for the selected type → reopen both hubs), not just a socket reopen: after 30+ minutes the round has rolled over and `gameId` has changed. |
| 9 | **Chat starts empty** after resume (existing no-history-replay behaviour, unchanged) and **own-bet near-miss rows re-derive from the fetched bets** via the existing `reseedOwnBetsFromInstance` path. |
| 10 | **Embedded: same rules**, plus a new `{ source: 'space-stroker', type: 'idle-disconnected' }` postMessage so the parent casino can react or re-launch. **No new event on resume** — the existing lifecycle events resume naturally. |
| 11 | **Pending bets get no exemption.** Payouts settle server-side and appear in balance/history; exempting bettors would let a staked 24h **Space Jackpot** player pin a socket for a full day. |
| 12 | **Thresholds are env vars with defaults**, following the `VUE_APP_SOLANA_RPC_PROXY_COOLDOWN_MS` precedent. |
| 13 | **Reach is the game play page only.** The lobby, stats, history, admin and background pages hold no websocket and are untouched. |
| 14 | **An in-flight mobile wallet hop is exempt from the hidden timer** (resolves the card's open item — see §6.4). |

---

## 4. Data model

**Not applicable.** No entity, column, EF mapping or migration change. This feature persists nothing —
all idle state is in-memory and dies with the page. It deliberately does **not** use `localStorage`:
a reload is a fresh, deliberate visit and must start connected.

---

## 5. Configuration

Two new build-time frontend variables (Vue CLI bakes `VUE_APP_*` in at build time).

### 5.1 `monkify-front/.env.example` (and the local `.env.*` files)

```dotenv
# Idle websocket disconnect — close the game's SignalR hubs when nobody is watching, to stop paying
# for idle Cloud Run instances. Two independent timers; whichever fires first disconnects.
# How long the tab may stay hidden/backgrounded before both hubs are stopped (ms). production: 1800000
VUE_APP_IDLE_DISCONNECT_HIDDEN_MS=1800000
# How long with zero user input (visible or not) before both hubs are stopped (ms). This is the
# safety net for a visible-but-abandoned tab. production: 3600000
VUE_APP_IDLE_DISCONNECT_INACTIVE_MS=3600000
```

### 5.2 Parsing rules

Read once at module load in `useIdleDisconnect.ts`:

```ts
const readMs = (raw: string | undefined, fallback: number): number => {
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : fallback;
};

const HIDDEN_MS   = readMs(process.env.VUE_APP_IDLE_DISCONNECT_HIDDEN_MS, 30 * 60 * 1000);
const INACTIVE_MS = readMs(process.env.VUE_APP_IDLE_DISCONNECT_INACTIVE_MS, 60 * 60 * 1000);
```

Unset, empty, non-numeric, zero or negative ⇒ fall back to the defaults. (A `0` **does not** mean
"disable" — an accidental empty env var must never disconnect players instantly.)

### 5.3 Deploy tool

Add both keys to the `frontBuild` block of `monkify-meta/deploy/secrets.example.json` and to the
`frontBuild` row of `monkify-meta/deploy/README.md` (§ "secrets.\<env\>.json"), so a real deploy can
tune them per environment. Values are non-secret; `secrets.example.json` carries the production
defaults `"1800000"` / `"3600000"`.

---

## 6. Frontend behaviour

### 6.1 New composable — `src/hooks/useIdleDisconnect.ts`

A **module-scope singleton** (same pattern as `useGame`/`useNearMiss`: state lives at module scope,
the exported factory returns bindings). Multiple components may call it; only one set of listeners
and timers exists.

**State**

| Ref | Type | Meaning |
| --- | --- | --- |
| `isIdleDisconnected` | `ref<boolean>` | The hubs were stopped by the idle rule; re-init is blocked and the overlay is shown. |
| `hiddenSince` | `number \| null` (module var) | Timestamp the tab last became hidden; `null` while visible. |
| `lastActivityAt` | `number` (module var) | Timestamp of the last qualifying user input. |

**Public API**

```ts
export const useIdleDisconnect = () => ({
  isIdleDisconnected,          // ComputedRef<boolean> — read by the overlay + bet gate
  startIdleWatch,              // () => void   — arm listeners + interval (GamePage onMounted)
  stopIdleWatch,               // () => void   — remove listeners + interval (GamePage onUnmounted)
  markActivity,                // () => void   — reset the inactivity clock
  setIdleDisconnected,         // (v: boolean) => void — set/clear the flag (useGame owns the transition)
  onIdleReached,               // (cb: () => void) => void — register the disconnect action
});
```

The composable **detects** idle and **owns the flag**; it never touches SignalR itself. `useGame`
registers the actual disconnect via `onIdleReached`. This keeps the timer logic testable in isolation
and keeps hub knowledge in `useGame`, where it already lives.

**Activity events** (all on `window`, `{ passive: true, capture: true }`):
`pointerdown`, `keydown`, `touchstart`, `scroll`, `wheel`, and `mousemove` **throttled to at most one
`markActivity()` per 5 s** (an unthrottled `mousemove` would write on every frame).

`markActivity()` sets `lastActivityAt = Date.now()`. It does **not** clear `isIdleDisconnected` —
only an explicit `RECONNECT` click does that (decision 7).

**Visibility handling** — a `visibilitychange` listener:
- → hidden: `hiddenSince = Date.now()`.
- → visible: evaluate immediately (see below), then `hiddenSince = null`. Note it does **not** call
  `markActivity()`; returning to a tab is not interaction, so the inactivity clock keeps running.

### 6.2 Evaluation — timestamps, never timer callbacks

> **This is the single most important implementation rule in this spec.** Backgrounded and mobile tabs
> throttle `setTimeout`/`setInterval` heavily, and a frozen/bfcached tab does not run them at all. A
> `setTimeout(disconnect, 30 * 60 * 1000)` armed on hide is **not guaranteed to fire**, so it cannot be
> the mechanism.

`evaluateIdle()` computes from stored timestamps:

```ts
const evaluateIdle = () => {
  if (isIdleDisconnected.value) return;
  if (isWalletHopInFlight()) return;                       // §6.4
  const now = Date.now();
  const hiddenTooLong   = hiddenSince !== null && now - hiddenSince >= HIDDEN_MS;
  const inactiveTooLong = now - lastActivityAt >= INACTIVE_MS;
  if (hiddenTooLong || inactiveTooLong) triggerIdleDisconnect();
};
```

`evaluateIdle()` runs on:
1. a **30 s `setInterval`** — the ordinary foreground path (catches the inactive-while-visible case
   promptly; the coarse tick is fine against 30/60-minute thresholds and costs nothing);
2. every **`visibilitychange`** — the load-bearing path. When a throttled or frozen tab is restored,
   this is what notices that 30+ minutes elapsed while the interval was asleep;
3. `window` **`focus`** and **`pageshow`** — belt-and-braces for bfcache restores, which do not always
   fire `visibilitychange`.

### 6.3 The disconnect action (`useGame.ts`)

`useGame` registers a callback that:

1. sets `isIdleDisconnected = true` **first**, so any in-flight re-init is already blocked;
2. `await disconnectAllPreviousConnections()` — reuses the existing teardown at `useGame.ts:710`
   (`LeaveGame` → `off(...)` → `stop()` on both hubs, every network call already try/caught);
3. emits `postToParent('idle-disconnected', { gameId })` (no-op standalone — see §6.6).

> **Reuse note.** `disconnectAllPreviousConnections` is guarded by `if (wsBetsConnection.value && oldId)`
> where `oldId = gameInstance.value?.gameId`. On the game page with a loaded round both hold, so it is
> safe to reuse as-is. If `gameInstance` is somehow unset there is nothing to tear down and the
> function correctly no-ops — but still set the flag, so nothing re-opens.

**Suppressing reconnect (two distinct paths — both are required):**

| Path | Why it happens | Fix |
| --- | --- | --- |
| Explicit `stop()` | Our own idle teardown | SignalR does **not** auto-reconnect after an explicit `stop()`. Nothing extra needed. |
| **Socket died on its own** | The OS/browser froze or suspended a background tab and killed the transport. `.withAutomaticReconnect()` then tries to re-establish when the tab is restored. | Add `onreconnecting` / `onclose` handlers on **both** hubs that check the idle state and `stop()` the connection instead of letting the reconnect land. |

Without the second row the whole feature is a **no-op on mobile** — precisely the platform it matters
most for. Concretely, in `initalizeWebsocketConnections`, for each hub:

```ts
wsGamesConnection.value.onreconnecting(() => {
  // A frozen/suspended tab's socket dies on its own; withAutomaticReconnect would revive it right
  // as the player returns. If we are past the idle threshold, kill it instead of reconnecting.
  evaluateIdle();
  if (isIdleDisconnected.value) wsGamesConnection.value?.stop().catch(() => {});
});
```

`evaluateIdle()` is called first because the reconnect attempt may be the very first thing that runs
on restore, before the 30 s interval or `visibilitychange` has had a chance to evaluate. Mirror the
same block on `wsBetsConnection`. Keep the existing `onreconnected` re-`JoinGame` handlers unchanged —
they remain correct for an ordinary transient network blip while the player is active.

**Gating re-initialisation.** `initalizeWebsocketConnections` is called from `getBets()`
(`useGame.ts:217`, deliberately un-awaited) which is itself called by `updateNewGame`. Guard at the
top of `initalizeWebsocketConnections`:

```ts
const initalizeWebsocketConnections = async () => {
  if (isIdleDisconnected.value) return;   // resume() clears the flag before re-running the join path
  await disconnectAllPreviousConnections();
  ...
};
```

Guarding here rather than in `getBets` means a caller can still refresh the round data (harmless HTTP)
without re-opening sockets, and there is exactly one gate to reason about.

### 6.4 Mobile wallet hop exemption (resolves the card's open item)

`features/mobile-wallet-connect.md`: a mobile `SolanaToken` bet hops out to the wallet app and back.
On **Android** the return is a fresh page load, so timers reset naturally and nothing is at risk. On
**iOS** the hop is in-place — the page merely goes hidden — so a player who spends more than 30
minutes approving in the wallet app would return to the overlay with a pending transaction stranded
behind it.

Resolution: **an in-flight wallet hop suspends the hidden timer.** `useMobileWallet` already persists
`swMobileWalletPendingBet` in `localStorage` and exports `getPendingBet()`. In `evaluateIdle()`:

```ts
// A mobile wallet hop leaves the page hidden while the player approves in the wallet app. Do not
// idle-disconnect underneath it — the return path (handleMobileDeeplinkReturn) still needs the live
// round + /Bets stream to submit and echo the bet. See specs/mobile-wallet-connect.md.
const isWalletHopInFlight = () => !!getPendingBet();
```

The **inactivity** timer is unaffected (approving in the wallet app is genuine activity, and the
return path calls `markActivity()`). When the hop resolves — bet submitted, cancelled, or cleared via
`clearPendingBet()` — the exemption lifts and the next `evaluateIdle()` applies normally. A stale
pending bet cannot pin a connection forever because it is cleared on rollover (`updateNewGame` →
`pendingCryptoBet = null` and the mobile pending bet is consumed by `handleMobileDeeplinkReturn`).

### 6.5 Resume

`useGame` exposes `resumeFromIdle()`:

```ts
const resumeFromIdle = async () => {
  if (!isIdleDisconnected.value) return;
  setIdleDisconnected(false);   // clear FIRST so the init gate opens
  markActivity();               // the click is activity — re-arm both clocks
  try {
    await updateNewGame(true);  // full join path: teardown → getBets → initalizeWebsocketConnections
  } catch {
    setIdleDisconnected(true);  // failed → keep the overlay so the player can retry (§6.7)
  }
};
```

`updateNewGame(true)` is the correct entry point: it already tears down stale connections, re-fetches
the round via `getBets` (which re-seeds `currentPot` and calls `initalizeWebsocketConnections`), resets
`terminalOutput` / `chatMessages` / `nearMiss` / `ownBetChoices`, re-arms the `game-started` and
victory/lose one-shots, and finally calls `reseedOwnBetsFromInstance()` so a returning bettor's
`> N AWAY!` rows come back. Passing `ignoreInitialBets = true` matches the page-load path and avoids
replaying bet cues for bets already placed.

**Chat** legitimately starts empty — `updateNewGame` clears `chatMessages` and the backend replays no
history (`features/game-chat.md`). This is existing rollover behaviour, not a regression.

### 6.6 Embedded

Add `'idle-disconnected'` to `EmbedEventType` in `src/utils/embed.ts`:

```ts
export type EmbedEventType =
  | 'ready'
  | 'token-expired'
  | 'game-started'
  | 'bet-placed'
  | 'bet-settled'
  | 'game-ended'
  | 'idle-disconnected'
  | 'resize';
```

Emitted once per idle disconnect with `{ gameId }` — no wallet, token, signature or secret, matching
the existing envelope contract (`{ source: 'space-stroker', type, payload }`, `targetOrigin: '*'`).
`postToParent` already no-ops standalone, so the call site needs no `isEmbedded` guard.

The overlay renders **inside the frame** (like `EmbedRelaunching.vue`) and must never navigate the top
window. **No resume event** — once reconnected the existing `game-started` / `bet-*` / `game-ended`
events resume naturally, so the parent needs nothing new.

Document the event in `EMBEDDING.md` §3 (events table + the `switch` example) and add a row to the
`public/embed-example.html` live log if it enumerates types.

### 6.7 Overlay component — `src/components/game/GameIdleOverlay.vue`

- Rendered by `GamePage.vue` with `v-if="isIdleDisconnected"`, absolutely positioned over the game
  area (both the desktop three-sibling row **and** the mobile `GAME | CHAT` layout — place it above the
  segmented views so it covers whichever is active).
- Copy — generic, no operator/contract detail:
  - Title: `> DISCONNECTED — INACTIVE`
  - Body: `Live updates were paused to save resources.`
  - Button: `RECONNECT`
- Styled with the existing arcade material classes (`.arcade-panel`, `.btn-arcade` — see
  `features/app-design-refinement.md`); respects `html.light-graphics` (no blur/glow in light graphics
  mode, per `features/light-graphics-mode.md`).
- The button shows a pending state while `resumeFromIdle()` runs and is disabled during it, so a
  double-click cannot fire two join paths.
- **Failure:** if the resume throws, the overlay stays (the flag is restored) and shows
  `Couldn't reconnect — try again.`; it must **not** route to `/error`. A 401 during the re-fetch keeps
  today's behaviour — `mainService`'s interceptor routes standalone to the existing vague error path and,
  when embedded, emits `token-expired` + the relaunching state.

### 6.8 Bet gate

At the top of `bet()` (`useGame.ts:422`), before the existing choice-exclusivity pre-check:

```ts
if (isIdleDisconnected.value) {
  betOutput.value = 'Reconnect to place a bet';
  if (onBetFail) onBetFail();
  return;
}
```

This is defence in depth — the overlay already covers the bet form — but it guarantees no POST is ever
issued against a stale `gameId`, including from a mobile deeplink resume path that bypasses the UI.

### 6.9 Wiring in `GamePage.vue`

`startIdleWatch()` in `onMounted` and `stopIdleWatch()` in `onUnmounted` (the file has no `onUnmounted`
today — add the import). All listeners and the interval must be removed on teardown so nothing leaks
into the lobby, stats or history routes. `GamePage.vue` is the **only** wiring site, which is what
scopes the feature to the play page (decision 13).

---

## 7. Backend behaviour

**None.** No controller, handler, consumer, hub, DTO, entity, migration or configuration change in
`monkify-backend`. Server-side SignalR `KeepAliveInterval` / `ClientTimeoutInterval` tuning is
explicitly **out of scope** (§9) — a client that stops cleanly already releases the server connection,
and changing server timeouts would affect every active player, not just idle ones.

---

## 8. Flow diagrams

### 8.1 Idle → disconnect → resume

```mermaid
sequenceDiagram
    participant P as Player
    participant I as useIdleDisconnect
    participant G as useGame
    participant H as SignalR hubs
    participant C as Parent casino (embedded only)

    Note over I: interval 30s + visibilitychange + focus/pageshow → evaluateIdle()
    P->>I: backgrounds the tab
    I->>I: hiddenSince = now
    Note over P,I: ...30+ minutes pass (timers may be frozen)...
    P->>I: returns to the tab (visibilitychange)
    I->>I: evaluateIdle(): now - hiddenSince >= HIDDEN_MS
    I->>G: onIdleReached()
    G->>G: isIdleDisconnected = true (gates re-init)
    G->>H: LeaveGame → off(...) → stop() ×2
    G->>C: postMessage idle-disconnected
    Note over P: overlay: "> DISCONNECTED — INACTIVE" + RECONNECT
    P->>G: clicks RECONNECT
    G->>G: setIdleDisconnected(false); markActivity()
    G->>G: updateNewGame(true) → getBets → initalizeWebsocketConnections
    G->>H: start() + JoinGame(newGameId) ×2
    Note over P: live on the CURRENT round; chat empty; own-bet rows re-seeded
```

### 8.2 The frozen-tab reconnect race (why `onreconnecting` matters)

```mermaid
sequenceDiagram
    participant OS as Browser/OS
    participant S as SignalR client
    participant I as useIdleDisconnect

    OS->>S: freezes the background tab; transport dies
    Note over S: withAutomaticReconnect arms a retry
    OS->>S: tab restored after 40 min
    S->>S: onreconnecting fires (possibly before any evaluate)
    S->>I: evaluateIdle()
    I-->>S: isIdleDisconnected = true
    S->>S: stop() — the reconnect is abandoned
    Note over S: without this, the socket silently comes back and nothing is saved
```

---

## 9. Out of scope

- **Any `monkify-backend` change**, including a server-side idle timeout and SignalR `KeepAlive` /
  `ClientTimeout` tuning.
- Pausing the ~10 s `TokenBalance.vue` balance poll or any other recurring HTTP — it already pauses on
  `visibilitychange` (`TokenBalance.vue:135`).
- Any change to the lobby, stats, history, admin or background pages — they hold no websocket.
- Cloud Run / Terraform scaling or min-instance configuration changes.
- Silent auto-resume, and cross-tab connection sharing or coordination (each tab runs its own timers).
- Persisting idle state across reloads — a reload starts connected by design.
- Measuring or reporting the actual GCP cost saving.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**No files.** This feature does not touch the backend.

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/hooks/useIdleDisconnect.ts` | **New.** Module-scope singleton: `HIDDEN_MS`/`INACTIVE_MS` env parsing, `hiddenSince`/`lastActivityAt`, activity + `visibilitychange` + `focus` + `pageshow` listeners, 30 s interval, `evaluateIdle()`, `isIdleDisconnected`, `startIdleWatch`/`stopIdleWatch`, `markActivity`, `setIdleDisconnected`, `onIdleReached`, and the wallet-hop exemption. |
| 2 | `src/components/game/GameIdleOverlay.vue` | **New.** Arcade overlay (`> DISCONNECTED — INACTIVE` + `RECONNECT`) with pending/failed states; works desktop + mobile + embedded; light-graphics safe. |
| 3 | `src/hooks/useGame.ts` | Register the idle disconnect via `onIdleReached` (flag → `disconnectAllPreviousConnections()` → `postToParent('idle-disconnected')`); guard `initalizeWebsocketConnections` (line 238) on the flag; add `onreconnecting`/`onclose` idle checks to **both** hubs alongside the existing `onreconnected` (lines 341, 376); add the `bet()` gate (line 422); add and export `resumeFromIdle()` and `isIdleDisconnected`. |
| 4 | `src/utils/embed.ts` | Add `'idle-disconnected'` to `EmbedEventType`. |
| 5 | `src/pages/GamePage.vue` | `startIdleWatch()` in `onMounted`, `stopIdleWatch()` in `onUnmounted` (new import); render `<game-idle-overlay v-if="isIdleDisconnected">` above both the mobile segmented views and the desktop row. |
| 6 | `.env.example` | Add both `VUE_APP_IDLE_DISCONNECT_*` vars with the dev defaults + production comments (§5.1). |
| 7 | `.env.development.local`, `.env.production`, `.env.production.local` | Add the same two keys so local dev and the prod-local build match. Mock/non-secret values only. |
| 8 | `EMBEDDING.md` | Document `idle-disconnected` in the §3 events table + the `switch` example; note that no resume event is emitted. |
| 9 | `public/embed-example.html` | Add `idle-disconnected` to the live event log if the page enumerates types. |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/idle-websocket-disconnect.md` | **New.** This spec. |
| 2 | `INDEX.md` | Add the spec row (status **Ready**); set "Feature currently in flight". |
| 3 | `deploy/secrets.example.json` | Add both keys to `frontBuild` with the production defaults. |
| 4 | `deploy/README.md` | Add both keys to the `frontBuild` row of the secrets table. |
| 5 | `architecture.md` | *(ship-time)* Note the client-side idle lifecycle on the SignalR connection description. |
| 6 | `features/iframe-embedding.md` | *(ship-time)* Add `idle-disconnected` to the documented postMessage event set. |
| 7 | `features/mobile-wallet-connect.md` | *(ship-time)* Note the hidden-timer exemption during an in-flight hop. |

> Rows 5–7 are the **Minor change** KB obligation: existing docs are corrected at ship time, but **no
> new `features/idle-websocket-disconnect.md` doc is created**.

---

## 11. Unit tests

**None.** `monkify-backend` is untouched, so there is no `SpaceStroker.Tests` work, and `monkify-front`
has no test runner configured (confirmed in `specs/bet-choice-casing.md`). Verification is the manual
QA matrix in §13.

> If a frontend test runner is introduced later, `useIdleDisconnect.evaluateIdle()` is deliberately a
> pure function of `(now, hiddenSince, lastActivityAt, HIDDEN_MS, INACTIVE_MS, walletHopInFlight)` and
> is the natural first unit under test.

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist on card #96.

1. Given a player is on `/space-stroker/game/play` with both hubs connected, when the tab is hidden/backgrounded continuously for 30 minutes, then both the `Games` and `RecentBets` hubs are stopped (`LeaveGame` + `off` + `stop`) and no reconnect is attempted.
2. Given the tab stays visible but receives no pointer, key, touch, scroll or wheel input for 60 minutes, when the inactivity threshold is reached, then both hubs are stopped and the resume overlay is shown.
3. Given a player keeps the tab visible and interacts at least once within every 60-minute window, when 30+ minutes pass while they silently watch the typer stream, then the connections are NEVER stopped and the stream continues uninterrupted.
4. Given the tab is hidden for less than 30 minutes, when it becomes visible again, then the hidden timer is reset, no overlay is shown, and both hubs are still live with the stream continuing.
5. Given the app has idle-disconnected, when the player returns to the tab or interacts with the page, then a "disconnected due to inactivity" overlay with a `RECONNECT` button is displayed and NO connection is re-opened until that button is clicked.
6. Given the round rolled over while the player was idle, when they click `RECONNECT`, then the app re-fetches the current round for the selected game type and re-runs the full join path, and the terminal, near-miss dock and POT all reflect the NEW `gameId` rather than stale state.
7. Given the app is idle-disconnected, when the player attempts to place a bet, then the bet is blocked (no POST is issued with the stale `gameId`) and the player is directed to reconnect first.
8. Given a bettor idle-disconnected and then reconnected into a new round in which they hold a bet, when the resume completes, then chat starts empty (no history replay) and their own-bet `> N AWAY!` near-miss rows are restored from the fetched bets via `currentPlayerId`, exactly as after a normal page refresh.
9. Given a mobile/backgrounded tab whose timers were throttled or frozen and whose socket was dropped by the OS, when the tab is restored after more than 30 minutes hidden, then elapsed time is derived from timestamps (not a timer callback), `.withAutomaticReconnect()` is prevented from re-establishing the connection via the `onreconnecting`/`onclose` idle check, and the overlay is shown.
10. Given the game is running embedded in a casino iframe, when an idle disconnect fires, then a `{ source: 'space-stroker', type: 'idle-disconnected' }` postMessage is emitted to the parent, the overlay renders inside the frame, and the app never navigates to `/error`.
11. Given a player holds an unsettled bet (e.g. a 24h Space Jackpot entry), when the idle threshold is reached, then they are disconnected like anyone else, and after reconnecting their balance and Games History reflect the server-side settled payout.
12. Given `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS` and `VUE_APP_IDLE_DISCONNECT_INACTIVE_MS` are set, when the app builds and runs, then those values drive the two thresholds; and when either is unset or invalid, then the code falls back to 30 minutes / 60 minutes respectively.
13. Given the `RECONNECT` click fails because the API or hub is unreachable, when the resume attempt errors, then the overlay stays in place and can be retried, the app does not navigate to `/error`, and when embedded the frame is not broken.
14. Given the launch token has expired while the player was idle, when they click `RECONNECT` and the re-fetch returns 401, then a vague "not found / unavailable" message is shown standalone (never revealing operator contract status) and a `token-expired` + "relaunching" state is emitted when embedded.
15. Given the player is on the lobby, stats, history, admin or background page, when any idle threshold elapses, then nothing changes on those pages (they hold no websocket) and no overlay is shown.
16. Given the player leaves the game page (route change) or the component unmounts, when the idle composable tears down, then all `visibilitychange` and activity listeners and both timers are removed with no leaks carried into the next route.

---

## 13. Manual QA

Run against the local Docker stack (`setup_local_environment.bat`). **Temporarily set the thresholds
low** in `.env.development.local` — e.g. `VUE_APP_IDLE_DISCONNECT_HIDDEN_MS=20000` and
`VUE_APP_IDLE_DISCONNECT_INACTIVE_MS=40000` — so a run takes seconds, not an hour. **Revert to the real
defaults before committing.** Watch the browser Network → WS panel to confirm sockets actually close.

| # | Scenario | Expected | AC |
| --- | --- | --- | --- |
| 1 | On `/game/play`, switch to another tab, wait past the hidden threshold, come back | Both WS entries closed in DevTools; overlay shown; nothing reconnects until `RECONNECT` | 1, 5 |
| 2 | Stay on the page, touch nothing past the inactive threshold | Sockets close and the overlay appears in place | 2 |
| 3 | Stay on the page and move the mouse every ~10 s for >2× the hidden threshold | Never disconnects; stream keeps running | 3 |
| 4 | Hide the tab for well under the hidden threshold, return | No overlay; stream continues; clock reset | 4 |
| 5 | Idle-disconnect, wait for a round rollover, click `RECONNECT` | New `gameId` in the network calls; fresh terminal/POT; chat empty | 6, 8 |
| 6 | Place a bet, idle-disconnect, then try to bet from the mobile layout | Bet blocked with "Reconnect to place a bet"; no POST in Network | 7 |
| 7 | Bet, idle-disconnect through the round's end, reconnect | Balance/history reflect the settled payout | 11 |
| 8 | Chrome DevTools → Application → Background Services / or throttle + lock a real phone past the threshold | On restore the socket does **not** silently reconnect; overlay appears | 9 |
| 9 | Load `/embed-example.html`, launch the game, idle-disconnect | `idle-disconnected` in the parent's event log; overlay inside the frame; no `/error` | 10 |
| 10 | Stop the API container, then click `RECONNECT` | Overlay stays with a retry message; no `/error` | 13 |
| 11 | Unset both env vars, rebuild | Defaults 30/60 min apply (verify by logging the resolved values once) | 12 |
| 12 | Navigate `/game/play` → lobby → stats → history and idle there | No overlay anywhere; no listener leaks (check via `getEventListeners(window)` in DevTools) | 15, 16 |
| 13 | Mobile `SolanaToken` bet: start the wallet hop, wait past the hidden threshold in the wallet app, return | No idle disconnect during the hop; the bet resumes and shows | §6.4 |

---

## 14. Trello / pipeline

- **Source card:** [#96 — Add websocket disconnection when idle](https://trello.com/c/fwdi7Fgh) — list
  **Specs created**; the **Implementation** section on the card holds branch, worktree, spec path and level.
- **Worktree:** `.worktrees/idle-websocket-disconnect/<repo>` (`monkify-front`, `monkify-meta`).
- **Branch:** `feature/idle-websocket-disconnect` in both repos.
- **Next:** `/monkify-implement-feature Add websocket disconnection when idle`, then
  `/monkify-ship-feature Add websocket disconnection when idle`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-25 | Initial spec from refined card #96 (refinement + design agreed in-chat 2026-07-25). Resolves the card's open item on the iOS wallet hop via the §6.4 exemption. |
