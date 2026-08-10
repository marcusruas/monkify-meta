# Feature — Orbit feedback adjustments

> Status: **shipped.** Authoritative spec: [`../specs/orbit-feedback-adjustments.md`](../specs/orbit-feedback-adjustments.md)
> (merged via `feature/orbit-feedback-adjustments` on 2026-08-06). Trello #150
> ([fiKfaawz](https://trello.com/c/fiKfaawz)). Builds on
> [`orbit-ui-overhaul.md`](orbit-ui-overhaul.md) (#141), which shipped the `/orbit` page this work
> extends.
> ⚠️ **Partially superseded 2026-08-07** by [orbit-single-control](orbit-single-control.md):
> STOP/`Stopped`, the re-enable lock, deposits-during-cool-off and the claim lifetime all changed —
> see the inline notes below.

## What it does

Live testing of Orbit came back with six pieces of feedback sharing one theme: **the player had too
little control over what Orbit did with their money, and too little visibility into what it had
done.** Orbit picked their word for them, stopping it meant withdrawing everything and waiting 24h,
cashing out was all-or-nothing once a day, nothing told them off-screen that they had won or run dry,
and `/orbit` showed no history. All six are fixed here: an optional **reserved word**, a **STOPPED**
state, **partial cash-out**, a **per-player bet log**, **per-event Telegram toggles**, and deposits
accepted **during the disable cool-off**.

Still gated by `VUE_APP_ORBIT_ENABLED` — Orbit remains dark in production.

## Core rules

### The reserved word

- **Setting a word is optional.** With no word, Orbit still derives the choice from the round's
  committed `ServerSeed` (`OrbitChoiceGenerator.DeriveChoice`), exactly as before.
- On **free-text** types the word is a **standing claim on that game type**, held from the moment it
  is set until it is changed, cleared, or Orbit is disabled. It blocks **other Orbit players and human
  bettors** in every round — **including rounds Orbit does not bet** (STOPPED, out of funds,
  cool-off). A conditional claim could be lost mid-losing-streak, which would make pressing STOP
  dangerous. Accepted consequence: a human can be blocked from a word nothing is currently betting.
- **Preset types honour a fixed pick but never reserve it.** Reserving preset choices would cap a
  preset round at 8 bets and start refunding human bets on a game that has never rejected a duplicate.
- **Disabling releases the claim; re-enabling re-validates it** and is refused with a clear message if
  it was taken in the gap — never a silent restore. The word survives as a remembered preference.
  > ⚠️ **Claim lifetime narrowed 2026-08-07** ([orbit-single-control](orbit-single-control.md)): the
  > claim is held only while Orbit **can actually play** — `Enabled` **and** balance ≥ the enrolled
  > game's stake. A paused, dry or cashed-out-to-zero Orbit releases it (so "including rounds Orbit
  > does not bet" above no longer covers the out-of-funds case). The preference still survives
  > everything, and RUN re-attempts the claim through this same machinery.
- **Switching game type re-claims on the new type**, validated at switch time; the switch is refused if
  it fails, and the old claim is released only once the new one succeeds.
- ⚠️ **A blocked human sees the existing `ErrorMessages.ChoiceAlreadyTaken`** and the shipped refund
  path — byte-identical to a same-round collision. This is deliberate: **Orbit's invisibility to other
  players** is the #118 rule, and a distinct "reserved" message would announce that somebody on this
  type runs Orbit.
- ⚠️ **Money consequence, accepted:** a paid `SolanaToken` bet on a reserved word is rejected *after*
  the transfer lands, so it auto-refunds via `RefundInvalidBet`. Free and `Standard` bets are
  reject-only (pre-debit). This card increases how often that refund path runs.
- The word obeys human-choice rules — trimmed, lowercase, no spaces or symbols, exactly
  `choiceRequiredLength`. **Provable fairness is not broken:** only the *choice* stops being
  seed-derived, exactly as it already is for a human bet; `Bet.Seed` and the outcome keystream are
  untouched.

### STOP

> ⚠️ **Superseded 2026-08-07** ([orbit-single-control](orbit-single-control.md)): `Stopped` was
> merged INTO `Enabled` and the column dropped (`Enabled = Enabled AND NOT Stopped`). The free-pause
> semantics this section fought for are now what `Enabled = false` itself means — no forced
> cash-out, no cool-off, instant resume — and the armed-start-gate rule below carries over
> unchanged. The `STOPPED` state and copy are gone; surfaces show `OFF / PAUSED / RUNNING`.

- **STOPPED is a state separate from `Enabled`.** It makes the planner skip the player and **nothing
  else**: the balance stays put (**no forced cash-out, so no cool-off is owed**), the reserved word
  stays claimed, deposits and cash-outs still work, notifications still fire, and resuming is instant.
- Eligibility is now `Enabled` **and not STOPPED**, type matches, not self-excluded, funded, and no bet
  this round.
- **Naming is load-bearing:** the deliberate stop is `STOP` / `STOPPED`; the automatic
  `isPausedForFunds` state **keeps** the word "paused", so no shipped copy moved.
- A bet already in flight when STOP is pressed settles normally and can still fire a threshold or floor
  cash-out. **STOP never abandons an armed start gate** — a placement already dispatched behind
  `Game.ExpectedOrbitBets` must still land or decrement, or the round would wait on a bet that never
  comes. STOP takes effect from the next planning pass.

### Cash-out

- **Partial cash-out:** an amount floored at `MinimumCashOut` (1 USDC), ceiling the ledger balance,
  with a one-tap MAX. No-minimum was rejected — each cash-out is a real SPL transfer paid in SOL from
  the Orbit account's fee balance, so sub-minimum withdrawals can cost more than they move.
- **Both 24h rules became 1h.** The one-`Manual`-per-UTC-day cap is now a **rolling 1h gap** since the
  last Manual cash-out, and `ReEnableLockHours` went 24 → 1. They move together: the re-enable lock
  existed only to stop off/on/off walking around the daily cap.
  ⚠️ Stricter in one window, deliberately — 23:50 used to allow another at 00:00; now it waits to 00:50.
  > ⚠️ **The re-enable lock is gone entirely since 2026-08-07**
  > ([orbit-single-control](orbit-single-control.md)): `ReEnableLockHours` deleted, not shortened —
  > disable no longer withdraws, so the lock had nothing left to guard. The rolling manual gap is
  > the **only** cool-off in Orbit, and it closes the off/on/off loophole by itself (a full
  > cash-out is a `Manual` cash-out and consumes the gap).
- `Threshold`, `Floor`, `ForcedOnDisable` and `ForcedOnSelfExclusion` stay **exempt** from both the gap
  and the minimum, and still cash out the **whole** balance. A rate limit must never trap funds.
  > ⚠️ `ForcedOnDisable` has had **no writer since 2026-08-07** — disabling stopped cashing out; the
  > enum value survives only so historical ledger rows keep explaining themselves.
- The reserver still computes from the **ledger**, never the materialized column. A partial cash-out
  racing a bet debit fails cleanly on the atomic conditional debit rather than over-withdrawing.

### Telegram notifications

- **Three independent per-player toggles, Telegram only:** `Cashout` · `Zero out` · `Any bet's wins`.
- **Defaults: Cashout ON, Zero out ON, Any win OFF.** All-off would silently stop the cash-out messages
  linked players get today (money leaving custody with no notice); all-on would send an always-on
  player dozens of messages a day.
- ⚠️ This **deliberately reverses #123's** decision not to send Telegram for wins. Opt-in is what makes
  the reversal safe.
- **"Zero out" means the balance can no longer cover the type's stake.** It needed a server-side
  `OrbitEnrollment.ZeroOutNotifiedAt`, because the shipped out-of-funds state is derived **client-side**
  and de-duplicated in `localStorage`, which a Telegram sender in Consumers cannot read — without the
  flag the message would either never fire or fire every round.
- **The toggles govern Telegram only.** The in-app win, pause and redirect toasts keep firing as #123
  shipped them.
- The section stays **hidden when Telegram is not configured or not linked**, and the `chat_id` is
  still never returned to the client. **Telegram is never allowed to fail a money path.**

### The bet log

- **`GET /orbit/history`** — session-gated, server-side paged, newest first, `pageSize` capped at 30,
  and **not Redis-cached** (one player's private money history, unlike the public stats endpoints).
  ⚠️ The route is `/orbit/history`, **not** the spec's `/orbit/bets`: `RateLimitSettingsTests` guards
  the money bet path with a blunt substring check on `/bets`, and weakening that guard for a read-only
  page was the wrong trade, so the route moved instead.
- Shows **bets and their results only** — timestamp, game type, choice, stake, outcome (won / lost /
  refunded) and payout.
- ⚠️ **Rows pair on `BetId`, not `GameId`.** `OrbitTransaction.GameId` is set on `BetDebit` rows only —
  it *is* the placement claim — while `WinCredit` and `Refund` carry `BetId`.
- ⚠️ **A `BetDebit` with a null `BetId` is excluded.** The debit is written before the transfer is
  signed and `BetId` is filled only once the bet lands, so a failed placement leaves one behind — that
  was never a bet and must not be rendered as one.
- **Skipped rounds are not logged.** Persisting the planner's skip decisions would mean new writes on
  the hot placement path; "why isn't Orbit betting" is already answered by the status banner.

### Deposits during the cool-off

> ⚠️ **Superseded 2026-08-07** ([orbit-single-control](orbit-single-control.md)): there is no
> cool-off left to except, and even the enable-first rule is gone — deposits are gated on the
> **enrolment existing** (the game pick creates the row), so a parked Orbit takes deposits and
> waits for RUN. A deposit never starts play; only RUN sets `Enabled`.

- **Only the cool-off refusal was dropped; the enable-first rule stays.** A player mid-cool-off intends
  to come back, and with the lock at 1h the money is playable within the hour.
- ⚠️ **This needed an explicit exception, not a deletion.** Removing the cool-off refusal alone was a
  no-op: disabling sets `Enabled = false` *before* writing the row the cool-off keys on, so being in
  the cool-off always implies being disabled, and the very next check (enable-first) refused the same
  deposit with a different message. The cool-off is now a **named exception to enable-first**.
- Deposits stay idempotent by signature; the client stash and replay are unchanged.

## Shipped deviations from the spec (deliberate)

1. **The endpoint is `GET /orbit/history`**, not `/orbit/bets` — see above.
2. **The `useGame.bet()` pre-pay guard was NOT built** (spec §7.5). It cannot exist without an "is this
   word reserved?" lookup, which **is** the Orbit-membership probe the #118 invisibility rule forbids —
   it would tell any stranger that someone runs Orbit on that type. The shipped per-round guard only
   works because taken choices are already public in the bets list; a cross-round claim is not.
   Protection is the backend refusal plus the automatic refund.
3. **Deposits during cool-off** became a named exception rather than a deletion (above).

## A money-path defect fixed on the way (predates this card)

⚠️ **`OrbitLedgerService.ResolveBalance` could corrupt a correct balance.** It summed the ledger and
read the materialized `OrbitBalance` column in **two separate statements**. Every writer commits its
ledger row and its column update in one transaction, so the pair is never genuinely inconsistent — but
under READ COMMITTED each read takes its own snapshot, so the two could straddle a commit and see a
debit in one but not the other. The self-healing routine then "corrected" the column to a mid-flight
total and the real update applied on top, leaving the player **short by exactly one stake**.

It surfaced now because this card's zero-out evaluation calls `ResolveBalance` for **every** enabled
Orbit player at **every** settlement; before it, the only settlement-time caller returned early unless
a cash-out floor was set. **Fix:** the two reads share a **REPEATABLE READ** snapshot, and the
correction runs outside it **guarded on the column still holding the value that was read** — a column
that moved underneath is left alone instead of overwritten.

> ⚠️ **Since 2026-08-10 the stop rules no longer call `ResolveBalance` at all**
> ([`specs/orbit-stop-loss-trigger.md`](../specs/orbit-stop-loss-trigger.md)) — they call
> **`ResolveSettledBalance`**, which wraps it and adds back the stakes of rounds still in play. The
> snapshot and the guarded self-heal described above are untouched and still do the work; the settled
> read reuses `ResolveBalance` precisely so that logic is not duplicated. The raw read the floor and
> zero-out used was one stake short at settlement, because the successor round's stake is signed
> before the previous round settles.

⚠️ **`ZeroOutNotifiedAt` writes with `ExecuteUpdateAsync`.** A load-and-save called `SaveChangesAsync`
on a context holding a **tracked-but-stale** `OrbitBalance`, flushing it back over the debit — a second,
distinct hazard. This is the repo-wide NoTracking/`ExecuteUpdate` trap, and it applies here.

## Key code touchpoints

**`monkify-backend`**

- Entities — `SpaceStroker.Domain/Orbit/Entities/OrbitChoiceClaim.cs` (**new**; the row's existence *is*
  the claim, unique on `(GameParametersId, Choice)`); `OrbitEnrollment.cs` gains `Choice`, `Stopped`,
  `NotifyOnCashOut` / `NotifyOnZeroOut` / `NotifyOnWin` and `ZeroOutNotifiedAt`.
- Migration — `SpaceStroker.Infrastructure/Migrations/20260805154055_AddOrbitFeedbackAdjustments.cs`
  (additive, no backfill: every new column is nullable or defaulted, so existing enrolments arrive with
  no word, not stopped, notifications on). Applied by `SpaceStroker.Consumers` only.
- Bet path — `Handlers/Games/RegisterBet/RegisterBetHandler.cs` reads the claim on **every free-text
  bet**, so it is a cheap indexed read on the money path.
- Orbit services — `OrbitBetPlacementService`, `OrbitChoiceGenerator`, `OrbitCashOutReserver`,
  `OrbitReEnableLock`, `OrbitBetSettlementService`, `OrbitLedgerService` (the REPEATABLE READ fix).
- Handlers — `CreditOrbitDepositHandler` (cool-off exception), `UpdateOrbitSettingsHandler` (word, STOP,
  flags), `GetOrbitHandler`; `SpaceStroker.Api/Controllers/OrbitController.cs` → `[HttpGet("history")]`
  + its `[HttpOptions]` and rate-limit rule.
- Consumers — `OrbitCashOutConsumer`, `OrbitNotificationConsumer`.
- Tests — `OrbitCashOutReserverTests`, word-reservation cases in `RegisterBetHandlerTests`, zero-out
  cases in `OrbitBetSettlementServiceTests`, `RateLimitSettingsTests` (exact-set + limit rows for
  `*:/orbit/history`), and `SpaceStroker.Tests.Integration/OrbitFeedbackFlowTests.cs` — including
  `ASettledOrbitRound_LeavesTheBalanceAndTheLedgerAgreeing`, which reads the **raw column** rather than
  going through the ledger service, because that service self-heals on read and would hide exactly what
  the test looks for.

**`monkify-front`**

- `src/hooks/useOrbit.ts` — word set/change/clear + validation, `stopped`, the three notify flags,
  amount-aware cash-out, the paged log.
- `src/pages/OrbitPage.vue` (as restructured by #141) — the word step with per-letter cells, the STOP
  control, partial cash-out, the notify toggles in the Telegram block, and the log table below the main
  card. `src/components/orbit/OrbitModal.vue` surfaces STOP.
- `src/services/orbitService.ts`, `src/types/orbit.ts` — the `history` endpoint and the new fields.
