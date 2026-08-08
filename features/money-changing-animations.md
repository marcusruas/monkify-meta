# Feature — Money change animations

> Status: **shipped.** Authoritative spec:
> [`../specs/money-changing-animations.md`](../specs/money-changing-animations.md)
> (merged via `feature/money-changing-animations` on 2026-08-08).
> Trello #151 ([AaPjqrw5](https://trello.com/c/AaPjqrw5)). **Frontend-only — `monkify-backend` is
> not touched by this card.**

## What it does

Money used to move silently. A player's on-chain wallet balance and their Orbit balance both change
several times a round — a stake leaves, a win lands, a refund returns, a deposit moves money from one
to the other — and every one of those was a number quietly replacing itself. This ships **one shared
animated money readout** (`MoneyValue`): the figure rolls old → new while a floating `+ / −` chip
rises past it. And it gives the two numbers a home to animate in — the header's two 40px icon doors
become **balance-carrying pills**, `[◎ 5.00]` violet Orbit and `[▤ 12.34]` amber wallet.

Everything Orbit-side is still gated by `VUE_APP_ORBIT_ENABLED` (fail-closed), so the Orbit pill is
invisible in production while Orbit stays dark.

## Core rules

### Attribution — the idea everything hangs off

- **Only an *attributed* change animates.** A balance change is either attributed (the app knows what
  caused it) or it is not. Attributed → the number **rolls** and a chip fires. Unattributed → the
  number **snaps**, silently.
- There are **exactly two** ways a change becomes attributed:
  1. **The amount is known up front** — a stake, a deposit, a cash-out. Applied optimistically and
     published in the **same synchronous block** as the new value.
  2. **The cause is known but the amount is not** — a win, a refund. The caller **arms the read**
     (`refresh({ attribute })`), and the discovered difference is published under that cause.
- ⚠️ **An armed read publishes only a same-signed delta.** `win`/`refund`/`cashout` publish only a
  **positive** discovered delta; `stake`/`deposit` only a negative one. A losing player whose balance
  moved for an unrelated reason is never shown a win chip — the number just updates silently.
- **Everything else is unattributed by construction**: the 30s poll tick, the `visibilitychange`
  catch-up, the idle-disconnect return, a mint switch, the first load, a wallet switch. Money
  arriving from outside the app lands silently, **on purpose** — two changes inside one poll window
  would collapse into one net figure that tells the wrong story (a `−1.00` stake and a `+5.00` win
  rendering as a single `+4.00` makes the bet look like it never happened).
- **A zero delta is never published** — a chip that says nothing is worse than no chip.
- **The first value a surface ever renders is a baseline**, never a delta: first load, a wallet
  switch, an Orbit sign-in, a mint switch. No roll, no chip.
- **An optimistic figure contradicted by the server corrects silently.** No second chip, and never a
  chip reversing the first — this falls out of the design, because the reconciling read is unarmed.

### The motion

- **The number rolls** over `ROLL_MS = 600` on `requestAnimationFrame`, ease-out-quint, `tabular-nums`.
  The target is always the *current* value when a segment starts, so a burst still converges on the
  authoritative figure.
- ⚠️ **The chip rises from BELOW the number and never sits above it.** The header bar is the top of
  the viewport — a 40px pill in the 64px bar leaves ~12px of headroom, and only ~5px below `lg` where
  the bar is 54px and the pill grows to 44px — so a chip rising *above* would be clipped by the window
  edge. **This is a deviation from the spec** (§7.4/D3-D4 said the downward travel carried the
  direction): travel is now **identical in both directions**, and **colour plus the `+ / −` sign carry
  credit vs debit on their own**.
- ⚠️ **A credit HOLDS, a debit does not.** A credit rises (~410ms), sits motionless ~2.5s, then fades
  (~510ms) — total **3400ms**. A debit is a plain **900ms** rise-and-fade. A win is the one money
  event worth dwelling on; a stake leaves on *every* round, so a dim chip loitering that long would
  nag rather than inform.
- ⚠️ **`CHIP_MS_CREDIT` / `CHIP_MS_DEBIT` in `MoneyValue.vue` must stay in sync with the CSS
  durations.** They gate the queue; if they drift, chips either overlap or leave a gap.
- **Chips play serially**, one at a time. Beyond `QUEUE_COALESCE_AT = 2` pending, the pending entries
  **collapse into one net figure** rather than backing up a visible queue; a net of exactly zero is
  dropped. The **number** never queues — it always rolls toward the current value.
- **Width is reserved for the duration of the roll only** — `max(len(from), len(to))ch` plus the
  prefix. `formatCompact` produces variable-length output (`999` ↔ `1.2K`), so `tabular-nums` alone
  does not stop a pill jittering mid-roll.

### Colour — and why the debit is not red

- **Credit is green (`--color-live`); debit is dim ink (`--color-ink-mute`).**
- ⚠️ **A debit chip is never red.** This is not a style preference — it is what keeps
  [`orbit-ui-overhaul.md`](orbit-ui-overhaul.md)'s *red = warning only* discipline alive. Red means
  `paused`, `no floor`, failed deposit. A red debit chip would fire on a **healthy running Orbit every
  single round** and burn that signal. The `−` sign and the colour carry the direction instead.
- The component owns the chip's colour and **no colour of the number itself** — that stays the call
  site's (`text-orbit` on holds, the pill's own ink in the header), the same rule
  `.orbit-statstrip__number` already follows.

### Where it animates

- **Four call sites, one implementation** — both header pills, `OrbitStatusBanner`'s `ORBIT HOLDS`
  and `OrbitStatStrip`'s. There is deliberately no second implementation to drift.
- **The formatter is injected.** Header pills pass `formatCompact` (they must fit two numbers in a
  54px bar at 360px) with a **`$` prefix**; Orbit's own readouts pass their existing 2dp `money()`
  with **no prefix** — they split the unit into a separate `USDC` tag, so a prefix would state the
  currency twice.
- **Header figures are 10px on mobile / 12px on desktop.**
- **`OrbitMoneyTab`'s cash-out amount, MAX figure and "won't cover a…" line do NOT animate** — they
  change because the player is typing, and animating a field being edited reads as lag, not feedback.
- `PER ROUND`, `WALLET BALANCE` and the stake cells are not animated either — they are rates and
  reference figures, not the player's moving money.

### Accessibility and performance

- **`prefers-reduced-motion` keeps the information and drops the movement**: the number snaps and the
  chip fades in and out **in place**. The durations are unchanged, so a win still dwells ~2.5s —
  reduced motion drops movement, never reading time.
- ⚠️ **The reduced-motion rule names BOTH chip modifiers.** `.money-value__chip--credit` sets its own
  `animation-name` at equal specificity; only being later in the file wins it back.
- ⚠️ **Phones get the full animation. Do NOT add `.money-value*` to `arcade.css`'s light-graphics
  `animation: none` strip list.** That list is for *infinite* animations that repaint forever for
  decoration; this is a one-shot that fires only when the player's own money moved. Every viewport
  below 1024px is permanently in light-graphics, so stripping it would mean **most players never see
  the feature**. The exception is written into the file beside `.animate-wobble`'s.
- The chip is `aria-hidden`. The authoritative number sits beside it, and announcing "+ 5.00" as a
  live region every round would be screen-reader noise for a value that is already read.

### The header pills

- ⚠️ **This narrowly reverses the 2026-08-04 "a DOOR, not a readout" call** (re-taken by the owner
  2026-08-07). The reversal is **scoped to the number**: a door that has a number to show became a
  pill that shows it. Each pill is **still one door** that opens the panel/menu, still carries **no
  `SIGN IN`** and **no text label** — colour and icon supply the context. It is **not** the pre-#141
  stacked `WALLET`/`ORBIT` readout; that layout stays deleted.
- **The `SPACE STROKER` wordmark is cut entirely below `lg`** (logo only). This is what pays for the
  second number: the bar is 54px below 1024px while `--ctl-h-md` *grows* to 44px there, so there is no
  spare width otherwise. It replaces the old hide-below-360px rule.
- **`.btn-door--pill` extends `.btn-door`, it does not fork it** — it overrides `width` and `padding`
  only. The shared rule exists precisely so the two header squares cannot drift apart.
- **Signed-out Orbit shows the word `ORBIT`, never `0`.** ⚠️ Gated on **`hasSession && orbit !== null`**,
  never on the balance — `useOrbit().balance` reads `0` when Orbit has simply not loaded, and a `0`
  for an unreadable balance is a lie on a money surface. A session with a **genuinely** zero balance
  shows `0` as a normal, true number. The pill keeps its width either way, so there is no reflow on
  sign-in.
- **Wallet disconnected → no wallet pill**; the `Connect Wallet` button and its `md:w-[200px]` reflow
  guard are untouched. **Embedded → no Orbit pill**, but the wallet pill remains and still animates.

### The wallet balance is now a shared singleton

- **`useTokenBalance` is a module-level singleton** that owns the balance, its poll and its history.
  This is **required, not tidying**: the delta chip needs **one place that knows old vs new**, which
  three independent fetches cannot provide.
- Before it, three components read `GET /solana/balance` independently (the header's `TokenBalance`,
  `OrbitModal`'s MAX, `OrbitMoneyTab`'s MAX) — and ⚠️ **`mainService.getTokenBalance` is one of the few
  GETs *not* behind `singleFlight`**, so they really were three undeduplicated requests. There is now
  **exactly one call site app-wide**.
- **The poll is refcounted.** `activate()`/`deactivate()` start it on `0 → 1` and clear it on `1 → 0`,
  which makes "only one poll in flight" structurally true rather than incidental. The connected wallet
  pill is the primary consumer.
- **`applyDebit`/`applyCredit` no-op on `amount <= 0`** — a free bet passes `0` and must stay silent.
- ⚠️ **`restartPoll()` after an optimistic apply is load-bearing, not housekeeping.** It pushes the
  next poll a full interval out so the reconcile lands *after* the on-chain debit has propagated.
  Losing it is how a stale read reverts the optimistic value.
- **The header owns the mint** (it is the only place that knows the selected game's token).
  ⚠️ Orbit's consumers read `orbit.mintAddress`, a **different source**: they use the shared balance
  **only when `currentMint() === orbit.mintAddress`**, otherwise `walletTokenBalance` stays `null` and
  MAX falls back to the deposit ceiling. Never render one token's balance under another's label.

### Orbit's balance, and the hazard that shapes it

- ⚠️⚠️ **The optimistic Orbit delta must NEVER be written into `orbit.value.balance`.** `useOrbit`'s
  auto-park watcher **PATCHes `enabled: false` in the database** when it believes Orbit cannot afford a
  round. Feeding it an optimistic guess would switch a player's Orbit off **in storage** before the
  server ever agreed, on a bet that might yet be refunded — a real, persisted state change caused by an
  animation.
- The design that prevents it: `orbit.value.balance` stays **server truth**; the guess lives in a
  separate `orbitOptimisticDelta`, and **only the display `balance` computed adds it**.
  `isPausedForFunds`, `needsFirstDeposit`, `roundsLeft`, `orbitStatus` and the auto-park watcher all
  keep reading `orbit.value.balance`. **Do not "simplify" `balance = (orbit.balance ?? 0) + delta`
  back into a plain read — that is the bug.**
- `orbitOptimisticDelta` resets to `0` on **every** successful `load()`/`refreshQuietly()` and in
  `reset()`. Server truth supersedes the guess, which is also what makes a silent correction silent.
- **Orbit's auto-bet debit is optimistic and frontend-only.** Orbit's stake leaving the balance
  **pushes no event and is not polled** — only wins push. When the live bet stream shows this round's
  own bet was Orbit-funded, the display balance is decremented by the **viewed** game's stake
  (`selectedGame`, never `activeGameType` — Orbit may be pointed elsewhere) and animated.
  ⚠️ **Once per round**, guarded on the round id: the bet list is re-delivered on reconnect, and
  without the guard a reconnect would double-charge the display figure and fire a second chip.
- **Accepted limitation:** it only fires while the player is **on that game's page**. Orbit betting on
  a game the player is not watching stays silent until the next `GET /orbit` — that read is unarmed,
  so the corrected value lands with no chip.
- **A win's amount comes from the READ, not from `win.amount`** — a threshold cash-out fires the
  instant the credit lands, so a win arriving alongside one nets out non-positive and publishes
  **nothing** rather than lying.
- **A deposit and a cash-out animate BOTH pills at once, in opposite directions** — a deposit *is*
  wallet → Orbit, so `−10.00` dim on the wallet and `+10.00` green on Orbit fire in the same
  synchronous block. This is designed behaviour; **do not "fix" it by serialising the two.**

### Deviations from the spec, and one instruction that could not be followed

- **Chip direction** (owner, at the mockup gate) — rises from below, never above; travel no longer
  carries direction. See the motion rules above.
- **Credit dwell** (owner, post-test) — 3400ms hold vs 900ms for a debit; the spec had one `CHIP_MS`.
- **Header type + `$` prefix** (owner, post-test) — 10px/12px with `$`, header-only.
- ⚠️ **Spec §7.2.5/§7.3.3 said to move `myBetThisRound`/`myBetThisRoundIsOrbit` to module scope for
  the watchers. That crashes the app at boot** — they derive from `useWallet()`, which throws
  `WalletNotInitializedError` until `main.ts` runs `initWallet()`, and module bodies execute at import
  time. The shipped code uses the file's existing **register-once flag** idiom instead
  (`moneyWiringDone` in `useGame`, `ensureWatchers()` in `useTokenBalance`). Behaviour is as specified;
  only the registration mechanism differs. **Register-once still matters** — several components call
  `useGame()`, and a per-consumer watcher would fire one duplicate `live` read per mounted consumer per
  round.
- The round-outcome watcher **cannot** live in `useTokenBalance`: it needs `gameInstance`, while
  `useGame` must import `useTokenBalance` for the bet debit — a **circular import**.

## Known gaps (shipped knowingly)

- A win collected in a **background tab** is not celebrated on return — the `visibilitychange`
  catch-up read is unattributed by construction.
- Orbit betting on a game the player is **not viewing** is silent (above).
- `monkify-front` has **no test runner at all**, so the bar for this work was `yarn lint` + `yarn
  build` clean plus a manual pass. No backend change ⇒ **no integration test** (`constraints.md` §8
  scopes that mandate to `monkify-backend`).
- Verified without a wallet: zero console errors, correct chip colours, reduced-motion swap, no
  light-graphics strip entry, and 360px **and** 320px fit with a worst-case `$1234.56` in both pills.
  The full money matrix (win, refund, deposit/cash-out pair, Orbit auto-bet) was walked by the owner
  on the local stack.

## Key code touchpoints

**`monkify-backend`** — ⛔ none. This repo is not touched. The only endpoint the feature reads is the
one already in use (`GET /solana/balance[/live]`), and its call volume went **down**.

**`monkify-front`**

- `src/components/ui/MoneyValue.vue` — **the** animated readout: roll, chip queue/coalesce, baseline
  detection, reduced motion, width reservation. Four call sites.
- `src/hooks/useTokenBalance.ts` — module singleton: balance, `change` signal, refcounted poll,
  `applyDebit`/`applyCredit`, armed `refresh`, mint ownership, register-once watchers.
- `src/types/money.ts` — `MoneyChange` / `MoneyChangeReason` and the value-then-change contract.
- `src/utils/money.ts` — `formatCompact`, lifted verbatim out of the deleted `TokenBalance.vue`.
- `src/hooks/useOrbit.ts` — `orbitChange`, `orbitOptimisticDelta` (⚠️ display-only), the display
  `balance` computed, `applyOrbitDebit`/`applyOrbitCredit`, attribution in `deposit()`/`cashOut()`,
  the armed `load({ attribute })`.
- `src/hooks/useOrbitNotifications.ts` — arms the `onWin` `refreshQuietly` with `'win'`.
- `src/hooks/useGame.ts` — `updateBalanceCallback` deleted in favour of a direct
  `useTokenBalance().applyDebit(...)` at bet time; the register-once round-outcome watchers and the
  Orbit auto-bet debit watcher.
- `src/components/GameHeader.vue` — wordmark `hidden lg:block`, the `TokenBalance` column wrapper
  removed, `setMint` wiring, the rewritten reversal comment.
- `src/components/header/WalletButtons.vue` — the connected `isDoor` branch is now
  `.btn-door--pill` + `MoneyValue`; owns the poll's lifetime; the token-name tooltip moved here.
  ⚠️ The disconnected branch is untouched.
- `src/components/orbit/OrbitLauncher.vue` — the violet pill; `showsBalance` gated on the session; the
  rewritten history comment recording both the 2026-08-04 call and its 2026-08-07 reversal.
- `src/components/orbit/OrbitStatusBanner.vue`, `OrbitStatStrip.vue` — `ORBIT HOLDS` through
  `MoneyValue`; both stay presentational/props-only, so `OrbitModal.vue` and `OrbitPage.vue` pass the
  `change` down.
- `src/components/orbit/OrbitModal.vue`, `src/components/orbit/tabs/OrbitMoneyTab.vue` — duplicate
  balance fetches deleted in favour of the shared composable **plus the mint guard**.
- `src/components/TokenBalance.vue` — **deleted**.
- `src/assets/styles/arcade.css` — `.btn-door--pill`, `.money-value*`, and the file's **first**
  `@keyframes` (`money-chip-rise`, `money-chip-hold`, `money-chip-fade`); the reduced-motion additions;
  the light-graphics **exception comment**.
- `.env.example` — comment only: `VUE_APP_BALANCE_POLL_INTERVAL_MS` now lives in `useTokenBalance`.
