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
  (`Σ bets[].amount`); for **lottery** it equals the base pot `+ game.carriedExtra` (the jackpot
  stacked from prior un-won rounds) `+ Σ bets`. See `features/lottery-pot-stacking.md`.
- **Seed on load — from the game-instance DTO (Trello #88, 2026-07-24):** when a game loads
  (`getBets`), the pot is `gameData.isLottery ? (gameData.lotteryPot ?? 0) + (gameData.carriedExtra ?? 0) : 0`
  `+ Σ bets`. The lottery base now comes from **`ActiveGameDto.LotteryPot`** on the instance itself
  (added for this fix), not the separately-fetched game-type `selectedGame.lotteryParameters` — so the
  base can't be silently dropped when the two fetches are out of sync.
- **Realtime:** each new bet on the `{gameId}/Bets` SignalR stream increments the pot, for every
  game type (the previous lottery-only gate is removed). That stream moved from the removed
  `RecentBets` hub onto the single `Games` hub in `unified-websocket-connection`; the event name and
  payload are unchanged.
- **Free bets** (`amount = 0`) contribute nothing, so a dev free-bet race pot reads `0` while a
  lottery still shows its base pot.
- **Always visible while a game is loaded:** the pot renders in the pre-run/waiting view **and** in
  the running/ended view, so it stays on screen through betting, the run (`InProgress`), and after the
  game ends (`Ended`), showing the final accumulated total.
- **Rollover / initial load:** `updateNewGame` (the initial-load path *and* rollover) calls `getBets`,
  which is the **single source of truth** for `currentPot`. It does **not** separately zero the pot —
  a prior `currentPot.value = 0` that ran *after* `getBets` clobbered the seeded lottery base to `0` on
  every load/rollover (the "`POT: 0` on a fresh Space Jackpot" bug, Trello #88); it was removed.
- **Currency/format:** rendered via the existing `formatAmount` helper — `USDC` for crypto operators,
  the player's preferred display currency for regulated operators (identical to the `> BET:` line).
  Settlement stays USD; this is display-only.
- Settlement/payout math is untouched — `BetDomainService.SetPotAmount` remains the source of truth
  for actual winnings; this feature only mirrors the pot for display.

## Key code touchpoints

- `monkify-front/src/hooks/useGame.ts` — `currentPot` ref (was `currentLotteryPot`); seeded in
  `getBets` from the instance DTO (`gameData.lotteryPot` / `carriedExtra`), accumulated on the `/Bets`
  handler, exposed as a computed. `updateNewGame` no longer zeroes it (getBets owns it).
- `monkify-front/src/types/index.ts` — `GameInstance.lotteryPot?: number` (mirrors `ActiveGameDto.LotteryPot`).
- `monkify-backend/src/SpaceStroker.Infrastructure/Contracts/Games/ActiveGameDto.cs` — `LotteryPot`
  (config base pot on the instance; `0` for non-lottery). Query already includes `LotteryParameters`.
- `monkify-front/src/components/game/GameTerminal.vue` — universal `> POT:` line in the pre-run view
  and a second `> POT:` in the running/ended view, both via `formatAmount(currentPot)`. Also
  `formatLotteryFrequency` parses the day component of the .NET TimeSpan (`"1.00:00:00"` → "24 HOURS",
  not "1 HOUR"; Trello #88).
