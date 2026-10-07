# Feature — Game-mode UX: round rollover, state reset and the paid figure

> Status: **shipped.** Authoritative spec: `../specs/game-mode-ux.md`
> (merged via `feature/game-mode-ux` on 2026-08-24).

## What it does

Makes a **second** round on `/game/play` playable. The front used to sit on the finished result for
**30 s** while the backend opened the successor immediately and started it ~25 s later — so the player
arrived ~5 s *into* a running race, and on a table that reaches its 2-player minimum by itself (house
seed bet + one Orbit bet) they could never place a bet on a rolling round at all. The hold is now
**9 s measured from `Ended`**, it is driven by a wall-clock deadline rather than a timer (so a
backgrounded tab cannot be stranded), the per-round state that leaked across rounds is cleared, and
the terminal now states **what a winner was actually paid**.

## Core rules

### Rollover

- A round's result stays on screen for **`RESULT_HOLD_MS` (9 000 ms), measured from `Ended`**, then the
  front switches to the type's currently-active round. The value comes from
  `VUE_APP_NEW_GAME_DELAY`, defaulting to 9 000 in code.
- **The hold is also the cap.** At the deadline the front rolls over **whether or not the payout
  finished**. Accepted consequence: on a slow payout the player may never see `> REWARDS SENT` — the
  balance chip and the win toast are what confirm the money.
- Reaching a terminal payout state (`RewardForWinnersCompleted` or `ErrorWhenProcessingRewards`)
  **early does not roll over early**; the player still gets the full hold to read the result.
- The rollover is a **wall-clock deadline** re-evaluated from three sources: every status change, a
  500 ms ticker, and `visibilitychange`. Chrome throttles and freezes timers in a hidden tab, so a bare
  `setTimeout` is what used to leave a backgrounded tab on a dead view until reload.
- **There is no `nextGame` precondition.** The rollover fetches the type's currently-active round;
  `RunGameConsumer` creates the successor *before* it publishes `RewardGameEvent`, so the fetch cannot
  race an unopened round. Dropping the gate is what fixes a missed `GameReceived` broadcast without an
  unbounded retry loop.
- **One path for every game type, lottery included** — no mode branch. On Space Jackpot the only effect
  is landing on the next draw's countdown sooner.
- The **manual lever** runs the same rollover, and its gate is **either** terminal payout state — a
  player on an errored round can still pull themselves out of it.
- Practice mode reads the **same constant**, so `/game/practice` holds for 9 s too.
- ⚠️ **Exception (#200): Orbit solo auto-play holds the result indefinitely.** While Orbit auto-plays the
  Hyper Roll or Monkey Duel type on screen (`orbitAutoPlaysSoloGame`), the deadline does not roll over: a
  rollover would re-read the round and wipe its result. The next `OrbitAutoRound` push (`attachRound`)
  clears it, and pausing Orbit releases it. Shared races (Cosmic Sprint) never get that push, so they roll
  over as always. See [`auto-bet-duel-hyper-roll.md`](auto-bet-duel-hyper-roll.md).
- **Every mode's typer highlights the winning word** at the end of the stream (`GameTyper`,
  `.winning-hit`, the practice typer's yellow `#fde047`), since 2026-10-06. The backend ends a round the
  instant the last N characters are a placed choice, so the tail is split off and lit when it matches
  `winnerChoice`.

### Per-round state

- `winnerChoice`, `winners`, `finishedTyping` and the net-reward figure are **cleared on rollover**.
  `finishedTyping` was previously set `true` and never set back anywhere in the repo, which also meant
  that from round 2 onward the result block rendered at `Ended` instead of at
  `RewardForWinnersCompleted` — round 1 and rounds 2+ behaved differently.
- ⚠️ **`currentPot` is deliberately NOT reset** on rollover. `getBets()` runs first and is its single
  source of truth (it has already seeded `lotteryBase + Σ bets`); zeroing it afterwards is the shipped
  `POT: 0` regression on Space Jackpot.
- Because `resumeFromIdle()` calls the same path, a tab resumed from the idle overlay never shows the
  previous round's winner or PAID line.
- Edson's end-of-round animation chain (finale → regen → idle, 4.45 s) is **cancellable** and is
  cancelled on status change and on unmount. At the old 30 s hold it always finished in time; at 9 s an
  uncancelled chain would drop a stale frame onto a race already running.
- `GameMonkey` has a defined branch for all five previously-unhandled statuses. The two reward states
  deliberately **leave a running finale alone** (it lands on idle by itself); the three terminal error
  states rest on idle.
- A pending `gradualSpeedReset()` step-down is **stamped with its round** and dropped if that round is
  gone, so a late-evaluating deadline cannot step the typing speed down into a race already running.

### The paid figure

- The reward-completed status event carries **`netRewardPerWinner`**: the NET amount a single **paid**
  winner received after the house cut — exactly what moved on-chain (or into Orbit custody).
- It is **captured from the payout loop**, never recomputed. `CalculateRewardForBet` subtracts prior
  `Credit` rows for idempotency, so it is not a pure function of the round and a later call would
  return a different number.
- It is **uniform across paid winners by construction** (same `RequiredAmount` stake, same frozen
  `PayableWinnerCount` divisor, same `houseFundedShare`), which is the only reason a single figure can
  be broadcast to the whole game group without leaking anything about an individual. That invariant is
  pinned by a unit test.
- It is **null whenever nothing was paid**: a free-bet-only round, a round nobody won, a pre-cutover
  round, or `ErrorWhenProcessingRewards`. Null is omitted from the JSON, so a client built before this
  change sees exactly the previous payload shape.
- ⚠️ **An errored round publishes null even when one winner really was paid.** Deliberate: the money
  moved and is in the ledger, but the round does not make a payout *claim* it cannot make for every
  winner.
- Before this feature the reward-completed event carried **`result: null` entirely** — `GameResult` was
  only ever built when a typer was supplied, and the reward path has none. It is now also built on the
  reward path from persisted state (`Game.WinningChoice` + the loaded `Bets`), using the **same**
  `GetWalletAddress()` + `Distinct()` derivation as the typer path.

### What the player sees

- A **`> PAID:`** line under `> PAID TO:`, shown to everyone watching the round. **Omitted entirely**
  when nothing was paid — it never prints `0` or `0.0000`.
- A **win toast for wallet-funded winners**, which did not exist before (the app's only win toast was
  Orbit's). It shows the same figure as the PAID line. Gated on the **loaded bets** — my bet, the
  winning choice, `amount > 0`, `!viaOrbit` — so a player who refreshes mid-round and then wins is
  still told, and the Orbit and manual toasts can never both fire for one bet.
- `> YOU WON! CONGRATS!` renders in the existing near-miss amber (`#fde047`); no other result line
  changes colour.
- **Mobile only** (below `lg`), the SERVER SEED block moves **above** `> COMPLETE!`, so the terminal's
  auto-scroll to `scrollHeight` lands on the result instead of past it. Desktop order is unchanged and
  the DOM is byte-identical — it is a `v-if`/`v-else` pair on `props.mobile`, so exactly one seed node
  exists at a time.
- ⚠️ `ErrorWhenProcessingRewards` (status 8) falls through the result block's gate to the pre-game
  branch, which prints the existing `> ERROR SENDING REWARDS. CONTACT SUPPORT.` copy. "No PAID line, no
  toast on an errored round" is therefore **structurally** true — do not add a branch that breaks it.

### Money display precision

Three precisions, deliberately, all in `utils/money.ts`:

| Surface | Places | Formatter |
| --- | --- | --- |
| Pot, lobby card price, Orbit panel, everything else | **2** (`DP`) | `formatSol` |
| Header balance pills | **max 3** | `formatCompact` |
| The `> PAID:` line and both win toasts | **max 4** | `formatPayout` |

- All three **truncate, never round**, and all three retain a significant-digit escape so a real but
  tiny figure never renders as a row of zeros. **The escape floor must match the places** — `formatSol`
  escapes below `0.01`, which is exactly why a `0.0296` payout used to print `0.02`.
- **Global `DP` stays 2.** Raising it app-wide is what once put `0.5656` in the header pill.
- Header is **max 3, not padded to 3**: `0.15` stays `0.15`, `0.155` renders `0.155`.
- `formatCompact`'s only callers **are** the header pills (`WalletButtons` ×2 + `OrbitLauncher`), which
  is what makes its 3 places genuinely header-only. It keeps its `K`/`M`/`B` fold above a thousand,
  which exists for width: two pills share a 54 px bar at 360 px.

## Key code touchpoints

**`monkify-backend`**

- `src/SpaceStroker.Infrastructure/Contracts/Games/GameStatusUpdatedEvent.cs` — `GameResult` carries
  `decimal? NetRewardPerWinner`; wire name `netRewardPerWinner`, omitted when null.
- `src/SpaceStroker.Infrastructure/Services/Games/GameService.cs` — `UpdateGameStatus` builds a
  `GameResult` on the **reward** path (second arm beside the typer arm) from persisted state.
- `src/SpaceStroker.Infrastructure/Services/Games/IGameService.cs` — the `netRewardPerWinner` parameter.
- `src/SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` — captures
  `netRewardPerWinner ??= rewardResult.Value` on the first successful **paid** credit; passes it only
  when the final status is `RewardForWinnersCompleted`.
- `src/SpaceStroker.Domain/Bets/Services/BetDomainService.cs` — unchanged; it is the source of the
  captured figure and of the uniformity invariant.

**`monkify-front`**

- `src/constants/gameTiming.ts` — `RESULT_HOLD_MS`, the single owner of the hold (default 9 000).
- `src/hooks/useGame.ts` — the rollover engine (module-scope, register-once, deadline + ticker +
  `visibilitychange`), the `updateNewGame()` reset block, `netRewardPerWinner`, `myWinningWalletBet` /
  `maybeFireWinToast`, the `speedResetGameId` guard, and the typed `onStatus`.
- `src/components/game/GameConsole.vue` — lever animation only; watches `rolloverCount`, delegates to
  `triggerRollover`, gates on `canRollOver`.
- `src/components/game/GameMonkey.vue` — cancellable `Ended` chain + the five status branches.
- `src/components/game/GameTerminal.vue` — the `> PAID:` line, `.result-win` amber, the mobile-only seed
  reorder.
- `src/utils/money.ts` — `formatPayout` (4dp), `formatCompact` (3dp, header-only), shared
  `truncatedWithEscape`; `DP` unchanged at 2.
- `src/hooks/useOrbitNotifications.ts` — the Orbit win toast uses `formatPayout`.
- `src/types/index.ts` — `GameResultPayload`, `GameStatusEvent`, and `GameStatus.GameEndedAbruptely`
  (value 9, which the backend has always sent but the enum never modelled).

**`monkify-meta`**

- `deploy/secrets.example.json` → `frontBuild.VUE_APP_NEW_GAME_DELAY` — makes the hold reachable in a
  production build for the first time.

## Gotchas worth knowing

- ⚠️ **The env knob was dead in production until this feature.** `monkify-front` tracks only
  `.env.example` (`.env.production`, `.env.local` and `.env.*.local` are git-ignored), and the deploy
  tool's `frontBuild` block never carried the key under either name — so every production round ran the
  hardcoded fallback. **The code default is the load-bearing fix**, not the config key.
- ⚠️ `GameConsole` can be mounted more than once (desktop via `GameTerminal`; mobile via `GamePage`, in
  two breakpoint-gated places). Any rollover timer living there makes the rollover's **cardinality a
  property of the layout** — which is why it moved to `useGame` module scope behind a register-once flag.
- ⚠️ `useIsMobile` is `matchMedia('(max-width: 1023px)')` — exactly the `lg` boundary — and `GamePage`
  switches `<game-terminal mobile>` on it, so `props.mobile` **is** the `< lg` switch. A `lg:` utility
  duplication of the seed block is not needed and was rejected.
- ⚠️ **`statusData.serverSeedHash` is dead on the wire.** `useGame.onStatus` reads it, but
  `GameStatusUpdatedEvent` has no such member and both publishers send that record — the read always
  falls back to the loaded instance, so nothing is broken. Typed optional; **not** fixed here.
- ⚠️ `GeneralSettings.Games.DelayBetweenGames` (= 20) is referenced nowhere. Recorded, not removed.
- ⚠️ Verify the mobile ended screen at a **true 360 px via CDP device emulation** — `resize_page` stops
  at ~500 px, where these blocks are a line shorter and the collision does not reproduce.
