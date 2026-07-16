# Feature — Game Pot Display

> Status: **shipped.** Authoritative spec: `../specs/game-pot-display.md`
> (merged via `feature/game-pot-display` on 2026-07-07).

## What it does

The game terminal shows a `> POT:` line — the amount winners split — for **every** game type
(free-type race, preset race, and lottery), updating in realtime as bets arrive. Previously only
lottery games displayed a pot. Player-facing and **frontend-only**: the pot is derived client-side
from data the client already receives, with no backend, DB, SignalR-contract, or settlement change.

## Core rules

- The pot is shown for **all three modes**. For **races** it equals the sum of placed bet amounts
  (`Σ bets[].amount`); for **lottery** it equals `lotteryParameters.lotteryPot` (base)
  `+ game.carriedExtra` (the jackpot stacked from prior un-won rounds, surfaced on the
  active-game DTO) `+ Σ bets`. See `features/lottery-pot-stacking.md`.
- **Seed on load:** when a game loads (`getBets`), the pot is set to `Σ bets` plus the lottery base
  only when the game is a lottery.
- **Realtime:** each new bet on the `RecentBets` `/Bets` SignalR stream increments the pot, for every
  game type (the previous lottery-only gate is removed).
- **Free bets** (`amount = 0`) contribute nothing, so a dev free-bet race pot reads `0` while a
  lottery still shows its base pot.
- **Always visible while a game is loaded:** the pot renders in the pre-run/waiting view **and** in
  the running/ended view, so it stays on screen through betting, the run (`InProgress`), and after the
  game ends (`Ended`), showing the final accumulated total.
- **Rollover:** on game rollover (`updateNewGame`) the pot resets to `0` and is reseeded from
  the next game's bets (base pot for lottery).
- **Currency/format:** rendered via the existing `formatAmount` helper — `USDC` for crypto operators,
  the player's preferred display currency for regulated operators (identical to the `> BET:` line).
  Settlement stays USD; this is display-only.
- Settlement/payout math is untouched — `BetDomainService.SetPotAmount` remains the source of truth
  for actual winnings; this feature only mirrors the pot for display.

## Key code touchpoints

- `monkify-front/src/hooks/useGame.ts` — `currentPot` ref (was `currentLotteryPot`); seeded in
  `getBets`, accumulated on the `/Bets` handler, reset in `updateNewGame`, exposed as a computed.
- `monkify-front/src/components/game/GameTerminal.vue` — universal `> POT:` line in the pre-run view
  (lottery block keeps only `> FREQUENCY:`) and a second `> POT:` line in the running/ended view,
  both via `formatAmount(currentPot)`.
