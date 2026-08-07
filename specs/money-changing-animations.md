# Space Stroker — "Money change animations" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Add money changing animations (#151)](https://trello.com/c/AaPjqrw5) (2026-08-07).
> Repos affected: `monkify-front`, and this doc in `monkify-meta`. **`monkify-backend` is untouched.**
> Branch/worktree: `feature/money-changing-animations` → `.worktrees/money-changing-animations/<repo>`.
>
> **Purpose:** money moves silently. A player's on-chain wallet balance and their Orbit balance both
> change several times a round — a stake leaves, a win lands, a refund returns, a deposit moves money
> from one to the other — and today every one of those is a number quietly replacing itself in the
> header. This adds one shared animated money readout (a value roll plus a floating ± chip), and
> restructures the header's two icon-only doors into balance-carrying pills so both numbers have a
> home to animate in.

---

## 1. Summary

Two halves, one card.

**The motion.** A shared component rolls a money figure old → new (~600 ms, `tabular-nums`) while a
small chip rises above it and fades: green `+` for a credit, **dim ink** `−` for a debit. It fires
**only for attributed changes** — a change whose cause the app knows. Unattributed drift discovered
by a background poll updates the number silently.

**The surface.** The header's two 40 px doors (`OrbitLauncher`, `WalletButtons iconOnly`) become
balance-carrying pills — `[◎ 5.00]` violet Orbit, `[▤ 12.34]` amber wallet. Colour and icon supply
the context; there is **no text label**. The `SPACE STROKER` wordmark is cut below `lg` to pay for
the width. Each pill stays a door: tapping still opens the Orbit panel / wallet menu.

| Surface | Number shown | Animates | Today |
| --- | --- | --- | --- |
| Header wallet pill | on-chain token balance | ✅ | bare number beside a 40 px glyph door |
| Header Orbit pill | Orbit balance | ✅ | **not rendered at all** |
| `OrbitStatusBanner` → `ORBIT HOLDS` | Orbit balance | ✅ | static |
| `OrbitStatStrip` → `ORBIT HOLDS` | Orbit balance | ✅ | static |
| `OrbitMoneyTab` cash-out amount / MAX | typed input | ❌ **excluded** | static |

### 1.1 ⚠️ This card knowingly reverses a shipped decision

`OrbitLauncher.vue:7-17` and `GameHeader.vue:258-274` record an owner's call from **2026-08-04**
that the header Orbit control is *"a DOOR, not a readout"* — taken after three separate attempts to
surface Orbit's state in the chrome each failed. The owner **re-took that decision during refinement
on 2026-08-07**, narrowly: *a door that has a number to show becomes a pill that shows it.*

The reversal is **scoped to the number**. The control is still one door, still opens the panel, still
carries no `SIGN IN` and no text label. Every code comment asserting "no balance, no copy of any
kind" must be **rewritten, not deleted** — the next agent needs the history and the new call in one
place (§7.6, §10.3).

### 1.2 What does NOT change

- The disconnected `Connect Wallet` button, its label, and its `md:w-[200px]` reflow guard.
- The Orbit production gate `VUE_APP_ORBIT_ENABLED` (fail-closed) and the mount gate
  `orbitEnabled && !isEmbedded && effectiveWallet`.
- Orbit's invisibility to other players — nothing here is observable to a non-owner.
- `orbit-ui-overhaul.md`'s colour discipline: **red stays warning-only** (see decision D4).
- The `/orbit` page's documented "it does not poll" stance (`OrbitPage.vue:52-55`).
- The ☰ menu, the audio cluster, the logo art, and every non-money header control.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo:** `feature/money-changing-animations`, hub stays on `main`.
- **Unit tests:** ⛔ **N/A.** No backend change, and `monkify-front` has **no test harness at all**
  (no `*.spec.*` / `*.test.*`, no test script in `package.json` — only `serve`/`build`/`lint`).
  Verification is §12 + §13 + `yarn lint`. See §11.
- **Integration test:** ⛔ **N/A.** `constraints.md` §8 scopes the mandate to `monkify-backend`; this
  card has no backend change. Do **not** invent one.
- **EF mapping / migrations:** N/A — no backend change.
- **Secrets/config:** no new env var (§5). Mock-only values in any committed config.
- **Vague messaging:** N/A — this card ships no error copy.
- **Frontend env prefix is `VUE_APP_*`**, never `VITE_*`.
- ⚠️ **`yarn lint` requires uniform CRLF.** Never fix files with `sed -i` — it rewrites the whole
  file to LF and prettier then fails with `Insert ␍` on every line. Edit in place; if endings get
  mangled, repair with `unix2dos` and verify with `od -c`.

---

## 3. Resolved product decisions

Answer mode during refinement: **Short**. Sources: card **Decisions** block + refinement comment
(2026-08-07). Decisions **D14–D15** are resolved by this spec (the card explicitly delegated them).

| # | Decision |
| --- | --- |
| D1 | **Doors become labeled pills.** Each header door widens to carry its own number. **Context comes from the button's own colour and icon, never a text label.** Rejected: a stacked `WALLET`/`ORBIT` readout (that is the pre-#141 layout, deliberately deleted) and one merged money pill (hides the wallet behind a menu hop). Each pill stays a door. |
| D2 | **The wordmark is cut below `lg`** — logo only, extending the existing hide-below-360px rule (`GameHeader.vue:184-185`) to the whole mobile bar. This is what pays for the second number: the bar is 54 px below 1024 px (`GameHeader.vue:176`) while `--ctl-h-md` *grows* to 44 px there (`colors.css:164-165`). |
| D3 | **The animation is a value roll plus a floating ± chip.** Rejected: chip-only (the balance changing goes unmarked) and roll-only (never states the delta, the one thing the card asked for). A slot-machine digit tumble was rejected as disproportionate for phones that are all in light-graphics. |
| D4 | **Credit is green (`--color-live`), debit is dim ink (`--color-ink-mute`) — never red.** ⚠️ Resolves a real conflict with `features/orbit-ui-overhaul.md`, whose rule is *red = warning only* (`no floor`, `paused`, failed deposit) and *every value is plain ink*. A red debit chip would fire on a **healthy** running Orbit every round and burn that signal. The `−` sign and the downward travel carry the direction. Rejected: amending the rule; splitting the idiom by surface. |
| D5 | **Only attributed changes animate.** A chip fires only when the app knows the cause (stake, win, refund, deposit, cash-out). A difference discovered by the 30 s poll with **no known cause** updates the number **silently**. Rationale: two changes inside one poll window collapse into one net figure that tells the wrong story — a `−1.00` stake and a `+5.00` win would render as a single `+4.00`, making the bet look like it never happened. |
| D6 | **Orbit's auto-bet debit is decremented optimistically, frontend-only.** Orbit's stake leaving the balance pushes no event and is not polled — only wins push (`useGameHub.ts` → `useOrbitNotifications.ts:43-72`). When the live bet stream shows this round's own bet was Orbit-funded (`myBetThisRoundIsOrbit`, `useGame.ts:202`), decrement the Orbit balance by the game's stake and animate it. Rejected: adding a bet-placed kind to `OrbitNotification` (the honest fix, but it turns a UI card into a cross-repo one). **Accepted limitation:** it only fires while the player is on that game's page. |
| D7 | **Animated surfaces:** the two header pills, plus `OrbitStatusBanner.vue:79` and `OrbitStatStrip.vue:72`. **One shared component serves all four.** `OrbitMoneyTab`'s cash-out amount / MAX are **excluded** — they change because the player is typing, and animating a field being edited reads as lag. |
| D8 | **A deposit and a cash-out animate both pills at once, in opposite directions.** A deposit *is* wallet → Orbit, so `▤ −10.00` (dim) and `◎ +10.00` (green) fire simultaneously; a cash-out is the mirror. Designed for, not worked around (§7.4.4). |
| D9 | **Reduced motion keeps the information, drops the movement.** `prefers-reduced-motion` → no tween and no float; the chip fades in and out **in place** and the number snaps. |
| D10 | **Phones get the full animation.** Every viewport below 1024 px is permanently in light-graphics (`usePerfMode.ts:39`), whose policy is `animation: none` for **infinite** animations (`arcade.css:1778-1787`). This is a short **one-shot**, so it is a documented exception and must **not** be added to that strip list. Rejected: treating light-graphics as reduced-motion, which would mean most players never see the designed version. |
| D11 | **The wallet balance is lifted into a shared composable.** Not tidying: **the delta chip needs one place that knows old vs new**, which the current three independent fetches cannot provide. |
| D12 | **Mockups gate the build.** 3 visual options for the header pill cluster and 3 for the change animation; the user picks one of each **before any production code is written**. |
| D13 | **The signed-out Orbit pill** — two candidates carried into the header mockups: **(a)** the pill collapses back to today's bare `--ctl-h-md` violet door until a balance is readable, or **(b)** the pill keeps its width and reads `ORBIT` instead of a number. A `SIGN IN` label is **rejected** (#141 deliberately deleted that chrome). Balance `0.00` **with** a session is shown as a normal pill — `0` is a true number there. |
| D14 | **Formatting — resolved here** (the card delegated it). The shared component takes a **`format` prop**; the roll/chip/colour/reduced-motion logic is shared, the formatter is injected. Header pills pass the existing **`formatCompact`** (the header must still fit two numbers in a 54 px bar at 360 px); Orbit's panel and page pass their existing **`money()`** (2 dp `toLocaleString`), unchanged. `formatCompact`'s variable-length output (`999` ↔ `1.2K`) is handled by **reserving width for the duration of the roll** (§7.4.5), not by changing what either surface shows today. |
| D15 | **Optimism must not feed Orbit's auto-park watcher — resolved here.** See §7.3.2. This is a hidden blocker found during code exploration; it is not optional. |

---

## 4. Data model

⛔ **N/A.** No entity, no column, no EF mapping, no migration. Nothing here is persisted — not the
chip, not the queue, not the optimistic delta. A reload shows the current balances and animates
nothing (§7.4.6, baseline rule).

---

## 5. Configuration

**No new environment variable.**

| Var | Change |
| --- | --- |
| `VUE_APP_BALANCE_POLL_INTERVAL_MS` | **Moves**, unchanged in meaning, from `TokenBalance.vue:30` into `useTokenBalance.ts`. Same defensive parse (`0`/empty/`NaN` → 30 000 default, never "disabled"). |
| `VUE_APP_ORBIT_ENABLED` | Untouched. Still fail-closed; the Orbit pill is gated at the mount point exactly as the launcher is today (§7.6.2). |

Animation timings are **module constants**, not env vars — they are design, not deployment:
`ROLL_MS = 600`, `CHIP_MS = 900`, `QUEUE_COALESCE_AT = 2`.

---

## 6. Backend behaviour

⛔ **None. `monkify-backend` is not touched by this card.**

Two places this deliberately did **not** go, both re-affirmed during refinement:

- **No bet-placed kind on `OrbitNotification`.** That is the honest fix for D6 and it is explicitly
  out of scope — it would make a UI card cross-repo. The frontend optimism is the agreed trade.
- **No new polling of `GET /orbit`.** `OrbitPage.vue:52-55` documents that the page does not poll;
  that stands.

The only backend endpoint this feature reads is the one already in use:
`GET /solana/balance` / `/solana/balance/live` (`mainService.getTokenBalance`,
`mainService.ts:228-234`). **Its call volume goes down, not up** (§7.2.4).

---

## 7. Frontend behaviour (`monkify-front`)

### 7.0 The attribution model — read this first

Everything in this feature hangs off one idea: a balance change is either **attributed** (the app
knows what caused it) or it is not.

```
attributed   → the number ROLLS + a chip fires
unattributed → the number SNAPS, silently, no chip
```

There are exactly two ways a change becomes attributed:

1. **The amount is known up front** — a stake, a deposit, a cash-out. The composable applies it
   optimistically and publishes the delta in the same synchronous block. The next authoritative read
   reconciles **silently**.
2. **The cause is known but the amount is not** — a win, a refund. The caller **arms an attribution
   on the read**; whatever difference that read discovers is published under that cause. If the read
   discovers no difference, **nothing is published**.

Everything else — the 30 s poll tick, the `visibilitychange` catch-up, the idle-disconnect return,
the mint switch, the first load, a wallet switch — is **unattributed by construction**. Money
arriving from outside the app lands silently. That is the point (D5).

#### 7.0.1 The shared change signal

New file `src/types/money.ts`:

```ts
export type MoneyChangeReason = 'stake' | 'win' | 'refund' | 'deposit' | 'cashout';

export interface MoneyChange {
  /** Monotonic. The animated component compares this against the last one it played. */
  seq: number;
  /** Signed. Negative = debit (dim ink), positive = credit (green). Never zero. */
  delta: number;
  reason: MoneyChangeReason;
}
```

**Contract (both producers must honour it):** the new `value` and the bumped `change` are assigned in
**one synchronous block, value first**. The consumer then does not depend on watcher ordering — it
watches `value` and, inside the callback, asks whether `change.seq` moved (§7.4.1).

### 7.1 `useTokenBalance` — the wallet balance, lifted out of the component (D11)

New file `src/hooks/useTokenBalance.ts`. **Module-level singleton state**, in the style of
`useOrbit.ts` — one balance, one poll, one place that knows old vs new.

It absorbs, verbatim in behaviour, everything `TokenBalance.vue` owns today:

| From `TokenBalance.vue` | Into the composable |
| --- | --- |
| `:18` `balance`, `:38` `hasLoaded`, `:19` `isFetching` | module refs |
| `:26-30` `readMs` + `BALANCE_POLL_INTERVAL_MS` | unchanged |
| `:89-109` `getBalance({ silent, live })` | `refresh(opts)` |
| `:114-132` `pollTick` / `startPoll` / `restartPoll` (skips `document.hidden`, stops on idle-disconnect) | unchanged, refcounted (§7.2.3) |
| `:121-124` `onVisibility` | unchanged, **unattributed** |
| `:186-189` mint-switch watcher | unchanged, **unattributed**, new mint is a **baseline** |
| `:193-198` idle-disconnect return | unchanged, **unattributed** |
| `:202-204` `effectivePublicKey` watcher | unchanged, wallet switch **resets the baseline** |
| `:142-149` optimistic `updateBalanceCallback` | becomes `applyDebit()` (§7.2.2) |
| `:163-183` `gameInstance.status` watcher | ⚠️ **moves to `useGame`**, not here — see §7.2.5 |
| `:58-78` `formatCompact` | ⚠️ **moves to `src/utils/money.ts`** — see §7.7 |

#### 7.2.1 Public surface

```ts
useTokenBalance(): {
  balance: Ref<number>;
  change: Ref<MoneyChange | null>;
  isFetching: Ref<boolean>;
  hasLoaded: Ref<boolean>;

  /** Refcounted poll lifecycle — call from onMounted/onUnmounted of any consumer. */
  activate(): void;
  deactivate(): void;

  /** The header owns the mint. See §7.2.6. */
  setMint(mint: string): void;
  currentMint(): string | null;

  /** Optimistic, amount known up front. `amount <= 0` is a no-op (free bets). */
  applyDebit(amount: number, reason: MoneyChangeReason): void;
  applyCredit(amount: number, reason: MoneyChangeReason): void;

  /** `attribute` arms the read: a discovered difference is published under that cause. */
  refresh(opts?: { live?: boolean; silent?: boolean; attribute?: MoneyChangeReason }): Promise<void>;
}
```

#### 7.2.2 `applyDebit` / `applyCredit`

Replaces `TokenBalance.vue:142-149`. Keeps the existing shape exactly:

- `amount <= 0` → **no-op**, no chip. (A free bet passes `0` today and must stay silent.)
- otherwise: `balance = max(0, balance ± amount)`, publish `{ delta: ±amount, reason }`, then
  `restartPoll()` — pushing the next poll a full interval out so the reconcile lands **after** the
  on-chain debit has propagated, exactly as the shipped comment at `TokenBalance.vue:140-141`
  explains. Losing that `restartPoll` is how a stale read reverts the optimistic value.

#### 7.2.3 Poll lifecycle

The poll runs while **any** consumer is active. `activate()` increments a refcount and starts the
timer on `0 → 1`; `deactivate()` decrements and clears it on `1 → 0`. This replaces
`TokenBalance.vue`'s `onMounted`/`onUnmounted` pair and is what makes "only one poll of the balance
endpoint is in flight for the app" (AC 24) structurally true rather than incidental.

#### 7.2.4 ⚠️ Code finding — the duplicate fetches are real, and they are not deduped

`mainService.getTokenBalance` (`mainService.ts:228-234`) is one of the **few** GETs in that file that
does **not** go through `singleFlight` (compare `:82`, `:122`, `:159`, `:170`, `:185`, `:197`). So
today three independent, undeduplicated readers hit the endpoint:

- `TokenBalance.vue:94` — header, on a 30 s poll,
- `OrbitModal.vue:752` — panel MAX sizing,
- `OrbitMoneyTab.vue:83` — page MAX sizing.

Both Orbit call sites carry a comment explaining *why* they duplicate — `OrbitModal.vue:733-738`:
*"Read here rather than shared from the header's TokenBalance: that component keeps its number in
local state with no provide/inject, so there is nothing to reuse without turning it into a store."*
**This composable is that store**, and those two comments must be replaced (not deleted) with a
pointer to it.

#### 7.2.5 ⚠️ The round-outcome refreshes move to `useGame`, not into this composable

`TokenBalance.vue:163-183` refetches on `gameInstance.status`. That watcher **cannot** live in
`useTokenBalance`: it needs `useGame().gameInstance`, and `useGame` must import `useTokenBalance` for
the bet debit — a **circular import**. Put it in `useGame.ts` instead, at **module level** (outside
the exported function, so it registers once), following the precedent of `useOrbit.ts:1077`'s
auto-park watcher. `useGame` already imports `useOrbit` (`useGame.ts:24`), so no new cycle is created.

This is also where attribution belongs, because this is where the cause is known:

| Status | Read | Attribution |
| --- | --- | --- |
| `Ended` | `live`, after `WINNER_SETTLE_DELAY_MS` (5 000) | `'win'` — **only if the player bet this round**; otherwise unattributed |
| `RewardForWinnersCompleted` | `live` | `'win'`, same guard |
| `ErrorWhenProcessingRewards` | `live` | unattributed (nothing good happened) |
| `NotEnoughPlayersToStart` | `live` | `'refund'` |

⚠️ **An armed read publishes only a same-signed delta.** `'win'`/`'refund'` publish only when the
discovered delta is **positive**; `'stake'` only when negative. A losing player whose balance moved
for an unrelated reason must not be shown a win chip. If the sign disagrees with the cause, the
number updates **silently**.

#### 7.2.6 ⚠️ The mint, and the one guard Orbit's consumers need

The header owns the mint: `GameHeader.vue` already computes
`selectedGame?.tokenMintAddress || USDC_TOKEN_ADDRESS` (`:33-35`) and passes it to `TokenBalance`. It
now calls `setMint()` with the same value.

Orbit's own consumers read `orbit.mintAddress`, which is a **different source**. In the shipped
configuration these are the same token, but the composable must not assume it:

> `OrbitModal` / `OrbitMoneyTab` use the shared balance **only when
> `currentMint() === orbit.mintAddress`**. If they differ, `walletTokenBalance` stays `null` and MAX
> falls back to the deposit ceiling — which is exactly what those controls already do with no
> `available` (`OrbitModal.vue:731-732`). Never render a balance in the wrong token.

This guard is expected never to fire. Ship it anyway; it is three lines and the failure it prevents
is showing one token's balance under another's label on a money surface.

### 7.3 `useOrbit` — the Orbit balance gets the same signal

`src/hooks/useOrbit.ts`. Add module-level state beside the existing block (`:46-55`):

```ts
const orbitChange = ref<MoneyChange | null>(null);
/** Optimistic delta, DISPLAY ONLY. Never read by an affordability rule — see §7.3.2. */
const orbitOptimisticDelta = ref(0);
```

#### 7.3.1 Attribution points

| Where | Trigger | Publishes |
| --- | --- | --- |
| `deposit()` (`:473`) | after `sendToken` confirms, **before** the credit POST | `+amount, 'deposit'` on Orbit **and** `applyDebit(amount, 'deposit')` on the wallet — same block (D8) |
| `cashOut()` (`:651`) | after `requestOrbitCashOut` resolves | `−(amount ?? balance), 'cashout'` on Orbit **and** `applyCredit(same, 'cashout')` on the wallet — same block (D8) |
| `useOrbitNotifications.onWin` (`:43-72`) | the SignalR win push | arm the existing `refreshQuietly(wallet)` with `'win'`; publish the discovered delta **only if positive** |
| Orbit auto-bet (new, D6) | `myBetThisRoundIsOrbit` flips true | `−stake, 'stake'` (§7.3.3) |

⚠️ **`refreshQuietly` / `load` are otherwise silent.** Every unarmed read — boot `loadIfSignedIn`,
`syncWallet`, a settings PATCH's returned state, the deposit/cash-out reconcile — assigns the balance
with **no** change published. It also **clears `orbitOptimisticDelta` to 0**: the server figure is now
authoritative and the optimistic guess has been superseded.

The `onWin` amount is deliberately taken from the **read**, not from `win.amount`. The shipped comment
at `useOrbitNotifications.ts:54-55` explains why: *"a threshold cash-out fires the instant the credit
lands, so the only trustworthy number is a fresh read."* A win that arrives together with a threshold
cash-out therefore nets out and — being non-positive — publishes nothing rather than lying.

#### 7.3.2 ⚠️⚠️ Code finding (D15) — optimism must NOT feed the auto-park watcher

This is the one thing in this card that can break shipped money behaviour, and it is not obvious.

`useOrbit.ts:1077-1098` watches an affordability condition and, when Orbit can no longer pay for a
round, **PATCHes `enabled: false`** — it parks Orbit **in the database**. `isPausedForFunds`
(`:694-709`) and `needsFirstDeposit` (`:726-732`) both read `orbit.value.balance` directly.

If the optimistic Orbit debit (D6, deposit, cash-out) were written into `orbit.value.balance`, it
would feed that watcher **on a guess**: an Orbit whose bet is later refunded, or whose optimistic
figure is simply wrong, would get **switched off in storage** before the server ever agreed. That is a
real, persisted state change caused by an animation.

**Required design — non-negotiable:**

- `orbit.value.balance` stays **server truth**. Nothing optimistic is ever written into it.
- The optimistic delta lives in `orbitOptimisticDelta`, and only the **display** balance adds it:

  ```ts
  const balance = computed(() => (orbit.value?.balance ?? 0) + orbitOptimisticDelta.value);
  ```

- `isPausedForFunds`, `needsFirstDeposit`, `roundsLeft`, `orbitStatus` and the auto-park watcher keep
  reading `orbit.value.balance` — **unchanged**. Do not "tidy" them onto the new `balance` computed.
- `orbitOptimisticDelta` resets to `0` on every successful `load()`/`refreshQuietly()` and in
  `reset()` (`:255-267`).

Add this reasoning as a comment on `orbitOptimisticDelta`. The next agent reading
`balance = (orbit.balance ?? 0) + delta` will otherwise "simplify" it straight back into the bug.

#### 7.3.3 The Orbit auto-bet debit (D6)

Lives in `useGame.ts`, module level, beside the status watcher from §7.2.5 — it needs
`myBetThisRoundIsOrbit` (`useGame.ts:202-214`) and the viewed game's stake, and `useGame` already
imports `useOrbit`.

```
watch myBetThisRoundIsOrbit
  fire when: orbitEnabled && it flipped false → true
             && this round's gameId has not already been charged
             && the viewed game's requiredAmount > 0
  then:      useOrbit().applyOrbitDebit(stake, 'stake')
             remember the gameId
```

- **Once per round.** Guard on a module-level `lastOrbitDebitGameId`; the bet list is re-delivered on
  reconnect and on refetch, and `myBetThisRoundIsOrbit` is a computed over it.
- **Stake source:** the *viewed* game's `requiredAmount` (`selectedGame`), because the AC scopes this
  to the game the player is watching. Not `activeGameType` — Orbit may be pointed elsewhere.
- **Accepted limitation (on the card):** Orbit betting on a game the player is not viewing stays
  silent until the next `GET /orbit`. That read is unarmed, so the corrected value lands with no chip
  (AC 9).

### 7.4 `MoneyValue.vue` — the one shared animated readout (D7)

New file `src/components/ui/MoneyValue.vue`, beside `ArcadeModal.vue`. **Four call sites, one
implementation** — the whole point of the component is that there is no second one to drift.

```ts
defineProps<{
  value: number;
  change: MoneyChange | null;
  /** D14. Header pills pass formatCompact; Orbit surfaces pass money(). */
  format: (n: number) => string;
  /** Where the chip floats relative to the number. */
  chipAlign?: 'center' | 'end';
}>();
```

The component owns the **motion, the colour and the queue**. It owns **no colour of the number
itself** — that stays the call site's (`text-orbit` on holds, `text-ink` on the stake, the pill's own
ink in the header), exactly as `.orbit-statstrip__number` already refuses to set `color`
(`arcade.css:1245-1248`).

#### 7.4.1 Detecting an attributed change

Keep `lastSeq` and `displayed` as local refs. Watch `() => props.value`:

```
on value change:
  if (props.change && props.change.seq !== lastSeq) → ATTRIBUTED
       lastSeq = props.change.seq
       roll displayed → props.value
       enqueue a chip for props.change.delta
  else → SILENT
       displayed = props.value        (snap, no chip)
```

This is why §7.0.1 requires value-then-change in one synchronous block: Vue batches them into a
single callback and the component never has to reason about watcher order.

#### 7.4.2 The roll

`requestAnimationFrame` tween over `ROLL_MS = 600`, ease-out (`t => 1 - Math.pow(1 - t, 5)` — the
JS twin of `--ease-out-quint`). Cancel any in-flight frame on a new change and on unmount. The
target is always the **current** `props.value` at the moment the segment starts, so a burst of
changes still converges on the authoritative figure.

#### 7.4.3 The chip queue and coalescing

Chips play **serially**, one at a time, `CHIP_MS = 900` each.

- A change arriving mid-animation is **queued**.
- When the queue would exceed `QUEUE_COALESCE_AT = 2` pending entries, **collapse the pending
  entries into one net figure** (sum the deltas) rather than backing up a visible queue. The
  collapsed chip's sign is the sign of the net; a net of exactly `0` is **dropped**.
- The number does **not** queue — it always rolls toward the current value (§7.4.2).

#### 7.4.4 A deposit and a cash-out (D8)

Nothing special in the component. Because both sides are applied optimistically in one synchronous
block (§7.3.1), the wallet pill and the Orbit pill each receive their own change in the same tick and
animate together in opposite directions. This is the designed behaviour, not a coincidence — do not
"fix" it by serialising the two.

#### 7.4.5 Width reservation during a roll (D14)

`formatCompact` produces variable-length strings (`999` → `1.2K`), so `tabular-nums` alone does not
stop the pill jittering mid-roll. For the duration of the roll only:

```
min-width: max(format(from).length, format(to).length) ch
```

released when the tween ends. `tabular-nums` (already load-bearing on both Orbit surfaces —
`OrbitStatusBanner.vue:167-176`, `arcade.css:1253`) makes `ch` a truthful unit here. This preserves
what each surface shows today: the header keeps compact figures and stays inside a 54 px bar at
360 px; Orbit keeps its 2 dp `money()`.

#### 7.4.6 Baseline, correction and reduced motion

- **Baseline.** The first value a consumer ever renders — first load, a wallet switch, an Orbit
  sign-in, a mint switch — is a **baseline**: `displayed` is set with no roll and no chip. Producers
  guarantee this by never publishing a change alongside a baseline assignment.
- **Silent correction.** An optimistic figure contradicted by the authoritative read corrects
  **silently** — no second chip, and never a chip reversing the first. This falls out of the design:
  the reconciling read is unarmed.
- **Reduced motion (D9).** `window.matchMedia('(prefers-reduced-motion: reduce)')`, read reactively.
  When set: no rAF tween (`displayed` snaps) and the chip gets the in-place fade variant — it still
  says `+ 5.00` in green or `− 1.00` in dim ink, it just does not travel. **The information survives;
  only the movement is dropped.**
- **Light graphics (D10).** No branch at all. Phones get the full animation. See §7.8.

### 7.5 The header (`GameHeader.vue`)

#### 7.5.1 The wordmark (D2)

`GameHeader.vue:184-191`. The two-line-stack-below-`sm` treatment and the
`hidden min-[360px]:flex … sm:block` chain collapse to a single rule:

```
hidden lg:block   (+ keep the existing lg text size and the two SPACE / STROKER spans)
```

The `<span class="text-primary sm:ml-[0.5em]">STROKER</span>` inline spacing is kept — at `lg` the
wordmark renders exactly as it does today. Below `lg` it is not rendered at all, so nothing about it
can push a control off-screen.

#### 7.5.2 The cluster

`GameHeader.vue:257-279`. The wrapper `<div class="flex flex-col items-end gap-0.5 min-w-0">` around
`TokenBalance` (`:264-270`) is **deleted** — the number moves inside the wallet pill, so a column
wrapper holding a single item is dead furniture. The row becomes:

```
[ Orbit pill ]  [ wallet pill ]        (gap-1.5 at every width, unchanged — the comment at :254-256
                                        explains why this is not md:gap-3, and it still applies)
```

`TokenBalance.vue` is **retired as a header component**. Its remaining job — the token-name tooltip
(`:42-56`, `:220-233`) — moves onto the wallet pill so the "which currency is this" affordance is not
lost. Delete the file only once that tooltip has a home; see §10.2.

Gates are unchanged: `OrbitLauncher` keeps `v-if="orbitEnabled && !isEmbedded && effectiveWallet"`
(`:275`), `WalletButtons icon-only` keeps its own connected/disconnected branch.

### 7.6 The two pills

#### 7.6.1 `WalletButtons.vue` — the amber pill

`:71-83`. The `isDoor` branch's class list gains a pill variant: same `.btn-door` base (see §7.8 —
**extend, never fork**), `width: auto` with horizontal padding, glyph + `MoneyValue`.

- ⚠️ **The disconnected branch is untouched** — `Connect Wallet`, its label, and `md:w-[200px]`
  (`:81`). The file's own comment at `:62-69` explains why, and it still holds.
- The `aria-label` / `title` keep today's `My wallet · <shortAddress>` (`:123-124`, `:190-191`) —
  the pill now *shows* a number, but the accessible name is about which door this is.
- Reads `useTokenBalance()`; the wallet pill is the primary consumer that calls `activate()`.

#### 7.6.2 `OrbitLauncher.vue` — the violet pill

- Becomes a pill carrying `useOrbit().balance` through `MoneyValue`.
- **Keeps:** the paused red dot (`:57-61`), the violet focus ring (`:71-74`), and the three
  `aria-label` states (`:37-41`), extended to name the balance when one is shown.
- ⚠️ **The signed-out state (D13) must not gate on `balance`.** `useOrbit().balance` is
  `orbit.balance ?? 0`, so it reads `0` when Orbit has simply not loaded. Gate on **`hasSession &&
  orbit !== null`**. A session with a genuinely zero balance shows `0.00`; no session shows the
  mockup-chosen signed-out state (collapsed door, or a pill reading `ORBIT`), **never `0`**.
- The header comment at `:7-17` is **rewritten**, not deleted: keep the three-failed-attempts history
  and the 2026-08-04 call, then record the 2026-08-07 reversal and its scope (§1.1).
- **Zero new requests.** The pill renders state the header already loads
  (`GameHeader.vue:82-95`). Flag-off builds still make no `/orbit/*` call, because the mount gate and
  the `orbitEnabled` guard inside the wallet watcher (`:89`) are untouched.

### 7.7 Orbit's own readouts, and the shared formatter

- `OrbitStatusBanner.vue:79` (`ORBIT HOLDS`) and `OrbitStatStrip.vue:72` (`ORBIT HOLDS`) each swap
  their `{{ money(orbitBalance) }}` / `{{ money(holds) }}` span for `MoneyValue`, passing their own
  `money()` as `format`. Both are **presentational, props-only** components (see their header
  comments) — that rule holds: `OrbitModal.vue` and `OrbitPage.vue` pass the `change` down as a prop
  alongside the value. Do **not** import `useOrbit` into either component.
- `PER ROUND`, `WALLET BALANCE` and the stake cells are **not** animated — they are rates and
  reference figures, not the player's moving money.
- `formatCompact` moves from `TokenBalance.vue:58-78` to **`src/utils/money.ts`** and is exported
  unchanged, so the two header pills and their chips share one definition. `money()` stays duplicated
  in the two Orbit components exactly as today — unifying it is not this card's job.

### 7.8 `arcade.css`

- **`.btn-door` (`:940-962`) is extended, never forked.** Add a `.btn-door--pill` modifier that
  overrides `width` and `padding` only; geometry, fill, hover and the press dip stay in the shared
  rule. The file's own comment at `:920-939` explains that these two header squares exist precisely
  so they cannot drift apart — a forked pill would undo that.
- **New `.money-value`, `.money-value__number`, `.money-value__chip`** + **`@keyframes
  money-chip-rise`** and **`money-chip-fade`**. ⚠️ These are the **first `@keyframes` in
  `arcade.css`** — the file has none today, and `tailwind.config.js` declares no `keyframes` /
  `animation` extension either (`main.css:16` holds the app's only other one, `blink`). This is
  greenfield; there is no existing count-up, flash or transition infrastructure to extend.
- **Reduced motion (`:1681-1695`)** — add `.money-value__number` (no transition) and swap
  `.money-value__chip` onto `money-chip-fade` (opacity only, no `translate`).
- ⚠️ **Do NOT add anything from this feature to the light-graphics strip list (`:1778-1787`).** That
  list is for **infinite** animations that repaint forever; this is a 900 ms one-shot. Write the
  exception comment next to `.animate-wobble`'s (`:1789-1796`), which is the exact same argument
  already made and accepted once — so a future perf pass does not re-add the selector and silently
  kill the animation on every phone.

### 7.9 The mockup gate (D12)

**No production code before the picks.** Produce and get a decision on:

1. **3 header pill-cluster options** — each must show: both pills at `lg+`, both at 360 px with the
   wordmark cut, and the **signed-out Orbit state** (D13 candidate (a) *and* (b) across the set).
2. **3 change-animation options** — each must show a credit, a debit, and the simultaneous
   deposit pair.

Only after one of each is chosen does §7.1–§7.8 get built.

---

## 8. Flow

### 8.1 A manual bet, then a win

```mermaid
sequenceDiagram
  participant P as Player
  participant G as useGame
  participant B as useTokenBalance
  participant V as MoneyValue (wallet pill)
  participant API as /solana/balance

  P->>G: place bet (stake 1.00)
  G->>B: applyDebit(1.00, 'stake')
  B->>B: balance 12.34 → 11.34; change{seq+1, -1.00}
  B-->>V: value + change (same tick)
  V->>V: roll 12.34 → 11.34 + dim-ink "− 1.00" chip
  B->>B: restartPoll()  (reconcile lands after propagation)

  Note over G: round ends — player bet this round
  G->>B: refresh({live, attribute:'win'}) after 5s
  B->>API: GET /solana/balance/live
  API-->>B: 16.34
  B->>B: delta +5.00 (positive, matches cause) → publish
  B-->>V: value + change
  V->>V: roll 11.34 → 16.34 + green "+ 5.00" chip

  Note over B: 30s poll tick — unattributed
  B->>API: GET /solana/balance
  API-->>B: 16.34 (or anything)
  B->>B: assign value, publish NOTHING
  V->>V: snap, no chip
```

### 8.2 A deposit — both pills, one tick (D8)

```mermaid
sequenceDiagram
  participant P as Player
  participant O as useOrbit.deposit
  participant B as useTokenBalance
  participant W as wallet pill
  participant R as Orbit pill

  P->>O: deposit(10)
  O->>O: sendToken confirms on-chain
  par one synchronous block
    O->>B: applyDebit(10, 'deposit')
    O->>O: orbitOptimisticDelta += 10; publish +10 'deposit'
  end
  B-->>W: dim-ink "− 10.00", roll down
  O-->>R: green "+ 10.00", roll up
  O->>O: POST credit, then refreshQuietly (UNARMED)
  Note over O: server truth lands, orbitOptimisticDelta → 0, no chip
```

---

## 9. Out of scope

- **Any backend change** — no `OrbitNotification` bet-placed kind, no new endpoint, no migration.
- **New polling of `GET /orbit`** — the page's "it does not poll" stance stands.
- Animating `OrbitMoneyTab`'s cash-out amount, MAX figure, or the "won't cover a…" line.
- The ☰ menu, the audio cluster, the logo art, and every non-money header control.
- Practice mode and every non-money number (pot, character stream, stats).
- **Sound** for money changes — `features/audio-overhaul.md` dropped the proximity "coin" bleeps; a
  money SFX is a separate conversation.
- Fixing `formatCompact`'s K/M/B behaviour anywhere it is **not** animating.
- Unifying Orbit's two duplicated `money()` helpers.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

⛔ **No files. This repo is not touched.**

### 10.2 Frontend (`monkify-front`)

| # | File | Change |
| --- | --- | --- |
| 1 | `src/types/money.ts` | **NEW** — `MoneyChange`, `MoneyChangeReason` (§7.0.1) |
| 2 | `src/utils/money.ts` | **NEW** — `formatCompact` moved out of `TokenBalance.vue:58-78`, unchanged |
| 3 | `src/hooks/useTokenBalance.ts` | **NEW** — module singleton: balance, change, refcounted poll, `applyDebit`/`applyCredit`, armed `refresh`, mint ownership (§7.1–§7.2) |
| 4 | `src/components/ui/MoneyValue.vue` | **NEW** — the shared animated readout: roll, ± chip, queue/coalesce, baseline, reduced-motion (§7.4) |
| 5 | `src/hooks/useOrbit.ts` | `orbitChange` + `orbitOptimisticDelta`; display-only `balance` computed; attribution in `deposit`/`cashOut`; ⚠️ affordability rules and the auto-park watcher keep reading `orbit.value.balance` (§7.3.2) |
| 6 | `src/hooks/useOrbitNotifications.ts` | arm the `onWin` `refreshQuietly` with `'win'`; publish only a positive discovered delta (§7.3.1) |
| 7 | `src/hooks/useGame.ts` | delete `updateBalanceCallback` (`:64`, `:466`, `:920`) → call `useTokenBalance` directly; **new module-level watchers**: round-outcome refreshes (§7.2.5) and the Orbit auto-bet debit (§7.3.3) |
| 8 | `src/components/GameHeader.vue` | wordmark `hidden lg:block` (`:184-191`); drop the `TokenBalance` column wrapper (`:264-270`); `setMint()`; rewrite the `:258-274` comment block for the reversal |
| 9 | `src/components/header/WalletButtons.vue` | connected `isDoor` branch → `.btn-door--pill` with glyph + `MoneyValue` + the token-name tooltip; ⚠️ disconnected branch untouched (§7.6.1) |
| 10 | `src/components/orbit/OrbitLauncher.vue` | pill carrying `useOrbit().balance`; signed-out state gated on `hasSession && orbit !== null`; keep dot / focus ring / aria states; rewrite the `:7-17` comment (§7.6.2) |
| 11 | `src/components/orbit/OrbitStatusBanner.vue` | `ORBIT HOLDS` value → `MoneyValue` with `money()`; new `change` prop; stays props-only |
| 12 | `src/components/orbit/OrbitStatStrip.vue` | same for the `/orbit` strip; stays props-only |
| 13 | `src/components/orbit/OrbitModal.vue` | pass `change` into the banner; drop `readWalletBalance` (`:745-759`) for the shared composable + mint guard (§7.2.6); replace the `:733-738` comment |
| 14 | `src/components/orbit/tabs/OrbitMoneyTab.vue` | drop `readWalletBalance` (`:76-101`) + its mint watcher for the shared composable + mint guard; replace the `:63-75` comment |
| 15 | `src/pages/OrbitPage.vue` | pass `change` into `OrbitStatStrip` (`:426`) |
| 16 | `src/components/TokenBalance.vue` | **DELETE** — poll/state to `useTokenBalance`, formatter to `utils/money.ts`, tooltip to the wallet pill. ⚠️ Delete only once the tooltip has a home |
| 17 | `src/assets/styles/arcade.css` | `.btn-door--pill` (extends, never forks `:940-962`); `.money-value*` + the file's **first** `@keyframes`; reduced-motion additions (`:1681-1695`); ⚠️ **no** light-graphics strip entry, with the exception comment (§7.8) |
| 18 | `.env.example` | comment only — `VUE_APP_BALANCE_POLL_INTERVAL_MS` now lives in `useTokenBalance` |

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/money-changing-animations.md` | **NEW** — this file |
| 2 | `INDEX.md` | KB map row + "Feature currently in flight" |
| 3 | `features/money-changing-animations.md` | **NEW at ship time** (card level = `Feature`). Must record the 2026-08-04 → 2026-08-07 header reversal (§1.1) and the D15 auto-park hazard |
| 4 | `features/orbit-custodial-wallet.md` | ⚠️ **Correction at ship time** — its Frontend section asserts the header carries no Orbit number. Made false by this card |
| 5 | `features/orbit-ui-overhaul.md` | ⚠️ **Correction at ship time** — same assertion. Its **colour rules stay intact**; add the credit-green / debit-dim-ink idiom as an explicit *"and this is why the debit is not red"* note (D4) |
| 6 | `features/light-graphics-mode.md` | ⚠️ **Correction at ship time** — record the one-shot exception (D10) beside `.animate-wobble`'s |

---

## 11. Tests

### 11.1 Backend

⛔ **N/A — no backend change.** `constraints.md` §3 (unit tests) and §8 (mandatory integration test
for every new feature) both scope to `monkify-backend`. Neither applies. Do not add a test to
`SpaceStroker.Tests` or `SpaceStroker.Tests.Integration` for this card.

### 11.2 Frontend

⚠️ **`monkify-front` has no test harness.** Verified: no `*.spec.*` / `*.test.*` anywhere under
`src/`, and `package.json` declares only `serve`, `build`, `lint`, `lint:fix`. Standing up a test
runner is **not** in this card's scope.

Verification is therefore:

| Gate | Command / method |
| --- | --- |
| Lint + format | `yarn lint` — must be clean (see the CRLF warning in §2) |
| Build | `yarn build` |
| Behaviour | §12 acceptance criteria, walked on the local Docker stack |
| Manual matrix | §13 |

---

## 12. Acceptance criteria

Copied 1:1 from the Trello checklist (25 items).

1. Given a connected wallet and an Orbit session on a viewport ≥ 1024 px, when the header renders,
   then both header doors are pills — the amber wallet pill showing the on-chain balance and the
   violet Orbit pill showing the Orbit balance — and each still opens its panel/menu when tapped.
2. Given a viewport below 1024 px, when the header renders, then the `SPACE STROKER` wordmark is not
   rendered at all (logo only) and the logo, both pills and the menu button fit the 54 px bar with no
   horizontal overflow and no clipped control.
3. Given a 360 px-wide phone in portrait and the same device in landscape, when both pills carry
   balances, then nothing is pushed off-screen and the page body does not scroll horizontally.
4. Given the player places a manual bet, when the stake is debited, then the wallet pill's number
   rolls from the old value to the new one and a dim-ink `− <stake>` chip rises above the pill and
   fades out.
5. Given the player bet on a round that ends in a win, when the payout is read, then the wallet pill
   rolls up to the new balance and a green `+ <amount>` chip rises and fades.
6. Given a round is cancelled for too few players, when the refund lands on the wallet balance, then
   a green `+ <stake>` chip is shown on the wallet pill.
7. Given a deposit completes, when both balances update, then the wallet pill shows a dim-ink
   `− <amount>` and the Orbit pill a green `+ <amount>` at the same time; a cash-out shows the mirror
   image.
8. Given Orbit places an auto-bet on the game the player is currently viewing, when the live bet
   stream marks the player's bet for that round as Orbit-funded, then the Orbit balance is
   decremented locally by the game type's stake and a dim-ink `− <stake>` chip is shown.
9. Given Orbit places an auto-bet on a game the player is **not** viewing, when the stake leaves the
   Orbit balance, then no chip is shown and the balance simply reads the corrected value on the next
   `GET /orbit` (unchanged from today).
10. Given the background balance poll returns a value different from the last known one with no
    attributed cause (money arriving from outside the app, or a catch-up read after idle), when the
    number updates, then it updates with no roll and no chip.
11. Given a stake, win, refund, deposit or cash-out changes the Orbit balance while the Orbit panel
    or the `/orbit` page is open, when the change lands, then the `ORBIT HOLDS` figure in
    `OrbitStatusBanner` and `OrbitStatStrip` animates with the same shared component as the header
    pills.
12. Given the `OrbitMoneyTab` cash-out amount, MAX figure and "won't cover a…" line, when the player
    edits the amount, then none of those figures animate.
13. Given `prefers-reduced-motion` is set, when any attributed change occurs, then the number snaps to
    its new value and the chip fades in and out in place with no roll and no upward travel.
14. Given a phone (permanently in light-graphics mode), when an attributed change occurs, then the
    full roll and floating chip play — the animation is a one-shot and is **not** added to the
    light-graphics `animation: none` strip list in `arcade.css`.
15. Given a debit on any surface, when its chip renders, then the chip is dim ink and never red — red
    remains reserved for warnings only (paused, no floor, failed deposit), per
    `features/orbit-ui-overhaul.md`.
16. Given a connected wallet with no Orbit session, when the header renders, then the Orbit control
    shows the signed-out state chosen from the mockups (collapsed door, or a pill reading `ORBIT`) and
    never renders a `0` balance.
17. Given an Orbit session whose balance is genuinely zero, when the header renders, then the Orbit
    pill shows `0.00` as a normal readable value.
18. Given the wallet is not connected, when the header renders, then no wallet pill is shown, the
    `Connect Wallet` button is unchanged (including its `md:w-[200px]` reflow guard), and nothing
    animates.
19. Given `VUE_APP_ORBIT_ENABLED` is anything other than `"true"`, when the app loads, then no Orbit
    pill renders anywhere and the app makes zero `/orbit/*` calls (verified in the network tab, not by
    grepping the bundle).
20. Given the app is running embedded in an iframe, when the header renders, then no Orbit pill is
    shown and the wallet pill still renders and still animates its attributed changes.
21. Given the player connects a different wallet, or the page loads for the first time, when the
    balance resolves, then that value is taken as a baseline — the number is rendered without a roll
    and no chip is shown.
22. Given an optimistic decrement is later contradicted by the authoritative server value, when the
    live read lands, then the number corrects to the server value silently — no second chip and no
    chip reversing the first.
23. Given two or more attributed deltas land inside the animation window, when they render, then chips
    play serially one at a time; beyond ~2 pending they coalesce into a single net figure rather than
    backing up a visible queue.
24. Given `useTokenBalance` has shipped, when the app runs, then `OrbitModal` and `OrbitMoneyTab` no
    longer perform their own on-chain balance fetch and only one poll of the balance endpoint is in
    flight for the app.
25. Given implementation begins, when the header cluster and the change animation are built, then 3
    visual options were proposed for each and the user picked one of each before any production code
    was written.

---

## 13. Manual QA

Local Docker stack (`docker compose up -d --build` in `monkify-backend`, API on `:6001`), front on
`yarn serve`. Every phone is in light-graphics, so the mobile checks are not optional.

1. **Desktop, connected + Orbit signed in** — both pills carry numbers; each still opens its
   surface. (AC 1)
2. **Manual bet** — wallet pill rolls down, dim-ink `−` chip. (AC 4)
3. **Win a round** — wallet pill rolls up ~5 s after `Ended`, green `+` chip. (AC 5)
4. **Under-subscribed round** (bet alone, let it cancel) — green refund chip. (AC 6)
5. **Deposit 10, then cash out** — both pills animate simultaneously, opposite directions, both ways.
   (AC 7)
6. **Orbit auto-bet on the viewed game** — Orbit pill rolls down by the stake, dim chip. Then
   navigate away and let Orbit bet elsewhere: no chip, value corrects silently on the next read.
   (AC 8, 9)
7. **Idle the tab past the poll**, return — number updates with no roll and no chip. (AC 10)
8. **`/orbit` page + Orbit panel open** during a change — `ORBIT HOLDS` animates on both. Type in the
   cash-out field — nothing animates. (AC 11, 12)
9. **Responsive:** 360 px portrait, 360 px landscape, and ≤ 1023 px generally — wordmark absent, no
   horizontal body scroll, no clipped control. (AC 2, 3)
10. **`prefers-reduced-motion: reduce`** (DevTools → Rendering → Emulate CSS media) — snap + in-place
    chip. (AC 13)
11. **Phone / light-graphics** — full animation still plays. (AC 14)
12. **Signed-out Orbit** (connected wallet, no session) — mockup-chosen state, never `0`. Then sign in
    with a genuinely empty balance — `0.00`. (AC 16, 17)
13. **Disconnect the wallet** — `Connect Wallet` unchanged, nothing animates. (AC 18)
14. **`VUE_APP_ORBIT_ENABLED=false` build** — no Orbit pill, **zero `/orbit/*` requests in the network
    tab**. (AC 19)
15. **Embedded (`/embed` iframe)** — no Orbit pill, wallet pill still animates. (AC 20)
16. **Switch wallets** — new balance is a baseline, no chip. (AC 21)
17. **Network tab across a full session** — exactly one recurring `/solana/balance` poll; opening the
    Orbit panel and the Money tab adds **no** extra balance read. (AC 24)

---

## 14. Trello / pipeline

- **Source card:** [Add money changing animations (#151)](https://trello.com/c/AaPjqrw5) — list
  **Specs created**; branch, worktree and spec path are in the card's **Implementation** block.
- **Next:** `/monkify-implement-feature Add money changing animations` — ⚠️ it must run the **mockup
  gate (§7.9 / D12) first** and get both picks before writing production code.
- **Then:** `/monkify-ship-feature Add money changing animations` — which must also land the §10.3
  doc corrections, including the new `features/money-changing-animations.md`.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-07 | Initial spec from refined card #151 (refined 2026-08-07, answer mode Short) + live code exploration of `monkify-front`. Resolved D14 (formatting: injected formatter + width reservation) and D15 (optimistic delta must not feed Orbit's auto-park watcher) — both delegated to the spec by the card. |
