# Space Stroker — "House edge rework" — Implementation Spec

> **Level:** `Feature` (money-affecting behaviour change across referral earnings, the house cut and
> house seeding). **Repos:** `monkify-backend`, `monkify-front`, `monkify-lp`, `monkify-meta`.
> **Branch / worktree:** `feature/house-edge-rework` in all four.
> **Trello:** none — raised directly by the owner on 2026-09-17 after a partner reported being paid
> "50% of what ppl wager, not 50% of house edge".

---

## 1. Summary

Three changes, shipped together because the first two share one number:

1. **Referral earnings move from turnover to the house edge.** A partner earns `SharePercentage` of
   the commission their referred players produced — on the stakes they **lost** and on the profit
   they **won** — not `SharePercentage` of everything those players wagered.
2. **House cut 5% → 2%** (`GeneralSettings:Operator:Token:CommissionPercentage`).
3. **The house seed bet yields to Orbit.** When **2 or more** Orbit players are betting in a round, the
   house seed wallet does not place its bet.

### 1.1 Before / after

| | Before | After |
|---|---|---|
| Partner earnings | `floor(Σ paid stakes × Share/100)` | `floor(commission base × Commission/100 × Share/100)`, base = lost stakes + won profit above own stake |
| A 50% partner, heads-up, 1 SOL each, referred player loses OR wins | 1.000 SOL (per 2 SOL of referred turnover, 20× the edge) | 0.010 SOL (half of the 0.02 SOL commission) |
| Same, but BOTH players are referred to that partner | 1.000 SOL | 0.020 SOL — the one 0.02 commission is credited from both sides (§3.3) |
| House cut | 5% of winnings above own stake | 2% of winnings above own stake |
| House seed | one bet in every production round | skipped when ≥ 2 Orbit players bet in the round |

---

## 2. Constraints (MANDATORY)

- `constraints.md` §2 worktree-first; §3 unit tests via `scripts/run_tests.bat`; §3 integration tests.
- The referral payable invariant (`specs/referral-cash-out.md`) is untouched: the cash-out ledger is
  still summed with **no status filter**, and a negative payable is still **carried, never clamped**.
- The house seed's production gate stays in `HouseSeedBetConsumer`; every decision stays in
  `HouseSeedBetService` so the Testcontainers suite can drive it.

---

## 3. Resolved product decisions (owner, 2026-09-17 — answered "A" to all three)

| # | Question | Decision | Why |
|---|---|---|---|
| 1 | What "house edge" means for a partner | **A — the commission their losses generated. ⚠️ Revised in §3.3: wins count too.** `Share × Commission × Σ losing paid stakes`, only in rounds where at least one PAID winner was paid. | The only basis where "50% of house edge" is literally true and a share ≤ 100 can never pay more than the traffic earned. B (rate × turnover) still overpays ~2× heads-up. |
| 2 | Past earnings when the rate moves 5% → 2% | **A — re-price history at the current rate.** No per-round snapshot, no migration. | Production's only real partner is the 50% one whose history is the mistake being corrected. Consequence accepted: every future rate change re-prices partner history too, the same way a `SharePercentage` edit already does. |
| 3 | What "2 or more people registered on Orbit" means | **A — Orbit players actually betting in THIS round.** Union of (players whose `FundedByOrbit` bet has landed) and (`IOrbitBetPlacementService.ResolveCandidates(parameters, gameId)`), checked after the placement delay. | Skips only when the round will start without the house, using the planner's own eligibility rule so the two cannot disagree. B (any enabled enrolment) counts empty balances; C (global) would stop seeding types Orbit does not play. |

### 3.1 Why the Orbit count is a union (a code finding)

`ResolveCandidates(…, gameId)` **excludes** players who already bet in the round. The seed checks
after its randomised delay, by which point `OrbitPlacementPlannerConsumer` (same `GameOpenedEvent`)
has usually dispatched and the Orbit bets have landed — so a candidates-only count returns **zero**
for a round Orbit has fully filled, and the house would seed anyway.

### 3.2 Reversal of house-seed decision 11 (scoped)

`specs/house-seed-bet.md` decision 11 — "the seeded bet is placed whoever else turned up" — still
holds for **humans betting in person**. Orbit players are the single exception, pinned by
`StillSeeds_WhenTwoHumansWhoAreNotOnOrbitHaveBet`.

### 3.3 Revision of decision 1: wins count too (owner, 2026-09-17, after the first push)

The shipped-to-branch version of decision 1 counted **losses only**, on the grounds that the
commission on a win is levied on other players' losses and counting both would credit it twice. The
owner overruled that: **"the partner earnings must also count player wins."**

- A referred **win** contributes its taxed profit: `Σ round stakes / paid winners − own stake`,
  floored at 0 — exactly the base `BetDomainService.CommissionForWinner` taxes (house-funded lottery
  money excluded, as it is never commissioned). A win with nothing above its stake contributes 0.
- ⚠️ **Accepted consequence:** when two referred players meet, the round's one commission is credited
  from the loss AND the win — twice to one partner, or once to each of two partners. Above a 50%
  share, a partner whose traffic mostly plays itself can be paid more than the house took on those
  rounds. Documented on `ReferralSettings.SharePercentage`, not enforced in code.

---

## 4. Data model

**No change. No migration.** Earnings remain a derived aggregate over `GameBets` / `Games` /
`Players` / `ReferralPartners` / `ReferralCashOuts`. `IX_GameBets_ReferralCode` still leads the query;
the paid-winner check is an `EXISTS` on the same round.

---

## 5. Configuration

- `spacestrokersettings.json` → `GeneralSettings:Operator:Token:CommissionPercentage`: `5.0` → `2.0`.
  Overridden by no deploy surface, so the committed file **is** the production rate (pinned by
  `CommissionRateTests`).
- `ReferralPartners.SharePercentage` keeps its column and precision; its **meaning** changes from
  "% of turnover" to "% of house edge". The production partner row set at `50` now means what the
  partner was told. **No data change is required for it.**

---

## 6. Backend behaviour

### 6.1 `ReferralEarningsService`

- `ResolveTurnover` → **`ResolveCommissionBase`**: same exclusions as before (free bets, non-
  `RewardForWinnersCompleted` rounds, pre-`SettlementCutoverUtc`, the partner's own payout wallet,
  exact code match) plus:
  - `Status == Made || Status == Rewarded` — lost or won. This replaces the three
    `!= NeedsRefunding/Refunded/NeedsManualAnalysis` clauses.
  - `Game.Bets.Any(Status == Rewarded && Amount > 0)` — commission was actually charged in that round.
  - Per bet: a loss contributes `Amount`; a win contributes
    `max(0, Σ Game.Bets.Amount / count(paid Rewarded) − Amount)` (§3.3).
- `ApplyShare` multiplies by `CommissionPercentage/100` then `SharePercentage/100`, and floors
  (`MidpointRounding.ToZero`) **once**, on the final product.

### 6.2 `HouseSeedBetService`

- Injects `IOrbitBetPlacementService`.
- After the post-delay re-read, `CountOrbitPlayers(game) >= 2` ⇒ log at Information and return
  before any balance read, server-seed commit, choice derivation or transfer.
- Lottery types and an unconfigured Orbit account resolve zero candidates, so they always seed.

### 6.3 Behaviour that must NOT change

- `BetDomainService` commission arithmetic (only the configured rate moves).
- Cash-out reservation, solvency guard, cooldown, the no-status-filter ledger sum.
- House seed idempotency, min-players refusal, SOL floor, choice collision handling.

---

## 7. Frontend (`monkify-front`)

- `PartnerPage.vue`: `N% OF REFERRED TURNOVER` → `N% OF THE HOUSE EDGE`.
- The hand-written 5% copies → 2%: `GuideToken.vue` (title + body + rule comment),
  `GuideWhatYouWin.vue` (body + rule comment), `HomeTokenCard.vue`.
- The STATS badges already read `commissionPercentage` live — no change.

## 7b. Landing page (`monkify-lp`)

- `TokenSection.vue`: "The profit from the 2% house cut…".
- `terms.astro`: "currently 2%", `LAST_UPDATED` → 17 September 2026. The REFERRALS clause names no
  percentage or basis, so it is unchanged.

## 7c. Meta

- `deploy/sql/referral-payout-report.sql` mirrors the new predicate and formula, reads partners from
  the `ReferralPartners` table (the config note was stale since #177), and takes the commission rate
  as a parameter.

---

## 8. Deploy sequencing

- The commission rate is read **at payout time**, so deploy with **no paid round open** and release
  **API and Consumers together** (same rule as #175 / #177).
- **Deploy the LP with the backend**, or `/terms` publishes a 5% cut while 2% is taken.
- Partner balances re-price the moment Consumers/API run the new code (earnings cache ≤ 60 s). A
  partner already paid more than their new all-time earnings will read **Available 0** and carry the
  deficit — expected, not a bug.

---

## 9. Out of scope

- Snapshotting the commission rate per round (decision 2 → B).
- Crediting no-winner rounds, where the house keeps the whole pot but no commission is levied.
- Clawing back anything already paid out under the turnover basis.
- A seeding config flag; the Orbit threshold is a code constant like the production gate.

---

## 10. File checklist

### 10.1 Backend
- `Services/Referrals/ReferralEarningsService.cs`, `IReferralEarningsService.cs`
- `Services/HouseSeed/HouseSeedBetService.cs`
- `SpaceStroker.Common/spacestrokersettings.json`
- Doc comments: `ReferralSettings.cs`, `ReferralPartner.cs`, `CacheHelper.cs`, `ReferralCashOutReserver.cs`
- Tests: `ReferralEarningsServiceTests.cs`, `CommissionRateTests.cs`, `HouseSeedBetServiceTests.cs`,
  `HouseSeedBetFlowTests.cs`, `ReferralCashOutFlowTests.cs`, `Harness/IntegrationTestFixture.cs`

### 10.2 Frontend
- `pages/PartnerPage.vue`, `components/guide/sections/GuideToken.vue`,
  `components/guide/sections/GuideWhatYouWin.vue`, `components/home/HomeTokenCard.vue`

### 10.3 Landing page
- `components/TokenSection.vue`, `pages/terms.astro`

### 10.4 Meta
- `specs/house-edge-rework.md` (this file), `deploy/sql/referral-payout-report.sql`

---

## 11. Tests

### 11.1 Unit
- `ReferralEarningsServiceTests` rewritten around a `Round` (referred bet + optional paid winner +
  optional unreferred loser in the same game): losing stake counts; a win counts its profit above its
  own stake, split across paid winners, not divided by a free-bet winner, zero when nobody lost; a
  referred loss and a referred win in one round both count; no-winner round and free-bet-only winner
  excluded; all
  previous exclusions kept; `Earnings_AreTheShareOfTheHouseEdge_NotOfTheStakes` (the regression);
  current-rate re-pricing; zero commission ⇒ zero earnings.
- `CommissionRateTests.CommissionPercentage_IsTwoPercent`.
- `HouseSeedBetServiceTests`: skip on 2 candidates, on 2 landed Orbit bets with no candidates, on 1
  landed + 1 candidate; seed on 1 Orbit player, on the same player landed and candidate, on 2 humans.

### 11.2 Integration (rule 8)
- `HouseSeedBetFlowTests.SeedRound_SkipsTheRound_WhenTwoOrbitPlayersWillBetInIt` — real Postgres,
  real planner eligibility query. The class now disables Orbit enrolments on dispose (churn rule).
- `ReferralCashOutFlowTests` happy path proves the new predicate translates on Postgres: the suite
  partner is now 50% (at 1% a heads-up round of 2 would earn 0.0008, under the 0.001 minimum), and
  `EarnAsync` returns `4 × Commission × Share` — the lost stake of 2 plus the winner's profit of 2,
  both referred.

---

## 12. Acceptance criteria

1. A partner's `EarnedAllTime` equals `floor(commission base × CommissionPercentage/100 ×
   SharePercentage/100)`, the base being referred lost stakes plus referred won profit in paid-winner
   rounds.
2. A referred player's winning bet adds the commission on its profit above its own stake — never on
   the stake itself.
3. The committed commission rate is 2%, and every hand-written rate in the app and LP says 2%.
4. The partner page labels the share as a share of the house edge.
5. In production, a round with ≥ 2 Orbit players betting receives no house seed bet; a round with
   0–1 Orbit players is seeded exactly as before, whatever humans have bet.

## 13. Manual QA

- `/partner` for the production partner after deploy: share reads `50% OF THE HOUSE EDGE`; Available
  drops to the house-edge figure (or 0 with a carried deficit).
- Run section 3 of `referral-payout-report.sql` with `commission_percentage = 2.0` and compare with
  the page.
- Enrol two funded wallets on one Orbit type in production and confirm the next round has no bet
  from the house seed wallet; pause one and confirm the following round is seeded.
