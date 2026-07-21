# SpaceStroker — "Live wallet balance refresh on the game page" Feature — Implementation Spec

> **Status: Shipped** (2026-07-17) — merged via `feature/wallet-balance-refresh` into `main`
> (`monkify-front` + `monkify-meta`). Refined via Trello card
> [wallet amount not updating on game page](https://trello.com/c/m4rcHArM) (2026-07-17).
> Repos affected: `monkify-front`, and this doc in `monkify-meta`. **No `monkify-backend` change.**
> Shipped with two post-implementation refinements (see §7.4 / §3 #4): **instant optimistic decrement
> on bet** and the **win-payout read ~5 s after `Ended`**.
>
> **Purpose:** On a **SolanaToken** game, the SPL-token balance shown in the header does not update
> on its own after the player bets or receives cash (a win payout or a refund). Make that balance
> keep itself current — a lightweight periodic poll plus an immediate refresh on the player's own
> bet, on payout, and on refund — so a player never has to reload or reconnect to see their real
> balance. **Frontend-only bug fix.**

---

## 1. Summary

The only wallet balance rendered anywhere on the game page is the on-chain **SPL-token balance** in
the header (`TokenBalance.vue`, mounted inside `GameHeader.vue`), and it renders **only for
SolanaToken games** — Standard (off-chain USD) games show no balance at all (SpaceStroker holds no player
float; there is no balance endpoint, only debit/credit/refund). So this card is **SolanaToken-only**.

Today the balance is read from chain (`getTokenBalance` → `getTokenAccountBalance`) at only three
moments, each **single-shot with no retry**:

| Trigger today | Where | Problem |
| --- | --- | --- |
| On mount / on wallet-connect | `TokenBalance.vue` `onMounted` + `callbackConnect` | Baseline only — fine. |
| After the player's own bet | `updateBalanceCallback` fired in `useGame.submitBet` | Reads immediately after `confirmed`, usually **before** the RPC node reflects the debit → stale, never retried. |
| On game settlement | `watch(gameInstance.status)` → `RewardForWinnersCompleted` / `ErrorWhenProcessingRewards` | Reads the instant the status arrives (pre-propagation → stale); **misses `NotEnoughPlayersToStart`**, the under-subscription refund state; and can miss the transition entirely on a fast rollover. |

There is **no polling** and **no dedicated balance-changed signal**, so a stale read persists until
the next bet or the next round.

**Fix (frontend-only):** turn the balance into a self-refreshing value.

- **Periodic poll** every **~10 s** while a SolanaToken game is on screen with a wallet connected.
  The poll is naturally scoped by the component lifecycle (`TokenBalance` only exists when
  `showWallet` is true) and gated on `connected` + tab visibility; it **pauses when the tab is
  hidden** (immediate read on becoming visible) and **stops on unmount** (leaving the crypto game or
  disconnecting). This poll is the **self-correcting safety net**: any stale immediate read is
  corrected within one interval, and rollover/missed-status races are covered.
- **Immediate one-off refresh** on the three money-moving moments: **own bet placed** (already wired),
  **win payout**, and **refund** — the last two by widening the existing status watch to include the
  refund state.
- **Resilient reads:** keep the **last known balance** on a transient RPC failure (never flash to
  `0`/blank); stop showing the loading spinner after the first successful load.

No backend, DTO, SignalR, or data change. The backend already emits `{gameId}/Status` for every
terminal state; we simply react to more of it and add a timer.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Branch-per-repo / worktree-first:** work lives in `.worktrees/wallet-balance-refresh/<repo>`; the
  shared checkout of each repo stays on `main` (never `git checkout -b` on the hub). Branch
  `feature/wallet-balance-refresh` in `monkify-front` and `monkify-meta`. Push after branch work.
- **Frontend env vars use `VUE_APP_*`** (Vue CLI), not `VITE_*`. (This spec adds **no** env var — the
  interval is a named constant; see §5.)
- **No backend integration test required.** Per `AGENTS.md` rule 8, the mandatory happy-path
  integration test applies to **new features**, not bug fixes — this is a frontend-only bug fix.
  `SpaceStroker.Tests` / `SpaceStroker.Tests.Integration` are untouched.
- **No frontend unit-test harness exists** (`monkify-front` has no vitest/jest). Verification is
  `yarn lint` + `yarn build` green + the manual QA in §13.
- **Front-worktree build gotchas** apply at implement/deploy time: copy the hub `yarn.lock` into the
  front worktree before a Docker build (it is gitignored), and mind prettier `endOfLine: lf` (CRLF
  breaks `yarn build`). See the KB memories on front-worktree builds.
- **Operator messaging / secrets:** not touched by this card (display-only, on-chain read).

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Scope = SolanaToken header balance only, frontend-only.** Fix the on-chain token-balance refresh for crypto games. Standard games are unchanged (no balance shown). Adding an off-chain USD balance is **out of scope** (would need a new operator-wallet `GetBalance` endpoint + adapter + UI). |
| 2 | **Mechanism = periodic polling** of the connected wallet's token balance every **~10 seconds** while on a SolanaToken game page with a wallet connected. |
| 3 | **Poll is scoped and paused** to limit RPC traffic: runs only for a connected SolanaToken game; **pauses when the browser tab is hidden** and does an **immediate read when it becomes visible again**; **stops** when the player leaves the crypto game (games list / a Standard game) or disconnects the wallet. |
| 4 | **Plus an immediate refresh** the moment these events happen (snappy, with the poll as safety net): **own bet placed**, **win payout**, and **refund**. Two refinements from user testing (2026-07-17): (a) **own bet updates instantly** — instead of a stale on-chain read at bet time, the header **optimistically subtracts the known stake** immediately and the poll reconciles once the debit propagates; (b) the **win payout read runs ~5 s after the winner is decided** (`Ended`), not the instant of settlement, so the reward transfer has time to settle + propagate. |
| 5 | **Refund detection** = the round reaching a refunding terminal state. Concretely: widen the existing status watch (`RewardForWinnersCompleted`, `ErrorWhenProcessingRewards`) to also include **`NotEnoughPlayersToStart`** (under-subscription refund). Any state not explicitly caught is still covered by the ~10 s poll. |
| 6 | **Keep the existing initial fetch** on wallet-connect / game entry as the baseline display. |
| 7 | **Resilient reads:** on a transient RPC read failure keep the **last known balance** (no flash to `0`/blank) and retry on the next tick. |
| 8 | **Poll interval is a fixed ~10 s constant** (not env-configurable) — a single named constant in the component. |

---

## 4. Data model

**N/A** — no entity, column, EF mapping, or migration change. This is a display-only on-chain read on
the frontend; the backend is untouched.

---

## 5. Configuration

**No new env var.** The user chose a fixed ~10 s cadence, so the interval is a **named constant**
inside `TokenBalance.vue`:

```ts
// Poll the on-chain token balance this often (ms) while a connected SolanaToken game is on screen.
const BALANCE_POLL_INTERVAL_MS = 10_000;
```

Existing Solana env vars are reused unchanged (`VUE_APP_SOLANA_NETWORK`,
`VUE_APP_TRANSACTION_CONFIRMATION_TIMEOUT`, etc. in `useConnection.ts`). No `.env.example` change.

---

## 6. Backend behaviour

**N/A** — no backend change. The existing `{gameId}/Status` broadcast (`GameService.UpdateGameStatus`)
and the `RewardGameConsumer` / `RefundInvalidBetConsumer` settlement paths already fire for every
terminal state, including `NotEnoughPlayersToStart` for under-subscription refunds. The frontend just
reacts to more of the states it already receives.

---

## 7. Frontend behaviour

All changes are contained in **`monkify-front/src/components/TokenBalance.vue`**. `useConnection.ts`
already exposes `getTokenBalance(mint)`; `useGame.ts` already fires `updateBalanceCallback` on the
player's own bet and already streams `{gameId}/Status` into `gameInstance.status`. No change is
required in `useConnection.ts` or `useGame.ts` for the core fix (see §7.5 for an optional hardening).

### 7.1 Component lifecycle scopes the poll for free

`TokenBalance` is mounted **only when `GameHeader.showWallet` is true** (i.e.
`selectedGame.paymentMethod === SolanaToken`), and its visible body is gated on `connected`. So:

- **Entering a SolanaToken game** mounts the component → start the poll timer.
- **Leaving to the games list or a Standard game** unmounts it → clear the timer (decision #3).
- The per-tick body additionally **skips** when the wallet is not `connected` or the tab is hidden.

### 7.2 Periodic poll (decisions #2, #3, #8)

- On `onMounted`: keep the existing baseline fetch (`getBalance()` if `connection` ready, else defer
  via `callbackConnect`), keep `updateBalanceCallback.value = () => getBalance()`, **and** start
  `setInterval(tick, BALANCE_POLL_INTERVAL_MS)`.
- `tick()`: if `document.hidden` → **skip** (paused). Else if `connected.value` → `getBalance()`.
  (When not connected the component is mounted but the balance is hidden, so a tick is a no-op.)
- On `onUnmounted`: `clearInterval`, remove the `visibilitychange` listener, and reset
  `updateBalanceCallback.value` back to a no-op so a later bet cannot call this unmounted
  component's `getBalance` closure (decision-#4 hardening / the "callback race").

### 7.3 Pause/resume on tab visibility (decision #3)

- Add a `visibilitychange` listener (registered in `onMounted`, removed in `onUnmounted`):
  when the tab **becomes visible** (`document.visibilityState === 'visible'`) and `connected`, do an
  **immediate** `getBalance()`. While hidden, `tick()` already skips, so the poll is effectively
  paused.

### 7.4 Immediate refresh on money-moving events (decisions #4, #5)

- **Own bet — optimistic, instant (refined 2026-07-17).** A chain read at bet time returns the
  **pre-debit** balance (the transfer has not propagated), so it looked like "nothing happened" until
  the next poll. Instead, `useGame.submitBet` passes the **stake** to the callback and the header
  **subtracts it immediately** — accurate, because the debit is exactly the stake — then restarts the
  poll so the on-chain reconcile lands *after* propagation (a stale read would otherwise revert the
  optimistic value). `updateBalanceCallback` is typed `(spent?: number) => void`:

  ```ts
  // useGame.ts
  const updateBalanceCallback = ref<(spent?: number) => void>(() => {});
  // ...in submitBet, after applyBet succeeds:
  updateBalanceCallback.value(body.amount); // body.amount is 0 for a free bet

  // TokenBalance.vue (onMounted)
  updateBalanceCallback.value = (spent?: number) => {
    if (spent && spent > 0) {
      balance.value = Math.max(0, balance.value - spent); // instant, exact
      restartPoll();                                       // reconcile a full interval later
    } else {
      getBalance();                                        // free bet / generic refresh
    }
  };
  ```

- **Win payout — read ~5 s after the winner is decided (refined 2026-07-17).** On
  `GameStatus.Ended` (winner known), schedule a single read `WINNER_SETTLE_DELAY_MS = 5_000` ms later,
  by which time the reward transfer has settled + propagated. The timer is cleared on unmount and
  re-armed if `Ended` fires again.

- **Refund / reward-error:** widen the existing status watch so it also fires `getBalance()` for the
  refund state. A stale read here is corrected by the next poll tick within ~10 s.

  ```ts
  watch(
    () => gameInstance.value?.status,
    (newStatus) => {
      if (newStatus === GameStatus.Ended) {                 // winner decided → payout lands shortly
        if (settleTimer) clearTimeout(settleTimer);
        settleTimer = setTimeout(() => getBalance(), WINNER_SETTLE_DELAY_MS);
      }
      if (
        newStatus === GameStatus.RewardForWinnersCompleted || // paid win (poll/settle read reconciles)
        newStatus === GameStatus.ErrorWhenProcessingRewards || // reward-path refund / manual
        newStatus === GameStatus.NotEnoughPlayersToStart        // under-subscription refund (NEW)
      ) {
        getBalance();
      }
    }
  );
  ```

### 7.5 Token switch between crypto games (edge case)

`props.tokenAddress` changes when `selectedGame` switches to a different SolanaToken game (different
mint). Add `watch(() => props.tokenAddress, () => getBalance())` so the header immediately reflects
the newly-selected token instead of the previous game's balance.

### 7.6 Resilient reads + spinner (decision #7)

- `getBalance()` currently `toast.error`s on failure and leaves `balance` unchanged — keep the
  last-known value (good), but also set `isFetching.value = false` in the `catch` so a first-load
  error doesn't leave the spinner stuck, and **don't** re-enter the loading state on subsequent
  polls. Introduce a `hasLoaded` flag: show `LoadingDots` only until the first successful load; every
  later poll updates the number in place with no spinner flash.
- To avoid toast spam from a flaky RPC on a 10 s cadence, suppress the error toast on **poll** reads
  (log to console instead); keep a user-visible toast only for the explicit user-driven first load if
  desired. (Implementer's discretion; the invariant is: never blank the number, never spam toasts.)

### 7.7 What stays the same

- `GameHeader.showWallet` gating (SolanaToken only), `WalletButtons`, the `v-if="connected"` display
  gate, `formatCompact`, and the coin icon are unchanged.
- Standard games and the games list render no balance and start no poll (nothing mounts).
- Free bets (amount 0) move no tokens → the balance simply doesn't change; polling is harmless.

---

## 8. Flow diagram

```mermaid
sequenceDiagram
  participant P as Player (browser)
  participant TB as TokenBalance.vue
  participant RPC as Solana RPC
  participant BE as SpaceStroker backend (SignalR)

  Note over TB: mounted only for a connected SolanaToken game
  TB->>RPC: getTokenBalance(mint)  (initial)
  loop every ~10s while visible + connected
    TB->>RPC: getTokenBalance(mint)  (poll — self-correcting)
  end
  P->>BE: place bet (on-chain transfer + applyBet)
  BE-->>TB: (submitBet) updateBalanceCallback()
  TB->>RPC: getTokenBalance(mint)  (immediate; poll re-reads if stale)
  BE-->>TB: {gameId}/Status = RewardForWinnersCompleted | NotEnoughPlayersToStart | ErrorWhenProcessingRewards
  TB->>RPC: getTokenBalance(mint)  (immediate; poll re-reads if stale)
  Note over TB: tab hidden → ticks skipped; on visible → immediate read
  Note over TB: unmount (leave crypto game / disconnect) → clearInterval
```

---

## 9. Out of scope

- A **Standard / off-chain USD** balance display (needs a new operator-wallet `GetBalance` endpoint +
  adapter + UI — a separate, larger, backend+frontend card).
- Any **backend** change or a new **SignalR** settlement/balance event.
- **Native SOL** balance and any **USD/price conversion** of the token balance.
- Optimistic local balance math (−stake/+winnings) — the chosen mechanism is polling + immediate
  re-read, not client-side arithmetic.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.**

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/components/TokenBalance.vue` | Add `BALANCE_POLL_INTERVAL_MS = 10_000` + `WINNER_SETTLE_DELAY_MS = 5_000` constants. In `onMounted` start the poll (`startPoll`) and register a `visibilitychange` listener; `pollTick` skips when `document.hidden` and reads only when `connected`. Add `onUnmounted` to clear the poll + settle timers, remove the listener, and reset `updateBalanceCallback` to a no-op. On visible→immediate read. `updateBalanceCallback` becomes `(spent?) =>` **optimistic subtract of the stake** + `restartPoll()` (else `getBalance()`). Status watch: on `Ended` schedule a `getBalance()` **5 s later** (`settleTimer`); also `getBalance()` on `RewardForWinnersCompleted` / `ErrorWhenProcessingRewards` / `NotEnoughPlayersToStart`. Add `watch(() => props.tokenAddress, getBalance)` for token switches. Add a `hasLoaded` flag so the spinner shows only until first load; set `isFetching=false` in the `catch` and keep the last-known balance; suppress error toasts on poll reads. |
| 2 | `src/hooks/useGame.ts` | Type `updateBalanceCallback` as `ref<(spent?: number) => void>`; in `submitBet` call `updateBalanceCallback.value(body.amount)` after a successful `applyBet` (passes the stake for the optimistic decrement; `0` for a free bet). |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/wallet-balance-refresh.md` | This spec. |
| 2 | `INDEX.md` | Add the spec row to the KB map (status **Ready**); update **Feature currently in flight**. |

> `features/` is updated later by `/monkify-ship-feature` (KB sync on the feature branch before merge),
> not in this spec step.

---

## 11. Unit tests

**N/A (no frontend test harness).** `monkify-front` has no vitest/jest setup and no `test` script;
existing frontend-only features verify via `yarn lint` + `yarn build` + manual QA. Backend
`SpaceStroker.Tests` is untouched. See §13 for manual verification mapped to the acceptance criteria.

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given a connected wallet on a SolanaToken game, when the page is open, then the header shows the
   current token balance and re-reads it automatically about every 10 seconds without any user action.
2. Given a SolanaToken game, when the player places a bet, then the header balance updates to reflect
   the debited stake without a manual refresh (immediately, and corrected within one poll interval if
   the on-chain read was stale).
3. Given a SolanaToken round the player bet on and won, when the round settles
   (`RewardForWinnersCompleted`), then the header balance updates to reflect the payout without a
   manual refresh.
4. Given a SolanaToken bet that is refunded on-chain (lottery under-subscription, abrupt game end, or
   invalid bet), when the refund settles, then the header balance updates to reflect the returned
   tokens without a manual refresh.
5. Given the browser tab is hidden, when it is backgrounded, then balance polling pauses; and when the
   tab becomes visible again, then an immediate balance read runs.
6. Given the player navigates off the SolanaToken game (to the games list or a Standard game) or
   disconnects the wallet, when that happens, then balance polling stops and no further balance RPC
   calls are made.
7. Given a transient RPC read failure, when a poll read fails, then the last known balance stays
   displayed (no flash to `0` or blank) and the next poll tick retries.
8. Given a Standard (off-chain USD) game or the games list, when the player is on that page, then no
   wallet balance is shown and no balance polling occurs (behavior unchanged).

---

## 13. Manual QA

Local stack (`setup_local_environment.bat`), dev operator, a **SolanaToken** game type (e.g. **6X
Crypto Clash**), a devnet wallet holding the game's token (see the KB memory on local paid SolanaToken
bets):

1. **Poll (AC1):** open the crypto game, connect the wallet → balance shows. Move tokens to/from the
   wallet in another tab (or a faucet/transfer) and confirm the header number updates within ~10 s
   with no reload. (DevTools → Network shows a periodic `getTokenAccountBalance` roughly every 10 s.)
2. **Bet (AC2):** place a paid bet → the header balance drops by the stake without reloading (may
   settle to the exact value within one poll tick).
3. **Win (AC3):** win a round → after settlement the balance rises by the payout without reloading.
4. **Refund (AC4):** trigger an under-subscription round (a preset/min-players game that doesn't fill)
   → after `NotEnoughPlayersToStart` the refunded stake is reflected in the balance.
5. **Visibility (AC5):** background the tab (Network quiesces → no balance calls); refocus → an
   immediate balance call fires.
6. **Teardown (AC6):** navigate to the games list / a Standard game, or disconnect → no further
   balance calls in Network.
7. **Resilience (AC7):** throttle/offline the RPC briefly → the number holds its last value (no blank
   / `0`), and recovers on the next successful tick.
8. **Standard (AC8):** open a Standard game → no balance element, no balance polling.

Then `yarn lint` and `yarn build` green in the front worktree (copy the hub `yarn.lock` in first).

---

## 14. Trello / pipeline

- **Source card:** [wallet amount not updating on game page](https://trello.com/c/m4rcHArM) — moved to
  **Specs created**; the **Implementation** section on the card lists the branch + spec + worktree.
- **After merge:** `/monkify-ship-feature wallet amount not updating on game page` (reads branch +
  spec from the card's Implementation section, syncs the KB, merges to `main`).
- **Next step now:** `/monkify-implement-feature wallet amount not updating on game page`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-17 | Initial spec from the refined card #67. Frontend-only: periodic ~10 s poll + immediate refresh on own bet / win / refund (incl. `NotEnoughPlayersToStart`), visibility pause, resilient reads, token-switch refetch. All contained in `TokenBalance.vue`. |
| 2026-07-17 | Post-implementation refinements from user testing: (a) **own bet updates instantly** via an optimistic stake decrement (`updateBalanceCallback(spent)`) + poll restart instead of a stale on-chain read; (b) the **win payout read runs ~5 s after `Ended`** (`WINNER_SETTLE_DELAY_MS`) so the reward transfer has time to propagate. Adds a one-line `useGame.ts` change (typed callback + pass `body.amount`). |
