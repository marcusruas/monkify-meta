# Feature — Crypto commission (house cut on SolanaToken winnings)

> Status: **shipped.** Authoritative spec: `../specs/crypto-commission.md`
> (merged via `feature/crypto-commission` on 2026-07-17).

## What it does

For **`SolanaToken` (crypto) game types only**, ChimpOut retains a small **house commission** on
winnings instead of paying out the whole pot. The cut is taken only from each winner's **profit above
their own stake** (a winner always gets their stake back), defaults to **0.5%**, and is realized by
crediting winners less on-chain — the retained amount simply stays in the operator's token account.
**Standard/USD games are unchanged** (winners still split 100% of the pot). The rate is shown to
players as a **house-cut badge** in the `/game` STATS side panel. This re-adds a commission that was
removed in the operator-wallet-api refactor (see `operator-wallet-api.md`).

## Core rules

- **Scope: SolanaToken only.** The commission is applied in `BetDomainService.CalculateRewardForBet`
  only when the game's `PaymentMethod == SolanaToken`. Standard games never apply it (full pot to
  winners, 0% cut).
- **What is taxed = player-funded profit above the winner's own stake.** Per winner:
  `commission = rate × max(0, winnerReward − ownStake − houseFundedShare)`, then
  `netReward = winnerReward − commission`.
  - A winner **always keeps their full stake** — the cut comes only from profit above it.
  - The **house-funded lottery money is never taxed**: `houseFundedShare = (LotteryParameters.LotteryPot
    + Game.CarriedExtra) / payableWinnerCount` is subtracted before applying the rate, so only
    player-stake-funded winnings are commissioned. For races this term is 0.
  - Because every bet in a game shares one fixed `RequiredAmount`, the total retained equals
    **rate × (losing-bet stakes)**. For a 3-token / 2-winner round at 0.5% each winner nets **1.4975**
    (retained 0.005 = 0.5% of the one losing stake).
- **Configurable rate, default 0.5%.** `GeneralSettings.Token.CommissionPercentage` is a **percent**
  (`0.5` = 0.5%); the domain converts it to a fraction (`/100`). `0` (or absent) disables the
  commission. This restores the location of the old, removed `TokenSettings.CommisionPercentage`
  (correctly spelled).
- **Retained funds stay put.** The cut is realized by a smaller on-chain credit — **no new transfer,
  no dedicated ledger / `CryptoTransactionLog` row, no migration.** "Total paid out" stats drop
  accordingly.
- **Rounding.** The net reward still rounds **down** to the token's decimals (`MidpointRounding.ToZero`),
  applied after the commission, so the sum of payouts never exceeds the pot held on-chain.
- **Free bets** (amount 0) contribute nothing to the pot and receive no payout → no commission.
- **Refunds are unaffected** — a refund always returns the full stake (commission applies to rewards
  only).
- **UI (STATS badge only).** `GameStatsPanel.vue`'s house-cut badge is method-aware, rendering the
  backend-provided `ActiveGame.commissionPercentage`: SolanaToken → `"0.5% HOUSE CUT"` (with a
  "winners keep the pot minus a 0.5% house cut" blurb); Standard → `"0% HOUSE CUT"` (winners split
  100%). No terminal line, no lobby-card change.

### Reward-division fix shipped alongside (pre-existing bug)

The commission surfaced a latent multi-winner payout bug in the reward path, fixed in the same branch:

- `BetDomainService.Winners` was a **deferred** query (`game.Bets.Where(Status == NeedsRewarding)`),
  and `RewardGameConsumer` flips each winner's `Status` to `Rewarded` as it pays them — so
  `Winners.Count()` **shrank mid-payout** and later winners divided the pot by a smaller count (a
  3-token / 2-winner round paid the second winner the **whole pot**).
- Fix: the winner set is **materialized once** (`ToList()`), and the pot is divided among **paid
  winners only** (`PayableWinnerCount = Winners.Count(w => !IsFreeBet(w))`, frozen, guarded ≥ 1). Paid-
  winner division also makes the free+paid rule explicit — a free winner takes no share, so the paid
  winner keeps the whole pot (previously achieved only by the drift). Both the reward divisor and the
  commission's `houseFundedShare` use `PayableWinnerCount`.

## Key code touchpoints

**Backend (`monkify-backend`)**

- Reward math: `ChimpOut.Domain/Bets/Services/BetDomainService.cs` — ctor captures `_isSolanaToken`,
  `_houseFundedBase`, `_commissionRate`, `_payableWinnerCount` (+ `PayableWinnerCount`); `CommissionForWinner`
  applies the cut in `CalculateRewardForBet`; `Winners` is materialized.
- Config: `ChimpOut.Domain/Configs/Entities/TokenSettings.cs` (`CommissionPercentage`);
  `ChimpOut.Common/chimpoutsettings.json` → `GeneralSettings.Token.CommissionPercentage` (mock `0.5`).
- DTO: `ChimpOut.Infrastructure/Contracts/Games/GameParametersDto.cs` (`CommissionPercentage`, SolanaToken
  → rate else 0); populated in
  `ChimpOut.Infrastructure/Handlers/Games/GetActiveParameters/GetActiveParametersHandler.cs`
  (injects `GeneralSettings`).
- Settlement (unchanged wiring): `ChimpOut.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`
  credits `rewardResult.Value` via the payment strategy.
- Tests: `ChimpOut.Tests/UnitTests/Domain/Games/BetDomainServiceTests.cs` (commission + sequential-
  payout regression), `ChimpOut.Tests/UnitTests/Handlers/GetActiveParametersHandlerTests.cs` (rate on
  SolanaToken only), `ChimpOut.Tests.Integration/GameFlowTests.cs`
  (`PaidCryptoRace_AppliesCommission_CreditsPotMinusCut`, + Standard full-pot assertion).

**Frontend (`monkify-front`)**

- `src/types/index.ts` (`ActiveGame.commissionPercentage`).
- `src/components/game/GameStatsPanel.vue` (method-aware house-cut badge label + blurb).
