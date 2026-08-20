# Domain — Money (commission, payouts and refunds)

> **What this covers.** What the house keeps out of a winning pot, how a payout is divided among
> winners, and how money that got stuck comes back to the player. Everything here settles in the one
> configured asset on-chain — there is no off-chain money path any more.
>
> ⚠️ **That asset is NATIVE SOL since 2026-08-20** (Trello #174) — not an SPL token. The math below is
> unchanged, but it now runs at **9 decimals** over lamports, and money that predates the switch is
> refused by the payout and refund paths rather than settled. See
> [`sol-settlement.md`](sol-settlement.md).
>
> ⚠️ **The project's pump.fun coin is NOT this token and never appears in this document.** It is
> marketing information on a page, gated by its own env key, and it touches no stake, payout, refund or
> ledger row. See [`token-launch.md`](token-launch.md) — conflating the two mints is the mistake that
> doc exists to prevent.
>
> Deep design: [`../specs/crypto-commission.md`](../specs/crypto-commission.md) (the cut),
> [`../specs/commission-rate-1-percent.md`](../specs/commission-rate-1-percent.md) (the current rate),
> [`../specs/refund-safeguards.md`](../specs/refund-safeguards.md) (the refund lifecycle) and
> [`../specs/payment-methods-refactor.md`](../specs/payment-methods-refactor.md) (registered tokens —
> its `Standard` half is dead).

---

## 1. House commission

> ⚠️ **Scope widened by [`remove-operators.md`](remove-operators.md) (2026-07-30).** The commission was
> `SolanaToken`-only because `Standard` (off-chain USD) games existed and took no cut. `Standard` is
> gone, so the commission applies to **every** game type and the `PaymentMethod` branch that gated it is
> deleted. The rate key moved to **`GeneralSettings.Operator.Token.CommissionPercentage`**.

> ⚠️ **Rate raised 0.5% → 1% on 2026-08-04.** Only the number changed — no code, entity or migration
> change. Every figure below is the **current 1%** rate; the 0.5% figures live on in
> [`../specs/crypto-commission.md`](../specs/crypto-commission.md) as the record of what that card shipped.

Space Stroker retains a small **house commission** on winnings instead of paying out the whole pot. The
cut comes only from each winner's **profit above their own stake**, and is realized by crediting winners
less on-chain — the retained amount simply stays in the house token account.

### The math

Per winner:

```
commission = rate × max(0, winnerReward − ownStake − houseFundedShare)
netReward  = winnerReward − commission
```

- **A winner always keeps their full stake.** The cut only touches profit above it.
- **House-funded lottery money is never taxed.**
  `houseFundedShare = (LotteryParameters.LotteryPot + Game.CarriedExtra) / payableWinnerCount` is
  subtracted before the rate applies. For races this term is 0.
- Because every bet in a game shares one fixed `RequiredAmount`, the total retained equals
  **rate × (losing-bet stakes)**.
- ⚠️ **The yield as a share of the pot is therefore always BELOW the nominal rate** — the winner's own
  stake is never taxed. At 1%: a 6-bet/1-winner race retains **0.83%** of the pot, a heads-up race
  **0.50%**, a 3-bet/2-winner round **0.33%**. *Anyone quoting "1% of the pot" is overstating it.*
- **Rounding** is down to the token's decimals (`MidpointRounding.ToZero`), applied **after** the
  commission, so the sum of payouts never exceeds the pot held on-chain.
- **Free bets** (amount 0) contribute nothing and receive nothing → no commission.
- **Refunds are unaffected** — a refund always returns the full stake.
- ⚠️ **A bet created before `SettlementCutoverUtc` is never paid or refunded.** Its `Amount` is a bare
  number denominated in the previous settlement asset, so moving it now would pay the same figure in
  SOL. Both consumers mark it `NeedsManualAnalysis` and log at Error. See
  [`sol-settlement.md`](sol-settlement.md).

### Configuring the rate

- `GeneralSettings.Operator.Token.CommissionPercentage` is a **percent** (`1.0` = 1%); the domain
  converts to a fraction (`/100`). `0` or absent disables it.
- ⚠️ **The committed `spacestrokersettings.json` value IS the production rate.** No deploy surface
  overrides it — not `secrets.<env>.json`, not the workload tfvars, not the secret lists in
  `deploy/terraform/workload/main.tf`. Changing it needs a redeploy of **both** Cloud Run hosts:
  Consumers computes the payout, the API serves the badge.
  `SpaceStroker.Tests/UnitTests/Api/CommissionRateTests.cs` pins the committed value so it cannot
  silently revert — it lives in the **fast** suite deliberately, so it runs on every backend change.
- ⚠️ **The rate is read at payout time, never snapshotted per round.** A round already open when
  Consumers restarts with a new rate settles at the **new** rate, even for bets placed while the badge
  showed the old one. Deliberate (a snapshot would need a migration); the mitigation is to deploy a rate
  change with no paid round open — the exposure is the 24h **Space Jackpot** lottery.
- **Retained funds stay put** — a smaller on-chain credit, **no new transfer, no ledger row, no
  migration**. "Total paid out" stats drop accordingly.

### Where the rate is shown

- **Badges only, all rendered live from the backend.** `GameStatsPanel.vue` (the `/game` STATS tab) and
  `GameStatsPage.vue` (`/game/stats`) interpolate `ActiveGame.commissionPercentage` /
  `GlobalStats.commissionPercentage`, so they read `"1% HOUSE CUT"` today and `"0% HOUSE CUT"` at a zero
  rate. **A rate change is zero frontend work.**
- ⚠️ **There are now FOUR hand-written "1%"s, and a rate change must move ALL of them.** The badges above
  are free; these are not. None of them is generated from config:

  | Where | File | Note |
  | --- | --- | --- |
  | Terms page (LP) | `monkify-lp/src/pages/terms.astro` | A **legal document**. It was once found stating 0.5% after the rate moved to 1% |
  | The manual, payout section | `monkify-front/.../GuideWhatYouWin.vue` | Since **2026-08-12** — it used to name no number |
  | The manual, token section | `monkify-front/.../GuideToken.vue` | The buyback mechanic is funded "from the 1% house cut" |
  | The LP token section + home card | `monkify-lp/.../TokenSection.vue`, `monkify-front/.../HomeTokenCard.vue` | Same mechanic sentence |

  The guide's two both keep a hedge — the number is named **and** the reader is pointed at the STATS
  panel for their mode's live rate. ⚠️ **The token copy does not and cannot**: a buyback funded by "a
  small cut" is not a claim anyone can check. That is the cost of naming the mechanic. See
  [`token-launch.md`](token-launch.md).
- **No terminal line, no lobby-card change.**

### The multi-winner division fix (shipped alongside)

The commission surfaced a latent payout bug, fixed in the same branch:

- `BetDomainService.Winners` was a **deferred** query, and `RewardGameConsumer` flips each winner to
  `Rewarded` as it pays them — so `Winners.Count()` **shrank mid-payout** and later winners divided the
  pot by a smaller count. A 3-token / 2-winner round paid the second winner the **whole pot**.
- Fix: the winner set is **materialized once** (`ToList()`), and the pot is divided among **paid winners
  only** (`PayableWinnerCount = Winners.Count(w => !IsFreeBet(w))`, frozen, guarded ≥ 1). That also makes
  the free+paid rule explicit — a free winner takes no share. Both the reward divisor and the
  commission's `houseFundedShare` use `PayableWinnerCount`.

---

## 2. Refund safeguards

`BetStatus.NeedsRefunding` / `Refunded` is a **real, self-clearing lifecycle**. Before this, both were
defined but never set: refunds were fire-and-forget (a failed refund only logged an error and the money
was never returned) and there was no way to force one.

- **Self-clearing.** `RefundInvalidBetConsumer` sets `Bet.Status = Refunded` on any **successful**
  `Refund()` — including the three pre-existing inline triggers (under-subscribed lottery, game-type
  deactivation, invalid paid bet), which previously left the bet `Made`. On **failure** the status is
  left untouched, so a swept bet retries next sweep. The flip uses a minimal tracked re-query
  (`MarkRefunded`) ⚠️ **because `SpaceStrokerDbContext` is NoTracking by default** — a plain
  load-mutate-SaveChanges would persist nothing.
- **12-hour sweep.** `PendingRefundSweepJob` (Hangfire, cron `0 */12 * * *`, id `pending-refund-sweep`,
  in `SpaceStroker.Consumers`) loads **all** `NeedsRefunding` bets in **any** game state and publishes one
  `RefundInvalidBetEvent` each — **except** pre-cutover bets, which it **parks** as
  `NeedsManualAnalysis` instead. ⚠️ Parking rather than skipping is load-bearing: `NeedsRefunding` is
  the status this sweep selects on, so a merely-skipped bet would be re-swept every 12 hours forever. The sweep changes no status — it only publishes; the consumer moves the
  money and flips the status. Failed refunds retry every 12h indefinitely.
- **Admin refund by payment signature.** `POST /admin/bets/refund` (under `X-Admin-Token`) takes
  `{ paymentSignature }` and marks the bet `NeedsRefunding`. It is the **only** producer of that status,
  and it does **not** refund immediately — money moves on the next sweep, so a manual refund may sit up
  to ~12h.
- **Lookup** is by `Bet.Signature` (renamed from `ExternalTransactionReference` when operators were
  removed). An unknown signature returns a vague *"No bet found for that signature."* No tenant scoping —
  there is one deployment.
- ⚠️ **Money-safety guards.** The admin action rejects a bet already `Rewarded` or `Refunded` — refunding
  a `Rewarded` bet would **double-pay**, because `CalculateRemainingBetPayout` nets out prior *refund*
  movements only, not reward credits. It also rejects a **free bet** (`Amount == 0`), since the consumer
  skips amount-0 without clearing the status. There is **no game-state restriction**.
- **Idempotent.** Re-marking an already-`NeedsRefunding` bet is a success no-op. Ledger-based idempotency
  still applies: a duplicated `RefundInvalidBetEvent` returns `BetHasAlreadyBeenRefunded`, so no money
  moves twice.
- **Refund amount.** A `NeedsRewarding` winner whose payout failed and is then marked for refund gets its
  **stake** back, not winnings.
- **Host split.** The sweep and all money movement run in `SpaceStroker.Consumers`. The admin endpoint
  only reads and flips a status column — no money, no Solana call, no key decrypt — so it stays in the
  stateless `SpaceStroker.Api`.
- **No schema change.** Both statuses already existed and `Bet.Signature` was already indexed.

---

## Key code touchpoints

**Backend (`monkify-backend`)**

| File | Role |
| --- | --- |
| `SpaceStroker.Domain/Bets/Services/BetDomainService.cs` | `CommissionForWinner` in `CalculateRewardForBet`; materialized `Winners`; `PayableWinnerCount`; `CalculateRefundForInvalidBet` / `CalculateRemainingBetPayout` |
| `SpaceStroker.Domain/Configs/Entities/TokenSettings.cs` | `CommissionPercentage` |
| `SpaceStroker.Common/spacestrokersettings.json` | `GeneralSettings.Operator.Token.CommissionPercentage` = **`1.0`** — the production rate, not a mock |
| `SpaceStroker.Infrastructure/Contracts/Games/GameParametersDto.cs` · `GlobalStatsDto` | Rate pass-through for the badges |
| `SpaceStroker.Infrastructure/Consumers/RewardGame/RewardGameConsumer.cs` | Credits `rewardResult.Value` via the payment strategy |
| `SpaceStroker.Infrastructure/Jobs/PendingRefundSweepJob.cs` | The 12h sweep |
| `SpaceStroker.Infrastructure/Consumers/RefundInvalidBet/RefundInvalidBetConsumer.cs` | `Refunded` on success via `MarkRefunded` |
| `SpaceStroker.Infrastructure/Handlers/Admin/RefundBet/` | Lookup, guards, set `NeedsRefunding` |
| `SpaceStroker.Api/Controllers/AdminController.cs` | `POST /admin/bets/refund` |

**Frontend (`monkify-front`)** — `src/types/index.ts` (`ActiveGame.commissionPercentage`),
`src/components/game/GameStatsPanel.vue` (badge), `src/components/admin/BetRefundManager.vue` +
`GameTypeManager.vue` `refunds` tab, `src/services/adminService.ts` (`refundBet`).

**Tests** — `CommissionRateTests` (pins the rate), `BetDomainServiceTests` (⚠️ its fixtures deliberately
use their **own** `0.5m` rate with hardcoded expectations `1.4975` / `19.95` / `18.98`: they test the
**formula** independently of the configured rate — **do not "fix" them to 1%**), `PendingRefundSweepJobTests`,
`RefundBetHandlerTests`, `RefundInvalidBetConsumerTests`; integration `GameFlowTests.PaidCryptoRace_AppliesCommission_CreditsPotMinusCut`
(derives the expectation from the **running** config, so it validates whatever rate ships) and
`RefundSafeguardsTests`.
