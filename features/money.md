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

> ⚠️ **Rate raised 0.5% → 1% on 2026-08-04, then 1% → 2% on 2026-08-21** (Trello #175, which raised
> it to fund the [referral program](referral-program.md)). Only the number changed each time — no code,
> entity or migration change. Every figure below is the **current 2%** rate; the 0.5% figures live on in
> [`../specs/crypto-commission.md`](../specs/crypto-commission.md) as the record of what that card shipped.

> **Not on house-edge types.** Hyper Roll takes **no** commission — a winner is paid exactly
> `stake × multiplier` from the treasury and the margin is the 91.84% RTP built into the board
> (`CommissionPercentage` maps to 0). See [`hyper-roll.md`](hyper-roll.md).

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
  stake is never taxed. At 2%: a 6-bet/1-winner race retains **1.67%** of the pot, a heads-up race
  **1.00%**, a 3-bet/2-winner round **0.67%**. *Anyone quoting "2% of the pot" is overstating it.*
  ⚠️ This is also the ceiling on a referral partner’s cut — see [`referral-program.md`](referral-program.md).
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
  `GlobalStats.commissionPercentage`, so they read `"5% HOUSE CUT"` today and `"0% HOUSE CUT"` at a zero
  rate. **A rate change is zero frontend work.**
- ⚠️ **There are FIVE hand-written rate figures, and a rate change must move ALL of them.** (#175 found
  five, not the four recorded here before — `GuideToken.vue` names it **twice**, in a row title and in
  the row body.) The badges above
  are free; these are not. None of them is generated from config:

  | Where | File | Note |
  | --- | --- | --- |
  | Terms page (LP) | `monkify-lp/src/pages/terms.astro` | A **legal document**. It was once found stating 0.5% after the rate moved to 1% |
  | The manual, payout section | `monkify-front/.../GuideWhatYouWin.vue` | Since **2026-08-12** — it used to name no number |
  | The manual, token section — **row title** | `monkify-front/.../GuideToken.vue` | `THE 5% HOUSE CUT BUYS THE COIN` |
  | The manual, token section — **row body** | `monkify-front/.../GuideToken.vue` | "The game takes 5% of what you win above your own stake" |
  | The LP token section + home card | `monkify-lp/.../TokenSection.vue`, `monkify-front/.../HomeTokenCard.vue` | Same mechanic sentence |

  ⚠️ **Do NOT blanket find-and-replace the rate.** `monkify-lp/src/constants.ts` (`"1% per $100M of
  market cap"`) and `GuideToken.vue`’s release-cadence prose are the **team vesting schedule**, not the
  house cut, and must stay at 1%.

  The guide's two both keep a hedge — the number is named **and** the reader is pointed at the STATS
  panel for their mode's live rate. ⚠️ **The token copy does not and cannot**: a buyback funded by "a
  small cut" is not a claim anyone can check. That is the cost of naming the mechanic. See
  [`token-launch.md`](token-launch.md).
- **No lobby-card change.** ⚠️ **There IS a terminal line as of 2026-08-24** — see §3 below. It states
  the **net a winner was paid**, not the rate, so it needs no update when the rate moves.

## 3. What the player is told they were paid

Shipped with [`game-mode-ux.md`](game-mode-ux.md) (2026-08-24).

- The reward-completed status event carries **`netRewardPerWinner`** — the NET a single **paid** winner
  received after the cut, i.e. what actually moved. The terminal prints it as **`> PAID:`** under
  `> PAID TO:`, and the win toasts show the same figure.
- It is **captured from the payout loop**, never re-derived on the client. Re-deriving it in TypeScript
  from pot/winners/rate was explicitly rejected: it would duplicate `BetDomainService` (free-bet
  `PayableWinnerCount`, lottery `houseFundedShare`, 9-dp truncation) and drift silently the next time
  payout math changes.
- **Uniform across paid winners by construction**, which is the only reason one figure can go to the
  whole game group without leaking anything. Pinned by
  `BetDomainServiceTests.EveryPaidWinnerInARound_ReceivesTheIdenticalNetAmount`.
- **Null whenever nothing was paid** — free-bet-only round, nobody won, pre-cutover, or
  `ErrorWhenProcessingRewards`. The line is then **absent entirely**; it never prints `0`.
- ⚠️ An errored round publishes null **even when one winner was really paid**. Deliberate: the money is
  in the ledger, but the round does not make a payout claim it cannot make for every winner.

### Display precision — three, on purpose

| Surface | Places | Formatter |
| --- | --- | --- |
| Pot, lobby card price, Orbit panel, everything else | **2** (`DP`) | `formatSol` |
| Header balance pills | **max 3** | `formatCompact` |
| `> PAID:` and both win toasts | **max 4** | `formatPayout` |

All three **truncate, never round** (a displayed figure is therefore always ≤ the real one), and each
keeps a significant-digit escape. ⚠️ **The escape floor must match the places**: `formatSol` escapes
only below `0.01`, which is exactly why a `0.0296` payout used to render `0.02`. **Global `DP` stays 2**
— raising it app-wide is what once put `0.5656` in the header pill.

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
| `SpaceStroker.Common/spacestrokersettings.json` | `GeneralSettings.Operator.Token.CommissionPercentage` = **`5.0`** — the production rate, not a mock |
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
**formula** independently of the configured rate — **do not "fix" them to the configured rate**), `PendingRefundSweepJobTests`,
`RefundBetHandlerTests`, `RefundInvalidBetConsumerTests`; integration `GameFlowTests.PaidCryptoRace_AppliesCommission_CreditsPotMinusCut`
(derives the expectation from the **running** config, so it validates whatever rate ships) and
`RefundSafeguardsTests`.
