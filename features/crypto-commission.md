# Feature — House commission (cut on winnings)

> ⚠️ **Scope widened by `features/remove-operators.md` (2026-07-30).** The commission was
> `SolanaToken`-only because `Standard` (off-chain USD) games existed and took no cut. `Standard` is
> gone — every game settles in the one configured token — so the commission now applies to **every**
> game type, and the `PaymentMethod` branch that gated it is deleted. The rate moved from
> `GeneralSettings.Token.CommissionPercentage` to
> **`GeneralSettings.Operator.Token.CommissionPercentage`**. The math below is otherwise unchanged.

> ⚠️ **Rate raised 0.5% → 1% by `specs/commission-rate-1-percent.md` (2026-08-04).** Only the number
> changed: the base is still profit above each winner's own stake with the house-funded lottery money
> excluded, and there was no code, entity or migration change. Every number below is the **current**
> 1% rate; the 0.5% figures live on in `../specs/crypto-commission.md` as the historical record of
> what that card shipped.

> Status: **shipped.** Authoritative spec: `../specs/crypto-commission.md`
> (merged via `feature/crypto-commission` on 2026-07-17); rate amended by
> `../specs/commission-rate-1-percent.md` (merged via `feature/commission-rate-1-percent` on 2026-08-04).

## What it does

SpaceStroker retains a small **house commission** on
winnings instead of paying out the whole pot. The cut is taken only from each winner's **profit above
their own stake** (a winner always gets their stake back), is **1%** today, and is realized by
crediting winners less on-chain — the retained amount simply stays in the house token account.
The rate is shown to
players as a **house-cut badge** in the `/game` STATS side panel. This re-adds a commission that was
removed in the operator-wallet-api refactor (see `operator-wallet-api.md`).

## Core rules

- **Scope: every game type.** The commission is applied in `BetDomainService.CalculateRewardForBet`
  unconditionally (there is one settlement token, so there is nothing to branch on).
  *(Historically it was gated on `PaymentMethod == SolanaToken`; Standard games applied none — full pot to
  winners, 0% cut.)*
- **What is taxed = player-funded profit above the winner's own stake.** Per winner:
  `commission = rate × max(0, winnerReward − ownStake − houseFundedShare)`, then
  `netReward = winnerReward − commission`.
  - A winner **always keeps their full stake** — the cut comes only from profit above it.
  - The **house-funded lottery money is never taxed**: `houseFundedShare = (LotteryParameters.LotteryPot
    + Game.CarriedExtra) / payableWinnerCount` is subtracted before applying the rate, so only
    player-stake-funded winnings are commissioned. For races this term is 0.
  - Because every bet in a game shares one fixed `RequiredAmount`, the total retained equals
    **rate × (losing-bet stakes)**. For a 3-token / 2-winner round at 1% each winner nets **1.495**
    (retained 0.01 = 1% of the one losing stake).
  - **So the yield is always BELOW the nominal rate as a share of the pot** — the winner's own stake is
    never taxed. At 1%: a 6-bet/1-winner race retains 0.83% of the pot, a heads-up race 0.50%, a
    3-bet/2-winner round 0.33%. Anyone quoting "1% of the pot" is overstating it.
- **Configurable rate, currently 1%.** `GeneralSettings.Operator.Token.CommissionPercentage` is a
  **percent** (`1.0` = 1%); the domain converts it to a fraction (`/100`). `0` (or absent) disables the
  commission. It was `0.5` from 2026-07-17 until 2026-08-04. The committed
  `spacestrokersettings.json` value **is** the production rate — no deploy surface overrides it
  (neither `secrets.<env>.json`, the workload tfvars, nor the secret lists in
  `deploy/terraform/workload/main.tf`) — so changing it needs a redeploy of **both** Cloud Run hosts:
  Consumers computes the payout, the API serves the badge. `SpaceStroker.Tests/UnitTests/Api/CommissionRateTests.cs`
  pins the committed value so it cannot silently revert.
- **The rate is read at payout time, never snapshotted per round.** A round already open when
  Consumers restarts with a new rate settles at the **new** rate, even for bets placed while the badge
  showed the old one. Deliberate (a snapshot would mean a migration); the mitigation is to deploy a
  rate change with no paid round open — the exposure is the 24h **Space Jackpot** lottery.
- **Retained funds stay put.** The cut is realized by a smaller on-chain credit — **no new transfer,
  no dedicated ledger / `CryptoTransactionLog` row, no migration.** "Total paid out" stats drop
  accordingly.
- **Rounding.** The net reward still rounds **down** to the token's decimals (`MidpointRounding.ToZero`),
  applied after the commission, so the sum of payouts never exceeds the pot held on-chain.
- **Free bets** (amount 0) contribute nothing to the pot and receive no payout → no commission.
- **Refunds are unaffected** — a refund always returns the full stake (commission applies to rewards
  only).
- **UI (badges only) — every surface renders the backend number, none hardcodes it.**
  `GameStatsPanel.vue` (the `/game` STATS tab) and `GameStatsPage.vue` (the global `/game/stats` page)
  both interpolate the backend-provided rate — `ActiveGame.commissionPercentage` /
  `GlobalStats.commissionPercentage` — so today they read `"1% HOUSE CUT"` with a "winners keep the pot
  minus a 1% house cut" blurb, and a `0` rate renders `"0% HOUSE CUT"`. A rate change therefore needs
  **no frontend work at all**. The how-to-play guide (`GuideWhatYouWin.vue`) deliberately names **no
  number** for exactly this reason. No terminal line, no lobby-card change.
- **The LP Terms page is the one place a number is written by hand** — `monkify-lp/src/pages/terms.astro`
  states the rate as *"currently 1%"*, leads with the rule ("only from profit above your own stake…
  never nets less than they staked") and points at the in-app STATS panel as the live figure. **It must
  be updated with any rate change** — it is a legal document, and it was found stating 0.5% for exactly
  this reason.

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

- Reward math: `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` — ctor captures `_isSolanaToken`,
  `_houseFundedBase`, `_commissionRate`, `_payableWinnerCount` (+ `PayableWinnerCount`); `CommissionForWinner`
  applies the cut in `CalculateRewardForBet`; `Winners` is materialized.
- Config: `SpaceStroker.Domain/Configs/Entities/TokenSettings.cs` (`CommissionPercentage`);
  `SpaceStroker.Common/spacestrokersettings.json` → `GeneralSettings.Operator.Token.CommissionPercentage`
  (**`1.0`** — this committed value is the production rate, not a mock).
- DTO: `SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs` (`CommissionPercentage`, straight
  from `TokenSettings`); populated in
  `SpaceStroker.Infrastructure/Handlers/Games/GetActiveParameters/GetActiveParametersHandler.cs`
  (injects `GeneralSettings`). Also on `GlobalStatsDto` (`GlobalStatsDto.From`) for the `/game/stats` badge.
- Settlement (unchanged wiring): `SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs`
  credits `rewardResult.Value` via the payment strategy.
- Tests:
  - `SpaceStroker.Tests/UnitTests/Api/CommissionRateTests.cs` — **pins the committed rate to 1%** and
    that it is a percent, not a fraction. This is the guard against a silent revert; it lives in the
    **fast** suite deliberately, so it runs on every backend change rather than only behind Docker.
  - `SpaceStroker.Tests/UnitTests/Domain/Games/BetDomainServiceTests.cs` — commission math + the
    sequential-payout regression. ⚠️ Its fixtures deliberately use their **own** `0.5m` rate with
    hardcoded expectations (`1.4975`, `19.95`, `18.98`): they test the **formula** independently of the
    configured rate, so **do not "fix" them to 1%** — that would be churn with no new coverage.
  - `SpaceStroker.Tests/UnitTests/Handlers/GetActiveParametersHandlerTests.cs`,
    `GetGlobalStatsHandlerTests.cs`, `UnitTests/Contracts/GlobalStatsDtoTests.cs` — pass-through of the
    configured rate to the DTOs, each against its own injected settings.
  - `SpaceStroker.Tests.Integration/GameFlowTests.cs` — `PaidCryptoRace_AppliesCommission_CreditsPotMinusCut`
    derives its expected payout from the **running** config (`pot − rate × losing stake`), so it validates
    whatever rate ships without ever being edited.

**Frontend (`monkify-front`)**

- `src/types/index.ts` (`ActiveGame.commissionPercentage`).
- `src/components/game/GameStatsPanel.vue` (method-aware house-cut badge label + blurb).
