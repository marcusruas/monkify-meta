# Feature — Session Pot Display

> Status: **shipped.** Authoritative spec: `../specs/session-pot-display.md`
> (merged via `feature/session-pot-display` on 2026-07-07).

## What it does

The game terminal shows a `> POT:` line — the amount winners split — for **every** session type
(free-type race, preset race, and lottery), updating in realtime as bets arrive. Previously only
lottery sessions displayed a pot. Player-facing and **frontend-only**: the pot is derived client-side
from data the client already receives, with no backend, DB, SignalR-contract, or settlement change.

## Core rules

- The pot is shown for **all three modes**. For **races** it equals the sum of placed bet amounts
  (`Σ bets[].amount`); for **lottery** it equals `lotteryParameters.lotteryPot` (base)
  `+ session.carriedExtra` (the jackpot stacked from prior un-won rounds, surfaced on the
  active-session DTO) `+ Σ bets`. See `features/lottery-pot-stacking.md`.
- **Seed on load:** when a session loads (`getBets`), the pot is set to `Σ bets` plus the lottery base
  only when the session is a lottery.
- **Realtime:** each new bet on the `RecentBets` `/Bets` SignalR stream increments the pot, for every
  session type (the previous lottery-only gate is removed).
- **Free bets** (`amount = 0`) contribute nothing, so a dev free-bet race pot reads `0` while a
  lottery still shows its base pot.
- **Always visible while a session is loaded:** the pot renders in the pre-run/waiting view **and** in
  the running/ended view, so it stays on screen through betting, the run (`InProgress`), and after the
  session ends (`Ended`), showing the final accumulated total.
- **Rollover:** on session rollover (`updateNewSession`) the pot resets to `0` and is reseeded from
  the next session's bets (base pot for lottery).
- **Currency/format:** rendered via the existing `formatAmount` helper — `USDC` for crypto operators,
  the player's preferred display currency for regulated operators (identical to the `> BET:` line).
  Settlement stays USD; this is display-only.
- Settlement/payout math is untouched — `BetDomainService.SetPotAmount` remains the source of truth
  for actual winnings; this feature only mirrors the pot for display.

## Key code touchpoints

- `monkify-front/src/hooks/useSession.ts` — `currentPot` ref (was `currentLotteryPot`); seeded in
  `getBets`, accumulated on the `/Bets` handler, reset in `updateNewSession`, exposed as a computed.
- `monkify-front/src/components/game/GameTerminal.vue` — universal `> POT:` line in the pre-run view
  (lottery block keeps only `> FREQUENCY:`) and a second `> POT:` line in the running/ended view,
  both via `formatAmount(currentPot)`.
